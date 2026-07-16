using System.Diagnostics;
using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using EngineMove = Chess.Move;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// The top-level move-selection driver for the computer opponent.
///
/// <para>
/// <see cref="ChooseMove"/> is the entry point: it is a *driver* that wraps an alpha-beta search,
/// enforces a time/node budget, handles terminal positions, and — crucially — *guarantees* that
/// whatever it returns is a legal move (or <see langword="null"/> when the position is terminal).
/// </para>
///
/// <para>
/// The architecture is deliberately layered so each acceptance criterion lives in one place:
/// <list type="bullet">
///   <item><b>Driver wraps search</b> — <see cref="ChooseMove"/> orchestrates
///       <see cref="SearchRoot"/>, which in turn drives the recursive
///       <see cref="Search"/> alpha-beta routine.</item>
///   <item><b>Time/node budget enforced</b> — a <see cref="SearchBudget"/> carries a wall-clock
///       deadline and a hard node cap. Every node checks <see cref="SearchBudget.IsExhausted"/>
///       and aborts the in-flight iteration the instant either limit trips, so the engine never
///       runs away with the user's time. Iterative deepening means a usable best move is always
///       on hand from the most recently *completed* depth.</item>
///   <item><b>Terminal states handled</b> — before any search, the driver enumerates legal moves.
///       An empty set means checkmate/stalemate (already classified by the rules engine on the
///       move that produced the FEN), so the driver returns <see langword="null"/> and never
///       searches a dead position. Inside the search, terminal leaves are scored with the
///       mate/draw sentinel values so the tree-search prefers mating lines and avoids stalemates.
///   </item>
///   <item><b>Guarantees a legal returned move</b> — the driver scores the root moves at
///       depth 1 *unconditionally* and keeps that best move as a floor. Search can only
///       *refine* the choice among moves it proved legal by actually playing them; it can never
///       downgrade below the guaranteed-legal floor. As a final belt-and-braces check, the
///       chosen move is re-verified against the legal-move set before it is returned, so a
///       search bug or a budget abort mid-replacement can never yield an illegal move. If somehow
///       no verified move survives, the driver falls back to the depth-1 floor, and ultimately to
///       the first legal move.</item>
/// </list>
/// </para>
///
/// <para>
/// Determinism: tie-breaks are on SAN (ordinal compare), and the root move list is taken from the
/// engine in its natural order, so repeated calls against the same FEN pick the same move —
/// essential for unit tests and reproducible AI games. With a default budget that is generous
/// enough to complete the shallow positions used in tests, the depth-1 behaviour matches the old
/// greedy heuristic exactly (it grabs free material, plays a mate on the board, checks when it
/// can't mate), which keeps the existing AI unit tests green.
/// </para>
/// </summary>
public sealed class GreedyHeuristicChessAi : IChessAi
{
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

    private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;

    // Search sentinels. A win for the side to move is a large positive number that shrinks with
    // distance-to-mate so the engine prefers faster mates; a loss is its negation. The
    // half-million margin comfortably exceeds any reachable material swing (max ~9 queens ≈
    // 8100) so material can never be confused with a forced mate.
    private const int MateScore = 1_000_000;
    private const int MateMargin = 500_000;
    private const int DrawScore = 0;

    // Default budget. The time limit is the hard ceiling on a single move; iterative deepening
    // returns the best move from the last fully-searched depth as soon as it trips. The node cap
    // is a second, independent guard against pathological positions where per-node work is cheap
    // but the branching factor explodes. Both are deliberately conservative for an MVP opponent
    // and cheap enough that the shallow test positions complete instantly.
    private static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromMilliseconds(500);
    private const long DefaultNodeLimit = 200_000;

    // The maximum depth iterative deepening will attempt. It's a backstop so a position where
    // every ply is instant still terminates; the time/node budget is the real bound in practice.
    private const int MaxSearchDepth = 64;

    private readonly TimeSpan _timeLimit;
    private readonly long _nodeLimit;
    private readonly IStopwatch _stopwatch;

    /// <summary>
    /// Creates the driver with the default time and node budget.
    /// </summary>
    public GreedyHeuristicChessAi()
        : this(DefaultTimeLimit, DefaultNodeLimit, SystemStopwatch.Instance)
    {
    }

    /// <summary>
    /// Creates the driver with an explicit time and node budget, primarily for tests that need
    /// to probe budget enforcement without relying on wall-clock timing.
    /// </summary>
    public GreedyHeuristicChessAi(TimeSpan timeLimit, long nodeLimit)
        : this(timeLimit, nodeLimit, SystemStopwatch.Instance)
    {
    }

    /// <summary>
    /// Internal constructor that takes the clock abstraction so budget behaviour can be tested
    /// deterministically.
    /// </summary>
    internal GreedyHeuristicChessAi(TimeSpan timeLimit, long nodeLimit, IStopwatch stopwatch)
    {
        if (timeLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeLimit), "Time limit must be positive.");
        }

        if (nodeLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeLimit), "Node limit must be positive.");
        }

        _timeLimit = timeLimit;
        _nodeLimit = nodeLimit;
        _stopwatch = stopwatch;
    }

    /// <summary>
    /// The top-level driver. Wraps the alpha-beta search, enforces the time/node budget, handles
    /// terminal states, and guarantees a legal returned move.
    /// </summary>
    public AiMove? ChooseMove(string fen)
    {
        // --- Terminal-state handling -----------------------------------------------------------
        // If the FEN is malformed there's nothing to do; the caller should have caught this
        // upstream, but returning null keeps the AI total and never throws.
        if (!ChessBoard.TryLoadFromFen(fen, out var board, EnabledDrawRules))
        {
            return null;
        }

        var legalMoves = board.Moves().ToList();
        if (legalMoves.Count == 0)
        {
            // No legal moves: terminal position already classified by the rules engine on the
            // move that produced `fen`. Nothing for the AI to do — return null so the caller
            // leaves the game state alone.
            return null;
        }

        var sideToMove = board.Turn;

        // --- Guaranteed-legal floor ------------------------------------------------------------
        // Score every legal root move at depth 1 *unconditionally*. This is the move we will
        // fall back to no matter what the deeper search does, and it is provably legal because
        // it came straight from the engine's legal-move generator. It also matches the old
        // greedy heuristic's behaviour, keeping the existing tests green.
        var floor = ScoreRootMoves(board, legalMoves, sideToMove);

        // --- Search with budget enforcement ---------------------------------------------------
        // Iterative deepening: search to depth 1, 2, 3, ... and keep the best move from the
        // most recently *completed* iteration. If the budget trips mid-iteration we discard
        // that partial result and return the previous iteration's move. The floor is always the
        // depth-1 result, so the very first iteration is effectively free and guaranteed.
        var budget = new SearchBudget(_stopwatch, _timeLimit, _nodeLimit);
        var bestSoFar = floor;

        for (var depth = 1; depth <= MaxSearchDepth; depth++)
        {
            // If the budget is already gone before we even start this iteration, stop — we have
            // a guaranteed-legal move from the previous iteration.
            if (budget.IsExhausted)
            {
                break;
            }

            var iterationResult = SearchRoot(board, legalMoves, depth, budget);

            // A budget abort means this iteration's result is incomplete and must not be
            // trusted over the last completed one. The floor remains our guarantee.
            if (iterationResult.Aborted)
            {
                break;
            }

            if (iterationResult.Best is not null)
            {
                bestSoFar = iterationResult.Best;
            }

            // Found a forced mate — no point searching deeper, the result can't improve.
            if (iterationResult.IsMateFound)
            {
                break;
            }
        }

        // --- Final legal-move guarantee --------------------------------------------------------
        // As a last line of defence, re-verify the chosen move is actually legal. The search
        // only ever proposes moves it played on the board, so this should always pass, but it
        // protects against a budget abort landing on a partially-updated candidate or any future
        // search bug. If verification fails, fall back to the guaranteed-legal floor.
        var chosen = bestSoFar;
        if (chosen is not null && !IsLegal(board, chosen))
        {
            chosen = floor;
        }

        if (chosen is null)
        {
            // Should be unreachable — `legalMoves` was non-empty and the floor scores all of
            // them — but be total: if everything has somehow gone wrong, fall back to the first
            // legal move rather than returning an illegal one or throwing.
            var fallback = legalMoves[0];
            chosen = ToCandidate(board, fallback, score: 0);
        }

        return new AiMove(chosen.From, chosen.To, chosen.Promotion);
    }

    /// <summary>
    /// Scores every legal root move at depth 1 and returns the best one. This is the
    /// guaranteed-legal floor: it never calls the recursive search, so it can never be aborted
    /// or produce an illegal move. Tie-breaks on SAN for determinism.
    /// </summary>
    private static CandidateMove ScoreRootMoves(
        ChessBoard board,
        List<EngineMove> legalMoves,
        PieceColor sideToMove)
    {
        CandidateMove? best = null;
        foreach (var move in legalMoves)
        {
            var scored = ScoreRootMove(board, move, sideToMove);
            if (IsBetter(scored, best))
            {
                best = scored;
            }
        }

        // legalMoves is non-empty here (caller checked), so best is never null.
        return best!;
    }

    /// <summary>
    /// Scores a single root move by playing it and evaluating the resulting position. This is
    /// the depth-1 evaluation that the floor and the old greedy heuristic both rely on. Mate is
    /// detected via <c>move.IsMate</c>, which the library sets on the move that produces mate.
    /// </summary>
    private static CandidateMove ScoreRootMove(ChessBoard board, EngineMove move, PieceColor sideToMove)
    {
        var (after, legal) = PlayMove(board, move);
        if (!legal)
        {
            // The engine's own legal-move generator produced this, so this branch is defensive;
            // score it worst so it can never win the tie-break.
            return ToCandidate(board, move, int.MinValue, isMate: false);
        }

        var score = EvaluateMaterial(after, sideToMove);

        var isMate = move.IsMate;
        if (isMate)
        {
            // A move that delivers checkmate dominates every other score, so the AI always plays
            // a mate when one is on the board.
            score += MateScore;
        }

        // Small bonus for giving check (encourages forcing moves over quiet ones among
        // otherwise-equal material positions).
        if (move.IsCheck)
        {
            score += 50;
        }

        return ToCandidate(board, move, score, isMate);
    }

    /// <summary>
    /// The root of the alpha-beta search for one iterative-deepening iteration. Returns the best
    /// move at <paramref name="depth"/> plus whether the iteration was aborted by the budget.
    /// </summary>
    private IterationResult SearchRoot(
        ChessBoard board,
        List<EngineMove> legalMoves,
        int depth,
        SearchBudget budget)
    {
        CandidateMove? best = null;
        var alpha = int.MinValue + 1;
        var beta = int.MaxValue;
        var mateFound = false;

        foreach (var move in legalMoves)
        {
            // Budget is checked at the root too, so a very large legal-move list can't blow the
            // cap before we ever recurse.
            if (budget.IsExhausted)
            {
                return IterationResult.Aborted(best);
            }

            var (after, legal) = PlayMove(board, move);
            if (!legal)
            {
                // Defensive: skip moves the engine now rejects (shouldn't happen for its own
                // generator output). They can't be chosen, so don't let them contaminate the
                // search.
                continue;
            }

            // Negamax: the child is scored from the opponent's perspective, so we negate.
            var childScore = -Search(after, depth - 1, -beta, -alpha, budget);

            // If the budget tripped during the child search, this score is unreliable — abort
            // the whole iteration and keep the previous completed one.
            if (budget.IsExhausted)
            {
                return IterationResult.Aborted(best);
            }

            // `move.IsMate` is the library's reliable signal that this move delivered checkmate
            // (it's the same flag the original heuristic relied on).
            var candidate = ToCandidate(board, move, childScore, isMate: move.IsMate);

            if (candidate.IsMate)
            {
                mateFound = true;
            }

            if (IsBetter(candidate, best))
            {
                best = candidate;
            }

            if (childScore > alpha)
            {
                alpha = childScore;
            }
        }

        return IterationResult.Completed(best, mateFound);
    }

    /// <summary>
    /// Recursive negamax alpha-beta search. Returns the score from the side-to-move's
    /// perspective. Honours the <paramref name="budget"/> at every node: the instant it is
    /// exhausted the search returns a sentinel and unwinds, and the caller discards the result.
    /// </summary>
    private int Search(ChessBoard board, int depth, int alpha, int beta, SearchBudget budget)
    {
        // --- Budget enforcement at every node -------------------------------------------------
        budget.Tick();
        if (budget.IsExhausted)
        {
            // Returning a neutral-ish score is fine because the caller checks IsExhausted
            // immediately after and discards the result.
            return 0;
        }

        // Generate moves first. This is needed both to recurse and because the library evaluates
        // the endgame (checkmate/stalemate) as part of move generation, populating board.EndGame
        // for terminal positions.
        var moves = board.Moves().ToList();

        // --- Terminal-state handling inside the tree -------------------------------------------
        if (moves.Count == 0)
        {
            return ClassifyTerminal(board, depth);
        }

        // The library may flag an auto-draw (fifty-move rule) even when moves remain; honour it
        // so the search doesn't try to play past a drawn position.
        if (IsDrawEndgame(board.EndGame))
        {
            return DrawScore;
        }

        // --- Leaf evaluation ------------------------------------------------------------------
        if (depth <= 0)
        {
            return EvaluateMaterial(board, board.Turn);
        }

        var bestScore = int.MinValue + 1;

        foreach (var move in moves)
        {
            if (budget.IsExhausted)
            {
                return 0;
            }

            var (after, legal) = PlayMove(board, move);
            if (!legal)
            {
                continue;
            }

            var score = -Search(after, depth - 1, -beta, -alpha, budget);

            if (budget.IsExhausted)
            {
                return 0;
            }

            if (score > bestScore)
            {
                bestScore = score;
            }

            if (score > alpha)
            {
                alpha = score;
            }

            if (alpha >= beta)
            {
                // Beta cutoff: the opponent can refute this line, so don't search the rest.
                break;
            }
        }

        // If every move was rejected as illegal (shouldn't happen given the terminal checks
        // above), fall back to a static eval rather than returning the int.MinValue sentinel.
        return bestScore == int.MinValue + 1 ? EvaluateMaterial(board, board.Turn) : bestScore;
    }

    /// <summary>
    /// Scores a terminal position (no legal moves) from the side-to-move's perspective. With
    /// only the fifty-move rule enabled, the library's <see cref="ChessBoard.EndGame"/> is set
    /// for checkmate, stalemate and fifty-move rule; we classify by elimination, mirroring the
    /// rules-engine adapter's <c>endgame?.EndgameType</c> pattern (which works whether
    /// <c>Endgame</c> is a struct or a class). A mated side gets a large negative score that
    /// shrinks with distance-to-mate so the engine prefers faster mates; a draw scores zero.
    /// </summary>
    private static int ClassifyTerminal(ChessBoard board, int depth)
    {
        if (IsDrawEndgame(board.EndGame))
        {
            // Stalemate or fifty-move draw.
            return DrawScore;
        }

        // No moves and not a flagged draw: the side to move has been checkmated. Penalise it,
        // and prefer mates that are *closer* (smaller remaining depth => faster mate) by making
        // the penalty less severe for nearer mates. We negate because this is the mated side's
        // perspective.
        return -(MateScore - (MateMargin - depth));
    }

    /// <summary>
    /// True if <paramref name="endgame"/> represents a draw (stalemate or fifty-move rule) — the
    /// only auto-draw enabled via <see cref="EnabledDrawRules"/>. Uses the same
    /// <c>?.EndgameType</c> access as the rules-engine adapter so it compiles regardless of
    /// whether <c>Endgame</c> is a value type or reference type.
    /// </summary>
    private static bool IsDrawEndgame(Endgame? endgame) =>
        endgame?.EndgameType == EndgameType.Stalemate
        || endgame?.EndgameType == EndgameType.FiftyMoveRule;

    /// <summary>
    /// Plays <paramref name="move"/> on a freshly reloaded copy of <paramref name="board"/> so
    /// the caller's board is untouched (the library doesn't expose a safe make/unmake across
    /// versions). Returns the resulting board and whether the move was legal.
    /// </summary>
    private static (ChessBoard After, bool Legal) PlayMove(ChessBoard board, EngineMove move)
    {
        if (!ChessBoard.TryLoadFromFen(board.ToFen(), out var after, EnabledDrawRules))
        {
            // Should never happen — we just serialised this board — but treat as unplayable.
            return (after!, false);
        }

        // The library raises this event for promotions; default to queen, which a greedy eval
        // almost always scores highest and which matches the original behaviour.
        after.OnPromotePawn += (_, e) => e.PromotionResult = PromotionType.ToQueen;

        try
        {
            if (!after.Move(move))
            {
                return (after, false);
            }
        }
        catch (Exception)
        {
            // The library throws for some illegal-at-the-engine-level moves; treat as unplayable.
            return (after, false);
        }

        return (after, true);
    }

    /// <summary>
    /// Material balance (our pieces minus opponent's), evaluated from the side-to-move's
    /// perspective. Captures naturally score highly because the captured piece vanishes from the
    /// opponent's total.
    /// </summary>
    private static int EvaluateMaterial(ChessBoard board, PieceColor sideToMove)
    {
        var material = 0;

        // ChessBoard doesn't expose a piece iterator in a stable form across library versions,
        // so walk the 64 squares directly. File a..h, rank 1..8 — the library's indexer accepts
        // algebraic notation and returns a typed, nullable piece with .Type/.Color.
        for (var file = 'a'; file <= 'h'; file++)
        {
            for (var rank = 1; rank <= 8; rank++)
            {
                var piece = board[$"{file}{rank}"];
                if (piece is null)
                {
                    continue;
                }

                var value = MaterialValue.TryGetValue(piece.Type, out var v) ? v : 0;
                if (piece.Color == sideToMove)
                {
                    material += value;
                }
                else
                {
                    material -= value;
                }
            }
        }

        return material;
    }

    /// <summary>
    /// Builds a <see cref="CandidateMove"/> from an engine move, resolving the promotion piece
    /// (queen by default, matching the original heuristic).
    /// </summary>
    private static CandidateMove ToCandidate(ChessBoard board, EngineMove move, int score, bool isMate = false)
    {
        return new CandidateMove(
            move.OriginalPosition.ToString(),
            move.NewPosition.ToString(),
            move.San,
            ResolvePromotion(board, move),
            score,
            isMate);
    }

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

    /// <summary>
    /// True if <paramref name="candidate"/> is a better root move than the current
    /// <paramref name="best"/>. Tie-breaks on SAN (ordinal) for determinism so repeated runs
    /// against the same position pick the same move.
    /// </summary>
    private static bool IsBetter(CandidateMove candidate, CandidateMove? best)
    {
        if (best is null)
        {
            return true;
        }

        if (candidate.Score > best.Score)
        {
            return true;
        }

        return candidate.Score == best.Score
            && string.CompareOrdinal(candidate.San, best.San) < 0;
    }

    /// <summary>
    /// Verifies a candidate move is legal in <paramref name="board"/> by checking it against the
    /// engine's legal-move generator. This is the final guarantee that the returned move is
    /// legal regardless of what the search did.
    /// </summary>
    private static bool IsLegal(ChessBoard board, CandidateMove candidate)
    {
        foreach (var move in board.Moves())
        {
            if (string.Equals(move.OriginalPosition.ToString(), candidate.From, StringComparison.OrdinalIgnoreCase)
                && string.Equals(move.NewPosition.ToString(), candidate.To, StringComparison.OrdinalIgnoreCase))
            {
                // For promotions, legality doesn't depend on the promotion piece (any legal
                // promotion target is acceptable), so matching from/to is sufficient.
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A scored root-move candidate carried up to the driver. <see cref="San"/> drives the
    /// deterministic tie-break; <see cref="IsMate"/> lets the driver short-circuit on a forced
    /// mate.
    /// </summary>
    private sealed record CandidateMove(
        string From,
        string To,
        string San,
        PromotionPieceType? Promotion,
        int Score,
        bool IsMate);

    /// <summary>
    /// The outcome of one iterative-deepening root iteration.
    /// </summary>
    private sealed record IterationResult(CandidateMove? Best, bool Aborted, bool IsMateFound)
    {
        public static IterationResult Completed(CandidateMove? best, bool mateFound) =>
            new(best, Aborted: false, IsMateFound: mateFound);

        public static IterationResult Aborted(CandidateMove? best) =>
            new(best, Aborted: true, IsMateFound: false);
    }

    /// <summary>
    /// The time/node budget for a single <see cref="ChooseMove"/> call. Checked at every search
    /// node; the moment either limit trips the search aborts and the driver keeps the best move
    /// from the last completed iteration.
    /// </summary>
    private sealed class SearchBudget
    {
        private readonly IStopwatch _stopwatch;
        private readonly long _deadlineTicks;
        private readonly long _nodeLimit;
        private long _nodes;

        public SearchBudget(IStopwatch stopwatch, TimeSpan timeLimit, long nodeLimit)
        {
            _stopwatch = stopwatch;
            _deadlineTicks = stopwatch.ElapsedTicks + timeLimit.Ticks;
            _nodeLimit = nodeLimit;
        }

        public void Tick() => Interlocked.Increment(ref _nodes);

        public bool IsExhausted =>
            _nodes >= _nodeLimit || _stopwatch.ElapsedTicks >= _deadlineTicks;
    }

    /// <summary>
    /// Abstraction over a stopwatch so the budget can be tested deterministically.
    /// </summary>
    internal interface IStopwatch
    {
        long ElapsedTicks { get; }
    }

    private sealed class SystemStopwatch : IStopwatch
    {
        public static readonly SystemStopwatch Instance = new();
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public long ElapsedTicks => _sw.ElapsedTicks;
    }
}
