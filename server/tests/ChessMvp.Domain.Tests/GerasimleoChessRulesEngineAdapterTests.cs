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

    [Fact]
    public void GetAllLegalMoves_FromStartingPosition_EnumeratesTwentyMovesWithSan()
    {
        var moves = _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.White);

        Assert.Equal(20, moves.Count);

        // Every from/to pair is unique.
        Assert.Equal(
            moves.Count,
            moves.Select(m => (Key(m.FromSquare), Key(m.ToSquare))).Distinct().Count());

        // The 16 pawn moves and 4 knight moves make up the full opening move set.
        Assert.Equal(16, moves.Count(m => m.FromSquare[1] == '2'));
        Assert.Equal(4, moves.Count(m => m.FromSquare is "b1" or "g1"));

        Assert.Contains(moves, m => m.San == "e4");
        Assert.Contains(moves, m => m.San == "Nf3");

        Assert.All(moves, m =>
        {
            Assert.False(m.IsCheck);
            Assert.False(m.IsCheckmate);
            Assert.False(m.IsStalemate);
            Assert.False(m.IsFiftyMoveDraw);
            Assert.False(string.IsNullOrWhiteSpace(m.San));
        });
    }

    [Fact]
    public void GetAllLegalMoves_EveryDestination_IsReportedByGetLegalDestinations()
    {
        var moves = _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.White);

        Assert.NotEmpty(moves);

        foreach (var move in moves)
        {
            var destinations = _sut.GetLegalDestinations(ChessConstants.StartingFen, move.FromSquare);

            Assert.True(
                destinations.Contains(move.ToSquare),
                $"GetLegalDestinations({move.FromSquare}) did not include {move.ToSquare}.");
        }
    }

    [Fact]
    public void GetAllLegalMoves_AggregatedDestinations_MatchGetLegalDestinationsPerSquare()
    {
        // Castling rights both sides, plus rook/king mobility, in one position.
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);
        var origins = moves.Select(m => Key(m.FromSquare)).Distinct().ToList();

        Assert.NotEmpty(origins);

        // Every origin the enumeration reports must agree square-by-square with the single-square
        // query, and vice versa.
        foreach (var origin in origins)
        {
            var expected = _sut.GetLegalDestinations(fen, origin)
                .Select(Key)
                .ToHashSet();

            var actual = moves
                .Where(m => Key(m.FromSquare) == origin)
                .Select(m => Key(m.ToSquare))
                .ToHashSet();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void GetAllLegalMoves_IncludesCastlingWithCorrectSan()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);

        var kingside = Assert.Single(moves, m => m.FromSquare == "e1" && m.ToSquare == "g1");
        Assert.Equal("O-O", kingside.San);

        var queenside = Assert.Single(moves, m => m.FromSquare == "e1" && m.ToSquare == "c1");
        Assert.Equal("O-O-O", queenside.San);
    }

    [Fact]
    public void GetAllLegalMoves_EveryEnumeratedMove_IsAcceptedByTryApplyMove()
    {
        // Requires check evasion, and exercises rook and king mobility alongside castling rights.
        const string fen = "4r2k/8/8/8/8/5N2/8/R3K2R w KQ - 5 40";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);

        Assert.NotEmpty(moves);

        foreach (var move in moves)
        {
            var applied = _sut.TryApplyMove(fen, PlayerColor.White, move.FromSquare, move.ToSquare, null);

            Assert.True(applied.IsLegal, $"Enumerated move {move.FromSquare}->{move.ToSquare} was rejected by TryApplyMove.");

            // SAN and terminal flags must match what actually applying the move reports.
            Assert.Equal(applied.San, move.San);
            Assert.Equal(applied.IsCheck, move.IsCheck);
            Assert.Equal(applied.IsCheckmate, move.IsCheckmate);
            Assert.Equal(applied.IsStalemate, move.IsStalemate);
            Assert.Equal(applied.IsFiftyMoveDraw, move.IsFiftyMoveDraw);
        }
    }

    [Fact]
    public void GetAllLegalMoves_CheckmatingMove_CarriesCheckAndCheckmateFlagsAndSan()
    {
        // Fool's Mate, one ply before mate: Qh4# is both check and checkmate.
        var fen = ChessConstants.StartingFen;
        fen = Apply(fen, PlayerColor.White, "f2", "f3");
        fen = Apply(fen, PlayerColor.Black, "e7", "e5");
        fen = Apply(fen, PlayerColor.White, "g2", "g4");

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.Black);

        Assert.Contains(moves, m => m.IsCheck);

        var mate = Assert.Single(moves, m => m.IsCheckmate);
        Assert.Equal("d8", mate.FromSquare);
        Assert.Equal("h4", mate.ToSquare);
        Assert.StartsWith("Qh4", mate.San, StringComparison.OrdinalIgnoreCase);
        Assert.True(mate.IsCheck);
    }

    [Fact]
    public void GetAllLegalMoves_StalematingMove_CarriesStalemateFlagConsistentWithTryApplyMove()
    {
        // Same position as the TryApplyMove stalemate test: Qf7 stalemates black.
        const string fen = "7k/8/5QK1/8/8/8/8/8 w - - 0 1";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);

        Assert.NotEmpty(moves);

        var stalemating = moves.Where(m => m.IsStalemate).ToList();

        Assert.NotEmpty(stalemating);
        Assert.Contains(stalemating, m => m.FromSquare == "f6" && m.ToSquare == "f7");

        // The flags must agree with what actually applying the move reports.
        foreach (var move in stalemating)
        {
            var applied = _sut.TryApplyMove(fen, PlayerColor.White, move.FromSquare, move.ToSquare, null);

            Assert.True(applied.IsLegal);
            Assert.Equal(applied.IsStalemate, move.IsStalemate);
            Assert.Equal(applied.IsCheckmate, move.IsCheckmate);
            Assert.Equal(applied.IsCheck, move.IsCheck);
        }
    }

    [Fact]
    public void GetAllLegalMoves_MoveReachingFiftyMoveThreshold_CarriesFiftyMoveFlag()
    {
        // The halfmove clock sits at 99, so every legal move reaches the hundred-ply threshold.
        const string fen = "k6K/8/8/8/8/8/8/7R w - - 99 60";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);

        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.True(m.IsFiftyMoveDraw));
        Assert.Contains(moves, m => m.FromSquare == "h1" && m.ToSquare == "h2");
    }

    [Fact]
    public void GetAllLegalMoves_Promotion_IsReportedOncePerSquarePairWithDefaultSan()
    {
        // Promotion-piece choice is deferred to TryApplyMove, so the e7->e8 promotion must be
        // enumerated exactly once even though the library models four promotion moves.
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.White);

        var promotions = moves.Where(m => m.FromSquare == "e7" && m.ToSquare == "e8").ToList();

        var single = Assert.Single(promotions);
        Assert.Contains("=Q", single.San);
    }

    [Fact]
    public void GetAllLegalMoves_EnPassantCapture_IsIncludedAndCrossChecked()
    {
        // Black pawn on d4 may capture the white e-pawn en passant on e3.
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        var moves = _sut.GetAllLegalMoves(fen, PlayerColor.Black);

        var enPassant = Assert.Single(moves, m => m.FromSquare == "d4" && m.ToSquare == "e3");

        Assert.Contains("dxe3", enPassant.San, StringComparison.OrdinalIgnoreCase);

        var destinations = _sut.GetLegalDestinations(fen, "d4");
        Assert.Contains("e3", destinations, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAllLegalMoves_CheckmatePosition_ReturnsNoMoves()
    {
        // Fool's Mate completed: black has just mated white.
        var fen = ChessConstants.StartingFen;
        fen = Apply(fen, PlayerColor.White, "f2", "f3");
        fen = Apply(fen, PlayerColor.Black, "e7", "e5");
        fen = Apply(fen, PlayerColor.White, "g2", "g4");
        fen = Apply(fen, PlayerColor.Black, "d8", "h4");

        Assert.Empty(_sut.GetAllLegalMoves(fen, PlayerColor.White));
    }

    [Fact]
    public void GetAllLegalMoves_StalematePosition_ReturnsNoMoves()
    {
        const string fen = "7k/8/6QK/8/8/8/8/8 b - - 0 1";

        Assert.Empty(_sut.GetAllLegalMoves(fen, PlayerColor.Black));
    }

    [Fact]
    public void GetAllLegalMoves_InvalidFen_ReturnsEmpty()
    {
        var moves = _sut.GetAllLegalMoves("not a fen", PlayerColor.White);

        Assert.Empty(moves);
    }

    [Fact]
    public void GetAllLegalMoves_SideNotToMove_ReturnsEmpty()
    {
        var moves = _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.Black);

        Assert.Empty(moves);
    }

    [Fact]
    public void GetAllLegalMoves_IsStateless_ReportsSameMoveSetOnRepeatedCalls()
    {
        var before = _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.White).ToList();

        _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.White);
        _sut.GetLegalDestinations(ChessConstants.StartingFen, "e2");

        var after = _sut.GetAllLegalMoves(ChessConstants.StartingFen, PlayerColor.White).ToList();

        Assert.Equal(
            before.Select(m => (m.FromSquare, m.ToSquare, m.San)).OrderBy(t => t.Item1).ThenBy(t => t.Item2),
            after.Select(m => (m.FromSquare, m.ToSquare, m.San)).OrderBy(t => t.Item1).ThenBy(t => t.Item2));
    }

    private string Apply(string fen, PlayerColor color, string from, string to)
    {
        var result = _sut.TryApplyMove(fen, color, from, to, null);
        Assert.True(result.IsLegal, $"Expected {from}->{to} to be legal.");
        return result.ResultingFen!;
    }

    private static string Key(string square) => square.ToUpperInvariant();
}
