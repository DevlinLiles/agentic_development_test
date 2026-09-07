# Automated Pre-Human Review — PR #49

**Verdict: REQUEST_CHANGES**

PR #49 adds a heuristic position evaluator (`IHeuristicEvaluator` + `HeuristicEvaluator` +
`HeuristicEvaluatorTests`). CI is reported green, but the most important finding below shows that
the new code is **not actually part of the build**, so the green CI signal is vacuous for this
change. The implementation logic itself also has a contract violation and several quality issues.
No fixes are applied here; each finding is listed concretely for a separate remediation task.

---

## Finding 1 — CRITICAL (Correctness / Build): new files committed outside the repository tree

**Files / lines:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (whole file)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (whole file)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (whole file)

**What is wrong:** All three files were added under a `.worktrees/<id>/server/...` path rather than
the repository-root `server/...` path. No `.csproj` or `.sln` references that worktree directory, so
none of these files are compiled or discovered by `dotnet build` / `dotnet test`. The actual
`server/src/ChessMvp.Domain/Abstractions`, `server/src/ChessMvp.Domain/Services`, and
`server/tests/ChessMvp.Domain.Tests` directories contain **no** `IHeuristicEvaluator.cs`,
`HeuristicEvaluator.cs`, or `HeuristicEvaluatorTests.cs`. Consequently CI is green only because the
new code is never built or run — the tests literally cannot execute.

**How to fix:** Move the three files to their intended locations:
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Remove the stray `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` copies entirely. Then confirm
`dotnet build` and `dotnet test` actually compile and execute `HeuristicEvaluatorTests` (e.g. the
test runner reports the new test cases as run, not zero).

---

## Finding 2 — MAJOR (Correctness / contract violation): antisymmetry contract broken by the mobility term

**File / lines:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, `Score` method
(the `perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);` line);
contract declared in `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` remarks.

**What is wrong:** `IHeuristicEvaluator` documents the invariant
`Score(board, White) == -Score(board, Black)`. The material + piece-square part (`EvaluateForWhite`
then negated for Black) is antisymmetric, but `Score` then **adds** `MobilityWeight *
CountPseudoLegalMoves(grid, side)` as a strictly positive term for whichever side is requested.
For any color-symmetric position (notably the start position) White and Black have equal mobility,
so:

- `Score(start, White) = 0 + M·moves(White) = +M·m`
- `Score(start, Black) = 0 + M·moves(Black) = +M·m`

i.e. `Score(start, White) == Score(start, Black) > 0`, which directly contradicts
`Score(board, White) == -Score(board, Black)` (that would require `+M·m == -M·m`, i.e. `m == 0`).
The test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` asserts this
equal-positive behavior, i.e. it encodes the contract violation rather than the contract.

**How to fix:** Make the mobility term antisymmetric, e.g.:

```csharp
var ownMobility    = CountPseudoLegalMoves(grid, sideToMoveFor: side);
var opponentMobility = CountPseudoLegalMoves(grid, sideToMoveFor: Opposite(side));
perspective += MobilityWeight * (ownMobility - opponentMobility);
```

This yields `Score(start, White) == Score(start, Black) == 0` and restores antisymmetry while still
favoring the more-mobile side from its own perspective. (The existing
`Score_MirroredPosition_ScoresEquallyForMirroredColor` and
`Score_CentralizedQueen_OutScoresCorneredQueen` tests remain valid under this change; the
`Score_StartingPosition_...` test must be updated to assert `== 0` instead of `> 0`, since it
currently asserts the buggy behavior.) Alternatively, if positive mobility-from-perspective is
intended, correct the interface doc to state that antisymmetry holds only for the material and
piece-square components, and update the test accordingly.

---

## Finding 3 — MAJOR (Quality / integration): no consumer and a dangling `<see cref="IChessAiPlayer"/>`

**File / lines:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (class-level
`<summary>` and `<remarks>` reference `<see cref="IChessAiPlayer"/>`).

**What is wrong:** The interface describes itself as "used by an `IChessAiPlayer`" and cross-
references `IChessAiPlayer` twice, but no `IChessAiPlayer` type exists anywhere in the codebase
(only `IGameRepository`, `IGameNotifier`, `IChessRulesEngine` exist under `Abstractions`). Once the
files are moved into the build (Finding 1) this produces a CS1574 unresolved cref warning. More
substantively, `HeuristicEvaluator` is never registered in DI (`ServiceCollectionExtensions` /
`Program.cs`) and is not called by any service — the PR ships an evaluator with no integration, so
the new behavior is unexercised end-to-end.

**How to fix:** Either (a) add the `IChessAiPlayer` consumer that uses the evaluator, with its DI
registration, in this PR; or (b) remove the `IChessAiPlayer` references from the doc comments, land
the evaluator as a clearly-labeled standalone utility, and track a follow-up for integration.

---

## Finding 4 — MINOR (Quality / accuracy): pawn "mobility" counts empty diagonal squares as moves

**File / lines:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
`CountPawnMoves` (the diagonal-capture loop).

**What is wrong:** The diagonal loop increments the count when `target is null` ("a pawn attacks the
diagonal regardless of occupancy"). A pawn cannot move to an empty diagonal square (it only
captures diagonally; en passant is explicitly unmodeled). This inflates pawn mobility by 2 per pawn
regardless of real options — e.g. every pawn in the start position contributes 2 phantom "moves".
The comment acknowledges the relaxation, but it makes the mobility count diverge from real
pseudo-legal move counts and weakens the guarantees tests like
`Score_CentralizedQueen_OutScoresCorneredQueen` rely on.

**How to fix:** Count a diagonal only when an enemy piece occupies the target, or rename/document
the pawn term explicitly as an "attack-square" count rather than a move count so the semantics are
not misleading.

---

## Finding 5 — MINOR (Quality): dead `sideToMove` out-parameter

**File / lines:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
`TryParsePlacement` (signature) and its sole caller in `Score`.

**What is wrong:** `TryParsePlacement` parses the FEN side-to-move into
`out PlayerColor sideToMove`, but `Score` calls it as `TryParsePlacement(board, out var grid, out _)`
and never uses the value. The parsed side-to-move is dead.

**How to fix:** Either use it (e.g. weight mobility for the side actually to move) or drop the
`out` parameter so the parsing work is not performed and discarded.

---

## Non-issues (checked, no action)

- **Security:** `Score` is a pure function over a FEN string — no I/O, no process/SQL/HTML, no
  secret handling. Malformed input returns `0` (graceful degradation) rather than throwing. No
  injection or unsafe-input-trust surface.
- **FEN parsing robustness:** `TryParsePlacement` correctly handles empty/whitespace input, wrong
  rank count (`!= 8`), bad file counts (`fileIndex != 8`), overflow digits, and unknown piece chars
  by returning `false` → neutral `0`. Bounds checking via `(uint)x < 8` is sound.

---

## Summary

The headline blocker is Finding 1: the entire change is stranded outside the build, so it is
neither compiled nor tested and contributes no real functionality yet. Findings 2 and 3 should also
be resolved before human review (contract violation and missing integration/dangling reference).
Findings 4 and 5 are minor cleanups. Recommend REQUEST_CHANGES; remediation should be dispatched as a
bounded fix task.
