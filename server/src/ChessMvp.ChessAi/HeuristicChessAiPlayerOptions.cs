using ChessMvp.Domain.Entities;

namespace ChessMvp.ChessAi;

/// <summary>
/// Configuration for <see cref="HeuristicChessAiPlayer"/>. All properties are optional and
/// default to the greedy, queen-promoting behaviour described by the player's contract.
/// </summary>
public sealed record HeuristicChessAiPlayerOptions
{
    /// <summary>
    /// The promotion piece to use for every promotion move, or <c>null</c> to let the player
    /// choose automatically. When <c>null</c> the player defaults to a queen promotion and only
    /// underpromotes to a knight when a knight promotion evaluates to a strictly better resulting
    /// position than the queen promotion (the one underpromotion that is ever tactically useful).
    /// Rook and bishop underpromotions are virtually never correct under a material/positional
    /// heuristic and are therefore not selected automatically, but a caller may force any piece by
    /// setting this property.
    /// </summary>
    public PromotionPieceType? PromotionPiece { get; init; }

    /// <summary>A <see cref="HeuristicChessAiPlayerOptions"/> with all defaults applied.</summary>
    public static HeuristicChessAiPlayerOptions Default { get; } = new();
}
