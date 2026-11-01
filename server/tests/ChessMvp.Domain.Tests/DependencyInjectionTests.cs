using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using ChessMvp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Composition-root pins for <see cref="ServiceCollectionExtensions.AddChessInfrastructure"/>:
/// once registered, the container must resolve the chess-domain service graph — in particular
/// <see cref="IChessAi"/> and <see cref="GameService"/> from one and the same scope — without
/// lifetime or activation errors.
/// </summary>
/// <remarks>
/// Uses a bare <see cref="ServiceCollection"/> rather than WebApplicationFactory on purpose:
/// these tests assert registration and activation only, so they need neither an HTTP host nor
/// a database. The DbContext is configured with a syntactically valid connection string and is
/// never opened — context construction alone is what resolution exercises here.
/// </remarks>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to: EF Core only needs the string to configure the context's
                // options; nothing resolved below executes a query against it. "ChessMvpDb" is
                // the key AddChessInfrastructure reads (see appsettings.Development.json).
                ["ConnectionStrings:ChessMvpDb"] =
                    "Server=localhost;Database=ChessMvp;Trusted_Connection=True;TrustServerCertificate=True;",
            })
            .Build();

        var services = new ServiceCollection();

        // The production composition, mirrored: AddChessInfrastructure (called from Program.cs
        // with the host configuration) plus the GameService registration Program.cs makes.
        // IGameNotifier is deliberately left out: GameService treats it as optional, and the
        // real SignalR notifier is an Api-layer registration that needs a hub context.
        services.AddChessInfrastructure(configuration);
        services.AddScoped<IGameService, GameService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Container_ResolvesIChessAi_AsHeuristicChessAi()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var ai = scope.ServiceProvider.GetRequiredService<IChessAi>();

        Assert.IsType<HeuristicChessAi>(ai);
    }

    [Fact]
    public void IChessAi_IsScoped_SharedWithinAScope_NewInstancePerScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<IChessAi>();
        var second = scope.ServiceProvider.GetRequiredService<IChessAi>();
        var fromOtherScope = otherScope.ServiceProvider.GetRequiredService<IChessAi>();

        // Scoped, like GameService: one instance per scope, so everything resolved within one
        // request shares the same AI, while separate requests get separate instances.
        Assert.Same(first, second);
        Assert.NotSame(first, fromOtherScope);
    }

    [Fact]
    public void Container_ResolvesGameService_FromTheSameScopeAsIChessAi()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // GameService resolves with its full constructor graph — the repository (and the
        // DbContext behind it) and the rules engine — and the AI seam resolves from that very
        // same scope, so wiring the seam into GameService needs no further registration: both
        // come from one scope with matching lifetimes.
        var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
        var ai = scope.ServiceProvider.GetRequiredService<IChessAi>();

        Assert.IsType<GameService>(gameService);
        Assert.IsType<HeuristicChessAi>(ai);
    }
}
