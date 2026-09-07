# Automated Pre-Human Review — PR #15

**Verdict:** `APPROVE`

**One-line rationale:** PR #15 is a focused CI/test-hardening fixup — it swaps a flaky, retireable SQL Server CU tag for the stable `2022-latest` rolling image the app actually runs, adds the one `using` directive that the test compilation was missing, and corrects a flaky evaluator-call assertion that double-counted the substitute setup call — all sound, minimal, and well-commented.

---

## Scope reviewed

PR #15 remediates CI breakage that surfaced after the PR #14 (VsAi feature) integration. The
substantive diff (three files, no contract or product-logic changes):

- `server/tests/ChessMvp.Api.Tests/ChessApiFactory.cs` — `MsSqlContainer` image tag changed from
  `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` to
  `mcr.microsoft.com/mssql/server:2022-latest`, with an explanatory comment.
- `server/tests/ChessMvp.Domain.Tests/ChessAiPlayerRegistrationTests.cs` — adds
  `using ChessMvp.Infrastructure.ChessRulesEngine;`.
- `server/tests/ChessMvp.Domain.Tests/HeuristicChessAiPlayerTests.cs` — in
  `SelectMoveAsync_EvaluatesEveryLegalMoveAtOnePly`, replaces
  `Assert.Equal(legalMoveCount, evaluator.ReceivedCalls().Count())` with
  `evaluator.Received(legalMoveCount).Evaluate(Arg.Any<string>())`, plus an explanatory comment.

Base: the PR #14 integration point (`a3610e7`, the epic-story merge carrying the VsAi work).
Head: the consolidated fix tips (`d6bdb26` SQL/CI fix chain and `4ad18b1` assertion fix).

---

## Correctness

- **Stable SQL image tag is the right fix.** `2022-CU14-ubuntu-22.04` is a specific cumulative-
  update tag that MCR can retire/remove over time; when it disappears, `MsSqlBuilder(...).Build()`
  fails to pull and every integration test that depends on `ChessApiFactory` (the whole
  `ChessMvp.Api.Tests` web-application-factory suite) breaks. Pinning to `2022-latest` — the same
  image `docker-compose.yml` uses for the app's SQL service — removes that single point of failure
  and keeps the test environment consistent with the runtime environment. The comment correctly
  documents the rationale.
- **Missing `using` is genuinely required.** `ChessAiPlayerRegistrationTests` references
  `GerasimleoChessRulesEngineAdapter` in four `services.AddSingleton<IChessRulesEngine,
  GerasimleoChessRulesEngineAdapter>()` registrations. That type lives in
  `ChessMvp.Infrastructure.ChessRulesEngine`, which was not previously imported in this file, so
  the test project would not compile without this directive. The addition is load-bearing, not
  dead/redundant.
- **Assertion fix is correct and removes a real flake.** The old
  `Assert.Equal(legalMoveCount, evaluator.ReceivedCalls().Count())` counted *every* entry in the
  NSubstitute call log, including the call recorded when the `.Returns(0.0)` setup is itself
  invoked/recorded during configuration. That inflates the raw count by one relative to the 20
  real `Evaluate` invocations made during the 1-ply search, so the equality against
  `legalMoveCount` (20 for the starting position) was off-by-one. Switching to
  `evaluator.Received(legalMoveCount).Evaluate(Arg.Any<string>())` asserts exactly that the real
  search issued `legalMoveCount` matching calls — NSubstitute's `Received(n)` counts invocations
  matching the specified call spec, not raw log entries, so the setup-time recording no longer
  contaminates the count. The comment captures the root cause accurately. No other call-count
  assertion in the file has this shape (the promotion-under-promotion test uses a `.Returns(call
  => ...)` lambda and asserts on the *selected* move, not on call count), so this is the only
  site needing the fix.

## Quality

- Minimal, surgical diff. No accidental complexity, no dead code introduced. Each change is
  accompanied by a comment explaining *why*, which raises the reviewability of otherwise-trivial
  test-infra churn.
- No new behavior is introduced, so no new behavioral tests are required; the changes *repair*
  existing tests (DI-registration tests compile; the 1-ply evaluate-count assertion is no longer
  flaky) rather than adding untested surface area.
- The fix is appropriately scoped: it does not restructure `ChessApiFactory` or the assertion
  test, and does not touch production (`ChessMvp.Api` / `ChessMvp.Domain` / `ChessMvp.Infrastructure`
  source) at all — correct, since the CI breakage is entirely in the test harness.

## Security

- No injection, authz, or secret-handling surface touched. The image tag is a hard-coded,
  non-user-controlled string literal of a public vendor image; no user input flows into it. The
  `using` directive and the assertion change are test-only and touch no request handling,
  persistence, or auth path. No secrets are introduced or logged.

---

## Notes

- "CI is green" is meaningful for this PR: the changes exist precisely to make CI stay green
  reliably (retireable-tag removal eliminates an intermittent pull failure; the `using` fix
  restores compilation of the DI-registration test project; the assertion fix removes an
  off-by-one that would fail once NSubstitute records the setup call). The green signal now
  actually exercises the code this PR touches.
- No code changes were made by this review task.

**Recommendation:** Approve. Ready for a human reviewer.
