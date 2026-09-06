namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A single legal move in a given position, expressed in domain terms and decoupled from the
/// underlying third-party move-generation library. Produced by <see cref="IChessRulesEngine.GetAllLegalMoves"/>.
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
    /// Whether the move is a pawn promotion (a pawn reaching the back rank). Detected via the same
    /// heuristic used by <see cref="IChessRulesEngine.IsPromotionMove"/>.
    /// </summary>
    public bool IsPromotion { get; init; }
}
