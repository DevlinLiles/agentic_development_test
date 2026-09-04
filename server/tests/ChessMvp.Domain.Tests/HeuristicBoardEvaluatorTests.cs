using Chess;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessEvaluation;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicBoardEvaluatorTests
{
    private readonly HeuristicBoardEvaluator _sut = new();

    [Fact]
    public void Score_StartingPositionForWhite_ReturnsZero()
    {
        // Symmetric position, identical material, mobility, and piece placement for both colors,
        // so the side-relative score is zero regardless of perspective.
        var board = Load(ChessConstants.StartingFen);

        var score = _sut.Score(board, PlayerColor.White);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_IsSymmetricAcrossPerspectives()
    {
        var board = Load(ChessConstants.StartingFen);

        Assert.Equal(-_sut.Score(board, PlayerColor.White), _sut.Score(board, PlayerColor.Black));
    }

    [Fact]
    public void Score_UpExtraPawn_IsPositiveForSideWithMaterialAdvantage()
    {
        // White is up a pawn (extra pawn on e4) and it is White to move.
        const string fen = "rnbqkbnr/pppp1ppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1";
        var board = Load(fen);

        var score = _sut.Score(board, PlayerColor.White);

        Assert.True(score > 100, $"Expected a score above a pawn's material value (100), got {score}.");
    }

    [Fact]
    public void Score_UpExtraPawn_IsNegativeFromOpponentPerspective()
    {
        const string fen = "rnbqkbnr/pppp1ppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1";
        var board = Load(fen);

        var score = _sut.Score(board, PlayerColor.Black);

        Assert.True(score < -100, $"Expected a negative score below -100, got {score}.");
    }

    [Fact]
    public void Score_IsConsistentForSameBoard()
    {
        var board = Load(ChessConstants.StartingFen);

        var first = _sut.Score(board, PlayerColor.White);
        var second = _sut.Score(board, PlayerColor.White);
        var third = _sut.Score(board, PlayerColor.White);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Score_MobilityTermReflectedInScore()
    {
        // Give White far more freedom than Black: White has rooks/queen with open lines, Black is
        // boxed in. White to move. The mobility (and material) advantage should heavily favor White.
        const string fen = "3k4/8/8/8/8/8/8/R3K2R w KQ - 0 1";
        var board = Load(fen);

        var score = _sut.Score(board, PlayerColor.White);

        Assert.True(score > 0, $"Expected White to be favored, got {score}.");
    }

    [Fact]
    public void Score_FenOverload_MatchesBoardOverload()
    {
        const string fen = "rnbqkbnr/pppp1ppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1";
        var board = Load(fen);

        Assert.Equal(_sut.Score(board, PlayerColor.White), _sut.Score(fen, PlayerColor.White));
    }

    [Fact]
    public void Score_FenOverload_InvalidFen_ReturnsZero()
    {
        Assert.Equal(0, _sut.Score("not a fen", PlayerColor.White));
    }

    [Fact]
    public void Score_KnightOnCenterSquare_ScoresBetterThanOnRim()
    {
        // Two otherwise-equal positions differing only in knight placement: center vs rim.
        // A knight is worth the same material in both, so the score difference comes from the
        // piece-square table: a knight on d4 (center) should outscore a knight on a4 (rim).
        const string center = "4k3/8/8/8/3N4/8/8/4K3 w - - 0 1";
        const string rim = "4k3/8/8/8/N7/8/8/4K3 w - - 0 1";

        Assert.True(_sut.Score(center, PlayerColor.White) > _sut.Score(rim, PlayerColor.White));
    }

    [Fact]
    public void Score_AdvancedPawn_ScoresBetterThanStartingPawn()
    {
        // A pawn on e7 is far more advanced (table bonus 50) than a pawn on e2 (table bonus 0).
        const string advanced = "4k3/4P3/8/8/8/8/8/4K3 w - - 0 1";
        const string start = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";

        Assert.True(_sut.Score(advanced, PlayerColor.White) > _sut.Score(start, PlayerColor.White));
    }

    private static ChessBoard Load(string fen)
    {
        Assert.True(ChessBoard.TryLoadFromFen(fen, out var board, AutoEndgameRules.FiftyMoveRule));
        return board;
    }
}
