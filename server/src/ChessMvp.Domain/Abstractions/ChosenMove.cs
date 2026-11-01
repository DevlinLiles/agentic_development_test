using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The move an <see cref="IChessAi"/> chose for one position: exactly what the caller needs to
/// play it through <see cref="IChessRulesEngine.TryApplyMove"/> and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// The record deliberately carries no score, depth, node count or any other evaluation
/// vocabulary. Those are properties of whichever implementation produced the move; exposing
/// them here would leak the heuristic through the seam and force every future (minimax)
/// implementation to fabricate comparable numbers it may not have.
/// </para>
/// <para>
/// Promotion is resolved rather than deferred, unlike in <see cref="RulesLegalMove"/>: the
/// chosen move is always complete, so the caller passes <see cref="PromotionPiece"/> straight
/// through and never has to pick a promotion piece on the AI's behalf. It is non-null exactly
/// when the selected from/to pair is a promotion.
/// </para>
/// </remarks>
public sealed record ChosenMove
{
    /// <summary>The square the move starts from, e.g. "e2".</summary>
    public required string FromSquare { get; init; }

    /// <summary>The square the move ends on, e.g. "e4".</summary>
    public required string ToSquare { get; init; }

    /// <summary>
    /// The promotion piece when the selected from/to pair promotes, otherwise
    /// <see langword="null"/>. Never <see langword="null"/> for a promotion: the AI has already
    /// decided.
    /// </summary>
    public PromotionPieceType? PromotionPiece { get; init; }
}
