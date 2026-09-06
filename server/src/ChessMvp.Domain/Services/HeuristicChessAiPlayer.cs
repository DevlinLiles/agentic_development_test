using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

/// <summary>
/// A 1-ply greedy chess AI. For every legal move it applies the move to a fresh board derived
/// from the request FEN, scores the resulting position with <see cref="IChessBoardEvaluator"/>,
/// and selects the move that maximises the side-to-move's advantage. Because the evaluator
/// reports from the perspective of whoever is on the move in the resulting position (i.e. the
/// opponent after our move), the returned score is negated into our own perspective — the
/// standard negamax convention — so that "highest score" always means "best for us".
/// </summary>
/// <remarks>
/// The player is pure and stateless: it never mutates the caller's board. Move application and
/// evaluation both operate on FEN strings (immutable), and <see cref="IChessRulesEngine"/> builds
/// a fresh internal board for every call, so no state can leak back to the caller. Ties between
/// equally scored moves are broken deterministically by ascending move-string ordering
/// ("<c>&lt;from&gt;&lt;to&gt;</c>"), making the choice reproducible for a given position
/// regardless of the order in which the legal moves are enumerated.
/// </remarks>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    private static readonly PromotionPieceType[] PromotionCandidates =
    {
        PromotionPieceType.Queen,
        PromotionPieceType.Rook,
        PromotionPieceType.Bishop,
        PromotionPieceType.Knight,
    };

    private readonly IChessRulesEngine _rulesEngine;
    private readonly IChessBoardEvaluator _evaluator;

    public HeuristicChessAiPlayer(IChessRulesEngine rulesEngine, IChessBoardEvaluator evaluator)
    {
        _rulesEngine = rulesEngine;
        _evaluator = evaluator;
    }

    /// <inheritdoc/>
    public Task<ChessAiMoveResult> ChooseMoveAsync(
        ChessAiMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LegalMoves is null || request.LegalMoves.Count == 0)
        {
            throw new InvalidOperationException(
                "HeuristicChessAiPlayer.ChooseMoveAsync was called with no legal moves; " +
                "the position is terminal and no move can be selected.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string? bestFrom = null;
        string? bestTo = null;
        PromotionPieceType? bestPromotion = null;
        var bestScore = double.NegativeInfinity;
        // The move key is tracked only for deterministic tie-breaking, so the comparison is
        // self-contained and does not depend on the source enumeration order.
        string? bestKey = null;

        foreach (var move in request.LegalMoves)
        {
            PromotionPieceType? promotion;
            string resultingFen;

            if (move.IsPromotion)
            {
                // Resolve the promotion piece deterministically, applying it to a fresh board to
                // obtain the resulting FEN used for evaluation.
                var resolved = ResolvePromotion(
                    request.Fen, request.SideToMove, move.FromSquare, move.ToSquare);
                promotion = resolved.Piece;
                resultingFen = resolved.ResultingFen;
            }
            else
            {
                promotion = null;
                // Apply the move to a fresh board built from the request FEN. The rules engine is
                // FEN-in/FEN-out and stateless, so the caller's board is never touched.
                var application = _rulesEngine.TryApplyMove(
                    request.Fen,
                    request.SideToMove,
                    move.FromSquare,
                    move.ToSquare,
                    null);

                if (!application.IsLegal || application.ResultingFen is null)
                {
                    // Legal moves are produced by the same engine, so this should not happen; skip
                    // defensively rather than propagating an inconsistent state.
                    continue;
                }

                resultingFen = application.ResultingFen;
            }

            // The evaluator scores from the side-to-move's perspective. After our move the side to
            // move is the opponent, so negate to express the score from our perspective.
            var opponentScore = _evaluator.Evaluate(resultingFen);
            var score = -opponentScore;

            var key = MoveKey(move.FromSquare, move.ToSquare);

            // Select the highest score; on an exact tie keep the lexicographically smallest move
            // key so the choice is deterministic regardless of enumeration order.
            if (score > bestScore || (score == bestScore && IsEarlierKey(key, bestKey)))
            {
                bestScore = score;
                bestFrom = move.FromSquare;
                bestTo = move.ToSquare;
                bestPromotion = promotion;
                bestKey = key;
            }
        }

        if (bestFrom is null)
        {
            throw new InvalidOperationException(
                "HeuristicChessAiPlayer could not apply any of the supplied legal moves; " +
                "the rules engine rejected every candidate.");
        }

        return Task.FromResult(new ChessAiMoveResult
        {
            FromSquare = bestFrom,
            ToSquare = bestTo,
            Promotion = bestPromotion,
            Evaluation = bestScore,
        });
    }

    /// <summary>
    /// Determines the promotion piece to play for a promotion move, together with the FEN that
    /// results from applying it. Standard chess permits all four promotion pieces for a pawn
    /// reaching the back rank, so the common path returns <see cref="PromotionPieceType.Queen"/>.
    /// Each candidate is probed deterministically so that, should the rules ever permit exactly
    /// one promotion piece for a given move, that sole legal piece is chosen; in every other case
    /// (zero or multiple legal pieces) the choice defaults to Queen.
    /// </summary>
    private (PromotionPieceType Piece, string ResultingFen) ResolvePromotion(
        string fen,
        PlayerColor sideToMove,
        string fromSquare,
        string toSquare)
    {
        var legalResults = new Dictionary<PromotionPieceType, string>();

        foreach (var candidate in PromotionCandidates)
        {
            var application = _rulesEngine.TryApplyMove(
                fen, sideToMove, fromSquare, toSquare, candidate);

            if (application.IsLegal && application.ResultingFen is not null)
            {
                legalResults[candidate] = application.ResultingFen;
            }
        }

        // Exactly one legal promotion piece: use it.
        if (legalResults.Count == 1)
        {
            var sole = legalResults.Keys.Single();
            return (sole, legalResults[sole]);
        }

        // Otherwise default to Queen (the normal case where all four are legal). If, in some
        // degenerate position, Queen itself were not legal but other pieces were, fall back to the
        // first legal candidate in deterministic order so a playable move is always returned.
        if (legalResults.TryGetValue(PromotionPieceType.Queen, out var queenFen))
        {
            return (PromotionPieceType.Queen, queenFen);
        }

        var fallback = PromotionCandidates.First(c => legalResults.ContainsKey(c));
        return (fallback, legalResults[fallback]);
    }

    private static string MoveKey(string fromSquare, string toSquare) =>
        // Lower-cased so case differences in source-square notation cannot perturb the ordering.
        string.Concat(fromSquare, toSquare).ToLowerInvariant();

    private static bool IsEarlierKey(string candidate, string? current) =>
        // A null current means no key has been recorded yet, so the candidate wins by default.
        current is null || string.CompareOrdinal(candidate, current) < 0;
}
