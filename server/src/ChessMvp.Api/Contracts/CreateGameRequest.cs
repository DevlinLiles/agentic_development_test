using System.Text.Json.Serialization;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

/// <summary>
/// Optional body of POST /api/games: selects how the second seat of the new game is filled.
///
/// The whole body is optional (see <c>GamesController.CreateGame</c>): a request with no body
/// at all — the only call shape that existed before opponent selection — or a body that simply
/// doesn't mention the opponent both mean the classic two-player game, which reproduces the
/// pre-existing behavior exactly.
///
/// The wire field name is pinned to "opponent", matching the client contract declared in
/// <c>client/src/types/gameTypes.ts</c> (<c>CreateGameRequest</c>), the documented single
/// source of truth for this API.
/// </summary>
public sealed record CreateGameRequest
{
    /// <summary>
    /// "Human" (or omitted/null): a two-player game created <see cref="GameStatus.WaitingForPlayer2"/>,
    /// whose second seat is filled later through the shareable join link.
    ///
    /// "Computer": a solo game against the server-side AI, created already
    /// <see cref="GameStatus.Active"/> with the AI seated on Black from the start, so the game
    /// is immediately playable and can never be joined by a second human.
    ///
    /// Nullable so that "not specified" is distinguishable from an explicit choice; the
    /// controller resolves null to <see cref="OpponentMode.Human"/>.
    /// </summary>
    [JsonPropertyName("opponent")]
    public OpponentMode? Opponent { get; init; }
}
