namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The outcome category of an <see cref="IChessAiPlayer.SelectMoveAsync"/> request.
/// </summary>
public enum AiMoveStatus
{
    /// <summary>A move was selected and is described by the result's move fields.</summary>
    MoveSelected,

    /// <summary>
    /// The side to move has no legal moves (checkmate/stalemate); no move is produced.
    /// </summary>
    NoLegalMoves,

    /// <summary>
    /// The search was cancelled before completing. The move fields may still carry a partial
    /// best-so-far move, but the result must not be treated as a completed selection.
    /// </summary>
    Cancelled,
}
