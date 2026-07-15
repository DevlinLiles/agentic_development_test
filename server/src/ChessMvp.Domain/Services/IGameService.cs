using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

public interface IGameService
{
    Task<Game> CreateGameAsync(GameOpponent opponent = GameOpponent.Human);

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
