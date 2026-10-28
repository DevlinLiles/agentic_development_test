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

    /// <summary>
    /// Posts a create-game request whose body mirrors the client's <c>createGame()</c> wrapper:
    /// <c>null</c> sends no body at all (the legacy call shape), anything else sends it as JSON.
    /// </summary>
    private Task<HttpResponseMessage> PostCreateGameAsync(object? body) =>
        body is null
            ? _client.PostAsync("/api/games", content: null)
            : _client.PostAsJsonAsync("/api/games", body, JsonOptions);

    private async Task<CreateGameResponse> CreateGameWithBodyAsync(object? body)
    {
        var response = await PostCreateGameAsync(body);
        response.EnsureSuccessStatusCode();
        var parsed = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(parsed);
        return parsed!;
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

        // The opponent-mode discriminator must reach the client on the game state. It round-trips
        // the database (the AddOpponentModeToGame migration is applied by this factory's
        // Development-environment boot path), so this also proves the EF Core mapping persists it.
        Assert.Equal(OpponentMode.Human, body.GameState.OpponentMode);

        // The discriminator is also surfaced at the top level of the create response, so the
        // create flow never needs to reach into the nested state to learn which mode it got.
        // An absent body is the only call shape that existed before opponent selection, so it
        // must keep resolving to the classic two-player game.
        Assert.Equal(OpponentMode.Human, body.OpponentMode);
    }

    [Fact]
    public async Task CreateGame_WithEmptyJsonObjectBody_PreservesBodylessBehavior()
    {
        // A body that mentions no opponent field at all must behave exactly like no body: the
        // field is nullable rather than required, so "{}" deserializes with a null Opponent and
        // the controller's Human default applies.
        var response = await PostCreateGameAsync(new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(OpponentMode.Human, body!.OpponentMode);
        Assert.Equal(OpponentMode.Human, body.GameState.OpponentMode);
        Assert.Equal(GameStatus.WaitingForPlayer2, body.GameState.Status);
        Assert.Equal(PlayerColor.White, body.Color);
        Assert.Equal($"/game/{body.GameId}", body.JoinUrl);
    }

    [Fact]
    public async Task CreateGame_WithExplicitHumanBody_MatchesBodylessBehavior()
    {
        var defaulted = await CreateGameWithBodyAsync(null);
        var explicitHuman = await CreateGameWithBodyAsync(new CreateGameRequest { Opponent = OpponentMode.Human });

        // Everything the two responses can convey must agree: an explicit Human request and the
        // legacy body-less request produce the same game shape from the caller's point of view.
        Assert.Equal(OpponentMode.Human, explicitHuman.OpponentMode);
        Assert.Equal(defaulted.OpponentMode, explicitHuman.OpponentMode);
        Assert.Equal(defaulted.GameState.Status, explicitHuman.GameState.Status);
        Assert.Equal(GameStatus.WaitingForPlayer2, explicitHuman.GameState.Status);
        Assert.Equal(defaulted.GameState.OpponentMode, explicitHuman.GameState.OpponentMode);
        Assert.Equal(defaulted.Color, explicitHuman.Color);
        Assert.Equal(PlayerColor.White, explicitHuman.Color);
        Assert.Equal(defaulted.JoinUrl.Length, explicitHuman.JoinUrl.Length);

        // A waiting human game still hands out its shareable join link, and the second seat is
        // still fillable through it.
        var join = await _client.PostAsync($"/api/games/{explicitHuman.GameId}/join", content: null);
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
    }

    [Fact]
    public async Task CreateGame_WithComputerBody_CreatesActiveSoloGameWithComputerEcho()
    {
        var body = await CreateGameWithBodyAsync(new CreateGameRequest { Opponent = OpponentMode.Computer });

        // A computer game never waits for a second player: it is created already active, with
        // the caller on White and the server-side AI seated on Black.
        Assert.Equal(OpponentMode.Computer, body.OpponentMode);
        Assert.Equal(GameStatus.Active, body.GameState.Status);
        Assert.Equal(OpponentMode.Computer, body.GameState.OpponentMode);
        Assert.Equal(PlayerColor.White, body.Color);
        Assert.Equal(PlayerColor.White, body.GameState.Turn);
        Assert.NotEqual(Guid.Empty, body.PlayerToken);

        // A solo game must be playable immediately — no join required.
        var move = await SubmitMoveAsync(body.GameId, body.PlayerToken, "e2", "e4");
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
    }

    [Fact]
    public async Task CreateGame_ComputerMode_RespondsCreatedWithLocationHeader()
    {
        var response = await PostCreateGameAsync(new CreateGameRequest { Opponent = OpponentMode.Computer });

        // The new body must not disturb the original 201 + Location contract.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains(body!.GameId.ToString(), response.Headers.Location!.OriginalString);
        Assert.Equal(OpponentMode.Computer, body.OpponentMode);
    }

    [Fact]
    public async Task CreateGame_ComputerBody_SerializesOpponentModeAsPinnedStringFieldOnTheWire()
    {
        // The wire name is pinned to "opponentType" (not the C# property name "OpponentMode"),
        // and enums serialize as strings — matching the contract declared in
        // client/src/types/gameTypes.ts, the documented single source of truth for this API.
        var response = await PostCreateGameAsync(new CreateGameRequest { Opponent = OpponentMode.Computer });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Computer", json.GetProperty("opponentType").GetString());
        Assert.Equal("Computer", json.GetProperty("gameState").GetProperty("opponentType").GetString());
        Assert.False(json.TryGetProperty("opponentMode", out _));
    }

    [Fact]
    public async Task CreateGame_WithNullOpponentInBody_CreatesHumanGame()
    {
        // The client only ever sends an explicit value, but the field is nullable on the wire, so
        // an explicit null must resolve to the human default rather than failing the request.
        var body = await CreateGameWithBodyAsync(new CreateGameRequest { Opponent = null });

        Assert.Equal(OpponentMode.Human, body.OpponentMode);
        Assert.Equal(GameStatus.WaitingForPlayer2, body.GameState.Status);
    }

    [Fact]
    public async Task JoinGame_OnComputerGame_Returns409GameNotActive()
    {
        var created = await CreateGameWithBodyAsync(new CreateGameRequest { Opponent = OpponentMode.Computer });

        // A computer game's Black seat belongs to the server-side AI from the moment of
        // creation, so joining it maps to the same 409 GameNotActive answer a full or
        // no-longer-waiting human game gets — no new error vocabulary on the wire.
        var response = await _client.PostAsync($"/api/games/{created.GameId}/join", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        Assert.NotNull(error);
        Assert.Equal("GameNotActive", error!.Error);
        Assert.Contains("computer", error.Message, StringComparison.OrdinalIgnoreCase);
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
