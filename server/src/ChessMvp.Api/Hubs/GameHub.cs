using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using Microsoft.AspNetCore.SignalR;

namespace ChessMvp.Api.Hubs;

/// <summary>
/// Real-time channel for game state pushes. Clients join a per-game group after presenting the
/// same player token used for REST calls; the hub validates it the same way GameService does.
/// </summary>
public class GameHub : Hub
{
    private readonly IGameService _gameService;

    public GameHub(IGameService gameService)
    {
        _gameService = gameService;
    }

    public async Task JoinGameChannel(Guid gameId, Guid playerToken)
    {
        Game game;
        try
        {
            game = await _gameService.GetGameAsync(gameId);
        }
        catch (GameNotFoundException)
        {
            throw new HubException($"Game '{gameId}' was not found.");
        }

        if (game.WhiteSlotToken != playerToken && game.BlackSlotToken != playerToken)
        {
            throw new HubException("The supplied player token does not match either seat in this game.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, gameId.ToString());
    }
}
