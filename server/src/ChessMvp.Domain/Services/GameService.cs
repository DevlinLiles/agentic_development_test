using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IGameNotifier? _notifier;
    private readonly IChessAi? _ai;

    public GameService(
        IGameRepository repository,
        IChessRulesEngine rulesEngine,
        IGameNotifier? notifier = null,
        IChessAi? ai = null)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _notifier = notifier;
        _ai = ai;
    }

    public async Task<Game> CreateGameAsync(GameOpponent opponent = GameOpponent.Human)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            WhiteSlotToken = Guid.NewGuid(),
            BlackSlotToken = null,
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            Status = GameStatus.WaitingForPlayer2,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
            IsVsAi = opponent == GameOpponent.Ai,
        };

        if (game.IsVsAi)
        {
            // Seat the AI as black immediately and start play. The AI never moves first (white
            // always starts), so there's nothing to play here — we just mark the game active.
            // No broadcast is needed on create: the creator navigates straight to the game page
            // and loads state over REST, and no second client is watching yet.
            game.BlackSlotToken = Guid.NewGuid();
            game.Status = GameStatus.Active;
        }

        await _repository.AddAsync(game);
        await _repository.SaveChangesAsync();

        return game;
    }

    public async Task<Game> JoinGameAsync(Guid gameId)
    {
        var game = await _repository.GetByIdAsync(gameId) ?? throw new GameNotFoundException(gameId);

        if (game.IsVsAi)
        {
            throw new GameNotActiveException("This game is against the AI; there is no second seat to join.");
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

        ApplyMoveResult(game, resolvedColor, fromSquare, toSquare, promotion, result);

        // If the human's move didn't end the game and the AI is on move next, play the AI's reply
        // within the same unit of work so a single REST/SignalR round-trip lands both plies and
        // the client never observes an intermediate "AI to move" state it can't act on anyway.
        if (game.Status == GameStatus.Active && game.IsVsAi && game.Turn == PlayerColor.Black)
        {
            await PlayAiMoveAsync(game);
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

    private async Task PlayAiMoveAsync(Game game)
    {
        if (_ai is null)
        {
            // AI wasn't wired up (e.g. in a test host that omitted it). Rather than silently hang
            // the game waiting for a move that will never come, treat this as a configuration
            // error surfaced to the operator — the human's move has already been applied and
            // persisted below, so the game is in a consistent, recoverable state.
            throw new InvalidOperationException(
                "An AI game requires an IChessAi to be registered, but none was found.");
        }

        var aiMove = _ai.ChooseMove(game.CurrentFen);
        if (aiMove is null)
        {
            // No legal moves for the AI. The position is terminal and was already classified when
            // the move that produced the current FEN was applied, so there's nothing to do.
            return;
        }

        if (_rulesEngine.IsPromotionMove(game.CurrentFen, aiMove.FromSquare, aiMove.ToSquare)
            && aiMove.Promotion is null)
        {
            // The AI should always report a promotion piece for a promoting move; fall back to
            // queen so we never deadlock waiting for a choice it didn't make.
            aiMove = aiMove with { Promotion = PromotionPieceType.Queen };
        }

        var aiResult = _rulesEngine.TryApplyMove(
            game.CurrentFen,
            PlayerColor.Black,
            aiMove.FromSquare,
            aiMove.ToSquare,
            aiMove.Promotion);

        if (!aiResult.IsLegal)
        {
            // The AI only chose from legal moves, so this should be unreachable. Leave the game
            // in the post-human-move state rather than corrupting it with an illegal ply.
            return;
        }

        ApplyMoveResult(game, PlayerColor.Black, aiMove.FromSquare, aiMove.ToSquare, aiMove.Promotion, aiResult);
    }

    private void ApplyMoveResult(
        Game game,
        PlayerColor color,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion,
        MoveApplicationResult result)
    {
        var now = DateTime.UtcNow;
        var nextMoveNumber = (game.Moves.Count == 0 ? 0 : game.Moves.Max(m => m.MoveNumber)) + 1;

        game.CurrentFen = result.ResultingFen!;
        game.Turn = color == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
        game.HalfmoveClock = ParseHalfmoveClock(result.ResultingFen!);
        game.UpdatedUtc = now;

        game.Moves.Add(new Move
        {
            GameId = game.Id,
            MoveNumber = nextMoveNumber,
            PlyColor = color,
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
            game.Result = color == PlayerColor.White ? GameResult.WhiteWins : GameResult.BlackWins;
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
