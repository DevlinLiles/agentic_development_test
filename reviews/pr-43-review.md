# Automated Pre-Human Review — PR #43

**Verdict: REQUEST_CHANGES**

CI is green, but the diff does not deliver a functional feature and commits
worktree internals into the repository. Three blocking findings below, plus one
non-blocking observation. Line references are to the PR head tree.

## Blocking findings

### 1. Worktree directories committed as submodule gitlinks (Correctness / Quality)

**Files:** `.worktrees/01m1mgnqs4dbnrw1wvktjqdpbm`,
`.worktrees/01m1mgnqxm7hczvj5nfa7gnvh7`,
`.worktrees/01m1vy27727029etgw6qp81jdc/` (new entries, mode `160000`).

**What is wrong:** The diff adds the `.worktrees/` directory to the repository,
including nested linked worktrees recorded as gitlinks / submodule entries
(mode `160000`). `.worktrees/` is local agent scaffolding and is not listed in
`.gitignore`, so it was tracked by accident. This pollutes the tree with
machine-specific paths and broken submodule pointers that nobody else can clone
or initialize.

**How to fix:** Add `.worktrees/` to `.gitignore` and run
`git rm --cached -r .worktrees` so none of it is tracked. No `.worktrees/` path
should appear in the PR diff.

### 2. New feature source files are stranded outside the build tree (Correctness / Quality)

**Files:**
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:** The only copies of `IHeuristicEvaluator.cs`,
`HeuristicEvaluator.cs`, and `HeuristicEvaluatorTests.cs` in this diff live
under `.worktrees/.../server/...`. They are NOT present under the real
`server/src/ChessMvp.Domain/...` or `server/tests/ChessMvp.Domain.Tests/...`
trees, and no `.csproj` or `ChessMvp.slnx` references them (the solution file is
unchanged in this PR). They are therefore dead code: never compiled, never
tested, never shipped. "CI is green" only because the build never sees these
files — the green CI is misleading, not evidence the feature works.

**How to fix:** Move the three files into the real solution tree:
`server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`,
`server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
`server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`; remove the
`.worktrees/` copies; and confirm `dotnet build` / `dotnet test` in `server/`
actually compiles and runs `HeuristicEvaluatorTests`.

### 3. AI opponent is not wired end-to-end; server ignores `opponentType` (Correctness)

**Files:**
- `client/src/api/gamesApi.ts` — `createGame` now sends a `{ opponentType }` body.
- `client/src/screens/CreateGameScreen.tsx` — passes `opponentType` to `createGame`.
- `server/src/ChessMvp.Api/Controllers/GamesController.cs` — **UNCHANGED**.
- `server/src/ChessMvp.Domain/Entities/Game.cs` — **UNCHANGED**.
- `server/src/ChessMvp.Domain/Services/GameService.cs` — **UNCHANGED**.

**What is wrong:** The client lets the user pick "Computer (AI)" and sends
`{ "opponentType": "Ai" }` as the `POST /api/games` body, but there are zero
server-side changes: no `OpponentType` enum, no `IChessAiPlayer`, no AI move
generation, no persistence of opponent type on `Game`, and no controller
parameter that reads the request body. ASP.NET silently drops the unknown field,
so selecting "Computer (AI)" creates an ordinary human-vs-human game and the AI
never makes a move. The headline feature is non-functional end-to-end.
Separately, the `HeuristicEvaluator` from finding 2 is only a position scorer —
there is no player/search component that uses it to choose and apply moves, so
even after moving the files into the tree the AI still would not play.

**How to fix:** Implement the server half of the feature: accept and validate
`opponentType` on the create-game endpoint, store it on the `Game` entity (with
a migration), and add an AI player that selects moves via the heuristic
evaluator and applies them after the human moves. Add tests covering AI game
creation and an AI move being made. Until the server consumes `opponentType`,
the client selector is misleading and should be reverted or gated behind server
support.

## Non-blocking observations

### 4. Hardcoded database password in compose (Security / secret handling)

**File:** `docker-compose.yml`, `api.environment.ConnectionStrings__ChessMvpDb`
(`Password=ChessMvp!Local2026`).

**What is wrong:** The SQL Server SA password is committed in plaintext in the
compose file. Even for a local `Development` setup, committing credentials into
source is a secret-handling smell and sets a bad precedent for later
environments.

**How to fix:** Reference the password via an environment variable / `.env`
override (the `.env` / `.env.local` entries are already gitignored) rather than
hardcoding it in `docker-compose.yml`.
