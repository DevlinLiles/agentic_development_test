using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Selects a move for the server-side AI opponent from a FEN position. Implementations must be
/// deterministic (same fen + sideToMove always yields the same selection), stateless, and
/// side-effect free. Returns null when <paramref name="sideToMove"/> has no legal moves in the
/// position — terminal-position handling is the caller's concern, not the engine's.
/// </summary>
public interface IHeuristicChessEngine
{
    HeuristicMoveSelection? SelectMove(string fen, PlayerColor sideToMove);
}

/// <summary>
/// The AI's chosen move. Promotion-piece selection is NOT deferred here: when the selected
/// from/to pair is a promotion, <see cref="PromotionPiece"/> is always
/// <see cref="PromotionPieceType.Queen"/> (FR-4), so the caller can pass it straight to
/// IChessRulesEngine.TryApplyMove / SubmitMoveAsync.
/// </summary>
public sealed record HeuristicMoveSelection
{
    public required string FromSquare { get; init; }

    public required string ToSquare { get; init; }

    public PromotionPieceType? PromotionPiece { get; init; }
}
