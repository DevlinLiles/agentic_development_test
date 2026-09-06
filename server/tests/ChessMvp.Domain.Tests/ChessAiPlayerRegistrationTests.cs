using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Ai;
using ChessMvp.Infrastructure;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChessMvp.Domain.Tests;

public class ChessAiPlayerRegistrationTests
{
    [Fact]
    public void AddChessAiPlayer_RegistersHeuristicEvaluatorAsSingleton()
    {
        var services = new ServiceCollection();

        // The AI player depends on IChessRulesEngine; register a stand-in so the built provider can
        // actually construct the singleton graph without a real rules engine.
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAiPlayer();

        using var provider = services.BuildServiceProvider();

        var evaluator = provider.GetRequiredService<IHeuristicEvaluator>();
        Assert.NotNull(evaluator);
        Assert.IsType<MaterialHeuristicEvaluator>(evaluator);

        // Singleton: a second resolution returns the exact same instance.
        Assert.Same(evaluator, provider.GetRequiredService<IHeuristicEvaluator>());
    }

    [Fact]
    public void AddChessAiPlayer_RegistersHeuristicChessAiPlayerAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAiPlayer();

        using var provider = services.BuildServiceProvider();

        var player = provider.GetRequiredService<IChessAiPlayer>();
        Assert.NotNull(player);
        Assert.IsType<HeuristicChessAiPlayer>(player);
    }

    [Fact]
    public void ServiceProvider_ResolvesNonNullHeuristicChessAiPlayerInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();
        services.AddChessAiPlayer();

        using var provider = services.BuildServiceProvider();

        var player = provider.GetRequiredService<IChessAiPlayer>();
        Assert.NotNull(player);
        // The exact type is HeuristicChessAiPlayer (not just any IChessAiPlayer).
        var concrete = Assert.IsType<HeuristicChessAiPlayer>(player);
        Assert.NotNull(concrete);

        // Singleton lifetime: repeated resolution yields the same instance.
        Assert.Same(player, provider.GetRequiredService<IChessAiPlayer>());
    }

    [Fact]
    public void AddChessAiPlayer_IsIdempotent_DoesNotDuplicateServicesWhenCalledTwice()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChessRulesEngine, GerasimleoChessRulesEngineAdapter>();

        services.AddChessAiPlayer();
        var countAfterFirst = services.Count;

        services.AddChessAiPlayer();
        var countAfterSecond = services.Count;

        // No new descriptors added on the second call.
        Assert.Equal(countAfterFirst, countAfterSecond);

        // Exactly one descriptor per AI service after repeated registration.
        Assert.Single(services, s => s.ServiceType == typeof(IHeuristicEvaluator));
        Assert.Single(services, s => s.ServiceType == typeof(IChessAiPlayer));

        // And the built provider still resolves a single, consistent instance.
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IChessAiPlayer>());
    }
}
