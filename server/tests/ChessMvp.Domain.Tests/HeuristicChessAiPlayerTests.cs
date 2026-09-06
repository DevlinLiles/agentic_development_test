using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicChessAiPlayerTests
{
    // The player needs a real rules engine to apply moves and produce resulting FENs. It is
    // stateless and FEN-in/FEN-out, so using the concrete adapter keeps the tests honest about
    // behaviour while still exercising the evaluator seam through a stub where useful.
    private readonly IChessRulesEngine _rulesEngine = new GerasimleoChessRulesEngineAdapter();

    private static IReadOnlyList<LegalMove> LegalMovesFor(string fen, IChessRulesEngine engine) =>
        engine.GetAllLegalMoves(fen);

    private static ChessAiMoveRequest RequestFor(string fen, PlayerColor side, IChessRulesEngine engine) =>
        new()
        {
            Fen = fen,
            SideToMove = side,
            LegalMoves = LegalMovesFor(fen, engine),
        };

    [Fact]
    public void Implements_IChessAiPlayer()
    {
        IChessAiPlayer sut = new HeuristicChessAiPlayer(_rulesEngine, Substitute.For<IChessBoardEvaluator>());

        Assert.IsAssignableFrom<IChessAiPlayer>(sut);
    }

    [Fact]
    public async Task ChooseMove_StartingPosition_ReturnsALegalMoveWithEvaluation()
    {
        var evaluator = new ChessBoardEvaluator(_rulesEngine);
        var sut = new HeuristicChessAiPlayer(_rulesEngine, evaluator);
        var request = RequestFor(ChessConstants.StartingFen, PlayerColor.White, _rulesEngine);

        var result = await sut.ChooseMoveAsync(request);

        Assert.False(string.IsNullOrEmpty(result.FromSquare));
        Assert.False(string.IsNullOrEmpty(result.ToSquare));
        // The result must be one of the supplied legal moves.
        Assert.Contains(
            request.LegalMoves,
            m => m.FromSquare == result.FromSquare && m.ToSquare == result.ToSquare);
        Assert.Null(result.Promotion);
    }

    [Fact]
    public async Task ChooseMove_SamePositionAlwaysYieldsSameMove_IsDeterministic()
    {
        var evaluator = new ChessBoardEvaluator(_rulesEngine);
        var sut = new HeuristicChessAiPlayer(_rulesEngine, evaluator);
        var request = RequestFor(ChessConstants.StartingFen, PlayerColor.White, _rulesEngine);

        var first = await sut.ChooseMoveAsync(request);
        var second = await sut.ChooseMoveAsync(request);

        Assert.Equal(first.FromSquare, second.FromSquare);
        Assert.Equal(first.ToSquare, second.ToSquare);
        Assert.Equal(first.Promotion, second.Promotion);
        Assert.Equal(first.Evaluation, second.Evaluation);
    }

    [Fact]
    public async Task ChooseMove_DeterministicRegardlessOfMoveEnumerationOrder()
    {
        // A stubbed evaluator that returns a constant score for every position collapses all moves
        // into a tie. The only remaining differentiator is the deterministic move-string tie-break,
        // so the result must be the lexicographically smallest move regardless of input order.
        const string fen = ChessConstants.StartingFen;
        var rulesEngine = Substitute.For<IChessRulesEngine>();

        // Build two different enumerations of the same starting-position legal moves.
        var moves = LegalMovesFor(fen, _rulesEngine).ToList();
        var reversed = moves.AsEnumerable().Reverse().ToList();

        // The stubbed engine reports every move as legal with some resulting FEN, so the player
        // can ask the evaluator for a score. ReturnsForAnyArgs ignores the argument specification.
        rulesEngine.TryApplyMove(default!, default, default!, default!, default)
            .ReturnsForAnyArgs(_ => new MoveApplicationResult
            {
                IsLegal = true,
                ResultingFen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1",
            });

        var evaluator = Substitute.For<IChessBoardEvaluator>();
        evaluator.Evaluate(Arg.Any<string>()).Returns(0); // every position ties at 0

        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        var reqNormal = new ChessAiMoveRequest
        {
            Fen = fen,
            SideToMove = PlayerColor.White,
            LegalMoves = moves,
        };
        var reqReversed = new ChessAiMoveRequest
        {
            Fen = fen,
            SideToMove = PlayerColor.White,
            LegalMoves = reversed,
        };

        var normal = await sut.ChooseMoveAsync(reqNormal);
        var reversedResult = await sut.ChooseMoveAsync(reqReversed);

        Assert.Equal(normal.FromSquare, reversedResult.FromSquare);
        Assert.Equal(normal.ToSquare, reversedResult.ToSquare);

        // And the chosen move should be the lexicographically smallest legal move.
        var smallest = moves
            .Select(m => (m.FromSquare + m.ToSquare).ToLowerInvariant())
            .OrderBy(k => k, StringComparer.Ordinal)
            .First();
        Assert.Equal(smallest, (normal.FromSquare + normal.ToSquare).ToLowerInvariant());
    }

    [Fact]
    public async Task ChooseMove_SelectsHighestEvaluatedMove()
    {
        // White: king on e1, rook on a1, pawn on e2; Black: king on e8, queen on a8. The rook can
        // capture the queen for free (Rxa8), which must score highest and be selected.
        const string fen = "q3k3/8/8/8/8/8/4P3/R3K3 w - - 0 1";
        var evaluator = new ChessBoardEvaluator(_rulesEngine);
        var sut = new HeuristicChessAiPlayer(_rulesEngine, evaluator);
        var request = RequestFor(fen, PlayerColor.White, _rulesEngine);

        var result = await sut.ChooseMoveAsync(request);

        Assert.Equal("a1", result.FromSquare);
        Assert.Equal("a8", result.ToSquare);
        Assert.True(result.Evaluation > 0, $"Expected a positive evaluation for winning a queen, got {result.Evaluation}.");
    }

    [Fact]
    public async Task ChooseMove_PromotionDefaultsToQueen()
    {
        // White pawn on e7 promoting to the empty e8 square (black king parked on h8 so e8 is
        // open). With all four promotion pieces legal, the player must default to Queen.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";
        var evaluator = new ChessBoardEvaluator(_rulesEngine);
        var sut = new HeuristicChessAiPlayer(_rulesEngine, evaluator);
        var request = RequestFor(fen, PlayerColor.White, _rulesEngine);

        var result = await sut.ChooseMoveAsync(request);

        // The promotion move e7-e8 should be selected (promoting to a queen is the best outcome),
        // and with all four promotion pieces legal it must default to Queen.
        Assert.Equal("e7", result.FromSquare);
        Assert.Equal("e8", result.ToSquare);
        Assert.Equal(PromotionPieceType.Queen, result.Promotion);
    }

    [Fact]
    public async Task ChooseMove_DoesNotMutateCallerBoard_FenIsImmutable()
    {
        // The player only ever receives a FEN string and legal moves; it cannot mutate the caller's
        // state. Confirm the request FEN is unchanged after a move is chosen, and that re-deriving
        // legal moves from the original FEN afterwards yields the same set (proving no leak).
        const string fen = ChessConstants.StartingFen;
        var evaluator = new ChessBoardEvaluator(_rulesEngine);
        var sut = new HeuristicChessAiPlayer(_rulesEngine, evaluator);
        var request = RequestFor(fen, PlayerColor.White, _rulesEngine);
        var originalFen = request.Fen;
        var originalMoves = request.LegalMoves.Select(m => (m.FromSquare, m.ToSquare)).ToList();

        _ = await sut.ChooseMoveAsync(request);

        Assert.Equal(originalFen, request.Fen);
        var movesAfter = LegalMovesFor(request.Fen, _rulesEngine)
            .Select(m => (m.FromSquare, m.ToSquare)).ToList();

        // The underlying rules engine may enumerate the same legal moves in a different order on a
        // fresh board load, so the immutability contract is about the *set* of moves being
        // unchanged rather than their enumeration order. Compare as a set to honour that contract.
        Assert.Equal(
            originalMoves.OrderBy(m => m.FromSquare, StringComparer.Ordinal).ThenBy(m => m.ToSquare, StringComparer.Ordinal),
            movesAfter.OrderBy(m => m.FromSquare, StringComparer.Ordinal).ThenBy(m => m.ToSquare, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ChooseMove_NoLegalMoves_Throws()
    {
        var sut = new HeuristicChessAiPlayer(_rulesEngine, Substitute.For<IChessBoardEvaluator>());
        var request = new ChessAiMoveRequest
        {
            Fen = ChessConstants.StartingFen,
            SideToMove = PlayerColor.White,
            LegalMoves = Array.Empty<LegalMove>(),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ChooseMoveAsync(request));
    }

    [Fact]
    public async Task ChooseMove_NullRequest_Throws()
    {
        var sut = new HeuristicChessAiPlayer(_rulesEngine, Substitute.For<IChessBoardEvaluator>());

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ChooseMoveAsync(null!));
    }

    [Fact]
    public async Task ChooseMove_PromotionWhenOnlyOnePieceLegal_SelectsThatPiece()
    {
        // Drive the player with a stubbed rules engine so we can make exactly one promotion piece
        // legal. The player probes Queen, Rook, Bishop, Knight; when only Knight is legal it must
        // return Knight rather than defaulting to Queen.
        const string fen = "7k/4P3/8/8/8/8/8/4K3 w - - 0 1";
        var rulesEngine = Substitute.For<IChessRulesEngine>();

        // Use argument matchers for ALL parameters (NSubstitute requires consistency) and decide
        // legality from the promotion argument captured in the call info.
        rulesEngine.TryApplyMove(
                Arg.Any<string>(), Arg.Any<PlayerColor>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PromotionPieceType?>())
            .Returns(callInfo =>
            {
                var promotion = callInfo.Arg<PromotionPieceType?>();
                return promotion == PromotionPieceType.Knight
                    ? new MoveApplicationResult
                    {
                        IsLegal = true,
                        ResultingFen = "7k/4N3/8/8/8/8/8/4K3 b - - 0 1",
                    }
                    : new MoveApplicationResult
                    {
                        IsLegal = false,
                        FailureReason = MoveFailureReason.IllegalMove,
                    };
            });

        var evaluator = Substitute.For<IChessBoardEvaluator>();
        evaluator.Evaluate(Arg.Any<string>()).Returns(10);

        var sut = new HeuristicChessAiPlayer(rulesEngine, evaluator);

        var request = new ChessAiMoveRequest
        {
            Fen = fen,
            SideToMove = PlayerColor.White,
            LegalMoves = new List<LegalMove>
            {
                new() { FromSquare = "e7", ToSquare = "e8", IsPromotion = true },
            },
        };

        var result = await sut.ChooseMoveAsync(request);

        Assert.Equal(PromotionPieceType.Knight, result.Promotion);
    }
}
