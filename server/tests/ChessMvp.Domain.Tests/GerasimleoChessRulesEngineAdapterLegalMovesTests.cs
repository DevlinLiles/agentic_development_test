using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class GerasimleoChessRulesEngineAdapterLegalMovesTests
{
    private readonly GerasimleoChessRulesEngineAdapter _sut = new();

    [Fact]
    public void GetLegalMoves_FromStartingPosition_ReturnsTwentyMoves()
    {
        var moves = _sut.GetLegalMoves(ChessConstants.StartingFen);

        // 16 pawn moves (8 single + 8 double) + 4 knight moves = 20 legal moves at the start.
        Assert.Equal(20, moves.Count);
        Assert.All(moves, m => Assert.Equal(MoveKind.Normal, m.Kind));
        Assert.All(moves, m => Assert.Null(m.Promotion));
    }

    [Fact]
    public void GetLegalMoves_NeverReturnsMoveThatLeavesOwnKingInCheck()
    {
        // White rook on e2 is pinned to the king on e1 by the black rook on e8. The only legal
        // moves are along the e-file; no sideways rook move may appear.
        const string fen = "4r3/8/8/8/8/8/4R3/4K3 w - - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        Assert.NotEmpty(moves);
        Assert.All(moves, m => Assert.True(m.FromSquare[0] == 'e' && m.ToSquare[0] == 'e'));
    }

    [Fact]
    public void GetLegalMoves_WhenInCheck_ReturnsOnlyMovesThatResolveCheck()
    {
        // White king on e1 in check from the black queen on e8. Legal moves must either capture
        // the queen, block on the e-file, or move the king off the e-file/away from the check.
        const string fen = "4q3/8/8/8/8/8/8/4K3 w - - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        Assert.NotEmpty(moves);
        // No move may leave the king on e1/e-file still in check — verify the post-move position
        // is never one where white's king remains en prise by re-applying each via TryApplyMove.
        foreach (var m in moves)
        {
            var result = _sut.TryApplyMove(fen, PlayerColor.White, m.FromSquare, m.ToSquare, m.Promotion);
            Assert.True(result.IsLegal, $"Move {m.FromSquare}-{m.ToSquare} should be legal.");
        }
    }

    [Fact]
    public void GetLegalMoves_IncludesCastlingWhenRightsAndClearPath()
    {
        const string fen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        var castles = moves.Where(m => m.Kind == MoveKind.Castling).ToList();
        Assert.Equal(2, castles.Count);
        Assert.Contains(castles, m => m.FromSquare == "e1" && m.ToSquare == "g1");
        Assert.Contains(castles, m => m.FromSquare == "e1" && m.ToSquare == "c1");
    }

    [Fact]
    public void GetLegalMoves_ExcludesCastlingThroughAttackedSquare()
    {
        // Black rook on f8 attacks f1, the square the white king passes through for kingside castling.
        const string fen = "5r1k/8/8/8/8/8/8/R3K2R w KQ - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        Assert.DoesNotContain(moves, m => m.FromSquare == "e1" && m.ToSquare == "g1" && m.Kind == MoveKind.Castling);
    }

    [Fact]
    public void GetLegalMoves_IncludesEnPassantCapture()
    {
        // White just pushed e2-e4; black pawn on d4 may capture en passant on e3.
        const string fen = "rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        var moves = _sut.GetLegalMoves(fen);

        var enPassant = moves.SingleOrDefault(m => m.Kind == MoveKind.EnPassant);
        Assert.NotNull(enPassant);
        Assert.Equal("d4", enPassant!.FromSquare);
        Assert.Equal("e3", enPassant.ToSquare);
    }

    [Fact]
    public void GetLegalMoves_ExpandsPromotionIntoFourPieces()
    {
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        var promotions = moves.Where(m => m.Kind == MoveKind.Promotion).ToList();
        Assert.Equal(4, promotions.Count);
        Assert.All(promotions, p =>
        {
            Assert.Equal("e7", p.FromSquare);
            Assert.Equal("e8", p.ToSquare);
            Assert.NotNull(p.Promotion);
        });
        Assert.Equal(
            new HashSet<PromotionPieceType>
            {
                PromotionPieceType.Queen,
                PromotionPieceType.Rook,
                PromotionPieceType.Bishop,
                PromotionPieceType.Knight,
            },
            promotions.Select(p => p.Promotion!.Value).ToHashSet());
    }

    [Fact]
    public void GetLegalMoves_IncludesPromotionCapture()
    {
        // White pawn on e7 can capture the rook on d8 and promote; all four promotions are legal.
        const string fen = "3rk3/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        var promotions = moves.Where(m => m.Kind == MoveKind.Promotion && m.ToSquare == "d8").ToList();
        Assert.Equal(4, promotions.Count);
    }

    [Fact]
    public void GetLegalMoves_InvalidFen_ReturnsEmpty()
    {
        var moves = _sut.GetLegalMoves("not a fen");

        Assert.Empty(moves);
    }

    [Fact]
    public void GetLegalMoves_PromotionEntriesAreIndividuallyApplicable()
    {
        const string fen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var moves = _sut.GetLegalMoves(fen);

        foreach (var promotion in new[] { PromotionPieceType.Queen, PromotionPieceType.Rook,
                                          PromotionPieceType.Bishop, PromotionPieceType.Knight })
        {
            var result = _sut.TryApplyMove(fen, PlayerColor.White, "e7", "e8", promotion);
            Assert.True(result.IsLegal, $"Promotion to {promotion} should be legal.");
        }
    }
}
