using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// An automated chess opponent. Given a board state and the legal moves available to the side to
/// move, an implementation chooses a single move to play.
/// </summary>
/// <remarks>
/// This is the only seam through which AI move selection may be reached from the game pipeline.
/// Implementations are free to use any technique (heuristic search, neural evaluation, random
/// selection, remote engines, …); no algorithm is assumed or required by this contract. The
/// interface itself contains no logic — concrete behaviour lives entirely in implementations.
/// </remarks>
public interface IChessAiPlayer
{
    /// <summary>
    /// Chooses a move to play from the position described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The current board, side to move, and legal moves to choose from.</param>
    /// <param name="cancellationToken">Propagates notification that the caller wishes to cancel the selection.</param>
    /// <returns>
    /// The chosen move, its optional promotion piece, and the evaluation that produced the choice.
    /// </returns>
    Task<ChessAiMoveResult> ChooseMoveAsync(
        ChessAiMoveRequest request,
        CancellationToken cancellationToken = default);
}
