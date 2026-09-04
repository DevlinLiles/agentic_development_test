namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// A single legal move produced by <see cref="IChessRulesEngine.GetAllLegalMoves"/>. Unlike the
/// persisted <c>Move</c> entity this is a lightweight, allocation-friendly value object that
/// carries just enough information for the UI to render move hints and detect promotion prompts
/// without re-querying the rules engine per destination.
/// </summary>
/// <param name="FromSquare">Origin square in algebraic notation (e.g. "e2").</param>
/// <param name="ToSquare">Destination square in algebraic notation (e.g. "e4").</param>
/// <param name="San">Standard Algebraic Notation of the move (e.g. "e4", "Nf3", "exd5").</param>
/// <param name="IsPromotion">
/// <c>true</c> when the move is a pawn reaching the back rank and therefore requires a
/// promotion piece to be chosen before it can be applied.
/// </param>
public sealed record LegalMove(
    string FromSquare,
    string ToSquare,
    string San,
    bool IsPromotion);
