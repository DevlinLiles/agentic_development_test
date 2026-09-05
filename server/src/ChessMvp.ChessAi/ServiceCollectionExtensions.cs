using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ChessMvp.ChessAi;

/// <summary>
/// Dependency-injection extension methods that wire the chess AI layer into the service
/// container. Call <see cref="AddChessAi"/> once during startup (after the rules engine has
/// been registered, e.g. via <c>AddChessInfrastructure</c>) so that <see cref="IChessAiPlayer"/>
/// can be resolved transparently by the rest of the application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default heuristic chess AI player as a singleton for
    /// <see cref="IChessAiPlayer"/>, together with its <see cref="IHeuristicEvaluator"/>
    /// dependency, so the container can construct and resolve the AI player without any
    /// additional per-consumer wiring. Also registers a <see cref="ChessAiResponder"/> adapter
    /// for the domain-layer <see cref="IGameAiResponder"/> seam so <c>GameService</c> can
    /// orchestrate automated replies without depending on this project.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The supplied <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="HeuristicChessAiPlayer"/> is registered as a singleton: the player is
    /// deterministic and stateless with respect to a single position (see the contract remarks
    /// on <see cref="IChessAiPlayer"/>), and its only mutable field is a last-candidates cache
    /// intended for analysis tooling rather than gameplay, so a single shared instance is safe
    /// and correct across the whole application. The container therefore returns the same
    /// instance on every subsequent resolution of <see cref="IChessAiPlayer"/>.
    /// </para>
    /// <para>
    /// The player (and its <see cref="HeuristicEvaluator"/> dependency) both rely on
    /// <see cref="IChessRulesEngine"/>, which must be registered separately — typically by
    /// <c>AddChessInfrastructure</c> — since the rules engine is an infrastructure concern that
    /// does not belong to the AI layer. Both dependencies are resolved from the container at
    /// construction time, so as long as <see cref="IChessRulesEngine"/> is registered before the
    /// provider is built, <see cref="IChessAiPlayer"/> resolves cleanly.
    /// </para>
    /// <para>
    /// <see cref="ChessAiResponder"/> is also a singleton: it only forwards to the singleton
    /// AI player and rules engine and holds no mutable state of its own.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddChessAi(this IServiceCollection services)
    {
        // The evaluator is a stateless, side-relative function of a position; a single shared
        // instance is correct and avoids re-initialising the piece-square tables per consumer.
        services.AddSingleton<IHeuristicEvaluator, HeuristicEvaluator>();

        // The AI player is deterministic and shares only an analysis-only candidate cache, so a
        // singleton is appropriate and guarantees identical instances across resolutions.
        services.AddSingleton<IChessAiPlayer, HeuristicChessAiPlayer>();

        // The domain-layer orchestration seam. Adapts the singleton AI player to domain-only
        // types so GameService can request automated replies without a project reference here.
        services.AddSingleton<IGameAiResponder, ChessAiResponder>();

        return services;
    }
}
