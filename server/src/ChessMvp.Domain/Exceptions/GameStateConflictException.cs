namespace ChessMvp.Domain.Exceptions;

/// <summary>
/// Thrown when an optimistic-concurrency check (the <c>Game.Version</c> rowversion) detects that
/// the game was modified by another request between load and save.
/// </summary>
public sealed class GameStateConflictException : Exception
{
    public GameStateConflictException()
        : base("The game state changed before this move could be saved. Reload and retry.")
    {
    }
}
