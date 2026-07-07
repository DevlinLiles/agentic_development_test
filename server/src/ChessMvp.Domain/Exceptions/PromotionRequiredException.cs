namespace ChessMvp.Domain.Exceptions;

public sealed class PromotionRequiredException : Exception
{
    public PromotionRequiredException()
        : base("This move requires a promotion piece to be specified.")
    {
    }
}
