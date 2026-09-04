using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessAi;
using ChessMvp.Infrastructure.ChessEvaluation;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicChessAiPlayerTests
{
    // The real rules engine is used so the search exercises genuine legal-move generation, move
    // application, and promotion resolution end-to-end rather than mocked behavior.
    private readonly HeuristicChessAiPlayer _sut =
        new(new GerasimleoChessRulesEngineAdapter(), new HeuristicBoardEvaluator());

    private static IChessAiPlayer CreateSut() =>
        new HeuristicChessAiPlayer(new GerasimleoChessRulesEngineAdapter(), new HeuristicBoardEvaluator());

    [Fact]
    public async Task ChooseMoveAsync_ImplementsIChessAiPlayer()
    {
        Assert.IsAssignableFrom<IChessAiPlayer>(CreateSut());
    }

    [Fact]
    public async Task ChooseMoveAsync_StartingPosition_PicksALegalMove()
    {
        var result = await _sut.ChooseMoveAsync(ChessConstants.StartingFen, PlayerColor.White);

        Assert.True(result.Success, result.Reason);
        Assert.NotNull(result.Move);

        var legal = new GerasimleoChessRulesEngineAdapter()
            .GetAllLegalMoves(ChessConstants.StartingFen)
            .Any(m => string.Equals(m.FromSquare, result.Move!.FromSquare, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(m.ToSquare, result.Move!.ToSquare, StringComparison.OrdinalIgnoreCase));

        Assert.True(legal, $"Chosen move {result.Move!.FromSquare}-{result.Move!.ToSquare} is not in the legal-move set.");
    }

    [Fact]
    public async Task ChooseMoveAsync_StartingPosition_DeterministicAcrossCalls()
    {
        // Same position must always resolve to the same move: the greedy search plus lexicographic
        // tie-break must be stable regardless of how many times it runs.
        var first = await _sut.ChooseMoveAsync(ChessConstants.StartingFen, PlayerColor.White);
        var second = await _sut.ChooseMoveAsync(ChessConstants.StartingFen, PlayerColor.White);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Move!.FromSquare, second.Move!.FromSquare);
        Assert.Equal(first.Move!.ToSquare, second.Move!.ToSquare);
        Assert.Equal(first.Move!.Promotion, second.Move!.Promotion);
    }

    [Fact]
    public async Task ChooseMoveAsync_CapturesHangingQueen_WhenAvailable()
    {
        // A black queen sits undefended on d4; White's knight on f3 can capture it (Nf3xd4). With no
        // recapture available (the black king is far away on e8), the queen capture is a ~900cp
        // material swing — far above any quiet move — so the greedy 1-ply search must play it.
        const string fen = "4k3/8/8/8/3q4/5N2/8/4K3 w - - 0 1";

        var result = await _sut.ChooseMoveAsync(fen, PlayerColor.White);

        Assert.True(result.Success, result.Reason);
        Assert.Equal("f3", result.Move!.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("d4", result.Move!.ToSquare, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChooseMoveAsync_InvalidFen_ReturnsFailureWithoutThrowing()
    {
        // Lenient FEN handling mirrors the rules engine: a bad/empty FEN is an abstention, not an
        // exception.
        var ex = await Record.ExceptionAsync(() => _sut.ChooseMoveAsync("not a fen", PlayerColor.White));
        Assert.Null(ex);

        var result = await _sut.ChooseMoveAsync("not a fen", PlayerColor.White);
        Assert.False(result.Success);
        Assert.Null(result.Move);
    }

    [Fact]
    public async Task ChooseMoveAsync_Promotion_CommitsToQueenAsMaterialMaximizer()
    {
        // White pawn on e7 promotes with the black king safely far away on h8. The evaluator is a
        // pure material/position/mobility function (it does not detect checkmate), so the queen
        // promotion (900cp) outscores rook/bishop/knight and is committed as part of the move —
        // proving each promotion piece was evaluated rather than the move defaulting to no piece.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var result = await _sut.ChooseMoveAsync(fen, PlayerColor.White);

        Assert.True(result.Success, result.Reason);
        Assert.Equal("e7", result.Move!.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("e8", result.Move!.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(PromotionPieceType.Queen, result.Move!.Promotion);
    }

    [Fact]
    public async Task ChooseMoveAsync_Promotion_AlwaysResolvesAPromotionPiece()
    {
        // A promotion move in a more cluttered position must still arrive with a resolved
        // promotion piece (queen/rook/bishop/knight) — never null — regardless of which piece wins.
        const string fen = "3rk3/4P3/8/8/8/8/8/4K3 w - - 0 1";

        var result = await _sut.ChooseMoveAsync(fen, PlayerColor.White);

        Assert.True(result.Success, result.Reason);
        Assert.Equal("e8", result.Move!.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(result.Move!.Promotion);
    }

    [Fact]
    public async Task ChooseMoveAsync_RecordsResultingFenAndDiagnostics()
    {
        var result = await _sut.ChooseMoveAsync(ChessConstants.StartingFen, PlayerColor.White);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ResultingFen));
        Assert.NotNull(result.Diagnostics);
        Assert.Equal("1-ply-greedy", result.Diagnostics!["strategy"]);
    }
}
