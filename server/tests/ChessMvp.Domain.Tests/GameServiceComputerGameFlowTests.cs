using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Service-level coverage for the remaining scenarios of the Computer-game flow (the ADR-0084
/// synchronous in-service AI reply): scenario (d) and scenario (e) — the two terminal-state
/// no-reply cases, checkmate and stalemate — plus the promotion flow inside the reply path.
/// </summary>
/// <remarks>
/// <para>
/// Scenarios (d) and (e) start from a game that a previous submission has already finished:
/// checkmate for (d), stalemate for (e). The human's client can still attempt a move against
/// that finished game, so the service must refuse it before any AI or broadcast side effect
/// can run — no AI reply is triggered and the fake notifier records zero calls.
/// </para>
/// <para>
/// The promotion tests pin the single-notification contract on promoting turns: whether the
/// promotion lands on the human's ply or on the AI's reply, the submission still produces
/// exactly one AI consultation and exactly one "game updated" broadcast for the whole turn.
/// </para>
/// </remarks>
public class GameServiceComputerGameFlowTests
{
    private static readonly Guid WhiteToken = Guid.NewGuid();
    private static readonly Guid BlackToken = Guid.NewGuid();

    private readonly IGameRepository _repository = Substitute.For<IGameRepository>();
    private readonly IChessRulesEngine _rulesEngine = Substitute.For<IChessRulesEngine>();
    private readonly IGameNotifier _notifier = Substitute.For<IGameNotifier>();

    // The AI seam GameService resolves through sub-1's constructor ripples: the default SUT is
    // always built with an AI available, so the assertions below prove it is the terminal-state
    // guard — never a missing dependency — that keeps the reply branch off.
    private readonly IChessAi _chessAi = Substitute.For<IChessAi>();

    private readonly GameService _sut;

    public GameServiceComputerGameFlowTests()
    {
        _sut = new GameService(_repository, _rulesEngine, _notifier, _chessAi);
    }

    /// <summary>
    /// A persisted Computer-mode game as <see cref="GameService.CreateGameAsync"/> writes it:
    /// both seats carry a token and the game is already Active. Mirrors the private fixture of
    /// the same name in <see cref="GameServiceTests"/> (its helpers cannot be shared across
    /// test classes); the terminal-state fixtures below relax the status, turn, result and FEN
    /// fields to describe a game that is already over.
    /// </summary>
    private static Game NewComputerGame() => new()
    {
        Id = Guid.NewGuid(),
        WhiteSlotToken = WhiteToken,
        BlackSlotToken = BlackToken,
        OpponentMode = OpponentMode.Computer,
        CurrentFen = ChessConstants.StartingFen,
        Turn = PlayerColor.White,
        Status = GameStatus.Active,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Scenario (d): a Computer game finished on the previous submission when the AI's reply
    /// delivered checkmate (Fool's mate: 1. f3 e5 2. g4 Qh4#). The game is over, Black won,
    /// and it is White — the human — who would be to move in a position they cannot move in.
    /// </summary>
    private static Game NewCheckmatedComputerGame()
    {
        var game = NewComputerGame();

        game.CurrentFen = "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3";
        game.Turn = PlayerColor.White;
        game.Status = GameStatus.Ended;
        game.Result = GameResult.BlackWins;
        game.ResultReason = GameResultReason.Checkmate;
        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = 4,
            PlyColor = PlayerColor.Black,
            San = "Qh4#",
            FromSquare = "d8",
            ToSquare = "h4",
            ResultingFen = game.CurrentFen,
            IsCheck = true,
            IsCheckmate = true,
            CreatedUtc = DateTime.UtcNow,
        });

        return game;
    }

    /// <summary>
    /// Scenario (e): a Computer game finished on the previous submission when the human's
    /// queen move stalemated the AI. The Black king on a8 is not in check but has no legal
    /// move (the queen on b6 covers a7, b7 and b8), so the game ended as a draw and it is
    /// Black — the AI's seat — who would be to move.
    /// </summary>
    private static Game NewStalematedComputerGame()
    {
        var game = NewComputerGame();

        game.CurrentFen = "k7/8/KQ6/8/8/8/8/8 b - - 1 1";
        game.Turn = PlayerColor.Black;
        game.Status = GameStatus.Ended;
        game.Result = GameResult.Draw;
        game.ResultReason = GameResultReason.Stalemate;
        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = 41,
            PlyColor = PlayerColor.White,
            San = "Qb6",
            FromSquare = "b5",
            ToSquare = "b6",
            ResultingFen = game.CurrentFen,
            CreatedUtc = DateTime.UtcNow,
        });

        return game;
    }

    /// <summary>
    /// The fake-notifier fixture: a deterministic notifier that records the game state at the
    /// moment of each broadcast. Where a substitute's call log answers "how often" only
    /// indirectly, this answers it — and the emitted state — directly, which is what the
    /// zero-call and single-notification assertions need.
    /// </summary>
    private sealed class RecordingNotifier : IGameNotifier
    {
        public List<(GameStatus Status, PlayerColor Turn, string CurrentFen, int MoveCount)> Calls { get; } = new();

        public Task NotifyGameUpdatedAsync(Game game)
        {
            Calls.Add((game.Status, game.Turn, game.CurrentFen, game.Moves.Count));
            return Task.CompletedTask;
        }
    }

    /// <summary>Builds the SUT against a specific notifier so call counts can be recorded.</summary>
    private GameService NewSut(IGameNotifier notifier) => new(_repository, _rulesEngine, notifier, _chessAi);

    // ---------------------------------------------------------------------------------------------
    // Scenario (d) — checkmate: terminal state triggers no AI reply and no notification
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_ScenarioD_CheckmatedGame_TriggersNoAiReplyAndZeroNotifications()
    {
        var game = NewCheckmatedComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        var notifier = new RecordingNotifier();
        var sut = NewSut(notifier);

        var movesBefore = game.Moves.Count;

        // The human's tab still shows a board and lets them try a move; the service must refuse
        // it because the game is over.
        await Assert.ThrowsAsync<GameNotActiveException>(() =>
            sut.SubmitMoveAsync(game.Id, WhiteToken, "g2", "g3", null));

        // Scenario (d): no AI reply is triggered for a terminal position — the AI is never even
        // asked, so the reply branch cannot fire for a finished game.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());

        // ... and the fake notifier records zero calls: nothing is broadcast for a rejected
        // move, so connected clients are never told a finished game changed.
        Assert.Empty(notifier.Calls);

        // The refusal happens before any validation, application or persistence, so the terminal
        // position is left exactly as it was.
        _rulesEngine.DidNotReceive().IsPromotionMove(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        await _repository.DidNotReceive().SaveChangesAsync();
        Assert.Equal(movesBefore, game.Moves.Count);
        Assert.Equal(GameStatus.Ended, game.Status);
        Assert.Equal(GameResult.BlackWins, game.Result);
        Assert.Equal(GameResultReason.Checkmate, game.ResultReason);
    }

    // ---------------------------------------------------------------------------------------------
    // Scenario (e) — stalemate: terminal state triggers no AI reply and no notification
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_ScenarioE_StalematedGame_TriggersNoAiReplyAndZeroNotifications()
    {
        var game = NewStalematedComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        var notifier = new RecordingNotifier();
        var sut = NewSut(notifier);

        var movesBefore = game.Moves.Count;

        // A submission arrives for the side that is nominally to move (the AI's Black seat) —
        // any token would do, since the terminal-state refusal must come before the turn
        // check, but using the side to move keeps the scenario free of guard-ordering
        // assumptions.
        await Assert.ThrowsAsync<GameNotActiveException>(() =>
            sut.SubmitMoveAsync(game.Id, BlackToken, "a8", "a7", null));

        // Scenario (e): no AI reply is triggered — the AI is never consulted for a game that
        // stalemate already finished.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());

        // ... and the fake notifier records zero calls.
        Assert.Empty(notifier.Calls);

        // Nothing is validated, applied or persisted against a drawn game.
        _rulesEngine.DidNotReceive().IsPromotionMove(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        await _repository.DidNotReceive().SaveChangesAsync();
        Assert.Equal(movesBefore, game.Moves.Count);
        Assert.Equal(GameStatus.Ended, game.Status);
        Assert.Equal(GameResult.Draw, game.Result);
        Assert.Equal(GameResultReason.Stalemate, game.ResultReason);
    }

    // ---------------------------------------------------------------------------------------------
    // Promotion flow inside the Computer-game reply path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_PromotionReply_ProducesExactlyOneAiReplyAndSingleNotification()
    {
        // White opens with a quiet king move; Black (the AI) then promotes the b2 pawn. The
        // position is chosen so both plies and the promotion are genuinely legal.
        var game = NewComputerGame();
        const string initialFen = "4k3/8/8/8/8/8/1p6/4K3 w - - 0 1";
        const string afterHumanFen = "4k3/8/8/8/8/8/1p2K3/8 b - - 1 1";
        const string afterReplyFen = "4k3/8/8/8/8/8/4K3/1q6 w - - 0 2";
        game.CurrentFen = initialFen;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(initialFen, PlayerColor.White, "e1", "e2", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Ke2",
                ResultingFen = afterHumanFen,
            });

        // A ChosenMove is complete by contract: the promoting reply already carries the piece.
        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black)
            .Returns(new ChosenMove
            {
                FromSquare = "b2",
                ToSquare = "b1",
                PromotionPiece = PromotionPieceType.Queen,
            });

        _rulesEngine.IsPromotionMove(afterHumanFen, "b2", "b1").Returns(true);
        _rulesEngine.TryApplyMove(afterHumanFen, PlayerColor.Black, "b2", "b1", PromotionPieceType.Queen)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "b1=Q",
                ResultingFen = afterReplyFen,
            });

        var notifier = new RecordingNotifier();
        var sut = NewSut(notifier);

        var updated = await sut.SubmitMoveAsync(game.Id, WhiteToken, "e1", "e2", null);

        // A promoting reply still produces exactly one AI consultation for the whole turn: the
        // promotion piece is not re-chosen, and the AI is not asked a second time.
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _chessAi.Received(1).ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());

        // The reply is applied exactly once, with the promotion piece the AI already decided on.
        _rulesEngine.Received(1)
            .TryApplyMove(afterHumanFen, PlayerColor.Black, "b2", "b1", PromotionPieceType.Queen);

        Assert.Same(game, updated);
        Assert.Equal(afterReplyFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
        Assert.Equal("b1=Q", updated.Moves[1].San);
        Assert.Equal("b2", updated.Moves[1].FromSquare);
        Assert.Equal("b1", updated.Moves[1].ToSquare);
        Assert.Equal(PromotionPieceType.Queen, updated.Moves[1].PromotionPiece);

        // The single-notification contract holds on a promoting turn: exactly one broadcast for
        // both plies, emitted after the reply and already carrying the post-promotion state.
        var notification = Assert.Single(notifier.Calls);
        Assert.Equal(GameStatus.Active, notification.Status);
        Assert.Equal(PlayerColor.White, notification.Turn);
        Assert.Equal(afterReplyFen, notification.CurrentFen);
        Assert.Equal(2, notification.MoveCount);

        // Both plies of the promoting turn persist atomically in a single save.
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_HumanPromotionMove_StillProducesExactlyOneAiReplyAndSingleNotification()
    {
        // The promoting ply is the human's own this time: White promotes on e8 and the AI still
        // answers once, so the promotion cannot suppress or duplicate the reply.
        var game = NewComputerGame();
        const string initialFen = "8/4P3/8/k7/8/8/8/4K3 w - - 0 1";
        const string afterHumanFen = "4Q3/8/8/k7/8/8/8/4K3 b - - 0 1";
        const string afterReplyFen = "4Q3/8/k7/8/8/8/8/4K3 w - - 1 2";
        game.CurrentFen = initialFen;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.IsPromotionMove(initialFen, "e7", "e8").Returns(true);
        _rulesEngine.TryApplyMove(initialFen, PlayerColor.White, "e7", "e8", PromotionPieceType.Queen)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e8=Q",
                ResultingFen = afterHumanFen,
            });

        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black)
            .Returns(new ChosenMove { FromSquare = "a5", ToSquare = "a6" });

        _rulesEngine.IsPromotionMove(afterHumanFen, "a5", "a6").Returns(false);
        _rulesEngine.TryApplyMove(afterHumanFen, PlayerColor.Black, "a5", "a6", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Ka6",
                ResultingFen = afterReplyFen,
            });

        var notifier = new RecordingNotifier();
        var sut = NewSut(notifier);

        var updated = await sut.SubmitMoveAsync(game.Id, WhiteToken, "e7", "e8", PromotionPieceType.Queen);

        // Exactly one AI reply still follows the promoting human ply.
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _chessAi.Received(1).ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        _rulesEngine.Received(1).TryApplyMove(afterHumanFen, PlayerColor.Black, "a5", "a6", null);

        Assert.Same(game, updated);
        Assert.Equal(afterReplyFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PlayerColor.White, updated.Moves[0].PlyColor);
        Assert.Equal("e8=Q", updated.Moves[0].San);
        Assert.Equal(PromotionPieceType.Queen, updated.Moves[0].PromotionPiece);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
        Assert.Equal("Ka6", updated.Moves[1].San);

        // Exactly one notification covers the promotion and the reply together, and it carries
        // the post-reply state.
        var notification = Assert.Single(notifier.Calls);
        Assert.Equal(GameStatus.Active, notification.Status);
        Assert.Equal(PlayerColor.White, notification.Turn);
        Assert.Equal(afterReplyFen, notification.CurrentFen);
        Assert.Equal(2, notification.MoveCount);

        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_PromotionMoveWithoutPiece_ThrowsBeforeAnyReplyOrNotification()
    {
        // The promotion guard runs inside the shared ApplyMove path, so an incomplete promotion
        // in a Computer game is rejected before the AI reply branch and the broadcast can run.
        var game = NewComputerGame();
        const string initialFen = "8/4P3/8/k7/8/8/8/4K3 w - - 0 1";
        game.CurrentFen = initialFen;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.IsPromotionMove(initialFen, "e7", "e8").Returns(true);

        await Assert.ThrowsAsync<PromotionRequiredException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e7", "e8", null));

        // No AI reply and no notification for the rejected ply.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
        await _repository.DidNotReceive().SaveChangesAsync();
    }
}
