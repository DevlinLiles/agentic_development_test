using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The result returned by <see cref="IChessAiPlayer.ChooseMoveAsync"/>: the move the AI elected
/// to play plus optional diagnostics useful for logging, tests, and transparency. This is a
/// lightweight, serializable value type — it carries no persistence concerns and never references
/// the <c>ChessMvp.Domain.Entities.Move</c> aggregate directly, so it can be asserted against in
/// tests and serialized for diagnostics without an EF context.
/// </summary>
public sealed record AiMoveResult
{
    /// <summary>
    /// True when the AI was able to produce a move for the requested position. When false,
    /// <see cref="Move"/> is null and <see cref="Reason"/> describes why (e.g. no legal moves,
    /// invalid FEN). Callers should treat a false result as "the AI declines/abstains".
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The chosen move projected onto <see cref="AiMove"/>, including promotion if any. Null when
    /// <see cref="Success"/> is false.
    /// </summary>
    public AiMove? Move { get; init; }

    /// <summary>
    /// The resulting FEN after applying <see cref="Move"/>, when the AI computes it. Optional:
    /// implementations may leave this null and let the rules engine derive it.
    /// </summary>
    public string? ResultingFen { get; init; }

    /// <summary>
    /// A human-readable reason explaining why <see cref="Success"/> is false, or, when true,
    /// an optional note about the selection (e.g. "forced mate in 1"). Never required for success.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Free-form diagnostics the implementation wishes to surface (e.g. search depth, nodes
    /// evaluated, principal-variation SAN). Optional and intended for logging/tests only; callers
    /// must not depend on any particular shape or presence.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Diagnostics { get; init; }

    public static AiMoveResult Failed(string reason) =>
        new() { Success = false, Reason = reason };
}

/// <summary>
/// The move an AI chose to play, mirroring the lightweight <see cref="LegalMove"/> shape but
/// resolving the promotion piece (since an AI must commit to a promotion, unlike UI move
/// listing). Serializable and self-contained so it can be asserted against directly in tests.
/// </summary>
public sealed record AiMove
{
    /// <summary>The square the moved piece departs from, e.g. "e2".</summary>
    public required string FromSquare { get; init; }

    /// <summary>The square the moved piece arrives on, e.g. "e4".</summary>
    public required string ToSquare { get; init; }

    /// <summary>
    /// The promotion piece when the move is a pawn promotion, otherwise null. Matches
    /// <see cref="IChessRulesEngine.TryApplyMove"/>'s promotion parameter.
    /// </summary>
    public PromotionPieceType? Promotion { get; init; }
}
