using System.Diagnostics;
using ChessMvp.Infrastructure.ChessAi;

// AI response-time benchmark.
//
// Deterministic timing harness: runs a sample of representative positions through the depth-3
// search engine and asserts each move's wall-clock time is within the SLA (2000 ms) on the
// reference machine. Prints per-position timings and an aggregate pass/fail verdict, and exits
// non-zero if any position breaches the threshold. No LLM or human judgment is involved — only
// a Stopwatch and a numeric threshold.

const int ThresholdMs = 2000;
const int SearchDepth = 3;

// A varied sample: the opening start position, two opening tabiyas, a dense middlegame
// ("kiwipete", ~48 legal moves — the timing stress case), a bare endgame, and a forced mate.
var positions = new (string Name, string Fen)[]
{
    ("start", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
    ("ruy_lopez_3Bb5", "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"),
    ("italian_3_Bc5", "r1bqkbnr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"),
    ("kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"),
    ("endgame_KvK", "8/8/8/5k2/8/8/4K3/8 w - - 0 1"),
    ("back_rank_mate", "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1"),
};

var ai = new SearchChessAi(SearchDepth);

// One discarded warm-up solve so JIT compilation is not charged against the first measured
// position — the SLA is a steady-state response-time bound, not a cold-start bound.
_ = ai.ChooseMove(positions[0].Fen);

Console.WriteLine($"AI response-time benchmark — depth {SearchDepth}, threshold {ThresholdMs} ms/position");
Console.WriteLine(new string('-', 64));
Console.WriteLine($"{("position",-22)}{("time (ms)",12)}{("move",10)}{("result",10)}");
Console.WriteLine(new string('-', 64));

var allPass = true;
foreach (var (name, fen) in positions)
{
    var sw = Stopwatch.StartNew();
    var move = ai.ChooseMove(fen);
    sw.Stop();
    var elapsedMs = sw.Elapsed.TotalMilliseconds;

    var solved = move is not null;
    var withinSla = elapsedMs <= ThresholdMs;
    var pass = solved && withinSla;

    var moveStr = move is null ? "--" : $"{move.FromSquare}{move.ToSquare}";
    Console.WriteLine(
        $"{name,-22}{elapsedMs,12:F1}{moveStr,10}{(pass ? "PASS" : "FAIL"),10}");

    if (!pass)
        allPass = false;
}

Console.WriteLine(new string('-', 64));
Console.WriteLine(
    allPass
        ? "AGGREGATE: PASS — all positions solved within the SLA"
        : "AGGREGATE: FAIL — one or more positions breached the SLA");

return allPass ? 0 : 1;
