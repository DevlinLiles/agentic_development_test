using System.Text.Json.Serialization;
using ChessMvp.Api.Hubs;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure;
using ChessMvp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "ClientOrigin";

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Liveness endpoint for the containerized deployment's HEALTHCHECK (see
// docker-compose.yml). No database checks are registered on purpose: it must only
// prove that the HTTP server itself is up and accepting requests.
builder.Services.AddHealthChecks();

builder.Services.AddChessInfrastructure(builder.Configuration);
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

    // A containerized cold start races SQL Server's real readiness: the database
    // container's own HEALTHCHECK can already pass while the engine is still finishing
    // its internal system-database setup, and a single failed migration would kill the
    // app's first boot. Retry with backoff when configured (docker-compose.yml sets
    // Database__MigrateRetryAttempts=60); local and test hosts keep the default
    // single, fail-fast attempt.
    var migrateRetryAttempts = builder.Configuration.GetValue("Database:MigrateRetryAttempts", 1);
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < migrateRetryAttempts)
        {
            app.Logger.LogWarning(
                ex,
                "Database migration attempt {Attempt}/{Total} failed; retrying in 5s.",
                attempt,
                migrateRetryAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

// This is a local-only MVP with no HTTPS/production requirement yet (see product spec):
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
app.MapHealthChecks("/health");

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
