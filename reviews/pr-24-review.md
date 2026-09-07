# Automated Pre-Human Review — PR #24

**Reviewer:** automated review pass (reviewer agent)
**PR head commit:** `415c123` — "Add automated pre-human review verdict for PR #23 (APPROVE)"
**Base:** `07fc8dc` (`main`)
**Diff scope:** 1 new file, +80 lines
  - `reviews/pr-23-review.md` (80 lines) — the automated APPROVE review verdict produced for PR #23

## Verdict: APPROVE

The document under review (`reviews/pr-23-review.md`) is a sound APPROVE verdict on PR #23: it
correctly confirms that `reviews/pr-22-review.md` (PR #23's sole artifact) is itself a sound APPROVE
on PR #22, which in turn correctly endorses PR #21's `REQUEST_CHANGES` call on PR #20's stray empty
`_dump.py`. Every substantive claim the document makes — the PR mapping, the actionable defect, the
antisymmetry re-verification, the dead `sideToMove` output, and the `+68` mobility figure — was
independently re-verified against the real diffs and source and holds. No code changes were made by
this review.

---

## Verification performed

**Diff scope (sanity).** Confirmed `git diff --stat 07fc8dc 415c123` = `reviews/pr-23-review.md`
(80), a single static Markdown file — the sole artifact under review. Each review PR in this chain
branches independently off `main` (`07fc8dc`), so PR #24's diff is exactly its own verdict document.
✓

**PR mapping (re-checked).** The document's cited topology was re-verified:
- PR #20 head = `7b5187d`; `git diff --stat 07fc8dc 7b5187d` = `_dump.py` (0) +
  `reviews/pr-19-review.md` (97). ✓
- PR #22 head = `c2ad404`; `git diff --stat 07fc8dc c2ad404` = `reviews/pr-21-review.md` (57). ✓
- PR #23 head = `57aef37`; `git diff --stat 07fc8dc 57aef37` = `reviews/pr-22-review.md` (73). ✓
The document's "PR head commit: `57aef37`" / "Base: `07fc8dc` (`main`)" are correct and internally
consistent. ✓

**Actionable finding (stray `_dump.py`).** Re-checked: `_dump.py` is a 0-byte empty blob in PR #20's
diff, unreferenced by any source/test/build file, committed to the repo root — a scratch artifact
that should not ship. PR #21's `pr-20-review.md` rendered `REQUEST_CHANGES` for it; the chain the
document under review endorses is sound. ✓

**Antisymmetry invariant (re-checked against source).** Confirmed against
`server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`: `Score` calls `EvaluateForWhite(grid)`
(antisymmetric material + vertically-mirrored piece-square tables, White add / Black subtract), then
`perspective = side == PlayerColor.White ? whiteScore : -whiteScore`, then
`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)`. Because the
mobility term is non-negative and only ever added for the requested side, `Score(b, White)` and
`Score(b, Black)` share a non-negative mobility contribution with the same sign rather than opposite
signs, so `Score(b, White) != -Score(b, Black)` in general. The "antisymmetry violated" conclusion is
correct. ✓

**Dead `sideToMove` output (re-checked).** Confirmed: `TryParsePlacement(..., out PlayerColor
sideToMove)` populates `sideToMove` from `fields[1]`, the sole caller in `Score` binds it via
`out _`, and `sideToMove` is not read anywhere else inside `TryParsePlacement`. Genuinely dead
output. ✓

**`+68` mobility correction (hand-verified against move-counting code).** Computed White mobility at
the start FEN against `CountPawnMoves` / `CountKnightMoves` / `CountSlidingMoves` / `CountKingMoves`:
- Pawns on rank 2: interior six (b2–g2) = single push (1) + double push (1) + two in-bounds diagonals
  (2) = 4 each → 24; corner a2/h2 = 2 pushes + 1 in-bounds diagonal = 3 each → 6; total **30**.
  (Diagonals count even on empty targets — the pseudo-legal relaxation the code applies.)
- Knights b1/g1: 2 each. b1 reaches a3, c3 (empty) and is blocked by the d2 own pawn; g1 reaches f3,
  h3 (empty) and is blocked by the e2 own pawn. → **4**.
- Back-rank rooks/bishops/queen/king: every ray/step is blocked by an own piece on rank 2 or the
  back rank → **0**.
- Total = 34; × `MobilityWeight` (2) = **`Score(start, White) = +68`**. Material+PST `whiteScore` is
  0 at the symmetric start, and Black mobility is symmetric, so `Score(start, Black) = +68` too,
  giving `+68 != -68` — the invariant is broken at the correct magnitude. ✓

**Security.** No concerns. The diff is a static Markdown review document — no executable code, no
I/O, no authz surface, no secret handling, no untrusted-input processing. ✓

---

## Notes (non-blocking, no change requested)

- In its own non-blocking note, the document "corrects" `reviews/pr-22-review.md`'s cross-color sum
  formula `Score(b, White) + Score(b, Black) = 2*MobilityWeight*mob(W)` to
  `2*MobilityWeight*(mob(W) + mob(B))`. The genuinely complete general expression is
  `MobilityWeight*(mob(W) + mob(B))` — a single weight factor applied to the sum of both mobilities;
  `reviews/pr-22-review.md`'s `2*MobilityWeight*mob(W)` is in fact the correct specialization to the
  symmetric start (where `mob(W) == mob(B)`), and the document's "correction" introduces a spurious
  extra factor of two (it would evaluate to 272 vs the true 136 at the start). This does not affect
  the document's verdict or any conclusion — the qualitative "antisymmetry violated" result stands —
  so no change is required. Flagged only so the slip is not propagated further down the review chain.
