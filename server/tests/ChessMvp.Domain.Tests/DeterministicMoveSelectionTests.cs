using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Focused determinism fixture for <see cref="HeuristicChessAiPlayer"/>. The player is pure and
/// stateless, so for any fixed board position it must select the exact same move on every call.
/// These tests pin that contract by running selection many times per position and by exercising
/// the deterministic tie-break on a position whose top score is genuinely shared by more than one
/// move. Every test constructs the player with a real <see cref="ChessBoardEvaluator"/> (backed by
/// a real rules engine) so the genuine scoring path is exercised rather than a stub.
/// </summary>
public class DeterministicMoveSelectionTests
{
    // The player needs a real rules engine to apply moves and produce resulting FENs; it is
    // stateless and FEN-in/FEN-out. The evaluator is the real ChessBoardEvaluator so the fixture
    // scores positions through the genuine material + PST + mobility path.
    private readonly IChessRulesEngine _rulesEngine = new GerasimleoChessRulesEngineAdapter();
    private readonly IChessBoardEvaluator _realEvaluator;
    private readonly HeuristicChessAiPlayer _sut;

    // "Multiple times" — large enough to catch any non-determinism without making the suite slow.
    private const int Iterations = 20;

    public DeterministicMoveSelectionTests()
    {
        _realEvaluator = new ChessBoardEvaluator(_rulesEngine);
        _sut = new HeuristicChessAiPlayer(_rulesEngine, _realEvaluator);
    }

    private static IReadOnlyList<LegalMove> LegalMovesFor(string fen, IChessRulesEngine engine) =>
        engine.GetAllLegalMoves(fen);

    private static ChessAiMoveRequest RequestFor(string fen, PlayerColor side, IChessRulesEngine engine) =>
        new()
        {
            Fen = fen,
            SideToMove = side,
            LegalMoves = LegalMovesFor(fen, engine),
        };

    [Fact]
    public async Task ChooseMove_FixedPosition_IsIdenticalAcrossManyRuns()
    {
        // The standard starting position is a known, universally-valid position. The real
        // evaluator resolves the (materially equal) candidates via PST and mobility, so a single
        // best move exists; it must come back byte-for-byte identical across many independent
        // calls.
        var request = RequestFor(ChessConstants.StartingFen, PlayerColor.White, _rulesEngine);

        var first = await _sut.ChooseMoveAsync(request);

        // Sanity: the chosen move is one of the supplied legal moves.
        Assert.Contains(
            request.LegalMoves,
            m => m.FromSquare == first.FromSquare && m.ToSquare == first.ToSquare);

        for (var i = 1; i < Iterations; i++)
        {
            var again = await _sut.ChooseMoveAsync(request);

            Assert.Equal(first.FromSquare, again.FromSquare);
            Assert.Equal(first.ToSquare, again.ToSquare);
            Assert.Equal(first.Promotion, again.Promotion);
            Assert.Equal(first.Evaluation, again.Evaluation);
        }
    }

    [Fact]
    public async Task ChooseMove_PromotionPosition_PromotionPieceIsDeterministicAcrossRuns()
    {
        // White pawn on e7 with e8 open (black king tucked on h8). All four promotion pieces are
        // legal, so the player resolves the promotion deterministically to Queen (the default and
        // highest-material outcome). The selected promotion piece must be Queen on every run, and
        // the move itself (e7-e8) must never vary.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";
        var request = RequestFor(fen, PlayerColor.White, _rulesEngine);

        for (var i = 0; i < Iterations; i++)
        {
            var result = await _sut.ChooseMoveAsync(request);

            Assert.Equal("e7", result.FromSquare);
            Assert.Equal("e8", result.ToSquare);
            Assert.Equal(PromotionPieceType.Queen, result.Promotion);
        }
    }

    [Fact]
    public async Task ChooseMove_TiedScores_TieBreakIsDeterministicAcrossRuns()
    {
        // White: knights on b1 and g1, king on e1; Black: lone king on a8. With only knights and
        // kings on the board, every White move leaves Black with exactly the same mobility (the
        // black king always has the same three escape squares), so the ranking is driven purely by
        // the piece-square tables. The two central knight jumps b1-c3 and g1-f3 land on
        // mirror-symmetric squares that share the identical PST value, so they score EXACTLY the
        // same with the real evaluator — a genuine tied-score position, not a forced one. The
        // player must break the tie deterministically by selecting the lexicographically smallest
        // move key ("b1c3" < "g1f3"), and it must do so on every call.
        const string fen = "k7/8/8/8/8/8/8/1N2K1N1 w - - 0 1";
        var request = RequestFor(fen, PlayerColor.White, _rulesEngine);

        // Independently derive the expected tie-break winner from the public scoring contract
        // (score == -evaluator.Evaluate(resultingFen)) so the assertion does not hard-code a
        // square and stays meaningful if the evaluation tables ever change.
        var (expectedKey, tiedCount) = ExpectedTieBreakWinner(request);

        // The position must actually present a tie; otherwise the test would not be exercising
        // tie-breaking at all.
        Assert.True(
            tiedCount >= 2,
            $"Expected a tied-score position but only {tiedCount} move(s) share the top score.");

        var reference = await _sut.ChooseMoveAsync(request);

        Assert.Equal(expectedKey, MoveKey(reference.FromSquare, reference.ToSquare));

        for (var i = 1; i < Iterations; i++)
        {
            var again = await _sut.ChooseMoveAsync(request);

            Assert.Equal(reference.FromSquare, again.FromSquare);
            Assert.Equal(reference.ToSquare, again.ToSquare);
            Assert.Equal(reference.Promotion, again.Promotion);
            Assert.Equal(reference.Evaluation, again.Evaluation);
        }
    }

    /// <summary>
    /// Computes the move key the deterministic tie-break is expected to select, together with the
    /// number of moves that share the top score. Uses only the public rules-engine and evaluator
    /// contracts and the documented negamax scoring, mirroring the player's selection rule
    /// (highest score, then ascending move-string key) without reaching into its internals.
    /// </summary>
    private (string Key, int TiedCount) ExpectedTieBreakWinner(ChessAiMoveRequest request)
    {
        var scored = new List<(string Key, double Score)>();

        foreach (var move in request.LegalMoves)
        {
            // This fixture only uses non-promotion positions for tie-break analysis, so the
            // simple apply-with-null path is sufficient and mirrors the player's main branch.
            Assert.False(move.IsPromotion,
                "ExpectedTieBreakWinner only supports non-promotion positions.");

            var application = _rulesEngine.TryApplyMove(
                request.Fen, request.SideToMove, move.FromSquare, move.ToSquare, null);

            if (!application.IsLegal || application.ResultingFen is null)
            {
                continue;
            }

            var score = -(double)_realEvaluator.Evaluate(application.ResultingFen);
            scored.Add((MoveKey(move.FromSquare, move.ToSquare), score));
        }

        Assert.NotEmpty(scored);

        var maxScore = scored.Max(s => s.Score);
        var tied = scored.Where(s => s.Score == maxScore).ToList();
        var winner = tied.OrderBy(s => s.Key, StringComparer.Ordinal).First().Key;

        return (winner, tied.Count);
    }

    private static string MoveKey(string fromSquare, string toSquare) =>
        string.Concat(fromSquare, toSquare).ToLowerInvariant();
}
