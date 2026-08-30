namespace ChessMvp.Domain.Entities;

/// <summary>
/// The kind of chess piece, independent of color. Used for material evaluation in the
/// heuristic move-selector.
/// </summary>
public enum PieceType
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}
