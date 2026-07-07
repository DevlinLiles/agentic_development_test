using ChessMvp.Api.Contracts;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace ChessMvp.Api.Hubs;

/// <summary>
/// Bridges GameService's transport-agnostic <see cref="IGameNotifier"/> seam to SignalR. Takes
/// the already-updated <see cref="Game"/> straight from GameService so it never needs a redundant
/// re-read from the database. IHubContext&lt;T&gt; is a singleton-safe service, so this notifier
/// can be registered as a singleton too.
/// </summary>
public sealed class SignalRGameNotifier : IGameNotifier
{
    public const string GameStateUpdatedEvent = "GameStateUpdated";

    private readonly IHubContext<GameHub> _hubContext;

    public SignalRGameNotifier(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyGameUpdatedAsync(Game game)
    {
        // Broadcast state is neutral (no yourColor) - each client already knows its own color
        // from the REST responses it received when creating/joining the game.
        var payload = GameStateResponse.FromGame(game, yourColor: null);

        return _hubContext.Clients
            .Group(game.Id.ToString())
            .SendAsync(GameStateUpdatedEvent, payload);
    }
}
