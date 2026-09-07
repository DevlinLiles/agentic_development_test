# Automated Pre-Human Review — PR #22

**Reviewer:** automated review pass (reviewer agent)
**PR head commit:** `c2ad404` — "Add automated pre-human review verdict for PR #21 (APPROVE)"
**Base:** `07fc8dc` (`main`)
**Diff scope:** 1 new file, +57 lines
  - `reviews/pr-21-review.md` (57 lines) — the automated review verdict produced for PR #21

## Verdict: APPROVE

The review document under review (`reviews/pr-21-review.md`) correctly endorses PR #21's
`REQUEST_CHANGES` call on PR #20's stray empty `_dump.py`, and every substantive claim it makes —
the actionable defect, the two non-blocking re-verifications, and the `+68` mobility correction —
was independently re-verified against the real diffs and source and holds. No code changes were
made by this review.

---

## Verification performed

**PR mapping (sanity).** Confirmed the recursive review chain so the document's cited commits are
checked against the right PRs:
- PR #20 head = `7b5187d` ("Review PR #19"); `git diff --stat main..7b5187d` = `_dump.py` (0) +
  `reviews/pr-19-review.md` (97).
- PR #21 head = `5bed20d` ("verdict for PR #20 (REQUEST_CHANGES)"); its diff = `reviews/pr-20-review.md`.
- PR #22 head = `c2ad404` ("verdict for PR #21 (APPROVE)"); `git diff --stat main..c2ad404` =
  `reviews/pr-21-review.md` (57), the single file under review here.
The document's "PR head commit: `5bed20d`" and "Re-checked PR #20's real head (`main..7b5187d`)" are
therefore both correct and internally consistent. ✓

**Actionable finding (stray `_dump.py`).** Re-checked `git diff --stat main..7b5187d`: the stray
`_dump.py` is present at 0 bytes (empty blob `e69de29`), unreferenced by any source/test/build file,
and committed to the repo root — a scratch artifact that should not ship. PR #21's
`pr-20-review.md` rendered `REQUEST_CHANGES` for it; the document under review re-verifies this and
endorses it. Sound. ✓

**Re-verification note — antisymmetry invariant.** Confirmed against
`server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`: `Score` computes an antisymmetric
`EvaluateForWhite` (material + mirrored piece-square tables, White add / Black subtract), then
applies `perspective = side == White ? whiteScore : -whiteScore`, and finally adds a one-sided
`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)`. Because the
mobility count is non-negative and only ever added for the requested side,
`Score(b, White) + Score(b, Black) = 2*MobilityWeight*mob(W)` only cancels when both mobilities are
zero; in general `Score(b, White) != -Score(b, Black)`. The "antisymmetry violated" conclusion is
correct. ✓

**Re-verification note — dead side-to-move.** Confirmed: `TryParsePlacement(..., out PlayerColor
sideToMove)` populates `sideToMove` from `fields[1]`, the sole caller in `Score` binds it via
`out _`, and `sideToMove` is not read anywhere else inside `TryParsePlacement`. Genuinely dead
output. ✓

**Non-blocking `+68` correction.** Hand-computed white mobility at the start FEN against the actual
move-counting code:
- Pawns on rank 2: interior six pawns = single push (1) + double push (1) + two diagonals (2) = 4
  each → 24; corner a2/h2 = 2 pushes + 1 in-bounds diagonal = 3 each → 6; total **30**.
- Knights b1/g1: 2 each (a3/c3, f3/h3) → **4**.
- Back-rank rooks/bishops/queen/king: every ray/step is blocked by an own piece → **0**.
- Total = 34; × `MobilityWeight` (2) = **`Score(start, White) = +68`**. Material+PST `whiteScore`
  is 0 at the symmetric start, and black mobility is symmetric, so `Score(start, Black) = +68` too,
  giving `-68 != +68` — the invariant is broken at the correct magnitude. The document's correction
  of the earlier `+40` (= pawn pushes 16 + knights 4 = 20 × 2, which drops the 14 pseudo-legal
  diagonal "attacks" the evaluator counts) is accurate. ✓

**Security.** No concerns. The diff is a static Markdown review document — no executable code, no
I/O, no authz surface, no secret handling, no untrusted-input processing. ✓

---

## Notes (non-blocking, no change requested)

- None. The document's structure, citations, and arithmetic all re-verify cleanly; the one item it
  flags as a correction (`+40` → `+68`) is itself correct, and it explicitly records the corrected
  figure to prevent re-propagation — good practice.
