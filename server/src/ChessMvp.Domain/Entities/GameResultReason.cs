namespace ChessMvp.Domain.Entities;

public enum GameResultReason
{
    Checkmate,
    Stalemate,
    FiftyMoveRule,

    // Reserved for a future phase; resignation negotiation is out of scope for the MVP.
    Resignation,
}
