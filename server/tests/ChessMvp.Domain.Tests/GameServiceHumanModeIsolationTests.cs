using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Services;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Service-level isolation tests for the opponent-mode discriminator on the move-submission
/// path: a Human game must never trigger the AI reply, no matter which of the two row shapes a
/// database can hold represents that Human mode:
///  <list type="bullet">
///   <item>an explicit <see cref="OpponentMode.Human"/> written by
///   <see cref="GameService.CreateGameAsync"/> since the discriminator existed, and</item>
///   <item>a legacy row whose <c>OpponentMode</c> is NULL — written before the
///   AddOpponentModeToGame migration made the column exist — which every consumer must
///   normalize to Human.</item>
///  </list>
/// The games are constructed directly rather than through a Computer-game builder: the Computer
/// shape pins <see cref="OpponentMode.Computer"/> (both seats filled at creation with the AI on
/// Black), and <see cref="GameService.CreateGameAsync"/> can never produce a NULL discriminator,
/// so persisted-shape construction is the only way to reach the legacy row — exactly the row the
/// isolation guarantee has to hold for.
///
/// The fixtures mirror the existing GameServiceTests fakes (an NSubstitute
/// <see cref="IGameRepository"/> substitute plus a recording <see cref="IGameNotifier"/>). Those
/// fixtures are file-local to this test project's GameServiceTests and are deliberately left
/// unmodified, so the same shapes are reproduced here verbatim rather than extracted or edited.
/// </summary>
public class GameServiceHumanModeIsolationTests
{
    private static readonly Guid WhiteToken = Guid.NewGuid();
    private static readonly Guid BlackToken = Guid.NewGuid();

    private const string AfterHumanFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
    private const string AfterReplyFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3";

    private readonly IGameRepository _repository = Substitute.For<IGameRepository>();
    private readonly IChessRulesEngine _rulesEngine = Substitute.For<IChessRulesEngine>();

    /// <summary>
    /// A willing AI. It answers every consultation with a concrete reply, and each scenario
    /// scripts the rules engine to accept that reply, so a Human row that ever selected the
    /// reply branch would fail loudly — a second ply, a second rules-engine application and a
    /// two-ply broadcast — instead of the isolation tests passing vacuously.
    /// </summary>
    private readonly IChessAi _chessAi = Substitute.For<IChessAi>();

    public GameServiceHumanModeIsolationTests()
    {
        _chessAi.ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>())
            .Returns(new ChosenMove { FromSquare = "e7", ToSquare = "e5" });
    }

    /// <summary>
    /// A persisted-shape game with both seats taken, active and White to move.
    /// <paramref name="opponentMode"/> is the discriminator exactly as the row holds it —
    /// <see cref="OpponentMode.Human"/>, NULL for a legacy row, or
    /// <see cref="OpponentMode.Computer"/> for the control test.
    /// </summary>
    private static Game NewActiveGame(OpponentMode? opponentMode) => new()
    {
        Id = Guid.NewGuid(),
        WhiteSlotToken = WhiteToken,
        BlackSlotToken = BlackToken,
        OpponentMode = opponentMode,
        CurrentFen = ChessConstants.StartingFen,
        Turn = PlayerColor.White,
        Status = GameStatus.Active,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Wires a game whose discriminator is <paramref name="opponentMode"/> so that the AI reply
    /// is fully available: the human White move e2-e4 is legal, and the willing AI's e7-e5 reply
    /// is legal from the position that move leaves. If the reply branch is ever selected for
    /// such a row, everything it would do is observable.
    /// </summary>
    private (Game Game, string AfterHumanFen) SetupGameWhereAiCouldReply(OpponentMode? opponentMode)
    {
        var game = NewActiveGame(opponentMode);
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = AfterHumanFen,
            });

        _rulesEngine.TryApplyMove(AfterHumanFen, PlayerColor.Black, "e7", "e5", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e5",
                ResultingFen = AfterReplyFen,
            });

        return (game, AfterHumanFen);
    }

    /// <summary>
    /// A deterministic notifier that records the game state at the moment of each broadcast.
    /// Same fixture shape as the existing GameServiceTests fake notifier (which stays
    /// unmodified and file-local there).
    /// </summary>
    private sealed class RecordingNotifier : IGameNotifier
    {
        public List<(PlayerColor Turn, string CurrentFen, int MoveCount)> Calls { get; } = new();

        public Task NotifyGameUpdatedAsync(Game game)
        {
            Calls.Add((game.Turn, game.CurrentFen, game.Moves.Count));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Asserts the submission describes the single human ply and nothing else: the position,
    /// turn, clock, status and move list are exactly what applying e2-e4 produces, with no
    /// game-end transition and no second ply.
    /// </summary>
    private static void AssertDescribesOnlyTheSubmittedPly(Game updated, Game game, string afterHumanFen)
    {
        Assert.Same(game, updated);
        Assert.Equal(afterHumanFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Equal(0, updated.HalfmoveClock);
        Assert.Null(updated.Result);
        Assert.Null(updated.ResultReason);

        var move = Assert.Single(updated.Moves);
        Assert.Equal(1, move.MoveNumber);
        Assert.Equal(PlayerColor.White, move.PlyColor);
        Assert.Equal("e2", move.FromSquare);
        Assert.Equal("e4", move.ToSquare);
        Assert.Equal("e4", move.San);
    }

    /// <summary>
    /// Asserts the fingerprint the AI reply would have left behind is entirely absent: the AI is
    /// never consulted, no Black ply is ever applied, no reply-driven broadcast is recorded and
    /// no extra save happens.
    /// </summary>
    private async Task AssertNoAiReplyWasTriggeredAsync(RecordingNotifier notifier, string afterHumanFen)
    {
        // No AI reply is triggered: the AI is never asked for a move — for either colour, from
        // any position — so no reply is ever chosen.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());

        // Consequently the rules engine is never asked to apply a Black ply: the only engine
        // application on record is the submitted White move.
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), PlayerColor.Black, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());

        // The reply flow records zero notifier calls of its own. The single broadcast on record
        // is the one the submitted ply always earns, and it describes that ply alone — Black to
        // move, on the human move's FEN, with one move played. Zero of the recorded calls
        // describe a replied-to, two-ply position (which is what the Computer-mode control test
        // below records), so the reply contributed no notification whatsoever.
        var broadcast = Assert.Single(notifier.Calls);
        Assert.Equal(PlayerColor.Black, broadcast.Turn);
        Assert.Equal(afterHumanFen, broadcast.CurrentFen);
        Assert.Equal(1, broadcast.MoveCount);
        Assert.Equal(0, notifier.Calls.Count(c => c.MoveCount > 1));

        // One submission, one save: the reply flow adds no persistence of its own.
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_ExplicitHumanOpponentMode_TriggersNoAiReply()
    {
        var (game, afterHumanFen) = SetupGameWhereAiCouldReply(OpponentMode.Human);
        var notifier = new RecordingNotifier();
        var sut = new GameService(_repository, _rulesEngine, notifier, _chessAi);

        var updated = await sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        AssertDescribesOnlyTheSubmittedPly(updated, game, afterHumanFen);
        Assert.Equal(OpponentMode.Human, updated.OpponentMode);
        await AssertNoAiReplyWasTriggeredAsync(notifier, afterHumanFen);
    }

    [Fact]
    public async Task SubmitMoveAsync_LegacyNullOpponentMode_IsTreatedAsHumanAndTriggersNoAiReply()
    {
        // Rows written before the discriminator existed read back as NULL; those games are
        // normalized to the pre-existing two-player behaviour, so submitting the human move in
        // such a game must keep the pure one-ply flow even though an AI is wired and willing.
        var (game, afterHumanFen) = SetupGameWhereAiCouldReply(opponentMode: null);
        var notifier = new RecordingNotifier();
        var sut = new GameService(_repository, _rulesEngine, notifier, _chessAi);

        var updated = await sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        AssertDescribesOnlyTheSubmittedPly(updated, game, afterHumanFen);

        // The legacy NULL discriminator is normalized for behaviour but never rewritten: the row
        // is left exactly as it was found, so nothing about the reply flow leaks into it.
        Assert.Null(updated.OpponentMode);
        Assert.Equal(OpponentMode.Human, updated.OpponentMode ?? OpponentMode.Human);

        await AssertNoAiReplyWasTriggeredAsync(notifier, afterHumanFen);
    }

    [Theory]
    [InlineData(OpponentMode.Human)]
    [InlineData(null)]
    public async Task SubmitMoveAsync_HumanMode_BehaviourIdenticalWhetherOrNotAnAiIsWired(OpponentMode? opponentMode)
    {
        // Isolation cuts both ways: wiring the AI seam must not change a Human game's behaviour
        // at all. The same submission runs twice — once with the willing AI injected and once
        // with no AI at all (the pre-seam construction) — and every observable must agree.
        var (withAiGame, afterHumanFen) = SetupGameWhereAiCouldReply(opponentMode);
        var (withoutAiGame, _) = SetupGameWhereAiCouldReply(opponentMode);

        var withAiNotifier = new RecordingNotifier();
        var withoutAiNotifier = new RecordingNotifier();
        var withAi = new GameService(_repository, _rulesEngine, withAiNotifier, _chessAi);
        var withoutAi = new GameService(_repository, _rulesEngine, withoutAiNotifier, chessAi: null);

        var withAiResult = await withAi.SubmitMoveAsync(withAiGame.Id, WhiteToken, "e2", "e4", null);
        var withoutAiResult = await withoutAi.SubmitMoveAsync(withoutAiGame.Id, WhiteToken, "e2", "e4", null);

        Assert.Equal(
            (withoutAiResult.Turn, withoutAiResult.CurrentFen, withoutAiResult.Moves.Count, withoutAiResult.Status),
            (withAiResult.Turn, withAiResult.CurrentFen, withAiResult.Moves.Count, withAiResult.Status));
        Assert.Equal(withoutAiResult.Moves[0].San, withAiResult.Moves[0].San);
        Assert.Equal(withoutAiResult.Moves[0].PlyColor, withAiResult.Moves[0].PlyColor);

        // The notification profile is likewise unchanged: the same single human-ply broadcast,
        // with zero reply-driven calls in either run.
        Assert.Single(withoutAiNotifier.Calls);
        Assert.Single(withAiNotifier.Calls);
        Assert.Equal(withoutAiNotifier.Calls, withAiNotifier.Calls);
        Assert.Equal(0, withAiNotifier.Calls.Count(c => c.MoveCount > 1));

        // The wired AI is never the reason a Human game stays reply-free — the mode guard is.
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        Assert.Equal(afterHumanFen, withAiResult.CurrentFen);
        Assert.Equal(PlayerColor.Black, withAiResult.Turn);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerOpponentMode_Control_TheSameFixturesDoRecordAiReply()
    {
        // Control for the isolation tests above: the very same fixtures, driven through a
        // Computer row, do record an AI reply — a second ply, a post-reply position and a
        // two-ply broadcast. The zero-consultation and zero-reply-broadcast assertions in the
        // Human-mode tests are therefore meaningful, not a fixture that cannot hear a reply.
        var (game, _) = SetupGameWhereAiCouldReply(OpponentMode.Computer);
        var notifier = new RecordingNotifier();
        var sut = new GameService(_repository, _rulesEngine, notifier, _chessAi);

        var updated = await sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        _chessAi.Received(1).ChooseMove(AfterHumanFen, PlayerColor.Black);
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(AfterReplyFen, updated.CurrentFen);

        var broadcast = Assert.Single(notifier.Calls);
        Assert.Equal(2, broadcast.MoveCount);
        Assert.Equal(AfterReplyFen, broadcast.CurrentFen);
        Assert.Equal(PlayerColor.White, broadcast.Turn);
    }

    [Fact]
    public async Task CreateGameAsync_HumanMode_RecordsZeroNotifierCallsAndNeverConsultsTheAi()
    {
        // Creation is the Human-mode entry point that legitimately records zero notifier calls:
        // no state transition has happened for anyone to broadcast, and no position has been
        // reached for the AI to reply to. An explicitly Human creation and the parameterless
        // (legacy) creation must agree on both counts.
        var notifier = new RecordingNotifier();
        var sut = new GameService(_repository, _rulesEngine, notifier, _chessAi);

        var explicitHuman = await sut.CreateGameAsync(OpponentMode.Human);
        var defaulted = await sut.CreateGameAsync();

        foreach (var game in new[] { explicitHuman, defaulted })
        {
            Assert.Equal(OpponentMode.Human, game.OpponentMode);
            Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
            Assert.Null(game.BlackSlotToken);
            Assert.Equal(PlayerColor.White, game.Turn);
        }

        Assert.Empty(notifier.Calls);
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), Arg.Any<PlayerColor>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
    }
}
