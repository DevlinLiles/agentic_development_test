using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A proposed reply move for an automated (AI) seat, expressed purely in domain chess
/// primitives so <c>GameService</c> can orchestrate it without depending on any concrete AI
/// implementation. <see cref="PromotionPiece"/> is the promotion piece the engine chose when
/// the move is a promotion, or <c>null</c> for non-promotion moves.
/// </summary>
public sealed record AiReplyMove(
    string FromSquare,
    string ToSquare,
    PromotionPieceType? PromotionPiece);

/// <summary>
/// Orchestration seam through which <c>GameService</c> asks for an automated reply move for the
/// side to move in a given position. This deliberately returns domain-only types: the concrete
/// AI player (<c>IChessAiPlayer</c>) lives in the <c>ChessMvp.ChessAi</c> layer, which already
/// depends on <c>ChessMvp.Domain</c>, so the Domain layer cannot reference it directly without
/// creating a circular dependency. Instead an adapter in the AI layer implements this interface
/// and is injected at the composition root, mirroring the <see cref="IGameNotifier"/> /
/// <see cref="IChessRulesEngine"/> seams.
/// </summary>
/// <remarks>
/// Implementations must NOT throw for the no-legal-moves case: return <c>null</c> instead so the
/// caller can simply skip the reply (the position is terminal and the game will already have
/// been ended by the endgame-detection logic that ran on the preceding human move).
/// </remarks>
public interface IGameAiResponder
{
    /// <summary>
    /// Selects a reply move for <paramref name="sideToMove"/> from the position described by
    /// <paramref name="fen"/>, or returns <c>null</c> when there is no move to make (no legal
    /// moves, invalid position, or engine failure).
    /// </summary>
    /// <param name="fen">The board state in Forsyth–Edwards Notation.</param>
    /// <param name="sideToMove">The color whose move is to be selected.</param>
    /// <returns>
    /// An <see cref="AiReplyMove"/> describing the chosen move, or <c>null</c> if no reply is
    /// available.
    /// </returns>
    AiReplyMove? SelectReply(string fen, PlayerColor sideToMove);
}
