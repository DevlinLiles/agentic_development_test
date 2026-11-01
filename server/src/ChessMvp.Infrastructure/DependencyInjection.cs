using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The heuristic engine lives in the Domain layer (pure, no persistence/HTTP concerns —
        // NFR-6) but is wired here, beside the rules-engine adapter it consumes, so all
        // chess-domain services are registered by one composition call. Registered as a
        // singleton with the singleton rules engine: it is stateless and side-effect free by
        // contract, and the AI reply is computed synchronously inside SubmitMoveAsync.
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddSingleton<IHeuristicChessEngine, HeuristicChessEngine>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<ChessDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IGameRepository, GameRepository>();

        return services;
    }
}
