using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Service-level coverage of the Computer-game reply-flow scenarios (a)-(c) of the reply-flow
/// spec (ADR-0084 synchronous in-service trigger):
///
/// <list type="bullet">
/// <item>(a) qualifying AI reply — the human ply leaves an Active game with Black to move, so
/// the AI is consulted exactly once and its reply is applied inside the same request;</item>
/// <item>(b) no reply available — <see cref="IChessAi.ChooseMove"/> answers null, so no second
/// ply is applied and the response describes the human ply alone;</item>
/// <item>(c) no reply expected — the human ply ends the game, so the AI is never consulted and
/// the game-end machinery owns the position.</item>
/// </list>
///
/// Every scenario asserts the fake-notifier call count, which is the single-notification
/// contract (AC-10): exactly one <c>NotifyGameUpdatedAsync</c> per qualifying AI reply — never
/// one per ply — and zero notifications on the paths where no reply (and therefore no
/// broadcast) is expected. The supporting tests at the bottom pin the zero side of that
/// contract.
/// </summary>
/// <remarks>
/// Fixtures mirror the existing ones in <see cref="GameServiceTests"/> — the fake
/// <see cref="IGameRepository"/>/<see cref="IGameNotifier"/> substitutes, the
/// <c>RecordingNotifier</c> shape and the <c>NewComputerGame()</c> factory — without modifying
/// any of them. Sub-1's constructor ripples (optional notifier/chessAi parameters) are what
/// let the four-dependency construction below compile against the current
/// <see cref="GameService"/> signature.
/// </remarks>
public class GameServiceComputerGameReplyFlowTests
{
    private static readonly Guid WhiteToken = Guid.NewGuid();
    private static readonly Guid BlackToken = Guid.NewGuid();

    private readonly IGameRepository _repository = Substitute.For<IGameRepository>();
    private readonly IChessRulesEngine _rulesEngine = Substitute.For<IChessRulesEngine>();
    private readonly IChessAi _chessAi = Substitute.For<IChessAi>();

    /// <summary>
    /// The fake notifier: same fixture shape as <c>RecordingNotifier</c> in
    /// <see cref="GameServiceTests"/>, extended with an explicit <see cref="CallCount"/> so the
    /// single-notification contract can be asserted as an integer rather than inferred from a
    /// substitute's call log.
    /// </summary>
    private readonly CallCountingNotifier _notifier = new();

    private readonly GameService _sut;

    public GameServiceComputerGameReplyFlowTests()
    {
        _sut = new GameService(_repository, _rulesEngine, _notifier, _chessAi);
    }

    /// <summary>
    /// A persisted Computer-mode game as <see cref="GameService.CreateGameAsync"/> writes it:
    /// both seats carry a token and the game is already Active. Identical in shape and
    /// semantics to the <c>NewComputerGame()</c> fixture in <see cref="GameServiceTests"/>.
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
    /// Wires a Computer game so the human White move e2-e4 is legal and the scripted AI reply
    /// e7-e5 is legal for the position that move leaves. Both plies' rules-engine results are
    /// mocked, so the scenario observes the orchestration (counts, returned state) rather than
    /// re-testing the rules engine.
    /// </summary>
    private (Game Game, string AfterHumanFen, string AfterReplyFen) SetupComputerGameWithReply()
    {
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterHumanFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        const string afterReplyFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3";

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterHumanFen,
            });

        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black)
            .Returns(new ChosenMove { FromSquare = "e7", ToSquare = "e5" });

        _rulesEngine.TryApplyMove(afterHumanFen, PlayerColor.Black, "e7", "e5", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e5",
                ResultingFen = afterReplyFen,
            });

        return (game, afterHumanFen, afterReplyFen);
    }

    // ---------------------------------------------------------------------------------------------
    // Scenario (a): qualifying AI reply — exactly one notification for the whole turn
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ScenarioA_ComputerGame_QualifyingAiReply_EmitsExactlyOneNotificationForTheTurn()
    {
        var (game, afterHumanFen, afterReplyFen) = SetupComputerGameWithReply();

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // Single-notification contract: the combined human+AI turn is broadcast exactly once —
        // one per qualifying AI reply, never one per ply. A count of 2 here would mean the
        // client was pushed an intermediate "Active + Black to move" snapshot.
        Assert.Equal(1, _notifier.CallCount);

        // The one notification is emitted after the reply, so it already carries the
        // post-reply snapshot: White to move again, on the reply's FEN, both plies counted.
        var notification = Assert.Single(_notifier.Calls);
        Assert.Equal(PlayerColor.White, notification.Turn);
        Assert.Equal(afterReplyFen, notification.CurrentFen);
        Assert.Equal(2, notification.MoveCount);
        Assert.Equal(GameStatus.Active, notification.Status);

        // The reply qualifies exactly once: one consultation, one Black ply through the same
        // ApplyMove path a human move takes.
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), PlayerColor.White);
        _rulesEngine.Received(1).TryApplyMove(afterHumanFen, PlayerColor.Black, "e7", "e5", null);

        // The response describes the game after the reply, and both plies persist in one save.
        Assert.Same(game, updated);
        Assert.Equal(afterReplyFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PlayerColor.White, updated.Moves[0].PlyColor);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
        await _repository.Received(1).SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // Scenario (b): no reply available — no second ply, still exactly one notification
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ScenarioB_ComputerGame_AiReportsNoLegalMove_AppliesNoReplyAndStillNotifiesOnce()
    {
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterHumanFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterHumanFen,
            });

        // ChooseMove answers null exactly when the side to move has no legal move: no reply
        // ply exists, so none may be applied or broadcast.
        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black).Returns((ChosenMove?)null);

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // Exactly one notification covers the submission — zero notifications are attributable
        // to a reply that never happened, and the one broadcast describes the human ply only.
        Assert.Equal(1, _notifier.CallCount);
        var notification = Assert.Single(_notifier.Calls);
        Assert.Equal(PlayerColor.Black, notification.Turn);
        Assert.Equal(afterHumanFen, notification.CurrentFen);
        Assert.Equal(1, notification.MoveCount);
        Assert.Equal(GameStatus.Active, notification.Status);

        // The AI was asked once and answered "nothing"; no Black ply was ever handed to the
        // rules engine, and the response leaves Black to move on the human ply's position.
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), PlayerColor.Black, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());

        Assert.Same(game, updated);
        Assert.Equal(afterHumanFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Single(updated.Moves);
        await _repository.Received(1).SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // Scenario (c): no reply expected — the human ply ends the game
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ScenarioC_ComputerGame_HumanMoveEndsGame_SkipsReplyAndNotifiesOnce()
    {
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string matingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5p2/PPPPP1PP/RNBQKBNR b KQkq - 0 2";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "f2", "f3", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "f3",
                ResultingFen = matingFen,
                IsCheck = true,
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "f2", "f3", null);

        // No reply is expected once the game is over, yet the terminal position is still
        // broadcast — exactly once, never a second, reply-shaped push on top of it.
        Assert.Equal(1, _notifier.CallCount);
        var notification = Assert.Single(_notifier.Calls);
        Assert.Equal(matingFen, notification.CurrentFen);
        Assert.Equal(1, notification.MoveCount);
        Assert.Equal(GameStatus.Ended, notification.Status);

        // Zero consultations: the ended game is handed to the result machinery, never to the
        // AI, and no Black ply is applied.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), PlayerColor.Black, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());

        Assert.Same(game, updated);
        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.WhiteWins, updated.Result);
        Assert.Equal(GameResultReason.Checkmate, updated.ResultReason);
        Assert.Single(updated.Moves);
        await _repository.Received(1).SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // Supporting coverage for the zero side of the single-notification contract: paths in the
    // Computer-game flow where no reply — and no broadcast at all — is expected.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateGameAsync_ComputerMode_NoReplyExpected_ZeroNotifications()
    {
        // Creating a Computer game is not a move submission: the AI sits idle (it is White, the
        // human's move) and nothing is broadcast, because the only connected client is the one
        // that just asked for the game.
        var game = await _sut.CreateGameAsync(OpponentMode.Computer);

        Assert.Equal(OpponentMode.Computer, game.OpponentMode);
        Assert.Equal(GameStatus.Active, game.Status);
        Assert.NotNull(game.BlackSlotToken);
        Assert.Equal(0, _notifier.CallCount);
        Assert.Empty(_notifier.Calls);

        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_RejectedHumanMove_NoReplyExpected_ZeroNotifications()
    {
        // A submission that never gets past validation can trigger no reply and must leave the
        // broadcast count at zero — the rejected client is told by the exception, not by a push.
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e5", null)
            .Returns(MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove));

        await Assert.ThrowsAsync<IllegalMoveException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e5", null));

        Assert.Equal(0, _notifier.CallCount);
        Assert.Empty(_notifier.Calls);

        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _repository.DidNotReceive().SaveChangesAsync();
    }

    /// <summary>
    /// A deterministic notifier that records the game state at the moment of each broadcast and
    /// counts the calls, so the single-notification contract can be asserted directly.
    /// </summary>
    private sealed class CallCountingNotifier : IGameNotifier
    {
        public List<(PlayerColor Turn, string CurrentFen, int MoveCount, GameStatus Status)> Calls { get; } = new();

        public int CallCount => Calls.Count;

        public Task NotifyGameUpdatedAsync(Game game)
        {
            Calls.Add((game.Turn, game.CurrentFen, game.Moves.Count, game.Status));
            return Task.CompletedTask;
        }
    }
}
