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

    /// <summary>
    /// Creates a default two-player game. Kept for callers that don't yet specify a mode so the
    /// existing two-player flow is unchanged.
    /// </summary>
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
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
            Mode = mode,
        };

        if (mode == GameMode.VsAi)
        {
            // VsAi games fill the Black seat with a synthetic slot token so the human (White) can
            // start playing immediately — there is no second player to join, so the game goes
            // straight to Active instead of WaitingForPlayer2.
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

        ApplyMoveResult(game, resolvedColor, result, fromSquare, toSquare, promotion);

        // VsAi games: after the human's move, generate an inline AI reply for the side now to move,
        // reusing the same move-application and endgame-detection logic. The AI reply is only
        // generated when the human's move did not already end the game (e.g. the human delivered
        // checkmate) and an AI player is available.
        if (game.Mode == GameMode.VsAi && game.Status == GameStatus.Active && _aiPlayer is not null)
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
    /// Asks the registered AI player to choose a move for the side to move and applies it to the
    /// game, reusing <see cref="ApplyMoveResult"/> so endgame detection (checkmate/stalemate/
    /// fifty-move) is identical to the human move path. No-op if there are no legal moves.
    /// </summary>
    private async Task GenerateAiReplyAsync(Game game)
    {
        var aiColor = game.Turn;
        var legalMoves = _rulesEngine.GetAllLegalMoves(game.CurrentFen);

        if (legalMoves.Count == 0)
        {
            // The position is terminal; the human's move should have ended the game already, but
            // guard defensively rather than asking the AI to choose from an empty set.
            return;
        }

        var aiChoice = await _aiPlayer!.ChooseMoveAsync(new ChessAiMoveRequest
        {
            Fen = game.CurrentFen,
            SideToMove = aiColor,
            LegalMoves = legalMoves,
        });

        var aiResult = _rulesEngine.TryApplyMove(
            game.CurrentFen,
            aiColor,
            aiChoice.FromSquare,
            aiChoice.ToSquare,
            aiChoice.Promotion);

        if (!aiResult.IsLegal)
        {
            // The AI chose a move drawn from the legal set, so the engine should accept it; skip
            // defensively rather than propagating an inconsistent state.
            return;
        }

        ApplyMoveResult(game, aiColor, aiResult, aiChoice.FromSquare, aiChoice.ToSquare, aiChoice.Promotion);
    }

    /// <summary>
    /// Applies a single (already-validated) move result to the game: advances the FEN, flips the
    /// side to move, persists a <see cref="Move"/> record, and runs the shared endgame detection
    /// (checkmate, stalemate, fifty-move rule). Used by both the human move and the AI reply paths
    /// so the two never diverge on how a move is recorded or how the game ends.
    /// </summary>
    private static void ApplyMoveResult(
        Game game,
        PlayerColor moverColor,
        MoveApplicationResult result,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion)
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
