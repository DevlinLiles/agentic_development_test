using System.Globalization;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessEvaluation;

namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// A 1-ply greedy chess AI. It enumerates every legal move for the side to move, evaluates the
/// position that results from each move with <see cref="HeuristicBoardEvaluator"/> (scored from
/// the mover's perspective), and plays the move yielding the highest score. Ties are broken
/// deterministically by lexicographic ordering of a canonical move string, so the same position
/// always resolves to the same move. Promotion moves are expanded into one candidate per legal
/// promotion piece (queen, rook, bishop, knight) and the best-scoring promotion is committed as
/// part of the chosen move. No deeper search or caching is performed.
/// </summary>
public sealed class HeuristicChessAiPlayer : IChessAiPlayer
{
    /// <summary>
    /// The promotion pieces considered for every promotion move, in evaluation order. The final
    /// selection is driven purely by score and the deterministic tie-break, so this ordering has
    /// no effect on which promotion is chosen, only on iteration.
    /// </summary>
    private static readonly PromotionPieceType?[] PromotionPieces =
    {
        PromotionPieceType.Queen,
        PromotionPieceType.Rook,
        PromotionPieceType.Bishop,
        PromotionPieceType.Knight,
    };

    private readonly IChessRulesEngine _rulesEngine;
    private readonly HeuristicBoardEvaluator _evaluator;

    public HeuristicChessAiPlayer(IChessRulesEngine rulesEngine, HeuristicBoardEvaluator evaluator)
    {
        _rulesEngine = rulesEngine;
        _evaluator = evaluator;
    }

    /// <inheritdoc/>
    public Task<AiMoveResult> ChooseMoveAsync(
        string fen,
        PlayerColor sideToMove,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fen))
        {
            return Task.FromResult(AiMoveResult.Failed("No FEN was provided."));
        }

        var legalMoves = _rulesEngine.GetAllLegalMoves(fen);
        if (legalMoves.Count == 0)
        {
            return Task.FromResult(
                AiMoveResult.Failed("There are no legal moves available for the side to move."));
        }

        Candidate best = default;
        var haveBest = false;

        foreach (var legalMove in legalMoves)
        {
            // A non-promotion move is a single candidate (no promotion piece). A promotion move
            // expands into four candidates — one per legal promotion piece — so the best promotion
            // is selected by score rather than always defaulting to queen.
            PromotionPieceType?[] promotions = legalMove.IsPromotion
                ? PromotionPieces
                : new PromotionPieceType?[] { null };

            foreach (var promotion in promotions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var applied = _rulesEngine.TryApplyMove(
                    fen,
                    sideToMove,
                    legalMove.FromSquare,
                    legalMove.ToSquare,
                    promotion);

                // A move reported legal by GetAllLegalMoves should always apply; skip defensively
                // rather than failing the whole search if the engine ever disagrees.
                if (!applied.IsLegal || applied.ResultingFen is null)
                {
                    continue;
                }

                // Score the resulting position from the mover's perspective: a higher score means a
                // better outcome for the side to move, which is exactly what a greedy 1-ply search
                // maximizes.
                var score = _evaluator.Score(applied.ResultingFen, sideToMove);
                var candidate = new Candidate(
                    legalMove.FromSquare,
                    legalMove.ToSquare,
                    promotion,
                    score,
                    MoveKey(legalMove.FromSquare, legalMove.ToSquare, promotion),
                    applied.ResultingFen);

                if (!haveBest || candidate.IsPreferableOver(best))
                {
                    best = candidate;
                    haveBest = true;
                }
            }
        }

        if (!haveBest)
        {
            return Task.FromResult(AiMoveResult.Failed("No legal move could be applied."));
        }

        return Task.FromResult(new AiMoveResult
        {
            Success = true,
            Move = new AiMove
            {
                FromSquare = best.FromSquare,
                ToSquare = best.ToSquare,
                Promotion = best.Promotion,
            },
            ResultingFen = best.ResultingFen,
            Reason = "1-ply greedy search",
            Diagnostics = new Dictionary<string, string>
            {
                ["strategy"] = "1-ply-greedy",
                ["score"] = best.Score.ToString(CultureInfo.InvariantCulture),
            },
        });
    }

    /// <summary>
    /// Builds the canonical, deterministic key used for lexicographic tie-breaking. The format is
    /// <c>{from}{to}</c> for ordinary moves (e.g. "e2e4") and <c>{from}{to}{promotion}</c> for
    /// promotions (e.g. "e7e8Queen"), so two candidates with equal scores always resolve to the
    /// same move regardless of enumeration order.
    /// </summary>
    private static string MoveKey(string fromSquare, string toSquare, PromotionPieceType? promotion) =>
        promotion is null
            ? $"{fromSquare}{toSquare}"
            : $"{fromSquare}{toSquare}{promotion}";

    /// <summary>
    /// A scored candidate move carried through the 1-ply search. A value type so the inner loop
    /// stays allocation-free; the only heap traffic is the resulting FEN strings produced by the
    /// rules engine.
    /// </summary>
    private readonly record struct Candidate(
        string FromSquare,
        string ToSquare,
        PromotionPieceType? Promotion,
        int Score,
        string Key,
        string ResultingFen)
    {
        /// <summary>
        /// True when this candidate should replace <paramref name="other"/> as the current best:
        /// a higher score wins, and on equal scores the lexicographically smaller move key wins
        /// (deterministic tie-breaking via ordinal string comparison).
        /// </summary>
        public bool IsPreferableOver(in Candidate other)
        {
            if (Score != other.Score)
            {
                return Score > other.Score;
            }

            return string.Compare(Key, other.Key, StringComparison.Ordinal) < 0;
        }
    }
}
