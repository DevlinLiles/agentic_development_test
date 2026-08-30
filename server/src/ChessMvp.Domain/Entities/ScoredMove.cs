namespace ChessMvp.Domain.Entities;

/// <summary>
/// A legal move scored by the heuristic move-selector's combined evaluation. The total
/// <see cref="Score"/> is the sum of the capture-stage <see cref="MaterialGain"/> (victim value
/// minus aggressor value, zero for non-captures) and the heuristic tie-breakers: a
/// <see cref="CheckBonus"/> for moves that give check, a <see cref="DevelopmentBonus"/> for
/// non-pawn/non-king pieces leaving their starting area, a <see cref="CentralControlBonus"/> for
/// moves that improve occupation or influence near the center, and a
/// <see cref="QueenPromotionBonus"/> that always prefers a queen promotion over an
/// under-promotion. The tie-breakers are small relative to a pawn (100) so they only decide
/// between moves of equal <see cref="MaterialGain"/>.
/// </summary>
public sealed record ScoredMove
{
    public required LegalMove Move { get; init; }

    public required int MaterialGain { get; init; }

    public required int CheckBonus { get; init; }

    public required int DevelopmentBonus { get; init; }

    public required int CentralControlBonus { get; init; }

    public required int QueenPromotionBonus { get; init; }

    /// <summary>
    /// The combined score: <see cref="MaterialGain"/> plus every tie-breaker bonus. The move
    /// with the highest <see cref="Score"/> is selected.
    /// </summary>
    public required int Score { get; init; }
}
