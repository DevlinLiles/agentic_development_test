# Automated pre-human review pass — PR #10

**Branch:** epic/01M1M4VT5PE5QK94E1MBQJ06RB (head `4ad18b1`) vs base (`cdb78ec`)
**Scope:** 39 files changed, +1391 / −99 — VsAi (AI opponent) game mode end to end.

## Verdict: APPROVE

Clean diff. No actionable correctness, security, or blocking quality issues
found. CI is green and the new behavior is covered by unit + e2e tests.

## Findings

### Correctness — no blocking issues

1. **VsAi creation flow (`GameService.CreateGameAsync(GameMode)`)** — VsAi games
   are created with `Status = Active` and `BlackSlotToken = null`, so the human
   (White) can move immediately. TwoPlayer games keep the original
   `WaitingForPlayer2` path. Backward-compatible parameterless `CreateGameAsync()`
   delegates to `CreateGameAsync(GameMode.TwoPlayer)`. ✅

2. **Inline AI reply (`SubmitMoveAsync` → `GenerateAiReplyAsync`)** — Triggered
   only when `game is { Mode: VsAi, Status: Active, Turn: Black }`, i.e. after a
   non-terminal human White move. Endgame detection from the human move is reused;
   if the human's move already ended the game (checkmate/stalemate/fifty-move) the
   AI turn is correctly skipped. The shared `ApplyMoveToGame` refactor keeps
   turn/`PlyColor`/result assignment consistent between human and AI plies (uses
   `mover`, not the stale `resolvedColor`). ✅

3. **Defensive no-op fallbacks** — If `_aiPlayer` is null, the AI returns
   `NoLegalMoves`/`Cancelled`, or `TryApplyMove` rejects the AI move, the code
   returns without throwing, leaving the human move persisted. Reasonable for an
   MVP; the AI only ever enumerates legal moves so the rejected-move branch is
   genuinely defensive. ✅

4. **`MaterialHeuristicEvaluator` / `HeuristicChessAiPlayer`** — Pure, stateless,
   deterministic. `ScoreForSide` negates White-perspective scores for the Black
   mover. Promotion candidates are evaluated per-piece and the best-scoring one
   kept (tested with a stub evaluator that forces an under-promotion). Tie-break
   is deterministic (score → SAN → from/to → promotion ordinal). ✅

5. **`GetAllLegalMoves` adapter** — Maps `board.Moves()` to `LegalMove` records
   with shared `IsPawnPromotion` heuristic reused by `IsPromotionMove`, so the two
   code paths agree on what counts as a promotion. Legality is validated
   end-to-end by the e2e "AI replies to the player's move" spec (the AI returns a
   legal Black move). ✅

6. **Migration** — Additive `int Mode` column, `nullable: false`,
   `defaultValue: 0` (= `TwoPlayer`), with matching `Down`. Safe for existing
   rows. ✅

### Security — no issues

- The `mode` query parameter is bound via `[FromQuery] GameMode` — an enum, so
  out-of-range values are rejected by model binding rather than injected.
- Client `buildQueryString` uses `URLSearchParams` (proper encoding), null/undefined
  values skipped. No string concatenation of raw user input into URLs.
- No new authz surface: VsAi games still require the White slot token to submit
  moves (`ResolveSlotToken` unchanged). No secret handling changes.

### Quality — minor observations (non-blocking)

- **Missing direct unit test for `GameService.GenerateAiReplyAsync` fallback
  branches** (null AI player, `NoLegalMoves`/`Cancelled`, AI move rejected by
  `TryApplyMove`). The happy path is covered by the e2e spec and the controller
  creation tests, and the AI player itself is well unit-tested, so this is a
  coverage nit rather than a defect. Recommend a follow-up to add focused
  `GameService` VsAi tests, but it does not block this PR.
- `AiSearchOptions.Shallow()` doc comment says "Defaults to a two-ply search" but
  the implementation is 1-ply greedy (`SearchDepthInPlies = 1`); the `Shallow(1)`
  used by `GenerateAiReplyAsync` is consistent with the actual behavior. Cosmetic
  doc mismatch only.

## Note on review submission

This automated pass was produced in a sandboxed worktree where the `gh` CLI and
network review submission are not available in the permitted tool set (only
`git diff/status/log/add/commit` are allowed). The verdict above is the review
that should be posted to PR #10 as a GitHub PR review with state **APPROVE** and
rationale: "Clean diff — VsAi flow, inline AI reply, contracts, client, and
additive migration are correct and covered by unit + e2e tests; no blocking
issues."
