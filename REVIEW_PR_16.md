# Automated Pre-Human Review — PR #16

**Verdict:** `REQUEST_CHANGES`

**One-line rationale:** PR #16 adds a heuristic position evaluator, but the three new files are committed under a nested `.worktrees/<id>/server/...` path instead of `server/...`, so they are excluded from every built project — "CI green" is vacuous for this change (the new code is never compiled or tested); and, once that path defect is remedied, two concrete code-level defects surface (an evaluator that violates its own antisymmetry contract, and a "malformed board" test that feeds a valid FEN).

---

## Scope reviewed

PR #16 introduces a heuristic position evaluator for the AI opponent:

- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` — new interface (`Score(string board, PlayerColor side)`).
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` — implementation: material values, Michniewski piece-square tables, and a pseudo-legal mobility term.
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` — six unit tests.

Base: the PR #15 integration point. Head: the "Add heuristic evaluator with material, piece-square tables, and mobility scoring" tip (`07fc8dc`) atop the auto-commit (`43a5502`).

---

## Findings

### 1. Files committed at the wrong path — excluded from all built projects; CI green is vacuous (Correctness / CI integrity — blocker)

**What is wrong.** In the PR head tree, the three new files live under a nested worktree directory rather than under `server/`:

- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

This is verifiable against the commit: `git diff <base> <head> --name-only` lists those three at the `.worktrees/01m1vy27727029etgw6qp81jdc/server/...` path, while other churn in the same diff (e.g. `client/src/components/panels/OpponentTypeSelector.tsx`) is at the correct top-level `client/...` path — so this is not a uniform display artifact. Consequently the files are **not** picked up by the `ChessMvp.Domain` (`server/src/ChessMvp.Domain/*.csproj`) or `ChessMvp.Domain.Tests` (`server/tests/ChessMvp.Domain.Tests/*.csproj`) projects. `dotnet build` / `dotnet test` in CI never compile `HeuristicEvaluator.cs`, never compile `IHeuristicEvaluator.cs`, and never run `HeuristicEvaluatorTests.cs`. "CI is green" carries no signal for this change, and `IHeuristicEvaluator` is not available for the AI player / DI wiring to consume.

**How to fix.** Move the three files to their project locations and remove the misplaced copies:
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` → `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` → `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` → `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Then confirm `dotnet build` and `dotnet test` from `server/` actually compile and execute the new tests (the test runner should report `HeuristicEvaluatorTests`).

### 2. `HeuristicEvaluator.Score` violates the antisymmetry contract declared by `IHeuristicEvaluator` (Correctness — broken invariant)

**What is wrong.** `IHeuristicEvaluator.cs` (interface `<remarks>`) promises: *"A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."* The `HeuristicEvaluator` class summary repeats: *"The implementation is antisymmetric."*

The implementation does not satisfy this. In `HeuristicEvaluator.cs`, `Score(...)` computes:

```csharp
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
return perspective;
```

The mobility term is added for the **evaluated side only**, so:

- `Score(board, White) = whiteScore + 2·mob(White)`
- `Score(board, Black) = -whiteScore + 2·mob(Black)`
- `Score(board, White) + Score(board, Black) = 2·(mob(White) + mob(Black)) ≠ 0`

i.e. `Score(board, White) ≠ -Score(board, Black)` whenever either side has any pseudo-legal move. The class remarks even half-acknowledge this ("…for the material and placement components"), yet the interface contract and class summary state antisymmetry unconditionally. The test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` encodes the *non-antisymmetric* behaviour directly: it asserts `Assert.Equal(whiteScore, blackScore)` with `whiteScore > 0` (both equal ~40 for the start position), which requires `Score(W) == Score(B)` — the opposite of the documented `Score(W) == -Score(B)`. So the contract, the implementation, and the tests are mutually inconsistent.

A consumer that trusts the documented invariant (e.g. an AI player that scores `Score(after, side)` and infers `Score(after, opponent) == -Score(after, side)` instead of recomputing) will get wrong values.

**How to fix (choose one).**
- (Preferred) Make mobility differential so the invariant actually holds:
  `perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));`
  Then `Score(W) = whiteScore + 2(mobW − mobB)` and `Score(B) = −whiteScore + 2(mobB − mobW) = −Score(W)`. Update `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`: for the symmetric start position the score becomes `0`, so the `whiteScore > 0` / "IsPositiveFromMobility" expectation must change (e.g. assert `Equal(0, whiteScore)` and `Equal(0, blackScore)`, and rename the test).
- Or, keep the greedy-scoring design and correct the docs: remove the antisymmetry claim from `IHeuristicEvaluator.cs` and the `HeuristicEvaluator` class summary, and state explicitly that the score is perspective-relative (intended for `argmax Score(after, ownSide)` move selection), not a symmetric analysis function.

### 3. `Score_MalformedBoard_ReturnsNeutralZero` feeds a valid FEN and asserts `0` (Correctness — failing test, currently masked by finding #1)

**What is wrong.** In `HeuristicEvaluatorTests.cs`, the `[Theory]` `Score_MalformedBoard_ReturnsNeutralZero` includes:

```csharp
[InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")] // missing ranks / malformed
```

That string is **not** malformed — it is the standard starting-position placement: eight slash-separated ranks, each valid (`rnbqkbnr`, `pppppppp`, four `8`s, `PPPPPPPP`, `RNBQKBNR`). `TryParsePlacement` requires only the placement field (`fields[0]`); the side-to-move/castling/en-passant/clock tail is optional (`if (fields.Length >= 2)`), so a placement-only FEN parses successfully and `Score` returns a nonzero mobility score (material is symmetric → 0; plus `MobilityWeight · mob(White) > 0`). The assertion `Assert.Equal(0, _sut.Score(board, PlayerColor.White))` therefore **fails** once the test actually runs. It is only green today because finding #1 keeps the test out of the compiled/tested set.

**How to fix.** Use an input that is genuinely rejected by `TryParsePlacement`. Examples that return `false` (→ `0`): a rank with the wrong width, e.g. `"rnbqkbnr/ppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"` (seven black pawns → `fileIndex != 8` → rejected), or fewer than eight ranks, e.g. `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP"` (seven segments → `ranks.Length != 8` → rejected). Alternatively, if placement-only FENs *should* be rejected, require `fields.Length >= 2` in `TryParsePlacement` — but that would be a behavioural change to the parser, not just the test.

---

## Notes / non-blocking

- **Verify `ChessConstants` is reachable from the test project.** `HeuristicEvaluatorTests.cs` references `ChessConstants.StartingFen` (three call sites) via `using ChessMvp.Domain.Entities;`. I could not locate a definition of `ChessConstants`/`StartingFen` in the diff or the inspected server tree. If that type is not defined in `ChessMvp.Domain.Entities` (or a referenced test-support namespace), `ChessMvp.Domain.Tests` will not compile once finding #1 is remedied and the test file is part of the build. Confirm it exists; if not, add it (or use a literal FEN) as part of the fix.
- **Pawn mobility counts empty-square diagonals (documented, low severity).** `CountPawnMoves` counts a diagonal "capture" even when the target square is empty ("a pawn attacks the diagonal regardless of occupancy"). This is a documented pseudo-legal relaxation and is color-symmetric, so it does not break antisymmetry of the material/placement components, but it inflates pawn mobility relative to the convention used for other pieces. Acceptable as a tie-breaker; flagging only for awareness if the evaluator is later wired into move selection where pawn-mobility weighting matters.
- **No security surface.** The evaluator parses a FEN string with `Split` and bounds-checked array indexing; it returns `0` on parse failure rather than throwing. No SQL/command/eval, no user input flows directly into it beyond a position string, no secrets. No injection/authz/secret-handling concerns.
- No code changes were made by this review task.

**Recommendation:** Request changes. Findings #1 and #2 are blockers (CI integrity and a broken documented invariant); finding #3 is a failing test that is currently hidden by #1. Remediation should be dispatched as a bounded fix task.
