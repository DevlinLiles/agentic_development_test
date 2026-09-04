using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record CreateGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameMode Mode,
    string? JoinUrl,
    GameStateResponse GameState);

public sealed record JoinGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameStateResponse GameState);
