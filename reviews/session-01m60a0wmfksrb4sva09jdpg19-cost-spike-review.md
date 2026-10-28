# Cost-spike review: session 01m60a0wmfksrb4sva09jdpg19

**Task under review:** Computer-move port and Gera.Chess engine adapter
**Trigger:** Session paused on a token-rate spike (230,349 tpm vs 228,863 tpm threshold).
**Reviewed on branch:** `task/01m60a5atwtdqb8addyeq47q6v/dev` (tracks `origin/main`, working tree clean at `d7d3d04`).

## What I audited

1. **Branch/commit state.** The dev branch sits at the post-revert baseline `d7d3d04` ("Revert 'Add basic heuristic AI opponent for solo play'"), which restored the repo to the clean two-player MVP (`f5cb2c5`) plus the CI workflow (`1a3c3e0`). There are **no orphaned or half-applied edits** from the paused session on this branch — the working tree is clean, so nothing was left mid-write when the session was cut off.
2. **The Gera.Chess engine adapter.** `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs` is the sole wrapper around the Gera.Chess (Geras1mleo) v1.2.0 package. It is in good shape and matches the architecture constraints that matter for a computer-move port:
   - Swap-seam intact: only this class (plus its tests in `server/tests/ChessMvp.Domain.Tests/GerasimleoChessRulesEngineAdapterTests.cs`) touches the `Chess.*` types; the domain depends only on `IChessRulesEngine`.
   - Terminal-position defensive handling is already present and correct: `board.Move` is guarded by try/catch on `ChessPieceNotFoundException` **and** `ChessGameEndedException`, and `GetLegalDestinations` iterates `board.Moves()` — consistent with the known failure mode where terminal positions surface either as an empty `Moves()` enumerable or as a thrown `ChessGameEndedException` depending on the code path.
   - Draw scoping is deliberate (`AutoEndgameRules.FiftyMoveRule` only) and commented, so a computer-move port won't inherit unintended endgame semantics.
3. **Domain orchestration seam.** `GameService.SubmitMoveAsync` is a single unit of work (validate → apply → persist → notify) driven by `IChessRulesEngine`/`IGameRepository`, with no EF Core leakage into the domain. The prior AI attempt's `IChessAi` / `GreedyHeuristicChessAi` / `GameOpponent` / `IsVsAi` migration were fully removed by the revert — so a resumed port starts from a clean slate rather than fighting leftover schema (the old `20260708120000_AddIsVsAi` migration and `GreedyHeuristicChessAi` are gone from the tree).
4. **Prior art elsewhere in the repo.** Other epic branches already carry a proven shape for this exact feature: `IChessAiPlayer` + heuristic move selection wired through DI, `Game.Mode`/`OpponentKind` as an additive enum column with a migration, and `GetAllLegalMoves` added to `IChessRulesEngine` + the adapter. That work was approved in review verdicts up through PR #75, which confirms the direction the paused session was heading is the direction the repo has already validated.

## Verdict: RESUME (on track), with a smaller footprint

The paused session's work is **on track and nothing is corrupted** — the spike was a cost/pace problem, not a correctness problem. Resume rather than cancel, with these adjustments:

1. **Land in small, independently green commits.** The earlier attempt on this line of work had to be committed by an auto-commit "budget exhausted" hook and was ultimately reverted; the successful sibling branches (PRs #64–#75) each shipped one narrow concern per task. Prefer: (a) `GetAllLegalMoves` on `IChessRulesEngine` + adapter mapping, then (b) the AI player behind a new abstraction, then (c) GameService orchestration + EF mapping, each with its own tests.
2. **Reuse the adapter as-is.** No changes to `GerasimleoChessRulesEngineAdapter.TryApplyMove` are needed for a computer-move port; any new capability (legal-move enumeration for the AI) should be an additive member on `IChessRulesEngine` implemented in the adapter, keeping the swap-seam promise intact.
3. **Keep the defensive try/catch pattern** in any new adapter member that touches `board.Moves()` on potentially terminal positions — normalize both the empty-enumerable and `ChessGameEndedException` outcomes to an empty result, matching the existing `TryApplyMove` pattern.
4. **Budget control** (the actual reason for the pause): cap exploratory reads per iteration, read only named files, and commit early so a future spike cannot strand uncommitted work.
