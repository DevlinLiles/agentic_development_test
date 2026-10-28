using ChessMvp.Api.Contracts;
using ChessMvp.Domain.Entities;
using ChessMvp.Domain.Exceptions;
using ChessMvp.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    public async Task<ActionResult<CreateGameResponse>> CreateGame(
        // EmptyBodyBehavior.Allow is what makes the body optional: a POST with no body at all —
        // the only call shape that existed before opponent selection, still sent today by the
        // client's createGame() wrapper whenever no mode is passed — binds to a null request
        // instead of being rejected with 400 "the request does not have a body". Without it,
        // adding this body parameter would have silently broken every existing caller.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateGameRequest? request)
    {
        // A null request (no body) and a body that simply omits the opponent field both resolve
        // to the classic two-player game, which reproduces the pre-existing behavior exactly;
        // the domain layer applies the same default, so an explicit Human and an absent body
        // are indistinguishable.
        var opponentMode = request?.Opponent ?? OpponentMode.Human;

        var game = await _gameService.CreateGameAsync(opponentMode);

        // The client owns its own origin, so we hand back a relative path rather than guessing
        // the client's scheme/host from this API request.
        var joinUrl = $"/game/{game.Id}";

        var response = new CreateGameResponse(
            GameId: game.Id,
            PlayerToken: game.WhiteSlotToken!.Value,
            Color: PlayerColor.White,
            JoinUrl: joinUrl,
            GameState: GameStateResponse.FromGame(game, PlayerColor.White))
        {
            // Echo how the second seat of this game is (or can be) filled — the mode the game
            // was actually created with, never a pass-through of the request — so a legacy
            // null discriminator is normalized to Human here too.
            OpponentMode = game.OpponentMode ?? OpponentMode.Human,
        };

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
