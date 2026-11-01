using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
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

        // The pre-existing AI seam, still consumed by GameService's computer-mode reply step.
        // Stateless and side-effect free by contract, and its only dependency is the singleton
        // rules engine above, so a singleton lifetime is safe for it.
        services.AddSingleton<IHeuristicChessEngine, HeuristicChessEngine>();

        // The strength-agnostic AI seam: it exposes only a chosen move, so consumers never see
        // the heuristics and a future minimax implementation can replace this registration
        // alone, never the contract.
        //
        // Lifetime — scoped, matching GameService (the consumer, registered scoped in
        // Program.cs), per ADR-0084's rule that a dependency's lifetime must not outlive its
        // consumer's. HeuristicChessAi is itself stateless and depends only on the singleton
        // rules engine above, so a singleton would resolve fine today; scoped is chosen
        // deliberately so a future stateful implementation (per-request caches, a per-game
        // difficulty setting) can be swapped in behind this same registration without
        // re-litigating lifetimes, and so an accidental capture by a longer-lived service
        // surfaces as a scope-validation error instead of silently wrong behaviour.
        services.AddScoped<IChessAi, HeuristicChessAi>();

        return services;
    }
}
