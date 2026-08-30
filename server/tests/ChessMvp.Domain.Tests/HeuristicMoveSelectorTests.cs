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

        var result = _sut.SelectBestCapture(fen, PlayerColor.Black == PlayerColor.White ? PlayerColor.Black : PlayerColor.White);
        // The position above is White to move; exercise it as White.
        result = _sut.SelectBestCapture(fen, PlayerColor.White);

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

    // --- Combined stage: SelectBestMove (material gain + tie-breakers) ---

    [Fact]
    public void SelectBestMove_NoLegalMoves_ReturnsNull()
    {
        // Starting FEN has White to move; asking for Black yields no legal moves.
        var result = _sut.SelectBestMove(ChessConstants.StartingFen, PlayerColor.Black);

        Assert.Null(result);
    }

    [Fact]
    public void SelectBestMove_WithCapture_ReturnsHighestGainCapture()
    {
        // Same position as the single-capture test: a knight taking a queen (gain 600) dwarfs any
        // quiet tie-breaker, so the capture-stage score dominates the combined score.
        const string fen = "k7/8/3q4/8/4N3/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestMove(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("d6", result!.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HeuristicMoveSelector.QueenValue - HeuristicMoveSelector.KnightValue, result.MaterialGain);
    }

    [Fact]
    public void SelectBestMove_Check_IsScoredAsPositiveTieBreaker()
    {
        // White rook on h5 (already off its home rank, so no development bonus) can move to a8 or
        // h8 along the files/ranks; both Ra5 and Rh8 deliver check to the black king on a8, while
        // the remaining rook moves are quiet. Every relevant move has zero material gain, zero
        // development and zero central-control change, so check is the sole deciding tie-breaker.
        const string fen = "k7/8/8/8/7R/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestMove(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("h5", result!.Move.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, result.MaterialGain);
        Assert.Equal(0, result.DevelopmentBonus);
        Assert.Equal(0, result.CentralControlBonus);
        Assert.Equal(HeuristicMoveSelector.CheckBonus, result.CheckBonus);
        Assert.True(result.CheckBonus > 0);
    }

    [Fact]
    public void SelectBestMove_Development_RewardsPieceLeavingStartingArea()
    {
        // From the starting position the only non-pawn/non-king pieces that can move are the
        // knights. A knight developing off its home rank (e.g. Ng1-f3 or Nb1-c3) earns the
        // development bonus plus a little central control, beating every quiet pawn push.
        var result = _sut.SelectBestMove(ChessConstants.StartingFen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal(0, result!.MaterialGain);
        Assert.Equal(HeuristicMoveSelector.DevelopmentBonus, result.DevelopmentBonus);
        Assert.Contains(
            result.Move.FromSquare,
            new[] { "b1", "g1" },
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectBestMove_CentralControl_RewardsMovesTowardCenter()
    {
        // A white knight on a3 (already off its home rank, so no development bonus) can jump to
        // b5, c4, c2 or b1. c4 is the most central of those squares, so it wins on central
        // control alone — no capture, no check, no development.
        const string fen = "7k/8/8/8/8/N7/8/7K w - - 0 1";

        var result = _sut.SelectBestMove(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("c4", result!.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, result.MaterialGain);
        Assert.Equal(0, result.DevelopmentBonus);
        Assert.Equal(0, result.CheckBonus);
        // c4 is one Chebyshev step from the center, a3 is three: (3 - 1) * weight.
        Assert.Equal(
            (HeuristicMoveSelector.CentralControlRingRadius - 1) * HeuristicMoveSelector.CentralControlWeight,
            result.CentralControlBonus);
        Assert.True(result.CentralControlBonus > 0);
    }

    [Fact]
    public void SelectBestMove_QueenPromotion_PreferredOverUnderPromotion()
    {
        // White pawn on e7 pushes to e8 with four legal promotions (Q/R/B/N), all non-captures so
        // all share material gain zero. The queen promotion bonus is largest, so a queen is chosen.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var result = _sut.SelectBestMove(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal(PromotionPieceType.Queen, result!.Move.Promotion);
        Assert.Equal("e8", result.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, result.MaterialGain);
        Assert.Equal(HeuristicMoveSelector.QueenValue - HeuristicMoveSelector.PawnValue, result.QueenPromotionBonus);
    }

    [Fact]
    public void SelectBestMove_TieBreakers_ResolveEqualMaterialGain()
    {
        // Two white pawns can each capture a black queen (gain 900 - 100 = 800). Both captures
        // improve central control by the same amount, but only c4xd5 gives check (the d5 pawn
        // attacks the black king on e6). The check tie-breaker breaks the material-gain tie.
        const string fen = "8/8/4k3/3qq3/2P2P2/8/8/7K w - - 0 1";

        var result = _sut.SelectBestMove(fen, PlayerColor.White);

        Assert.NotNull(result);
        Assert.Equal("d5", result!.Move.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(HeuristicMoveSelector.QueenValue - HeuristicMoveSelector.PawnValue, result.MaterialGain);
        Assert.Equal(HeuristicMoveSelector.CheckBonus, result.CheckBonus);
        Assert.True(result.CheckBonus > 0);
    }
}
