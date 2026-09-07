# Automated Pre-Human Review — PR #11

**Verdict:** `REQUEST_CHANGES`

**Scope reviewed:** PR head `0203340` vs base. The substantive diff adds the heuristic
position evaluator: `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`,
`server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (+507),
`server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (+108), plus a leftover
`REVIEW_PR_10.md` (see Finding 3). No other production code is altered in this PR.

**Summary:** The evaluator is well-structured and nicely documented, but the piece-square
table lookup is transposed, so placement scores are read from the wrong squares. The PR's
own unit test encodes the *correct* (non-transposed) expectation and would fail under the
implemented indexing. This is a correctness defect that must be fixed before merge.

---

## Findings

### Finding 1 — [Correctness / blocker] Piece-square tables are accessed with swapped indices

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Location:** `PieceSquareValue` (and every call site in `Score` / `EvaluateForWhite`)

**What is wrong:**
The piece-square tables are declared as 2-D array literals whose first index is a **rank**
and whose second index is a **file**. Each inner `{ ... }` group is one rank, laid out in the
standard Michniewski order (group 0 = rank 8 at the top, group 7 = rank 1 at the bottom); e.g.
`PawnTable` group 1 is `{ 50, 50, 50, 50, 50, 50, 50, 50 }` — the rank-7 "about to promote" row.

The code, however, reads the tables as `PawnTable[file, tableRank]` (first index = file,
second = rank), which **transposes** the table. Every piece therefore reads the value intended
for the square reflected across the long diagonal, so placement scoring is wrong for all
non-symmetric tables (pawn, rook, king; the knight/queen/bishop tables happen to be symmetric
enough to mask the error).

**Concrete demonstration (the PR's own test case):**
`HeuristicEvaluatorTests.Score_AfterKnownPawnPush_ChangesScore` plays 1.e4 and asserts
`scoreAfter < scoreBefore`, with the comment claiming "e2=50, e4=25".

With the grid convention used here (grid rank index 0 == rank 1, per `TryParsePlacement`'s
`rankIndex = 7 - fenRank`), a White pawn on e2 has `file = 4`, `rank = 1`, and on e4 `rank = 3`.

- Implemented (transposed) access:
  - e2 → `PawnTable[4, 1]` = group 4 `{ 0, 0, 0, 20, 20, 0, 0, 0 }`[1] = **0**
  - e4 → `PawnTable[4, 3]` = group 4 `[3]` = **20**
  - Placement term changes by **+20**, and mobility also rises, so `scoreAfter > scoreBefore`.
    The assertion `scoreAfter < scoreBefore` **fails**.
- Correct (rank-first) access `PawnTable[tableRank, file]`:
  - e2 → `PawnTable[1, 4]` = group 1 `{ 50, 50, ... }`[4] = **50**
  - e4 → `PawnTable[3, 4]` = group 3 `{ 5, 5, 10, 25, 25, 10, 5, 5 }`[4] = **25**
  - Placement term changes by **−25**, matching the test's stated rationale and yielding the
    expected decrease.

The test's expected values (50, 25) are only consistent with rank-first indexing, confirming
the implementation is the side that is wrong, not the test.

**How to fix:**
Index the tables rank-first. In `PieceSquareValue`, swap the indices to read
`PawnTable[tableRank, file]` (and the same swap for `KnightTable`, `BishopTable`, `RookTable`,
`QueenTable`, `KingMiddleGameTable`). Equivalently, declare the tables file-major — but the
rank-first swap is the smaller, clearer change and matches the data as authored. After the fix,
run `dotnet test` for `ChessMvp.Domain.Tests`; `Score_AfterKnownPawnPush_ChangesScore` and the
mirrored-position test should pass and genuinely exercise the placement term.

---

### Finding 2 — [Quality / CI] The test that catches Finding 1 is not actually validating the change

**File:** `.github/workflows/ci.yml` (server job) and `HeuristicEvaluatorTests.cs`

**What is wrong:**
Finding 1 would be caught by `Score_AfterKnownPawnPush_ChangesScore`, yet CI is reported green.
That means the `ChessMvp.Domain.Tests` assembly is not being executed (or its results are
masked) in CI. The server job runs `dotnet test --no-build || dotnet test`; if the build of the
net10.0-targeted projects fails on the first leg, the `||` fallback re-runs `dotnet test`, and a
real test failure there should redden CI. A green run that does not execute this test indicates
the new behavior is shipping unverified.

**How to fix (verification, not code):**
Confirm in the PR's CI logs that the server test job reports `HeuristicEvaluatorTests` as
discovered and run (look for the test count / "Passed: N"). If the tests are being silently
skipped (e.g. the test project is excluded from `dotnet test`, or the build is failing and the
`||` masks it), fix the CI invocation so the new tests actually run. This both validates
Finding 1's fix and prevents the next regression from slipping through.

---

### Finding 3 — [Hygiene / minor] Leftover review artifact committed into this PR

**File:** `REVIEW_PR_10.md` (added in this PR's diff)

**What is wrong:**
The PR #10 automated-review verdict markdown was committed onto this branch and is part of the
PR #11 diff. It is unrelated to the heuristic-evaluator change and should not ship with it.

**How to fix:**
Remove `REVIEW_PR_10.md` from this branch (it belongs only to PR #10's history). Trivial; do
not let it merge with the feature.

---

### Finding 4 — [Quality / minor] New evaluator is not yet consumed by anything

**File:** `IHeuristicEvaluator.cs` / `HeuristicEvaluator.cs`

**What is wrong:**
`IHeuristicEvaluator` is referenced only by its own implementation and tests; no
`IChessAiPlayer` / `GameService` / DI registration consumes it in this PR, so the new public
abstraction is currently dead code. The interface doc references `IChessAiPlayer`, which is not
present in this diff.

**How to fix:**
If this is intentional scaffolding (evaluator first, AI player in a follow-up PR), note that in
the PR description so reviewers know to expect the consumer later. Otherwise, either land the
consumer in this PR or hold the abstraction until it is used, to avoid merging unused public API
surface.

---

## Notes

- The evaluator's overall design (pure/deterministic, self-contained FEN parsing, graceful
  neutral-zero fallback on malformed input, antisymmetric material/placement, side-relative
  mobility) is sound and well documented. Only the index transposition (Finding 1) is a
  functional blocker.
- No security concerns: the evaluator takes a FEN string, parses it defensively (returns 0 on
  any malformed input rather than throwing), and performs no I/O, DI, or external calls. Input
  is not trusted beyond the parse boundary.

**Recommendation:** Address Finding 1 (blocker) and verify via `dotnet test`; address Findings
2–4 as cleanup. Requesting changes.
