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
    /// Returns every legal move for the side to move in the given position. The FEN already
    /// encodes whose turn it is; <paramref name="sideToMove"/> is supplied so callers can guard
    /// against querying the wrong side, and implementations must filter to that side.
    /// </summary>
    IReadOnlyList<LegalMove> GetAllLegalMoves(string fen, PlayerColor sideToMove);
}
