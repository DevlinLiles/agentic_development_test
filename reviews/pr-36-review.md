# Pre-Human Review — PR #36

**Verdict: REQUEST_CHANGES**

PR #36's diff is a single new file, `reviews/pr-35-review.md` — an automated pre-human review of
PR #35 (the heuristic position evaluator). The review's *logic* findings about the evaluator
(antisymmetry violation from a one-sided mobility term, private duplicate `Piece`/`PieceType` types,
and a test that encodes the contract violation instead of guarding it) are accurate and
well-reasoned, and were independently corroborated against the committed source. However, the review
document itself has a material defect that makes its findings non-actionable as written, and it
misses the single most consequential problem in the PR #35 diff. No code was changed by this review
(remediation is dispatched separately).

---

## Finding 1 — Review cites file paths that do not exist in the repository (Quality / Actionability)

**File:** `reviews/pr-35-review.md` — all three findings' `**File:**` headers.

**What is wrong.** Every finding locates the code under the canonical `server/...` tree:

- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

None of these paths exist in the PR head's tree. `git diff <base> <head> -- server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` returns no changes, and the working tree's `server/src/ChessMvp.Domain/Services/` contains only `GameService.cs` and `IGameService.cs`. The files were actually committed under a `.worktrees/<id>/` prefix:

- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

A remediation agent that follows the review's `File:` references will look in `server/src/...`, find
nothing, and be blocked before it can address any of the (correct) logic findings.

**How to fix.** Correct the paths in the review to reflect where the files actually live in the tree,
or — better — fold this into Finding 2 and recommend that the files be relocated to the canonical
`server/...` paths as the first remediation step (see Finding 2).

---

## Finding 2 — Review misses that the PR #35 files are committed outside the solution and are not built or tested (Correctness / Completeness)

**File:** `reviews/pr-35-review.md` — overall; a missing top-priority finding.

**What is wrong.** The review opens with "CI is green, but the implementation violates its own
published interface contract," then proceeds to review the evaluator's internal logic. It never
establishes that the code under review is actually part of the build. It is not. The three files are
committed at `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` — a directory tree rooted under
`.worktrees/`, disjoint from the `server/` tree that holds `ChessMvp.Domain.csproj`,
`ChessMvp.Domain.Tests.csproj`, and `ChessMvp.slnx`. The SDK-style projects compile `**/*.cs`
relative to their own directory (`server/src/ChessMvp.Domain/` and
`server/tests/ChessMvp.Domain.Tests/`), so they do not pick up the worktree-prefixed copies.
Consequently:

- `HeuristicEvaluator.cs` is never compiled — its `IHeuristicEvaluator` implementation is dead in
  this PR.
- `HeuristicEvaluatorTests.cs` is never discovered or executed by `dotnet test`.

This is the most important issue in the PR #35 diff: the evaluator and its tests are orphaned outside
the project structure. It also explains how CI can be green despite the antisymmetry violation the
review correctly identifies — the test that would assert the (non-antisymmetric) behavior
(`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`) never runs. The review treats
"CI is green" as a meaningful signal for this code when, for these files, it is not.

**How to fix.** Add a top-priority finding directing that the three files be moved from
`.worktrees/01m1vy27727029etgw6qp81jdc/server/...` to the canonical
`server/src/...` / `server/tests/...` locations and re-verified to compile and run under the solution,
*before* any of the logic-level findings can be meaningfully addressed or verified. Reframe the "CI
is green" statement to note that the evaluator code is not currently covered by CI until the
relocation is done.

---

## Non-blocking notes

- The review's three logic findings are technically correct against the committed source and do not
  need to be re-derived:
  - **Antisymmetry:** `IHeuristicEvaluator` declares
    `Score(board, White) == -Score(board, Black)` and "a positive score means the position favors
    `side`." The test asserts a symmetric starting position scores positively *and equally* for both
    sides (`whiteScore > 0`, `Assert.Equal(whiteScore, blackScore)`) — impossible under true
    antisymmetry, which for a symmetric position requires `0` with opposite signs. The review's
    diagnosis (mobility counted only for the evaluated side, so each side gets a positive constant)
    is the correct mechanism.
  - **Duplicate types:** the private `Piece` struct and `PieceType` enum at the bottom of
    `HeuristicEvaluator.cs` are confirmed present, parallel to the domain's first-party
    `ChessMvp.Domain.Entities` types already in use via `PlayerColor`.
  - **Test guards the bug:** `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
    asserts the non-antisymmetric behavior, and no antisymmetry-on-asymmetric-position or
    Black-perspective material test exists.
- No security surface was identified in the PR #36 diff (markdown-only) or in the reviewed evaluator
  (FEN handling degrades to `0` on malformed input, as the review notes; the evaluator consumes
  internally-produced FEN, not direct user input).

---

## Summary

| # | Severity | Category | Location |
|---|----------|----------|----------|
| 1 | Major | Quality / Actionability | `reviews/pr-35-review.md` — all `File:` headers cite non-existent `server/...` paths |
| 2 | Blocking | Correctness / Completeness | `reviews/pr-35-review.md` — misses that PR #35 files are orphaned outside the solution (committed under `.worktrees/<id>/`), not built or tested by CI |

Recommend REQUEST_CHANGES: correct the file paths (Finding 1) and add a top-priority finding that the
PR #35 files must be relocated into the `server/...` project structure — and re-verified to compile
and run — before the (correct) logic-level findings can be acted on (Finding 2). The review's
existing logic analysis is sound and can be retained as-is once the paths and the missing finding are
addressed.
