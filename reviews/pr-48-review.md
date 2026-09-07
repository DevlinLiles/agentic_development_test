# Automated Pre-Human Review — PR #48

- **Verdict:** REQUEST_CHANGES
- **Head:** `07fc8dc` — Add heuristic evaluator with material, piece-square tables, and mobility scoring (PR head includes the opponent-selector client slice + the evaluator from `43a5502`)
- **Base:** `cdb78ec` — Auto-fix: HEALTHCHECK Dockerfile safety net
- **Scope:** diff `cdb78ec..07fc8dc` — six client files + one Dockerfile line:
  - `client/src/api/gamesApi.ts` (createGame gains optional `opponentType`, +23/-4)
  - `client/src/components/panels/OpponentTypeSelector.tsx` (new radio group, +72)
  - `client/src/components/panels/OpponentTypeSelector.stories.tsx` (new Storybook stories, +52)
  - `client/src/components/panels/opponentTypeSelector.css` (new styles, +73)
  - `client/src/screens/CreateGameScreen.tsx` (wire selector into create flow, +13/-1)
  - `client/src/types/gameTypes.ts` (`OpponentType`, `CreateGameRequest`, +9)
  - `server/src/ChessMvp.Api/Dockerfile` (1-line change)

CI is green, but the diff ships a user-facing "play against the computer" affordance whose selected value is silently discarded by the server, leaving the feature non-functional end-to-end. Findings below are concrete (file, symbol, what is wrong, how to fix); remediation should be dispatched as a separate bounded fix task. No code was changed by this review.

---

## Finding 1 — Correctness: the "Computer (AI)" selection is silently ignored; solo play never happens

**Files**
- `client/src/components/panels/OpponentTypeSelector.tsx` — `OPPONENT_OPTIONS` exposes an `"Ai"` option labelled **"Computer (AI)"** with description *"Play solo against a basic computer opponent."*
- `client/src/screens/CreateGameScreen.tsx` — `handleCreate` calls `gamesApi.createGame(opponentType)`, forwarding the user's selection.
- `client/src/api/gamesApi.ts` — when `opponentType` is `"Ai"`, `createGame` POSTs `{ opponentType: "Ai" }` as the request body to `/api/games`.
- `server/src/ChessMvp.Api/Controllers/GamesController.cs` — `CreateGame()` (line 23) has **no `[FromBody]` parameter**; it calls `_gameService.CreateGameAsync()` with no arguments.
- `server/src/ChessMvp.Domain/Services/GameService.cs` — `CreateGameAsync()` (line 22) takes **no opponent-type argument**; it always creates a game in `WaitingForPlayer2` status with `BlackSlotToken = null`, i.e. an ordinary two-player human game awaiting a join.

**What is wrong**
The client presents a "Play solo against a basic computer opponent" choice and, when chosen, sends `opponentType: "Ai"` to the API. The server endpoint does not bind or read a request body for game creation, and the domain service has no concept of an opponent type. The body is therefore **silently ignored**, and the result is always a human-vs-human game stuck in `WaitingForPlayer2` — exactly the state that, for a solo player, will never be joined and never start. The headline feature of this PR is non-functional end-to-end: the user clicks "Create Game" expecting an AI opponent and instead gets a game that waits forever for a second human.

This is the central correctness defect of the diff: the contract the UI advertises is not honoured anywhere on the server. (Note: a `HeuristicEvaluator` does exist in the domain, added earlier in this branch at `43a5502`, but it is not referenced by `GameService` or any controller — `grep` for `Heuristic|Ai|opponent|Opponent` under `server/src` returns no matches outside the evaluator's own files — so there is no AI move-making path wired up either.)

**How to fix**
Close the loop on the server. Minimally:
1. Add an `OpponentType` enum to the domain (`Human`, `Ai`) matching the client's `OpponentType` type.
2. Accept it in the create endpoint, e.g. `public async Task<ActionResult<CreateGameResponse>> CreateGame([FromBody] CreateGameRequest request)` (with a default of `Human` when the body/field is absent, preserving the legacy parameterless behaviour the client deliberately keeps for `Human`).
3. Thread it through `IGameService.CreateGameAsync(OpponentType opponentType)` and, when `Ai`, create the game in an active state (auto-fill the black slot / mark solo) and drive AI replies via the existing `HeuristicEvaluator` + `IChessRulesEngine` after each human move (or document explicitly that AI move generation is out of scope for *this* PR and gate the `"Ai"` option off in the UI until a follow-up lands). The current state — UI advertises solo AI play, server silently ignores it — is not acceptable either way.

---

## Finding 2 — Quality/Tests: no automated tests for the new client behavior

**Files**
- `client/src/api/gamesApi.ts` — new `createGame(opponentType?)` branch (sends a body only for non-`Human`).
- `client/src/components/panels/OpponentTypeSelector.tsx` — new interactive component.

**What is wrong**
The only coverage added for the new component is `OpponentTypeSelector.stories.tsx` (Storybook stories). There are **no unit/component tests** for the new behavior this PR introduces:
- `createGame` now has a conditional branch (body sent vs. parameterless POST). There is no test asserting that `opponentType === "Human"`/`undefined` issues a bodyless request while `"Ai"` issues `{ opponentType: "Ai" }` — the exact backward-compatibility invariant the doc comment (lines 17–19) calls out as intentional. A search for `OpponentTypeSelector|createGame|opponentType` across `*.{test,spec}.{ts,tsx}` under `client/src` returns no matches.
- `OpponentTypeSelector` has no test asserting that selecting an option calls `onChange` with the right value, that `disabled` disables the inputs, or that the default selection is `Human`.

**How to fix**
Add component/unit tests (the repo already has a Vitest/RTL-style client test setup): cover (a) `createGame("Ai")` sends a JSON body and `createGame()`/`createGame("Human")` send none; (b) `OpponentTypeSelector` renders both options, calls `onChange` on selection, and honours `disabled`. Stories are not a substitute for executable assertions.

---

## Finding 3 — Quality: dead code / unintegrated evaluator in this PR's branch

**Files**
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`

**What is wrong**
The `HeuristicEvaluator` (introduced earlier in this branch at `43a5502`) is not referenced anywhere in the game flow: `GameService` has no `IHeuristicEvaluator` dependency, no controller invokes it, and there is no AI move-selection path. Combined with Finding 1, the evaluator is currently dead code relative to the feature this PR is meant to deliver.

**How to fix**
Either wire the evaluator into the AI opponent path created by Finding 1's fix, or — if AI move generation is deliberately deferred — do not land a UI affordance for AI play ahead of the engine integration (see Finding 1). As written, the PR adds both a user-facing AI selector and an evaluator but connects neither to the other.

---

## Non-blocking notes (no change required)

- **Backward compatibility is handled thoughtfully on the client:** `createGame` deliberately omits the body for `Human`/`undefined` to preserve the legacy parameterless `POST /api/games` contract (gamesApi.ts lines 17–30). Good instinct — but it is untested (Finding 2) and, more importantly, moot until the server actually consumes the field (Finding 1).
- **The `OpponentTypeSelector` component itself is clean:** controlled radio group, accessible `<fieldset>`/`<legend>`, `disabled` propagated to inputs, stable `key`ed options, no inline styles. The CSS is reasonable and scoped via BEM-ish class names. No injection or unsafe-input concerns in the new client code.
- **`gameTypes.ts` typing is correct:** `OpponentType = "Human" | "Ai"` and `CreateGameRequest { opponentType?: OpponentType }` mirror the intended server enum; the comment noting `default(OpponentType) == "Human"` is accurate for the legacy path.
- **Dockerfile 1-line change** is cosmetic/safety and not security-relevant.
- **Security:** the diff introduces no new untrusted-input parsing on the server (the body that would carry `opponentType` is currently not even bound), no secrets, no authz changes, and no injection surface. The only security-adjacent note is the inverse of Finding 1: once the server binds `opponentType`, it must validate the value against the known enum and reject unknown values with a 400 rather than trusting the client string — flagging here so the fix task handles it.

---

## Summary

The client-side opponent selector is well-built, but the feature it fronts is hollow: the server's create-game endpoint accepts no opponent type, so selecting "Computer (AI)" silently produces a two-player human game that never starts (Finding 1). The accompanying `HeuristicEvaluator` is not wired into any move-making path, leaving it dead relative to this feature (Finding 3). No automated tests cover the new `createGame` branch or the selector's behavior (Finding 2). Requesting changes; recommend a bounded fix task address Findings 1–3 before human review.
