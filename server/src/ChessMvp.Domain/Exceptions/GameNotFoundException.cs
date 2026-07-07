namespace ChessMvp.Domain.Exceptions;

public sealed class GameNotFoundException : Exception
{
    public GameNotFoundException(Guid gameId)
        : base($"Game '{gameId}' was not found.")
    {
    }
}
