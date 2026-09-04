using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IGameNotifier? _notifier;
    private readonly IChessAiPlayer? _aiPlayer;

    public GameService(
        IGameRepository repository,
        IChessRulesEngine rulesEngine,
        IGameNotifier? notifier = null,
        IChessAiPlayer? aiPlayer = null)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _notifier = notifier;
        _aiPlayer = aiPlayer;
    }

    public Task<Game> CreateGameAsync() => CreateGameAsync(GameMode.TwoPlayer);

    public async Task<Game> CreateGameAsync(GameMode mode)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            WhiteSlotToken = Guid.NewGuid(),
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            Mode = mode,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        if (mode == GameMode.VsAi)
        {
            // The AI occupies the black seat with a synthetic slot token so the rest of the game
            // flow (turn resolution, move history) treats it like any other player. There is no
            // second human to wait for, so the game starts active immediately.
            game.BlackSlotToken = Guid.NewGuid();
            game.Status = GameStatus.Active;
        }
        else
        {
            game.BlackSlotToken = null;
            game.Status = GameStatus.WaitingForPlayer2;
        }

        await _repository.AddAsync(game);
        await _repository.SaveChangesAsync();

        return game;
    }

    public async Task<Game> JoinGameAsync(Guid gameId)
    {
        var game = await _repository.GetByIdAsync(gameId) ?? throw new GameNotFoundException(gameId);

        if (game.BlackSlotToken is not null || game.Status != GameStatus.WaitingForPlayer2)
        {
            throw new GameNotActiveException("This game already has two players or is no longer accepting joins.");
        }

        game.BlackSlotToken = Guid.NewGuid();
        game.Status = GameStatus.Active;
        game.UpdatedUtc = DateTime.UtcNow;

        await _repository.SaveChangesAsync();

        if (_notifier is not null)
        {
            await _notifier.NotifyGameUpdatedAsync(game);
        }

        return game;
    }

    public async Task<Game> GetGameAsync(Guid gameId) =>
        await _repository.GetByIdWithMovesAsync(gameId) ?? throw new GameNotFoundException(gameId);

    public async Task<Game> SubmitMoveAsync(
        Guid gameId,
        Guid slotToken,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion)
    {
        var game = await _repository.GetByIdWithMovesAsync(gameId) ?? throw new GameNotFoundException(gameId);

        var resolvedColor = ResolveSlotToken(game, slotToken);

        if (game.Status != GameStatus.Active)
        {
            throw new GameNotActiveException("This game is not currently active.");
        }

        if (resolvedColor != game.Turn)
        {
            throw new NotYourTurnException();
        }

        if (_rulesEngine.IsPromotionMove(game.CurrentFen, fromSquare, toSquare) && promotion is null)
        {
            throw new PromotionRequiredException();
        }

        var result = _rulesEngine.TryApplyMove(game.CurrentFen, resolvedColor, fromSquare, toSquare, promotion);

        if (!result.IsLegal)
        {
            throw new IllegalMoveException($"Move {fromSquare}-{toSquare} is not legal: {result.FailureReason}.");
        }

        RecordMove(game, resolvedColor, fromSquare, toSquare, promotion, result, DateTime.UtcNow);

        // For VsAi games the human (White) has just moved, so hand the now-AI turn back to the AI
        // engine and apply its reply inline, reusing the same move-recording/endgame detection as
        // the human move above. Two-player games skip this entirely.
        await GenerateAiReplyAsync(game);

        await _repository.SaveChangesAsync();

        if (_notifier is not null)
        {
            await _notifier.NotifyGameUpdatedAsync(game);
        }

        return game;
    }

    public async Task<IReadOnlyList<Move>> GetMoveHistoryAsync(Guid gameId)
    {
        // Confirm the game exists so callers get a 404-mappable exception instead of an empty list.
        _ = await _repository.GetByIdAsync(gameId) ?? throw new GameNotFoundException(gameId);

        return await _repository.GetMovesAsync(gameId);
    }

    /// <summary>
    /// When the game is VsAi and it is the AI's turn (the human always plays White, the AI holds
    /// the synthetic black seat), asks the AI engine for a move and applies it through the same
    /// rules engine and <see cref="RecordMove"/> path used for human moves, so endgame detection
    /// is reused verbatim. No-ops for two-player games, ended games, or when no AI engine is
    /// configured (e.g. in unit tests).
    /// </summary>
    private async Task GenerateAiReplyAsync(Game game)
    {
        if (game.Mode != GameMode.VsAi || _aiPlayer is null)
        {
            return;
        }

        if (game.Status != GameStatus.Active || game.Turn != PlayerColor.Black)
        {
            return;
        }

        var aiChoice = await _aiPlayer.ChooseMoveAsync(game.CurrentFen, game.Turn, CancellationToken.None);
        if (!aiChoice.Success || aiChoice.Move is null)
        {
            return;
        }

        var aiMove = aiChoice.Move;
        var applied = _rulesEngine.TryApplyMove(
            game.CurrentFen, game.Turn, aiMove.FromSquare, aiMove.ToSquare, aiMove.Promotion);

        if (!applied.IsLegal)
        {
            return;
        }

        RecordMove(game, game.Turn, aiMove.FromSquare, aiMove.ToSquare, aiMove.Promotion, applied, DateTime.UtcNow);
    }

    /// <summary>
    /// Applies a validated <see cref="MoveApplicationResult"/> to the game aggregate: advances the
    /// position/turn/halfmove clock, appends the move to the history, and runs endgame detection
    /// (checkmate, stalemate, fifty-move rule). Shared by both the human move and the inline AI
    /// reply so endgame handling is implemented exactly once.
    /// </summary>
    private static void RecordMove(
        Game game,
        PlayerColor moverColor,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion,
        MoveApplicationResult result,
        DateTime now)
    {
        var nextMoveNumber = (game.Moves.Count == 0 ? 0 : game.Moves.Max(m => m.MoveNumber)) + 1;

        game.CurrentFen = result.ResultingFen!;
        game.Turn = moverColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        game.HalfmoveClock = ParseHalfmoveClock(result.ResultingFen!);
        game.UpdatedUtc = now;

        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = nextMoveNumber,
            PlyColor = moverColor,
            San = result.San!,
            FromSquare = fromSquare,
            ToSquare = toSquare,
            PromotionPiece = promotion,
            ResultingFen = result.ResultingFen!,
            IsCheck = result.IsCheck,
            IsCheckmate = result.IsCheckmate,
            CreatedUtc = now,
        });

        if (result.IsCheckmate)
        {
            game.Status = GameStatus.Ended;
            game.Result = moverColor == PlayerColor.White ? GameResult.WhiteWins : GameResult.BlackWins;
            game.ResultReason = GameResultReason.Checkmate;
        }
        else if (result.IsStalemate)
        {
            game.Status = GameStatus.Ended;
            game.Result = GameResult.Draw;
            game.ResultReason = GameResultReason.Stalemate;
        }
        else if (result.IsFiftyMoveDraw)
        {
            game.Status = GameStatus.Ended;
            game.Result = GameResult.Draw;
            game.ResultReason = GameResultReason.FiftyMoveRule;
        }
    }

    private static PlayerColor ResolveSlotToken(Game game, Guid slotToken)
    {
        if (game.WhiteSlotToken == slotToken)
        {
            return PlayerColor.White;
        }

        if (game.BlackSlotToken == slotToken)
        {
            return PlayerColor.Black;
        }

        throw new InvalidSlotTokenException();
    }

    /// <summary>
    /// The rules engine already computes the fifty-move-rule counter correctly as part of the
    /// resulting FEN's halfmove-clock field; parsing it back out here avoids reimplementing that
    /// logic in the domain layer.
    /// </summary>
    private static int ParseHalfmoveClock(string fen)
    {
        var fields = fen.Split(' ');
        return fields.Length > 4 && int.TryParse(fields[4], out var halfmoveClock) ? halfmoveClock : 0;
    }
}
