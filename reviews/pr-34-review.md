# PR #34 — Automated Pre-Human Review

**Verdict: REQUEST_CHANGES**

**Scope reviewed:** The PR diff (base `43a5502` → head `07fc8dc`) adds a heuristic position
evaluator for the chess AI: `IHeuristicEvaluator`, `HeuristicEvaluator`, and
`HeuristicEvaluatorTests`. (This is the same head commit reviewed previously for PR #33; the
findings below were re-verified independently against the current source and remain
actionable, so the verdict is unchanged: REQUEST_CHANGES.)

---

## Finding 1 — BLOCKING: All new files are committed under a nested `.worktrees/` path and are not built or tested

**Files / lines:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (entire file)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (entire file)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (entire file)

**What is wrong:**
Every file in this PR was committed under the path prefix
`.worktrees/01m1vy27727029etgw6qp81jdc/server/...` instead of the repository-root
`server/...` where the actual `ChessMvp.Domain` and `ChessMvp.Domain.Tests` projects live.
Confirmed against the working tree: the canonical
`server/src/ChessMvp.Domain/Abstractions/` (which contains `IChessRulesEngine.cs`,
`IGameNotifier.cs`, `IGameRepository.cs`, etc.), `server/src/ChessMvp.Domain/Services/`
(`GameService.cs`, `IGameService.cs`), and `server/tests/ChessMvp.Domain.Tests/`
(`ChessMvp.Domain.Tests.csproj`, `GameServiceTests.cs`,
`GerasimleoChessRulesEngineAdapterTests.cs`) contain **none** of the three new files — they
exist only under the stray `.worktrees/` prefix.

Consequences:
1. The new `IHeuristicEvaluator` interface and `HeuristicEvaluator` implementation are **not
   included in the `ChessMvp.Domain` project compilation** — they are orphaned source files in
   an unrelated nested directory.
2. The new `HeuristicEvaluatorTests` are **not discovered or executed** by the test runner,
   because they sit outside the `ChessMvp.Domain.Tests` project directory.
3. "CI is green" is **vacuously true** for this PR: CI builds/tests the root `server/`
   projects, which contain none of the new code. The new evaluator and its tests are effectively
   dead code that is never compiled and never run. Nothing in CI actually exercises the new
   behavior.
4. The `.worktrees/` directory is a git-worktree implementation artifact; committing it pollutes
   the repository and will be re-created/conflict on every agent session.

**How to fix:** Move all three files out of `.worktrees/01m1vy27727029etgw6qp81jdc/` to the
real project locations and remove the stray `.worktrees/` tree from the commit:
- `.worktrees/.../server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
  → `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/.../server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
  → `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/.../server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`
  → `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

After moving, run `dotnet build` and `dotnet test` on the `server/` solution to confirm the
evaluator now compiles into `ChessMvp.Domain` and the tests actually execute. Ensure
`.worktrees/` is gitignored so it cannot be re-committed.

---

## Finding 2 — Contract/doc inaccuracy: unconditional antisymmetry claim is violated by the mobility term

**File / line:** `IHeuristicEvaluator.cs`, class-level `<remarks>` doc
(`Score(board, White) == -Score(board, Black)`).

**What is wrong:** The *interface* doc states the evaluator is antisymmetric,
`Score(board, White) == -Score(board, Black)`, unconditionally. The material and piece-square
components are indeed antisymmetric, but the mobility term is added from the *requested* side's
perspective only (`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)`).
For a symmetric position (e.g. the start position) both sides have equal mobility, so
`Score(start, White) == Score(start, Black)` — a positive number — not `White == -Black`.
The PR's own test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` asserts
`Assert.Equal(whiteScore, blackScore)`, which directly contradicts the documented
antisymmetry invariant. (The `HeuristicEvaluator` class-level doc partially hedges by qualifying
the invariant as applying "for the material and placement components," but the *interface* — the
public contract — still makes the unconditional claim.) The invariant as written on the contract
is false.

**How to fix:** Correct the `IHeuristicEvaluator` interface documentation to state that only the
material and piece-square components are antisymmetric, and that the mobility term makes the
overall score side-perspective (not strictly antisymmetric). Alternatively, if true antisymmetry
is desired, compute mobility symmetrically (own mobility minus opponent mobility) so the
invariant holds — but then update the start-position test accordingly. Pick one and make the
contract doc and behavior agree.

---

## Finding 3 — Minor: pawn mobility over-counts by treating empty diagonal squares as moves

**File / line:** `HeuristicEvaluator.cs`, `CountPawnMoves` — the diagonal-capture loop.

**What is wrong:** The loop counts a diagonal target as a pawn move whenever
`target is null || target.Color != color`. A pawn cannot move diagonally onto an *empty*
square (only en passant, which the code explicitly does not model). So every empty diagonal
square is incorrectly counted as a pawn move, inflating pawn mobility. The code comment labels
this a "pseudo-legal relaxation," but it is not a legal or pseudo-legal pawn move — it is simply
not a move. For symmetric positions the error cancels between colors, so existing tests pass,
but for asymmetric positions it biases the mobility tie-breaker toward positions with pawns
that have empty diagonals.

**How to fix:** Count a diagonal only when an enemy piece is present
(`target is not null && target.Color != color`). Drop the `target is null` branch for pawn
diagonals. This is a heuristic, so the impact is small, but the current count does not represent
move count.

---

## Finding 4 — Minor: dangling `<see cref="IChessAiPlayer"/>` reference

**File / line:** `IHeuristicEvaluator.cs`, class-level XML doc
(`<see cref="IChessAiPlayer"/>`, appears twice).

**What is wrong:** The doc references `IChessAiPlayer`, but no type named `IChessAiPlayer`
exists anywhere in `server/` under the canonical project tree. The cref is dangling and will
produce XML doc warnings / unresolved references. (Note: this only becomes visible once Finding
1 is fixed and the file is actually compiled.)

**How to fix:** Either add the `IChessAiPlayer` abstraction this evaluator is meant to serve,
or remove/replace the cref with plain text ("an AI player") until that type exists.

---

## Summary

Finding 1 is blocking: the PR's entire deliverable is misplaced into a `.worktrees/` artifact
directory, so it is neither compiled nor tested by CI; "CI is green" does not validate any of
the new code. Findings 2–4 are correctness/quality issues in the evaluator itself that should
be addressed but are secondary to the files actually landing in the right place. No code changes
were made by this review; remediation should be dispatched as a separate bounded fix task.
