namespace ChessMvp.Domain.Exceptions;

/// <summary>
/// Covers both "still waiting for player 2" and "already ended" — any state in which the game
/// cannot currently accept a move or a join.
/// </summary>
public sealed class GameNotActiveException : Exception
{
    public GameNotActiveException(string message)
        : base(message)
    {
    }
}
