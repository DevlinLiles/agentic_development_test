# Automated Pre-Human Review — PR #64

**Verdict: REQUEST_CHANGES**

**Scope reviewed:** the full 48-file diff. (The task-supplied diff copy was truncated at ~115k chars; the remainder was verified against the repository history — base `d7d3d04` (main) → head `9944a79`, 48 files, +2779/−174.) The PR adds the opponent-mode contract and creation/join-rejection plumbing (server + client), a new `OpponentChooser` UI on the create screen, an additive EF migration, Docker/compose deployment for api/client, and tests across all layers.

**CI:** green, and the test coverage for what was added is genuinely good (contract wire-shape tests, radio-group a11y tests, controller/service tests, adapter tests, screen tests). The blockers below are about what was *not* finished, not about what was tested.

## Findings

### 1. Computer games can never progress — the AI opponent does not exist (correctness, blocker)

`server/src/ChessMvp.Domain/Services/GameService.cs`, `CreateGameAsync`, the `OpponentMode.Computer` branch (~lines 37–52): a Computer-mode game is created `Active` with both seats filled at creation. The code comment claims *"the engine submits its replies through the same token-resolution path a human player would"* — but `SubmitMoveAsync` is untouched in this PR and there is no AI player/engine anywhere in the head tree. After the human (White) makes their first move, turn passes to Black and nothing ever moves for Black: **every "Play the computer" game stalls permanently on "Computer's turn."** The client even ships UI for a reply that can never fire ("Computer played X" in `StatusPanel.tsx`).

**Fix:** land the AI move-selection + reply orchestration in this PR (in `SubmitMoveAsync`, after the human's move, resolving the AI's Black slot token), or gate the "Play the computer" option until the PR that actually adds the engine. As shipped, the user-visible flow is broken by design.

### 2. Dead public API surface with no product caller (quality, blocker)

`server/src/ChessMvp.Domain/Abstractions/IChessRulesEngine.cs` (`GetAllLegalMoves`), `server/src/ChessMvp.Domain/Abstractions/RulesLegalMove.cs`, and `server/src/ChessMvp.Infrastructure/GerasimleoChessRulesEngineAdapter.cs` (plus 258 lines of adapter tests). No production code calls `GetAllLegalMoves` — the only references anywhere are the interface, the record, the adapter, and the adapter's own tests. This is clearly groundwork for the missing engine from finding 1. Ship it together with that engine or drop it until then; as-is it is ~370 lines of unexercised production surface (including a Domain abstraction the rest of the codebase must now carry).

### 3. Duplicated hardcoded SA password (security)

`docker-compose.yml`, `api` service, `ConnectionStrings__ChessMvpDb`: the literal `ChessMvp!Local2026` is now embedded in a **second** service while the `sqlserver` service already owns it via `MSSQL_SA_PASSWORD`. Duplicated literals drift silently and widen the secret's footprint. **Fix:** define the password once (e.g. `.env` with `${MSSQL_SA_PASSWORD}` interpolation, or a shared env var) instead of a second literal connection string.

### 4. Committed temporary scaffolding (quality)

`docker-compose.tests.yml` (defines no services) and `docker/tests.Dockerfile` (a `FROM scratch` no-op). Both files' own headers say "TEMPORARY local verification scaffold — no longer needed, safe to delete". Delete them before merge.

### 5. Lockfile churn looks unintentional (quality)

`client/package-lock.json` / `client/package.json`: oxlint is downgraded 1.73.0→1.71.0 and vite 8.1.3→8.1.1 while the manifests still permit the newer versions; **every** platform-optional dependency lost its `libc` gating field (lockfile regenerated with an older npm — this regresses musl/Alpine installs of platform binaries); and `typescript` was widened `~6.0.2`→`^6.0.2` for no stated reason while the lock pins 6.0.2. **Fix:** restore the previous lockfile (or regenerate with a current npm) and keep `~6.0.2` unless the wider range is deliberate.

## What checked out (verified, no action needed)

- The additive nullable-column migration (`20260708090000_AddOpponentModeToGame`) plus `NULL`→`Human` normalization is applied consistently at **every** read site — `GamesController.CreateGame`, `GameStateResponse.FromGame`, `JoinGameAsync` — so legacy rows never surface a third "unknown" mode.
- `EmptyBodyBehavior.Allow` on the new `[FromBody] CreateGameRequest?` keeps the legacy bodyless POST working byte-for-byte as before; an omitted field and an absent body are indistinguishable, as documented.
- The join-rejection for Computer games deliberately reuses the existing 409 `GameNotActive` vocabulary instead of introducing a new exception type — good API-compat instinct.
- The client's optional `opponentType` with `undefined`-means-`Human` fallback is handled everywhere it is read (`JoinLinkPanel`, `GameScreen` reload/auto-join path, `StatusPanel`), so pre-upgrade servers can't crash the client.
- `OpponentChooser` uses a native radio pair inside `<fieldset><legend>` with `htmlFor`-wired labels and `aria-describedby` hints; the tests pin group semantics, checked state, keyboard arrows, and disabled behavior.

## Recommendation

Findings 1–2 are the blockers (the headline feature is half-shipped). Findings 3–5 are cleanup that should not ride along. Remediation should be dispatched as a separate bounded fix task; per review protocol, no code was changed in this pass.
