# Automated Pre-Human Review — PR #21

**Reviewer:** automated review pass (reviewer agent)
**PR head commit:** `5bed20d` — "Add automated pre-human review verdict for PR #20 (REQUEST_CHANGES)"
**Base:** `07fc8dc` (`main`)
**Diff scope:** 1 new file, +57 lines
  - `reviews/pr-20-review.md` (57 lines) — the automated review verdict produced for PR #20

## Verdict: APPROVE

The review document under review correctly identifies the one real defect in PR #20 (a stray,
empty `_dump.py` committed to the repo root) and renders a sound REQUEST_CHANGES verdict for it;
that actionable finding was independently re-verified against PR #20's actual diff and holds. The
document's non-blocking re-verification of PR #19's findings is qualitatively correct, with one
numerical slip (detailed below) that does not affect the verdict. No code changes were made by this
review.

---

## Verification performed

- **Finding 1 of the reviewed doc (stray `_dump.py`, actionable).** Re-checked PR #20's real head
  (`git diff --stat main..7b5187d`): the diff is exactly `_dump.py` (0 bytes) + `reviews/pr-19-review.md`
  (97 lines). The `_dump.py` blob is empty (`e69de29`), unreferenced by any source/test/build file, and
  its name marks it as a scratch artifact. The REQUEST_CHANGES call to remove it is correct. ✓
- **Re-verification note, Finding 2 (dead side-to-move).** Confirmed against
  `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`: `TryParsePlacement(..., out PlayerColor
  sideToMove)` populates `sideToMove` from `fields[1]`, the sole caller in `Score` discards it via
  `out _`, and mobility is counted for the requested `side` argument. Genuinely dead. ✓
- **Re-verification note, Finding 1 (antisymmetry).** Confirmed the interface contract
  (`IHeuristicEvaluator.cs` `<remarks>`) states `Score(board, White) == -Score(board, Black)`, while the
  implementation adds a one-sided `perspective += MobilityWeight * CountPseudoLegalMoves(grid,
  sideToMoveFor: side)` that is never subtracted for the opponent. The conclusion (antisymmetry
  violated) is correct. ✓ The cited counterexample number is off — see the non-blocking note below.

---

## Notes (non-blocking, no change requested)

- **`+40` starting-position figure is actually `+68`.** The reviewed document's non-blocking note
  endorses PR #19's counterexample as `Score(start, White) = +40`, `Score(start, Black) = +40`,
  "`-40 != +40`", and marks it "valid / Sound ✓". The *structure* of the counterexample is valid
  (the score is positive and equal for both colors, so it cannot be antisymmetric), but the specific
  number is wrong. Hand-computing white mobility at the starting FEN against the actual code:
  - Pawns (rank 2): each interior pawn gets single push (1) + double push (1) + two diagonal
    "attacks" (2) = 4 (×6 = 24); corner pawns a2/h2 get 1+1+1 = 3 (×2 = 6) → **30**. The code's
    pseudo-legal relaxation explicitly counts empty diagonal squares, which the `+40` figure omits.
  - Knights b1/g1: 2 each (a3,c3 and f3,h3) → **4**.
  - Rooks/bishops/queen/king on the back rank: all blocked by own pieces → **0**.
  - Total white mobility = 34; × `MobilityWeight` (2) = **`Score(start, White) = +68`**, and by
    symmetry `Score(start, Black) = +68`, so `-68 != +68` — the invariant is still broken, just with
    the correct magnitude. The `+40` value corresponds to counting only pawn pushes (16) + knights (4)
    = 20 × 2, i.e. it drops the diagonal-capture moves the evaluator actually counts. Recording this
  so the corrected figure is not re-propagated as "verified" by the next review pass; it does not
  change PR #20's verdict, hence non-blocking.
- **Security:** No concerns. The diff is a static Markdown review document — no executable code, no
  I/O, no authz surface, no secret handling, no untrusted-input processing. ✓
