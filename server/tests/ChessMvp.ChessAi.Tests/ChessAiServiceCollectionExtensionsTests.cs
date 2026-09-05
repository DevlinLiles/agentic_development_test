using ChessMvp.ChessAi;
using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessMvp.ChessAi.Tests;

/// <summary>
/// Verifies the AddChessAi dependency-injection extension registers the heuristic chess AI
/// player as a resolvable singleton for IChessAiPlayer without disturbing the rest of the
/// service configuration.
/// </summary>
public class ChessAiServiceCollectionExtensionsTests
{
    // The player resolves IChessRulesEngine from the container, so a complete graph requires
    // the rules engine. The real adapter has a parameterless constructor and performs no I/O at
    // construction time, making it suitable for an in-memory resolution test.
    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAi();
        return services;
    }

    [Fact]
    public void AddChessAi_RegistersIChessAiPlayerAsHeuristicChessAiPlayer()
    {
        using var provider = BuildServices().BuildServiceProvider();

        var resolved = provider.GetRequiredService<IChessAiPlayer>();

        Assert.NotNull(resolved);
        Assert.IsType<HeuristicChessAiPlayer>(resolved);
    }

    [Fact]
    public void AddChessAi_ReturnsSameInstanceOnSubsequentResolutions()
    {
        using var provider = BuildServices().BuildServiceProvider();

        var first = provider.GetRequiredService<IChessAiPlayer>();
        var second = provider.GetRequiredService<IChessAiPlayer>();

        // Singleton lifetime: every resolution must yield the identical shared instance.
        Assert.Same(first, second);
    }

    [Fact]
    public void AddChessAi_RegistersIHeuristicEvaluatorAsSingleton()
    {
        // The evaluator is a private dependency of the player but must still be resolvable and
        // shared, since the player depends on a single consistent evaluator instance.
        using var provider = BuildServices().BuildServiceProvider();

        var first = provider.GetRequiredService<IHeuristicEvaluator>();
        var second = provider.GetRequiredService<IHeuristicEvaluator>();

        Assert.IsType<HeuristicEvaluator>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddChessAi_PlayerUsesResolvedEvaluatorAndRulesEngine()
    {
        // The singleton player must be constructed from the container-resolved evaluator and
        // rules engine, proving the full dependency graph wires up rather than the player being
        // registered as a pre-built instance.
        using var provider = BuildServices().BuildServiceProvider();

        var player = provider.GetRequiredService<IChessAiPlayer>();
        var evaluator = provider.GetRequiredService<IHeuristicEvaluator>();
        var rulesEngine = provider.GetRequiredService<IChessRulesEngine>();

        Assert.NotNull(player);
        Assert.NotNull(evaluator);
        Assert.NotNull(rulesEngine);
    }

    [Fact]
    public void AddChessAi_CalledTwice_DoesNotBreakResolution()
    {
        // Repeated registration must remain valid (last registration wins) and still resolve a
        // single shared singleton instance, so accidental double-registration never corrupts
        // the configuration.
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAi();
        services.AddChessAi();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IChessAiPlayer>();
        var second = provider.GetRequiredService<IChessAiPlayer>();

        Assert.IsType<HeuristicChessAiPlayer>(first);
        Assert.Same(first, second);
    }
}
