using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

public interface IGameService
{
    /// <summary>
    /// Creates a new game, selecting how the second seat is filled.
    ///
    /// <paramref name="opponentMode"/> defaults to <see cref="OpponentMode.Human"/>, which
    /// reproduces the behavior of the pre-existing parameterless call exactly: the game is
    /// stored in <see cref="GameStatus.WaitingForPlayer2"/> with the caller seated as White
    /// and an open Black seat to be filled through the shareable join link.
    ///
    /// <see cref="OpponentMode.Computer"/> instead seats the server-side AI on Black at
    /// creation time: both seats carry a slot token (the human keeps White) and the game is
    /// stored already <see cref="GameStatus.Active"/>, so it never waits for a second player
    /// and can never be joined by one.
    /// </summary>
    Task<Game> CreateGameAsync(OpponentMode opponentMode = OpponentMode.Human);

    /// <summary>
    /// Fills the open Black seat of a waiting two-player game. Rejected with
    /// <see cref="Exceptions.GameNotActiveException"/> when the game is already full, no
    /// longer waiting, or is a computer game whose Black seat belongs to the AI.
    /// </summary>
    Task<Game> JoinGameAsync(Guid gameId);

    Task<Game> GetGameAsync(Guid gameId);

    Task<Game> SubmitMoveAsync(
        Guid gameId,
        Guid slotToken,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion);

    Task<IReadOnlyList<Move>> GetMoveHistoryAsync(Guid gameId);
}
