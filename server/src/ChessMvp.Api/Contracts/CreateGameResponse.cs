using System.Text.Json.Serialization;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record CreateGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    string JoinUrl,
    GameStateResponse GameState)
{
    /// <summary>
    /// How the second seat of the created game is filled ("Human" or "Computer" on the wire,
    /// serialized as a string like every other enum on this contract via the shared
    /// JsonStringEnumConverter). Always populated by this server version and always echoing
    /// the mode the game was actually created with.
    ///
    /// Added as an init-only property rather than a positional parameter so any caller that
    /// constructs this record with the previous positional layout keeps compiling.
    ///
    /// The C# property name matches the entity discriminator ("OpponentMode"), but the wire
    /// name is pinned to "opponentType" — the field name the client contract
    /// (client/src/types/gameTypes.ts, the declared single source of truth for this API)
    /// already declares on CreateGameResponse, so an already-deployed client needs no change
    /// to read it.
    /// </summary>
    [JsonPropertyName("opponentType")]
    public OpponentMode OpponentMode { get; init; }
}

public sealed record JoinGameResponse(
    Guid GameId,
    Guid PlayerToken,
    PlayerColor Color,
    GameStateResponse GameState);
