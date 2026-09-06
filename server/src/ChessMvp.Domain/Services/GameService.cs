using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IChessAiPlayer? _aiPlayer;
    private readonly IGameNotifier? _notifier;

    public GameService(IGameRepository repository, IChessRulesEngine rulesEngine, IGameNotifier? notifier = null)
        : this(repository, rulesEngine, aiPlayer: null, notifier)
    {
    }

    public GameService(
        IGameRepository repository,
        IChessRulesEngine rulesEngine,
        IChessAiPlayer? aiPlayer,
        IGameNotifier? notifier = null)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _aiPlayer = aiPlayer;
        _notifier = notifier;
    }

    public Task<Game> CreateGameAsync() => CreateGameAsync(GameMode.TwoPlayer);

    public async Task<Game> CreateGameAsync(GameMode mode)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Mode = mode,
            WhiteSlotToken = Guid.NewGuid(),
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        if (mode == GameMode.VsAi)
        {
            // No human will ever hold the Black seat, but the rest of the move pipeline keys off
            // BlackSlotToken to authorize moves and resolve which color a token belongs to. Assign
            // a synthetic token that is never returned to any client so AI replies can be applied
            // through the same code path as human moves, and skip the waiting-for-player2 state
            // entirely: the game is immediately active with the human on White.
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

        ApplyMoveToGame(game, resolvedColor, fromSquare, toSquare, promotion, result);

        // For VsAi games the human just moved (White); generate and apply the AI's reply in-line so
        // the returned game state already reflects both plies. The human always plays White, so the
        // AI replies as Black. Endgame detection is reused for the AI move, and if the human's move
        // already ended the game we skip the AI turn entirely.
        if (game is { Mode: GameMode.VsAi, Status: GameStatus.Active, Turn: PlayerColor.Black })
        {
            await GenerateAiReplyAsync(game);
        }

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
    /// Selects the AI's reply for the side to move and applies it through the same move/endgame
    /// pipeline used for human moves, so endgame detection logic is reused rather than duplicated.
    /// </summary>
    private async Task GenerateAiReplyAsync(Game game)
    {
        if (_aiPlayer is null)
        {
            // No AI player was wired up (e.g. a test that constructed GameService without one).
            // Leave the game awaiting the AI's move rather than throwing, so the human move still
            // persists. In production AddChessAiPlayer always registers an IChessAiPlayer.
            return;
        }

        var aiResult = await _aiPlayer.SelectMoveAsync(
            game.CurrentFen,
            game.Turn,
            AiSearchOptions.Shallow(),
            CancellationToken.None);

        if (aiResult.Status != AiMoveStatus.MoveSelected)
        {
            // No legal move for the AI (checkmate/stalemate) or the search was cancelled. The
            // preceding human move already toggled the turn, and endgame detection for the human
            // move has run; if the AI has no legal move the position is a terminal one that the
            // rules engine flagged when applying the human's move, so there is nothing more to do.
            return;
        }

        var application = _rulesEngine.TryApplyMove(
            game.CurrentFen,
            game.Turn,
            aiResult.FromSquare!,
            aiResult.ToSquare!,
            aiResult.Promotion);

        if (!application.IsLegal || application.ResultingFen is null)
        {
            // The AI returned a move that the rules engine rejects. Treat this as a no-op rather
            // than surfacing an error to the human; the game simply awaits an AI move it will not
            // receive. This is defensive — the AI only ever returns moves it enumerated as legal.
            return;
        }

        ApplyMoveToGame(
            game,
            game.Turn,
            aiResult.FromSquare!,
            aiResult.ToSquare!,
            aiResult.Promotion,
            application);
    }

    /// <summary>
    /// Applies a validated <see cref="MoveApplicationResult"/> to the game: updates the FEN, turn,
    /// halfmove clock, appends a <see cref="Move"/> record, and applies the shared endgame
    /// detection (checkmate / stalemate / fifty-move) that both human and AI moves rely on.
    /// </summary>
    private static void ApplyMoveToGame(
        Game game,
        PlayerColor mover,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion,
        MoveApplicationResult result)
    {
        var now = DateTime.UtcNow;
        var nextMoveNumber = (game.Moves.Count == 0 ? 0 : game.Moves.Max(m => m.MoveNumber)) + 1;

        game.CurrentFen = result.ResultingFen!;
        game.Turn = mover == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        game.HalfmoveClock = ParseHalfmoveClock(result.ResultingFen!);
        game.UpdatedUtc = now;

        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = nextMoveNumber,
            PlyColor = mover,
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
            game.Result = mover == PlayerColor.White ? GameResult.WhiteWins : GameResult.BlackWins;
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
