# Automated Pre-Human Review — PR #44

**Verdict: REQUEST_CHANGES**

CI is green, but the diff does not place any code where the build can see it,
and one published contract is violated by the implementation. Two blocking
findings below, plus non-blocking observations. Symbol/line references are to
the PR head tree (`07fc8dc`); the files currently live under a nested worktree
path (see Finding 1), which is itself the root problem.

## Blocking findings

### 1. New source and test files are committed under a nested worktree path — stranded outside the build, never compiled, never tested (Correctness / Quality)

**Files (as added in this PR):**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:** The only copies of `IHeuristicEvaluator.cs`,
`HeuristicEvaluator.cs`, and `HeuristicEvaluatorTests.cs` in this diff live
under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...`. They are NOT present
under the real `server/src/ChessMvp.Domain/...` or
`server/tests/ChessMvp.Domain.Tests/...` trees, and no `.csproj` or
`ChessMvp.slnx` references them (the solution file is unchanged in this PR).
They are therefore dead code: never compiled, never tested, never shipped.
"CI is green" only because the build never sees these files — the green CI is
misleading, not evidence the feature works. This is the same defect flagged as
Finding 2 in the PR #43 review and it has not been addressed.

**Concrete proof the tests were never executed:**
`HeuristicEvaluatorTests.Score_MalformedBoard_ReturnsNeutralZero` includes the
InlineData case `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"` (commented
"missing ranks / malformed") and asserts the result is `0`. That string is in
fact the **valid 8-rank starting placement** (7 slashes ⇒ 8 ranks), so
`HeuristicEvaluator.TryParsePlacement` parses it successfully. The position is
materially and positionally symmetric, so `EvaluateForWhite` returns `0`, but
`Score` then adds `MobilityWeight * CountPseudoLegalMoves(...)` for White.
White has 36 pseudo-legal moves in the starting position under this counter (32
pawn + 4 knight), so `Score` returns `0 + 2 * 36 = 72`, not `0`. The assertion
`Assert.Equal(0, …)` would therefore **fail** if the test were ever run. Green
CI is direct evidence it was never run, which is only possible because the files
are outside the build tree.

**How to fix:**
1. Move the three files into the real solution tree:
   `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`,
   `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
   `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`; remove the
   `.worktrees/...` copies.
2. Add `.worktrees/` to `.gitignore` and run `git rm --cached -r .worktrees` so
   no worktree scaffolding is tracked (still un-gitignored from PR #43).
3. Actually run `dotnet build` / `dotnet test` in `server/` and confirm
   `HeuristicEvaluatorTests` compiles and executes — then fix the failing
   `Score_MalformedBoard_ReturnsNeutralZero` case (replace the
   `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"` input with a genuinely
   malformed board, e.g. 7 ranks or a bad piece char) so the test reflects its
   stated intent.

### 2. Documented antisymmetry invariant is violated by the mobility term (Correctness)

**Files:**
- `.worktrees/.../server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
  — interface `<remarks>` states "A symmetric implementation is antisymmetric,
  i.e. `Score(board, White) == -Score(board, Black)`."
- `.worktrees/.../server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` —
  `Score(...)` adds one-sided mobility:
  `perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);`

**What is wrong:** The mobility term is counted only for the evaluated side and
is always `>= 0`, so for any position with legal moves
`Score(board, White) + Score(board, Black) = (mobWhite + mobBlack) * MobilityWeight > 0`,
i.e. `Score(board, White) != -Score(board, Black)`. Antisymmetry fails whenever
either side can move. The test
`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` asserts both
`whiteScore > 0` and `whiteScore == blackScore` — a truly antisymmetric
function with equal scores would yield `0`, so this test passes *only because*
the invariant is broken; it encodes the bug rather than guarding against it.
The class-level `<remarks>` partially hedges ("antisymmetric … for the material
and placement components") but the published interface contract still promises
full antisymmetry. For the stated consumer (`IChessAiPlayer`, typically a
negamax search that relies on `Score(board, side) == -Score(board, opponent)`),
a non-negamax-compatible score yields an incorrect search.

**How to fix (preferred):** Make the mobility term differential so the score is
truly zero-sum/antisymmetric:
`perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));`
then update `Score_StartingPosition_…` to assert `0` (or drop the `> 0` claim)
and remove the unqualified antisymmetry hedge from the class remarks.
**Alternative:** if one-sided mobility is intentional, correct the interface
XML doc to drop the antisymmetry claim and document that mobility is a
one-sided bonus (and note the negamax incompatibility) — but the differential
form is strongly preferred given the AI-search use case.

## Non-blocking observations

### 3. Pawn mobility is overcounted and inconsistent with other pieces (Quality)

**File:** `.worktrees/.../server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
— `CountPawnMoves`, diagonal-capture loop:
`if (target is null || target.Color != color) { count++; }`.

A pawn counts both diagonals as "moves" even when the target square is empty,
whereas knights, kings, and sliders only count empty-or-enemy squares (a pawn
has no non-capturing diagonal move). This inflates pawn mobility (e.g. each
unblocked pawn gets +2) relative to the other pieces and skews the mobility
tie-breaker. The code comment acknowledges this as a deliberate "pseudo-legal
relaxation," but it makes the term inconsistent with conventional pseudo-legal
move counts. Consider counting pawn diagonals only when an enemy piece is
present (en-passant aside), or documenting the inflation explicitly.

### 4. Feature still not wired end-to-end (Correctness, carryover from PR #43)

This PR adds only the position scorer. There is still no `IChessAiPlayer` /
search implementation that consumes `IHeuristicEvaluator`, and the server still
does not accept/persist `opponentType` (controller, `Game` entity, and
`GameService` are unchanged). The headline "AI opponent" feature therefore
remains non-functional end-to-end. This is arguably out of scope for a
scoped "add heuristic evaluator" PR, but the integration gap should be tracked
explicitly so the scorer is not mistaken for a working AI.

### 5. `.worktrees/` still not gitignored (Quality, pre-existing)

`.worktrees/` is absent from `.gitignore` (flagged in the PR #43 review), which
is why this PR was able to commit source under a `.worktrees/...` path in the
first place. Adding it to `.gitignore` (Finding 1, step 2) prevents a
recurrence.
