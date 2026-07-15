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
    /// Returns the complete set of legal moves for the side to move in <paramref name="fen"/>.
    /// Castling, en-passant, and promotion moves are included wherever they are legal, and every
    /// promotion reach is expanded to all four promotion pieces (queen, rook, bishop, knight).
    /// No move that would leave the mover's own king in check is returned (self-check filtering).
    /// Correctness of the full move set is verified externally by the perft procedure rather than
    /// within this method.
    /// </summary>
    IReadOnlyList<LegalMove> GetLegalMoves(string fen);
}
