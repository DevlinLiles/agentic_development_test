# PR #54 — Automated Pre-Human Review Verdict

**State:** REQUEST_CHANGES
**Review date:** 2026-09-07
**Branch:** task/01m1xmznrbvn1sqt1cjschg6j4/reviewer
**Head commit:** 07fc8dc — "Add heuristic evaluator with material, piece-square tables, and mobility scoring"

## Summary

PR #54 adds a heuristic position evaluator (`IHeuristicEvaluator` / `HeuristicEvaluator`) with unit tests. The evaluator's material + piece-square-table core is sound (antisymmetric, vertically-mirrored tables, graceful `0` fallback on malformed FEN). However the change has one **blocking** delivery defect (the code is committed to an orphaned `.worktrees/` path and is therefore never compiled or tested — the green CI is not evidence the new code works), one **major** correctness defect (the mobility term breaks the documented antisymmetry invariant, and a test locks in the violation instead of guarding the invariant), plus two quality issues. Actionable issues found; requesting changes.

## Findings

### 1. [BLOCKING] The three new files are committed under `.worktrees/<id>/server/...` instead of `server/...` — the code is orphaned from the solution and never compiled/tested

**Files (committed path → intended path):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` → `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` → `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` → `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:**
The head commit `07fc8dc` added all three files under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` rather than the real `server/...` project paths. `.worktrees/` is an agent worktree artifact, not part of the project layout. Consequences:
1. **Never compiled.** `server/ChessMvp.slnx` references `server/src/ChessMvp.Domain/ChessMvp.Domain.csproj` and `server/tests/ChessMvp.Domain.Tests/ChessMvp.Domain.Tests.csproj`; neither project globs or links anything under `.worktrees/`, so `HeuristicEvaluator` / `IHeuristicEvaluator` are not in any build. A search for `HeuristicEvaluator` across `server/**/*.cs` returns zero matches — the types do not exist at the project path.
2. **Tests never run.** `HeuristicEvaluatorTests.cs` lives outside the test project directory, so `dotnet test` never discovers it. The "CI is green" check therefore exercised none of the new behavior; it passed *because the new code was absent from the build*, not because the tests pass.
3. **Pollutes history.** `.gitignore` has no `.worktrees/` entry, so these stray source files are committed and will be checked out into every clone.

This is the dominant issue: the PR's stated feature is effectively not delivered to the codebase, and green CI provides no validation of it.

**How to fix:**
Move all three files from `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` to the corresponding `server/...` paths (e.g. `git mv`), remove the stray copies, then confirm `dotnet build server/ChessMvp.slnx` compiles the new type and `dotnet test` discovers and runs `HeuristicEvaluatorTests`. Also add `.worktrees/` to `.gitignore` so this cannot recur. (Do not fix in this review task — remediation dispatched separately.)

### 2. [MAJOR] Mobility term breaks the documented antisymmetry invariant; a test locks in the violation instead of guarding it

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` — `Score(string, PlayerColor)` and its class XML doc; corroborated by test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` in `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`.

**What is wrong:**
The class doc states the implementation is antisymmetric: *"`Score(board, White) == -Score(board, Black)` for the material and placement components."* The material+PST core (`EvaluateForWhite`) is indeed antisymmetric. But `Score` then **adds** `MobilityWeight * CountPseudoLegalMoves(grid, side)` for the evaluated side only:

```
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

Mobility is a non-negative count, so the final score is **not** antisymmetric. Antisymmetry would require `Score(W) == -Score(B)`, i.e. `whiteScore + 2·whiteMobility == whiteScore - 2·blackMobility`, which implies `whiteMobility == -blackMobility` — impossible unless both are zero. On the symmetric starting position the test even asserts the *broken* behavior: it checks `whiteScore == blackScore` **and** `whiteScore > 0` (both positive). A truly antisymmetric function on a symmetric position would yield `whiteScore == -blackScore`, so equal values would force both to be `0`; instead the test encodes the violation (equal positive values) rather than asserting the invariant.

This is a correctness defect (a documented invariant is broken) compounded by a test that pins the wrong behavior, so a future fix would appear to "break" the test.

**How to fix:**
Pick one and make the code, the doc, and the tests consistent:
- (a) Make mobility antisymmetric: add the **opponent's** mobility subtracted, e.g. `perspective += MobilityWeight * (ownMobility - opponentMobility)` computed for both colors, so `Score(W) == -Score(B)` holds; then fix the starting-position test to assert `whiteScore == 0` (or `whiteScore == -blackScore`). OR
- (b) Drop the antisymmetry claim from the doc and the starting-position test's symmetry assumption, and document mobility as a non-antisymmetric side-bias term (still ensure the *material/PST* invariant is separately asserted).

Do not ship a doc that claims an invariant the code violates, with a test that asserts the violation.

### 3. [MAJOR] Dead `<see cref="IChessAiPlayer"/>` references to a type that does not exist

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` — class summary XML doc.

**What is wrong:**
The interface doc says it is "used by an `IChessAiPlayer`" and references `<see cref="IChessAiPlayer"/>` twice. There is no `IChessAiPlayer` type anywhere in the repo (a search of `**/*.cs` for `IChessAiPlayer` returns no matches). Once the file is moved to the real project path, `<see cref="IChessAiPlayer"/>` will be an unresolved broken reference (IntelliSense/docfx warning) and misleads readers about an integration that isn't wired up.

**How to fix:**
Remove the `IChessAiPlayer` references from the doc comment (the evaluator is currently a standalone, unconsumed utility), or add the `IChessAiPlayer` abstraction this PR is supposedly building toward. Don't ship a `cref` to a nonexistent type.

### 4. [MINOR] New abstraction is never registered in DI or consumed by any service

**File:** `server/src/ChessMvp.Infrastructure/ServiceCollectionExtensions.cs` (unchanged by this PR).

**What is wrong:**
`IHeuristicEvaluator` / `HeuristicEvaluator` are added but never registered in `AddChessInfrastructure` and never injected/called by any service, controller, or test through DI. The feature is scaffolding with no integration point. Standalone tested utilities can be acceptable, but combined with finding #3 (which advertises an `IChessAiPlayer` consumer that doesn't exist), the PR overstates what is delivered.

**How to fix:**
Either scope the PR honestly to "standalone evaluator + unit tests" (and drop the `IChessAiPlayer` framing per finding #3), or register `IHeuristicEvaluator` in `ServiceCollectionExtensions` and add at least one integration test demonstrating it is resolvable and used. At minimum add a DI registration so the new abstraction is not dead.

## Positive notes

- The material + piece-square-table core is correct and well-factored: vertical mirroring of the rank index yields proper per-color tables, and `EvaluateForWhite` is cleanly antisymmetric in isolation.
- FEN parsing is careful: ranks read top-down (FEN rank 8 → grid index 7), each rank validated to fill exactly 8 files, and malformed input returns `false` so `Score` falls back to a neutral `0` rather than throwing.
- `InBounds` uses the `(uint)x < 8` idiom for a branchless single-comparison bounds check — nice.
- Tests prefer invariant-style assertions (material swing near queen value, mirrored-position equality, centralization > cornering, malformed-input neutrality) over brittle magic numbers, which is the right philosophy — once finding #2 makes them assert the *correct* invariants.

## Verdict

REQUEST_CHANGES. Finding #1 (orphaned `.worktrees/` path) means the PR does not deliver compilable, tested code to the project and green CI is not evidence it works — this alone blocks merge. Finding #2 (mobility breaks the documented antisymmetry invariant, with a test that locks in the violation) is a genuine correctness defect. Findings #3–#4 are quality/dead-code issues that should also be addressed. No fixes applied here per the review-only task scope.
