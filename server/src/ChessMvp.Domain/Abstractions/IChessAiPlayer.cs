using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The seam through which an automated chess opponent ("AI player") is asked to choose a move for
/// a given position. This is the single abstraction the game flow consults when a game's turn
/// belongs to an AI side (<see cref="GameMode.VsAi"/>); concrete engines live behind it so the
/// domain and API never depend on a particular chess-AI library.
/// </summary>
/// <remarks>
/// Implementations are expected to be FEN-in/move-out and otherwise stateless, mirroring
/// <see cref="IChessRulesEngine"/>, so they can be used freely in a stateless API. The contract is
/// intentionally minimal: one method that accepts the board/game state and returns the chosen move
/// plus optional diagnostics. No implementation or DI registration is included here — that is the
/// responsibility of the consuming composition root (e.g. an Infrastructure project).
/// </remarks>
public interface IChessAiPlayer
{
    /// <summary>
    /// Asks the AI to choose a move for the position described by <paramref name="fen"/> with
    /// <paramref name="sideToMove"/> to move.
    /// </summary>
    /// <param name="fen">The current position in FEN. Implementations may treat an invalid FEN as
    /// an abstention (a <see cref="AiMoveResult"/> with <see cref="AiMoveResult.Success"/> false)
    /// rather than throwing, consistent with the rules engine's lenient FEN handling.</param>
    /// <param name="sideToMove">The color whose move is being requested.</param>
    /// <param name="cancellationToken">Propagates cancellation so callers can bound thinking
    /// time; an implementation may surface this as a <see cref="AiMoveResult"/> failure instead of
    /// throwing.</param>
    /// <returns>An <see cref="AiMoveResult"/> carrying the chosen move and optional diagnostics,
    /// or a failure when the AI cannot produce a move.</returns>
    Task<AiMoveResult> ChooseMoveAsync(
        string fen,
        PlayerColor sideToMove,
        CancellationToken cancellationToken = default);
}
