using System.Text.Json.Serialization;
using ChessMvp.Api.Hubs;
using ChessMvp.ChessAi;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure;
using ChessMvp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "ClientOrigin";

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddChessInfrastructure(builder.Configuration);
// Wires the heuristic chess AI player in as the singleton IChessAiPlayer implementation so the
// rest of the application can resolve the AI player transparently. Depends on IChessRulesEngine,
// which is registered above by AddChessInfrastructure. Also registers the IGameAiResponder adapter
// that GameService consumes to orchestrate automated replies in VsAi games.
builder.Services.AddChessAi();
// GameService takes IGameAiResponder (optional) so it can generate an inline AI reply after a
// human move in VsAi games; resolving it from the container here keeps the Domain layer free of a
// dependency on the ChessMvp.ChessAi project.
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddSingleton<IGameNotifier, SignalRGameNotifier>();

// Without this, SignalR's JSON hub protocol serializes enums as their underlying int, while the
// REST controllers (configured above) serialize them as strings — the two transports would
// disagree on wire format for the same GameStateResponse payload.
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// SignalR requires a specific allowed origin (not AllowAnyOrigin) whenever AllowCredentials is
// used, so the client origin is read from config rather than wildcarded.
var clientOrigin = builder.Configuration["Cors:ClientOrigin"];
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        if (!string.IsNullOrWhiteSpace(clientOrigin))
        {
            policy.WithOrigins(clientOrigin)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ChessDbContext>();
    db.Database.Migrate();
}

// This is a local-only MVP with no https/production requirement yet (see product spec):
// redirecting to HTTPS in Development just forces every client onto the untrusted local dev
// cert for no benefit, breaking plain-HTTP usage from both real browsers and test tooling
// unless `dotnet dev-certs https --trust` has been run. Skip it outside Development.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(ClientCorsPolicy);

app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
