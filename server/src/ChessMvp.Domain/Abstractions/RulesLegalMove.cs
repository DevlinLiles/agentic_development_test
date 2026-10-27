namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A single legal move for a position, as enumerated by
/// <see cref="IChessRulesEngine.GetAllLegalMoves(string, Entities.PlayerColor)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="MoveApplicationResult"/>, but is produced without any particular move being
/// chosen: it carries the from/to squares, the SAN of the move, and the terminal-state flags the
/// move would produce if it were played.
/// </para>
/// <para>
/// Promotion-piece selection is deliberately deferred to move-application time (see
/// <see cref="IChessRulesEngine.TryApplyMove"/>), mirroring the underlying library's behaviour: a
/// promotion therefore appears exactly once per from/to pair, and <see cref="San"/> reflects the
/// default promotion piece.
/// </para>
/// </remarks>
public sealed record RulesLegalMove
{
    public required string FromSquare { get; init; }

    public required string ToSquare { get; init; }

    public required string San { get; init; }

    public bool IsCheck { get; init; }

    public bool IsCheckmate { get; init; }

    public bool IsStalemate { get; init; }

    public bool IsFiftyMoveDraw { get; init; }
}
