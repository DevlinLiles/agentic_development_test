using ChessMvp.Domain.Abstractions;

namespace ChessMvp.Domain.Ai;

/// <summary>
/// A pure, deterministic default implementation of <see cref="IHeuristicEvaluator"/> that scores a
/// position purely on material balance from White's perspective. Each piece contributes a fixed
/// pawn-unit value (P=1, N=3, B=3, R=5, Q=9) with White pieces positive and Black pieces negative.
/// The score is computed solely from the board half of the FEN, so a given FEN always yields the
/// same score and the evaluator is safe to share as a singleton.
/// </summary>
public sealed class MaterialHeuristicEvaluator : IHeuristicEvaluator
{
    /// <inheritdoc/>
    public double Evaluate(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentException("A FEN string is required.", nameof(fen));
        }

        var board = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        double score = 0;
        foreach (var ch in board)
        {
            score += ch switch
            {
                'P' => 1,
                'N' => 3,
                'B' => 3,
                'R' => 5,
                'Q' => 9,
                'p' => -1,
                'n' => -3,
                'b' => -3,
                'r' => -5,
                'q' => -9,
                _ => 0,
            };
        }

        return score;
    }
}
