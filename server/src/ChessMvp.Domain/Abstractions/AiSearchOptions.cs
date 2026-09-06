namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Configures the bounds of an <see cref="IChessAiPlayer"/> search without referencing any
/// particular heuristic, keeping the player contract decoupled from concrete evaluation logic.
/// Bounds are advisory: an implementation applies whichever it supports.
/// </summary>
public sealed record AiSearchOptions
{
    /// <summary>
    /// Maximum search depth in plies. When <c>null</c> the implementation chooses a default.
    /// </summary>
    public int? MaxDepthInPlies { get; init; }

    /// <summary>
    /// Soft wall-clock time budget for the search. When <c>null</c> the implementation chooses a
    /// default. Independent of the <see cref="CancellationToken"/> passed to
    /// <see cref="IChessAiPlayer.SelectMoveAsync"/>; either may terminate the search.
    /// </summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>
    /// When <c>true</c> (the default), the implementation should prefer reproducible results over
    /// time-limited strength so that identical inputs yield identical outputs. Useful for tests and
    /// deterministic replay.
    /// </summary>
    public bool Deterministic { get; init; } = true;

    /// <summary>
    /// Convenience preset: a shallow, fast, deterministic search suitable for tests and casual
    /// play. Defaults to a two-ply search.
    /// </summary>
    public static AiSearchOptions Shallow(int depthInPlies = 2) =>
        new() { MaxDepthInPlies = depthInPlies, Deterministic = true };

    /// <summary>
    /// Convenience preset: an unbounded-depth deterministic search that completes only when the
    /// search tree is exhausted or the cancellation token fires.
    /// </summary>
    public static AiSearchOptions FullDepth() =>
        new() { MaxDepthInPlies = null, Deterministic = true };
}
