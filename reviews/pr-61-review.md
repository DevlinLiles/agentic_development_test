# Automated Pre-Human Review — PR #61

**Verdict: REQUEST_CHANGES**

**Branch:** `task/01m1xq72tybmjmq5kqt0vwd93k/reviewer`
**Commits reviewed:** `43a5502` (Auto-commit: implementation) and `07fc8dc` (Add heuristic evaluator with material, piece-square tables, and mobility scoring)
**Scope:** Client opponent-type selector + server-side `IHeuristicEvaluator`/`HeuristicEvaluator` with tests.

CI is green, but green CI here is misleading: the server-side files added in this PR are not part of any project, so they are never compiled and their tests never execute (see Finding 1). The headline feature is inert as merged.

---

## Findings

### Finding 1 — Critical (correctness / build): Heuristic evaluator committed entirely outside the project tree

**Files (as committed in `07fc8dc`):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:** All three new server files were committed under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` instead of `server/...`. That path is outside the `server/src/ChessMvp.Domain` project (whose `.csproj` globs are relative to the project directory), so:
- `HeuristicEvaluator` is never compiled into `ChessMvp.Domain`.
- `IHeuristicEvaluator` is never part of the domain assembly.
- `HeuristicEvaluatorTests` is never discovered/run by the test project.

`git diff HEAD~1 HEAD --name-only` confirms the only files in that commit are the three `.worktrees/...` paths. The real `server/src/ChessMvp.Domain/Services/` still contains only `GameService.cs` / `IGameService.cs`. This is why CI is green: the orphaned files are simply never built.

**How to fix:** Move the three files to their correct locations and delete the `.worktrees/...` copies:
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then verify `dotnet build`/`dotnet test` against the solution actually picks them up.

---

### Finding 2 — Critical (repo hygiene / quality): Accidental worktree gitlinks committed

**Files (added in `43a5502`):**
- `.worktrees/01m1mgnqs4dbnrw1wvktjqdpbm` (mode `160000` gitlink → `cdb78ec`)
- `.worktrees/01m1mgnqxm7hczvj5nfa7gnvh7` (mode `160000` gitlink → `cdb78ec`)

**What is wrong:** Two entries under `.worktrees/` were committed as git submodule gitlinks (mode `160000`) pointing at internal commits. These are accidental artifacts of staging a worktree directory (which contains a `.git` file). They pollute the repository root, will confuse any submodule-aware tooling (`git submodule` operations, clones), and have no place in the product.

**How to fix:** `git rm` both gitlink entries. Ensure `.worktrees/` is covered by `.gitignore` so worktrees are never staged again (verify the ignore rule actually matches — these slipped through).

---

### Finding 3 — Major (correctness / broken invariant): Mobility term violates the documented antisymmetry invariant and does not measure relative mobility

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, `Score(...)` method (approx. lines 96–118); interface contract in `IHeuristicEvaluator.cs` `<remarks>` (approx. lines 18–20).

**What is wrong:** `IHeuristicEvaluator` explicitly documents the invariant:
> A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`.

But `Score` adds mobility as a **same-sign** term for whichever side is requested:
```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```
For a symmetric position this yields `Score(board, White) == Score(board, Black)` (both positive), not `Score(board, White) == -Score(board, Black)`. The unit test `Score_StartingPosition_FavorsNeitherSideAndIsPositive` even asserts this equality, confirming the implementation contradicts its own stated contract.

Beyond the contract violation, the term only counts the evaluated side's moves and never subtracts the opponent's, so it rewards "has any legal moves" rather than "has more mobility than the opponent." A side with 5 pseudo-legal moves against an opponent with 30 still receives a positive `+MobilityWeight*5` bonus. This is not a meaningful mobility evaluation.

**How to fix:** Make the mobility term antisymmetric (and consistent with the documented invariant), e.g.:
```csharp
var opponent = side == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));
```
Then update `Score_StartingPosition_...` to assert `Score(White) == -Score(Black)` (and `== 0` for the symmetric start, since mobility difference is zero). If same-sign mobility is actually intended, remove/rewrite the antisymmetry claim in the interface doc — but the relative-mobility problem remains either way.

---

### Finding 4 — Major (quality / dead code): Evaluator has no consumer, no DI registration, and its doc references a non-existent type

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (summary, approx. lines 5–9); `HeuristicEvaluator` is never referenced elsewhere.

**What is wrong:**
- The interface summary contains `<see cref="IChessAiPlayer"/>`, but no `IChessAiPlayer` type exists anywhere in `server/src/ChessMvp.Domain/Abstractions/` (the folder contains only `IGameRepository`, `IGameNotifier`, `IChessRulesEngine`, `MoveFailureReason`, `MoveApplicationResult`). This is a broken `cref` (CS1574) pointing at a phantom type and is misleading to readers.
- `HeuristicEvaluator` is never registered in DI (`ChessMvp.Infrastructure/ServiceCollectionExtensions.cs` has no registration) and is never invoked by any caller. Combined with Finding 1, the entire server-side addition is dead/inert.

**How to fix:** If the evaluator is meant to ship now, wire it: register `IHeuristicEvaluator` in DI and add at least one consuming caller (or an AI player that uses it), plus a test that exercises the wiring. If it is intentionally scaffolding for a later PR, drop the `cref="IChessAiPlayer"` reference (or add the `IChessAiPlayer` type) so the doc is accurate, and note in the PR description that the component is intentionally unused for now.

---

## Non-blocking observations (not requesting changes)

- `CountPawnMoves` counts empty diagonal squares as pawn "moves" (the documented pseudo-legal relaxation). This inflates pawn mobility and is mildly asymmetric, but it is explicitly called out in the code comment and is acceptable for a tie-breaker heuristic. Worth revisiting if the evaluator is later used for move selection.
- Client changes (`OpponentTypeSelector`, `CreateGameScreen`, `gamesApi.createGame`, `gameTypes.OpponentType`) look correct and backward-compatible: default is `"Human"`, the request body is omitted for the legacy Human path so the parameterless `POST /api/games` contract is preserved, and the radio group is accessible (fieldset/legend, `disabled` propagation). No injection/authz concerns in the client diff.

---

## Summary

The client-side work is clean. The server-side work, however, is not landable as-is: the entire `HeuristicEvaluator` feature was committed outside the source tree (Finding 1) and is therefore never compiled or tested — "green CI" does not validate it. Additionally, two worktree gitlinks were accidentally committed (Finding 2), the mobility term breaks the interface's own antisymmetry invariant and fails to measure relative mobility (Finding 3), and the evaluator is unwired with a broken `cref` to a non-existent type (Finding 4). Requesting changes; remediation should be dispatched as a bounded fix task (no code changes made in this review).
