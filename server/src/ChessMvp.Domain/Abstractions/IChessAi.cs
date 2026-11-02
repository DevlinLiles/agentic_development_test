using ChessMvp.Domain.Entities;

namespace ChessMvp.Domain.Abstractions;

/// <summary>
/// The domain seam through which the server-side chess opponent selects its reply for a
/// position. Where <see cref="IChessRulesEngine"/> owns what is <em>legal</em>,
/// <see cref="IChessAi"/> owns what is <em>chosen</em> — and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The contract mirrors <see cref="IChessRulesEngine"/>'s shape on purpose: stateless,
/// side-effect free and FEN-in/FEN-out, so an implementation can be registered as a singleton
/// and used freely from a stateless API. It is equally minimal — one method, one result type —
/// because every additional member would encode an assumption about <em>how</em> the move was
/// found.
/// </para>
/// <para>
/// The contract is strength-agnostic. It names no evaluation terms, no search depth, no
/// scoring vocabulary and no move-ordering hints, so a one-ply static evaluator and a full
/// minimax search are both just implementations of it: upgrading the server's playing strength
/// is an implementation swap behind this seam, never a contract change. Three invariants make
/// that possible and are binding on every implementation:
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Deterministic.</term>
/// <description>
/// <see cref="ChooseMove"/> returns the same <see cref="ChosenMove"/> every time it is called
/// with the same position and side to move. Implementations must not consult the clock,
/// randomness, thread scheduling or any mutable state, and must not depend on the order in
/// which a rules engine enumerates moves — so a test may permute the enumerated moves, repeat
/// the call, or run it on another platform and still assert the identical answer.
/// </description>
/// </item>
/// <item>
/// <term>Total.</term>
/// <description>
/// A non-null result is returned for every position in which the side to move has at least one
/// legal move, and <see langword="null"/> exactly when it has none — terminal-position handling
/// is the caller's concern, the same fail-empty convention
/// <see cref="IChessRulesEngine.GetAllLegalMoves(string, Entities.PlayerColor)"/> follows, so
/// a test can assert null on checkmate, stalemate and unparseable positions and non-null on
/// every other one.
/// </description>
/// </item>
/// <item>
/// <term>Legal by construction.</term>
/// <description>
/// The chosen move is drawn from a rules engine's own enumeration of the position, so it can be
/// applied through <see cref="IChessRulesEngine.TryApplyMove"/> without further validation; a
/// non-null result never fabricates a move — a test can round-trip every returned move through
/// <see cref="IChessRulesEngine.TryApplyMove"/> and assert it is accepted.
/// </description>
/// </item>
/// </list>
/// </remarks>
public interface IChessAi
{
    /// <summary>
    /// Selects the reply for <paramref name="sideToMove"/> in the position described by
    /// <paramref name="fen"/>, or <see langword="null"/> when that side has no legal move in it.
    /// </summary>
    /// <param name="fen">The position to move from, in FEN notation.</param>
    /// <param name="sideToMove">
    /// The side the AI plays for. It is passed explicitly, as it is to
    /// <see cref="IChessRulesEngine.GetAllLegalMoves(string, Entities.PlayerColor)"/>, even
    /// though a well-formed FEN encodes it: the caller states who is being asked, and the rules
    /// engine remains the sole authority on what the position means.
    /// </param>
    /// <returns>
    /// The chosen move — legal for the position by construction and complete enough to apply
    /// directly — or <see langword="null"/> when <paramref name="sideToMove"/> has no legal
    /// move in it.
    /// </returns>
    ChosenMove? ChooseMove(string fen, PlayerColor sideToMove);
}
