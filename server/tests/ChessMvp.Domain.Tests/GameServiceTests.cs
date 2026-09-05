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
    private readonly IGameAiResponder _aiResponder = Substitute.For<IGameAiResponder>();
    private readonly GameService _sut;

    public GameServiceTests()
    {
        // The 4-arg constructor threads the optional AI responder through; for TwoPlayer games
        // (the default entity Mode) the responder is never consulted, so existing two-player
        // tests remain unaffected by its presence.
        _sut = new GameService(_repository, _rulesEngine, _notifier, _aiResponder);
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

    private static Game NewActiveVsAiGame() => new()
    {
        Id = Guid.NewGuid(),
        WhiteSlotToken = WhiteToken,
        BlackSlotToken = BlackToken,
        CurrentFen = ChessConstants.StartingFen,
        Turn = PlayerColor.White,
        Status = GameStatus.Active,
        Mode = GameMode.VsAi,
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
        Assert.Equal(GameMode.TwoPlayer, game.Mode);
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task CreateGameAsync_WithTwoPlayer_InitializesWaitingWithNullBlackToken()
    {
        var game = await _sut.CreateGameAsync(GameMode.TwoPlayer);

        Assert.Equal(GameMode.TwoPlayer, game.Mode);
        Assert.Equal(GameStatus.WaitingForPlayer2, game.Status);
        Assert.NotNull(game.WhiteSlotToken);
        Assert.Null(game.BlackSlotToken);
        await _repository.Received(1).AddAsync(game);
        await _repository.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task CreateGameAsync_WithVsAi_InitializesWithSyntheticBlackTokenAndActiveStatus()
    {
        var game = await _sut.CreateGameAsync(GameMode.VsAi);

        Assert.Equal(GameMode.VsAi, game.Mode);
        // VsAi games skip the waiting-for-player2 step: a synthetic BlackSlotToken stands in for
        // the absent human opponent and the game starts Active so White can move immediately.
        Assert.Equal(GameStatus.Active, game.Status);
        Assert.NotNull(game.WhiteSlotToken);
        Assert.NotNull(game.BlackSlotToken);
        Assert.NotEqual(game.WhiteSlotToken, game.BlackSlotToken);
        Assert.Equal(ChessConstants.StartingFen, game.CurrentFen);
        Assert.Equal(PlayerColor.White, game.Turn);
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
    public async Task SubmitMoveAsync_TwoPlayerGame_DoesNotInvokeAiResponder()
    {
        // Two-player flow must remain completely unchanged even when an AI responder is wired in:
        // the responder is never consulted for a TwoPlayer game.
        var game = NewActiveGame();
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

        Assert.Single(updated.Moves);
        _aiResponder.DidNotReceive().SelectReply(Arg.Any<string>(), Arg.Any<PlayerColor>());
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_VsAi_GeneratesAiReplyAfterHumanMove()
    {
        var game = NewActiveVsAiGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterWhiteFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        const string afterBlackFen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 3";

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterWhiteFen,
            });

        // The AI responder is consulted with the position after the human move (Black to move).
        _aiResponder.SelectReply(afterWhiteFen, PlayerColor.Black)
            .Returns(new AiReplyMove("b8", "c6", null));

        _rulesEngine.TryApplyMove(afterWhiteFen, PlayerColor.Black, "b8", "c6", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Nc6",
                ResultingFen = afterBlackFen,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        // Both the human move and the AI reply are recorded, and it is White's turn again.
        Assert.Equal(2, updated.Moves.Count);
        Assert.Equal(PlayerColor.White, updated.Moves[0].PlyColor);
        Assert.Equal("e4", updated.Moves[0].San);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
        Assert.Equal("Nc6", updated.Moves[1].San);
        Assert.Equal(afterBlackFen, updated.CurrentFen);
        Assert.Equal(PlayerColor.White, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);

        // Both moves are persisted in a single save and broadcast once at the end.
        await _repository.Received(1).SaveChangesAsync();
        await _notifier.Received(1).NotifyGameUpdatedAsync(game);
    }

    [Fact]
    public async Task SubmitMoveAsync_VsAi_WhenHumanMoveEndsGame_DoesNotGenerateAiReply()
    {
        var game = NewActiveVsAiGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string matingFen = "rnb1kbnr/pppp1Q1p/5p2/4p3/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 0 4";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "f1", "f7", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Qxf7#",
                ResultingFen = matingFen,
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "f1", "f7", null);

        // The human's mating move ends the game, so there is nothing for the AI to reply to.
        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.WhiteWins, updated.Result);
        Assert.Single(updated.Moves);
        _aiResponder.DidNotReceive().SelectReply(Arg.Any<string>(), Arg.Any<PlayerColor>());
    }

    [Fact]
    public async Task SubmitMoveAsync_VsAi_AiReplyEndingInCheckmate_ReusesEndgameDetection()
    {
        var game = NewActiveVsAiGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterWhiteFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        const string matingFen = "rnb1kbnr/pppp1qpp/5p2/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 3";

        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterWhiteFen,
            });

        _aiResponder.SelectReply(afterWhiteFen, PlayerColor.Black)
            .Returns(new AiReplyMove("d8", "e7", null));

        // The AI's reply is itself a checkmate — the same endgame-detection logic used for the
        // human move must classify it and end the game with Black winning.
        _rulesEngine.TryApplyMove(afterWhiteFen, PlayerColor.Black, "d8", "e7", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "Qe7#",
                ResultingFen = matingFen,
                IsCheckmate = true,
            });

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        Assert.Equal(GameStatus.Ended, updated.Status);
        Assert.Equal(GameResult.BlackWins, updated.Result);
        Assert.Equal(GameResultReason.Checkmate, updated.ResultReason);
        Assert.Equal(2, updated.Moves.Count);
        Assert.True(updated.Moves[1].IsCheckmate);
        Assert.Equal(PlayerColor.Black, updated.Moves[1].PlyColor);
    }

    [Fact]
    public async Task SubmitMoveAsync_VsAi_WhenAiResponderReturnsNull_RecordsOnlyHumanMove()
    {
        var game = NewActiveVsAiGame();
        _repository.GetByIdWithMovesAsync(game.Id).Returns(game);

        const string afterWhiteFen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        _rulesEngine.TryApplyMove(game.CurrentFen, PlayerColor.White, "e2", "e4", null)
            .Returns(new MoveApplicationResult
            {
                IsLegal = true,
                San = "e4",
                ResultingFen = afterWhiteFen,
            });

        // Responder declines to produce a move (e.g. terminal position / engine failure).
        _aiResponder.SelectReply(afterWhiteFen, PlayerColor.Black).Returns((AiReplyMove?)null);

        var updated = await _sut.SubmitMoveAsync(game.Id, WhiteToken, "e2", "e4", null);

        Assert.Single(updated.Moves);
        Assert.Equal(PlayerColor.Black, updated.Turn);
        Assert.Equal(GameStatus.Active, updated.Status);
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
