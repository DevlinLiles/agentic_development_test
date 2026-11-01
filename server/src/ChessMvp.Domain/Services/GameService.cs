using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;

namespace ChessMvp.Domain.Services;

public sealed class GameService : IGameService
{
    private readonly IGameRepository _repository;
    private readonly IChessRulesEngine _rulesEngine;
    private readonly IGameNotifier? _notifier;
    private readonly IHeuristicChessEngine? _aiEngine;

    // notifier/aiEngine stay optional (null in unit tests) so existing test constructions keep
    // compiling and behaving exactly as before; the composition root supplies both.
    public GameService(
        IGameRepository repository,
        IChessRulesEngine rulesEngine,
        IGameNotifier? notifier = null,
        IHeuristicChessEngine? aiEngine = null)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _notifier = notifier;
        _aiEngine = aiEngine;
    }

    public async Task<Game> CreateGameAsync(OpponentMode opponentMode = OpponentMode.Human)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            WhiteSlotToken = Guid.NewGuid(),
            BlackSlotToken = null,
            OpponentMode = OpponentMode.Human,
            CurrentFen = ChessConstants.StartingFen,
            Turn = PlayerColor.White,
            Status = GameStatus.WaitingForPlayer2,
            HalfmoveClock = 0,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        // Only the Computer discriminator selects this branch; every other value (the Human
        // default, a legacy null, or an undefined enum cast) keeps the statement-for-statement
        // two-player setup above, so the parameterless/defaulted call behaves exactly as it
        // did before the parameter existed.
        if (opponentMode == OpponentMode.Computer)
        {
            // A computer game has both seats filled the instant it exists: the human caller
            // keeps White (the seat minted above) and the server-side AI takes Black. The AI
            // seat gets its own slot token at creation so the engine submits its replies
            // through the same token-resolution path a human player would, and the seat is
            // never open to a join. Both seats being occupied also means the game skips
            // WaitingForPlayer2 entirely and is immediately playable.
            game.BlackSlotToken = Guid.NewGuid();
            game.OpponentMode = OpponentMode.Computer;
            game.Status = GameStatus.Active;
        }

        await _repository.AddAsync(game);
        await _repository.SaveChangesAsync();

        return game;
    }

    public async Task<Game> JoinGameAsync(Guid gameId)
    {
        var game = await _repository.GetByIdAsync(gameId) ?? throw new GameNotFoundException(gameId);

        // A computer game's Black seat belongs to the server-side AI from the moment of
        // creation, so it is never joinable. This deliberately reuses the existing
        // "not accepting joins" vocabulary rather than introducing a new exception type, so
        // the API keeps answering 409 GameNotActive for it. The generic guard below would
        // already reject these games (their Black slot token is minted at creation and their
        // status is Active, not WaitingForPlayer2), but checking the discriminator makes the
        // intent explicit and keeps the guarantee if either of those invariants ever shifts.
        if ((game.OpponentMode ?? OpponentMode.Human) == OpponentMode.Computer)
        {
            throw new GameNotActiveException("This game is against the computer and cannot be joined by another player.");
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

        ApplyMove(game, slotToken, fromSquare, toSquare, promotion);

        var isComputerGame = (game.OpponentMode ?? OpponentMode.Human) == OpponentMode.Computer;

        // FR-1: in a computer game, exactly one Black reply is generated and applied inside
        // this same call (NFR-4), so the submission response already reflects both plies
        // (FR-6) and no "Active + Black to move" state can persist past the request.
        if (isComputerGame
            && game.Status == GameStatus.Active
            && game.Turn == PlayerColor.Black
            && _aiEngine is not null)
        {
            var selection = _aiEngine.SelectMove(game.CurrentFen, PlayerColor.Black);

            // FR-2: with no legal Black moves the game-end machinery owns the position; the AI
            // takes no action. SelectMove never fabricates a move, so a returned selection is
            // legal for this position by construction and, the board having two kings on it
            // (see ApplyMove's check), never one of our own pieces.
            if (selection is not null)
            {
                ApplyMove(game, game.BlackSlotToken!.Value, selection.FromSquare, selection.ToSquare, selection.PromotionPiece);
            }
        }

        // Both plies persist atomically with one save (Q-1, option 2): if the AI step throws,
        // nothing is written, the optimistic Version stamp never moves, and the client can
        // simply retry the identical move.
        await _repository.SaveChangesAsync();

        // AC-10: one full-snapshot push covers both plies. A connected client replaces state
        // wholesale, so a single broadcast is indistinguishable from two.
        if (_notifier is not null)
        {
            await _notifier.NotifyGameUpdatedAsync(game);
        }

        return game;
    }

    /// <summary>
    /// Validates and applies a single ply to the already-loaded <paramref name="game"/>: rules
    /// validation, move entity, turn, FEN, halfmove clock and the game-end transitions. This is
    /// the one code path for move application — the AI's reply rides it exactly like a human
    /// move (FR-5).
    /// </summary>
    /// <remarks>
    /// Pure move-within-method extraction: this body is statement-for-statement the ply logic
    /// that previously ran inline in SubmitMoveAsync, so Human games behave exactly as before,
    /// including legacy rows whose OpponentMode is NULL (read as Human). ApplyMove never
    /// inspects OpponentMode — opponent-mode orchestration stays entirely with the caller.
    /// Deliberately persists nothing: the caller batches every ply of one submission into a
    /// single SaveChangesAsync so an AI-reply failure cannot leave a half-applied game.
    /// </remarks>
    private void ApplyMove(
        Game game,
        Guid slotToken,
        string fromSquare,
        string toSquare,
        PromotionPieceType? promotion)
    {
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
