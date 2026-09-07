# PR #58 — Automated Pre-Human Review

**Verdict: REQUEST_CHANGES**

CI is green, but the green is vacuous for this change. Every file this PR adds is
committed **outside** the repository source tree (under another worktree's working
directory), so none of it is part of any project and none of it is built or tested
by CI. Beyond that, the implementation carries the same two correctness defects
against its own documented contract that were flagged on the prior iteration of
this exact code (PR #57) — none of which were addressed here. The three code files
in this diff are byte-for-byte identical to the copies PR #57 already reviewed
(verified: `git diff 3f2ad78 07fc8dc -- <each file>` is empty for all three), so
nothing has changed since the last REQUEST_CHANGES verdict. Findings below.

---

## Finding 1 — All new files are committed outside the source tree (critical, blocking)

**Files / lines:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (new)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (new)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (new)

**What is wrong:** All three files are added under the path
`.worktrees/01m1vy27727029etgw6qp81jdc/server/...` instead of `server/...`. That
`.worktrees/<id>/` prefix is *another worktree's* working directory, not part of the
real repository layout. The actual solution tree contains none of these files
(verified: the real `server/src/ChessMvp.Domain/Abstractions/`,
`server/src/ChessMvp.Domain/Services/`, and `server/tests/ChessMvp.Domain.Tests/`
list only their pre-existing members, and a repository-wide search for
`IHeuristicEvaluator` / `HeuristicEvaluator` returns **no** hits inside `server/`).
Because no `.csproj` references the orphaned path, the interface, the
implementation, and the 108-line test class are compiled and executed by
**nothing**. "CI is green" therefore tells us nothing about this change — the 646
added code lines are dead from the solution's perspective. This is the same
misplaced-path defect flagged as Finding 1 on PR #57, and it has not been fixed.

**How to fix:** Re-add the three files at their real locations and delete the
`.worktrees/...` copies:
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then rebuild the `ChessMvp.Domain` and `ChessMvp.Domain.Tests` projects and run the
new tests so CI actually exercises the code. Add `.worktrees/` to `.gitignore` so
worktree working directories can never be committed again.

---

## Finding 2 — Mobility term breaks the documented antisymmetry invariant

**File / line:** `HeuristicEvaluator.cs`, `Score(...)` method — the line
`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);`

**What is wrong:** The class-level XML doc (`HeuristicEvaluator.cs` remarks) states
the implementation is antisymmetric — `Score(board, White) == -Score(board, Black)` —
and the interface doc (`IHeuristicEvaluator.cs` remarks) repeats the same contract
("A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`").
The material + piece-square components *are* antisymmetric (computed once from
White's perspective and negated for Black). But the mobility term is added as a
**non-negative** count for the requested side only and is never subtracted from the
opponent. So for any position with non-zero mobility:

```
Score(board, White) = Wmat + Wmob            // = -(Bmat) + Wmob
Score(board, Black) = Bmat + Bmob            // = -(Wmat) + Bmob
Score(W) + Score(B) = Wmob + Bmob            // should be 0 under antisymmetry
```

`Wmob + Bmob > 0` for essentially every real position, so
`Score(board, White) != -Score(board, Black)`. The invariant is violated. The unit
tests pass only by coincidence — `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
and `Score_MirroredPosition_ScoresEquallyForMirroredColor` assert *equality* of two
separately-requested scores on symmetric positions (where `Wmob == Bmob`), not the
antisymmetry relationship itself, so they cannot catch this. This is the same class
of defect flagged as Finding 2 on PR #57; it has not been fixed.

**How to fix:** Make mobility antisymmetric. Compute both sides' pseudo-legal move
counts and use the difference, e.g.:

```csharp
var ownMobility = CountPseudoLegalMoves(grid, sideToMoveFor: side);
var oppMobility = CountPseudoLegalMoves(grid, sideToMoveFor: Opponent(side));
perspective += MobilityWeight * (ownMobility - oppMobility);
```

(Where `Opponent` flips `PlayerColor`.) Then `Score(W) == -Score(B)` holds for the
full score including mobility. Add a test that asserts the antisymmetry invariant
directly on an *asymmetric* position (e.g. `7k/8/8/8/8/8/4P3/4K3 w - - 0 1`:
`Score(board, White) == -Score(board, Black)`) so the contract is actually guarded.

---

## Finding 3 — Pawn mobility counts illegal empty-square diagonal "moves"

**File / line:** `HeuristicEvaluator.cs`, `CountPawnMoves(...)` — the diagonal
capture branch:
```csharp
var target = grid[targetFile, targetRank];
if (target is null || target.Color != color)
{
    count++;
}
```

**What is wrong:** A pawn may only move diagonally onto a square occupied by an
*enemy* piece (a capture), or via en passant (which the code explicitly says it does
not model). Here `target is null` (an *empty* diagonal square) is counted as a pawn
move. That is not a pseudo-legal pawn move by any standard definition — a pawn does
not move diagonally to an empty square. The inline comment rationalizes it as "a
pawn attacks the diagonal regardless of occupancy," but an *attack* is not a *move*,
and a mobility term is supposed to count move options. This systematically inflates
pawn mobility: every non-edge pawn gets +2 (both diagonals empty) for moves it
cannot make. On the starting position each side's 8 pawns contribute 16 phantom
moves, distorting the mobility tie-breaker the term is meant to be. This is the same
defect flagged as Finding 3 on PR #57; it has not been fixed.

**How to fix:** Only count a diagonal when the target holds an enemy piece (en
passant aside, which is intentionally omitted):
```csharp
var target = grid[targetFile, targetRank];
if (target is not null && target.Color != color)
{
    count++;
}
```
This matches the standard pseudo-legal pawn-move definition and keeps the mobility
term meaningful. Re-run the affected tests after the change.

---

## Note on iteration / duplicate work

PR #58's three code files are byte-for-byte identical to the copies reviewed under
PR #57 (verified: `git diff 3f2ad78 07fc8dc -- <each file>` is empty for all three).
This change re-submits the same code with the same defects and the same
misplaced-path problem, none of which were remediated. If the intent was to address
the PR #57 review, that remediation did not happen; please apply the fixes above
(and the relocation) rather than re-posting the identical diff.

---

## Summary

The change cannot be approved: the deliverable is not wired into the build at all
(Finding 1, blocking), and even once wired in the evaluator violates its own stated
antisymmetry contract (Finding 2) and over-counts pawn mobility with illegal moves
(Finding 3). No code changes were made by this review; remediation should be
dispatched as a separate bounded fix task.
