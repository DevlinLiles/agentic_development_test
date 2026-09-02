using ChessMvp.Api.Contracts;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChessMvp.Api.Controllers;

[ApiController]
[Route("api/games")]
public class GamesController : ControllerBase
{
    private const string PlayerTokenHeader = "X-Player-Token";

    private readonly IGameService _gameService;

    public GamesController(IGameService gameService)
    {
        _gameService = gameService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateGameResponse>> CreateGame([FromBody] CreateGameRequest? request)
    {
        // A null body is valid: it creates the original human-vs-human game that waits for player 2.
        var opponent = request?.OpponentValue ?? GameOpponentType.Human;
        var mode = request?.ModeValue ?? PlayerColor.White;

        var game = await _gameService.CreateGameAsync(opponent, mode);

        // For human-vs-human the creator always plays White; for AI games the creator plays the
        // side they requested (`mode`) and the AI takes the opposite seat. The slot token for the
        // creator's colour is handed back; the AI seat has no token (its moves are server-side).
        // The service guarantees a token exists for the creator's colour, so the null-forgiving
        // operator here is safe.
        var creatorColor = opponent == GameOpponentType.Ai ? mode : PlayerColor.White;
        var playerToken = creatorColor == PlayerColor.White
            ? game.WhiteSlotToken!.Value
            : game.BlackSlotToken!.Value;

        // The client owns its own origin, so we hand back a relative path rather than guessing
        // the client's scheme/host from this API request. AI games have no join URL, but the field
        // is kept for contract consistency; the client simply won't render a share link for them.
        var joinUrl = opponent == GameOpponentType.Ai ? null : $"/game/{game.Id}";

        var response = new CreateGameResponse(
            GameId: game.Id,
            PlayerToken: playerToken,
            Color: creatorColor,
            JoinUrl: joinUrl,
            GameState: GameStateResponse.FromGame(game, creatorColor));

        return CreatedAtAction(nameof(GetGame), new { gameId = game.Id }, response);
    }

    [HttpPost("{gameId:guid}/join")]
    public async Task<ActionResult<JoinGameResponse>> JoinGame(Guid gameId)
    {
        try
        {
            var game = await _gameService.JoinGameAsync(gameId);

            var response = new JoinGameResponse(
                GameId: game.Id,
                PlayerToken: game.BlackSlotToken!.Value,
                Color: PlayerColor.Black,
                GameState: GameStateResponse.FromGame(game, PlayerColor.Black));

            return Ok(response);
        }
        catch (GameNotFoundException)
        {
            return NotFound(new ErrorResponse("GameNotFound"));
        }
        catch (GameIsAiOpponentException ex)
        {
            return Conflict(new ErrorResponse("GameIsAiOpponent", ex.Message));
        }
        catch (GameNotActiveException ex)
        {
            return Conflict(new ErrorResponse("GameNotActive", ex.Message));
        }
    }

    [HttpGet("{gameId:guid}")]
    public async Task<ActionResult<GameStateResponse>> GetGame(Guid gameId)
    {
        try
        {
            var game = await _gameService.GetGameAsync(gameId);
            var yourColor = TryResolveColor(game, TryGetPlayerToken());

            return Ok(GameStateResponse.FromGame(game, yourColor));
        }
        catch (GameNotFoundException)
        {
            return NotFound(new ErrorResponse("GameNotFound"));
        }
    }

    [HttpPost("{gameId:guid}/moves")]
    public async Task<ActionResult<GameStateResponse>> SubmitMove(Guid gameId, [FromBody] MoveRequest request)
    {
        var playerToken = TryGetPlayerToken();
        if (playerToken is null)
        {
            return Unauthorized(new ErrorResponse("InvalidPlayerToken", $"{PlayerTokenHeader} header is required."));
        }

        try
        {
            var game = await _gameService.SubmitMoveAsync(
                gameId,
                playerToken.Value,
                request.FromSquare,
                request.ToSquare,
                request.Promotion);

            var yourColor = TryResolveColor(game, playerToken);
            return Ok(GameStateResponse.FromGame(game, yourColor));
        }
        catch (GameNotFoundException)
        {
            return NotFound(new ErrorResponse("GameNotFound"));
        }
        catch (InvalidSlotTokenException ex)
        {
            return Unauthorized(new ErrorResponse("InvalidPlayerToken", ex.Message));
        }
        catch (NotYourTurnException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("NotYourTurn", ex.Message));
        }
        catch (GameNotActiveException ex)
        {
            return Conflict(new ErrorResponse("GameNotActive", ex.Message));
        }
        catch (PromotionRequiredException)
        {
            return BadRequest(new ErrorResponse("PromotionRequired"));
        }
        catch (IllegalMoveException ex)
        {
            return BadRequest(new ErrorResponse("IllegalMove", ex.Message));
        }
        catch (GameStateConflictException ex)
        {
            return Conflict(new ErrorResponse("GameStateConflict", ex.Message));
        }
    }

    [HttpGet("{gameId:guid}/moves")]
    public async Task<ActionResult<MoveHistoryResponse>> GetMoveHistory(Guid gameId)
    {
        try
        {
            var moves = await _gameService.GetMoveHistoryAsync(gameId);
            return Ok(MoveHistoryResponse.FromMoves(moves));
        }
        catch (GameNotFoundException)
        {
            return NotFound(new ErrorResponse("GameNotFound"));
        }
    }

    private Guid? TryGetPlayerToken()
    {
        if (Request.Headers.TryGetValue(PlayerTokenHeader, out var values) &&
            Guid.TryParse(values.ToString(), out var token))
        {
            return token;
        }

        return null;
    }

    private static PlayerColor? TryResolveColor(Game game, Guid? playerToken)
    {
        if (playerToken is null)
        {
            return null;
        }

        if (game.WhiteSlotToken == playerToken)
        {
            return PlayerColor.White;
        }

        if (game.BlackSlotToken == playerToken)
        {
            return PlayerColor.Black;
        }

        return null;
    }
}
