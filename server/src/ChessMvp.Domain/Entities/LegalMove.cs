namespace ChessMvp.Domain.Entities;

/// <summary>
/// A single fully-legal move for the side to move, expressed in board coordinates. Promotion is
/// populated only when the move is a pawn promotion; otherwise it is null.
/// </summary>
public sealed record LegalMove
{
    public required string FromSquare { get; init; }

    public required string ToSquare { get; init; }

    public PromotionPieceType? Promotion { get; init; }
}
