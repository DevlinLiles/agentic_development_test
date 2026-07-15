using System.Diagnostics;
using ChessMvp.Infrastructure.ChessAi;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// AI response-time benchmark, exposed as an xUnit test so the deterministic procedure also
/// runs as part of the normal <c>dotnet test</c> suite. It runs a sample of positions through
/// the depth-3 search engine and asserts each move's wall-clock time is within the SLA
/// (2000 ms) on the reference machine. No LLM or human judgment is involved — only a
/// <see cref="Stopwatch"/> and a numeric threshold.
///
/// A console harness with the same logic (and a non-zero exit code on failure) lives in
/// <c>ChessMvp.Bench/Program.cs</c>.
/// </summary>
public class SearchChessAiResponseTimeBenchmarkTests
{
    /// <summary>
    /// Per-move wall-clock SLA in milliseconds, on the reference machine.
    /// </summary>
    private const int ThresholdMs = 2000;

    private const int SearchDepth = 3;

    private static readonly (string Name, string Fen)[] Positions =
    {
        ("start", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
        ("ruy_lopez_3Bb5", "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"),
        ("italian_3_Bc5", "r1bqkbnr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"),
        ("kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"),
        ("endgame_KvK", "8/8/8/5k2/8/8/4K3/8 w - - 0 1"),
        ("back_rank_mate", "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1"),
    };

    [Fact]
    public void EverySamplePosition_IsSolvedWithinTwoSeconds_AtDepthThree()
    {
        var ai = new SearchChessAi(SearchDepth);

        // One discarded warm-up solve so JIT compilation is not charged against the first
        // measured position — the SLA is a steady-state response-time bound, not cold-start.
        _ = ai.ChooseMove(Positions[0].Fen);

        var results = new List<(string Name, double Ms, string Move, bool Pass)>(Positions.Length);
        foreach (var (name, fen) in Positions)
        {
            var sw = Stopwatch.StartNew();
            var move = ai.ChooseMove(fen);
            sw.Stop();

            var ms = sw.Elapsed.TotalMilliseconds;
            var moveStr = move is null ? "--" : $"{move.FromSquare}{move.ToSquare}";
            var pass = move is not null && ms <= ThresholdMs;
            results.Add((name, ms, moveStr, pass));
        }

        // Report per-position timings and an aggregate verdict for test-runner diagnostics.
        var report = string.Join(
            Environment.NewLine,
            results.Select(r => $"{r.Name,-22}{r.Ms,10:F1} ms  {r.Move,-6} {(r.Pass ? "PASS" : "FAIL")}"));

        Assert.True(
            results.All(r => r.Pass),
            $"AI response-time benchmark (depth {SearchDepth}, SLA {ThresholdMs} ms) FAILED for one or more positions.{Environment.NewLine}{report}");
    }
}
