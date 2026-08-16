using ChessMvp.Domain.Abstractions;
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

        return services;
    }
}
