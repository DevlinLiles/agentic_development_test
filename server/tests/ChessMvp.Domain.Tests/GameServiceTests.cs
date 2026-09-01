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

    [Fact]
    public async Task CreateGameAsync_ReturnsWaitingGameWithWhiteToken()
    {
        var game = await _sut.CreateGameAsync();

        Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
        Assert.NotNull(game.WhiteSlotToken);
        Assert.Null(game.BlackSlotToken);
        Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);
        Assert.Equal(PlayerColor.White, game.Turn);
        Assert.Equal(OpponentType.Human, game.OpponentType);
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task CreateGameAsync_AiOpponent_ReturnsActiveGameWithReservedBlackSeat()
    {
        // An AI game is single-user: it must start Active (no waiting room) and reserve the
        // Black seat up front so the AI always has a stable token to act under.
        var game = await _sut.CreateGameAsync(OpponentType.Ai);

        Assert.Equal(GameStatus.Active, game.Status);
        Assert.NotNull(game.WhiteSlotToken);
        Assert.NotNull(game.BlackSlotToken);
        Assert.NotEqual(game.WhiteSlotToken, game.BlackSlotToken);
        Assert.Equal(PlayerColor.White, game.Turn);
        Assert.Equal(OpponentType.Ai, game.OpponentType);
        Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
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
    public async Task JoinGameAsync_OpenSeat_AssignsBlackTokenAndActivates()
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            WhiteSlotToken = WhiteToken,
            BlackSlotToken = null,
            Status = GameStatus.WaitingForPlayer2,
            CurrentFen = ChessConstants.StartingFen,
        };
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
