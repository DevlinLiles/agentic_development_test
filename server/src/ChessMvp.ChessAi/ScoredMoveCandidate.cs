using ChessMvp.Domain.Abstractions;

namespace ChessMvp.ChessAi;

/// <summary>
/// A single scored candidate considered by an <see cref="IChessAiPlayer"/> while selecting its
/// move. Concrete engines may publish their full candidate list (e.g. for analysis UIs and
/// diagnostic assertions in tests); for engines that do not enumerate candidates the collection
/// will simply contain the single chosen move.
/// </summary>
/// <remarks>
/// <see cref="Score"/> follows the same convention as
/// <see cref="AiMoveResult.Score"/>: expressed from the perspective of the side to move, higher
/// is better, and not comparable across implementations.
/// </remarks>
public sealed record ScoredMoveCandidate
{
    /// <summary>
    /// The legal move this candidate represents.
    /// </summary>
    public required LegalMove Move { get; init; }

    /// <summary>
    /// Implementation-defined score of the position resulting from <see cref="Move"/> from the
    /// perspective of the side to move (higher is better for the mover). Not comparable across
    /// different AI implementations.
    /// </summary>
    public double Score { get; init; }
}
