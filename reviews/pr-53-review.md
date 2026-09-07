# PR #53 — Automated Pre-Human Review Verdict

**State:** REQUEST_CHANGES
**Review date:** 2026-09-06
**Branch:** task/01m1xmsa031hhwgcsys4m2agxa/reviewer
**Head commit:** 07fc8dc — "Add heuristic evaluator with material, piece-square tables, and mobility scoring"

## Summary

PR #53 adds a heuristic position evaluator (`IHeuristicEvaluator` / `HeuristicEvaluator`) with unit tests. The evaluator logic itself is reasonable (material + piece-square tables + mobility, antisymmetric, graceful fallback on malformed FEN). However, the **entire change was committed to the wrong path**, which makes the new code dead/orphaned and the "CI is green" signal meaningless for this PR. This is a blocking issue.

## Findings

### 1. [BLOCKING] All three new files are committed under a `.worktrees/<id>/` path instead of the real `server/...` path — the code is orphaned from the solution

**Files (committed path → intended path):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` → should be `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` → should be `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` → should be `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:**
The commit `07fc8dc` added the files under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` rather than `server/...`. The `.worktrees/` directory is an agent worktree artifact, not part of the project layout. Consequences:

1. **The new code is never compiled.** `server/ChessMvp.slnx` references `server/src/ChessMvp.Domain/ChessMvp.Domain.csproj` and `server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj`. Neither project globs or links anything under `.worktrees/`, so `HeuristicEvaluator` / `IHeuristicEvaluator` are not part of any build. A `grep` for `HeuristicEvaluator` across `server/**/*.cs` returns zero matches — the files simply do not exist at the project path.
2. **The new tests never run.** `HeuristicEvaluatorTests.cs` lives outside the test project directory, so it is not discovered by `dotnet test`. The "CI is green" check therefore exercised none of the new behavior; it passed *because the new code was absent from the build*, not because the tests pass.
3. **The files are tracked in the repo.** `.gitignore` has no entry for `.worktrees/`, so these orphaned source files are committed to history and will be checked out into every clone, polluting the tree with a stray copy of domain code under a per-worktree subdirectory.

This is the dominant issue: the PR's stated feature (the evaluator) is effectively not delivered to the codebase, and the green CI does not validate it.

**How to fix:**
Move all three files from `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` to the corresponding `server/...` paths (i.e. `git mv` them, or re-add at the correct location and remove the stray copies), then commit. After the move, confirm `dotnet build server/ChessMvp.slnx` actually compiles the new type and `dotnet test` discovers and runs `HeuristicEvaluatorTests`. Also add `.worktrees/` to `.gitignore` so this cannot recur. (Do not fix in this review task — remediation is dispatched separately.)

### 2. [MAJOR] Dead/undefined cross-reference in public API docs — `IChessAiPlayer` does not exist

**File:** `IHeuristicEvaluator.cs` (intended path `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`), class/`<see cref>` references in the XML doc comment.

**What is wrong:**
The interface summary states it is "used by an `IChessAiPlayer`" and references `IChessAiPlayer` via `<see cref="IChessAiPlayer"/>`. There is no `IChessAiPlayer` type anywhere in `server/` (search of `server/**/*.cs` for `IChessAiPlayer` returns no matches). So the documented collaborator does not exist, and once the file is moved to the real project path, `<see cref="IChessAiPlayer"/>` will fail to resolve (a docfx/IntelliSense broken reference) and misleads readers about an integration that isn't wired up.

**How to fix:**
Either remove the `IChessAiPlayer` references from the doc comment (the evaluator is currently a standalone utility with no consumer), or add the `IChessAiPlayer` abstraction this PR is supposedly building toward. Don't ship a `cref` to a nonexistent type.

### 3. [MINOR] The new evaluator is not registered or consumed anywhere

**File:** `server/src/ChessMvp.Infrastructure/ServiceCollectionExtensions.cs` (no change in this PR).

**What is wrong:**
Even after the path fix, `IHeuristicEvaluator`/`HeuristicEvaluator` are added but never registered in DI (`ServiceCollectionExtensions` is untouched) and never injected/called by any service or controller. The feature is scaffolding with no integration point. This is acceptable for an isolated, tested utility, but combined with finding #2 (which advertises an `IChessAiPlayer` consumer), it suggests the PR overstates what is delivered.

**How to fix:**
Either scope the PR honestly to "standalone evaluator + unit tests" and drop the `IChessAiPlayer` framing (see finding #2), or register `IHeuristicEvaluator` in `ServiceCollectionExtensions` and add at least one integration/consumer test demonstrating it is resolvable and used. At minimum, add a DI registration so the new abstraction is not dead.

## Positive notes

- The evaluator design is sound for its scope: pure, deterministic, antisymmetric material + PST + mobility, with a safe `0` fallback on unparseable FEN (`TryParsePlacement` returns `false`).
- The unit tests encode invariant-style assertions (material swing near queen value, mirrored-position equality, centralization > cornering, malformed-input neutrality) rather than brittle magic numbers — good test philosophy.
- FEN parsing mirrors ranks correctly (FEN rank 8 → grid index 7) and validates each rank fills exactly 8 files.

## Verdict

REQUEST_CHANGES. The path-placement defect (finding #1) means the PR does not actually deliver compilable, tested code to the project and the green CI is not evidence the new code works. Findings #2–#3 should also be addressed. No fixes applied here per the review-only task scope.
