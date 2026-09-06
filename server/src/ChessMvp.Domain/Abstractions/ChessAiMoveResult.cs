using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The move an <see cref="IChessAiPlayer"/> chose to play, together with the evaluation that
/// produced the choice.
/// </summary>
public sealed record ChessAiMoveResult
{
    /// <summary>The square the chosen piece departs from, in algebraic notation (e.g. "e2").</summary>
    public required string FromSquare { get; init; }

    /// <summary>The square the chosen piece lands on, in algebraic notation (e.g. "e4").</summary>
    public required string ToSquare { get; init; }

    /// <summary>
    /// The promotion piece to apply when the chosen move is a promotion, or <see langword="null"/>
    /// for non-promotion moves.
    /// </summary>
    public PromotionPieceType? Promotion { get; init; }

    /// <summary>
    /// The player's evaluation of the chosen move, expressed from the perspective of the side to
    /// move. Higher values indicate a stronger move. The scale is implementation-defined; this is
    /// a confidence/quality signal rather than a normalised probability.
    /// </summary>
    public double Evaluation { get; init; }
}
