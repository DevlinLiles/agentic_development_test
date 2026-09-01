namespace ChessMvp.Infrastructure.ChessAi;

/// <summary>
/// Internal piece representation used by the heuristic AI's self-contained
/// board model. Kept separate from any domain/external chess types so the
/// heuristic selector has no hidden dependency on a specific rules engine.
/// </summary>
internal enum Piece
{
    None = 0,

    WhitePawn,
    WhiteKnight,
    WhiteBishop,
    WhiteRook,
    WhiteQueen,
    WhiteKing,

    BlackPawn,
    BlackKnight,
    BlackBishop,
    BlackRook,
    BlackQueen,
    BlackKing,
}
