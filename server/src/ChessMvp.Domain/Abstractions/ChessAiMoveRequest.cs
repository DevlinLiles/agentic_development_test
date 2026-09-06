using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The board state handed to an <see cref="IChessAiPlayer"/> when asking it to pick a move.
/// </summary>
public sealed record ChessAiMoveRequest
{
    /// <summary>The current board position, in FEN notation.</summary>
    public required string Fen { get; init; }

    /// <summary>The side that is to move from the current position.</summary>
    public required PlayerColor SideToMove { get; init; }

    /// <summary>
    /// The complete set of legal moves available to <see cref="SideToMove"/> in the current
    /// position, as produced by <see cref="IChessRulesEngine.GetAllLegalMoves"/>. The chosen move
    /// must be drawn from this collection.
    /// </summary>
    public required IReadOnlyList<LegalMove> LegalMoves { get; init; }
}
