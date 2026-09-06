using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Ai;

/// <summary>
/// A stateless automated chess opponent that performs a deterministic 1-ply greedy search. For the
/// side to move it enumerates every legal move, applies each one to obtain the resulting position,
/// and asks the injected <see cref="IHeuristicEvaluator"/> for a static score. The move that
/// produces the best score (from the mover's perspective) is selected; ties are broken
/// deterministically so identical inputs always yield identical outputs.
/// </summary>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IHeuristicEvaluator _evaluator;

    // Promotion candidates evaluated in a fixed order so the tie-break among promotion pieces is
    // reproducible. The piece yielding the highest score is chosen; within a promotion square any
    // residual tie resolves to this declared order (Queen first).
    private static readonly PromotionPieceType[] PromotionCandidates =
    {
        PromotionPieceType.Queen,
        PromotionPieceType.Rook,
        PromotionPieceType.Bishop,
        PromotionPieceType.Knight,
    };

    public HeuristicChessAiPlayer(IChessRulesEngine rulesEngine, IHeuristicEvaluator evaluator)
    {
        _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <inheritdoc/>
    public Task<AiMoveResult> SelectMoveAsync(
        string fen,
        PlayerColor sideToMove,
        AiSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentException("A FEN string is required.", nameof(fen));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var legalMoves = _rulesEngine.GetAllLegalMoves(fen);
        if (legalMoves is null || legalMoves.Count == 0)
        {
            return Task.FromResult(AiMoveResult.NoLegalMoves());
        }

        var candidates = ScoredCandidatesForMoves(fen, sideToMove, legalMoves, cancellationToken);
        if (candidates.Count == 0)
        {
            return Task.FromResult(AiMoveResult.NoLegalMoves());
        }

        // Deterministic selection: highest score first, then by SAN, then by from/to square so the
        // result is reproducible regardless of dictionary or move-enumeration order.
        var best = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.San, StringComparer.Ordinal)
            .ThenBy(c => c.FromSquare, StringComparer.Ordinal)
            .ThenBy(c => c.ToSquare, StringComparer.Ordinal)
            .ThenBy(c => c.Promotion.HasValue ? (int)c.Promotion.Value : -1)
            .First();

        return Task.FromResult(new AiMoveResult
        {
            Status = AiMoveStatus.MoveSelected,
            FromSquare = best.FromSquare,
            ToSquare = best.ToSquare,
            Promotion = best.Promotion,
            San = best.San,
            EvaluationScore = best.Score,
            SearchDepthInPlies = 1,
            PrincipalVariation = best.San ?? string.Empty,
        });
    }

    private List<ScoredCandidate> ScoredCandidatesForMoves(
        string fen,
        PlayerColor sideToMove,
        IReadOnlyList<LegalMove> legalMoves,
        CancellationToken cancellationToken)
    {
        var results = new List<ScoredCandidate>(legalMoves.Count);

        foreach (var move in legalMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (move.IsPromotion)
            {
                // Try each promotion piece and keep the one that scores best from the mover's
                // perspective. The residual tie resolves to PromotionCandidates order (Queen first).
                PromotionPieceType? bestPiece = null;
                var bestPieceScore = double.NegativeInfinity;
                string? bestPieceSan = null;

                foreach (var piece in PromotionCandidates)
                {
                    var application = _rulesEngine.TryApplyMove(
                        fen, sideToMove, move.FromSquare, move.ToSquare, piece);

                    if (!application.IsLegal || application.ResultingFen is null)
                    {
                        continue;
                    }

                    var score = ScoreForSide(_evaluator.Evaluate(application.ResultingFen), sideToMove);
                    if (score > bestPieceScore)
                    {
                        bestPieceScore = score;
                        bestPiece = piece;
                        bestPieceSan = application.San;
                    }
                }

                if (bestPiece.HasValue)
                {
                    results.Add(new ScoredCandidate
                    {
                        FromSquare = move.FromSquare,
                        ToSquare = move.ToSquare,
                        Promotion = bestPiece,
                        San = bestPieceSan ?? move.San,
                        Score = bestPieceScore,
                    });
                }
            }
            else
            {
                var application = _rulesEngine.TryApplyMove(
                    fen, sideToMove, move.FromSquare, move.ToSquare, null);

                if (!application.IsLegal || application.ResultingFen is null)
                {
                    continue;
                }

                results.Add(new ScoredCandidate
                {
                    FromSquare = move.FromSquare,
                    ToSquare = move.ToSquare,
                    Promotion = null,
                    San = application.San ?? move.San,
                    Score = ScoreForSide(_evaluator.Evaluate(application.ResultingFen), sideToMove),
                });
            }
        }

        return results;
    }

    // The evaluator reports scores from White's perspective. For the mover we want the score from
    // the mover's perspective, so negate it when the mover is Black.
    private static double ScoreForSide(double whitePerspectiveScore, PlayerColor sideToMove) =>
        sideToMove == PlayerColor.White ? whitePerspectiveScore : -whitePerspectiveScore;

    private sealed record ScoredCandidate
    {
        public required string FromSquare { get; init; }
        public required string ToSquare { get; init; }
        public PromotionPieceType? Promotion { get; init; }
        public string? San { get; init; }
        public double Score { get; init; }
    }
}
