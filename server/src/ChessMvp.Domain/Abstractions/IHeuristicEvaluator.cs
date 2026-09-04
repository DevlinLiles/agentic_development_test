using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Deterministic static evaluation of a chess position from the perspective of a given side.
/// The returned score combines material values, piece-square table bonuses, and a mobility
/// term; a higher score is better for <paramref name="color"/>. Implementations are stateless
/// and side-relative: evaluating the same position for the opposite color yields the negation
/// of the score (modulo the mobility term, which always favours the evaluated side).
/// </summary>
public interface IHeuristicEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="board"/> (a FEN string) from <paramref name="color"/>'s
    /// perspective and returns a comparable integer score.
    /// </summary>
    int Evaluate(string board, PlayerColor color);
}
