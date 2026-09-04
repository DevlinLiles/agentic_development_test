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
    /// Enumerates every legal move for the side to move in the given position. Each move is
    /// projected onto <see cref="LegalMove"/>, including promotion detection so callers can
    /// decide whether to prompt for a promotion piece. Returns an empty collection when the FEN
    /// is invalid (consistent with <see cref="GetLegalDestinations"/>).
    /// </summary>
    IReadOnlyList<LegalMove> GetAllLegalMoves(string fen);
}
