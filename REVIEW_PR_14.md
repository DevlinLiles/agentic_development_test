# Automated Pre-Human Review — PR #14

**Verdict:** `APPROVE`

**One-line rationale:** PR #14 cleanly remediates all four PR #13 findings — the AI evaluator now lives in the built solution tree and is wired through DI, the client/server `mode` contract is implemented end-to-end, the dead-code/wrong-path artifacts are gone, and the new behavior is covered by unit + e2e tests.

---

## Scope reviewed

PR head `46fbddf` (epic-story branch carrying the VsAi remediation work) diffed against
its base `07fc8dc` (= PR #13's head / local `main`). Substantive diff (excluding
`package-lock.json` and the two stray `.worktrees/<id>` pointer files, which are removed
here):

- `server/src/ChessMvp.Domain/Entities/GameMode.cs` (+7) — new `GameMode { TwoPlayer, VsAi }` enum.
- `server/src/ChessMvp.Domain/Entities/Game.cs` (+2) — `Game.Mode` defaulting to `TwoPlayer`.
- `server/src/ChessMvp.Domain/Abstractions/IChessAiPlayer.cs`, `AiMoveResult.cs`,
  `AiMoveStatus.cs`, `AiSearchOptions.cs`, `AiSearchStatistics.cs`, `IHeuristicEvaluator.cs`,
  `LegalMove.cs`, `IChessRulesEngine.cs` (+6) — AI + legal-move abstraction surface.
- `server/src/ChessMvp.Domain/Ai/HeuristicChessAiPlayer.cs` (+172) — deterministic 1-ply greedy selector.
- `server/src/ChessMvp.Domain/Ai/MaterialHeuristicEvaluator.cs` (+44) — pure material evaluator (now in the real tree).
- `server/src/ChessMvp.Domain/Services/GameService.cs` (+152/-23) — `CreateGameAsync(GameMode)`, inline AI-reply orchestration, shared `ApplyMoveToGame`.
- `server/src/ChessMvp.Domain/Services/IGameService.cs` (+2) — new overload.
- `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs` (+38) — `GetAllLegalMoves` + shared `IsPawnPromotion`.
- `server/src/ChessMvp.Infrastructure/ServiceCollectionExtensions.cs` (+29) — idempotent `AddChessAiPlayer` DI registration.
- `server/src/ChessMvp.Api/Program.cs` (+13/-1) — resolves `IChessAiPlayer` into `GameService`.
- `server/src/ChessMvp.Api/Controllers/GamesController.cs` (+10/-2) — `[FromQuery] GameMode mode`.
- `server/src/ChessMvp.Api/Contracts/{CreateGameResponse,GameStateResponse}.cs` — `Mode` + nullable `JoinUrl`.
- `server/src/ChessMvp.Infrastructure/Migrations/20260708000000_AddGameMode.cs` (+30) — additive int column, default 0.
- Tests: `HeuristicChessAiPlayerTests.cs` (+221), `ChessAiPlayerRegistrationTests.cs` (+87), `GamesControllerTests.cs` (+33).
- Client: `gamesApi.ts`/`httpClient.ts` (query-string support), `CreateGameScreen.tsx` (two buttons), `GameScreen.tsx` (skip join for VsAi), `JoinLinkPanel.tsx` (hide for VsAi), `gameTypes.ts` (`GameMode`), e2e `play-vs-ai.spec.ts` (+42) + `helpers.ts`.

---

## Correctness

- **AI-reply orchestration is sound.** `SubmitMoveAsync` applies the human move via the
  shared `ApplyMoveToGame`, then only triggers `GenerateAiReplyAsync` when
  `game is { Mode: VsAi, Status: Active, Turn: Black }`. Because `ApplyMoveToGame` sets
  `Status = Ended` on checkmate/stalemate/fifty-move, a human move that ends the game
  correctly short-circuits the AI turn (the `Status: Active` guard fails). The AI move is
  re-validated through `TryApplyMove` and routed back through the same `ApplyMoveToGame`, so
  endgame detection (e.g. the AI delivering mate) is reused rather than duplicated.
- **Single save point.** `SaveChangesAsync` was hoisted out of the old inline block to run
  once after both plies, so the human + AI moves persist atomically; the notifier fires once
  with the final state. `MoveNumber` is computed as `Max(...) + 1` and correctly advances for
  the AI ply since the human move is already in `game.Moves`.
- **`ApplyMoveToGame` refactor is behavior-preserving.** `resolvedColor` → `mover` is the
  only semantic change; both human and AI paths pass the actual side that moved, and
  turn-flip / `PlyColor` / result assignment all key off `mover`. No off-by-one or
  wrong-color regression.
- **Backward compatibility.** `CreateGame()` (parameterless) delegates to
  `CreateGameAsync(GameMode.TwoPlayer)`; the controller default is
  `[FromQuery] GameMode mode = GameMode.TwoPlayer`, so existing two-player clients that omit
  the param are unaffected. The migration is additive (`int`, default 0 = `TwoPlayer`).
- **Determinism.** `HeuristicChessAiPlayer` selects via `OrderByDescending(Score)` with
  deterministic tie-breakers (SAN → from → to → promotion), so VsAi games are reproducible —
  appropriate for a testable MVP opponent.

## Quality

- All PR #13 quality findings are addressed: the 507-line evaluator that lived under
  `.worktrees/<id>/...` is gone; a focused 44-line `MaterialHeuristicEvaluator` ships in the
  real solution tree and is the concrete the AI player consumes. `OpponentTypeSelector` (the
  untested interactive component) is removed entirely and replaced by two declarative
  buttons on `CreateGameScreen`.
- DI registration is idempotent (guards each descriptor against duplicate registration) and
  documents the singleton safety rationale (stateless/pure). `Program.cs` uses
  `GetRequiredService<IChessAiPlayer>`, so the production graph always supplies the player;
  the nullable `_aiPlayer` + no-op guard in `GenerateAiReplyAsync` is a reasonable escape
  hatch for two-player-only test construction.
- New behavior is tested: AI player scoring/selection/promotion/tie-breakers (221 lines), DI
  registration (87 lines), controller mode binding (33 lines), and an e2e spec asserting the
  VsAi game starts Active with no join link and that the AI replies (move history → 2).

## Security

- No injection or unsafe input trust. `mode` is bound to the `GameMode` enum via
  `[FromQuery]`; ASP.NET Core rejects unknown values with a 400 rather than accepting free
  text. The client builds the query string with `URLSearchParams` (encoded, null-skipped) —
  no string interpolation into URLs.
- No authz gap. VsAi games are created `Active` with `BlackSlotToken = null`, so
  `JoinGameAsync`'s existing guard (`BlackSlotToken is not null || Status != WaitingForPlayer2`)
  rejects any attempt to claim the AI seat. `JoinUrl` is explicitly `null` for VsAi and the
  client hides the `JoinLinkPanel` and skips the join path for AI games. No secrets or
  user-controlled free text reach persistence or HTML.

---

## Notes

- "CI is green" is meaningful for this PR: unlike PR #13, the new server code is inside the
  built/tested solution tree and is exercised by added unit tests and the play-vs-ai e2e
  spec, so the green signal covers the AI-selection and inline-reply paths.
- No code changes were made by this review task.

**Recommendation:** Approve. Ready for a human reviewer.
