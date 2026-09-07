# Automated Pre-Human Review — PR #47

- **Verdict:** REQUEST_CHANGES
- **Head:** `07fc8dc` — Add heuristic evaluator with material, piece-square tables, and mobility scoring
- **Base:** `43a5502` (Auto-commit: implementation)
- **Scope:** diff `43a5502..07fc8dc` — three new files, evaluator-only slice:
  - `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (interface, +31)
  - `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (implementation, +507)
  - `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (tests, +108)

CI is green, but the diff ships a documented invariant that the implementation does not satisfy, a new test that contradicts the implementation, and a missing test for the invariant. Findings below are concrete (file, symbol, what is wrong, how to fix); remediation should be dispatched as a separate bounded fix task. No code was changed by this review.

---

## Finding 1 — Correctness: the documented antisymmetry invariant is broken by the mobility term

**Files**
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` — interface `<remarks>`: *"A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."* (stated unconditionally)
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` — `Score(string board, PlayerColor side)`:
  ```csharp
  var whiteScore = EvaluateForWhite(grid);
  var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
  perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
  return perspective;
  ```

**What is wrong**
The material + piece-square part (`EvaluateForWhite`) is genuinely antisymmetric: `EvaluateForWhite` is White-relative and negated for Black, so `Score(W) == -Score(B)` for that component. But the mobility term is then **added from the evaluated side's own perspective for both colors** — always a non-negative `+MobilityWeight * mob(side)`. Therefore:

- `Score(board, White)  =  whiteScore + W·mob(W)`
- `Score(board, Black)  = -whiteScore + W·mob(B)`

Antisymmetry requires `Score(W) == -Score(B)`, i.e. `whiteScore + W·mob(W) == whiteScore - W·mob(B)`, which demands `mob(W) == -mob(B)` — impossible for non-negative counts. So `Score(board, White) == -Score(board, Black)` fails on **every** position where the two sides' mobilities differ, and even on the symmetric starting position strict antisymmetry fails (both sides receive `+W·mob`, not `±W·mob`). The class `<remarks>` hedges ("antisymmetric … for the material and placement components"), but the **interface** advertises antisymmetry unconditionally, and the interface is the contract an `IChessAiPlayer` consumer would rely on.

This is not merely a doc nit: the interface states the evaluator is *"used by an `IChessAiPlayer` to score a board position"*, and antisymmetry is a hard requirement for negamax-based search (you need `Score(side) == -Score(opponent)` or move selection is unsound). An AI player built on this evaluator would score the same position inconsistently across perspectives.

**How to fix**
Fold mobility into the White-relative score symmetrically and drop the separate per-side addition:
```csharp
var whiteScore = EvaluateForWhite(grid);
whiteScore += MobilityWeight * (CountPseudoLegalMoves(grid, PlayerColor.White)
                              - CountPseudoLegalMoves(grid, PlayerColor.Black));
return side == PlayerColor.White ? whiteScore : -whiteScore;
```
This preserves `Score(W) == -Score(B)` for all positions. Then align the interface `<remarks>` (remove the unconditional antisymmetry claim, or keep it once the code actually satisfies it) and the class `<remarks>` so both accurately describe the final behaviour.

---

## Finding 2 — Correctness/Tests: a newly added test contradicts the implementation and fails

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` — `Score_MalformedBoard_ReturnsNeutralZero`, specifically the `InlineData` case:
```csharp
[InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")] // missing ranks / malformed
```

**What is wrong**
The comment claims "missing ranks / malformed", but that FEN **has all eight ranks**: splitting on `/` yields exactly eight segments (`rnbqkbnr`, `pppppppp`, `8`, `8`, `8`, `8`, `PPPPPPPP`, `RNBQKBNR`), each of which expands to exactly eight files with valid piece glyphs. `HeuristicEvaluator.TryParsePlacement` only requires `fields.Length >= 1` (the side-to-move field is optional and defaults to White) and checks `ranks.Length == 8` plus per-rank file counts — all of which pass for this input. The string therefore **parses successfully**, and `Score` returns `EvaluateForWhite (== 0, symmetric material+PST) + MobilityWeight * mobility(White)`.

On this (full starting-rank) placement White's mobility is well above zero (e.g. eight pawns each with a single + double push, plus knight moves), so `Score(...)` returns a **positive** value (on the order of `2 * ~20`). The test asserts `Assert.Equal(0, _sut.Score(board, PlayerColor.White))`, which is contradicted by the implementation and fails.

(That CI is reported green while this test exists is itself worth flagging: it suggests the `ChessMvp.Domain.Tests` project may not actually be built/executed by the `dotnet test` step — e.g. not referenced by a solution or not included in the test run. Either way the test logic is wrong and must be corrected; if the project genuinely isn't running, that is a separate CI-coverage gap to close.)

**How to fix**
Make the InlineData actually malformed as intended. To exercise the "missing ranks" path, omit a rank so there are fewer than eight slash-separated segments, e.g. drop one `8/`:
```csharp
[InlineData("rnbqkbnr/pppppppp/8/8/8/PPPPPPPP/RNBQKBNR")] // only 7 ranks → malformed
```
That yields `ranks.Length == 7` and `TryParsePlacement` returns `false`, so `Score` returns `0` and the assertion holds. (Keep the existing `""`, `"   "`, and `"not-a-fen"` cases as-is; they correctly hit the `IsNullOrWhiteSpace` / parse-failure paths.) Alternatively, if the intent was a well-formed-but-incomplete FEN (missing side-to-move) that should still be accepted, change the assertion rather than the data — but the comment and method name ("MalformedBoard … NeutralZero") make clear the data is meant to be malformed, so fixing the data is correct.

---

## Finding 3 — Quality/Tests: the antisymmetry invariant is not tested on an asymmetric position

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` — `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` and `Score_MirroredPosition_ScoresEquallyForMirroredColor`.

**What is wrong**
The only color-symmetry assertions in the suite are `Assert.Equal(whiteScore, blackScore)` on the **symmetric** starting position and on a **mirrored** position. In both cases `mob(W) == mob(B)` and the White-relative material+PST score is `0`, so `Score(W) == Score(B)` holds *trivially* (both equal `+W·mob`) — it holds **despite** the broken antisymmetry from Finding 1, not because of it. No test asserts the interface's actual contract, `Score(board, White) == -Score(board, Black)`, on an **asymmetric** position — exactly the case where the contract fails today. The existing tests therefore give false confidence that the invariant holds.

**How to fix**
Add a test that asserts the documented invariant on a non-symmetric position, e.g.:
```csharp
[Fact]
public void Score_AsymmetricPosition_IsAntisymmetricAcrossColors()
{
    const string board = "7k/8/8/8/8/2q5/3P4/4K3 w - - 0 1";
    Assert.Equal(_sut.Score(board, PlayerColor.White),
                 -_sut.Score(board, PlayerColor.Black));
}
```
This will fail on the current code and pass once Finding 1 is fixed, locking the invariant in.

---

## Non-blocking notes (no change required)

- **Malformed-input handling** is otherwise sound: `Score` returns `0` for null/whitespace and for unparseable placements via `TryParsePlacement`, and the `""` / `"   "` / `"not-a-fen"` theory cases cover those paths correctly. The graceful-degradation design is good.
- **Self-containment**: the private `Piece` / `PieceType` mirror types and the built-in pseudo-legal move counters keep the evaluator free of any third-party move-generation dependency, matching the rest of the domain contracts. Acceptable.
- **`KingValue = 20000`** is intentionally large so the king is never "captured" in a material trade; fine for a static evaluator. No change needed.
- **Determinism**: the evaluator is pure (no static mutable state, no randomness), satisfying the interface's determinism contract. Good.
- **Security**: the PR introduces no I/O, no untrusted-input parsing beyond the already-robust FEN path, no secrets, and no new network surface. No injection/authz/secret-handling concerns in this diff.

---

## Summary

The evaluator's core material + piece-square logic is correct and well-structured, but (1) the mobility term violates the interface's own antisymmetry contract and the docs overstate it (Finding 1); (2) one new `InlineData` case in the malformed-board theory is actually well-formed and makes that test contradict the implementation (Finding 2); and (3) the antisymmetry invariant is never tested on an asymmetric position, so the suite cannot catch Finding 1 (Finding 3). Requesting changes; recommend a bounded fix task address Findings 1–3 before human review.
