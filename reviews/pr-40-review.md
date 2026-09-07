# Automated pre-human review — PR #40

**PR:** #40 — Add automated pre-human review verdict for PR #39 (REQUEST_CHANGES)
**Head:** `29f6245` (diff against base `07fc8dc`)
**Verdict:** APPROVE

## Scope reviewed

The PR diff adds a single new file (76 insertions, no edits to existing code):

- `reviews/pr-39-review.md` — the automated pre-human review verdict for PR #39 ("Add heuristic evaluator with material, piece-square tables, and mobility scoring").

This PR is itself a review-verdict artifact: its entire content is the markdown review of PR #39. Reviewed for correctness, quality, and security. There is no security surface (static markdown, no executable code, no secrets, no external/untrusted input handling).

## Verification of the verdict's technical claims

The verdict flags a broken antisymmetry invariant in the heuristic evaluator. Each claim was checked against the actual source under review (PR #39 head `07fc8dc`, read from the linked worktree):

1. **Interface advertises antisymmetry** — `IHeuristicEvaluator.cs` `<remarks>` states `Score(board, White) == -Score(board, Black)`. Confirmed verbatim.
2. **Material + piece-square components are antisymmetric** — `EvaluateForWhite(grid)` is summed once (White assets positive, Black negative) and negated for Black (`perspective = side == White ? whiteScore : -whiteScore`). Correct.
3. **Mobility term breaks antisymmetry** — `perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)` adds a per-side, non-negated term. The verdict's algebra holds: `Score(W) + Score(B) = W·(mobW + mobB) ≠ 0` whenever either side has a pseudo-legal move, so `Score(W) ≠ -Score(B)`. Verified against `HeuristicEvaluator.Score`.
4. **Class docs half-acknowledge the limitation** — the class-level `<remarks>` qualifies antisymmetry to "the material and placement components," contradicting the unconditional interface contract. Confirmed.
5. **No test guards the invariant on an asymmetric position** — every test position is symmetric (the starting position; a color-mirrored king+pawn pair); none asserts `Score(board, White) == -Score(board, Black)` on an asymmetric board. Confirmed in `HeuristicEvaluatorTests.cs`.
6. **Diff metadata accurate** — "646 insertions, no edits to existing code" across the three named files matches `git diff 43a5502 07fc8dc --stat`. Confirmed.

All findings are real, correctly localized (file + line + quoted code), and the verdict appropriately prescribes remediation without applying fixes — consistent with the review task's no-fix constraint. The cross-reference to the same invariant class previously flagged on PR #19 is accurate (`7b5187d`: "antisymmetry invariant broken").

## Non-blocking notes (do not affect approval)

- Finding #1 frames the break as occurring "on any asymmetric board." Strictly, the invariant fails whenever either side has a nonzero pseudo-legal move count — including the symmetric starting position, where `mobW = mobB = 20`, `whiteScore = 0`, so `Score(W) = Score(B) = 40` and `Score(W) ≠ -Score(B)` (40 ≠ −40). The core conclusion is correct; the framing is slightly conservative rather than wrong and does not undermine the finding.
- Verdict line-number references are content-accurate; the quoted code makes each finding unambiguous even if an off-by-one line drift occurs on future rebases.

## Verdict

APPROVE — the verdict markdown is technically accurate (every finding verified against the heuristic evaluator source), well-referenced, and correctly scoped; the diff is clean.
