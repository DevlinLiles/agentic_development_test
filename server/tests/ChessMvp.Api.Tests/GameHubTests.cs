using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChessMvp.Api.Contracts;
using ChessMvp.Api.Hubs;
using ChessMvp.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessMvp.Api.Tests;

[Collection(ChessApiCollection.Name)]
public class GameHubTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ChessApiFactory _factory;
    private readonly HttpClient _client;

    public GameHubTests(ChessApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HubConnection BuildConnection()
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(new Uri(_client.BaseAddress!, "/hubs/game"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            });

        builder.Services.Configure<JsonHubProtocolOptions>(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return builder.Build();
    }

    [Fact]
    public async Task BothPlayers_ReceiveGameStateUpdated_WhenAMoveIsSubmitted()
    {
        var createResponse = await _client.PostAsync("/api/games", content: null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(created);
        var gameId = created!.GameId;
        var whiteToken = created.PlayerToken;

        var joinResponse = await _client.PostAsync($"/api/games/{gameId}/join", content: null);
        joinResponse.EnsureSuccessStatusCode();
        var joined = await joinResponse.Content.ReadFromJsonAsync<JoinGameResponse>(JsonOptions);
        Assert.NotNull(joined);
        var blackToken = joined!.PlayerToken;

        var whiteConnection = BuildConnection();
        var blackConnection = BuildConnection();

        var whiteUpdateReceived = new TaskCompletionSource<GameStateResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blackUpdateReceived = new TaskCompletionSource<GameStateResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        whiteConnection.On<GameStateResponse>(
            SignalRGameNotifier.GameStateUpdatedEvent,
            state => whiteUpdateReceived.TrySetResult(state));
        blackConnection.On<GameStateResponse>(
            SignalRGameNotifier.GameStateUpdatedEvent,
            state => blackUpdateReceived.TrySetResult(state));

        await whiteConnection.StartAsync();
        await blackConnection.StartAsync();

        await whiteConnection.InvokeAsync("JoinGameChannel", gameId, whiteToken);
        await blackConnection.InvokeAsync("JoinGameChannel", gameId, blackToken);

        var moveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/moves")
        {
            Content = JsonContent.Create(new MoveRequest("e2", "e4", null), options: JsonOptions),
        };
        moveRequest.Headers.Add("X-Player-Token", whiteToken.ToString());
        var moveResponse = await _client.SendAsync(moveRequest);
        moveResponse.EnsureSuccessStatusCode();

        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        var whiteCompleted = await Task.WhenAny(whiteUpdateReceived.Task, timeout);
        var blackCompleted = await Task.WhenAny(blackUpdateReceived.Task, timeout);

        Assert.Same(whiteUpdateReceived.Task, whiteCompleted);
        Assert.Same(blackUpdateReceived.Task, blackCompleted);

        var whiteState = await whiteUpdateReceived.Task;
        var blackState = await blackUpdateReceived.Task;

        Assert.Equal(gameId, whiteState.GameId);
        Assert.Equal(gameId, blackState.GameId);
        Assert.Equal(1, whiteState.MoveCount);
        Assert.Equal(1, blackState.MoveCount);

        // The hub's JSON protocol must serialize enums as strings, matching the REST controllers —
        // otherwise browser clients (which have no C# enum typing) see raw numbers instead of the
        // "Active"/"White"/etc. string values their TypeScript types expect.
        Assert.Equal(GameStatus.Active, whiteState.Status);
        Assert.Equal(PlayerColor.Black, whiteState.Turn);

        await whiteConnection.DisposeAsync();
        await blackConnection.DisposeAsync();
    }

    [Fact]
    public async Task BothPlayers_ReceiveGameStateUpdated_WhenAPlayerResigns()
    {
        var createResponse = await _client.PostAsync("/api/games", content: null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(created);
        var gameId = created!.GameId;
        var whiteToken = created.PlayerToken;

        var joinResponse = await _client.PostAsync($"/api/games/{gameId}/join", content: null);
        joinResponse.EnsureSuccessStatusCode();
        var joined = await joinResponse.Content.ReadFromJsonAsync<JoinGameResponse>(JsonOptions);
        Assert.NotNull(joined);
        var blackToken = joined!.PlayerToken;

        var whiteConnection = BuildConnection();
        var blackConnection = BuildConnection();

        // The opponent (Black) is the party that must be notified when White resigns.
        var blackResignReceived = new TaskCompletionSource<GameStateResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var whiteResignReceived = new TaskCompletionSource<GameStateResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        whiteConnection.On<GameStateResponse>(
            SignalRGameNotifier.GameStateUpdatedEvent,
            state => whiteResignReceived.TrySetResult(state));
        blackConnection.On<GameStateResponse>(
            SignalRGameNotifier.GameStateUpdatedEvent,
            state => blackResignReceived.TrySetResult(state));

        await whiteConnection.StartAsync();
        await blackConnection.StartAsync();

        await whiteConnection.InvokeAsync("JoinGameChannel", gameId, whiteToken);
        await blackConnection.InvokeAsync("JoinGameChannel", gameId, blackToken);

        var resignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{gameId}/resign");
        resignRequest.Headers.Add("X-Player-Token", whiteToken.ToString());
        var resignResponse = await _client.SendAsync(resignRequest);
        resignResponse.EnsureSuccessStatusCode();

        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        var whiteCompleted = await Task.WhenAny(whiteResignReceived.Task, timeout);
        var blackCompleted = await Task.WhenAny(blackResignReceived.Task, timeout);

        Assert.Same(whiteResignReceived.Task, whiteCompleted);
        Assert.Same(blackResignReceived.Task, blackCompleted);

        var whiteState = await whiteResignReceived.Task;
        var blackState = await blackResignReceived.Task;

        // Both players must see the resignation terminal state via the hub push.
        Assert.Equal(gameId, whiteState.GameId);
        Assert.Equal(gameId, blackState.GameId);
        Assert.Equal(GameStatus.Ended, whiteState.Status);
        Assert.Equal(GameStatus.Ended, blackState.Status);
        Assert.Equal(GameResult.BlackWins, whiteState.Result);
        Assert.Equal(GameResult.BlackWins, blackState.Result);
        Assert.Equal(GameResultReason.Resignation, whiteState.ResultReason);
        Assert.Equal(GameResultReason.Resignation, blackState.ResultReason);

        await whiteConnection.DisposeAsync();
        await blackConnection.DisposeAsync();
    }

    [Fact]
    public async Task JoinGameChannel_WithTokenMatchingNeitherSeat_ThrowsHubException()
    {
        var createResponse = await _client.PostAsync("/api/games", content: null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateGameResponse>(JsonOptions);
        Assert.NotNull(created);

        var connection = BuildConnection();
        await connection.StartAsync();

        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinGameChannel", created!.GameId, Guid.NewGuid()));

        await connection.DisposeAsync();
    }
}
