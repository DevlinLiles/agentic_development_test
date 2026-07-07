namespace ChessMvp.Domain.Abstractions;

public enum MoveFailureReason
{
    InvalidFen,
    WrongSideToMove,
    IllegalMove,
}
