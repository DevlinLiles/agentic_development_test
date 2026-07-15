using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// A depth-limited alpha-beta (negamax) chess engine. It searches to a configurable fixed
/// depth (default 3) using a material-balance evaluation plus mate bonuses, with alpha-beta
/// pruning and MVV (most-valuable-victim) capture-first move ordering to keep per-move
/// wall-clock time bounded.
///
/// This is the engine exercised by the AI response-time benchmark (<c>ChessMvp.Bench</c>),
/// which asserts every sampled position is solved within the SLA (≤ 2000 ms at depth 3 on
/// commodity hardware). Like <see cref="GreedyHeuristicChessAi"/> it is stateless and
/// FEN-in/result-out, implementing <see cref="IChessAi"/>.
/// </summary>
public sealed class SearchChessAi : IChessAi
{
    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    // Mate is worth far more than any material swing; the ply adjustment makes the engine
    // prefer shorter mates. Kept well below int.MaxValue/2 so negamax negation never overflows.
    private const int MateScore = 1_000_000;
    private const int NegInf = int.MinValue + 1;

    // Standard piece values, in pawn units. The king is given a large value purely as a safe
    // sentinel; in a legal position it can never actually be captured.
    private static readonly Dictionary<PieceType, int> MaterialValue = new()
    {
        [PieceType.Pawn] = 100,
        [PieceType.Knight] = 320,
        [PieceType.Bishop] = 330,
        [PieceType.Rook] = 500,
        [PieceType.Queen] = 900,
        [PieceType.King] = 100_000,
    };

    // Pre-built algebraic square names so the leaf evaluation never allocates per-square
    // strings on the hot path.
    private static readonly string[] SquareNames = BuildSquareNames();

    private readonly int _depth;

    public SearchChessAi(int depth = 3)
    {
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth), "Search depth must be at least 1.");
        _depth = depth;
    }

    public AiMove? ChooseMove(string fen)
    {
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
            return null;

        var rootMoves = OrderMoves(board, board.Moves().ToList());
        if (rootMoves.Count == 0)
            return null;

        var alpha = NegInf;
        EngineMove? bestMove = null;
        var bestScore = NegInf;

        foreach (var move in rootMoves)
        {
            int score;
            if (move.IsMate)
            {
                // A mate in one at the root is the best possible outcome.
                score = MateScore;
            }
            else
            {
                var next = TryMakeMove(board, move);
                if (next is null)
                    continue; // library rejected the move; skip it entirely

                score = -Search(next, _depth - 1, NegInf, -alpha, 1);
            }

            // Deterministic tie-break on SAN so repeated benchmark runs pick the same move.
            if (bestMove is null
                || score > bestScore
                || (score == bestScore && string.CompareOrdinal(move.San, bestMove.San) < 0))
            {
                bestScore = score;
                bestMove = move;
            }

            if (score > alpha)
                alpha = score;
        }

        if (bestMove is null)
            return null;

        var promotion = ResolvePromotion(board, bestMove);
        return new AiMove(
            bestMove.OriginalPosition.ToString(),
            bestMove.NewPosition.ToString(),
            promotion);
    }

    /// <summary>
    /// Negamax with alpha-beta pruning. Returns the score from the side-to-move's perspective.
    /// Mates are detected via <see cref="EngineMove.IsMate"/> on the move that creates them, so
    /// they are short-circuited here rather than discovered by recursing into a mated position.
    /// </summary>
    private int Search(ChessBoard board, int depth, int alpha, int beta, int ply)
    {
        // Leaves are evaluated directly (no move generation) to keep the hot path cheap; a mate
        // that would appear at a leaf is already caught by IsMate on the parent's move.
        if (depth <= 0)
            return EvaluateFromSideToMove(board);

        var moves = board.Moves().ToList();
        if (moves.Count == 0)
        {
            // No legal moves and we only reach here when the parent move was not mate, so the
            // side to move is stalemated: a dead draw from its own perspective.
            return 0;
        }

        var best = NegInf;
        foreach (var move in OrderMoves(board, moves))
        {
            var next = TryMakeMove(board, move);
            if (next is null)
                continue;

            var score = move.IsMate
                ? MateScore - ply // prefer faster mates (fewer plies = higher score)
                : -Search(next, depth - 1, -beta, -alpha, ply + 1);

            if (score > best)
                best = score;
            if (best > alpha)
                alpha = best;
            if (alpha >= beta)
                break; // beta cutoff
        }

        return best;
    }

    /// <summary>
    /// Clones <paramref name="board"/> via a FEN round-trip and applies <paramref name="move"/>
    /// on the clone, leaving the caller's board untouched. Returns <see langword="null"/> if the
    /// library rejects the move (treated as unplayable and skipped by the search).
    /// </summary>
    private static ChessBoard? TryMakeMove(ChessBoard board, EngineMove move)
    {
        if (!ChessBoard.TryLoadFromFen(board.ToFen(), out var next, EnabledDrawRules))
            return null;

        // The library raises this event for promotions; default to queen.
        next.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

        try
        {
            return next.Move(move) ? next : null;
        }
        catch
        {
            // The library throws for some illegal-at-the-engine-level moves; treat as unplayable.
            return null;
        }
    }

    /// <summary>
    /// Material balance from the side-to-move's perspective (own pieces minus opponent's),
    /// which is the negamax convention the search relies on.
    /// </summary>
    private static int EvaluateFromSideToMove(ChessBoard board)
    {
        var sideToMove = board.Turn;
        var material = 0;
        foreach (var square in SquareNames)
        {
            var piece = board[square];
            if (piece is null)
                continue;

            var value = MaterialValue.TryGetValue(piece.Type, out var v) ? v : 0;
            if (piece.Color == sideToMove)
                material += value;
            else
                material -= value;
        }

        return material;
    }

    /// <summary>
    /// Orders moves so forcing moves are tried first, maximising alpha-beta pruning:
    ///   1. mates (highest priority),
    ///   2. captures, ordered by most-valuable-victim (MVV) — the classic, cheap and very
    ///      effective ordering for a material evaluation,
    ///   3. quiet moves.
    /// Stable SAN ordering within each bucket keeps benchmark runs reproducible.
    /// The victim value is read off the pre-move destination square; en passant (where the
    /// victim is not on the destination) simply falls into the quiet bucket, which is harmless
    /// for ordering quality.
    /// </summary>
    private static List<EngineMove> OrderMoves(ChessBoard board, List<EngineMove> moves)
        => moves
            .OrderByDescending(m => m.IsMate ? int.MaxValue : VictimValue(board, m))
            .ThenBy(m => m.San)
            .ToList();

    private static int VictimValue(ChessBoard board, EngineMove move)
    {
        var victim = board[move.NewPosition.ToString()];
        return victim is null
            ? 0
            : MaterialValue.TryGetValue(victim.Type, out var v) ? v : 0;
    }

    /// <summary>
    /// Determines whether <paramref name="move"/> is a pawn promotion and, if so, reports the
    /// promotion piece. The search defaults promotions to queen (set in the OnPromotePawn
    /// handler above) because a queen is almost always the highest-scoring promotion.
    /// </summary>
    private static PromotionPieceType? ResolvePromotion(ChessBoard board, EngineMove move)
    {
        var piece = board[move.OriginalPosition.ToString()];
        if (piece is null || piece.Type != PieceType.Pawn)
            return null;

        var destinationRank = move.NewPosition.ToString()[^1];
        return destinationRank is '8' or '1' ? PromotionPieceType.Queen : null;
    }

    private static string[] BuildSquareNames()
    {
        var names = new string[64];
        var i = 0;
        for (var rank = 1; rank <= 8; rank++)
            for (var file = 'a'; file <= 'h'; file++)
                names[i++] = $"{file}{rank}";
        return names;
    }
}
