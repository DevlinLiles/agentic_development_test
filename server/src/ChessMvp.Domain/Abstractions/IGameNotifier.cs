using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Broadcast seam invoked by <c>GameService</c> after a successful state mutation. The real-time
/// transport (SignalR) is wired up in ChessMvp.Api so ChessMvp.Domain has no dependency on it.
/// </summary>
public interface IGameNotifier
{
    Task NotifyGameUpdatedAsync(Game game);
}
