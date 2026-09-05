using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

public interface IGameService
{
    Task<Game> CreateGameAsync();

    Task<Game> CreateGameAsync(GameMode mode);

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
