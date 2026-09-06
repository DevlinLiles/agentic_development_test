using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

// JoinUrl is the relative path a second human player would open to join a two-player game. For
// VsAi games there is no second player to join, so it is null rather than a dead link.
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
