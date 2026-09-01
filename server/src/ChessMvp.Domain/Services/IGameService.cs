using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Services;

public interface IGameService
{
    // The opponent/mode parameter selects between the original two-human
    // share-link flow (WaitingForPlayer2) and a single-user game against the
    // built-in AI (immediately Active). Defaults to Human so existing callers
    // keep the original behaviour without supplying an argument.
    Task<Game> CreateGameAsync(OpponentType opponentType = OpponentType.Human);

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
