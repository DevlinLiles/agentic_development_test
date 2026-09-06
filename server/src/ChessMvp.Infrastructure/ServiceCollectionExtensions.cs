using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Ai;
using ChessMvp.Infrastructure.ChessRulesEngine;
using ChessMvp.Infrastructure.Persistence;
using ChessMvp.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ChessDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("ChessMvpDb")));

        services.AddScoped<IGameRepository, GameRepository>();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();

        services.AddChessAiPlayer();

        return services;
    }

    /// <summary>
    /// Registers the heuristic AI player and its supporting services as singletons. The
    /// <see cref="IHeuristicEvaluator"/> is resolved to <see cref="MaterialHeuristicEvaluator"/> and
    /// <see cref="IChessAiPlayer"/> to <see cref="HeuristicChessAiPlayer"/>. Both the player and the
    /// evaluator are pure and stateless, so a single shared instance is safe and avoids re-running
    /// the (trivial) construction per request. Registration is idempotent: calling it more than
    /// once on the same collection never produces duplicate service descriptors.
    /// </summary>
    public static IServiceCollection AddChessAiPlayer(this IServiceCollection services)
    {
        // The player depends on IChessRulesEngine and IHeuristicEvaluator, both already registered
        // by AddChessInfrastructure (or independently). We only own the AI-specific descriptors
        // here, so guard each one against duplicate registration.
        if (!services.Any(s => s.ServiceType == typeof(IHeuristicEvaluator)))
        {
            services.AddSingleton<IHeuristicEvaluator, MaterialHeuristicEvaluator>();
        }

        if (!services.Any(s => s.ServiceType == typeof(IChessAiPlayer)))
        {
            services.AddSingleton<IChessAiPlayer, HeuristicChessAiPlayer>();
        }

        return services;
    }
}
