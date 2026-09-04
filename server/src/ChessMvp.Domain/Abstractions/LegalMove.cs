namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A single legal move for the side to move in a given position, as enumerated by
/// <see cref="IChessRulesEngine.GetAllLegalMoves"/>. Unlike the persisted
/// <c>ChessMvp.Domain.Entities.Move</c>, this is a lightweight, stateless projection of a
/// candidate move: just enough to drive UI move-listing/highlighting without committing to a
/// promotion piece (callers resolve the specific promotion piece separately).
/// </summary>
public sealed record LegalMove
{
    /// <summary>The square the moved piece departs from, e.g. "e2".</summary>
    public required string FromSquare { get; init; }

    /// <summary>The square the moved piece arrives on, e.g. "e4".</summary>
    public required string ToSquare { get; init; }

    /// <summary>
    /// True when the move is a pawn promotion (a pawn reaching the back rank). Mirrors the
    /// heuristic in <see cref="IChessRulesEngine.IsPromotionMove"/> so callers can branch on it
    /// without re-querying the engine.
    /// </summary>
    public bool IsPromotion { get; init; }
}
