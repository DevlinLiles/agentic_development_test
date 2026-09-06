using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Unit tests for <see cref="HeuristicEvaluator"/>. Assertions are deterministic: they encode
/// known relationships of the heuristic (material dominates, centralization is rewarded, mirrored
/// positions score equally for mirrored colors) rather than brittle exact constants, so the tests
/// stay meaningful if the underlying table magnitudes are retuned.
/// </summary>
public class HeuristicEvaluatorTests
{
    private readonly IHeuristicEvaluator _sut = new HeuristicEvaluator();

    [Fact]
    public void Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility()
    {
        // The starting position is materially and positionally symmetric, so the only nonzero term
        // is mobility, which is identical for both colors. The score must therefore be equal (and
        // positive, since mobility is counted for the evaluated side).
        var whiteScore = _sut.Score(ChessConstants.StartingFen, PlayerColor.White);
        var blackScore = _sut.Score(ChessConstants.StartingFen, PlayerColor.Black);

        Assert.True(whiteScore > 0, "Starting position should yield a positive mobility-only score.");
        Assert.Equal(whiteScore, blackScore);
    }

    [Fact]
    public void Score_CapturingOpponentQueen_RaisesScoreByApproximatelyQueenValue()
    {
        // White pawn on d2 can capture the black queen on c3. Capturing a queen should swing the
        // material balance by roughly the queen's value (900), far more than any placement or
        // mobility delta, proving material scoring uses standard piece values.
        const string before = "7k/8/8/8/8/2q5/3P4/4K3 w - - 0 1";
        const string after = "7k/8/8/8/2P5/8/8/4K3 b - - 0 1";

        var scoreBefore = _sut.Score(before, PlayerColor.White);
        var scoreAfter = _sut.Score(after, PlayerColor.White);

        Assert.True(scoreAfter > scoreBefore, "Capturing the queen must improve the score.");
        // Queen is worth 900; allow slack for the queen's placement value and the pawn's placement
        // change, but the jump must be in the same order of magnitude as the queen value.
        Assert.True(scoreAfter - scoreBefore >= 850,
            $"Expected a material swing near the queen value (>=850), got {scoreAfter - scoreBefore}.");
    }

    [Fact]
    public void Score_AfterKnownPawnPush_ChangesScore()
    {
        // Playing 1.e4 from the starting position moves a pawn from a 50-valued square (e2) to a
        // 25-valued square (e4) while slightly increasing mobility. The net effect is a lower score
        // for White, demonstrating that the score changes after a known move and that the piece-
        // square table term is actually applied.
        var scoreBefore = _sut.Score(ChessConstants.StartingFen, PlayerColor.White);

        const string afterE2E4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        var scoreAfter = _sut.Score(afterE2E4, PlayerColor.White);

        Assert.NotEqual(scoreBefore, scoreAfter);
        Assert.True(scoreAfter < scoreBefore,
            "e2-e4 should lower White's score (placement loss outweighs the small mobility gain).");
    }

    [Fact]
    public void Score_MirroredPosition_ScoresEquallyForMirroredColor()
    {
        // A white pawn on e2 with kings mirrored is the exact color-mirror of a black pawn on e7
        // with kings mirrored. Because piece-square tables are applied per color by mirroring the
        // rank index, the evaluated-side score must be identical for the two mirrored viewpoints.
        const string whiteView = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";
        const string blackView = "4k3/4p3/8/8/8/8/8/4K3 b - - 0 1";

        var scoreForWhite = _sut.Score(whiteView, PlayerColor.White);
        var scoreForBlack = _sut.Score(blackView, PlayerColor.Black);

        Assert.Equal(scoreForWhite, scoreForBlack);
    }

    [Fact]
    public void Score_CentralizedQueen_OutScoresCorneredQueen()
    {
        // Same material (queen + king) in both positions, but the centralized queen has more
        // pseudo-legal moves and a better piece-square value than the cornered queen, so its score
        // must be higher. This exercises both the mobility term (more moves counted for the
        // evaluated side) and the piece-square table for the queen.
        const string cornered = "k7/8/8/8/8/8/8/Q3K3 w - - 0 1";
        const string centralized = "k7/8/8/8/4Q3/8/8/4K3 w - - 0 1";

        var cornerScore = _sut.Score(cornered, PlayerColor.White);
        var centerScore = _sut.Score(centralized, PlayerColor.White);

        Assert.True(centerScore > cornerScore,
            "A centralized queen (more mobility, better placement) must outscore a cornered one.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-fen")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")] // missing ranks / malformed
    public void Score_MalformedBoard_ReturnsNeutralZero(string board)
    {
        // A robust evaluator degrades gracefully instead of throwing on unparseable input.
        Assert.Equal(0, _sut.Score(board, PlayerColor.White));
    }
}
