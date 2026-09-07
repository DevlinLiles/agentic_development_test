# Automated Pre-Human Review — PR #62

**Verdict: REQUEST_CHANGES**

**Branch:** `task/01m1xqej5q48q16153666cq6ya/reviewer`
**Head commit reviewed:** `07fc8dc` (Add heuristic evaluator with material, piece-square tables, and mobility scoring), parent `43a5502` (Auto-commit: implementation)
**Scope:** Server-side `IHeuristicEvaluator` interface + `HeuristicEvaluator` implementation (material, piece-square tables, mobility) with unit tests. No client changes in this PR's diff.

CI is green, but that is misleading for this PR: the server-side files it adds are committed entirely outside the project tree, so they are never compiled and their tests never run (Finding 1). The headline feature is inert as merged. Several deeper correctness and quality issues also remain in the implementation itself.

---

## Findings

### Finding 1 — Critical (correctness / build): Heuristic evaluator committed entirely outside the project tree

**Files (as committed in `07fc8dc`):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:** All three new server files were committed under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` instead of `server/...`. That path is outside the `server/src/ChessMvp.Domain` project (and outside the solution), so:
- `IHeuristicEvaluator` / `HeuristicEvaluator` are never referenced by any `.csproj` and never compiled.
- `HeuristicEvaluatorTests` is never referenced by the test project and never executed.

Because the orphaned files are not part of any build, "green CI" does not actually validate any of this code — there is no compile or test coverage on the merged tree. The `git diff` for this PR shows only these three `.worktrees/...` paths; the real `server/src/ChessMvp.Domain/Services/` is untouched, which is why CI stays green.

**How to fix:** Move the three files to their correct locations and delete the `.worktrees/...` copies:
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then confirm the solution's `.csproj` references actually include them and that `dotnet build` / `dotnet test` picks them up. (Note: in this sandbox only the .NET 8 SDK is installed while the `server/` projects target `net10.0`, so a local build will fail with NETSDK1045 — confirm inclusion by checking the project items / IDE, and rely on CI after the path is corrected.)

---

### Finding 2 — Critical (correctness / broken invariant): Mobility term violates the documented antisymmetry invariant and does not measure relative mobility

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, `Score(...)` method (approx. lines 96–118); interface contract in `IHeuristicEvaluator.cs` `<remarks>` (approx. lines 18–20).

**What is wrong:** `IHeuristicEvaluator` explicitly documents the invariant:
> A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`.

But `Score` adds mobility as a **same-sign** term for whichever side is requested:
```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```
For a symmetric position this yields `Score(board, White) == Score(board, Black)` (both positive), not `Score(board, White) == -Score(board, Black)`. The unit test `Score_StartingPosition_FavorsNeitherSideAndIsPositive` asserts exactly this `whiteScore == blackScore` equality, confirming the implementation contradicts its own stated contract and the test encodes the wrong invariant.

Beyond the contract violation, the term only counts the evaluated side's moves and never subtracts the opponent's, so it rewards "has any legal moves" rather than "has more mobility than the opponent." A side with 5 pseudo-legal moves against an opponent with 30 still receives a positive `+MobilityWeight*5` bonus. This is not a meaningful mobility evaluation.

**How to fix:** Make the mobility term antisymmetric (and consistent with the documented invariant), e.g.:
```csharp
var opponent = side == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));
```
Then update `Score_StartingPosition_...` to assert `Score(White) == -Score(Black)` (and `== 0` for the symmetric start, since the mobility difference is zero). If same-sign mobility is actually intended, remove/rewrite the antisymmetry claim in the interface doc — but the relative-mobility problem remains either way.

---

### Finding 3 — Major (quality / dead code): Evaluator has no consumer, no DI registration, and its doc references a non-existent type

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (summary, approx. lines 5–9); `HeuristicEvaluator` is never referenced elsewhere.

**What is wrong:**
- The interface summary contains `<see cref="IChessAiPlayer"/>`, but no `IChessAiPlayer` type exists anywhere in `server/src/ChessMvp.Domain/Abstractions/`. This is a broken `cref` (CS1574) pointing at a phantom type and is misleading to readers.
- `HeuristicEvaluator` is never registered in DI and is never invoked by any caller. Combined with Finding 1, the entire server-side addition is dead/inert as merged.

**How to fix:** If the evaluator is meant to ship now, wire it: register `IHeuristicEvaluator` in DI and add at least one consuming caller (or an AI player that uses it), plus a test that exercises the wiring. If it is intentionally scaffolding for a later PR, drop the `cref="IChessAiPlayer"` reference (or add the `IChessAiPlayer` type) so the doc is accurate, and note in the PR description that the component is intentionally unused for now.

---

### Finding 4 — Minor (quality / missing test): No test guards the antisymmetry invariant, and pawn mobility over-counting is untested

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:** The new behavior's central advertised invariant — `Score(board, White) == -Score(board, Black)` for symmetric positions — has no test asserting it; instead the existing test asserts the *opposite* (same-sign) equality (see Finding 2). Additionally, `CountPawnMoves` counts empty diagonal squares as pawn "moves" (inflating pawn mobility and introducing a mild asymmetry vs. real pseudo-legal captures), and there is no test pinning or documenting that behavior for the new code, so a future change could silently regress it.

**How to fix:** Add a test that asserts `Score(sym, White) == -Score(sym, Black)` once the mobility term is fixed (Finding 2), and add a focused test for pawn move-counting that documents the empty-diagonal relaxation so the behavior is intentional rather than incidental.

---

## Non-blocking observations (not requesting changes)

- `TryParsePlacement` handles malformed FEN gracefully and returns `0`, with `Score_MalformedBoard_ReturnsNeutralZero` covering it — good defensive parsing, no injection/secret/authz surface in this server diff (it consumes only a FEN string, no I/O or untrusted external resources beyond the input).
- Material values and piece-square tables follow the standard "Simplified Evaluation Function" tables and are applied with correct per-color rank mirroring; the `Score_MirroredPosition_ScoresEquallyForMirroredColor` test reasonably validates the placement/mirror behavior.

---

## Summary

The implementation logic (material + piece-square tables + mobility) is largely sound in isolation, but this PR is not landable as-is: the entire `HeuristicEvaluator` feature was committed outside the source tree (Finding 1) and is therefore never compiled or tested — "green CI" does not validate it. Additionally, the mobility term breaks the interface's own antisymmetry invariant and fails to measure relative mobility (Finding 2), the evaluator is unwired with a broken `cref` to a non-existent type (Finding 3), and the missing/incorrect invariant test leaves the contract unguarded (Finding 4). Requesting changes; remediation should be dispatched as a bounded fix task (no code changes made in this review).
