namespace ChessMvp.Domain.Exceptions;

public sealed class InvalidSlotTokenException : Exception
{
    public InvalidSlotTokenException()
        : base("The supplied player token does not match either seat in this game.")
    {
    }
}
