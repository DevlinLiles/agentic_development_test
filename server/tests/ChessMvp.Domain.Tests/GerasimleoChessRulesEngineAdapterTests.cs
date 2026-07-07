using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class GerasimleoChessRulesEngineAdapterTests
{
    private readonly GerasimleoChessRulesEngineAdapter _sut = new();

    [Fact]
    public void TryApplyMove_LegalOpeningMove_IsAccepted()
    {
        var result = _sut.TryApplyMove(ChessConstants.StartingFen, PlayerColor.White, "e2", "e4", null);

        Assert.True(result.IsLegal);
        Assert.Equal("e4", result.San);
        Assert.False(result.IsCheck);
        Assert.NotNull(result.ResultingFen);
    }

    [Fact]
    public void TryApplyMove_PawnMovingThreeSquares_IsRejected()
    {
        var result = _sut.TryApplyMove(ChessConstants.StartingFen, PlayerColor.White, "e2", "e5", null);

        Assert.False(result.IsLegal);
        Assert.Equal(MoveFailureReason.IllegalMove, result.FailureReason);
    }

    [Fact]
    public void TryApplyMove_KnightMovingInStraightLine_IsRejected()
    {
        var result = _sut.TryApplyMove(ChessConstants.StartingFen, PlayerColor.White, "b1", "b3", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_MoveThatExposesOwnKingToCheck_IsRejected()
    {
        // White rook on e2 is pinned to the king on e1 by the black rook on e8.
        const string fen = "4r3/8/8/8/8/8/4R3/4K3 w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e2", "d2", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_MoveThatIgnoresExistingCheck_IsRejected()
    {
        // White king on e1 is in check from the black queen on e8; moving the knight doesn't help.
        const string fen = "4q3/8/8/8/8/8/8/1N2K3 w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "b1", "c3", null);

        Assert.False(result.IsLegal);
    }

    [Theory]
    [InlineData("e1", "g1")] // kingside
    [InlineData("e1", "c1")] // queenside
    public void TryApplyMove_CastlingWithClearPathAndRights_IsAccepted(string from, string to)
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, from, to, null);

        Assert.True(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_CastlingBlockedByPieceInBetween_IsRejected()
    {
        // Knight on b1 blocks the queenside castling path between the king and the rook.
        const string fen = "r3k2r/8/8/8/8/8/8/RN2K2R w KQkq - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e1", "c1", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_CastlingWithoutRights_IsRejected()
    {
        // Castling rights already revoked (e.g. king or rook previously moved).
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e1", "g1", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_CastlingThroughAttackedSquare_IsRejected()
    {
        // Black rook on f8 attacks f1, the square the king must pass through for kingside castling.
        const string fen = "5r1k/8/8/8/8/8/8/R3K2R w KQ - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e1", "g1", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_EnPassantImmediatelyAfterDoublePush_IsAccepted()
    {
        // White just pushed e2-e4; black pawn on d4 may capture en passant on e3.
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.Black, "d4", "e3", null);

        Assert.True(result.IsLegal);
        Assert.Contains("dxe3", result.San, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryApplyMove_EnPassantNotImmediatelyFollowingDoublePush_IsRejected()
    {
        // Same piece layout, but no en passant target recorded, so the capture is no longer available.
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.Black, "d4", "e3", null);

        Assert.False(result.IsLegal);
    }

    [Fact]
    public void TryApplyMove_PromotionToQueen_ProducesCorrectSanAndFen()
    {
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e7", "e8", PromotionPieceType.Queen);

        Assert.True(result.IsLegal);
        Assert.Contains("=Q", result.San);
        Assert.Contains("Q", result.ResultingFen!.Split(' ')[0]);
    }

    [Fact]
    public void TryApplyMove_UnderPromotionToKnight_ProducesCorrectSanAndFen()
    {
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "e7", "e8", PromotionPieceType.Knight);

        Assert.True(result.IsLegal);
        Assert.Contains("=N", result.San);
        Assert.Contains("N", result.ResultingFen!.Split(' ')[0]);
    }

    [Fact]
    public void TryApplyMove_CheckmatingMove_ReportsCheckmate()
    {
        // Fool's Mate.
        var fen = ChessConstants.StartingFen;
        fen = Apply(fen, PlayerColor.White, "f2", "f3");
        fen = Apply(fen, PlayerColor.Black, "e7", "e5");
        fen = Apply(fen, PlayerColor.White, "g2", "g4");

        var result = _sut.TryApplyMove(fen, PlayerColor.Black, "d8", "h4", null);

        Assert.True(result.IsLegal);
        Assert.True(result.IsCheckmate);
    }

    [Fact]
    public void TryApplyMove_MoveIntoStalemate_ReportsStalemate()
    {
        const string fen = "7k/8/5QK1/8/8/8/8/8 w - - 0 1";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "f6", "f7", null);

        Assert.True(result.IsLegal);
        Assert.True(result.IsStalemate);
    }

    [Fact]
    public void TryApplyMove_MoveReachingFiftyMoveThreshold_ReportsFiftyMoveDraw()
    {
        const string fen = "k6K/8/8/8/8/8/8/7R w - - 99 60";

        var result = _sut.TryApplyMove(fen, PlayerColor.White, "h1", "h2", null);

        Assert.True(result.IsLegal);
        Assert.True(result.IsFiftyMoveDraw);
    }

    [Fact]
    public void GetLegalDestinations_FromStartingPosition_ReturnsExpectedSquaresForPawn()
    {
        var destinations = _sut.GetLegalDestinations(ChessConstants.StartingFen, "e2");

        Assert.Equal(new HashSet<string> { "e3", "e4" }, destinations, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsPromotionMove_PawnMovingToBackRank_ReturnsTrue()
    {
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        Assert.True(_sut.IsPromotionMove(fen, "e7", "e8"));
    }

    [Fact]
    public void IsPromotionMove_NonPawnPiece_ReturnsFalse()
    {
        Assert.False(_sut.IsPromotionMove(ChessConstants.StartingFen, "b1", "c3"));
    }

    private string Apply(string fen, PlayerColor color, string from, string to)
    {
        var result = _sut.TryApplyMove(fen, color, from, to, null);
        Assert.True(result.IsLegal, $"Expected {from}->{to} to be legal.");
        return result.ResultingFen!;
    }
}
