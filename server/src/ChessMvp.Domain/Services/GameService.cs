using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IGameNotifier? _notifier;

    public GameService(IGameRepository repository, IChessRulesEngine rulesEngine, IGameNotifier? notifier = null)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _notifier = notifier;
    }

    public async Task<Game> CreateGameAsync(GameOpponentType opponent, PlayerColor mode)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        if (opponent == GameOpponentType.Ai)
        {
            // The human requested `mode` as their side, so the AI takes the opposite seat. The game
            // is immediately Active — there is no second human player to wait for. The AI's opening
            // move is intentionally NOT computed or applied here; a later step does that. For the
            // human-plays-Black case that leaves the game Active with Turn = White (the AI), which
            // is the correct initialised state until the AI move is generated.
            var aiColor = mode == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;

            game.OpponentType = GameOpponentType.Ai;
            game.AiColor = aiColor;
            game.Status = GameStatus.Active;

            // The human keeps the slot token for their colour so they can submit moves; the AI seat
            // gets no token because AI moves are applied server-side, not through the join/move
            // token path (and joining an AI game is rejected outright — see JoinGameAsync).
            if (mode == PlayerColor.White)
            {
                game.WhiteSlotToken = Guid.NewGuid();
                game.BlackSlotToken = null;
            }
            else
            {
                game.WhiteSlotToken = null;
                game.BlackSlotToken = Guid.NewGuid();
            }
        }
        else
        {
            // Human-vs-human: unchanged from the original behaviour — the creator plays White and
            // the game waits for a second player to join. `mode` is intentionally ignored here so
            // human games behave exactly as before.
            game.OpponentType = GameOpponentType.Human;
            game.AiColor = null;
            game.WhiteSlotToken = Guid.NewGuid();
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

        // AI games have no open human seat — the computer plays the second color, so joining one
        // as player 2 would either deadlock the board or overwrite the AI's slot. Reject early.
        if (game.OpponentType == GameOpponentType.Ai)
        {
            throw new GameIsAiOpponentException(gameId);
        }

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

        var now = DateTime.UtcNow;
        var nextMoveNumber = (game.Moves.Count == 0 ? 0 : game.Moves.Max(m => m.MoveNumber)) + 1;

        game.CurrentFen = result.ResultingFen!;
        game.Turn = resolvedColor == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        game.HalfmoveClock = ParseHalfmoveClock(result.ResultingFen!);
        game.UpdatedUtc = now;

        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = nextMoveNumber,
            PlyColor = resolvedColor,
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
            game.Result = resolvedColor == PlayerColor.White ? GameResult.WhiteWins : GameResult.BlackWins;
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
