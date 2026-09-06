using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.Infrastructure.Tests;

/// <summary>
/// Smoke tests for the dependency-injection wiring performed by
/// <see cref="ServiceCollectionExtensions.AddChessInfrastructure"/>. These confirm that the
/// heuristic AI player is registered as a singleton implementation of
/// <see cref="IChessAiPlayer"/> and that its evaluator dependency is registered and injected.
/// </summary>
public class ChessAiPlayerRegistrationTests
{
    private static IServiceProvider BuildProvider()
    {
        // AddChessInfrastructure registers a DbContext against the configured connection string.
        // DbContext registration is lazy — the SQL Server provider only connects when a context
        // instance is actually used — so a placeholder connection string is sufficient to exercise
        // the AI service wiring without a real database.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ChessMvpDb"] = "Server=localhost;Database=Dummy;Trusted_Connection=True;TrustServerCertificate=True",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddChessInfrastructure(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Resolves_IChessAiPlayer_AsHeuristicChessAiPlayer()
    {
        using var provider = BuildProvider();

        var player = provider.GetRequiredService<IChessAiPlayer>();

        Assert.IsType<HeuristicChessAiPlayer>(player);
    }

    [Fact]
    public void IChessAiPlayer_IsRegisteredAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IChessAiPlayer>();
        var second = provider.GetRequiredService<IChessAiPlayer>();

        Assert.Same(first, second);
    }

    [Fact]
    public void IChessBoardEvaluator_IsRegisteredAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IChessBoardEvaluator>();
        var second = provider.GetRequiredService<IChessBoardEvaluator>();

        Assert.Same(first, second);
    }

    [Fact]
    public void ResolvedPlayer_HasEvaluatorInjected()
    {
        using var provider = BuildProvider();

        // The evaluator is a singleton, so the instance injected into the player must be the same
        // one the container resolves directly — proving the dependency is wired and injected.
        var player = provider.GetRequiredService<IChessAiPlayer>();
        var evaluator = provider.GetRequiredService<IChessBoardEvaluator>();

        var heuristicPlayer = Assert.IsType<HeuristicChessAiPlayer>(player);
        Assert.Same(evaluator, heuristicPlayer.Evaluator);
    }
}
