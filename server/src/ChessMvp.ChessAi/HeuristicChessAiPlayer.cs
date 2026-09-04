using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.ChessAi;

/// <summary>
/// A greedy <see cref="IChessAiPlayer"/> that performs a 1-ply search over every legal move and
/// selects the one whose resulting position scores highest for the side to move, as measured by
/// an <see cref="IHeuristicEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// For each legal move the player asks the <see cref="IChessRulesEngine"/> to apply the move
/// (yielding the resulting FEN) and then scores that resulting position from the perspective of
/// the side that moved via the <see cref="IHeuristicEvaluator"/>. Because the evaluator is
/// side-relative (higher is better for the evaluated colour), the move with the highest such
/// score is greedily selected. This is a pure 1-ply search: the opponent's replies are not
/// expanded, so the player captures material and seeks positional gains but does not see
/// tactical refutations beyond what the static evaluator reflects.
/// </para>
/// <para>
/// Selection is deterministic. Ties in score are broken by ascending Standard Algebraic
/// Notation using an ordinal comparison, so two runs over the same position and legal-move set
/// always yield the same move. The exhaustive scored candidate list from the most recent
/// <see cref="SelectMove"/> call is published best-first via <see cref="GetLastCandidates"/> for
/// analysis tooling and test assertions.
/// </para>
/// <para>
/// Promotion moves are handled explicitly. By default a queen is promoted; a knight promotion is
/// chosen instead only when it evaluates to a strictly better resulting position than the queen
/// promotion (the standard case in which underpromotion avoids a stalemate or forces a win). The
/// promotion piece may alternatively be fixed through <see cref="HeuristicChessAiPlayerOptions"/>.
/// The chosen piece is recorded on the returned <see cref="AiMoveResult.PromotionPiece"/> so
/// callers know how to apply the move, and the score used to rank the candidate is the score of
/// the resulting position with that chosen piece.
/// </para>
/// </remarks>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IHeuristicEvaluator _evaluator;
    private readonly HeuristicChessAiPlayerOptions _options;

    // Published for GetLastCandidates. References are atomically assigned on .NET so a concurrent
    // read never observes a torn value; the field is otherwise only meaningful relative to the
    // most recent SelectMove call, which matches the documented "analysis tooling, not gameplay"
    // intent. A null value means SelectMove has not yet produced candidates.
    private IReadOnlyList<ScoredMoveCandidate>? _lastCandidates;

    /// <summary>
    /// Creates a player with the default (auto queen-or-knight) promotion policy.
    /// </summary>
    public HeuristicChessAiPlayer(IChessRulesEngine rulesEngine, IHeuristicEvaluator evaluator)
        : this(rulesEngine, evaluator, HeuristicChessAiPlayerOptions.Default)
    {
    }

    /// <summary>
    /// Creates a player with an explicit <paramref name="options"/> configuration.
    /// </summary>
    public HeuristicChessAiPlayer(
        IChessRulesEngine rulesEngine,
        IHeuristicEvaluator evaluator,
        HeuristicChessAiPlayerOptions options)
    {
        _rulesEngine = rulesEngine;
        _evaluator = evaluator;
        _options = options;
    }

    /// <inheritdoc />
    public AiMoveSelection SelectMove(
        string fen,
        PlayerColor sideToMove,
        IReadOnlyList<LegalMove> legalMoves,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return AiMoveSelection.Failure("Move selection was cancelled before it began.");
        }

        if (!IsValidFen(fen))
        {
            return AiMoveSelection.InvalidPosition("The supplied FEN is not a valid board state.");
        }

        if (legalMoves is null || legalMoves.Count == 0)
        {
            // No moves to choose from: the position is terminal (checkmate or stalemate). Reset
            // the candidate cache so a stale list from a previous call is never reported.
            _lastCandidates = null;
            return AiMoveSelection.NoMoves();
        }

        var scored = new List<ScoredCandidate>(legalMoves.Count);
        foreach (var move in legalMoves)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AiMoveSelection.Failure("Move selection was cancelled mid-search.");
            }

            if (TryScoreMove(fen, sideToMove, move, out var candidate))
            {
                scored.Add(candidate);
            }
        }

        if (scored.Count == 0)
        {
            // Every supplied move failed to apply (e.g. a contradictory rules-engine state). This
            // is unexpected for moves the caller asserted were legal, so surface it as a failure
            // rather than silently picking nothing.
            _lastCandidates = null;
            return AiMoveSelection.Failure(
                "No supplied legal move could be applied to produce a resulting position.");
        }

        // Deterministic ordering: best score first, then ascending SAN via an ordinal comparison
        // so the result is independent of the input ordering and of culture.
        scored.Sort(CompareCandidatesBestFirst);

        _lastCandidates = scored
            .Select(c => new ScoredMoveCandidate { Move = c.Move, Score = c.Score })
            .ToList();

        var best = scored[0];
        var result = new AiMoveResult
        {
            Move = best.Move,
            Score = best.Score,
            ConsideredMoveCount = scored.Count,
            PromotionPiece = best.Promotion,
            Explanation = best.Promotion.HasValue
                ? $"Greedy 1-ply: promoted to {best.Promotion.Value}."
                : "Greedy 1-ply selection.",
        };
        return AiMoveSelection.Success(result);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScoredMoveCandidate>? GetLastCandidates() => _lastCandidates;

    /// <summary>
    /// Scores a single legal move by applying it and evaluating the resulting position from the
    /// perspective of <paramref name="sideToMove"/>. Promotion moves are resolved according to
    /// the configured policy before scoring; the score returned is that of the resulting position
    /// with the chosen promotion piece. Returns <c>false</c> (and leaves <paramref name="candidate"/>
    /// at its default) when the move could not be applied to yield a resulting position.
    /// </summary>
    private bool TryScoreMove(
        string fen,
        PlayerColor sideToMove,
        LegalMove move,
        out ScoredCandidate candidate)
    {
        if (!move.IsPromotion)
        {
            var resultingFen = TryApply(fen, sideToMove, move, promotion: null);
            if (resultingFen is null)
            {
                candidate = default;
                return false;
            }

            var score = _evaluator.Evaluate(resultingFen, sideToMove);
            candidate = new ScoredCandidate(move, score, Promotion: null);
            return true;
        }

        // Promotion: resolve the piece (which itself scores the resulting positions) and reuse
        // the score of the chosen piece as the candidate score, avoiding a redundant application.
        return TryResolvePromotion(fen, sideToMove, move, out candidate);
    }

    /// <summary>
    /// Resolves the promotion piece for <paramref name="move"/> and produces the scored
    /// candidate for the chosen piece. When a fixed piece is configured it is always used and
    /// scored directly; otherwise the queen and knight promotions are both applied and scored,
    /// and the knight is chosen only when it is strictly better than the queen (the default is
    /// queen). Returns <c>false</c> when neither promotion could be applied.
    /// </summary>
    private bool TryResolvePromotion(
        string fen,
        PlayerColor sideToMove,
        LegalMove move,
        out ScoredCandidate candidate)
    {
        if (_options.PromotionPiece.HasValue)
        {
            return TryScorePromotion(
                fen, sideToMove, move, _options.PromotionPiece.Value, out candidate);
        }

        // Compare queen against knight under the default policy. Only the side that actually
        // applies contributes a candidate; if both apply, the strictly-higher-scoring piece wins
        // with queen as the tie-breaker.
        var queenOk = TryScorePromotion(fen, sideToMove, move, PromotionPieceType.Queen, out var queen);
        var knightOk = TryScorePromotion(fen, sideToMove, move, PromotionPieceType.Knight, out var knight);

        if (!queenOk && !knightOk)
        {
            candidate = default;
            return false;
        }

        if (!queenOk)
        {
            candidate = knight;
            return true;
        }

        if (!knightOk)
        {
            candidate = queen;
            return true;
        }

        // Default to queen; underpromote to a knight only when it is clearly (strictly) better.
        candidate = knight.Score > queen.Score ? knight : queen;
        return true;
    }

    /// <summary>
    /// Applies <paramref name="move"/> as a promotion to <paramref name="piece"/> and scores the
    /// resulting position from the perspective of <paramref name="sideToMove"/>. Returns
    /// <c>false</c> when the move could not be applied.
    /// </summary>
    private bool TryScorePromotion(
        string fen,
        PlayerColor sideToMove,
        LegalMove move,
        PromotionPieceType piece,
        out ScoredCandidate candidate)
    {
        var resultingFen = TryApply(fen, sideToMove, move, piece);
        if (resultingFen is null)
        {
            candidate = default;
            return false;
        }

        var score = _evaluator.Evaluate(resultingFen, sideToMove);
        candidate = new ScoredCandidate(move, score, piece);
        return true;
    }

    private string? TryApply(
        string fen,
        PlayerColor sideToMove,
        LegalMove move,
        PromotionPieceType? promotion)
    {
        var application = _rulesEngine.TryApplyMove(
            fen, sideToMove, move.FromSquare, move.ToSquare, promotion);

        if (!application.IsLegal || application.ResultingFen is null)
        {
            return null;
        }

        return application.ResultingFen;
    }

    /// <summary>
    /// Best-first comparison: higher score first, then ascending SAN by ordinal comparison so the
    /// ordering is deterministic and culture-independent.
    /// </summary>
    private static int CompareCandidatesBestFirst(ScoredCandidate x, ScoredCandidate y)
    {
        var byScore = y.Score.CompareTo(x.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        return StringComparer.Ordinal.Compare(x.Move.San, y.Move.San);
    }

    /// <summary>
    /// Lightweight FEN structural validation mirroring the checks the heuristic evaluator
    /// performs: a non-blank string, at least two whitespace-delimited fields, eight slash-
    /// separated ranks, and a single 'w'/'b' side-to-move field. This lets the player report
    /// <see cref="AiMoveSelectionStatus.InvalidPosition"/> for malformed input independently of
    /// whether the caller happened to supply an empty legal-move list.
    /// </summary>
    private static bool IsValidFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return false;
        }

        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
        {
            return false;
        }

        if (fields[0].Split('/').Length != 8)
        {
            return false;
        }

        var side = fields[1];
        return side.Length == 1 && (side[0] is 'w' or 'b');
    }

    /// <summary>
    /// Internal scored-candidate tuple carrying the chosen promotion piece (null for non-
    /// promotion moves) alongside the move and its evaluator score.
    /// </summary>
    private readonly record struct ScoredCandidate(
        LegalMove Move,
        double Score,
        PromotionPieceType? Promotion);
}
