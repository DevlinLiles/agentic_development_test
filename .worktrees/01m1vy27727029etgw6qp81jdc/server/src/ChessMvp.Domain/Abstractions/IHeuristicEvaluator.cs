using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Static position evaluator used by an <see cref="IChessAiPlayer"/> to score a board position
/// from the perspective of a given side. The board is represented as a Forsyth-Edwards Notation
/// (FEN) string, consistent with the rest of the domain contracts (<see cref="IChessRulesEngine"/>
/// and <see cref="IChessAiPlayer"/>), so the evaluator stays decoupled from any particular
/// third-party move-generation library.
/// </summary>
/// <remarks>
/// Implementations should be pure and deterministic: the same board and side must always yield the
/// same score, so results can be asserted for equality in tests. A positive score means the
/// position favors <paramref name="side"/>; a negative score favors the opponent. A symmetric
/// implementation is antisymmetric, i.e. <c>Score(board, White) == -Score(board, Black)</c>.
/// </remarks>
public interface IHeuristicEvaluator
{
    /// <summary>
    /// Scores the position described by <paramref name="board"/> from the perspective of
    /// <paramref name="side"/>.
    /// </summary>
    /// <param name="board">The board position in Forsyth-Edwards Notation.</param>
    /// <param name="side">The color whose perspective the score is computed from.</param>
    /// <returns>
    /// A heuristic score in centipawn-like units. Positive values favor <paramref name="side"/>;
    /// negative values favor the opponent. Returns <c>0</c> when the board cannot be parsed.
    /// </returns>
    int Score(string board, PlayerColor side);
}
