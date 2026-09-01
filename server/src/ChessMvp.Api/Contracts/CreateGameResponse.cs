using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record CreateGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    // Relative path for human-opponent games; null for AI-opponent games,
    // which have no second seat to join. Matches the client's `joinUrl: string | null`.
    string? JoinUrl,
    OpponentType OpponentType,
    GameStateResponse GameState);

public sealed record JoinGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameStateResponse GameState);
