using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.ChessAi;

/// <summary>
/// Adapts the AI layer's <see cref="IChessAiPlayer"/> to the domain-layer
/// <see cref="IGameAiResponder"/> seam so <c>GameService</c> can orchestrate an automated reply
/// using only domain chess primitives and stay free of a dependency on this project.
/// </summary>
/// <remarks>
/// <para>
/// The legal-move set required by <see cref="IChessAiPlayer.SelectMove"/> is obtained from
/// <see cref="IChessRulesEngine.GetAllLegalMoves"/> so the adapter is the single place that
/// knows how to feed the AI player. The AI player's own contract requires it to choose only from
/// that set, so any move returned is guaranteed legal.
/// </para>
/// <para>
/// The AI player reports a no-legal-moves position (checkmate/stalemate), an invalid position,
/// or an engine failure via <see cref="AiMoveSelection.Status"/> rather than by throwing. All
/// of those non-<see cref="AiMoveSelectionStatus.MoveSelected"/> outcomes are flattened to a
/// <c>null</c> reply here, letting the caller treat "nothing to play" uniformly.
/// </para>
/// </remarks>
public sealed class ChessAiResponder : IGameAiResponder
{
    private readonly IChessAiPlayer _aiPlayer;
    private readonly IChessRulesEngine _rulesEngine;

    public ChessAiResponder(IChessAiPlayer aiPlayer, IChessRulesEngine rulesEngine)
    {
        _aiPlayer = aiPlayer;
        _rulesEngine = rulesEngine;
    }

    /// <inheritdoc/>
    public AiReplyMove? SelectReply(string fen, PlayerColor sideToMove)
    {
        var legalMoves = _rulesEngine.GetAllLegalMoves(fen);
        if (legalMoves.Count == 0)
        {
            return null;
        }

        var selection = _aiPlayer.SelectMove(fen, sideToMove, legalMoves);

        if (selection.Status != AiMoveSelectionStatus.MoveSelected || selection.Move is null)
        {
            return null;
        }

        var move = selection.Move.Move;
        return new AiReplyMove(move.FromSquare, move.ToSquare, selection.Move.PromotionPiece);
    }
}
