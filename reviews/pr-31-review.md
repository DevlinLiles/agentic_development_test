# Automated Pre-Human Review — PR #31

**Verdict: REQUEST_CHANGES**

PR #31 adds a "play vs the computer" (VsAi) game mode on top of the existing two-player
online chess MVP: a `GameMode` entity/enum + EF migration, a `mode` query parameter on
`POST /api/games`, an in-line AI reply generated inside `SubmitMoveAsync`, a heuristic
1-ply greedy `HeuristicChessAiPlayer` backed by a `MaterialHeuristicEvaluator`, DI
registration, client UI/e2e changes, and a CI fix (stable SQL Server image tag + missing
`using`). The diff was reviewed against the merge-base of the PR head (`d6bdb26`) and the
main tip (`55c7ac8`).

The core logic is sound and well-tested: material evaluation, White-perspective score
negation for the side to move, deterministic tie-breaks, promotion-piece selection, the
VsAi creation/active-state flow, the null-Black-seat authz model (a VsAi game's `BlackSlotToken`
is `null` and `Status` is `Active`, so `JoinGameAsync` and `SubmitMoveAsync`'s
`ResolveSlotToken` correctly forbid any human from claiming or moving as Black), the EF
migration (`Mode` column `nullable:false defaultValue:0` = `TwoPlayer` backfills existing
rows), and the client `GameScreen`/`JoinLinkPanel` VsAi guards. The material evaluator and
greedy selection have dedicated unit tests with deterministic-replay and under-promotion
cases, and an e2e spec asserts the AI replies within the move history.

Two actionable issues block merge:

## Findings

### 1. [Correctness/Quality] The `AiSearchOptions` search-depth contract is dead — production runs at half the configured depth
**File:** `server/src/ChessMvp.Domain/Ai/HeuristicChessAiPlayer.cs` — `SelectMoveAsync` (the `AiSearchOptions options` parameter) and the `SearchDepthInPlies = 1` literal in the returned `AiMoveResult`.
**Related:** `server/src/ChessMvp.Domain/Abstractions/AiSearchOptions.cs` — `Shallow(int depthInPlies = 2)` whose XML doc states *"Defaults to a two-ply search."*; `server/src/ChessMvp.Domain/Services/GameService.cs` — `GenerateAiReplyAsync` calls `AiSearchOptions.Shallow()` (no argument, so the 2-ply default).

`HeuristicChessAiPlayer.SelectMoveAsync` accepts `AiSearchOptions options` but never reads
it — only `cancellationToken` is used. `MaxDepthInPlies` and `TimeLimit` have no effect on
the search, and the result hardcodes `SearchDepthInPlies = 1`. The player therefore always
performs a 1-ply greedy search regardless of the configured depth.

This makes the public contract misleading and the production configuration wrong:
`AiSearchOptions.Shallow()` is documented to default to a **two-ply** search, and
`GameService.GenerateAiReplyAsync` (the production call site) invokes `Shallow()` with no
argument, so production expects a 2-ply search but always gets 1-ply. The AI is weaker than
configured and the `options` parameter / `MaxDepthInPlies` / `TimeLimit` / `SearchDepthInPlies`
contract is not honored. The unit tests mask this by passing `Shallow(1)` explicitly (they
were written knowing the implementation ignores the value), so the divergence only surfaces
at the production call site.

**How to fix:** Either (a) honor `options.MaxDepthInPlies` by extending the search to the
requested depth (e.g. a negamax/minimax over `GetAllLegalMoves` to depth N, reporting
`SearchDepthInPlies = N`), so the documented 2-ply `Shallow()` default is actually performed
in production; or (b) if 1-ply is intentionally the only supported depth for this MVP, stop
advertising a depth the implementation does not perform — change `Shallow()`'s default and
doc to 1-ply, have the player read `options.MaxDepthInPlies` and either respect it or throw
`NotSupportedException` when a depth it does not support is requested, and update the
`IChessAiPlayer`/`AiSearchOptions` docs to state that the heuristic player currently supports
only 1-ply. In either case `GenerateAiReplyAsync` should pass an option value consistent with
what the player actually does.

### 2. [Correctness/Robustness] A rejected AI move silently bricks the game with no error or terminal state
**File:** `server/src/ChessMvp.Domain/Services/GameService.cs` — `GenerateAiReplyAsync`, the
`if (!application.IsLegal || application.ResultingFen is null) { ... return; }` branch.

When the AI returns a move that the rules engine rejects, `GenerateAiReplyAsync` returns
without applying a move and without changing `game.Turn`/`game.Status`. After the preceding
human move, `game.Turn` is `Black` and `game.Status` is `Active`, so the game is left in a
state where it is "Black's turn" but no human holds the Black slot token (`BlackSlotToken` is
`null` for VsAi games). The human (White) can never move again — `SubmitMoveAsync` throws
`NotYourTurnException` because `resolvedColor (White) != game.Turn (Black)` — and there is no
Black token holder to move either. The game is permanently stuck in `Active` with no error
surfaced to the client and no path to a terminal state.

The code comment acknowledges this is "defensive — the AI only ever returns moves it
enumerated as legal," so under the current invariant the branch is unreachable. But the
invariant is only maintained by the *same* code that depends on it: `HeuristicChessAiPlayer`
enumerates moves via `GetAllLegalMoves` and returns one of them, then `GenerateAiReplyAsync`
re-validates with `TryApplyMove`. Any future divergence between `GetAllLegalMoves` and
`TryApplyMove` (e.g. the shared `IsPawnPromotion` heuristic disagreeing with the engine's real
promotion handling, or a different `IChessAiPlayer` implementation) silently turns the game
into an unrecoverable dead state rather than failing loudly.

**How to fix:** Don't leave the game in an unmoveable `Active` state. At minimum, when the AI
move is rejected, log the divergence and transition the game to a terminal state (e.g.
`Status = Ended` with an appropriate `ResultReason`, or a dedicated error status) so the
client gets a definitive outcome instead of an infinite "Opponent's turn." Preferably, treat
an AI-returned illegal move as an internal error (throw/log) so the invariant violation is
detected rather than swallowed.

## Summary

The VsAi feature's domain logic, authz model, migration, and test coverage are correct. The
merge is blocked by (1) the `AiSearchOptions` search-depth contract being entirely ignored —
production calls a documented 2-ply `Shallow()` but the player always does 1-ply and reports
`SearchDepthInPlies = 1`, leaving `options`/`MaxDepthInPlies`/`TimeLimit` as dead
configuration — and (2) the AI-move-rejected branch in `GenerateAiReplyAsync` leaving the game
permanently stuck in an unmoveable `Active` state with no error or terminal outcome. No code
changes are made in this review; remediation is left to a separate fix task.
