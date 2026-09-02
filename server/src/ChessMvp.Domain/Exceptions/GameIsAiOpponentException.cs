namespace ChessMvp.Domain.Exceptions;

/// <summary>
/// Thrown when an action that requires a human opponent (e.g. joining a game) is attempted
/// against a game whose opponent is the computer/AI.
/// </summary>
public sealed class GameIsAiOpponentException : Exception
{
    public GameIsAiOpponentException(Guid gameId)
        : base($"Game '{gameId}' is an AI opponent game and cannot be joined by a second player.")
    {
    }
}
