# Automated Pre-Human Review — PR #29

**Verdict: REQUEST_CHANGES**

PR #29 adds `IHeuristicEvaluator` and a `HeuristicEvaluator` implementation (material + piece-square tables + a mobility term) with unit tests. The material and piece-square-table logic is sound and well documented, but the mobility term breaks the antisymmetry contract the interface documents, one of the "malformed board" test cases is not actually malformed and would fail if the tests were executed, and the FEN side-to-move is parsed but never used (dead code).

## Findings

### 1. [Correctness / Contract] Mobility term breaks the documented antisymmetry invariant
**Files:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (lines 13-14); `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (line 136; misleading comment lines 125-127)

The interface states: *"A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."* The implementation honors this for material + piece-square tables via the `whiteScore` / `-whiteScore` flip at line 130, but then adds a **side-relative** mobility term at line 136:

```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

`CountPseudoLegalMoves` returns a non-negative count for the requested `side`, so for the **same** board:
- `Score(board, White) = whiteScore + M·mobW`
- `Score(board, Black) = -whiteScore + M·mobB`

Antisymmetry would require `Score(board, White) == -Score(board, Black)`, i.e. `M·mobW == -M·mobB`, which is impossible whenever either side has any legal/pseudo-legal move. The invariant is therefore broken for every non-terminal position. This matters because a negamax-style consumer (the conventional use of a heuristic evaluator) relies on `Score(b, Black) == -Score(b, White)` and would compute wrong scores with this implementation. The in-implementation comment at lines 125-127 ("keeps the function antisymmetric") is inaccurate — it is only true *before* the mobility addition at line 136.

The existing test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` (`HeuristicEvaluatorTests.cs` lines 24-25) actually encodes the violation: it asserts `Assert.Equal(whiteScore, blackScore)` together with `whiteScore > 0` for the **same** board, which is impossible under antisymmetry (it would force `whiteScore == 0`).

**How to fix (preferred):** make the mobility term antisymmetric, e.g.
```csharp
var opponent = side == PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));
```
and update the starting-position test to assert `whiteScore == -blackScore` (and `whiteScore == 0` for the symmetric start, or adjust expectations accordingly). Alternatively, if side-relative mobility is intentional, remove/correct the antisymmetry claim in the interface and the misleading comment at lines 125-127 so consumers do not rely on it.

### 2. [Correctness / Test] "Malformed board" test case is not malformed and would fail if run
**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (InlineData line 99; assertion line 103)

```csharp
[InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")] // missing ranks / malformed
public void Score_MalformedBoard_ReturnsNeutralZero(string board)
{
    Assert.Equal(0, _sut.Score(board, PlayerColor.White));
}
```

The comment says "missing ranks / malformed", but `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"` is a **valid 8-rank placement** — exactly the starting position minus the `w KQkq - 0 1` suffix. `TryParsePlacement` deliberately parses only the placement field (`fields[0]`), so it accepts this input and returns `true`. `Score` then returns a **positive mobility-based value** (the symmetric starting position yields a White pseudo-legal move count of ~34 → `2·34 ≈ 68`, not 0). `Assert.Equal(0, …)` at line 103 therefore fails.

This demonstrates the new tests were never actually executed — consistent with this repo's known environment issue: the `*.csproj` files target `net10.0` but only the .NET 8 SDK is installed, so `dotnet test` cannot build. "CI is green" is not evidence these new tests pass.

**How to fix:** replace that `InlineData` with a genuinely malformed board — e.g. a 7-rank string `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP"` or a rank that overflows 8 files such as `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPPP/RNBQKBNR"`. If placement-only FENs are meant to be accepted, assert the resulting non-zero mobility score instead of 0.

### 3. [Quality] Dead side-to-move parsing
**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (`Score` line 120 discards it; `TryParsePlacement` parses `fields[1]` into `out PlayerColor sideToMove`)

`TryParsePlacement` parses the FEN side-to-move token (`fields[1] == "b" ? PlayerColor.Black : PlayerColor.White`) into an `out` parameter, but `Score` discards it with `out _` at line 120. The parsed value is never used — mobility is counted for the requested `side`, not the FEN's side to move. This is dead code and a recurring pattern previously flagged on PR #19 ("dead side-to-move code").

**How to fix:** either drop the `out PlayerColor sideToMove` parameter and its parsing block, or actually use the side-to-move (e.g. count mobility for the side that is actually to move), which would also interact with finding #1.

## Non-blocking note
- `CountPawnMoves` counts empty diagonal squares as pawn "moves" (the documented pseudo-legal relaxation). This is symmetric across colors and explicitly called out in comments, so it does not affect color-fairness, but it inflates pawn mobility relative to true pseudo-legal pawn move counts. Acceptable for a tie-breaker; noting it only so reviewers are aware it is not standard pseudo-legal pawn mobility.
