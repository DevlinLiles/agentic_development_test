using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A chess AI opponent. Implementations are stateless and FEN-in/move-out so they can be used
/// freely in a stateless API. The side to move is encoded in the FEN, so no separate colour
/// argument is required.
/// </summary>
public interface IChessAiPlayer
{
    /// <summary>
    /// Chooses a move for the side to move in <paramref name="fen"/>, or null when the position
    /// has no legal moves (checkmate/stalemate).
    /// </summary>
    AiMove? ChooseMove(string fen);
}

/// <summary>
/// A move chosen by an <see cref="IChessAiPlayer"/>: origin square, destination square, and an
/// optional promotion piece (queen by convention when a promotion is chosen).
/// </summary>
public sealed record AiMove(string FromSquare, string ToSquare, PromotionPieceType? Promotion);
