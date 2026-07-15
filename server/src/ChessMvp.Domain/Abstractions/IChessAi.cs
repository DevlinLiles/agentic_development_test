using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// Seam through which the heuristic computer opponent picks its move. Implementations are
/// stateless and FEN-in/result-out, mirroring <see cref="IChessRulesEngine"/> so they can be
/// used freely in a stateless API and swapped for a stronger engine later without touching
/// <c>GameService</c>.
/// </summary>
public interface IChessAi
{
    /// <summary>
    /// Chooses the AI's next move for the side to move in <paramref name="fen"/>.
    /// Returns <see langword="null"/> when the side to move has no legal moves (checkmate /
    /// stalemate), in which case the caller should leave the game state alone — the rules
    /// engine already classified the terminal position when the move that produced
    /// <paramref name="fen"/> was applied.
    /// </summary>
    AiMove? ChooseMove(string fen);
}

/// <summary>
/// The AI's chosen move. <see cref="Promotion"/> is <see langword="null"/> unless the move is a
/// pawn promotion, in which case the AI always under/over-promotes to the piece it scored best.
/// </summary>
public sealed record AiMove(string FromSquare, string ToSquare, PromotionPieceType? Promotion);
