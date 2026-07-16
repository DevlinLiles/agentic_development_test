using Chess;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessAi;
using Xunit;
using EngineMove = Chess.Move;

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

    [Fact]
    public void OrderMoves_PlacesHashMoveFirst()
    {
        // Hash/PST ordering: when a hash move is supplied it must be tried ahead of every other
        // move, regardless of whether the others are captures.
        var board = LoadBoard(ChessConstants.StartingFen);
        var moves = board.Moves().ToList();
        var hashMove = moves.First(m => m.San == "e4");

        var ordered = GreedyHeuristicChessAi.OrderMoves(board, moves, hashMove).ToList();

        Assert.Equal("e4", ordered[0].San);
    }

    [Fact]
    public void OrderMoves_PutsCapturesBeforeQuietMoves()
    {
        // Captures-before-quiet ordering: in a position with both captures and quiet moves, no
        // quiet move may precede a capture in the ordered list. White knight on e4 can capture
        // the rook on g5 (e4 attacks g5) and also has many quiet moves.
        const string fen = "4k3/8/8/6r1/4N3/8/8/4K3 w - - 0 1";
        var board = LoadBoard(fen);
        var moves = board.Moves().ToList();

        var ordered = GreedyHeuristicChessAi.OrderMoves(board, moves, hashMove: null).ToList();

        var firstCaptureIndex = ordered.FindIndex(IsCapture(board));
        var firstQuietIndex = ordered.FindIndex(m => !IsCapture(board)(m));

        Assert.True(firstCaptureIndex >= 0, "expected at least one capture");
        Assert.True(firstQuietIndex >= 0, "expected at least one quiet move");
        Assert.True(firstCaptureIndex < firstQuietIndex, "captures must be ordered before quiet moves");
    }

    [Fact]
    public void OrderMoves_RanksCapturesByMvvLva_LeastValuableAttackerFirst()
    {
        // MVV-LVA: among captures of the same victim, the least valuable attacker comes first.
        // White pawn on e3 (attacks d4/f4 diagonally) and queen on d1 (attacks d4 along the
        // d-file) both capture the black rook on d4. The pawn capture (lower attacker value)
        // must precede the queen capture (higher attacker value).
        const string fen = "4k3/8/8/8/3r4/4P3/8/3QK3 w - - 0 1";
        var board = LoadBoard(fen);
        var moves = board.Moves().ToList();
        var captures = GreedyHeuristicChessAi.OrderMoves(board, moves, hashMove: null)
            .Where(IsCapture(board))
            .ToList();

        Assert.NotEmpty(captures);
        var pawnCapture = captures.Single(m => board[m.OriginalPosition.ToString()]!.Type == PieceType.Pawn);
        var queenCapture = captures.Single(m => board[m.OriginalPosition.ToString()]!.Type == PieceType.Queen);

        // Pawn (least valuable attacker) capturing the rook orders ahead of the queen capturing it.
        Assert.True(captures.IndexOf(pawnCapture) < captures.IndexOf(queenCapture),
            "MVV-LVA: least-valuable attacker of a given victim must come first");
    }

    [Fact]
    public void OrderMoves_PrefersCapturingHigherValueVictim_WhenSameAttacker()
    {
        // MVV-LVA victim half: the white queen on d1 captures either the rook on d8 (along the
        // d-file) or the bishop on h5 (along the d1-h5 diagonal). The rook (more valuable
        // victim) must be ordered before the bishop (less valuable victim).
        const string fen = "3r2k1/8/8/7b/8/8/8/3QK3 w - - 0 1";
        var board = LoadBoard(fen);
        var moves = board.Moves().ToList();
        var captures = GreedyHeuristicChessAi.OrderMoves(board, moves, hashMove: null)
            .Where(IsCapture(board))
            .ToList();

        Assert.NotEmpty(captures);
        var rookCapture = captures.Single(m => board[m.NewPosition.ToString()]!.Type == PieceType.Rook);
        var bishopCapture = captures.Single(m => board[m.NewPosition.ToString()]!.Type == PieceType.Bishop);

        Assert.True(captures.IndexOf(rookCapture) < captures.IndexOf(bishopCapture),
            "MVV-LVA: more valuable victim must be ordered before less valuable victim");
    }

    [Fact]
    public void ChooseMove_MeetsResponseTimeBound_WithinAcceptableDuration()
    {
        // The iterative-deepening search with a node budget must return promptly even from the
        // maximal-branching starting position. Bound is generous for CI containers but well
        // below any pathological runaway; a search that ignores ordering blows past it.
        var ai = new GreedyHeuristicChessAi();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var move = ai.ChooseMove(ChessConstants.StartingFen);

        sw.Stop();
        Assert.NotNull(move);
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"ChooseMove took {sw.ElapsedMilliseconds}ms, exceeding the response-time bound");
    }

    private static ChessBoard LoadBoard(string fen)
    {
        Assert.True(ChessBoard.TryLoadFromFen(fen, out var board, AutoEndgameRules.FiftyMoveRule),
            $"could not load FEN: {fen}");
        return board;
    }

    private static System.Predicate<EngineMove> IsCapture(ChessBoard board) =>
        m => board[m.NewPosition.ToString()] is not null;
}
