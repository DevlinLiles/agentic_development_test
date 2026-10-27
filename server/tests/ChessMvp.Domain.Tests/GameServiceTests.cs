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
    private readonly GameService _sut;

    public GameServiceTests()
    {
        _sut = new GameService(_repository, _rulesEngine, _notifier);
    }

    private static Game NewActiveGame(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        WhiteSlotToken = WhiteToken,
        BlackSlotToken = BlackToken,
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
