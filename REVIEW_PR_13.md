# Automated Pre-Human Review — PR #13

**Verdict:** `REQUEST_CHANGES`

**Scope reviewed:** PR head `07fc8dc` (branch `task/01m1x0mwfbg70wefn9h5h0nq69/reviewer`)
diffed against its merge base `cdb78ec` on `main`. The substantive diff (excluding
`package-lock.json` and two stray one-line `.worktrees/<id>` pointer files):

```
client/.storybook/main.ts                          |  11 +
client/.storybook/preview.ts                       |   9 +
client/package.json                                |   8 +-
client/src/api/gamesApi.ts                         |  23 +-
.../panels/OpponentTypeSelector.stories.tsx        |  52 +
.../src/components/panels/OpponentTypeSelector.tsx |  72 +
.../src/components/panels/opponentTypeSelector.css |  73 +
client/src/screens/CreateGameScreen.tsx            |  13 +-
client/src/types/gameTypes.ts                      |   9 +
.worktrees/<id>/server/.../Abstractions/IHeuristicEvaluator.cs        |  31 +
.worktrees/<id>/server/.../Services/HeuristicEvaluator.cs             | 507 +
.worktrees/<id>/server/.../tests/.../HeuristicEvaluatorTests.cs       | 108 +
```

CI is green, but the green is not meaningful here: the green runs never compiled or
executed the new server code, and the client test/CI gate has no coverage for the new
opponent-selection behavior, so the contract defect below was not caught. Findings below.

---

## Findings

### Finding 1 — [Correctness / blocker] Server "feature" is committed outside the solution tree and is never built, tested, or wired in

**Files:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (new, +31)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (new, +507)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (new, +108)

**What is wrong:**
All three new server-side files are added under the path prefix
`.worktrees/01m1vy27727029etgw6qp81jdc/server/...`, i.e. inside a Git worktree working
directory that was accidentally tracked, **not** under the repository's real
`server/src/ChessMvp.Domain/...` and `server/tests/ChessMvp.Domain.Tests/...` trees.

Verification against the checked-out tree confirms this:
- `server/src/ChessMvp.Domain/Services/` contains only `GameService.cs` and
  `IGameService.cs` — no `HeuristicEvaluator.cs`.
- `server/src/ChessMvp.Domain/Abstractions/` contains no `IHeuristicEvaluator.cs`.
- A repository-wide search for `IHeuristicEvaluator` / `HeuristicEvaluator` returns **zero
  matches** in the real source tree.
- `.gitignore` does **not** exclude `.worktrees/`, so these orphan files are tracked and
  will merge into `main` as dead, unreachable-from-the-solution files.

Because they live outside `server/` and outside `ChessMvp.slnx`, they are never referenced
by any `.csproj`, so `dotnet build` / `dotnet test` for the server never compiles or runs
them. That is exactly why CI is green despite this PR containing a brand-new 507-line
service plus tests: the green signal is reporting on a tree that does not include this code.

**How to fix:**
Move the three files to their correct locations:
- `.worktrees/<id>/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
  → `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/<id>/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
  → `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/<id>/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`
  → `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then add `.worktrees/` to `.gitignore` to prevent recurrence, and re-run `dotnet build` +
`dotnet test` from `server/` to confirm the evaluator actually compiles and its tests run.
Remove the two stray top-level `.worktrees/<id>` one-line pointer files from this diff as
well.

---

### Finding 2 — [Correctness / blocker] Client sends `opponentType` to a server endpoint that does not accept it; AI selection is silently ignored

**Files:**
- `client/src/api/gamesApi.ts` — `createGame(opponentType)` now sends
  `POST /api/games` with body `{ opponentType }` when `opponentType === "Ai"`.
- `server/src/ChessMvp.Api/Controllers/GamesController.cs` — `CreateGame()` is still
  parameterless: `public async Task<ActionResult<CreateGameResponse>> CreateGame()` and
  calls `_gameService.CreateGameAsync()` with no opponent/mode argument.

**What is wrong:**
The client added an "Opponent" selector (Human vs Computer/AI) and, when the user picks
"Computer (AI)", `createGame` sends `{ opponentType: "Ai" }` in the request body. But the
server endpoint never binds that body — `CreateGame()` takes no `[FromBody]` parameter and
`IGameService.CreateGameAsync()` has no `GameMode`/`OpponentType` overload in this diff.
ASP.NET Core will simply discard the unmatched JSON body and create a **human-vs-human**
game exactly as before.

Net effect: the user selects "Play solo against a basic computer opponent", the UI promises
AI play, and the backend creates a normal two-player game waiting for a human to join. The
feature is non-functional end-to-end. The `CreateGameRequest` client type and the
`OpponentType` union were added with no server counterpart in this PR.

(For context: a `GameMode` enum and `Game.Mode` property, plus a `CreateGameAsync(GameMode)`
overload, exist on other branches of this repo's history, but none of that server-side
contract work is present in PR #13's diff. Against this PR's base, the server simply does
not understand `opponentType`.)

**How to fix:**
Either:
1. Add the matching server contract in this PR — a `[FromBody] CreateGameRequest` (with an
   `OpponentType`/`GameMode` field) on `GamesController.CreateGame`, a
   `CreateGameAsync(GameMode)` overload on `IGameService`/`GameService`, and the AI-reply
   orchestration so an "Ai" game actually gets computer moves — **or**
2. If the AI backend is intentionally deferred to a later PR, do not ship the client
   `opponentType` request body in this PR either: keep `createGame()` parameterless until
   the server can honor the field, so the client never advertises/selects behavior the
   backend silently ignores.

Do not merge a client that sends an unhandled field that mutates user-visible behavior
(creating an AI game) with no backend support.

---

### Finding 3 — [Quality] Heuristic evaluator is dead code: no consumer, not registered in DI

**Files:**
- `.worktrees/<id>/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (new)
- `.worktrees/<id>/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (new)
- `server/src/ChessMvp.Infrastructure/ServiceCollectionExtensions.cs` (unchanged)
- `server/src/ChessMvp.Api/Program.cs` (unchanged)

**What is wrong:**
Even setting aside the wrong path (Finding 1), `IHeuristicEvaluator` / `HeuristicEvaluator`
have no caller anywhere in the reachable tree: it is not referenced by any
`IChessAiPlayer` implementation, not registered in DI
(`Infrastructure.ServiceCollectionExtensions` and `Api.Program` are unchanged in this diff),
and not used by `GameService`. A 507-line service plus its interface shipped with zero
consumers is dead code. The interface XML doc references "an `IChessAiPlayer`" that uses it,
but no such wiring exists in this PR.

**How to fix:**
Either wire the evaluator into an AI player and register it in DI as part of this PR, or
hold the evaluator behind the same PR that introduces its consumer. Shipping an unused
538-line (interface + impl) service with no integration path is not reviewable as a working
feature.

---

### Finding 4 — [Quality] New interactive client component has no unit test; only a Storybook story

**Files:**
- `client/src/components/panels/OpponentTypeSelector.tsx` (new, +72)
- `client/src/components/panels/OpponentTypeSelector.stories.tsx` (new, +52)
- `client/src/screens/CreateGameScreen.tsx` (modified)

**What is wrong:**
The repo already has a unit-test pattern for panels
(`client/src/components/panels/StatusPanel.test.tsx`, +105 lines, using vitest +
@testing-library/react). The new `OpponentTypeSelector` — an interactive radio group that
drives whether an AI body is sent — gets only a Storybook story and no behavioral test
(e.g. that `onChange` fires with the selected `OpponentType`, that `disabled` disables the
inputs, that the default selected option is "Human"). The `CreateGameScreen` change that
threads the selection into `createGame(opponentType)` is likewise untested. Combined with
Finding 2, the absence of a test is precisely why the contract mismatch slipped past CI.

**How to fix:**
Add `OpponentTypeSelector.test.tsx` covering selection/`onChange`/`disabled`/default, and a
`CreateGameScreen` test asserting that choosing "Ai" calls `createGame` with `"Ai"` (and
that choosing "Human" sends no body). This both raises coverage and would have caught
Finding 2.

---

## Security

No injection, authz, or secret-handling concerns introduced by this diff. The new client
code constructs a fixed-shape JSON body (`{ opponentType }`) with no string interpolation
into URLs or HTML; `OpponentType` is a closed `"Human" | "Ai"` union, so there is no
user-controlled free text reaching the request. The server-side evaluator parses FEN
locally with bounds-checked grid access (`InBounds` uses `(uint)x < 8`) and degrades to a
neutral `0` score on malformed input rather than throwing — no unsafe input trust on the
security axis. The blockers above are correctness/quality, not security.

---

## Notes

- "CI is green" does not certify this PR: per Finding 1, the new server code is outside the
  built/tested tree, so the green server CI ran on a tree that excludes this PR's server
  additions; per Finding 2/4, the client CI has no test that exercises the AI-selection
  path. The green signal should not be weighted as evidence the feature works.
- No code changes were made by this review task. Remediation of the findings above should be
  dispatched as a separate bounded fix task.

**Recommendation:** Request changes. Do not merge until the evaluator lives in the real
solution tree and is built/tested (Finding 1), the client/server `opponentType` contract is
either implemented end-to-end or withheld from this PR (Finding 2), the evaluator is wired
to a consumer or deferred (Finding 3), and the new selector is covered by unit tests
(Finding 4).
