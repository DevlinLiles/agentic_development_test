# Automated pre-human review: PR #83

**Change under review:** `reviews/session-01m60aw1qmj7ffqs2yps5bn7v1-cost-spike-review.md` (new file, +23 lines, single-purpose commit).
**Base:** `d7d3d04` ("Revert 'Add basic heuristic AI opponent for solo play'").
**Scope:** Documentation-only cost-spike review. No source, contract, DTO, endpoint, migration, or client change.

## Scope checked

- Correctness of the review document's factual claims against the actual repo state.
- Quality: consistency with the established cost-spike review precedent (`e30dba4`, session `01m60a0wmfksrb4sva09jdpg19`).
- Security: any secrets, tokens, or unsafe trust in the new file.

## Findings

**No actionable issues.** All checkable claims in the document were verified against the repository and are accurate:

1. **Branch/commit state (item 1) — verified.** Working tree is clean at `d7d3d04` ("Revert 'Add basic heuristic AI opponent for solo play'"); `git log --all` contains no ref or commit referencing session `01m60aw1qmj7ffqs2yps5bn7v1` other than this verdict file itself. Nothing was stranded mid-refactor, so the RESUME-from-clean-baseline conclusion holds.
2. **Refactor target description (item 2) — verified exactly.** `server/src/ChessMvp.Domain/Services/GameService.cs`: `SubmitMoveAsync` begins at line 68, the promotion pre-check (`IsPromotionMove`) at line 89, `_rulesEngine.TryApplyMove` at line 94, `IllegalMoveException` on failure, state mutation (`CurrentFen`, `Turn`, `HalfmoveClock`, `UpdatedUtc`), `MoveNumber` via `game.Moves.Max(...)` at line 102, `Move` append, terminal-status ordering checkmate → stalemate → fifty-move at lines 124/130/136, `SaveChangesAsync` at 143, notifier after. The method is ~90 lines and unreachable except via the human slot-token path, exactly as described.
3. **Epic-story prior art (item 3) — verified.** Commits `10dd77a` (OpponentKind persistence + additive EF migration), `30c58ca` (IComputerOpponent port + Gera.Chess adapter + unit tests), and `73c7097` (Computer games with no Black slot token, discriminator-keyed join denial) exist on `epic-story/01M5Y7VCP3V8Y330720DP4AX9H/01M603W9C8AYWQXW97RKCN9NHR` in that order, with matching content. Minor nit, not blocking: PR #79 has no verdict commit in history, so "PRs #77–#81" is an inclusive range covering five PRs whose verdict commits are #77, #78, #80, #81 — the substantive claim (each named increment was pre-human approved) is correct.
4. **Revert failure-mode claim (item 4) — verified.** `6e3ca6f` touched exactly the files named (`gamesApi.ts`, `CreateGameScreen.tsx`, `StatusPanel.test.tsx`, `useClickToMove.test.ts`, `gameTypes.ts`, plus `CreateGameResponse`/`GameStateResponse` contracts and server-side AI additions) and was reverted by `d7d3d04`. The proposed helper is domain-internal (`GameService.cs` private member + `GameServiceTests.cs`) and does not reproduce the contract-widening failure mode.
5. **Budget hygiene (item 5) — consistent.** 267,130 vs 261,572 tpm is a ~2% overshoot; the target is one file plus tests. Plausible and consistent with the spike-review precedent.
6. **Security — clean.** No secrets, credentials, or unsafe input trust; markdown only.

## Validation by subsequent events

The constraints in the verdict were followed in practice and produced a green result: commit `9c61d9b` ("Extract shared ApplyMovePlyAsync helper so human and computer plies share one path") landed on exactly the boundary this review prescribed (domain-internal, single-purpose commit, `GameServiceTests` green, no client/API surface change) and was approved in PR #82. The review's recommendations are therefore validated, not speculative.

## Recommendation

**APPROVE.** Documentation-only, factually accurate against the repo, follows the established cost-spike review format (`e30dba4`), and its guidance has already been borne out by the approved `9c61d9b` extraction. Nothing here blocks a human reviewer.
