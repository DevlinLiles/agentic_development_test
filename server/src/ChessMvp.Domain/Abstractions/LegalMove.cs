namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A single legal move produced by <see cref="IChessRulesEngine.GetAllLegalMoves"/>. It is a
/// lightweight, FEN-derived view of a move (independent of the persisted <c>Move</c> entity) so
/// callers can enumerate options without round-tripping through the move-application path.
/// </summary>
public sealed record LegalMove
{
    /// <summary>The square the moving piece departs from, in algebraic notation (e.g. "e2").</summary>
    public required string FromSquare { get; init; }

    /// <summary>The square the moving piece lands on, in algebraic notation (e.g. "e4").</summary>
    public required string ToSquare { get; init; }

    /// <summary>Standard Algebraic Notation for the move, when available from the engine.</summary>
    public string? San { get; init; }

    /// <summary>
    /// True when the move is a pawn reaching the back rank and therefore requires a promotion
    /// piece selection before it can be applied.
    /// </summary>
    public bool IsPromotion { get; init; }

    /// <summary>True when the move delivers check to the opposing king.</summary>
    public bool IsCheck { get; init; }

    /// <summary>True when the move delivers checkmate (ends the game).</summary>
    public bool IsCheckmate { get; init; }
}
