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
    /// Enumerates every legal move for the side to move in <paramref name="fen"/>. Each entry
    /// is a <see cref="LegalMove"/> with its origin/destination squares, SAN, and a flag
    /// indicating whether a promotion piece must be chosen. Returns an empty collection when the
    /// FEN is invalid or there are no legal moves (checkmate/stalemate).
    /// </summary>
    IReadOnlyList<LegalMove> GetAllLegalMoves(string fen);
}
