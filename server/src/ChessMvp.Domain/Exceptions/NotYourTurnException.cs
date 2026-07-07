namespace ChessMvp.Domain.Exceptions;

public sealed class NotYourTurnException : Exception
{
    public NotYourTurnException()
        : base("It is not this player's turn to move.")
    {
    }
}
