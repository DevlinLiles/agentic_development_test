# Automated Pre-Human Review — PR #26

**Verdict: REQUEST_CHANGES**

## Scope

Reviewed the PR diff (3 files, +646 lines) introducing a static position evaluator:
- `IHeuristicEvaluator.cs` (new interface)
- `HeuristicEvaluator.cs` (new implementation: material + piece-square tables + mobility)
- `HeuristicEvaluatorTests.cs` (new unit tests)

The evaluation logic itself (FEN parsing, material/PCS scoring, pseudo-legal move counting)
is internally coherent and the tests are deterministic. However, the diff has a **build-breaking
correctness defect** that the prior logic-only reviews of this same code missed, plus the
contract/behavior and test inconsistencies carried over from those reviews. None of the new code
actually participates in the project build or test run as committed, so "CI is green" reflects the
code being absent from the build, not verified. These are actionable and must be fixed before a
human review.

---

## Findings

### 1. Correctness (BLOCKER) — New files are committed under a nested `.worktrees/` path, not the project source tree; the interface/impl never compile and the tests never run

**File / lines:** All three added files, as recorded in the commit:

```
.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs
.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs
.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs
```

(`git diff --name-only <base> <head>` confirms every path is prefixed with
`.worktrees/01m1vy27727029etgw6qp81jdc/`.)

**What is wrong:**
The files were committed at paths nested inside another linked worktree's directory
(`.worktrees/01m1vy27727029etgw6qp81jdc/server/...`) instead of the canonical project locations:

- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Verified against the checked-out tree: `server/src/ChessMvp.Domain/Abstractions/` contains only the
five pre-existing files (`IChessRulesEngine.cs`, `IGameNotifier.cs`, `IGameRepository.cs`,
`MoveApplicationResult.cs`, `MoveFailureReason.cs`) — **no `IHeuristicEvaluator.cs`** — and
`server/src/ChessMvp.Domain/Services/` contains only `GameService.cs` / `IGameService.cs` —
**no `HeuristicEvaluator.cs`**. Likewise `server/tests/ChessMvp.Domain.Tests/` has no
`HeuristicEvaluatorTests.cs`.

Consequences:
1. `HeuristicEvaluator` is never compiled into `ChessMvp.Domain`, and `IHeuristicEvaluator` is not
   part of the `ChessMvp.Domain.Abstractions` namespace surface. Any consumer that was supposed to
   depend on this interface (e.g. an AI player) cannot resolve it.
2. The new test class is not part of the `ChessMvp.Domain.Tests` project, so the six new test cases
   are **never discovered or executed** by `dotnet test`. This is why CI is green: the tests do not
   exist as far as the test runner is concerned, so nothing can fail. "CI green" here is a false
   positive — the new behavior is unverified.
3. The stranded copy under `.worktrees/...` is effectively dead code in the repository; it is not
   covered by the project's build/CI and will silently rot or be deleted when that worktree is
   cleaned up.

This is the highest-severity issue: the deliverable of the PR (a compilable, tested evaluator) is
not actually delivered into the project.

**How to fix:**
Move the three files from the nested worktree paths to the canonical project paths (remove the
`.worktrees/01m1vy27727029etgw6qp81jdc/` prefix) and re-commit, e.g.:

```
git mv .worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs \
       server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs
git mv .worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs \
       server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs
git mv .worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs \
       server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs
```

Then confirm `dotnet build` on `ChessMvp.Domain` compiles the new files and `dotnet test` on
`ChessMvp.Domain.Tests` actually runs the six `HeuristicEvaluatorTests` cases (check the test
count in the runner output). If the nested `.worktrees/` directory is otherwise empty after the
move, remove it.

### 2. Correctness — Documented antisymmetry invariant is violated by the mobility term

**File:** `IHeuristicEvaluator.cs` (interface `<remarks>`) and `HeuristicEvaluator.cs`
(class `<remarks>`).

**What is wrong:**
Both the interface and the class document the contract as antisymmetric:

> `Score(board, White) == -Score(board, Black)`

The material and piece-square components are antisymmetric, but `Score` then adds a side-relative
mobility term:

```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

For the starting position both sides have equal pseudo-legal move counts, so:
`Score(StartingFen, White) == +M` and `Score(StartingFen, Black) == +M` (both positive), i.e.
`White == Black`, **not** `White == -Black`. The documented invariant is therefore false for any
symmetric or near-symmetric position. A caller (e.g. a negamax/minimax AI player) that derives the
opponent's score by negation — which the documented invariant invites — will get wrong ordering.

**How to fix (pick one, keep implementation and docs consistent):**
- Make mobility antisymmetric: add `MobilityWeight * (CountPseudoLegalMoves(grid, side) -
  CountPseudoLegalMoves(grid, opponent))` so the whole `Score` is antisymmetric again; **or**
- Correct the documentation on **both** the interface and the class to state that antisymmetry
  holds only for the material + piece-square components, and that the mobility term is side-relative
  and explicitly **not** antisymmetric (`Score(board, White) != -Score(board, Black)` in general).

### 3. Quality — Test asserts behavior that contradicts the documented invariant

**File:** `HeuristicEvaluatorTests.cs`,
`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`.

**What is wrong:**
The test asserts `Assert.Equal(whiteScore, blackScore)` with both `> 0`. This is correct for the
*actual* implementation but **incompatible** with the contract documented in Finding 2
(`Score(board, White) == -Score(board, Black)`), which would require `whiteScore == -blackScore`.
A test that encodes the opposite of the documented invariant is a smell: either the contract or the
test is wrong, and a future developer will trust the interface docs over the test.

There is also no test exercising the **opponent-relative** view of a clearly asymmetric position to
pin down the intended side-relative behavior — exactly the case an AI consumer cares about.

**How to fix:**
Reconcile the test with whichever contract is chosen in Finding 2. If the docs are corrected to
"mobility is side-relative, not antisymmetric," keep the `Equal` assertion with a comment explaining
*why* (mobility breaks antisymmetry) and add a test on an asymmetric position asserting the
documented relationship (e.g. `Score(asym, White) > 0 && Score(asym, Black) < 0` for the material
component, with an explicit note on the mobility contribution). If mobility is made antisymmetric,
change the test to assert `whiteScore == -blackScore` (which for the starting position forces
`whiteScore == 0`, so the `> 0` assertion must also change).

### 4. Quality — Pawn mobility counts empty diagonals as moves, overstating pawn mobility inconsistently

**File:** `HeuristicEvaluator.cs`, `CountPawnMoves` (the diagonal-capture block).

**What is wrong:**
The comment says pawn diagonals are counted "including empty target squares, which is the
pseudo-legal relaxation: a pawn 'attacks' the diagonal regardless of occupancy." Every other piece
type (knight, king, sliders) counts a target only when it is empty **or** holds an enemy piece —
i.e. a square the piece can actually move to. For a pawn, an empty diagonal is **not** a legal or
pseudo-legal *move* (a pawn cannot move diagonally to an empty square); it is only an *attack*.
Counting it as a move overstates pawn mobility relative to the other piece types and relative to the
literal "pseudo-legal move" definition in the method's own doc comment ("moves a piece could make").
On a near-empty board this inflates the pawn term enough to distort ordering.

Lower severity than Findings 1-3 (heuristic tie-breaker), but it is an internal inconsistency:
the method documents "pseudo-legal moves" yet counts pawn attacks that are not moves.

**How to fix:**
Either restrict the pawn diagonal count to occupied enemy squares (matching the other piece types
and the "pseudo-legal move" definition), **or** update the method doc comment to state that pawn
diagonals are counted as attacks rather than moves and apply that consistently. Prefer the former
for consistency with the rest of the method.

---

## Non-blocking observations (no change required)

- `TryParsePlacement` parses the side-to-move field into `out PlayerColor sideToMove` but the value
  is discarded (`out _` at the call site). The mobility term is keyed off the **requested** `side`,
  not the FEN side-to-move, which is a reasonable design choice for a position evaluator; the unused
  parse is just dead output. Consider dropping the `out sideToMove` to avoid implying it is used.
- `KingValue = 20000` is fine as a material guard, but since kings are never removed, the value
  never enters a trade comparison; harmless but effectively cosmetic.

---

## Summary

The evaluation logic is sound, but the PR does not deliver compilable or tested code into the
project: **all three files are stranded under a nested `.worktrees/...` path (Finding 1)**, so the
interface/implementation never compile and the six new tests never run — "CI green" is a false
positive. Additionally, the documented antisymmetry invariant is broken by the side-relative
mobility term (Finding 2), the new test asserts the opposite of that documented invariant without a
reconciling asymmetric-case test (Finding 3), and pawn mobility counts non-move attacks
inconsistently with the other piece types (Finding 4). Fix the commit paths first (Finding 1) so
the code actually builds and the tests actually execute, then reconcile the contract/behavior
(Findings 2-3) and the pawn-mobility inconsistency (Finding 4). Recommended state:
**REQUEST_CHANGES**.
