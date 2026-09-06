using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Contract for an automated chess opponent that selects a move for the side to move in a given
/// position. Implementations are free to use any search/evaluation heuristic, but must remain
/// stateless and FEN-in/move-out so the contract can be consumed by UI and service layers without
/// coupling to a particular engine. The returned <see cref="AiMoveResult"/> is a deterministic
/// value type suitable for equality assertions in tests.
/// </summary>
public interface IChessAiPlayer
{
    /// <summary>
    /// Selects the best move available to <paramref name="sideToMove"/> in the position described
    /// by <paramref name="fen"/>, subject to <paramref name="options"/> (search depth/time limits).
    /// </summary>
    /// <param name="fen">The board position in Forsyth-Edwards Notation.</param>
    /// <param name="sideToMove">The color on whose behalf the AI must move.</param>
    /// <param name="options">
    /// Advisory bounds on the search (depth, time budget, determinism). Decoupled from any specific
    /// heuristic so the same contract serves engines of differing strength and strategy.
    /// </param>
    /// <param name="cancellationToken">
    /// Allows the caller to abandon a long search. When cancelled the implementation may either
    /// throw <see cref="OperationCanceledException"/> or return an <see cref="AiMoveResult"/> whose
    /// <see cref="AiMoveResult.Status"/> is <see cref="AiMoveStatus.Cancelled"/> (optionally
    /// carrying the best move found so far).
    /// </param>
    /// <returns>
    /// An <see cref="AiMoveResult"/> describing the chosen move and its evaluation. When the side
    /// to move has no legal moves (checkmate/stalemate) the result's
    /// <see cref="AiMoveResult.Status"/> is <see cref="AiMoveStatus.NoLegalMoves"/>.
    /// </returns>
    Task<AiMoveResult> SelectMoveAsync(
        string fen,
        PlayerColor sideToMove,
        AiSearchOptions options,
        CancellationToken cancellationToken = default);
}
