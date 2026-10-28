using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The only seam through which third-party chess move-generation/validation logic may be reached.
/// Implementations are stateless and FEN-in/FEN-out so they can be used freely in a stateless API.
/// </summary>
public interface IChessRulesEngine
{
    MoveApplicationResult TryApplyMove(
        string fen,
        PlayerColor sideToMove,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion);

    IReadOnlySet<string> GetLegalDestinations(string fen, string fromSquare);

    bool IsPromotionMove(string fen, string fromSquare, string toSquare);

    /// <summary>
    /// Enumerates every legal move available to <paramref name="sideToMove"/> in the position
    /// described by <paramref name="fen"/>, including its SAN and the terminal-state flags
    /// (check, checkmate, stalemate, fifty-move draw) the move would produce.
    /// </summary>
    /// <remarks>
    /// Stateless and side-effect free: the input position is never mutated. Promotion-piece
    /// selection is deferred to <see cref="TryApplyMove"/>, so a promotion is reported once per
    /// from/to pair. Positions that cannot be parsed yield an empty enumeration rather than
    /// throwing, matching <see cref="GetLegalDestinations"/>.
    /// </remarks>
    IReadOnlyList<RulesLegalMove> GetAllLegalMoves(string fen, PlayerColor sideToMove);
}
