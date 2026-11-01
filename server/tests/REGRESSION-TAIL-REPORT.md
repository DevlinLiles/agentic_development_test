# Regression tail report — full pre-existing GameServiceTests run (sub-5)

**Branch:** `task/01m6b01dc7j0b32n3dedp2ysz6/qa`
**Date:** 2026-11-01
**Result:** ✅ **GREEN — 114 / 114 passed, 0 failed, 0 skipped**

## How the suite was run

The whole Domain test suite (pre-existing + new files) was executed in a **single run** via the
containerized verification build, because the local .NET SDK is 8.x while every project in
`server/` targets `net10.0` with EF Core 10 refs (a local `dotnet test` fails with `NETSDK1045`
before compiling anything):

```sh
docker compose build --no-cache tests
```

That builds `docker/tests.Dockerfile` (target `unittests`), which restores and compiles both
test projects and everything they reference, then runs the container-free Domain unit-test
suite as a build step — so any red test fails the build outright. As of this subtask the
`unittests` stage uses the detailed console logger, so the build log lists every executed test
by name (full captured output below).

`ChessMvp.Api.Tests` is compiled (green) but **not** executed: its Testcontainers/MsSql
fixtures need a Docker daemon that a build container does not have, so it stops at the
compile gate by design. It contains no `GameServiceTests` — the pre-existing suite lives in
`ChessMvp.Domain.Tests`.

Reproduce anytime with the wrapper: `sh server/tests/run-regression-tail.sh` (it runs the
exact command above and tees output to `server/tests/logs/regression-tail.log`, which is
gitignored as a transient build log — hence this committed report).

## Pre-existing suite unmodified (audit)

`git diff 0f9df5a..HEAD -- server/tests/ChessMvp.Domain.Tests/GameServiceTests.cs` shows
**323 insertions, 3 deletions**. The 3 deletions are exactly the mechanical sub-1
constructor ripples:

1. `_sut = new GameService(_repository, _rulesEngine, _notifier);` →
   `new GameService(_repository, _rulesEngine, _notifier, _chessAi);` (new 4-arg signature)
2. `private static Game NewActiveGame(Guid? id = null)` →
   `NewActiveGame(OpponentMode? opponentMode = OpponentMode.Human)` (helper signature)
3. `Id = id ?? Guid.NewGuid(),` → `Id = Guid.NewGuid(),` (same line, helper body)

Since the cumulative diff deletes only those 3 lines — none inside a `[Fact]`/`[Theory]`
method — **every test body that existed at the pre-epic baseline is byte-identical today**.
The 323 insertions are exclusively new fields, new helpers, and newly *appended* test
methods. Every pre-existing test name runs and passes in the current suite (see log).

## Test inventory and outcome (single run, 114 total)

| Test file | Origin | Test cases | Result |
|---|---|---|---|
| `GameServiceTests.cs` | pre-existing suite (+ sub-1 ripples + epic-appended Computer block) | 33 facts + 1 theory × 2 cases = **35** | ✅ all pass |
| `GameServiceComputerGameReplyFlowTests.cs` | new (sub-2) | 5 facts = **5** | ✅ all pass |
| `GameServiceComputerGameFlowTests.cs` | new (sub-3) | 5 facts = **5** | ✅ all pass |
| `GameServiceHumanModeIsolationTests.cs` | new (sub-4) | 4 facts + 1 theory × 2 cases = **6** | ✅ all pass |
| `HeuristicChessAiTests.cs` | pre-existing | 17 facts = **17** | ✅ all pass |
| `HeuristicChessAiSelectionTests.cs` | pre-existing | 8 facts = **8** | ✅ all pass |
| `GerasimleoChessRulesEngineAdapterTests.cs` | pre-existing | 33 facts + 1 theory × 2 cases = **35** | ✅ all pass |
| `DependencyInjectionTests.cs` | pre-existing | 3 facts = **3** | ✅ all pass |

Reconciliation: 35 + 5 + 5 + 6 + 17 + 8 + 35 + 3 = **114** — matches the runner's
`Total tests: 114, Passed: 114` exactly, so nothing was skipped, filtered, or silently
excluded from the run.

### New suites by acceptance-criteria category

- **Computer-game flow (sub-2 + sub-3):** Scenarios A–E, creation/join rejection,
  promotion reply, human promotion, missing-promotion-piece — 10 tests, all pass.
- **Terminal-state no-reply (sub-3):** Scenario D (checkmate) and Scenario E (stalemate) —
  both assert zero AI consultations and zero notifications, both pass.
- **Promotion (sub-3):** AI promotion reply piece pass-through, human promotion still
  triggering exactly one reply, and promotion-without-piece throwing before any
  reply/notification — all pass.
- **Human-mode isolation (sub-4):** explicit Human, legacy `NULL` OpponentMode
  (normalized to Human), behaviour-identical-with-or-without-AI theory (Human + null),
  Computer-mode control, and zero-notifier/never-consults-AI on create — all pass.

## Failures

**None.** No test failed, so there was nothing to report-and-not-fix in this subtask.

## Captured full-suite output (detailed logger)

Pre-existing `GameServiceTests` (35 cases; names verbatim from the log):

```
  Passed ChessMvp.Domain.Tests.GameServiceTests.CreateGameAsync_ReturnsWaitingGameWithWhiteToken
  Passed ChessMvp.Domain.Tests.GameServiceTests.CreateGameAsync_SeedsHumanOpponentMode
  Passed ChessMvp.Domain.Tests.GameServiceTests.CreateGameAsync_ExplicitHumanMode_MatchesParameterlessBehavior
  Passed ChessMvp.Domain.Tests.GameServiceTests.CreateGameAsync_ComputerMode_CreatesActiveGameWithHumanWhiteAndAiBlack
  Passed ChessMvp.Domain.Tests.GameServiceTests.CreateGameAsync_ComputerMode_AISeatTokenResolvesToBlackForMoves
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_UnknownGame_ThrowsGameNotFoundException
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_AlreadyFull_ThrowsGameNotActiveException
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_ComputerGame_ThrowsGameNotActiveException
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_ComputerDiscriminatorAlone_RejectsJoin
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_LegacyNullDiscriminator_NormalizesToHumanAndAllowsJoin
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_OpenSeat_AssignsBlackTokenAndActivates
  Passed ChessMvp.Domain.Tests.GameServiceTests.JoinGameAsync_HumanModeOpenSeat_IsJoinable
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_UnknownGame_ThrowsGameNotFoundException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_TokenMatchesNeitherSeat_ThrowsInvalidSlotTokenException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_GameWaitingForPlayer2_ThrowsGameNotActiveException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_GameAlreadyEnded_ThrowsGameNotActiveException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_NotThatPlayersTurn_ThrowsNotYourTurnException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_PromotionMoveWithoutPromotionPiece_ThrowsPromotionRequiredException
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_IllegalMove_ThrowsAndDoesNotSaveOrNotify
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_LegalMove_UpdatesStateSavesAndNotifiesOnce
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_CheckmateMove_EndsGameWithCorrectResult
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_StalemateMove_EndsGameAsDraw
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_FiftyMoveDraw_EndsGameAsDraw
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_TriggersSynchronousAiReplyAfterHumanMove
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_ReturnsPostReplyStateWithSingleSave
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_EmitsExactlyOneNotificationForTheWholeTurn
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_PromotionReply_PassesChosenPieceThrough
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_AiHasNoLegalMove_AppliesNoReplyButStillSavesAndNotifiesOnce
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_HumanMoveEndsGame_DoesNotConsultAi
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_ComputerGame_AiReplyDeliversCheckmate_EndsGameWithCorrectResult
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_HumanAndLegacyNullOpponentMode_DoNotTriggerAiReply(opponentMode: Human)
  Passed ChessMvp.Domain.Tests.GameServiceTests.SubmitMoveAsync_HumanAndLegacyNullOpponentMode_DoNotTriggerAiReply(opponentMode: null)
  Passed ChessMvp.Domain.Tests.GameServiceTests.GetGameAsync_UnknownGame_ThrowsGameNotFoundException
  Passed ChessMvp.Domain.Tests.GameServiceTests.GetMoveHistoryAsync_UnknownGame_ThrowsGameNotFoundException
  Passed ChessMvp.Domain.Tests.GameServiceTests.GetMoveHistoryAsync_KnownGame_ReturnsMovesFromRepository
```

New sub-2 suite `GameServiceComputerGameReplyFlowTests` (5):

```
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameReplyFlowTests.ScenarioA_ComputerGame_QualifyingAiReply_EmitsExactlyOneNotificationForTheTurn
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameReplyFlowTests.ScenarioB_ComputerGame_AiReportsNoLegalMove_AppliesNoReplyAndStillNotifiesOnce
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameReplyFlowTests.ScenarioC_ComputerGame_HumanMoveEndsGame_SkipsReplyAndNotifiesOnce
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameReplyFlowTests.CreateGameAsync_ComputerMode_NoReplyExpected_ZeroNotifications
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameReplyFlowTests.SubmitMoveAsync_ComputerGame_RejectedHumanMove_NoReplyExpected_ZeroNotifications
```

New sub-3 suite `GameServiceComputerGameFlowTests` (5):

```
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameFlowTests.SubmitMoveAsync_ComputerGame_ScenarioD_CheckmatedGame_TriggersNoAiReplyAndZeroNotifications
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameFlowTests.SubmitMoveAsync_ComputerGame_ScenarioE_StalematedGame_TriggersNoAiReplyAndZeroNotifications
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameFlowTests.SubmitMoveAsync_ComputerGame_PromotionReply_ProducesExactlyOneAiReplyAndSingleNotification
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameFlowTests.SubmitMoveAsync_ComputerGame_HumanPromotionMove_StillProducesExactlyOneAiReplyAndSingleNotification
  Passed ChessMvp.Domain.Tests.GameServiceComputerGameFlowTests.SubmitMoveAsync_ComputerGame_PromotionMoveWithoutPiece_ThrowsBeforeAnyReplyOrNotification
```

New sub-4 suite `GameServiceHumanModeIsolationTests` (6):

```
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.SubmitMoveAsync_ExplicitHumanOpponentMode_TriggersNoAiReply
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.SubmitMoveAsync_LegacyNullOpponentMode_IsTreatedAsHumanAndTriggersNoAiReply
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.SubmitMoveAsync_HumanMode_BehaviourIdenticalWhetherOrNotAnAiIsWired(opponentMode: Human)
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.SubmitMoveAsync_HumanMode_BehaviourIdenticalWhetherOrNotAnAiIsWired(opponentMode: null)
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.SubmitMoveAsync_ComputerOpponentMode_Control_TheSameFixturesDoRecordAiReply
  Passed ChessMvp.Domain.Tests.GameServiceHumanModeIsolationTests.CreateGameAsync_HumanMode_RecordsZeroNotifierCallsAndNeverConsultsTheAi
```

Other pre-existing suites (66 cases, all Passed — each listed individually in the captured
log; names abbreviated here only for brevity):

```
  Passed ChessMvp.Domain.Tests.DependencyInjectionTests.Container_ResolvesGameService_FromTheSameScopeAsIChessAi (+ 2 more)
  Passed ChessMvp.Domain.Tests.HeuristicChessAiTests.*                  (17 tests)
  Passed ChessMvp.Domain.Tests.HeuristicChessAiSelectionTests.*         ( 8 tests)
  Passed ChessMvp.Domain.Tests.GerasimleoChessRulesEngineAdapterTests.* (35 cases)
```

Runner summary (verbatim):

```
Test Run Successful.
Total tests: 114
     Passed: 114
 Total time: 0.3522 Seconds
```
