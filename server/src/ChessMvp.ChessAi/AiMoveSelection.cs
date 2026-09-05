namespace ChessMvp.ChessAi;

/// <summary>
/// The outcome of an attempt by an <see cref="IChessAiPlayer"/> to select a move.
/// </summary>
public enum AiMoveSelectionStatus
{
    /// <summary>
    /// A move was selected successfully and is available via <see cref="AiMoveSelection.Move"/>.
    /// </summary>
    MoveSelected,

    /// <summary>
    /// The side to move has no legal moves (checkmate or stalemate). There is no move to return;
    /// the game is over and the caller should consult the rules engine to determine the terminal
    /// result rather than asking the AI for a move.
    /// </summary>
    NoLegalMoves,

    /// <summary>
    /// The input board state was invalid (e.g. malformed FEN) and no move could be computed.
    /// </summary>
    InvalidPosition,

    /// <summary>
    /// The engine was unable to produce a move within its configured constraints (e.g. a
    /// time/depth budget) or failed for an implementation-specific reason. See
    /// <see cref="AiMoveSelection.ErrorMessage"/> for details when present.
    /// </summary>
    Failed,
}

/// <summary>
/// The discriminated result of <see cref="IChessAiPlayer.SelectMove"/>. Always inspect
/// <see cref="Status"/> first: only when it is <see cref="AiMoveSelectionStatus.MoveSelected"/>
/// is <see cref="Move"/> guaranteed to be non-null.
/// </summary>
public sealed record AiMoveSelection
{
    /// <summary>
    /// The outcome of the selection attempt.
    /// </summary>
    public required AiMoveSelectionStatus Status { get; init; }

    /// <summary>
    /// The selected move and its evaluation. Guaranteed non-null only when
    /// <see cref="Status"/> is <see cref="AiMoveSelectionStatus.MoveSelected"/>; otherwise <c>null</c>.
    /// </summary>
    public AiMoveResult? Move { get; init; }

    /// <summary>
    /// Optional diagnostic message describing why <see cref="Status"/> is not
    /// <see cref="AiMoveSelectionStatus.MoveSelected"/>. <c>null</c> on success or when no
    /// detail is available.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful result carrying the chosen <paramref name="move"/>.</summary>
    public static AiMoveSelection Success(AiMoveResult move) =>
        new() { Status = AiMoveSelectionStatus.MoveSelected, Move = move };

    /// <summary>Creates a result indicating the position has no legal moves.</summary>
    public static AiMoveSelection NoMoves() =>
        new() { Status = AiMoveSelectionStatus.NoLegalMoves };

    /// <summary>Creates a result indicating the supplied FEN/position was invalid.</summary>
    public static AiMoveSelection InvalidPosition(string? message = null) =>
        new() { Status = AiMoveSelectionStatus.InvalidPosition, ErrorMessage = message };

    /// <summary>Creates a result indicating the engine failed to produce a move.</summary>
    public static AiMoveSelection Failure(string? message = null) =>
        new() { Status = AiMoveSelectionStatus.Failed, ErrorMessage = message };
}
