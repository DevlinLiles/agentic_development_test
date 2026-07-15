using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The kind of move a <see cref="LegalMove"/> represents. Used so callers (and external
/// verification such as perft) can distinguish the special moves from ordinary ones without
/// re-deriving them from the geometry of the from/to squares.
/// </summary>
public enum MoveKind
{
    /// <summary>An ordinary quiet move or capture.</summary>
    Normal,

    /// <summary>A king castling two squares toward a rook.</summary>
    Castling,

    /// <summary>A pawn capturing an enemy pawn "in passing" on the en-passant target square.</summary>
    EnPassant,

    /// <summary>A pawn reaching the last rank and promoting to a piece.</summary>
    Promotion,
}

/// <summary>
/// A single legal move for the side to move, as returned by
/// <see cref="IChessRulesEngine.GetLegalMoves"/>. The move is fully identified by its from/to
/// squares together with <see cref="Promotion"/> (which is non-null only for promotion moves).
/// <see cref="Kind"/> is a convenience classification; the from/to/promotion triple is the
/// authoritative description.
/// </summary>
public sealed record LegalMove(
    string FromSquare,
    string ToSquare,
    PromotionPieceType? Promotion = null,
    MoveKind Kind = MoveKind.Normal);
