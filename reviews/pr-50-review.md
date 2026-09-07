# Automated Pre-Human Review — PR #50

**Verdict: APPROVE**

PR #50 adds a single file, `reviews/pr-49-review.md`, recording the automated pre-human review
verdict (REQUEST_CHANGES) for PR #49's heuristic evaluator. The document is a static markdown
artifact with no executable code, so it has no correctness/security surface of its own; the review
below verifies that the verdict's *content* is accurate against the actual codebase. Every claim in
the document checks out, so the verdict is approved.

---

## Scope of this review

- **Diff reviewed:** `reviews/pr-49-review.md` (new file, +148 lines), PR head `25a720b` vs base
  `main` (`07fc8dc`). This is the only file changed by PR #50; the heuristic evaluator code itself
  lives on `main` and is PR #49's subject, already covered by the document under review.
- **Lens:** Since the change is a review verdict document, the meaningful review is whether its
  findings are correct, concrete, and free of misleading statements — not whether the *evaluated*
  code is correct (that is the document's job, and is dispatched separately for remediation).

---

## Verification of the document's findings (all confirmed accurate)

1. **Finding 1 — files committed outside the tree:** CONFIRMED. The three new source files exist
   only under `.worktrees/01m1vy27727029etgw6qp81jdc/server/...`; the canonical
   `server/src/ChessMvp.Domain/Abstractions/`, `server/src/ChessMvp.Domain/Services/`, and
   `server/tests/ChessMvp.Domain.Tests/` directories do **not** contain
   `IHeuristicEvaluator.cs` / `HeuristicEvaluator.cs` / `HeuristicEvaluatorTests.cs`. No `.csproj`
   globs the worktree path, so the code is not compiled and the tests do not run — the green CI
   signal is indeed vacuous for this change. The cited paths and the fix (move to repo-root
   `server/...`, drop the stray copies, confirm `dotnet build`/`dotnet test` actually execute the
   new tests) are correct.

2. **Finding 2 — antisymmetry contract broken by the mobility term:** CONFIRMED.
   `IHeuristicEvaluator` documents `Score(board, White) == -Score(board, Black)`. The material +
   piece-square component (`EvaluateForWhite` negated for Black) is antisymmetric, but `Score`
   then **adds** `MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)` — a strictly
   non-negative term for the requested side. For a color-symmetric position (e.g. the start
   position) both sides get `+M·m`, giving `Score(start,White) == Score(start,Black) > 0`, which
   contradicts the documented `White == -Black`. The test
   `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` asserts exactly this
   equal-positive (buggy) behavior rather than the contract. The proposed fix
   (`ownMobility - opponentMobility`) and the note that the mirrored/centralized-queen tests stay
   valid are correct.

3. **Finding 3 — dangling `<see cref="IChessAiPlayer"/>` and no integration:** CONFIRMED. No
   `IChessAiPlayer` type exists anywhere in the repo (only `IChessRulesEngine`, `IGameRepository`,
   `IGameNotifier`, `MoveApplicationResult`, `MoveFailureReason` under `Abstractions`), so the two
   `<see cref="IChessAiPlayer"/>` references in the interface doc are unresolved (CS1574 once in
   the build). `HeuristicEvaluator` is not registered in DI and is called by nothing. The finding
   and its two remediation options are accurate.

4. **Finding 4 — pawn mobility counts empty diagonals:** CONFIRMED. `CountPawnMoves`'s diagonal
   loop increments when `target is null`, crediting each pawn 2 phantom "moves" even with nothing
   to capture. Correctly flagged as a minor semantic/accuracy issue.

5. **Finding 5 — dead `sideToMove` out-parameter:** CONFIRMED. `TryParsePlacement` parses
   side-to-move into an `out` that `Score` discards via `out _`. Correctly flagged as minor dead
   work.

6. **Non-issues section:** CONFIRMED. `Score` is a pure function over a FEN string with no I/O,
   injection, or secret-handling surface; malformed input degrades to `0` rather than throwing;
   `TryParsePlacement` bounds/rank/file checks are sound.

---

## Quality of the document

- Findings are concrete (file, line/method, what is wrong, how to fix) and severity-tagged
  (CRITICAL / MAJOR / MAJOR / MINOR / MINOR), with a clear headline blocker and a "non-issues"
  section documenting what was checked and dismissed. This is the expected shape for an automated
  pre-human review verdict.
- No accidental complexity, no dead content, no misleading statements. The one environment-specific
  detail (the literal worktree id in the paths) is accurate to the actual committed locations and
  is necessary to identify the stray files.

## Security

Static markdown only — no injection, authz, secret-handling, or unsafe-input-trust surface. The
embedded worktree path is an internal naming detail, not a credential or sensitive identifier.

---

## Summary

The PR #49 review verdict document is accurate: all five findings reproduce against the real
codebase, the non-issues checks are correct, and the structure/severity labeling is appropriate.
The document is ready for a human reviewer to act on (its own REQUEST_CHANGES verdict against PR #49
stands). Approving PR #50.
