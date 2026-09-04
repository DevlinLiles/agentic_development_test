using ChessMvp.Domain.Abstractions;

namespace ChessMvp.ChessAi;

/// <summary>
/// A move selected by an <see cref="IChessAiPlayer"/> together with the evaluation that
/// produced it. This is the value returned from <see cref="IChessAiPlayer.SelectMove"/> when a
/// legal move exists and is the natural unit of inspection for tests, logging, and analysis
/// tooling.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Move"/> property is a <see cref="LegalMove"/> — the AI is expected to choose
/// from the set of legal moves supplied by the rules engine, so the chosen move reuses that
/// existing value type rather than introducing a parallel representation.
/// </para>
/// <para>
/// <see cref="Score"/> is an opaque, implementation-defined heuristic estimate of the resulting
/// position's favourability for the side that moved. It is expressed from the perspective of the
/// side to move (positive = good for the mover) so that callers can compare candidates from a
/// single engine consistently, and is not comparable across different implementations. It may be
/// <c>0</c> or unused by engines that do not score positions.
/// </para>
/// </remarks>
public sealed record AiMoveResult
{
    /// <summary>
    /// The legal move the player chose to play.
    /// </summary>
    public required LegalMove Move { get; init; }

    /// <summary>
    /// Implementation-defined score of the resulting position from the perspective of the side
    /// that moved (higher is better for the mover). Not comparable across different AI
    /// implementations. May be <c>0</c> when the engine does not produce a numeric evaluation.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Number of candidate moves the engine considered while selecting <see cref="Move"/>, when
    /// known. <c>0</c> indicates the value was not computed/available. Useful for diagnostics
    /// and shallow tests; never relied upon for correctness.
    /// </summary>
    public int ConsideredMoveCount { get; init; }

    /// <summary>
    /// Optional human-readable explanation of the choice (e.g. principal-variation summary,
    /// reason for a blunder). May be <c>null</c>.
    /// </summary>
    public string? Explanation { get; init; }
}
