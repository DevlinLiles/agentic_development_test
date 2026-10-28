# Automated Pre-Human Review — PR #85

**Verdict: REQUEST_CHANGES**

**Submission note:** the `submit_pr_review` GitHub submission of this verdict was rejected with `self_review_forbidden` (the submitting identity authored PR #85), which is terminal. Per repository convention, the committed verdict file is the review deliverable, so the verdict is recorded here instead — the same limitation the PR #79 and PR #72–#74 verdict files document.

**Scope reviewed:** the complete one-file diff — the addition of `reviews/pr-83-review.md` (the automated pre-human review verdict for PR #83, +30 lines, commit `3e61f6f`, sole parent `d7d3d04`). PR #85 makes no code, config, lockfile, or test changes; it is purely a review artifact, so this pass audited the verdict file for factual accuracy against the real repository history, for quality/formatting consistency with the verdict-file precedent, and for security (secrets, tokens, unsafe input trust).

## Methodology

PR #85's only content is a review of the cost-spike review artifact `reviews/session-01m60aw1qmj7ffqs2yps5bn7v1-cost-spike-review.md` (`743b043`, sole parent `d7d3d04`), so this pass re-verified the file's material claims against the actual repository state at `d7d3d04` and against the full ref set reachable via `git log --all`.

## Findings

### Finding 1 — incorrect repository-state claim inside a certification artifact (primary)

**File:** `reviews/pr-83-review.md`, item 3 in "Findings" (line 19 of the new file), the "Minor nit, not blocking" parenthetical.

**What is wrong:** the file asserts "**PR #79 has no verdict commit in history**, so 'PRs #77–#81' is an inclusive range covering five PRs whose verdict commits are #77, #78, #80, #81." That is false: PR #79's verdict commit **exists** — `d8bc7a7` "Add automated pre-human review verdict for PR #79 (REQUEST_CHANGES)" (sole parent `d7d3d04`, committed 2026-10-28 04:23:16 UTC, i.e. three minutes after this file's own commit `3e61f6f` at 04:20:27).

The claim was accurate at authoring time — a race with the parallel PR #79 reviewer, not a research error — but the file states it as an unqualified present-tense fact while its own preamble asserts "**All checkable claims in the document were verified against the repository and are accurate**." A human reviewer reading it now is told the PR #79 review record is missing when it exists and is a REQUEST_CHANGES. The false premise also feeds the endorsement that follows it: with `d8bc7a7` present, "PRs #77–#81" contains five verdict commits (#77, #78, #79, #80, #81), and #79's is a REQUEST_CHANGES, so the range can no longer be summarized as "verdict commits are #77, #78, #80, #81."

The substantive point underneath it does survive: `d8bc7a7`'s diff is exactly one new file, `reviews/pr-76-review.md` (+49 lines), and its own recommendation is "no code, config, lockfile, or test remediation is implicated," so the claim that each of `10dd77a`/`30c58ca`/`73c7097` was pre-human approved (via #77/#78/#80/#81) remains correct. What does not survive is the "PR #79 has no verdict commit in history" statement of repository state.

**Fix:** reword the nit to the current facts, e.g.: "Minor nit, not blocking: PR #79's verdict commit is `d8bc7a7` (REQUEST_CHANGES), but it reviewed only the docs artifact `reviews/pr-76-review.md` and implicates no code, config, lockfile, or test remediation, so the substantive claim — each named increment was pre-human approved — is correct."

### Finding 2 — imprecise absolute claim about `git log --all` (minor, non-blocking)

**File:** `reviews/pr-83-review.md`, item 1 in "Findings" (line 17 of the new file).

**What is wrong:** "`git log --all` contains no ref or commit referencing session `01m60aw1qmj7ffqs2yps5bn7v1` other than this verdict file itself." The commit `743b043` — the artifact under review — references the session in both its subject ("Add cost-spike review verdict for session 01m60aw1qmj7ffqs2yps5bn7v1 (RESUME)") and its content. The intended meaning (no stranded work branch) is correct and matches what the reviewed artifact actually claims ("`task/01m60aw1qmj7ffqs2yps5bn7v1/dev` resolves to no revision"), but as written the sentence is literally false and is the same unqualified-claim class as Finding 1.

**Fix:** scope it, e.g. "...other than the reviewed artifact (`743b043`) and this verdict file; in particular `task/01m60aw1qmj7ffqs2yps5bn7v1/dev` resolves to no revision, so nothing was stranded mid-refactor."

### Severity rationale

Minor in isolation — a two-sentence repository-state inaccuracy in a markdown review record; nothing here affects any code, config, lockfile, or test, and the file's APPROVE verdict on the cost-spike artifact remains substantively sound. But per the standard this chain itself set — PR #73's REQUEST_CHANGES for "an incorrect repository-state claim inside a certification artifact that downstream verdicts cite as precedent," reaffirmed by `d8bc7a7`, which held that a verdict file should not merge with a claim that misstates what the history contains, on the stated ground that "these verdicts cite each other's figures as precedent, so a wrong figure propagates" — the line should be corrected before a human reviewer relies on it.

## What checked out (directly re-verified in this pass)

- **Diff fidelity — verified.** `git diff d7d3d04..3e61f6f` is exactly one new file `reviews/pr-83-review.md`, +30 lines; `3e61f6f`'s sole parent is `d7d3d04` — matching the supplied PR #85 diff exactly, so the PR #85 range contains no code, config, lockfile, or test changes.
- **GameService.cs line-number claims — verified exactly.** At `d7d3d04`, `server/src/ChessMvp.Domain/Services/GameService.cs`: `SubmitMoveAsync` begins at line 68; the `IsPromotionMove` pre-check at line 89; `_rulesEngine.TryApplyMove` at line 94; `IllegalMoveException` on `!result.IsLegal`; state mutation (`CurrentFen`, `Turn`, `HalfmoveClock`, `UpdatedUtc`); `MoveNumber` via `game.Moves.Max(...)` at line 102; the `Move` append; terminal-status ordering checkmate → stalemate → fifty-move at lines 124/130/136; `SaveChangesAsync` at line 143; notifier after. The block is reachable only through the human slot-token path (`ResolveSlotToken` precedes it), exactly as described.
- **Epic-story prior art — verified.** `10dd77a` ("Persist opponent kind with OpponentKind enum, EF mapping and additive migration"), `30c58ca` ("Add IComputerOpponent port with Gera.Chess adapter, DI registration and unit tests"), and `73c7097` ("GameService: create Computer games with no Black slot token, keep join denial discriminator-keyed") exist in that order with matching subjects, and verdict commits `4b87954` (#77), `0d6b1ab` (#78), `c1bde19` (#80), `a8fd2cd` (#81) all exist.
- **Revert failure-mode claim — verified.** `6e3ca6f` touched exactly the files named (`client/src/api/gamesApi.ts`, `CreateGameScreen.tsx`, `StatusPanel.test.tsx`, `useClickToMove.test.ts`, `gameTypes.ts`, plus `CreateGameResponse.cs`/`GameStateResponse.cs` contracts and the server-side AI additions — 23 files, +913/−33), and `d7d3d04` reverted exactly that set (+33/−913).
- **Validation-by-subsequent-events claim — verified.** `9c61d9b` ("Extract shared ApplyMovePlyAsync helper so human and computer plies share one path") touches only `server/src/ChessMvp.Domain/Services/GameService.cs` (64 lines changed, +51/−13) — domain-internal, no client/API/contract/migration change, matching the boundary this review prescribed — and `0c9e455` (PR #82, APPROVE) exists.
- **Budget-hygiene figures — verified.** 267,130 vs 261,572 tpm is a ~2.1% overshoot, and the figures match the reviewed artifact's own statement; consistent with the `e30dba4` cost-spike precedent.
- **Security — clean.** Markdown only; no secrets, credentials, tokens, or unsafe input trust in the new file.

Claims about the paused session's internal state (which commits it had considered, whether writes had landed) are not derivable from history and were taken as the artifact's own report; they are consistent with the clean-at-`d7d3d04` working tree observed here.

## Recommendation

REQUEST_CHANGES, limited to the documentation correction on line 19 of `reviews/pr-83-review.md` (and optionally the line-17 scoping fix). Apply the one-sentence edit and re-submit; no code, config, lockfile, or test remediation is implicated by PR #85. The APPROVE substance of the PR #83 verdict on the cost-spike artifact is otherwise sound and its guidance was borne out by the approved `9c61d9b` extraction.
