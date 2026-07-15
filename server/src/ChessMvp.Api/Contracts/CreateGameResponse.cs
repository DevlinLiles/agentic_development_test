using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record CreateGameRequest(GameOpponent? Opponent);

public sealed record CreateGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    string JoinUrl,
    bool IsVsAi,
    GameStateResponse GameState);

public sealed record JoinGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameStateResponse GameState);
