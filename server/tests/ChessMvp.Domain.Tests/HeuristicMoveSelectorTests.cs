using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicMoveSelectorTests
{
    // Uses the real rules engine so move legality (checks, pins, en passant, promotion) is
    // exercised end-to-end; capture classification and scoring are the unit under test.
    private readonly HeuristicMoveSelector _sut =
        new(new GerasimleoChessRulesEngineAdapter());

    [Fact]
    public void SelectBestCapture_NoCapturesAvailable_ReturnsNull()
    {
        // The starting position has 20 legal moves, all of them quiet pawn/knight moves.
        var result = _sut.SelectBestCapture(ChessConstants.StartingFen, PlayerColor.White);

        Assert.Null(result);
    }

    [Fact]
    public void SelectBestCapture_WrongSideToMove_ReturnsNull()
    {
        // Starting FEN has White to move; asking for Black yields no legal moves.
        var result = _sut.SelectBestCapture(ChessConstants.StartingFen, PlayerColor.Black);

        Assert.Null(result);
    }

    [Fact]
    public void SelectBestCapture_SingleCapture_ReturnsThatCapture()
    {
        // White knight on e4 can capture the black queen on d6; no other captures exist.
        const string fen = "k7/8/3q4/8/4N3/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("e4", result!.Move.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("d6", result.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        // Queen (900) minus knight (300).
        Assert.Equal(HeuristicMoveSelector.QueenValue - HeuristicMoveSelector.KnightValue, result.MaterialGain);
    }

    [Fact]
    public void SelectBestCapture_MultipleCaptures_ReturnsHighestGain()
    {
        // White pawn on e4 may capture the black rook on d5 (gain 400) or the black queen on f5
        // (gain 800). A quiet push to e5 is also legal but must not be selected.
        const string fen = "k7/8/8/3r1q2/4P3/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("e4", result!.Move.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("f5", result.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        // Queen (900) minus pawn (100) beats rook (500) minus pawn (100).
        Assert.Equal(HeuristicMoveSelector.QueenValue - HeuristicMoveSelector.PawnValue, result.MaterialGain);
    }

    [Fact]
    public void SelectBestCapture_NegativeGainCapture_IsStillReturned()
    {
        // The only capture is a queen taking a pawn (gain 100 - 900 = -800). Non-captures are
        // excluded, so the sole capture wins even though its material gain is negative.
        const string fen = "k7/3p4/8/8/8/8/8/3QK3 w - - 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("d1", result!.Move.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("d7", result.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HeuristicMoveSelector.PawnValue - HeuristicMoveSelector.QueenValue, result.MaterialGain);
        Assert.True(result.MaterialGain < 0);
    }

    [Fact]
    public void SelectBestCapture_EnPassant_IsClassifiedAsCapture()
    {
        // White just pushed e2-e4; black pawn on d4 may capture en passant on e3. That is the
        // only capture available, so it must be returned (gain 0, pawn for pawn).
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.Black);

        Assert.NotNull(result);
        Assert.Equal("d4", result!.Move.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("e3", result.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        // Pawn (100) minus pawn (100).
        Assert.Equal(0, result.MaterialGain);
    }

    [Fact]
    public void SelectBestCapture_EnPassantTargetWithoutPawnMove_IsNotTreatedAsCapture()
    {
        // Same en passant target square, but no double push has occurred so there is no capturable
        // pawn; black has no captures at all (the d4 push to d3 is a quiet non-capture).
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.Black);

        Assert.Null(result);
    }

    [Fact]
    public void SelectBestCapture_OnlyNonCaptures_ReturnsNull()
    {
        // Bare kings: the only legal moves are quiet king moves, none of which capture.
        const string fen = "k7/8/8/8/8/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestCapture(fen, PlayerColor.White);

        Assert.Null(result);
    }
}
