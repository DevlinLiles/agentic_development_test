# Automated Pre-Human Review — PR #25

**Verdict: REQUEST_CHANGES**

## Scope

Reviewed the PR diff (3 files, +646 lines):
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (new interface)
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (new implementation)
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` (new tests)

The new code is well-structured, self-contained (no third-party engine dependency), and
deterministic. FEN parsing degrades gracefully to a neutral `0` on malformed input, and the
material/PCS evaluation is genuinely antisymmetric. However, the mobility term breaks the
contract that the interface and class documentation promise, and the new test suite
asserts behavior that contradicts that same contract. These are actionable and should be
fixed before a human review.

---

## Findings

### 1. Correctness — Documented antisymmetry invariant is violated by the mobility term (and the test proves it)

**File:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (interface `<remarks>`,
lines ~22-24) and `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (class `<remarks>`,
lines ~12-16).

**What is wrong:**
Both the interface and the implementation class document the contract as antisymmetric:

> `Score(board, White) == -Score(board, Black)`  (interface `<remarks>`)
> "The implementation is antisymmetric, so `Score(board, White) == -Score(board, Black)`" (class `<remarks>`)

The implementation does **not** satisfy this. In `Score` (HeuristicEvaluator.cs), after computing
the antisymmetric material/placement score `whiteScore` and perspective-negating it for Black, the
method adds a **side-relative** mobility term:

```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

Because mobility is counted for `side` only and added (never subtracted), this term is the **same
sign for both colors** whenever the two sides have equal mobility. The class remarks even hedge
"for the material and placement components," but the interface XML doc and the class summary state
the full antisymmetry claim unconditionally, so the published contract is false.

Concretely, for the starting position both sides have equal pseudo-legal move counts, so:

```
Score(StartingFen, White) == +M   (M > 0, mobility only — material/placement are 0)
Score(StartingFen, Black) == +M   (NOT -M)
```

This is `White == Black`, i.e. `White == -Black` only if both are 0. The invariant
`Score(board, White) == -Score(board, Black)` is therefore **violated** for any symmetric or
near-symmetric position. Any caller (e.g. a negamax/minimax AI player) that relies on the
documented antisymmetry to derive the opponent's score by negation will get wrong move ordering.

**How to fix (pick one, keep implementation and docs consistent):**
- Either make mobility antisymmetric too: count `mobility(side) - mobility(opponent)` and add the
  side-relative result (so the whole `Score` is again antisymmetric), **or**
- Correct the documentation on **both** the interface and the class to state the invariant only
  holds for the material + piece-square components, and explicitly note that the mobility term is
  side-relative and not antisymmetric. State the actual contract: `Score(board, White) != -Score(board, Black)` in general.

The documentation fix is the lower-risk option since the side-relative mobility may be intentional,
but whichever is chosen the **docs and the behavior must agree**.

### 2. Quality — Test asserts behavior that contradicts the documented invariant

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`,
`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` (lines ~22-28).

**What is wrong:**
The test asserts:

```csharp
Assert.True(whiteScore > 0, "Starting position should yield a positive mobility-only score.");
Assert.Equal(whiteScore, blackScore);
```

`Assert.Equal(whiteScore, blackScore)` with both `> 0` is correct **for the actual implementation**,
but it is **incompatible** with the contract documented in Finding 1
(`Score(board, White) == -Score(board, Black)`), which would require `whiteScore == -blackScore`.
A test that encodes the opposite of the documented invariant is a smell: either the contract or the
test is wrong, and a future developer reading the interface will trust the docs over the test.

Additionally, there is no test that exercises the **opponent-relative** view of an asymmetric
position to pin down the intended behavior (e.g. a position where White is strictly ahead). Without
such a test the side-relative-mobility behavior and the (broken) antisymmetry claim are both
unverified for the asymmetric case — which is exactly the case an AI consumer cares about.

**How to fix:**
- Reconcile the test with whichever contract is chosen in Finding 1. If the docs are corrected to
  "mobility is side-relative, not antisymmetric," keep the `Equal` assertion but add a comment that
  this is *because* mobility breaks antisymmetry, and add a test on a clearly asymmetric position
  asserting the documented relationship (e.g. `Score(asym, White) > 0 && Score(asym, Black) < 0`
  for material, plus an explicit note on the mobility contribution).
- If mobility is made antisymmetric (Finding 1, option 1), change the test to assert
  `whiteScore == -blackScore` (which for the starting position forces `whiteScore == 0`, so the
  `> 0` assertion must also change).

### 3. Quality — Pawn mobility counts empty diagonals as moves, overstating pawn mobility asymmetrically

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
`CountPawnMoves` (the diagonal-capture block, ~lines `foreach (var delta in new[] { -1, 1 })`).

**What is wrong:**
The comment says pawn diagonals are counted "including empty target squares, which is the
pseudo-legal relaxation: a pawn 'attacks' the diagonal regardless of occupancy." That is a defensible
choice, but note the asymmetry vs. every other piece type: knights, kings, and sliders only count a
target square when it is empty **or** holds an enemy piece — they do **not** count squares they
merely attack but cannot move to. For those pieces an empty non-attacked-but-reachable square *is*
a move, which is correct. For pawns, an empty diagonal is **not** a legal or pseudo-legal move (a
pawn cannot move diagonally to an empty square); it is only an attack. Counting it as a move
overstates pawn mobility relative to the other piece types and relative to the literal
"pseudo-legal move" definition stated in the method's own doc comment ("moves a piece could make").
On a near-empty board this inflates the pawn term enough to distort ordering.

This is lower severity than Findings 1-2 (it is a heuristic tie-breaker), but it is an internal
inconsistency: the method documents "pseudo-legal moves" but counts pawn attacks that are not moves.

**How to fix:**
- Either restrict the pawn diagonal count to occupied enemy squares (matching the other piece types
  and the "pseudo-legal move" definition), **or** update the method doc comment to state that pawn
  diagonals are counted as attacks rather than moves, and do so consistently. Prefer the former for
  consistency with the rest of the method.

---

## Non-blocking observations (no change required)

- `TryParsePlacement` parses the side-to-move field into `out PlayerColor sideToMove` but the value
  is discarded (`out _` at the call site). The mobility term is keyed off the **requested** `side`,
  not the FEN side-to-move, which is a reasonable design choice for a position evaluator; the unused
  parse is just dead output. Consider dropping the `out sideToMove` to avoid implying it is used.
- `KingValue = 20000` is fine for a material guard, but since the evaluator never actually removes
  kings, the value never enters a trade comparison; harmless but effectively cosmetic.

---

## Summary

The implementation is solid and the FEN parsing/PCS logic is correct and antisymmetric. The blocker
is the **contract/behavior mismatch**: the interface promises `Score(board, White) ==
-Score(board, Black)`, the mobility term breaks that, and the new test asserts the *opposite* of the
documented invariant without a reconciling test for the asymmetric case. Reconcile the docs and the
mobility term (Finding 1), align the tests (Finding 2), and resolve the pawn-mobility inconsistency
(Finding 3). Recommended state: **REQUEST_CHANGES**.
