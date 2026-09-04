using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class HeuristicEvaluatorTests
{
    private const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // A legal-move stub the evaluator can return from GetAllLegalMoves regardless of arguments.
    private static readonly IReadOnlyList<LegalMove> NoMoves = Array.Empty<LegalMove>();

    [Fact]
    public void Evaluate_InvalidFen_ReturnsZero()
    {
        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(Arg.Any<string>()).Returns(NoMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        Assert.Equal(0, sut.Evaluate("not-a-fen", PlayerColor.White));
        Assert.Equal(0, sut.Evaluate("        ", PlayerColor.White));
        Assert.Equal(0, sut.Evaluate("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", PlayerColor.White));
    }

    [Fact]
    public void Evaluate_StartingPosition_IsSymmetricAboutColor()
    {
        // The starting position is mirror-symmetric, so the only side-relative asymmetry is the
        // mobility term, which always favours the evaluated side. With no moves stubbed for
        // either side, the two scores must be equal.
        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(Arg.Any<string>()).Returns(NoMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var whiteScore = sut.Evaluate(StartingFen, PlayerColor.White);
        var blackScore = sut.Evaluate(StartingFen, PlayerColor.Black);

        Assert.Equal(whiteScore, blackScore);
        // Material is perfectly balanced at the start.
        Assert.Equal(0, whiteScore);
    }

    [Fact]
    public void Evaluate_MaterialAdvantage_FavoursStrongerSide()
    {
        // White is up a whole queen: black's queen removed from d8.
        const string fen = "rnb1kbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(Arg.Any<string>()).Returns(NoMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var whiteScore = sut.Evaluate(fen, PlayerColor.White);
        var blackScore = sut.Evaluate(fen, PlayerColor.Black);

        // Queen (900) plus its piece-square bonus on d1 (~0) => white is well ahead.
        Assert.True(whiteScore > 800);
        Assert.Equal(-whiteScore, blackScore);
    }

    [Fact]
    public void Evaluate_IsSideRelative_AndDeterministic()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";

        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(Arg.Any<string>()).Returns(NoMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var whiteScore = sut.Evaluate(fen, PlayerColor.White);
        var blackScore = sut.Evaluate(fen, PlayerColor.Black);

        // With identical (zero) mobility for both sides, the side-relative scores are negatives.
        Assert.Equal(-whiteScore, blackScore);

        // Determinism: the same call yields the same value.
        Assert.Equal(whiteScore, sut.Evaluate(fen, PlayerColor.White));
        Assert.Equal(blackScore, sut.Evaluate(fen, PlayerColor.Black));
    }

    [Fact]
    public void Evaluate_MobilityTerm_CountsLegalMovesForEvaluatedSide()
    {
        // Use the starting position; give White 20 legal moves and Black 5. White is side to move
        // in the starting FEN, so the original FEN query hits the White branch directly. Evaluating
        // Black rebuilds the FEN with "b" to move; the evaluator clears the en passant target
        // (none here) and keeps castling/halfmove/fullmove, so the flipped FEN is deterministic.
        var whiteMoves = BuildMoves(20);
        var blackMoves = BuildMoves(5);

        // The exact FEN the evaluator produces when flipping the starting position to Black to move.
        var blackToMoveFlippedFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1";

        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(StartingFen).Returns(whiteMoves);
        rulesEngine.GetAllLegalMoves(blackToMoveFlippedFen).Returns(blackMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var whiteScore = sut.Evaluate(StartingFen, PlayerColor.White);
        var blackScore = sut.Evaluate(StartingFen, PlayerColor.Black);

        Assert.Equal(HeuristicEvaluator.MobilityWeight * 20, whiteScore);
        Assert.Equal(HeuristicEvaluator.MobilityWeight * 5, blackScore);
    }

    [Fact]
    public void Evaluate_MobilityTerm_OnlyAffectsEvaluatedSide()
    {
        // The mobility term must count legal moves for the *evaluated* side, not the side to move
        // in the FEN. When evaluating White from a position where Black is to move, only White's
        // move count should matter.
        const string blackToMoveFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1";

        var whiteMoves = BuildMoves(20);
        var blackMoves = BuildMoves(0);

        // The exact FEN the evaluator produces when flipping this position to White to move.
        var whiteToMoveFlippedFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(blackToMoveFen).Returns(blackMoves);
        rulesEngine.GetAllLegalMoves(whiteToMoveFlippedFen).Returns(whiteMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var whiteScore = sut.Evaluate(blackToMoveFen, PlayerColor.White);
        var blackScore = sut.Evaluate(blackToMoveFen, PlayerColor.Black);

        Assert.Equal(HeuristicEvaluator.MobilityWeight * 20, whiteScore);
        Assert.Equal(HeuristicEvaluator.MobilityWeight * 0, blackScore);
    }

    [Fact]
    public void Evaluate_PieceSquareTable_ProvidesPositionalBonus()
    {
        // Two white pawns: one on e2 (home), one on e4 (centre). The e4 pawn reads a larger PST
        // bonus (the e-file centre entry). Position A (pawn on e4) should score higher than
        // Position B (pawn on e2) for White, with mobility held equal.
        // Position A: lone white pawn on e4, white king e1, black king e8, no black extras.
        const string fenA = "4k3/8/8/8/4P3/8/8/4K3 w - - 0 1";
        // Position B: lone white pawn on e2.
        const string fenB = "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1";

        var rulesEngine = Substitute.For<IChessRulesEngine>();
        rulesEngine.GetAllLegalMoves(Arg.Any<string>()).Returns(NoMoves);

        var sut = new HeuristicEvaluator(rulesEngine);

        var scoreA = sut.Evaluate(fenA, PlayerColor.White);
        var scoreB = sut.Evaluate(fenB, PlayerColor.White);

        Assert.True(scoreA > scoreB,
            $"Expected the advanced pawn (e4, score {scoreA}) to outscore the home pawn (e2, score {scoreB}).");
    }

    private static IReadOnlyList<LegalMove> BuildMoves(int count)
    {
        var moves = new LegalMove[count];
        for (var i = 0; i < count; i++)
        {
            moves[i] = new LegalMove("e2", "e4", "e4", false);
        }
        return moves;
    }
}
