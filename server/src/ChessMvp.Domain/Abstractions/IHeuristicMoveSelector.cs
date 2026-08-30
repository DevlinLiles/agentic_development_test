using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Selects a move from a position using lightweight heuristics. The first (capture) stage
/// enumerates every legal move for the side to move, classifies the captures, scores each by
/// material gain (victim value minus aggressor value), and returns the most profitable capture.
/// Non-captures are scored zero and are not selected at this stage. <see cref="SelectBestCapture"/>
/// returns null when the position contains no legal captures.
/// </summary>
public interface IHeuristicMoveSelector
{
    /// <summary>
    /// Returns the legal capture with the highest material gain, or null when no legal capture
    /// exists in <paramref name="fen"/> for <paramref name="sideToMove"/>.
    /// </summary>
    ScoredCapture? SelectBestCapture(string fen, PlayerColor sideToMove);

    /// <summary>
    /// Returns the legal move with the highest combined score for <paramref name="sideToMove"/>
    /// in <paramref name="fen"/>. The combined score adds heuristic tie-breakers — check, piece
    /// development, central control, and a queen-promotion preference — to the capture-stage
    /// material gain, so ties in material gain are resolved in favour of the more positionally
    /// desirable move. Returns null when the position has no legal moves.
    /// </summary>
    ScoredMove? SelectBestMove(string fen, PlayerColor sideToMove);
}
