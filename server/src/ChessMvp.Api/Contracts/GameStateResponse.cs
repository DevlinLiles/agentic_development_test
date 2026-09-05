using ChessMvp.Domain.Entities;

namespace ChessMvp.Api.Contracts;

public sealed record GameStateResponse(
    Guid GameId,
    GameStatus Status,
    string Fen,
    PlayerColor Turn,
    PlayerColor? YourColor,
    GameMode Mode,
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
            Mode: game.Mode,
            Result: game.Result,
            ResultReason: game.ResultReason,
            MoveCount: game.Moves.Count,
            IsCheck: lastMove?.IsCheck ?? false,
            LastMove: lastMove is null
                ? null
                : new LastMoveResponse(lastMove.FromSquare, lastMove.ToSquare, lastMove.San));
    }
}

public sealed record LastMoveResponse(string From, string To, string San);
