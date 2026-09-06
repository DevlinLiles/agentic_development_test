using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Ai;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessRulesEngine;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicChessAiPlayerTests
{
    // A deterministic, pure evaluator: sums piece material from White's perspective. Because it
    // has no randomness or time dependency, identical FEN inputs always produce identical scores,
    // which is exactly what the greedy player needs for reproducible move selection.
    private sealed class MaterialEvaluator : IHeuristicEvaluator
    {
        public double Evaluate(string fen)
        {
            var board = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            double score = 0;
            foreach (var ch in board)
            {
                score += ch switch
                {
                    'P' => 1,
                    'N' => 3,
                    'B' => 3,
                    'R' => 5,
                    'Q' => 9,
                    'p' => -1,
                    'n' => -3,
                    'b' => -3,
                    'r' => -5,
                    'q' => -9,
                    _ => 0,
                };
            }

            return score;
        }
    }

    private static HeuristicChessAiPlayer CreateSut(
        IChessRulesEngine? rulesEngine = null,
        IHeuristicEvaluator? evaluator = null) =>
        new(rulesEngine ?? new GerasimleoChessRulesEngineAdapter(), evaluator ?? new MaterialEvaluator());

    [Fact]
    public void Constructor_InjectsEvaluatorAndRulesEngine()
    {
        var rulesEngine = Substitute.For<IChessRulesEngine>();
        var evaluator = Substitute.For<IHeuristicEvaluator>();

        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        Assert.NotNull(sut);
        Assert.IsAssignableFrom<IChessAiPlayer>(sut);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new HeuristicChessAiPlayer(null!, Substitute.For<IHeuristicEvaluator>()));
        Assert.Throws<ArgumentNullException>(() =>
            new HeuristicChessAiPlayer(Substitute.For<IChessRulesEngine>(), null!));
    }

    [Fact]
    public async Task SelectMoveAsync_NoLegalMoves_ReturnsNoLegalMoves()
    {
        // Stalemate position: White king on a1, Black queen on c2 and king on h8.
        const string fen = "7k/8/8/8/8/8/2q5/K7 w - - 0 1";
        var sut = CreateSut();

        var result = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        Assert.Equal(AiMoveStatus.NoLegalMoves, result.Status);
    }

    [Fact]
    public async Task SelectMoveAsync_GreedyWhite_CapturesFreeQueen()
    {
        // White bishop on d4 can capture the undefended black queen on a1.
        const string fen = "7k/8/8/8/8/3B4/8/q3K3 w - - 0 1";
        var sut = CreateSut();

        var result = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        Assert.Equal(AiMoveStatus.MoveSelected, result.Status);
        Assert.Equal("d3", result.FromSquare);
        Assert.Equal("a1", result.ToSquare);
        Assert.Equal(1, result.SearchDepthInPlies);
        // Capturing the black queen gains ~9 points of material from White's perspective.
        Assert.True(result.EvaluationScore > 0);
    }

    [Fact]
    public async Task SelectMoveAsync_GreedyBlack_CapturesFreeRook()
    {
        // Black knight on f6 can capture the undefended white rook on e4.
        const string fen = "4k3/8/5n2/8/4R3/8/8/4K3 b - - 0 1";
        var sut = CreateSut();

        var result = await sut.SelectMoveAsync(fen, PlayerColor.Black, AiSearchOptions.Shallow(1));

        Assert.Equal(AiMoveStatus.MoveSelected, result.Status);
        Assert.Equal("f6", result.FromSquare);
        Assert.Equal("e4", result.ToSquare);
        // From Black's perspective the score is the negation of White's, so capturing White's
        // rook yields a positive score for Black.
        Assert.True(result.EvaluationScore > 0);
    }

    [Fact]
    public async Task SelectMoveAsync_Determinism_ReproducibleAcrossRunsOnFixedBoard()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 4 4";
        var sut = CreateSut();

        var first = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));
        var second = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));
        // Run a third time with a fresh instance to prove the determinism comes from the algorithm,
        // not from any shared mutable state.
        var third = await CreateSut().SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(AiMoveStatus.MoveSelected, first.Status);
    }

    [Fact]
    public async Task SelectMoveAsync_Determinism_HoldsAcrossShuffledEquivalentCalls()
    {
        // A symmetrical position with several equally-scoring moves exercises the tie-break: the
        // selected move must be stable regardless of how many times we ask.
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        var sut = CreateSut();

        var results = await Task.WhenAll(
            sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1)),
            sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1)),
            sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1)));

        Assert.Equal(results[0], results[1]);
        Assert.Equal(results[0], results[2]);
    }

    [Fact]
    public async Task SelectMoveAsync_PromotionMove_SelectsQueenAndPromotes()
    {
        // White pawn on e7 can promote; the material evaluator favours a queen (9) over knight (3).
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";
        var sut = CreateSut();

        var result = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        Assert.Equal(AiMoveStatus.MoveSelected, result.Status);
        Assert.Equal("e7", result.FromSquare);
        Assert.Equal("e8", result.ToSquare);
        Assert.Equal(PromotionPieceType.Queen, result.Promotion);
        Assert.NotNull(result.San);
        Assert.Contains("=Q", result.San);
    }

    [Fact]
    public async Task SelectMoveAsync_PromotionWhenUnderPromotionScoresHigher_SelectsBestPiece()
    {
        // White pawn on e7 promotes; a stub evaluator is configured so that the resulting FEN after
        // promoting to a Knight scores higher than any other promotion. This proves the player
        // evaluates each promotion piece and selects the best-scoring one, not just the queen.
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";
        var rulesEngine = new GerasimleoChessRulesEngineAdapter();
        var evaluator = Substitute.For<IHeuristicEvaluator>();
        evaluator.Evaluate(Arg.Any<string>()).Returns(call =>
        {
            var boardPart = ((string)call[0]!).Split(' ')[0];
            // A knight on the back rank appears as 'N' in the FEN board; reward it specifically so
            // it outscores the queen (which would otherwise win on material).
            return boardPart.Contains('N') ? 100.0 : 1.0;
        });

        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        var result = await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        Assert.Equal(AiMoveStatus.MoveSelected, result.Status);
        Assert.Equal(PromotionPieceType.Knight, result.Promotion);
        Assert.Contains("=N", result.San);
    }

    [Fact]
    public async Task SelectMoveAsync_EmptyFen_Throws()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SelectMoveAsync("", PlayerColor.White, AiSearchOptions.Shallow(1)));
    }

    [Fact]
    public async Task SelectMoveAsync_StubsEvaluatorOncePerLegalMove()
    {
        // Verifies the 1-ply search actually evaluates every legal move (at least the resulting
        // FENs) by counting evaluator calls against the number of legal moves in the position.
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        var rulesEngine = new GerasimleoChessRulesEngineAdapter();
        var evaluator = Substitute.For<IHeuristicEvaluator>();
        evaluator.Evaluate(Arg.Any<string>()).Returns(0.0);

        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);
        var legalMoveCount = rulesEngine.GetAllLegalMoves(fen).Count;

        await sut.SelectMoveAsync(fen, PlayerColor.White, AiSearchOptions.Shallow(1));

        // Each non-promotion legal move yields exactly one evaluator call.
        Assert.Equal(legalMoveCount, evaluator.ReceivedCalls().Count());
    }
}
