using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using NSubstitute;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class GameServiceTests
{
    private static readonly Guid WhiteToken = Guid.NewGuid();
    private static readonly Guid BlackToken = Guid.NewGuid();

    private readonly IGameRepository _repository = Substitute.For<IGameRepository>();
    private readonly IChessRulesEngine _rulesEngine = Substitute.For<IChessRulesEngine>();
    private readonly IGameNotifier _notifier = Substitute.For<IGameNotifier>();

    // The AI seam GameService resolves by constructor injection. It is substituted into the
    // default SUT so every test runs with an AI available: the Human-path regressions below
    // then prove the mode guard (not a missing dependency) is what keeps the reply branch off.
    private readonly IChessAi _chessAi = Substitute.For<IChessAi>();

    private readonly GameService _sut;

    public GameServiceTests()
    {
        _sut = new GameService(_repository, _rulesEngine, _notifier, _chessAi);
    }

    private static Game NewActiveGame(OpponentMode? opponentMode = OpponentMode.Human) => new()
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
    /// A persisted Computer-mode game as <see cref="GameService.CreateGameAsync"/> writes it:
    /// both seats carry a token and the game is already Active. Tests that only care about the
    /// discriminator can relax the status/token fields to isolate the mode guard.
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

    /// <summary>An open-seat game waiting for a second player, i.e. joinable.</summary>
    private static Game NewWaitingGame(OpponentMode? opponentMode = OpponentMode.Human) => new()
    {
        Id = Guid.NewGuid(),
        WhiteSlotToken = WhiteToken,
        BlackSlotToken = null,
        OpponentMode = opponentMode,
        CurrentFen = ChessConstants.StartingFen,
        Turn = PlayerColor.White,
        Status = GameStatus.WaitingForPlayer2,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// A deterministic notifier that records the game state at the moment of each broadcast.
    /// Used where the assertion is about <em>when</em> and <em>how often</em> the notification
    /// fires, which a substitute's call log can only answer indirectly.
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

    [Fact]
    public async Task CreateGameAsync_ReturnsWaitingGameWithWhiteToken()
    {
        var game = await _sut.CreateGameAsync();

        Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
        Assert.NotNull(game.WhiteSlotToken);
        Assert.Null(game.BlackSlotToken);
        Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);
        Assert.Equal(PlayerColor.White, game.Turn);
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task CreateGameAsync_SeedsHumanOpponentMode()
    {
        // New games must carry an explicit Human discriminator (not null, and not silently left
        // to the column default) so the in-memory entity matches a database round-trip and the
        // discriminator is always written on insert.
        var game = await _sut.CreateGameAsync();

        Assert.Equal(OpponentMode.Human, game.OpponentMode);
    }

    [Fact]
    public async Task CreateGameAsync_ExplicitHumanMode_MatchesParameterlessBehavior()
    {
        // Passing the default explicitly must be indistinguishable from not passing it at all —
        // the parameter was added to an existing call site, so its default cannot change the
        // stored game in any way.
        var explicitHuman = await _sut.CreateGameAsync(OpponentMode.Human);
        var defaulted = await _sut.CreateGameAsync();

        foreach (var game in new[] { explicitHuman, defaulted })
        {
            Assert.Equal(OpponentMode.Human, game.OpponentMode);
            Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
            Assert.NotNull(game.WhiteSlotToken);
            Assert.Null(game.BlackSlotToken);
            Assert.Equal(PlayerColor.White, game.Turn);
            Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);
            Assert.Equal(0, game.HalfmoveClock);
        }
    }

    [Fact]
    public async Task CreateGameAsync_ComputerMode_CreatesActiveGameWithHumanWhiteAndAiBlack()
    {
        var game = await _sut.CreateGameAsync(OpponentMode.Computer);

        Assert.Equal(OpponentMode.Computer, game.OpponentMode);
        Assert.Equal(GameStatus.Active, game.Status);
        Assert.Equal(PlayerColor.White, game.Turn);
        Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);

        // Both seats are filled at creation: the human caller keeps White and the AI takes
        // Black. The AI seat gets its own token so the engine's replies flow through the same
        // slot-token resolution path as a human player's moves.
        Assert.NotNull(game.WhiteSlotToken);
        Assert.NotNull(game.BlackSlotToken);
        Assert.NotEqual(game.WhiteSlotToken, game.BlackSlotToken);

        // Creation is the only state transition — there is no waiting phase to broadcast.
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task CreateGameAsync_ComputerMode_AISeatTokenResolvesToBlackForMoves()
    {
        // The token minted for the AI seat must resolve through the normal move-submission
        // path, i.e. a Computer game is immediately playable on both sides of the board.
        var game = await _sut.CreateGameAsync(OpponentMode.Computer);
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2",
            });

        var afterHumanMove = await _sut.SubmitMoveAsync(game.Id, game.WhiteSlotToken!.Value, "e2", "e4", null);
        Assert.Equal(PlayerColor.Black, afterHumanMove.Turn);

        _rulesEngine.TryApplyMove(afterHumanMove.CurrentFen, PlayerColor.Black, "e7", "e5", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e5",
                ResultingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2",
            });

        var afterAiMove = await _sut.SubmitMoveAsync(game.Id, game.BlackSlotToken!.Value, "e7", "e5", null);

        Assert.Equal(PlayerColor.White, afterAiMove.Turn);
        Assert.Equal(2, afterAiMove.Moves.Count);
        Assert.Equal(PlayerColor.Black, afterAiMove.Moves[1].PlyColor);
    }

    [Fact]
    public async Task JoinGameAsync_UnknownGame_ThrowsGameNotFoundException()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Game?)null);

        await Assert.ThrowsAsync<GameNotFoundException>(() => _sut.JoinGameAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task JoinGameAsync_AlreadyFull_ThrowsGameNotActiveException()
    {
        var game = NewActiveGame();
        _repository.GetByIdAsync(game.Id).Returns(game);

        await Assert.ThrowsAsync<GameNotActiveException>(() => _sut.JoinGameAsync(game.Id));
    }

    [Fact]
    public async Task JoinGameAsync_ComputerGame_ThrowsGameNotActiveException()
    {
        // A Computer game's Black seat belongs to the server-side AI from the moment of
        // creation, so it is never joinable. The rejection reuses the existing
        // GameNotActiveException vocabulary rather than introducing a new exception type, so
        // the API keeps answering 409 GameNotActive for it.
        var game = NewComputerGame();
        _repository.GetByIdAsync(game.Id).Returns(game);

        var exception = await Assert.ThrowsAsync<GameNotActiveException>(() => _sut.JoinGameAsync(game.Id));

        Assert.Contains("computer", exception.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing is persisted and no "game updated" broadcast fires for a rejected join.
        await _repository.DidNotReceive().SaveChangesAsync();
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
    }

    [Fact]
    public async Task JoinGameAsync_ComputerDiscriminatorAlone_RejectsJoin()
    {
        // Isolates the mode guard: this row carries the Computer discriminator but would
        // otherwise pass the generic join guard (open Black seat, still WaitingForPlayer2), so
        // the rejection can only come from the opponent mode — it must not depend on the
        // incidental status/token state CreateGameAsync happens to set for Computer games.
        var game = NewWaitingGame(OpponentMode.Computer);
        _repository.GetByIdAsync(game.Id).Returns(game);

        var exception = await Assert.ThrowsAsync<GameNotActiveException>(() => _sut.JoinGameAsync(game.Id));

        Assert.Contains("computer", exception.Message, StringComparison.OrdinalIgnoreCase);

        // The rejected join must leave the open seat untouched.
        Assert.Null(game.BlackSlotToken);
        Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
        await _repository.DidNotReceive().SaveChangesAsync();
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
    }

    [Fact]
    public async Task JoinGameAsync_LegacyNullDiscriminator_NormalizesToHumanAndAllowsJoin()
    {
        // Rows written before the discriminator existed read back as null; those games are
        // normalized to the pre-existing two-player behavior, so a joinable legacy row stays
        // joinable. This also pins the guard to the discriminator rather than to incidental
        // status/token state, so it cannot start rejecting ordinary human games.
        var game = NewWaitingGame(opponentMode: null);
        _repository.GetByIdAsync(game.Id).Returns(game);

        var result = await _sut.JoinGameAsync(game.Id);

        Assert.Equal(GameStatus.Active, result.Status);
        Assert.NotNull(result.BlackSlotToken);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task JoinGameAsync_OpenSeat_AssignsBlackTokenAndActivates()
    {
        var game = NewWaitingGame();
        _repository.GetByIdAsync(game.Id).Returns(game);

        var result = await _sut.JoinGameAsync(game.Id);

        Assert.Equal(GameStatus.Active, result.Status);
        Assert.NotNull(result.BlackSlotToken);
        await _repository.Received(1).SaveChangesAsync();

        // Player 1's already-open tab must flip from "waiting" to "active" without a manual
        // refresh, so joining has to broadcast just like a move does.
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task JoinGameAsync_HumanModeOpenSeat_IsJoinable()
    {
        // The complementary case to the Computer rejection: an explicitly Human game with an
        // open seat is joinable, so adding the discriminator guard did not tighten the
        // two-player flow.
        var game = NewWaitingGame(OpponentMode.Human);
        _repository.GetByIdAsync(game.Id).Returns(game);

        var result = await _sut.JoinGameAsync(game.Id);

        Assert.Equal(OpponentMode.Human, result.OpponentMode);
        Assert.Equal(GameStatus.Active, result.Status);
        Assert.NotNull(result.BlackSlotToken);
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_UnknownGame_ThrowsGameNotFoundException()
    {
        _repository.GetByIdWithMovesAsync(Arg.Any<Guid>()).Returns((Game?)null);

        await Assert.ThrowsAsync<GameNotFoundException>(() =>
            _sut.SubmitMoveAsync(Guid.NewGuid(), WhiteToken, "e2", "e4", null));
    }

    [Fact]
    public async Task SubmitMoveAsync_TokenMatchesNeitherSeat_ThrowsInvalidSlotTokenException()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        await Assert.ThrowsAsync<InvalidSlotTokenException>(() =>
            _sut.SubmitMoveAsync(game.Id, Guid.NewGuid(), "e2", "e4", null));

        await _repository.DidNotReceive().SaveChangesAsync();
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
    }

    [Fact]
    public async Task SubmitMoveAsync_GameWaitingForPlayer2_ThrowsGameNotActiveException()
    {
        var game = NewActiveGame();
        game.Status = GameStatus.WaitingForPlayer2;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        await Assert.ThrowsAsync<GameNotActiveException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null));
    }

    [Fact]
    public async Task SubmitMoveAsync_GameAlreadyEnded_ThrowsGameNotActiveException()
    {
        var game = NewActiveGame();
        game.Status = GameStatus.Ended;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        await Assert.ThrowsAsync<GameNotActiveException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null));
    }

    [Fact]
    public async Task SubmitMoveAsync_NotThatPlayersTurn_ThrowsNotYourTurnException()
    {
        var game = NewActiveGame();
        game.Turn = PlayerColor.White;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        // Black tries to move while it's White's turn.
        await Assert.ThrowsAsync<NotYourTurnException>(() =>
            _sut.SubmitMoveAsync(game.Id, BlackToken, "e7", "e5", null));

        await _repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_PromotionMoveWithoutPromotionPiece_ThrowsPromotionRequiredException()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);
        _rulesEngine.IsPromotionMove(game.CurrentFen, "e7", "e8").Returns(true);

        await Assert.ThrowsAsync<PromotionRequiredException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e7", "e8", null));

        await _repository.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_IllegalMove_ThrowsAndDoesNotSaveOrNotify()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e5", null)
            .Returns(MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove));

        await Assert.ThrowsAsync<IllegalMoveException>(() =>
            _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e5", null));

        await _repository.DidNotReceive().SaveChangesAsync();
        await _notifier.DidNotReceive().NotifyGameUpdatedAsync(Arg.Any<Game>());
    }

    [Fact]
    public async Task SubmitMoveAsync_LegalMove_UpdatesStateSavesAndNotifiesOnce()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string resultingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = resultingFen,
                IsCheck = false,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        Assert.Equal(resultingFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Equal(0, updated.HalfmoveClock);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Single(updated.Moves);
        Assert.Equal(1, updated.Moves[0].MoveNumber);
        Assert.Equal("e4", updated.Moves[0].San);
        Assert.Equal(PlayerColor.White, updated.Moves[0].PlyColor);

        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_CheckmateMove_EndsGameWithCorrectResult()
    {
        var game = NewActiveGame();
        game.Turn = PlayerColor.Black;
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string matingFen = "rnb1kbnr/pppp1Q1p/5p2/4p3/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 0 4";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.Black, "d8", "h4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Qxh4#",
                ResultingFen = matingFen,
                IsCheck = true,
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, BlackToken, "d8", "h4", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.BlackWins, updated.Result);
        Assert.Equal(GameResultReason.Checkmate, updated.ResultReason);
        Assert.True(updated.Moves[0].IsCheckmate);

        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_StalemateMove_EndsGameAsDraw()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "a1", "a2", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Ra2",
                ResultingFen = "8/8/8/8/8/8/k7/1K6 b - - 0 50",
                IsStalemate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "a1", "a2", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.Draw, updated.Result);
        Assert.Equal(GameResultReason.Stalemate, updated.ResultReason);
    }

    [Fact]
    public async Task SubmitMoveAsync_FiftyMoveDraw_EndsGameAsDraw()
    {
        var game = NewActiveGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "a1", "a2", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Ra2",
                ResultingFen = "8/8/8/8/8/8/k7/1K6 b - - 100 80",
                IsFiftyMoveDraw = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "a1", "a2", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.Draw, updated.Result);
        Assert.Equal(GameResultReason.FiftyMoveRule, updated.ResultReason);
        Assert.Equal(100, updated.HalfmoveClock);
    }

    // ---------------------------------------------------------------------------------------------
    // Computer-game AI reply branch (ADR-0084 synchronous in-service trigger)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Wires a Computer game so the human White move e2-e4 is legal, and the scripted AI reply
    /// e7-e5 is legal for the position that move leaves. Both plies' rules-engine results are
    /// mocked, so the tests observe the orchestration (order, counts, returned state) rather
    /// than re-testing the rules engine.
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

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_TriggersSynchronousAiReplyAfterHumanMove()
    {
        var (game, afterHumanFen, afterReplyFen) = SetupComputerGameWithReply();

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // The AI is consulted exactly once, and only after the human move has been applied:
        // the position it is handed is the one the human ply produced, and it is asked for
        // Black — never for the human's own colour.
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), PlayerColor.White);

        // The reply rides the same ApplyMove path a human move does: a second Move entity is
        // recorded, numbered and coloured as Black's ply, and both plies land in the game.
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(1, updated.Moves[0].MoveNumber);
        Assert.Equal(PlayerColor.White, updated.Moves[0].PlyColor);
        Assert.Equal("e4", updated.Moves[0].San);
        Assert.Equal(2, updated.Moves[1].MoveNumber);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
        Assert.Equal("e7", updated.Moves[1].FromSquare);
        Assert.Equal("e5", updated.Moves[1].ToSquare);
        Assert.Equal("e5", updated.Moves[1].San);
        Assert.Equal(afterReplyFen, updated.CurrentFen);

        // The reply is applied through the rules engine for Black, from the AI's chosen
        // squares — no other application of a Black ply can have happened.
        _rulesEngine.Received(1).TryApplyMove(afterHumanFen, PlayerColor.Black, "e7", "e5", null);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_ReturnsPostReplyStateWithSingleSave()
    {
        var (game, _, afterReplyFen) = SetupComputerGameWithReply();

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // The response describes the game after the AI's reply, not after the human ply: it is
        // White to move again, on the reply's FEN, with both plies already counted. No
        // "Active + Black to move" state can persist past the request.
        Assert.Same(game, updated);
        Assert.Equal(afterReplyFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
        Assert.Equal(2, updated.Moves.Count);

        // Both plies of the turn persist atomically in a single save, so an AI-reply failure
        // can never leave a half-applied game behind.
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_EmitsExactlyOneNotificationForTheWholeTurn()
    {
        var (game, _, afterReplyFen) = SetupComputerGameWithReply();
        var notifier = new RecordingNotifier();
        var sut = new GameService(_repository, _rulesEngine, notifier, _chessAi);

        await sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // Exactly one notification covers the combined human+AI turn — never one per ply — and
        // it is emitted after the reply, so it already carries the post-reply state.
        var notification = Assert.Single(notifier.Calls);
        Assert.Equal(PlayerColor.White, notification.Turn);
        Assert.Equal(afterReplyFen, notification.CurrentFen);
        Assert.Equal(2, notification.MoveCount);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_PromotionReply_PassesChosenPieceThrough()
    {
        // A ChosenMove is complete by contract: when the AI's reply promotes, the promotion
        // piece it already decided on must reach the rules engine untouched rather than being
        // re-chosen (or dropped) on the AI's behalf.
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterHumanFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        const string afterReplyFen = "rnbqkbn1/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3";

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterHumanFen,
            });

        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black)
            .Returns(new ChosenMove
            {
                FromSquare = "a2",
                ToSquare = "a1",
                PromotionPiece = PromotionPieceType.Queen,
            });

        _rulesEngine.IsPromotionMove(afterHumanFen, "a2", "a1").Returns(true);
        _rulesEngine.TryApplyMove(afterHumanFen, PlayerColor.Black, "a2", "a1", PromotionPieceType.Queen)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "a1=Q",
                ResultingFen = afterReplyFen,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        _rulesEngine.Received(1)
            .TryApplyMove(afterHumanFen, PlayerColor.Black, "a2", "a1", PromotionPieceType.Queen);
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PromotionPieceType.Queen, updated.Moves[1].PromotionPiece);
        Assert.Equal(afterReplyFen, updated.CurrentFen);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_AiHasNoLegalMove_AppliesNoReplyButStillSavesAndNotifiesOnce()
    {
        // ChooseMove answers null exactly when the side to move has no legal move; the reply
        // branch must then apply nothing (terminal positions belong to the game-end machinery),
        // while the submission itself still completes normally: one save, one notification, and
        // a response describing the human ply.
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

        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black).Returns((ChosenMove?)null);

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        Assert.Equal(afterHumanFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Single(updated.Moves);
        _chessAi.Received(1).ChooseMove(afterHumanFen, PlayerColor.Black);
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), PlayerColor.Black, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_HumanMoveEndsGame_DoesNotConsultAi()
    {
        // The reply step is guarded on the game still being Active after the human ply: a move
        // that finishes the game hands the position to the result machinery, not to the AI.
        var game = NewComputerGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "f2", "f3", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "f3",
                ResultingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5p2/PPPPP1PP/RNBQKBNR b KQkq - 0 2",
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "f2", "f3", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.WhiteWins, updated.Result);
        Assert.Single(updated.Moves);
        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_ComputerGame_AiReplyDeliversCheckmate_EndsGameWithCorrectResult()
    {
        // The AI's reply rides the same game-end transitions as a human move, so a mating reply
        // ends the game with Black winning — and the reply branch does not fire again, because
        // the game is no longer Active.
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

        _chessAi.ChooseMove(afterHumanFen, PlayerColor.Black)
            .Returns(new ChosenMove { FromSquare = "d8", ToSquare = "h4" });

        _rulesEngine.TryApplyMove(afterHumanFen, PlayerColor.Black, "d8", "h4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Qh4#",
                ResultingFen = "rnb1kbnr/pppp1ppp/8/4p3/4P3/7q/PPPP1PPP/RNBQKBNR w KQkq - 0 3",
                IsCheck = true,
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.BlackWins, updated.Result);
        Assert.Equal(GameResultReason.Checkmate, updated.ResultReason);
        Assert.Equal(2, updated.Moves.Count);
        Assert.True(updated.Moves[1].IsCheckmate);

        // Exactly one consultation for the whole turn — the ended game is not fed back to the AI.
        _chessAi.Received(1).ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Theory]
    [InlineData(OpponentMode.Human)]
    [InlineData(null)]
    public async Task SubmitMoveAsync_HumanAndLegacyNullOpponentMode_DoNotTriggerAiReply(OpponentMode? opponentMode)
    {
        // The reply branch is selected by the Computer discriminator alone. An explicit Human
        // row — and a legacy NULL row, normalized to Human — must keep the pure two-player
        // flow even though an AI is available: one ply, one save, one notification, and the AI
        // is never asked for a move.
        var game = NewActiveGame(opponentMode);
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string resultingFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = resultingFen,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // Zero behavioural change for non-Computer games: the response describes the single
        // submitted ply and it is now the opponent's move.
        Assert.Same(game, updated);
        Assert.Equal(resultingFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Single(updated.Moves);
        Assert.Equal(OpponentMode.Human, updated.OpponentMode ?? OpponentMode.Human);

        _chessAi.DidNotReceive().ChooseMove(Arg.Any<string>(), Arg.Any<PlayerColor>());
        _rulesEngine.DidNotReceive().TryApplyMove(
            Arg.Any<string>(), PlayerColor.Black, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PromotionPieceType?>());
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task GetGameAsync_UnknownGame_ThrowsGameNotFoundException()
    {
        _repository.GetByIdWithMovesAsync(Arg.Any<Guid>()).Returns((Game?)null);

        await Assert.ThrowsAsync<GameNotFoundException>(() => _sut.GetGameAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMoveHistoryAsync_UnknownGame_ThrowsGameNotFoundException()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>()).Returns((Game?)null);

        await Assert.ThrowsAsync<GameNotFoundException>(() => _sut.GetMoveHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMoveHistoryAsync_KnownGame_ReturnsMovesFromRepository()
    {
        var game = NewActiveGame();
        var moves = new List<Move> { new() { GameId = game.Id, MoveNumber = 1, San = "e4" } };
        _repository.GetByIdAsync(game.Id).Returns(game);
        _repository.GetMovesAsync(game.Id).Returns(moves);

        var history = await _sut.GetMoveHistoryAsync(game.Id);

        Assert.Same(moves, history);
    }
}
