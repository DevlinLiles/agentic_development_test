using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A stateless chess AI that selects a single legal move for a given board
/// position and side to move using a deterministic heuristic.
/// </summary>
public interface IChessAiPlayer
{
    /// <summary>
    /// Selects the best legal move for the side to move according to the
    /// heuristic. The selection is deterministic for a given position so that
    /// repeated calls with identical inputs yield identical output (a stable
    /// arbitrary legal move when no capture or tie-breaker distinguishes moves).
    /// </summary>
    /// <param name="positionFen">A FEN string describing the board and side to move.</param>
    /// <param name="sideToMove">The color that must move.</param>
    /// <returns>The selected legal move, or <c>null</c> if there are no legal moves.</returns>
    Move? SelectMove(string positionFen, PlayerColor sideToMove);
}
