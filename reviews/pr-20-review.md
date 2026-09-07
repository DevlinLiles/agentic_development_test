# Automated Pre-Human Review — PR #20

**Reviewer:** automated review pass (reviewer agent)
**PR head commit:** `7b5187d` — "Review PR #19: REQUEST_CHANGES (antisymmetry invariant broken, dead side-to-move code)"
**Base:** `07fc8dc` (`main`)
**Diff scope:** 2 new files, +97 lines
  - `reviews/pr-19-review.md` (97 lines) — the automated review verdict produced for PR #19
  - `_dump.py` (0 lines) — empty file at repository root

## Verdict: REQUEST_CHANGES

CI is green. The substantive content of this PR — the PR #19 review verdict in
`reviews/pr-19-review.md` — is well-structured and its technical analysis is sound (see
the non-blocking note below for a re-verification). However the PR also commits a stray,
empty `_dump.py` to the repository root that has no purpose, no caller, and no test
coverage; it should not be merged. One actionable finding below. No code changes were
made by this review; remediation should be dispatched as a separate bounded fix task.

---

## Finding 1 — Stray empty file committed to repo root (quality)

**File:** `_dump.py`
**Lines:** entire file (0 bytes — empty blob `e69de29`).

**What is wrong:**
`_dump.py` is added at the repository root and is completely empty. It is not referenced
by any source file, test, build script, or configuration, and its name (`_dump.py`)
indicates it is an accidental scratch/dump artifact rather than an intentional module.
Committing empty scratch files pollutes the repository root and establishes a precedent
for merging unintended artifacts into `main`.

**How to fix:**
- Remove `_dump.py` from the PR (delete the file and drop it from the commit). No code
  depends on it, so removal is safe and behavior-preserving.

---

## Notes (non-blocking, no change requested)

- **`reviews/pr-19-review.md` correctness check.** The verdict's two findings were
  re-verified against the PR #19 source (`server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`):
  - *Finding 1 (antisymmetry invariant):* The mobility term
    `perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)`
    is computed one-sided for the requested `side` and added (never subtracted for the
    opponent), while the interface contract (`IHeuristicEvaluator.cs`) claims
    `Score(board, White) == -Score(board, Black)`. The starting-position counterexample
    (`Score(start, White) = +40`, `Score(start, Black) = +40`, so
    `-Score(start, Black) = -40 != +40`) is valid. Sound. ✓
  - *Finding 2 (dead side-to-move code):* `TryParsePlacement` populates an
    `out PlayerColor sideToMove` from the FEN token, but the sole caller in `Score`
    discards it via `out _`, and mobility is counted for the requested `side` argument
    instead. Genuinely dead. ✓
  The verdict's REQUEST_CHANGES call on PR #19 is justified.
- **Security:** No concerns. The diff is a static Markdown document and an empty file —
  no executable code, no I/O, no authz surface, no secret handling, and no untrusted-input
  processing. ✓
