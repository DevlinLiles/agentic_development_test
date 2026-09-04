using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;

namespace ChessMvp.ChessAi;

/// <summary>
/// Abstraction for a chess AI player. A concrete implementation encapsulates a particular
/// algorithm or backing engine (minimax/alpha-beta, a neural network, an external process
/// adapter, a random/baseline player used in tests, etc.).
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in the AI layer and intentionally depends only on shared chess
/// primitives from the Domain layer (a FEN board state, <see cref="PlayerColor"/>, and the
/// <see cref="LegalMove"/> value type). It has no knowledge of any concrete implementation, so
/// AI players can be swapped or substituted (for example, replaced by a deterministic stub in
/// unit tests) via dependency injection without callers changing.
/// </para>
/// <para>
/// Implementations are expected to be given a position that is not yet terminal — i.e. the side
/// to move has at least one legal move — but they MUST handle the no-legal-moves case gracefully
/// by returning an <see cref="AiMoveSelection"/> whose <see cref="AiMoveSelection.Status"/> is
/// <see cref="AiMoveSelectionStatus.NoLegalMoves"/> rather than throwing. The same applies to
/// invalid input, which is reported via <see cref="AiMoveSelectionStatus.InvalidPosition"/>.
/// </para>
/// <para>
/// Implementations should be effectively stateless with respect to a single position: given the
/// same FEN, side, and configuration they should select the same move. Any persistent state
/// (transposition tables, NN caches) must be transparent to callers and must not leak across
/// independent games.
/// </para>
/// </remarks>
public interface IChessAiPlayer
{
    /// <summary>
    /// Selects the best move for <paramref name="sideToMove"/> from the position described by
    /// <paramref name="fen"/>.
    /// </summary>
    /// <param name="fen">
    /// The board state in Forsyth–Edwards Notation. The side to move encoded in the FEN is
    /// expected to match <paramref name="sideToMove"/>.
    /// </param>
    /// <param name="sideToMove">The color whose move is to be selected.</param>
    /// <param name="legalMoves">
    /// The exhaustive set of legal moves for <paramref name="sideToMove"/> in <paramref name="fen"/>,
    /// typically obtained from <see cref="IChessRulesEngine.GetAllLegalMoves"/>. Implementations
    /// MUST choose a move from this set (or report <see cref="AiMoveSelectionStatus.NoLegalMoves"/>
    /// when it is empty) and MUST NOT invent moves the rules engine did not provide. Supplying this
    /// set explicitly keeps the AI layer decoupled from the concrete rules engine while guaranteeing
    /// legality.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel a potentially long-running search. Implementations should honor it
    /// promptly and surface cancellation as <see cref="AiMoveSelectionStatus.Failed"/>.
    /// </param>
    /// <returns>
    /// An <see cref="AiMoveSelection"/> describing the outcome. When a move was selected,
    /// <see cref="AiMoveSelection.Move"/> is populated and
    /// <see cref="AiMoveSelection.Status"/> is <see cref="AiMoveSelectionStatus.MoveSelected"/>.
    /// </returns>
    AiMoveSelection SelectMove(
        string fen,
        PlayerColor sideToMove,
        IReadOnlyList<LegalMove> legalMoves,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optionally returns every scored candidate the engine considered for the most recent
    /// <see cref="SelectMove"/> call on this instance, sorted best-first. Engines that do not
    /// expose candidates may return <c>null</c> or a single-element list containing the chosen
    /// move. This is intended for analysis tooling and test assertions, not for gameplay logic.
    /// </summary>
    /// <remarks>
    /// Implementations are free to return <c>null</c> if maintaining the candidate list is not
    /// supported or not meaningful for the given call.
    /// </remarks>
    IReadOnlyList<ScoredMoveCandidate>? GetLastCandidates();
}
