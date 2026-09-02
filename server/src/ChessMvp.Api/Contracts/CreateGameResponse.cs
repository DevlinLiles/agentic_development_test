using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

// The creator always receives a slot token and a colour, so those stay non-nullable. The join URL
// is only meaningful for human-vs-human games (AI games have no second player to share with), so
// it is nullable.
public sealed record CreateGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    string? JoinUrl,
    GameStateResponse GameState);

public sealed record JoinGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameStateResponse GameState);
