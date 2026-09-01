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

    // Create a two-human game: starts WaitingForPlayer2 and returns a
    // shareable join link the creator forwards to their opponent.
    [HttpPost]
    public async Task<ActionResult<CreateGameResponse>> CreateGame()
    {
        var game = await _gameService.CreateGameAsync(OpponentType.Human);

        // The client owns its own origin, so we hand back a relative path rather than guessing
        // the client's scheme/host from this API request.
        var joinUrl = $"/game/{game.Id}";

        var response = new CreateGameResponse(
            GameId: game.Id,
            PlayerToken: game.WhiteSlotToken!.Value,
            Color: PlayerColor.White,
            JoinUrl: joinUrl,
            OpponentType: game.OpponentType,
            GameState: GameStateResponse.FromGame(game, PlayerColor.White));

        return CreatedAtAction(nameof(GetGame), new { gameId = game.Id }, response);
    }

    // Create a single-user game against the built-in AI: starts Active
    // immediately with the AI occupying the Black seat, so there is no join
    // link to share.
    [HttpPost("ai")]
    public async Task<ActionResult<CreateGameResponse>> CreateAiGame()
    {
        var game = await _gameService.CreateGameAsync(OpponentType.Ai);

        var response = new CreateGameResponse(
            GameId: game.Id,
            PlayerToken: game.WhiteSlotToken!.Value,
            Color: PlayerColor.White,
            JoinUrl: null,
            OpponentType: game.OpponentType,
            GameState: GameStateResponse.FromGame(game, PlayerColor.White));

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
