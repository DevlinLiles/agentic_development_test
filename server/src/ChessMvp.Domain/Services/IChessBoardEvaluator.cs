namespace ChessMvp.Domain.Services;

/// <summary>
/// Scores a chess position deterministically, combining material values, piece-square tables and
/// a mobility term. The implementation is pure: the same position always yields the same score.
/// Scores are returned from the perspective of the side to move (positive favours the side to
/// move, negative favours the opponent) so a heuristic AI can compare candidate positions
/// uniformly during move search.
/// </summary>
public interface IChessBoardEvaluator
{
    /// <summary>
    /// Evaluates the position described by <paramref name="fen"/> and returns a single numeric
    /// score from the side-to-move's perspective.
    /// </summary>
    /// <param name="fen">The FEN string of the position to evaluate.</param>
    /// <returns>An integer score in centipawns from the side-to-move's perspective.</returns>
    int Evaluate(string fen);
}
