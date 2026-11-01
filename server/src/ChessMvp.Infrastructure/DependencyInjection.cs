using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using ChessMvp.Infrastructure.Persistence;
using ChessMvp.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Both AI seams live in the Domain layer (pure, no persistence/HTTP concerns — NFR-6)
        // but are wired here, beside the rules-engine adapter they consume, so all chess-domain
        // services are registered by one composition call. Both are registered as singletons
        // with the singleton rules engine: each is stateless and side-effect free by contract,
        // and the AI reply is computed synchronously inside SubmitMoveAsync (ADR-0084).
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();

        // The strength-agnostic seam consumed by GameService's computer-mode reply step: it
        // exposes only a chosen move, so consumers never see the heuristics and a future
        // minimax implementation can replace this registration alone.
        services.AddSingleton<IChessAi, HeuristicChessAi>();

        var connectionString = configuration.GetConnectionString("ChessMvpDb");
        services.AddDbContext<ChessDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IGameRepository, GameRepository>();

        return services;
    }
}
