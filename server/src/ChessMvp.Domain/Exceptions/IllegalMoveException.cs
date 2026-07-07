namespace ChessMvp.Domain.Exceptions;

public sealed class IllegalMoveException : Exception
{
    public IllegalMoveException(string message)
        : base(message)
    {
    }
}
