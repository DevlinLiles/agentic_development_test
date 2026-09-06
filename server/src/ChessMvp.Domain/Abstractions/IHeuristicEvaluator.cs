namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Evaluates a chess position and returns a static heuristic score, decoupling position
/// assessment from the search strategy of an <see cref="IChessAiPlayer"/>. Implementations must be
/// pure and deterministic: a given FEN always yields the same score, so that a greedy search over
/// a fixed move list is reproducible across runs.
/// </summary>
public interface IHeuristicEvaluator
{
    /// <summary>
    /// Scores the position described by <paramref name="fen"/>.
    /// </summary>
    /// <param name="fen">The board position in Forsyth-Edwards Notation.</param>
    /// <returns>
    /// A heuristic score in pawn units from White's perspective: positive values favor White and
    /// negative values favor Black. The magnitude and exact composition are implementation-defined.
    /// </returns>
    double Evaluate(string fen);
}
