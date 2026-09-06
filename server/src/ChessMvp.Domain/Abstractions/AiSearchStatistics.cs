namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Non-deterministic telemetry produced during an AI search (elapsed time, nodes visited). These
/// values vary between runs even for identical inputs and therefore live outside the deterministic
/// portion of <see cref="AiMoveResult"/> (in the nullable <see cref="AiMoveResult.Statistics"/>
/// field) so that equality checks in tests remain reproducible.
/// </summary>
public sealed record AiSearchStatistics
{
    /// <summary>
    /// Number of nodes evaluated during the search, when reported by the implementation.
    /// </summary>
    public long NodesEvaluated { get; init; }

    /// <summary>Wall-clock time spent searching.</summary>
    public TimeSpan Elapsed { get; init; }
}
