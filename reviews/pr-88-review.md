# Automated Pre-Human Review — PR #88

**Verdict: REQUEST_CHANGES**

**Submission note:** the `submit_pr_review` GitHub submission of this verdict was rejected with `self_review_forbidden` (the submitting identity authored PR #88), which is terminal. Per repository convention, the committed verdict file is the review deliverable, so the verdict is recorded here instead — the same limitation the PR #85, PR #79 and PR #72–#74 verdict files document.

**Scope reviewed:** the complete one-file diff — the addition of `reviews/pr-85-review.md` (the automated pre-human review verdict for PR #85, +53 lines, commit `8fb3164`, sole parent `d7d3d04`). PR #88 makes no code, config, lockfile, or test changes; it is purely a review artifact, so this pass audited the verdict file for factual accuracy against the real repository history, for quality/formatting consistency with the verdict-file precedent, and for security (secrets, tokens, unsafe input trust).

## Methodology

PR #88's only content is a review of the PR #83 verdict artifact `reviews/pr-83-review.md` (`3e61f6f`, sole parent `d7d3d04`) — itself a review of the cost-spike artifact `reviews/session-01m60aw1qmj7ffqs2yps5bn7v1-cost-spike-review.md` (`743b043`) — so this pass re-verified the file's material claims against the actual repository state at `d7d3d04` and the full ref set reachable via `git log --all`.

## Findings

### Finding 1 — incorrect repository-state claim inside a certification artifact (primary)

**File:** `reviews/pr-85-review.md`, Finding 1, third paragraph (line 23 of the new file).

**What is wrong:** the file asserts "**`d8bc7a7`'s diff is exactly one new file, `reviews/pr-76-review.md` (+49 lines)**." That is false. `git diff --stat d7d3d04..d8bc7a7` is exactly one new file **`reviews/pr-79-review.md` (+49 lines)**. `reviews/pr-76-review.md` (+40 lines, added by `34eb4c5`) is the PR #76 head — it is the artifact the PR #79 verdict *reviews*, not what that verdict *adds*. The +49 figure belongs to `pr-79-review.md`.

The confusion compounds one paragraph later, in the file's proposed replacement text, which reads "it reviewed only the docs artifact `reviews/pr-76-review.md`." Read immediately after the misstatement, it implies the same file is both what `d8bc7a7` adds and what it reviews, and a remediation task dispatched off this file would inherit the wrong filename. The substantive point underneath it does survive: `d8bc7a7`'s diff is a single new docs-only verdict file (+49 lines) whose own recommendation is that "no code, config, lockfile, or test remediation is implicated," so the file's conclusion — that PR #79's verdict does not disturb the "each named increment was pre-human approved" claim — remains correct. What does not survive is the filename.

**Fix:** reword to: "`d8bc7a7`'s diff is exactly one new file, `reviews/pr-79-review.md` (+49 lines), which itself reviews the docs artifact `reviews/pr-76-review.md` (+40 lines, added by `34eb4c5`) and implicates no code, config, lockfile, or test remediation, so the substantive claim — each named increment was pre-human approved — is correct."

### Finding 2 — misattributed quotation in the severity rationale (minor, non-blocking)

**File:** `reviews/pr-85-review.md`, "Severity rationale" (line 37 of the new file).

**What is wrong:** the quoted standard "an incorrect repository-state claim inside a certification artifact that downstream verdicts cite as precedent" is attributed to "PR #73's REQUEST_CHANGES," but that exact phrase does not appear in `pr-73-review.md` (`452cfce`) — it originates in `d8bc7a7`'s severity rationale, which characterizes PR #73's holding in those words. The adjacent paraphrase — "reaffirmed by `d8bc7a7`, which held that a verdict file should not merge with a claim that **misstates what the history contains**" — also drifts from `d8bc7a7`'s actual holding, which is that a verdict file should not merge with a claim that "misstates what the **reviewed artifact** contains." The inner quotation "these verdicts cite each other's figures as precedent, so a wrong figure propagates" *is* in `d8bc7a7` and is quoted accurately. The intended meaning (this chain holds certification-accuracy claims to a high standard) is correct; the provenance of the outer quote is not.

**Fix:** attribute the phrase to `d8bc7a7`'s characterization of the PR #73 standard, or quote `pr-73-review.md`'s actual wording ("A wrong figure should be corrected before merge rather than merged as-is and propagated").

### Severity rationale

Minor in isolation — a filename misattribution plus a misattributed quotation in a markdown review record; nothing here affects any code, config, lockfile, or test, and the file's REQUEST_CHANGES verdict on PR #85 remains substantively sound (both findings it certifies against `pr-83-review.md` are genuine, re-confirmed below). But per the standard this chain itself set — PR #73's REQUEST_CHANGES for an incorrect repository-state claim inside a certification artifact that downstream verdicts cite as precedent, reaffirmed by `d8bc7a7` on the stated ground that "these verdicts cite each other's figures as precedent, so a wrong figure propagates" — the line should be corrected before a human reviewer or a remediation task relies on it.

## What checked out (directly re-verified in this pass)

- **Diff fidelity — verified.** `git diff --stat d7d3d04..8fb3164` is exactly one new file `reviews/pr-85-review.md`, +53 lines, sole parent `d7d3d04` — matching the supplied PR #88 diff exactly, so the PR #88 range contains no code, config, lockfile, or test changes.
- **The PR #85 chain fidelities — verified.** `git diff --stat d7d3d04..3e61f6f` is exactly one new file `reviews/pr-83-review.md`, +30 lines, sole parent `d7d3d04`; `git diff --stat d7d3d04..743b043` is exactly one new file `reviews/session-01m60aw1qmj7ffqs2yps5bn7v1-cost-spike-review.md`, +23 lines — all matching the file's stated scopes.
- **Finding 1's core defect against `pr-83-review.md` — independently confirmed.** `d8bc7a7` ("Add automated pre-human review verdict for PR #79 (REQUEST_CHANGES)") exists with sole parent `d7d3d04`, committed 2026-10-28 04:23:16 UTC — 2m49s after `3e61f6f` at 04:20:27 — so the "PR #79 has no verdict commit in history" sentence at line 19 of `pr-83-review.md` is indeed now false, exactly as the reviewed file claims.
- **The line-19 and line-17 citations — verified exactly.** Both quoted passages appear verbatim at those line numbers in the committed +30-line `pr-83-review.md`.
- **Finding 2's logic against `pr-83-review.md` — confirmed.** `743b043`'s subject ("Add cost-spike review verdict for session 01m60aw1qmj7ffqs2yps5bn7v1 (RESUME)") and its content both reference the session, so the "no ref or commit referencing session ... other than this verdict file itself" sentence at line 17 is indeed literally false, exactly as claimed.
- **GameService.cs line-number claims — verified exactly.** At `d7d3d04`, `server/src/ChessMvp.Domain/Services/GameService.cs`: `SubmitMoveAsync` begins at line 68; the `IsPromotionMove` pre-check at line 89; `_rulesEngine.TryApplyMove` at line 94; `IllegalMoveException` on `!result.IsLegal`; state mutation (`CurrentFen`, `Turn`, `HalfmoveClock`, `UpdatedUtc`); `MoveNumber` via `game.Moves.Max(...)` at line 102; the `Move` append; terminal-status ordering checkmate → stalemate → fifty-move at lines 124/130/136; `SaveChangesAsync` at line 143; notifier after; `ResolveSlotToken` precedes the block, so it is reachable only through the human slot-token path.
- **Epic-story prior art — verified.** `10dd77a`, `30c58ca` and `73c7097` exist with matching subjects, and verdict commits `4b87954` (#77), `0d6b1ab` (#78), `c1bde19` (#80), `a8fd2cd` (#81) all exist as APPROVEs.
- **Revert failure-mode claim — verified.** `6e3ca6f` touched exactly the 23 files named (+913/−33), and `d7d3d04` reverted exactly that set (+33/−913).
- **Validation-by-subsequent-events claim — verified.** `9c61d9b` touches only `server/src/ChessMvp.Domain/Services/GameService.cs` (+51/−13) — domain-internal, no client/API/contract/migration change — and `0c9e455` (PR #82, APPROVE) exists.
- **Budget-hygiene figures — verified.** 267,130 vs 261,572 tpm is a ~2.1% overshoot.
- **Security — clean.** Markdown only; no secrets, credentials, tokens, or unsafe input trust in the new file.

Claims about the paused session's internal state (which commits it had considered, whether writes had landed) are not derivable from history and were taken as the artifact's own report; they are consistent with the clean-at-`d7d3d04` working tree observed here.

## Recommendation

REQUEST_CHANGES, limited to the one-phrase documentation correction on line 23 of `reviews/pr-85-review.md` (and optionally the line-37 provenance fix). Apply the edit and re-submit; no code, config, lockfile, or test remediation is implicated by PR #88. The REQUEST_CHANGES substance of the PR #85 verdict on `pr-83-review.md` is otherwise sound and both of its findings are genuine.
