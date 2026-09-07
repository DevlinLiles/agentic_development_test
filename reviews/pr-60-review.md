# PR #60 — Automated Pre-Human Review

**Verdict: REQUEST_CHANGES**

CI is green, but that green is vacuous here: every file this PR adds is committed
**outside** the repository source tree (under another worktree's working directory),
so none of it is part of any project and none of it is built or tested by CI. Beyond
the misplaced path, the implementation carries the same two correctness defects
against its own documented contract that were flagged on the prior iterations of
this exact code (PRs #57, #58, #59) — none of which were addressed here. The PR #60
head `07fc8dc` is byte-for-byte identical to the heads of PRs #57/#58/#59 (the three
code files are unchanged), so this is a re-submission of the same diff with the same
defects and the same misplaced-path problem, none of which were remediated. Findings
below.

---

## Finding 1 — All new files are committed outside the source tree (critical, blocking)

**Files (from `git diff --name-only` of the PR):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (new)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (new)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (new)

**What is wrong:** The three new files live under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...`
— i.e. inside *another worktree's* working directory, not the repository's `server/`
tree. A check against the real source tree
(`find server -iname "*HeuristicEvaluator*"`) returns no hits; the only copies live
under `.worktrees/01m1vy27727029etgw6qp81jdc/`. Because no `.csproj` references the
orphaned path, the interface, the implementation, and the 108-line test class are
compiled and executed by **nothing**. "CI is green" therefore tells us nothing about
this change — the 646 added code lines are dead from the solution's perspective. This
is the same misplaced-path defect flagged as Finding 1 on PRs #57, #58, and #59; it
has not been fixed.

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
the implementation is antisymmetric — and explicitly scopes it: *"`Score(board,
White) == -Score(board, Black)` for the material and placement components"* — while
the interface doc (`IHeuristicEvaluator.cs` remarks) states the contract
unconditionally: *"A symmetric implementation is antisymmetric, i.e.
`Score(board, White) == -Score(board, Black)`."* The material + piece-square
components *are* antisymmetric (computed once from White's perspective and negated
for Black). But the mobility term is added as a **non-negative** count for the
requested side only and is never subtracted from the opponent. So for any position
with non-zero mobility:

```
Score(board, White) = Wmat + Wmob            // = -(Bmat) + Wmob
Score(board, Black) = Bmat + Bmob            // = -(Wmat) + Bmob
Score(W) + Score(B) = Wmob + Bmob            // should be 0 under antisymmetry
```

`Wmob + Bmob > 0` for essentially every real position, so
`Score(board, White) != -Score(board, Black)`. The interface's unconditional
antisymmetry contract is violated; the implementation's own doc already admits this
by narrowing the claim to "material and placement components." The unit tests pass
only by coincidence — `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
and `Score_MirroredPosition_ScoresEquallyForMirroredColor` assert *equality* of two
separately-requested scores on symmetric positions (where `Wmob == Bmob`), not the
antisymmetry relationship itself, so they cannot catch this. This is the same class
of defect flagged as Finding 2 on PRs #57, #58, and #59; it has not been fixed.

**How to fix:** Make mobility antisymmetric. Compute both sides' pseudo-legal move
counts and use the difference, e.g.:

```csharp
var ownMobility = CountPseudoLegalMoves(grid, sideToMoveFor: side);
var oppMobility = CountPseudoLegalMoves(grid, sideToMoveFor: Opponent(side));
perspective += MobilityWeight * (ownMobility - oppMobility);
```

(Where `Opponent` flips `PlayerColor`.) Then `Score(W) == -Score(B)` holds for the
full score including mobility, satisfying the interface's unconditional contract.
Also align the class-level XML doc so it no longer narrows the antisymmetry claim to
"material and placement components" only. Add a test that asserts the antisymmetry
invariant directly on an *asymmetric* position (e.g.
`7k/8/8/8/8/8/4P3/4K3 w - - 0 1`: `Score(board, White) == -Score(board, Black)`)
so the contract is actually guarded.

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
defect flagged as Finding 3 on PRs #57, #58, and #59; it has not been fixed.

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

PR #60's three code files are byte-for-byte identical to the copies reviewed under
PRs #57, #58, and #59 (the PR #60 head `07fc8dc` is the same commit those review
verdicts branched from, and the misplaced `.worktrees/...` copies are unchanged).
This change re-submits the same code with the same defects and the same
misplaced-path problem, none of which were remediated. If the intent was to address
the prior reviews, that remediation did not happen; please apply the fixes above
(and the relocation) rather than re-posting the identical diff.

---

## Summary

The change cannot be approved: the deliverable is not wired into the build at all
(Finding 1, blocking), and even once wired in the evaluator violates its own stated
antisymmetry contract (Finding 2) and over-counts pawn mobility with illegal moves
(Finding 3). No code changes were made by this review; remediation should be
dispatched as a separate bounded fix task.
