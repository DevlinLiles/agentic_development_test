using ChessMvp.Domain.Abstractions;
using ChessMvp.Domain.Entities;
using ChessMvp.Infrastructure.ChessAi;
using ChessMvp.Infrastructure.ChessEvaluation;
using ChessMvp.Infrastructure.ChessRulesEngine;
using Xunit;

namespace ChessMvp.Domain.Tests;

/// <summary>
/// Deterministic tie-breaking tests for <see cref="HeuristicChessAiPlayer"/>. These cover the
/// specific acceptance criterion that a position with several equally-scored moves resolves to a
/// single, stable move via the documented lexicographic tie-break, and that the resolution is
/// repeatable across invocations.
/// </summary>
public class HeuristicChessAiPlayerTieBreakTests
{
    // Bare kings with White to move. The White king on e3 can step to d2, e2, f2, d3, f3, d4, e4,
    // or f4. In the evaluator's king piece-square table the d2, e2, and f2 squares all carry a
    // value of 0 — the maximum among the king's available destinations (d3/f3 score -20 and the
    // rank-4 destinations score -40) — and each resulting position leaves the lone White king on
    // an interior second-rank square with exactly eight legal moves while the Black king on h8 is
    // untouched, so material, piece-square, and mobility are identical across the three
    // candidates. Ke3-d2, Ke3-e2, and Ke3-f2 therefore score *exactly* the same and are the
    // highest-scoring moves — a genuine top-of-list tie that the player must break deterministically.
    private const string TieFen = "7k/8/8/8/8/4K3/8/8 w - - 0 1";

    private static HeuristicChessAiPlayer CreateSut() =>
        new(new GerasimleoChessRulesEngineAdapter(), new HeuristicBoardEvaluator());

    [Fact]
    public async Task ChooseMoveAsync_TiePosition_ResolvesDeterministicallyAndRepeatedly()
    {
        var sut = CreateSut();
        var rulesEngine = new GerasimleoChessRulesEngineAdapter();
        var evaluator = new HeuristicBoardEvaluator();

        // Independently reproduce the player's 1-ply scoring to identify the top score group and
        // confirm a real tie exists at the top of the list (>= 2 equally-best candidates).
        var scored = new List<(string From, string To, int Score, string Key)>();
        foreach (var move in rulesEngine.GetAllLegalMoves(TieFen))
        {
            var applied = rulesEngine.TryApplyMove(
                TieFen, PlayerColor.White, move.FromSquare, move.ToSquare, promotion: null);
            if (!applied.IsLegal || applied.ResultingFen is null)
            {
                continue;
            }

            var score = evaluator.Score(applied.ResultingFen, PlayerColor.White);
            scored.Add((move.FromSquare, move.ToSquare, score, $"{move.FromSquare}{move.ToSquare}"));
        }

        Assert.NotEmpty(scored);
        var topScore = scored.Max(s => s.Score);
        var topGroup = scored.Where(s => s.Score == topScore).ToList();

        // The position must actually present a tie at the top — otherwise it cannot exercise
        // tie-breaking. If this ever fires, the fixture FEN no longer yields a top tie and must be
        // replaced with one that does.
        Assert.True(
            topGroup.Count >= 2,
            $"Expected a tie (>= 2 equally-best moves) in {TieFen}, but only one move scored {topScore}.");

        // The documented tie-break is lexicographically smallest move key (ordinal). e3d2 < e3e2 <
        // e3f2, so the deterministic choice must be Ke3-d2.
        var expected = topGroup.OrderBy(s => s.Key, StringComparer.Ordinal).First();
        Assert.Equal("e3", expected.From, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("d2", expected.To, StringComparer.OrdinalIgnoreCase);

        // The player must agree with the independently-computed deterministic winner...
        var first = await sut.ChooseMoveAsync(TieFen, PlayerColor.White);
        Assert.True(first.Success, first.Reason);
        Assert.Equal(expected.From, first.Move!.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected.To, first.Move!.ToSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Null(first.Move!.Promotion);

        // ...and must reproduce that exact move on every subsequent invocation (determinism).
        var second = await sut.ChooseMoveAsync(TieFen, PlayerColor.White);
        var third = await sut.ChooseMoveAsync(TieFen, PlayerColor.White);

        Assert.True(second.Success);
        Assert.True(third.Success);
        Assert.Equal(first.Move, second.Move);
        Assert.Equal(first.Move, third.Move);
        Assert.Equal(first.ResultingFen, second.ResultingFen);
        Assert.Equal(first.ResultingFen, third.ResultingFen);
    }

    [Fact]
    public async Task ChooseMoveAsync_TiePosition_PicksLexicographicallySmallestKey()
    {
        // A focused assertion on the tie-break rule itself: among the tied top moves Ke3-d2 (e3d2),
        // Ke3-e2 (e3e2), and Ke3-f2 (e3f2), the ordinal-smallest key "e3d2" must win. This decouples
        // the tie-break claim from the broader self-verifying test above.
        var sut = CreateSut();

        var result = await sut.ChooseMoveAsync(TieFen, PlayerColor.White);

        Assert.True(result.Success, result.Reason);
        Assert.Equal("e3", result.Move!.FromSquare, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("d2", result.Move!.ToSquare, StringComparer.OrdinalIgnoreCase);
        // Ordinal comparison: "e3d2" is the smallest of the tied keys, so the tie-break must never
        // select e2 or f2 here.
        Assert.NotEqual("e2", result.Move!.ToSquare);
        Assert.NotEqual("f2", result.Move!.ToSquare);
    }
}
