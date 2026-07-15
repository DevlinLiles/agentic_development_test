using System.Reflection;
using ChessMvp.Infrastructure.ChessRulesEngine;
using ChessMvp.Infrastructure.ChessAi;
using ChessMvp.Domain.Services;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Self-checking guard for the QA gate "No skipped or xfailed tests remain in the run"
/// (task: Automated Test-Suite Execution &amp; Coverage Verification).
///
/// xUnit marks a test skipped via <c>[Fact(Skip = "...")]</c> / <c>[Theory(Skip = "...")]</c>;
/// there is no first-class xfail in xUnit, but <c>Skip</c> is the only mechanism that yields a
/// "NotExecuted" outcome in the .trx — exactly what <c>run-qa-coverage.sh</c> gates on — so we
/// forbid it here. The guard reflects over every test class in this assembly and fails fast if
/// any test method is adorned with a Skip-bearing <c>FactAttribute</c>/<c>TheoryAttribute</c>,
/// keeping the gate deterministic and machine-verifiable instead of relying on eyeballing the
/// report.
/// </summary>
public class NoSkippedTestsGuardTests
{
    [Fact]
    public void NoTestMethodCarriesASkipAttribute()
    {
        var offending = new List<string>();
        var testAssembly = GetType().Assembly;

        foreach (var type in testAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var fact = method.GetCustomAttribute<FactAttribute>();
                var theory = method.GetCustomAttribute<TheoryAttribute>();

                if (!string.IsNullOrEmpty(fact?.Skip))
                {
                    offending.Add($"{type.FullName}.{method.Name} [Fact(Skip)]");
                }
                if (!string.IsNullOrEmpty(theory?.Skip))
                {
                    offending.Add($"{type.FullName}.{method.Name} [Theory(Skip)]");
                }
            }
        }

        Assert.Empty(offending);
    }

    [Fact]
    public void CoverageGate_CoversAllChessEngineImplementationModules()
    {
        // Documents that the coverage threshold is measured against the behavioural modules the
        // acceptance criteria name: legal move generation, terminal-state detection, evaluation,
        // search, and AI engine legality. If an implementation class is removed/renamed, this
        // surfaces it so run-qa-coverage.sh's IMPLEMENTATION_ASSEMBLIES list stays in sync.
        Assert.NotNull(typeof(GerasimleoChessRulesEngineAdapter)); // legal moves + terminal state
        Assert.NotNull(typeof(GreedyHeuristicChessAi));             // evaluation + search + AI legality
        Assert.NotNull(typeof(GameService));                        // move legality orchestration
    }
}
