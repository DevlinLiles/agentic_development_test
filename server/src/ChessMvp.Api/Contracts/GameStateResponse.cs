using System.Text.Json.Serialization;
using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record GameStateResponse(
    Guid GameId,
    GameStatus Status,
    string Fen,
    PlayerColor Turn,
    PlayerColor? YourColor,
    GameResult? Result,
    GameResultReason? ResultReason,
    int MoveCount,
    bool IsCheck,
    LastMoveResponse? LastMove)
{
    public static GameStateResponse FromGame(Game game, PlayerColor? yourColor)
    {
        var lastMove = game.Moves.Count == 0
            ? null
            : game.Moves.OrderBy(m => m.MoveNumber).Last();

        return new GameStateResponse(
            GameId: game.Id,
            Status: game.Status,
            Fen: game.CurrentFen,
            Turn: game.Turn,
            YourColor: yourColor,
            Result: game.Result,
            ResultReason: game.ResultReason,
            MoveCount: game.Moves.Count,
            IsCheck: lastMove?.IsCheck ?? false,
            LastMove: lastMove is null
                ? null
                : new LastMoveResponse(lastMove.FromSquare, lastMove.ToSquare, lastMove.San))
        {
            // The C# property is "OpponentMode" to match the entity discriminator, but the wire
            // name is pinned to the field the client already reads ("opponentType", see
            // client/src/types/gameTypes.ts, the declared single source of truth for this
            // contract), so an already-deployed client needs no change to receive it.
            //
            // A legacy row (written before the column existed) has a null discriminator; it is
            // normalized to Human here — matching the client's documented "absent means Human"
            // fallback — rather than surfacing a third "unknown" mode on the wire.
            OpponentMode = game.OpponentMode ?? ChessMvp.Domain.Entities.OpponentMode.Human,
        };
    }

    /// <summary>
    /// How the second seat of this game is filled ("Human" or "Computer" on the wire, serialized
    /// as a string like every other enum on this contract via the shared JsonStringEnumConverter).
    /// Always populated by this server version.
    /// </summary>
    [JsonPropertyName("opponentType")]
    public OpponentMode OpponentMode { get; init; }
}

public sealed record LastMoveResponse(string From, string To, string San);
