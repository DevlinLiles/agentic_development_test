using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IGameNotifier? _notifier;
    private readonly IGameAiResponder? _aiResponder;

    public GameService(IGameRepository repository, IChessRulesEngine rulesEngine, IGameNotifier? notifier = null)
        : this(repository, rulesEngine, notifier, aiResponder: null)
    {
    }

    public GameService(
        IGameRepository repository,
        IChessRulesEngine rulesEngine,
        IGameNotifier? notifier,
        IGameAiResponder? aiResponder)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _notifier = notifier;
        _aiResponder = aiResponder;
    }

    public async Task<Game> CreateGameAsync() => await CreateGameAsync(GameMode.TwoPlayer);

    public async Task<Game> CreateGameAsync(GameMode mode)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            WhiteSlotToken = Guid.NewGuid(),
            BlackSlotToken = mode == GameMode.VsAi ? Guid.NewGuid() : null,
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            Status = mode == GameMode.VsAi ? GameStatus.Active : GameStatus.WaitingForPlayer2,
            Mode = mode,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

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

        // For VsAi games the human always plays White, so only generate a reply when the move
        // just applied was White's (it is now the AI's turn as Black). If White's move already
        // ended the game there is nothing to reply to. Reuse the exact same application +
        // endgame-detection path the human move used.
        if (game.Mode == GameMode.VsAi && game.Status == GameStatus.Active && resolvedColor == PlayerColor.White)
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
    /// Selects and applies an automated (AI) reply for the side to move, reusing the shared
    /// <see cref="ApplyMoveToGame"/> + endgame-detection logic. No-ops when there is no AI
    /// responder wired in or when the responder declines to produce a move (e.g. no legal moves
    /// remaining, which only happens in a terminal position that would already have been ended
    /// by the preceding human move).
    /// </summary>
    private async Task GenerateAiReplyAsync(Game game)
    {
        if (_aiResponder is null)
        {
            return;
        }

        var reply = _aiResponder.SelectReply(game.CurrentFen, game.Turn);
        if (reply is null)
        {
            return;
        }

        var promotion = reply.PromotionPiece;
        if (_rulesEngine.IsPromotionMove(game.CurrentFen, reply.FromSquare, reply.ToSquare) && promotion is null)
        {
            // The responder should supply a promotion piece for promotion moves, but guard
            // against a degenerate responder by falling back to a queen rather than applying an
            // illegal/under-specified move.
            promotion = PromotionPieceType.Queen;
        }

        var aiColor = game.Turn;
        var result = _rulesEngine.TryApplyMove(game.CurrentFen, aiColor, reply.FromSquare, reply.ToSquare, promotion);

        if (!result.IsLegal)
        {
            // The responder is contractually obliged to return a legal move; if it does not,
            // leave the position untouched rather than persisting an illegal state.
            return;
        }

        ApplyMoveToGame(game, aiColor, reply.FromSquare, reply.ToSquare, promotion, result);
    }

    /// <summary>
    /// Persists the result of a legal move onto the in-memory <paramref name="game"/>: updates
    /// the FEN, flips the turn, records the <see cref="Move"/>, and applies the shared
    /// endgame-detection (checkmate / stalemate / fifty-move rule). Centralised here so the
    /// human move and the AI reply share identical update + endgame logic.
    /// </summary>
    private static void ApplyMoveToGame(
        Game game,
        PlayerColor moverColor,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion,
        MoveApplicationResult result)
    {
        var now = DateTime.UtcNow;
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
