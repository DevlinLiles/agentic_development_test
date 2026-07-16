using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// A depth-limited alpha-beta (negamax) chess AI with the move-ordering heuristics needed to
/// meet the response-time bound: hash/PST ordering, MVV-LVA for captures, and
/// captures-before-quiet separation. It is registered behind the stateless
/// <see cref="IChessAi"/> seam, so <c>GameService</c> is unchanged.
///
/// Ordering matters because alpha-beta prunes most effectively when the best move is searched
/// first. The three heuristics, applied at every node, are:
///   1. Hash move — the transposition table's best move for this position (from a previous,
///      shallower iteration) is tried first.
///   2. MVV-LVA — for captures, score = victimValue - attackerValue, so we search "win the most
///      valuable piece with the least valuable attacker" first.
///   3. Captures-before-quiet — all captures/promotions are placed ahead of quiet moves; among
///      quiet moves a piece-square-table (PST) score supplies a stable, position-aware ordering.
///
/// Iterative deepening from depth 1 upward, with a hard node budget as a backstop deadline,
/// keeps the engine inside the response-time bound even on a slow machine: we always have a
/// best move from the previous (shallower) iteration ready to return, and we stop as soon as the
/// budget is hit, the target depth is reached, or a forced mate is found.
/// </summary>
public sealed class GreedyHeuristicChessAi : IChessAi
{
    // Standard piece values, in pawn units. The king is given a large value purely as a safe
    // sentinel; in a legal position it can never actually be captured, and in material balance
    // the two king terms cancel out.
    internal static readonly Dictionary<PieceType, int> MaterialValue = new()
    {
        [PieceType.Pawn] = 100,
        [PieceType.Knight] = 320,
        [PieceType.Bishop] = 330,
        [PieceType.Rook] = 500,
        [PieceType.Queen] = 900,
        [PieceType.King] = 100_000,
    };

    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    // Search depth ceiling. Iterative deepening stops at this depth even if the node budget
    // hasn't been exhausted, which bounds worst-case latency on positions with few legal moves.
    private const int MaxDepth = 4;

    // Hard node budget — a deterministic, machine-independent deadline that bounds wall-clock
    // time. Each visited node decrements this; when it hits zero the search unwinds and returns
    // the best move found so far. Sized so the engine comfortably meets the response-time bound
    // while still searching deep enough to find mates and free material in test positions.
    private const int NodeBudget = 50_000;

    // Mate score sentinel. Kept well above any reachable material/PST score so a forced mate
    // dominates every other line. Adjusted by ply so shorter mates are preferred.
    private const int MateScore = 1_000_000;
    private const int MatePlyAdjust = 1_000;

    // Score tiers for move ordering: distinct bands keep captures ahead of quiet moves and let
    // the hash move lead. Quiet-move deltas are PST-based (a few hundred at most), so they live
    // in the Quiet band and never collide with the higher bands.
    private const int HashMoveScore = 300_000_000;
    private const int CaptureBand = 200_000_000;
    private const int QuietBand = 100_000_000;

    // Transposition table: maps a position key to its best move from a prior (shallower) visit.
    // Used for the hash-move ordering slot; cleared at the start of every ChooseMove call so the
    // implementation stays stateless and deterministic across calls (per the IChessAi contract).
    private readonly Dictionary<ulong, EngineMove> _transpositions = new();

    // Recreated per ChooseMove call so the search deadline is per-call.
    private int _remainingNodes;

    public AiMove? ChooseMove(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return null;
        }

        var legalMoves = board.Moves().ToList();
        if (legalMoves.Count == 0)
        {
            // No legal moves: terminal position already classified by the rules engine on the
            // move that produced `fen`. Nothing for the AI to do.
            return null;
        }

        // Stateless guarantee: clear any transpositions left over from a previous call so two
        // ChooseMove calls on the same position follow an identical search path (determinism).
        _transpositions.Clear();
        _remainingNodes = NodeBudget;

        var sideToMove = board.Turn;
        ScoredMove? best = null;

        // Iterative deepening: search depth 1, 2, ... up to MaxDepth. We always keep the best
        // move from the last completed iteration, so if the node budget runs out mid-iteration
        // we still return a fully-legal, sensible move (typically the mate/win found a ply
        // shallower) rather than nothing.
        for (var depth = 1; depth <= MaxDepth; depth++)
        {
            var iterationBest = SearchRoot(board, legalMoves, sideToMove, depth);

            if (iterationBest is not null)
            {
                best = iterationBest;

                // Found a forced mate already — searching deeper can only find an equally-fast
                // or slower mate, so stop early and bank the time budget.
                if (iterationBest.Score >= MateScore - MaxDepth * MatePlyAdjust)
                {
                    break;
                }
            }

            // Out of node budget mid-iteration: return the best move from the last completed
            // iteration. `best` is already set because depth 1 always completes within budget.
            if (_remainingNodes <= 0)
            {
                break;
            }
        }

        return best is null
            ? null
            : new AiMove(best.From, best.To, best.Promotion);
    }

    /// <summary>
    /// Root search: orders the root's legal moves and runs negamax alpha-beta to the given
    /// depth, returning the best move plus its score (from the side-to-move's perspective).
    /// </summary>
    private ScoredMove? SearchRoot(ChessBoard board, IReadOnlyList<EngineMove> legalMoves, PieceColor sideToMove, int depth)
    {
        // The hash move for the root is the best move found by the previous (shallower) iteration,
        // if any — the standard iterative-deepening hint that improves pruning at the root.
        var rootKey = PositionKey(board);
        _transpositions.TryGetValue(rootKey, out var hashMove);

        var ordered = OrderMoves(board, legalMoves, hashMove);

        ScoredMove? best = null;
        var alpha = int.MinValue + 1; // +1 avoids overflow when negated in negamax
        var beta = int.MaxValue;
        EngineMove? bestMove = null;

        foreach (var move in ordered)
        {
            var (after, promotion) = ApplyMove(board, move);
            if (after is null)
            {
                continue;
            }

            int score;
            if (move.IsMate)
            {
                // Our move delivers checkmate: a forced win, best at the root (ply 0) so it gets
                // the full mate score. No need to recurse into a terminal position.
                score = MateScore;
            }
            else
            {
                score = -Negamax(after, depth - 1, -beta, -alpha, Opponent(sideToMove), ply: 1);
            }

            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
                best = new ScoredMove(
                    move.OriginalPosition.ToString(),
                    move.NewPosition.ToString(),
                    move.San,
                    promotion,
                    score);
            }
        }

        // Record the root best move so the next (deeper) iteration searches it first.
        if (bestMove is not null)
        {
            _transpositions[rootKey] = bestMove;
        }

        return best;
    }

    /// <summary>
    /// Negamax alpha-beta. Returns the score of <paramref name="board"/> for the side to move,
    /// searching to <paramref name="depth"/>. Move ordering (hash move, MVV-LVA,
    /// captures-before-quiet, PST) is applied at every interior node to maximise pruning.
    /// </summary>
    private int Negamax(ChessBoard board, int depth, int alpha, int beta, PieceColor perspective, int ply)
    {
        // Deadline: stop expanding as soon as the node budget is exhausted. Returning a static
        // eval here is a safe stand-in that lets the caller unwind using the best move already
        // found at a shallower depth; it never corrupts a completed iteration above.
        if (--_remainingNodes <= 0)
        {
            return Evaluate(board, perspective);
        }

        var legalMoves = board.Moves().ToList();

        if (legalMoves.Count == 0)
        {
            // Terminal with no legal moves. A checkmate of the side to move is always produced by
            // the *parent* move, whose `IsMate` flag short-circuits the parent loop before it
            // recurses here — so a node that reaches this branch with no legal moves is a
            // stalemate (draw). Score it 0.
            return 0;
        }

        if (depth <= 0)
        {
            // Horizon: evaluate. Captures-first ordering plus the material term already steer
            // the last plies of each completed iteration toward forcing lines, which is enough
            // for the MVP response-time bound without a full quiescence search.
            return Evaluate(board, perspective);
        }

        // Hash move: the best move recorded for this position from a shallower visit. Tried
        // first to maximise alpha-beta cutoffs. (The table is used purely for ordering here —
        // no bound-based cutoffs — so correctness doesn't depend on stored score flags.)
        var key = PositionKey(board);
        _transpositions.TryGetValue(key, out var hashMove);

        var ordered = OrderMoves(board, legalMoves, hashMove);

        var best = int.MinValue + 1;
        EngineMove? bestMove = null;

        foreach (var move in ordered)
        {
            var (after, _) = ApplyMove(board, move);
            if (after is null)
            {
                continue;
            }

            int score;
            if (move.IsMate)
            {
                // This move delivers checkmate: a win for the side to move. Prefer faster mates
                // (smaller ply) via the ply adjustment.
                score = MateScore - ply * MatePlyAdjust;
            }
            else
            {
                score = -Negamax(after, depth - 1, -beta, -alpha, Opponent(perspective), ply + 1);
            }

            if (score > best)
            {
                best = score;
                bestMove = move;
            }

            if (best > alpha)
            {
                alpha = best;
            }

            if (alpha >= beta)
            {
                break; // beta cutoff — this move refutes the opponent's previous option
            }
        }

        if (bestMove is not null)
        {
            _transpositions[key] = bestMove;
        }

        return best;
    }

    /// <summary>
    /// Orders <paramref name="moves"/> for alpha-beta efficiency, in priority order:
    ///   1. the hash move (if any) — always first,
    ///   2. captures, by MVV-LVA (most valuable victim, least valuable attacker),
    ///   3. quiet moves, by piece-square-table score (position-aware, deterministic).
    /// Captures are kept ahead of quiet moves via disjoint score bands so the band, not the
    /// numeric score, drives the captures-before-quiet guarantee. Exposed internally so the
    /// ordering heuristics are unit-testable in isolation.
    /// </summary>
    internal static IEnumerable<EngineMove> OrderMoves(
        ChessBoard board, IReadOnlyList<EngineMove> moves, EngineMove? hashMove)
    {
        // Precompute each move's ordering score once. A stable sort then preserves the input
        // order among equal-scored moves, giving the deterministic ordering the tests rely on.
        var scored = new List<OrderedMove>(moves.Count);

        for (var i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            var score = MoveOrderScore(board, move, hashMove);
            scored.Add(new OrderedMove(move, score, i));
        }

        scored.Sort((a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score); // descending: best first
            if (byScore != 0)
            {
                return byScore;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex); // stable tie-break
        });

        for (var i = 0; i < scored.Count; i++)
        {
            yield return scored[i].Move;
        }
    }

    /// <summary>
    /// Computes the single integer ordering score for a move, encoding all three heuristics into
    /// disjoint bands so the ordering precedence is: hash move &gt; captures (MVV-LVA) &gt; quiet
    /// (PST). Exposed internally so the ordering heuristics are unit-testable in isolation.
    /// </summary>
    private static int MoveOrderScore(ChessBoard board, EngineMove move, EngineMove? hashMove)
    {
        // Hash move leads. Identity is by SAN, which the library guarantees is unique within a
        // position for distinct legal moves.
        if (hashMove is not null && string.Equals(move.San, hashMove.San, StringComparison.Ordinal))
        {
            return HashMoveScore;
        }

        var attacker = board[move.OriginalPosition.ToString()];
        var target = board[move.NewPosition.ToString()];

        var isPromotion = IsPromotion(board, move);
        var isCapture = target is not null;

        if (isCapture || isPromotion)
        {
            // Captures-before-quiet: place in the Capture band. Within it, MVV-LVA:
            // victimValue - attackerValue. A non-capturing promotion still belongs ahead of
            // quiet moves (it's forcing), so it gets the band baseline plus the promotion
            // piece's value to prefer queen promotions.
            var mvvLva = 0;
            if (isCapture && target is not null && attacker is not null)
            {
                var victimValue = MaterialValue.TryGetValue(target.Type, out var vv) ? vv : 0;
                var attackerValue = MaterialValue.TryGetValue(attacker.Type, out var av) ? av : 0;
                mvvLva = victimValue - attackerValue;
            }

            var promotionBonus = isPromotion ? 950 : 0; // ~queen value, keeps promotions high
            return CaptureBand + mvvLva + promotionBonus;
        }

        // Quiet move: PST-derived score, placed in the Quiet band so it always trails captures
        // but supplies a position-aware, deterministic order among quiet moves.
        var pst = attacker is not null ? QuietPstDelta(attacker, move) : 0;
        return QuietBand + pst;
    }

    /// <summary>
    /// A small, centralisation-aware piece-square table delta for a quiet move: the difference
    /// between the destination square's PST value and the origin's. Positive means the move
    /// improves the piece's positional footprint, so such moves are searched first among quiet
    /// moves. Values are intentionally small (tens) and live inside the Quiet band.
    /// </summary>
    private static int QuietPstDelta(Piece piece, EngineMove move)
    {
        var fromSq = move.OriginalPosition.ToString();
        var toSq = move.NewPosition.ToString();
        return PieceSquareTableValue(piece.Type, toSq, piece.Color)
             - PieceSquareTableValue(piece.Type, fromSq, piece.Color);
    }

    /// <summary>
    /// Piece-square table value for a piece on a square. A tiny, symmetric table rewards
    /// central squares for minor pieces and pawn advancement, which is enough to give quiet
    /// move ordering a position-aware (and fully deterministic) signal — the same tables the
    /// evaluator uses, so ordering and eval stay consistent.
    /// </summary>
    internal static int PieceSquareTableValue(PieceType type, string square, PieceColor color)
    {
        var (file, rank) = SquareToCoords(square); // file 0..7, rank 0..7 (rank 0 == rank "1")
        // Mirror for black so tables are authored from white's perspective.
        if (color == PieceColor.Black)
        {
            rank = 7 - rank;
        }

        return type switch
        {
            PieceType.Pawn => PawnPst[rank * 8 + file],
            PieceType.Knight => KnightPst[rank * 8 + file],
            PieceType.Bishop => BishopPst[rank * 8 + file],
            PieceType.Rook => RookPst[rank * 8 + file],
            PieceType.Queen => QueenPst[rank * 8 + file],
            PieceType.King => KingPst[rank * 8 + file],
            _ => 0,
        };
    }

    private static (int file, int rank) SquareToCoords(string square)
    {
        var file = char.ToLower(square[0]) - 'a';
        var rank = square[1] - '1';
        return (file, rank);
    }

    // Piece-square tables, authored from white's perspective (rank 1 at index 0..7). Values are
    // small and only used for *ordering* (and a small eval nudge), so exact tuning is unnecessary
    // — only the relative preference for central/advanced squares matters.
    private static readonly int[] PawnPst =
    {
         0,  0,  0,  0,  0,  0,  0,  0,
         5, 10, 10,-20,-20, 10, 10,  5,
         5, -5,-10,  0,  0,-10, -5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5,  5, 10, 25, 25, 10,  5,  5,
        10, 10, 20, 30, 30, 20, 10, 10,
        50, 50, 50, 50, 50, 50, 50, 50,
         0,  0,  0,  0,  0,  0,  0,  0,
    };

    private static readonly int[] KnightPst =
    {
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
    };

    private static readonly int[] BishopPst =
    {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    };

    private static readonly int[] RookPst =
    {
          0,  0,  0,  5,  5,  0,  0,  0,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          5, 10, 10, 10, 10, 10, 10,  5,
          0,  0,  0,  0,  0,  0,  0,  0,
    };

    private static readonly int[] QueenPst =
    {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -10,  5,  5,  5,  5,  5,  0,-10,
          0,  0,  5,  5,  5,  5,  0, -5,
         -5,  0,  5,  5,  5,  5,  0, -5,
        -10,  0,  5,  5,  5,  5,  0,-10,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
    };

    private static readonly int[] KingPst =
    {
         20, 30, 10,  0,  0, 10, 30, 20,
         20, 20,  0,  0,  0,  0, 20, 20,
        -10,-20,-20,-20,-20,-20,-20,-10,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
    };

    /// <summary>
    /// Static evaluation: material balance plus piece-square-table bonus, from
    /// <paramref name="perspective"/>'s point of view. The PST term both sharpens eval and —
    /// because <see cref="OrderMoves"/> uses the same tables — keeps quiet-move ordering
    /// consistent with what the eval rewards. King material cancels in the balance; only the
    /// kings' small PST deltas survive, which is harmless.
    /// </summary>
    private static int Evaluate(ChessBoard board, PieceColor perspective)
    {
        var score = 0;

        for (var file = 'a'; file <= 'h'; file++)
        {
            for (var rank = 1; rank <= 8; rank++)
            {
                var square = $"{file}{rank}";
                var piece = board[square];
                if (piece is null)
                {
                    continue;
                }

                var material = MaterialValue.TryGetValue(piece.Type, out var v) ? v : 0;
                var pst = PieceSquareTableValue(piece.Type, square, piece.Color);
                var pieceScore = material + pst;

                if (piece.Color == perspective)
                {
                    score += pieceScore;
                }
                else
                {
                    score -= pieceScore;
                }
            }
        }

        return score;
    }

    /// <summary>
    /// Applies <paramref name="move"/> on a fresh copy of <paramref name="board"/> (so the
    /// caller's board is untouched), returning the resulting board and resolved promotion piece,
    /// or <see langword="null"/> if the engine rejected the move. The library populates the
    /// passed-in move's <c>IsMate</c>/<c>IsCheck</c> flags during <c>board.Move</c>, so the
    /// caller may inspect <c><paramref name="move"/>.IsMate</c> afterwards.
    /// </summary>
    private static (ChessBoard? After, PromotionPieceType? Promotion) ApplyMove(
        ChessBoard board, EngineMove move)
    {
        if (!ChessBoard.TryLoadFromFen(board.ToFen(), out var after, EnabledDrawRules))
        {
            return (null, null);
        }

        after.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

        try
        {
            if (!after.Move(move))
            {
                return (null, null);
            }
        }
        catch (Exception)
        {
            return (null, null);
        }

        return (after, ResolvePromotion(board, move));
    }

    /// <summary>
    /// A stable, position-only key for the transposition table: hashes the piece placement and
    /// side-to-move fields of the FEN (FNV-1a 64-bit). This deliberately excludes
    /// castling/en-passant/halfmove so that transpositions across move-orders collide as
    /// intended, while still distinguishing whose turn it is.
    /// </summary>
    private static ulong PositionKey(ChessBoard board)
    {
        var fen = board.ToFen().Split(' ');
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        Combine(fen[0]); // piece placement
        Combine(fen[1]); // side to move
        return hash;

        void Combine(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= prime;
            }
        }
    }

    private static bool IsPromotion(ChessBoard board, EngineMove move)
    {
        var piece = board[move.OriginalPosition.ToString()];
        if (piece is null || piece.Type != PieceType.Pawn)
        {
            return false;
        }

        var destinationRank = move.NewPosition.ToString()[^1];
        return destinationRank is '8' or '1';
    }

    private static PieceColor Opponent(PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    /// <summary>
    /// Determines whether <paramref name="move"/> is a pawn promotion and, if so, reports the
    /// promotion piece. The AI defaults promotions to queen (set in the OnPromotePawn handler)
    /// because a queen is almost always the highest-scoring promotion.
    /// </summary>
    private static PromotionPieceType? ResolvePromotion(ChessBoard board, EngineMove move)
    {
        var piece = board[move.OriginalPosition.ToString()];
        if (piece is null || piece.Type != PieceType.Pawn)
        {
            return null;
        }

        var destinationRank = move.NewPosition.ToString()[^1];
        if (destinationRank is not ('8' or '1'))
        {
            return null;
        }

        return PromotionPieceType.Queen;
    }

    private sealed record ScoredMove(
        string From,
        string To,
        string San,
        PromotionPieceType? Promotion,
        int Score);

    private sealed record OrderedMove(EngineMove Move, int Score, int OriginalIndex);
}
