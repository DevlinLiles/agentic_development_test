using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessAi;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class GreedyHeuristicChessAiTests
{
    private readonly GreedyHeuristicChessAi _sut = new();

    [Fact]
    public void ChooseMove_FromStartingPosition_ReturnsALegalOpeningMove()
    {
        var move = _sut.ChooseMove(ChessConstants.StartingFen);

        Assert.NotNull(move);
        // Every legal opening move pushes a pawn or a knight from its starting square.
        Assert.Contains(move!.FromSquare, new[] { "a2", "b2", "c2", "d2", "e2", "f2", "g2", "h2", "b1", "g1" });
    }

    [Fact]
    public void ChooseMove_CapturesAFreeQueenWhenOneIsAvailable()
    {
        // White queen hanging on d4; it's Black's turn and the knight on f5 can take it for free.
        const string fen = "3rk2r/8/8/5N2/3Q4/8/8/4K3 b kq - 0 1";

        var move = _sut.ChooseMove(fen);

        Assert.NotNull(move);
        Assert.Equal("f5", move!.FromSquare);
        Assert.Equal("d4", move.ToSquare);
    }

    [Fact]
    public void ChooseMove_PlaysBackRankCheckmateWhenOneIsOnTheBoard()
    {
        // Black king boxed in by its own pawns on the back rank; White's rook delivers Ra8#.
        const string fen = "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1";

        var move = _sut.ChooseMove(fen);

        Assert.NotNull(move);
        Assert.Equal("a1", move!.FromSquare);
        Assert.Equal("a8", move.ToSquare);
    }

    [Fact]
    public void ChooseMove_WhenNoLegalMovesExist_ReturnsNull()
    {
        // Black king on a8, white queen on c7 and white king on b6: black is in stalemate
        // (no legal moves, not in check). After the move that produced this FEN the rules
        // engine already flagged stalemate, so the AI has nothing to play.
        const string fen = "k7/2Q5/1K6/8/8/8/8/8 b - - 0 1";

        var move = _sut.ChooseMove(fen);

        Assert.Null(move);
    }

    [Fact]
    public void ChooseMove_OnPromotion_ReportsQueenPromotion()
    {
        // White pawn on e7 with a clear path to e8 (black king out of the way on h8); the AI
        // should push and promote, defaulting to a queen.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var move = _sut.ChooseMove(fen);

        Assert.NotNull(move);
        Assert.Equal("e7", move!.FromSquare);
        Assert.Equal("e8", move.ToSquare);
        Assert.Equal(PromotionPieceType.Queen, move.Promotion);
    }

    [Fact]
    public void ChooseMove_IsDeterministic_AcrossRepeatedCalls()
    {
        // The same position must always yield the same move so AI games and tests are reproducible.
        var first = _sut.ChooseMove(ChessConstants.StartingFen);
        var second = _sut.ChooseMove(ChessConstants.StartingFen);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);
    }
}
