# Automated Pre-Human Review — PR #19

**Reviewer:** automated review pass (reviewer agent)
**PR head commit:** `07fc8dc` — "Add heuristic evaluator with material, piece-square tables, and mobility scoring"
**Base:** parent `43a5502`
**Diff scope:** 3 new files, +646 lines
  - `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
  - `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
  - `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

## Verdict: REQUEST_CHANGES

CI is green, but the implementation violates a correctness invariant that the new
interface documents as a contract, and one of the new unit tests codifies that
violation rather than guarding against it. Two actionable findings below. No code
changes were made by this review; remediation should be dispatched as a separate
bounded fix task.

---

## Finding 1 — Broken antisymmetry invariant (correctness)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** ~123–124 (misleading comment) and ~131 (`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);`) inside `Score(...)`.

**What is wrong:**
The interface contract (`IHeuristicEvaluator.cs`, `<remarks>`) explicitly states the
implementation is antisymmetric:

> A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`.

The material + piece-square components are antisymmetric (computed once from White's
perspective in `EvaluateForWhite` and negated for Black). But the **mobility** term is
not: it is computed *only for the requested side* and *added* (never subtracted for the
opponent):

```csharp
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

So `Score(b, White) = Wmat + 2·Wmob` and `Score(b, Black) = -Wmat + 2·Bmob`. These are
negatives of each other only when `Wmob == Bmob`, which is false on essentially every
non-symmetric position.

Concrete counterexample on the **starting** position (material 0 for both):
- `Score(start, White) = 0 + 2·(white mobility) = +40`
- `Score(start, Black) = -0 + 2·(black mobility) = +40`
- `-Score(start, Black) = -40 ≠ +40 = Score(start, White)`  ← invariant violated.

The existing test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
*asserts* `whiteScore == blackScore > 0`, which is only true *because* the invariant is
broken — it enshrines the bug instead of catching it. (A correct antisymmetric
mobility term would make `Score(start, ·) == 0`.)

**How to fix (one of):**
- Make mobility antisymmetric: `perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent))`, then negate for Black as already done for material. This restores `Score(b, W) == -Score(b, B)` and makes the starting-position score 0.
- Or, if one-sided mobility is intentional, correct the interface/implementation doc to drop the antisymmetry claim and update the starting-position test expectation accordingly (but then `Score_MirroredPosition_ScoresEquallyForMirroredColor` should also be reconciled — it passes only by coincidence of equal mirrored mobilities).

Either way the test suite must be reconciled: today the tests depend on the exact
behavior that breaks the documented contract.

---

## Finding 2 — Dead code: parsed side-to-move is never used (quality)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** `TryParsePlacement` sets `out PlayerColor sideToMove` (~line 413–416,
`if (fields.Length >= 2) { sideToMove = fields[1] == "b" ? ... }`), but the sole caller
discards it: `TryParsePlacement(board, out var grid, out _)` (~line 120 in `Score`).

**What is wrong:**
The FEN side-to-move token is parsed and returned, then thrown away by the only caller.
Mobility is counted for the *requested* `side` argument, not for the side to move in the
FEN, so the parsed token has no effect on any result. This is dead code that misleads
readers into thinking side-to-move influences the score.

**How to fix:**
- Remove the `out PlayerColor sideToMove` parameter from `TryParsePlacement` and the
  `fields[1]` parsing block, changing the signature to `TryParsePlacement(string fen, out Piece?[,] grid)`; update the `out _` call site to `out var grid`. Or,
- If side-to-move is intended to matter (e.g. counting mobility only for the side on
  move), wire it through and add a test for it. Today it is neither used nor tested as
  behavior.

---

## Notes (non-blocking, no change requested)

- `CountPawnMoves` counts both diagonal squares as mobility even when empty
  (documented as a "pseudo-legal relaxation"). This gives every pawn a flat +2
  mobility floor regardless of real attacks. Consistent and documented, so not
  actionable, but worth noting as a mild quality smell.
- `CountKnightMoves` and `CountKingMoves` are identical except for the offset array;
  they could collapse into one `CountOffsetMoves(..., offsets)` helper. Minor DRY.
- No security concerns: the evaluator is a pure function over a validated FEN string
  with bounds-checked grid access and no I/O/authz/secrets. Malformed input degrades
  gracefully to 0, matching the interface contract. ✓
