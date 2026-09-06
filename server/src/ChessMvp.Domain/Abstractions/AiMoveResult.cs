using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The outcome of asking an <see cref="IChessAiPlayer"/> to choose a move. This is a pure,
/// deterministic value type: for a given position and fixed <see cref="AiSearchOptions"/>, every
/// field except <see cref="Statistics"/> is reproducible across runs, so instances can be compared
/// for equality in tests. <see cref="Statistics"/> carries non-deterministic timing/throughput
/// metrics and defaults to <c>null</c>, so it does not interfere with deterministic equality
/// unless explicitly populated.
/// </summary>
public sealed record AiMoveResult
{
    /// <summary>The outcome category of the request.</summary>
    public required AiMoveStatus Status { get; init; }

    /// <summary>
    /// The square the chosen piece departs from, in algebraic notation (e.g. "e2"). Populated when
    /// <see cref="Status"/> is <see cref="AiMoveStatus.MoveSelected"/> (and optionally on
    /// cancellation).
    /// </summary>
    public string? FromSquare { get; init; }

    /// <summary>
    /// The square the chosen piece lands on, in algebraic notation (e.g. "e4"). Populated when
    /// <see cref="Status"/> is <see cref="AiMoveStatus.MoveSelected"/> (and optionally on
    /// cancellation).
    /// </summary>
    public string? ToSquare { get; init; }

    /// <summary>The promotion piece, if the chosen move is a pawn promotion.</summary>
    public PromotionPieceType? Promotion { get; init; }

    /// <summary>Standard Algebraic Notation for the chosen move, when available.</summary>
    public string? San { get; init; }

    /// <summary>
    /// The evaluation of the position after the chosen move, expressed in pawn units from the
    /// perspective of the side that moved (positive favors the mover). Deterministic for a given
    /// position and search configuration.
    /// </summary>
    public double EvaluationScore { get; init; }

    /// <summary>
    /// The search depth reached, in plies, when the move was chosen. Deterministic for a given
    /// position and search configuration.
    /// </summary>
    public int SearchDepthInPlies { get; init; }

    /// <summary>
    /// The principal variation leading from the chosen move, expressed as the sequence of SAN move
    /// strings joined by single spaces (e.g. "e4 e5 Nf3 Nc6"). Stored as a single string so that
    /// value equality is deterministic; empty when no variation is available.
    /// </summary>
    public string PrincipalVariation { get; init; } = string.Empty;

    /// <summary>
    /// Optional, non-deterministic search statistics (nodes visited, elapsed time). Defaults to
    /// <c>null</c> so that deterministic equality comparisons in tests are unaffected; real
    /// implementations may populate it for telemetry.
    /// </summary>
    public AiSearchStatistics? Statistics { get; init; }

    /// <summary>A result indicating the side to move has no legal moves (checkmate/stalemate).</summary>
    public static AiMoveResult NoLegalMoves() =>
        new() { Status = AiMoveStatus.NoLegalMoves };

    /// <summary>A result indicating the search was cancelled with no partial move available.</summary>
    public static AiMoveResult Cancelled() =>
        new() { Status = AiMoveStatus.Cancelled };
}
