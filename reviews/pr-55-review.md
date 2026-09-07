# Automated Pre-Human Review — PR #55

**Verdict: REQUEST_CHANGES**

**Scope reviewed:** diff of `task/01m1xn8tmqbdn4n2z4z8xwaejm/reviewer` (head `07fc8dc`) against base `1a3c3e0` (baseline CI workflow), covering server (`server/`), client (`client/`), Docker, and compose.

The diff is not ready for a human reviewer: the headline deliverable (the heuristic evaluator) is committed outside the project tree and therefore never compiles or runs, the client reintroduces an "AI opponent" feature whose server-side support this same PR deletes, and several stray worktree artifacts are tracked. Findings below are concrete and blocking.

---

## Finding 1 — [Correctness/Build, CRITICAL] New `HeuristicEvaluator` is committed outside the project tree and is invisible to the build

**Files:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (added, 31 lines)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (added, 507 lines)
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (added, 108 lines)

**What is wrong:** The entire new feature — interface, implementation, and tests (646 lines) — is committed under the path `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` instead of the real `server/...` tree. The actual `server/src/ChessMvp.Domain/Abstractions/` contains only `IChessRulesEngine.cs`, `IGameNotifier.cs`, `IGameRepository.cs`, `MoveApplicationResult.cs`, `MoveFailureReason.cs` — no `IHeuristicEvaluator.cs`. A repo-wide search for `IHeuristicEvaluator|HeuristicEvaluator` returns **no matches** in the real source tree, confirming the files are orphaned. `server/src/ChessMvp.Domain/ChessMvp.Domain.csproj` never references them, so they are not compiled, not tested, and not shipped. The Dockerfile `COPY server/src/ ./src/` step will never pick them up either.

This means the PR's stated goal — "Add heuristic evaluator with material, piece-square tables, and mobility scoring" — is effectively not delivered: the code exists only inside an unrelated worktree directory.

**How to fix:** Move all three files from `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` to the corresponding `server/...` paths:
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then delete the `.worktrees/01m1vy27727029etgw6qp81jdc/` subtree from the commit and verify `dotnet build` / `dotnet test` actually compile and execute `HeuristicEvaluatorTests`.

---

## Finding 2 — [Correctness/Contract, CRITICAL] Client sends an AI-opponent request body the server does not accept or handle

**Files:**
- `client/src/api/gamesApi.ts` (`createGame`, lines ~17–33)
- `client/src/types/gameTypes.ts` (`CreateGameRequest`, `OpponentType`)
- `server/src/ChessMvp.Api/Controllers/GamesController.cs` (`CreateGame`, `[HttpPost]`)
- `server/src/ChessMvp.Domain/Services/GameService.cs` (`CreateGameAsync`)

**What is wrong:** The client now sends `POST /api/games` with a JSON body `{ "opponentType": "Ai" }` when the user picks "Computer (AI)". However the server endpoint is `public async Task<ActionResult<CreateGameResponse>> CreateGame()` — a **parameterless** action that ignores the request body entirely — and `IGameService.CreateGameAsync()` takes no arguments. Selecting "AI" on the client therefore has no server-side effect: a normal two-player human game is always created. The feature is silently a no-op from the user's perspective.

**How to fix:** Either (a) add an `OpponentType` request model to `CreateGame` and thread it through `CreateGameAsync` so the server honours the choice, or (b) if the AI feature is intentionally being removed (see Finding 3), remove the `opponentType` request-body path and the `CreateGameRequest`/`OpponentType` types from the client so the client stops sending a body the server ignores. The two layers must agree.

---

## Finding 3 — [Correctness/Quality, CRITICAL] Client advertises an AI-opponent mode that this PR deletes from the server

**Files:**
- `client/src/components/panels/OpponentTypeSelector.tsx` (new, offers `value: "Ai"` → "Computer (AI)")
- `client/src/screens/CreateGameScreen.tsx` (wires `OpponentTypeSelector` into game creation)
- **Deleted server-side:** `server/src/ChessMvp.Domain/Abstractions/IChessAi.cs`, `server/src/ChessMvp.Domain/ChessAi/GreedyHeuristicChessAi.cs`, `server/src/ChessMvp.Domain/Entities/GameOpponent.cs`, `Persistence/ChessDbContext.cs` `GameOpponent` set, migration `20260708120000_AddIsVsAi`, `ServiceCollectionExtensions.cs` AI registration, `ChessMvp.Domain.Tests/GreedyHeuristicChessAiTests.cs`.

**What is wrong:** This PR simultaneously (i) adds a client UI letting users choose "Computer (AI)" as the opponent and (ii) deletes every piece of server-side AI-opponent machinery (`IChessAi`, `GreedyHeuristicChessAi`, the `GameOpponent` entity, its EF migration, its DI registration, and its tests). The two changes are in direct conflict: the client promises a capability the server no longer has. Whatever the intended direction, the PR as merged would ship a UI option that cannot function.

**How to fix:** Pick one consistent direction and make the diff coherent:
- If the goal is to **add** a heuristic AI: keep the client selector, but restore/keep the server AI wiring and connect it to the (relocated) `HeuristicEvaluator` (Finding 1), implementing the AI move path in `GameService`/`SubmitMoveAsync`.
- If the goal is to **remove** AI: remove the `OpponentTypeSelector`, the `opponentType` state in `CreateGameScreen`, the `OpponentType`/`CreateGameRequest` types, and the body-sending branch in `gamesApi.createGame`.

---

## Finding 4 — [Quality] `IHeuristicEvaluator` XML doc references a non-existent type `IChessAiPlayer`

**File:** `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (summary/remarks `<see cref="IChessAiPlayer"/>`).

**What is wrong:** The interface documentation says it is "used by an `IChessAiPlayer`" and references `IChessAiPlayer` via `<see cref="..."/>`, but no type named `IChessAiPlayer` exists anywhere in the repository (the AI abstraction this PR removed was named `IChessApi`/`IChessAi`, not `IChessAiPlayer`). This produces a broken cref and misleading documentation.

**How to fix:** Update the doc to reference the actual consumer, or remove the cref if there is no consumer after the relocation/direction decision in Findings 1–3.

---

## Finding 5 — [Quality/Hygiene] Stray worktree directories are tracked in the repo

**Files:** `.worktrees/01m1mgnqs4dbnrw1wvktjqdpbm` and `.worktrees/01m1mgnqxm7hczvj5nfa7gnvh7` (each added as a 1-line file per `git diff --stat`).

**What is wrong:** Two sibling worktree paths are committed as tracked files. `.worktrees/` is a local checkout artifact and must never be version-controlled; committing it pollutes the tree and is the same root cause behind Finding 1 (the evaluator landing under a `.worktrees/<id>/` path).

**How to fix:** Remove these paths from the commit (`git rm`) and add `.worktrees/` to `.gitignore` so future worktrees cannot be accidentally committed.

---

## Summary

| # | Severity | Category | One-line |
|---|----------|----------|----------|
| 1 | Critical | Correctness/Build | HeuristicEvaluator committed under `.worktrees/<id>/server/...`, not `server/...` — never compiled/tested/shipped |
| 2 | Critical | Correctness/Contract | Client sends `{opponentType:"Ai"}` body; server `CreateGame()` is parameterless and ignores it |
| 3 | Critical | Correctness/Quality | Client adds an AI-opponent selector while this PR deletes all server-side AI support |
| 4 | Major | Quality | `IHeuristicEvaluator` docs `<see cref="IChessAiPlayer"/>` points at a non-existent type |
| 5 | Minor | Hygiene | `.worktrees/<id>` directories tracked in the repo; add `.worktrees/` to `.gitignore` |

Findings 1–3 are mutually reinforcing and all block merge: the feature is not built, the contract is broken, and the client/server disagree on whether AI play exists. Recommend REQUEST_CHANGES; remediation should be dispatched as a bounded fix task that first decides the intended direction (add AI vs. remove AI), then relocates the evaluator, reconciles the API contract, and cleans up the stray worktree paths.
