using ChessMvp.ChessAi;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using NSubstitute;
using Xunit;

namespace ChessMvp.ChessAi.Tests;

public class HeuristicChessAiPlayerTests
{
    private const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string PromotionFen = "k7/4P3/8/8/8/8/8/4K3 w - - 0 1";

    // ---- Contract: implements IChessAiPlayer ---------------------------------------------

    [Fact]
    public void Implements_IChessAiPlayer()
    {
        IChessAiPlayer sut = new HeuristicChessAiPlayer(
            Substitute.For<IChessRulesEngine>(),
            Substitute.For<IHeuristicEvaluator>());

        Assert.NotNull(sut);
    }

    // ---- No-legal-moves / invalid / cancellation -----------------------------------------

    [Fact]
    public void SelectMove_NoLegalMoves_ReturnsNoLegalMovesAndClearsCandidates()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var result = sut.SelectMove(StartingFen, PlayerColor.White, Array.Empty<LegalMove>());

        Assert.Equal(AiMoveSelectionStatus.NoLegalMoves, result.Status);
        Assert.Null(result.Move);
        Assert.Null(sut.GetLastCandidates());
        // The rules engine and evaluator must never be consulted when there is nothing to search.
        rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        evaluator.DidNotReceive().Evaluate(Arg.Any<string>(), Arg.Any<PlayerColor>());
    }

    [Theory]
    [InlineData("")]                                         // blank
    [InlineData("   ")]                                      // whitespace only
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")] // missing side-to-move field
    [InlineData("not/valid w - - 0 1")]                      // wrong number of ranks
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")] // bad side char
    public void SelectMove_InvalidFen_ReturnsInvalidPosition(string fen)
    {
        var sut = BuildWithStubs(out _, out _);
        var moves = new[] { new LegalMove("e2", "e4", "e4", false) };

        var result = sut.SelectMove(fen, PlayerColor.White, moves);

        Assert.Equal(AiMoveSelectionStatus.InvalidPosition, result.Status);
        Assert.Null(result.Move);
    }

    [Fact]
    public void SelectMove_AlreadyCancelled_ReturnsFailed()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var moves = new[] { new LegalMove("e2", "e4", "e4", false) };

        var result = sut.SelectMove(StartingFen, PlayerColor.White, moves, cts.Token);

        Assert.Equal(AiMoveSelectionStatus.Failed, result.Status);
        rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        evaluator.DidNotReceive().Evaluate(Arg.Any<string>(), Arg.Any<PlayerColor>());
    }

    // ---- Greedy selection: highest score wins -------------------------------------------

    [Fact]
    public void SelectMove_PicksHighestScoredMoveForSideToMove()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        // Three legal moves; the rules engine applies each to a distinct resulting FEN.
        var e2e4 = new LegalMove("e2", "e4", "e4", false);
        var d2d4 = new LegalMove("d2", "d4", "d4", false);
        var g1f3 = new LegalMove("g1", "f3", "Nf3", false);

        StubApply(rulesEngine, e2e4, "fen-after-e4");
        StubApply(rulesEngine, d2d4, "fen-after-d4");
        StubApply(rulesEngine, g1f3, "fen-after-Nf3");

        // Nf3 scores highest => it must be selected.
        evaluator.Evaluate("fen-after-e4", PlayerColor.White).Returns(10);
        evaluator.Evaluate("fen-after-d4", PlayerColor.White).Returns(20);
        evaluator.Evaluate("fen-after-Nf3", PlayerColor.White).Returns(50);

        var result = sut.SelectMove(StartingFen, PlayerColor.White, new[] { e2e4, d2d4, g1f3 });

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Same(g1f3, result.Move!.Move);
        Assert.Equal(50, result.Move.Score);
        Assert.Equal(3, result.Move.ConsideredMoveCount);
    }

    // ---- Deterministic tie-breaking ------------------------------------------------------

    [Fact]
    public void SelectMove_TiesInScore_AreBrokenByAscendingSan()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        // Two moves with identical scores. "Nf3" sorts before "e4" under ordinal comparison
        // ('N' == 78 < 'e' == 101), so Nf3 must be chosen regardless of input order.
        var e4 = new LegalMove("e2", "e4", "e4", false);
        var nf3 = new LegalMove("g1", "f3", "Nf3", false);

        StubApply(rulesEngine, e4, "fen-e4");
        StubApply(rulesEngine, nf3, "fen-nf3");
        evaluator.Evaluate(Arg.Any<string>(), PlayerColor.White).Returns(42);

        // Same scores, deliberately supplied in the opposite of the tie-break order.
        var result = sut.SelectMove(StartingFen, PlayerColor.White, new[] { e4, nf3 });

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Same(nf3, result.Move!.Move);
    }

    [Fact]
    public void SelectMove_TieBreaking_IsDeterministicAcrossInputOrderings()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var a = new LegalMove("a2", "a3", "a3", false);
        var b = new LegalMove("b2", "b3", "b3", false);
        var c = new LegalMove("c2", "c3", "c3", false);

        StubApply(rulesEngine, a, "fa");
        StubApply(rulesEngine, b, "fb");
        StubApply(rulesEngine, c, "fc");
        evaluator.Evaluate(Arg.Any<string>(), PlayerColor.White).Returns(7);

        var orderings = new[]
        {
            new[] { a, b, c },
            new[] { c, b, a },
            new[] { b, a, c },
            new[] { c, a, b },
        };

        foreach (var ordering in orderings)
        {
            var result = sut.SelectMove(StartingFen, PlayerColor.White, ordering);
            Assert.Same(a, result.Move!.Move);
        }
    }

    // ---- Candidates published best-first ------------------------------------------------

    [Fact]
    public void GetLastCandidates_ReturnsAllScoredMovesBestFirst()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var e4 = new LegalMove("e2", "e4", "e4", false);
        var d4 = new LegalMove("d2", "d4", "d4", false);
        var nf3 = new LegalMove("g1", "f3", "Nf3", false);

        StubApply(rulesEngine, e4, "fe4");
        StubApply(rulesEngine, d4, "fd4");
        StubApply(rulesEngine, nf3, "fnf3");
        evaluator.Evaluate("fe4", PlayerColor.White).Returns(1);
        evaluator.Evaluate("fd4", PlayerColor.White).Returns(30);
        evaluator.Evaluate("fnf3", PlayerColor.White).Returns(20);

        sut.SelectMove(StartingFen, PlayerColor.White, new[] { e4, nf3, d4 });

        var candidates = sut.GetLastCandidates();
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates!.Count);
        Assert.Same(d4, candidates[0].Move);      // highest score (30)
        Assert.Equal(30, candidates[0].Score);
        Assert.Same(nf3, candidates[1].Move);     // 20
        Assert.Same(e4, candidates[2].Move);      // 1
    }

    [Fact]
    public void GetLastCandidates_IsNullBeforeAnySearch()
    {
        var sut = BuildWithStubs(out _, out _);
        Assert.Null(sut.GetLastCandidates());
    }

    // ---- Promotion: default queen, knight when strictly better, configurable ------------

    [Fact]
    public void SelectMove_PromotionDefaultsToQueen()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var promote = new LegalMove("e7", "e8", "e8=Q", IsPromotion: true);

        // Under the default policy the player applies both queen and knight to compare them, and
        // uses the chosen piece's score as the candidate score.
        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Queen, "fen-q");
        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Knight, "fen-n");
        evaluator.Evaluate("fen-q", PlayerColor.White).Returns(900);
        evaluator.Evaluate("fen-n", PlayerColor.White).Returns(800); // queen wins

        var result = sut.SelectMove(PromotionFen, PlayerColor.White, new[] { promote });

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Equal(PromotionPieceType.Queen, result.Move!.PromotionPiece);
        Assert.Equal(900, result.Move.Score);
    }

    [Fact]
    public void SelectMove_PromotionUnderpromotesToKnightWhenStrictlyBetter()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var promote = new LegalMove("e7", "e8", "e8=N", IsPromotion: true);

        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Queen, "fen-q");
        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Knight, "fen-n");
        evaluator.Evaluate("fen-q", PlayerColor.White).Returns(100);
        evaluator.Evaluate("fen-n", PlayerColor.White).Returns(300); // knight strictly better

        var result = sut.SelectMove(PromotionFen, PlayerColor.White, new[] { promote });

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Equal(PromotionPieceType.Knight, result.Move!.PromotionPiece);
        Assert.Equal(300, result.Move.Score);
    }

    [Fact]
    public void SelectMove_PromotionTie_FavoursQueen()
    {
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var promote = new LegalMove("e7", "e8", "e8=Q", IsPromotion: true);

        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Queen, "fen-q");
        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Knight, "fen-n");
        evaluator.Evaluate("fen-q", PlayerColor.White).Returns(500);
        evaluator.Evaluate("fen-n", PlayerColor.White).Returns(500); // equal => default queen

        var result = sut.SelectMove(PromotionFen, PlayerColor.White, new[] { promote });

        Assert.Equal(PromotionPieceType.Queen, result.Move!.PromotionPiece);
        Assert.Equal(500, result.Move.Score);
    }

    [Fact]
    public void SelectMove_ConfiguredPromotionPiece_AlwaysUsedAndSkipsComparison()
    {
        var re = Substitute.For<IChessRulesEngine>();
        var ev = Substitute.For<IHeuristicEvaluator>();
        var player = new HeuristicChessAiPlayer(re, ev,
            new HeuristicChessAiPlayerOptions { PromotionPiece = PromotionPieceType.Rook });

        var promote = new LegalMove("e7", "e8", "e8=R", IsPromotion: true);
        StubApplyForPiece(re, promote, PromotionPieceType.Rook, "fen-r");
        ev.Evaluate("fen-r", PlayerColor.White).Returns(123);

        var result = player.SelectMove(PromotionFen, PlayerColor.White, new[] { promote });

        Assert.Equal(PromotionPieceType.Rook, result.Move!.PromotionPiece);
        Assert.Equal(123, result.Move.Score);

        // Queen/knight must never have been tried under the configured policy.
        re.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(),
            Arg.Any<string>(), Arg.Any<string>(), PromotionPieceType.Queen);
        re.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(),
            Arg.Any<string>(), Arg.Any<string>(), PromotionPieceType.Knight);
    }

    [Fact]
    public void SelectMove_PromotionAmongSeveralMoves_CompetesByScore()
    {
        // A quiet knight move scores higher than a queen promotion here, so the greedy player
        // should prefer the knight move over promoting.
        var sut = BuildWithStubs(out var rulesEngine, out var evaluator);

        var promote = new LegalMove("e7", "e8", "e8=Q", IsPromotion: true);
        var knightHop = new LegalMove("g1", "f3", "Nf3", false);

        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Queen, "fen-q");
        StubApplyForPiece(rulesEngine, promote, PromotionPieceType.Knight, "fen-n");
        StubApply(rulesEngine, knightHop, "fen-nf3");
        evaluator.Evaluate("fen-q", PlayerColor.White).Returns(50);
        evaluator.Evaluate("fen-n", PlayerColor.White).Returns(40);
        evaluator.Evaluate("fen-nf3", PlayerColor.White).Returns(80); // knight move wins overall

        var result = sut.SelectMove(PromotionFen, PlayerColor.White, new[] { promote, knightHop });

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Same(knightHop, result.Move!.Move);
        Assert.Null(result.Move.PromotionPiece);
    }

    // ---- End-to-end with the real rules engine + heuristic evaluator ---------------------

    [Fact]
    public void SelectMove_RealEngine_CapturesFreePieceGreedily()
    {
        // White knight on e4 can capture a free black rook on d6 (a 500cp gain) versus a quiet
        // move. The greedy 1-ply player should grab the rook.
        const string fen = "k7/8/3r4/8/4N3/8/8/4K3 w - - 0 1";
        var rulesEngine = new GerasimleoChessRulesEngineAdapter();
        var evaluator = new HeuristicEvaluator(rulesEngine);
        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        var legalMoves = rulesEngine.GetAllLegalMoves(fen);
        Assert.NotEmpty(legalMoves);

        var result = sut.SelectMove(fen, PlayerColor.White, legalMoves);

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.Equal("d6", result.Move!.Move.ToSquare);
        Assert.Equal("e4", result.Move.Move.FromSquare);
    }

    [Fact]
    public void SelectMove_RealEngine_PromotesToQueenByDefault()
    {
        // White pawn on e7 can promote. The default policy should pick a queen promotion.
        var rulesEngine = new GerasimleoChessRulesEngineAdapter();
        var evaluator = new HeuristicEvaluator(rulesEngine);
        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        var legalMoves = rulesEngine.GetAllLegalMoves(PromotionFen);
        Assert.NotEmpty(legalMoves);

        var result = sut.SelectMove(PromotionFen, PlayerColor.White, legalMoves);

        Assert.Equal(AiMoveSelectionStatus.MoveSelected, result.Status);
        Assert.True(result.Move!.Move.IsPromotion);
        Assert.Equal(PromotionPieceType.Queen, result.Move.PromotionPiece);
    }

    // ---- Helpers --------------------------------------------------------------------------

    private static HeuristicChessAiPlayer BuildWithStubs(
        out IChessRulesEngine rulesEngine, out IHeuristicEvaluator evaluator)
    {
        rulesEngine = Substitute.For<IChessRulesEngine>();
        evaluator = Substitute.For<IHeuristicEvaluator>();
        return new HeuristicChessAiPlayer(rulesEngine, evaluator);
    }

    private static void StubApply(
        IChessRulesEngine rulesEngine, LegalMove move, string resultingFen)
    {
        rulesEngine.TryApplyMove(
                Arg.Any<string>(), Arg.Any<PlayerColor>(),
                move.FromSquare, move.ToSquare, Arg.Any<PromotionPieceType?>())
            .Returns(new MoveApplicationResult { IsLegal = true, ResultingFen = resultingFen });
    }

    private static void StubApplyForPiece(
        IChessRulesEngine rulesEngine, LegalMove move,
        PromotionPieceType piece, string resultingFen)
    {
        rulesEngine.TryApplyMove(
                Arg.Any<string>(), Arg.Any<PlayerColor>(),
                move.FromSquare, move.ToSquare, piece)
            .Returns(new MoveApplicationResult { IsLegal = true, ResultingFen = resultingFen });
    }
}
