using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class ChessBoardEvaluatorTests
{
    // Using the real rules-engine adapter (as the adapter tests do) gives a true mobility count,
    // which is what the acceptance criteria exercise. The evaluator itself remains pure because
    // the adapter is stateless.
    private readonly IChessRulesEngine _rulesEngine = new GerasimleoChessRulesEngineAdapter();
    private readonly ChessBoardEvaluator _sut;

    public ChessBoardEvaluatorTests()
    {
        _sut = new ChessBoardEvaluator(_rulesEngine);
    }

    [Fact]
    public void Evaluate_SameFenAlwaysYieldsSameScore_IsPure()
    {
        var first = _sut.Evaluate(ChessConstants.StartingFen);
        var second = _sut.Evaluate(ChessConstants.StartingFen);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Evaluate_StartingPosition_ReturnsMobilityOnlyBecauseMaterialAndPstCancel()
    {
        // The starting position is materially and structurally symmetric, so the material + PST
        // contributions cancel out and the score reduces to the mobility term. White has 20 legal
        // opening moves; weighted by MobilityWeight(2) that is 40.
        var score = _sut.Evaluate(ChessConstants.StartingFen);

        Assert.Equal(40, score);
    }

    [Fact]
    public void Evaluate_ExtraQueenForSideToMove_ProducesLargePositiveScore()
    {
        // White to move with a lone extra queen (plus kings). The side to move is decisively ahead;
        // the queen's 900 material dominates the score.
        const string fen = "4k3/8/8/8/8/8/8/Q3K3 w - - 0 1";

        var score = _sut.Evaluate(fen);

        Assert.True(score >= 850, $"Expected a large positive score, got {score}.");
    }

    [Fact]
    public void Evaluate_ExtraQueenForOpponent_ProducesLargeNegativeScore()
    {
        // White to move but Black holds the lone queen; the side to move is decisively behind.
        // The score is dominated by the opposing queen (900), offset only slightly by the side's
        // own king mobility, so it stays well below -800.
        const string fen = "4k3/8/8/8/8/8/8/4K2q w - - 0 1";

        var score = _sut.Evaluate(fen);

        Assert.True(score <= -800, $"Expected a large negative score, got {score}.");
    }

    [Fact]
    public void Evaluate_FlippingSideToMove_FlipsScorePerspective()
    {
        // Same material layout, only the side to move differs. White holds the lone queen, so with
        // White to move the score is large and positive; with Black to move that same material is
        // now against the side to move, so the score is large and negative. This confirms the
        // evaluator reports from the side-to-move's perspective.
        const string whiteFen = "4k3/8/8/8/8/8/8/Q3K3 w - - 0 1";
        const string blackFen = "4k3/8/8/8/8/8/8/Q3K3 b - - 0 1";

        var whiteScore = _sut.Evaluate(whiteFen);
        var blackScore = _sut.Evaluate(blackFen);

        Assert.True(whiteScore > 0);
        Assert.True(blackScore < 0);
    }

    [Fact]
    public void Evaluate_InvalidFen_ReturnsZeroWithoutThrowing()
    {
        Assert.Equal(0, _sut.Evaluate(""));
        Assert.Equal(0, _sut.Evaluate("   "));
        Assert.Equal(0, _sut.Evaluate("not a fen"));
    }

    [Fact]
    public void Evaluate_MobilityTermUsesRulesEngineLegalMoves()
    {
        // Confirm the mobility term is wired to IChessRulesEngine.GetAllLegalMoves by stubbing it
        // to a known move count and checking the score reflects it.
        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(ChessConstants.StartingFen)
            .Returns(new List<LegalMove>
            {
                new() { FromSquare = "e2", ToSquare = "e4" },
                new() { FromSquare = "e2", ToSquare = "e3" },
            });

        var sut = new ChessBoardEvaluator(rulesEngine);

        // Starting position is materially/PST-symmetric, so the score equals mobility * weight.
        var score = sut.Evaluate(ChessConstants.StartingFen);

        Assert.Equal(4, score); // 2 moves * MobilityWeight(2)
    }
}
