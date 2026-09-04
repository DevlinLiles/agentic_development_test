using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChessMvp.Api.Contracts;
using ChessMvp.Domain.Entities;
using Xunit;

namespace ChessMvp.Api.Tests;

[Collection(ChessApiCollection.Name)]
public class GamesControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client;

    public GamesControllerTests(ChessApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(Guid GameId, Guid WhiteToken)> CreateGameAsync()
    {
        var response = await _client.PostAsync("/api/games", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        return (body!.GameId, body.PlayerToken);
    }

    private async Task<Guid> JoinGameAsync(Guid gameId)
    {
        var response = await _client.PostAsync($"/api/games/{gameId}/join", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JoinGameResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!.PlayerToken;
    }

    private Task<HttpResponseMessage> SubmitMoveAsync(Guid gameId, Guid token, string from, string to)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/moves")
        {
            Content = JsonContent.Create(new MoveRequest(from, to, null), options: JsonOptions),
        };
        request.Headers.Add("X-Player-Token", token.ToString());
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task CreateGame_ReturnsWaitingGameWithWhiteToken()
    {
        var response = await _client.PostAsync("/api/games", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(GameStatus.WaitingForPlayer2, body!.GameState.Status);
        Assert.NotEqual(Guid.Empty, body.PlayerToken);
    }

    [Fact]
    public async Task CreateGame_WithoutMode_DefaultsToTwoPlayerAndIncludesJoinUrl()
    {
        // Backward compatibility: existing clients send no mode parameter and must keep
        // receiving the original two-player behaviour with a shareable join URL.
        var response = await _client.PostAsync("/api/games", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(GameMode.TwoPlayer, body!.Mode);
        Assert.Equal(GameMode.TwoPlayer, body.GameState.Mode);
        Assert.NotNull(body.JoinUrl);
        Assert.Equal(GameStatus.WaitingForPlayer2, body.GameState.Status);
    }

    [Fact]
    public async Task CreateGame_WithVsAiMode_ReturnsActiveGameWithNullJoinUrl()
    {
        var response = await _client.PostAsync("/api/games?mode=VsAi", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(GameMode.VsAi, body!.Mode);
        Assert.Equal(GameMode.VsAi, body.GameState.Mode);
        // VsAi games have no second human to invite, so the join URL is omitted.
        Assert.Null(body.JoinUrl);
        // The AI is seated immediately, so the game starts active rather than waiting.
        Assert.Equal(GameStatus.Active, body.GameState.Status);
        Assert.NotEqual(Guid.Empty, body.PlayerToken);
    }

    [Fact]
    public async Task CreateGame_WithExplicitTwoPlayerMode_BehavesAsDefault()
    {
        var response = await _client.PostAsync("/api/games?mode=TwoPlayer", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(GameMode.TwoPlayer, body!.Mode);
        Assert.NotNull(body.JoinUrl);
        Assert.Equal(GameStatus.WaitingForPlayer2, body.GameState.Status);
    }

    [Fact]
    public async Task GetGame_SurfacesModeViaFromGame()
    {
        // Create a VsAi game, then fetch it separately and confirm the mode propagates
        // through GameStateResponse.FromGame on the read path too.
        var created = await _client.PostAsync("/api/games?mode=VsAi", content: null);
        var createdBody = await created.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(createdBody);

        var state = await _client.GetFromJsonAsync<GameStateResponse>(
            $"/api/games/{createdBody!.GameId}", JsonOptions);
        Assert.NotNull(state);
        Assert.Equal(GameMode.VsAi, state!.Mode);
    }

    [Fact]
    public async Task FullGameFlow_FoolsMate_EndsInCheckmateWithCorrectHistory()
    {
        var (gameId, whiteToken) = await CreateGameAsync();
        var blackToken = await JoinGameAsync(gameId);

        var move1 = await SubmitMoveAsync(gameId, whiteToken, "f2", "f3");
        Assert.Equal(HttpStatusCode.OK, move1.StatusCode);

        var move2 = await SubmitMoveAsync(gameId, blackToken, "e7", "e5");
        Assert.Equal(HttpStatusCode.OK, move2.StatusCode);

        var move3 = await SubmitMoveAsync(gameId, whiteToken, "g2", "g4");
        Assert.Equal(HttpStatusCode.OK, move3.StatusCode);

        var move4 = await SubmitMoveAsync(gameId, blackToken, "d8", "h4");
        Assert.Equal(HttpStatusCode.OK, move4.StatusCode);
        var move4Body = await move4.Content.ReadFromJsonAsync<GameStateResponse>(JsonOptions);
        Assert.NotNull(move4Body);
        Assert.Equal(GameStatus.Ended, move4Body!.Status);
        Assert.Equal(GameResult.BlackWins, move4Body.Result);
        Assert.Equal(GameResultReason.Checkmate, move4Body.ResultReason);
        Assert.True(move4Body.IsCheck);

        var finalState = await _client.GetFromJsonAsync<GameStateResponse>($"/api/games/{gameId}", JsonOptions);
        Assert.NotNull(finalState);
        Assert.Equal(GameStatus.Ended, finalState!.Status);
        Assert.Equal(GameResult.BlackWins, finalState.Result);
        Assert.Equal(GameResultReason.Checkmate, finalState.ResultReason);

        var history = await _client.GetFromJsonAsync<MoveHistoryResponse>($"/api/games/{gameId}/moves", JsonOptions);
        Assert.NotNull(history);
        Assert.Equal(4, history!.Moves.Count);
        Assert.Collection(
            history.Moves,
            m => Assert.Equal((1, PlayerColor.White, "f3"), (m.MoveNumber, m.Color, m.San)),
            m => Assert.Equal((2, PlayerColor.Black, "e5"), (m.MoveNumber, m.Color, m.San)),
            m => Assert.Equal((3, PlayerColor.White, "g4"), (m.MoveNumber, m.Color, m.San)),
            m => Assert.Equal((4, PlayerColor.Black, "Qh4#"), (m.MoveNumber, m.Color, m.San)));
        Assert.True(history.Moves[3].IsCheckmate);
    }

    [Fact]
    public async Task SubmitMove_WithTokenThatMatchesNeitherSeat_Returns401()
    {
        var (gameId, _) = await CreateGameAsync();
        await JoinGameAsync(gameId);

        var response = await SubmitMoveAsync(gameId, Guid.NewGuid(), "e2", "e4");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitMove_OutOfTurn_Returns403()
    {
        var (gameId, _) = await CreateGameAsync();
        var blackToken = await JoinGameAsync(gameId);

        // It is White's turn first; Black attempting to move should be rejected.
        var response = await SubmitMoveAsync(gameId, blackToken, "e7", "e5");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SubmitMove_BeforeSecondPlayerJoins_Returns409()
    {
        var (gameId, whiteToken) = await CreateGameAsync();

        var response = await SubmitMoveAsync(gameId, whiteToken, "e2", "e4");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitMove_IllegalMove_Returns400()
    {
        var (gameId, whiteToken) = await CreateGameAsync();
        await JoinGameAsync(gameId);

        var response = await SubmitMoveAsync(gameId, whiteToken, "e2", "e5");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task JoinGame_UnknownGame_Returns404()
    {
        var response = await _client.PostAsync($"/api/games/{Guid.NewGuid()}/join", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task JoinGame_AlreadyFull_Returns409()
    {
        var (gameId, _) = await CreateGameAsync();
        await JoinGameAsync(gameId);

        var response = await _client.PostAsync($"/api/games/{gameId}/join", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentFirstMoves_OnlyOneSucceeds_AndFinalStateIsConsistent()
    {
        var (gameId, whiteToken) = await CreateGameAsync();
        await JoinGameAsync(gameId);

        var moveA = SubmitMoveAsync(gameId, whiteToken, "e2", "e4");
        var moveB = SubmitMoveAsync(gameId, whiteToken, "d2", "d4");

        var results = await Task.WhenAll(moveA, moveB);
        var statusCodes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();

        // Exactly one request should complete the move; the other must fail (either because the
        // rowversion changed underneath it, or - if it happened to load-after-save - because it's
        // no longer White's turn).
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);

        var finalState = await _client.GetFromJsonAsync<GameStateResponse>($"/api/games/{gameId}", JsonOptions);
        Assert.NotNull(finalState);
        Assert.Equal(1, finalState!.MoveCount);
        Assert.Equal(PlayerColor.Black, finalState.Turn);

        var history = await _client.GetFromJsonAsync<MoveHistoryResponse>($"/api/games/{gameId}/moves", JsonOptions);
        Assert.NotNull(history);
        var onlyMove = Assert.Single(history!.Moves);
        Assert.Contains(onlyMove.San, new[] { "e4", "d4" });
    }
}
