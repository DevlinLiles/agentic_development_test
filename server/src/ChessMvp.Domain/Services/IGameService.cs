using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

public interface IGameService
{
    /// <summary>
    /// Creates and persists a new game.
    /// <para>
    /// <paramref name="opponent"/> selects a human or AI opponent. <paramref name="mode"/> selects
    /// the requesting player's side (the AI takes the opposite seat) and is only meaningful for AI
    /// games — human-vs-human games are unchanged (the creator always plays White and the game
    /// waits for a second player).
    /// </para>
    /// <para>
    /// For AI games the game is created with <see cref="GameStatus.Active"/> and the AI is assigned
    /// to the seat opposite <paramref name="mode"/>; the AI opening move is NOT computed or applied
    /// here.
    /// </para>
    /// </summary>
    Task<Game> CreateGameAsync(GameOpponentType opponent, PlayerColor mode);

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
