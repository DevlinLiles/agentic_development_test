# Automated Pre-Human Review — PR #45

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
violation rather than guarding against it. A second finding flags dead code. Two
actionable findings below. No code changes were made by this review; remediation
should be dispatched as a separate bounded fix task.

This diff is materially identical to the one reviewed under PR #19 (same head
`07fc8dc`, same base `43a5502`, same three files). The findings below were
re-derived independently against the current source and remain present, so the
prior REQUEST_CHANGES verdict carries forward: the issues were not addressed in
this PR.

---

## Finding 1 — Broken antisymmetry invariant (correctness)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** `Score(...)` — the perspective/mobility lines
(`var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;`
followed by
`perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);`).

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

Concrete counterexample on the **starting** position (material and piece-square cancel
to 0 for both sides; pseudo-legal mobility is 20 per side — 16 pawn moves + 4 knight
moves):
- `Score(start, White) = 0 + 2·20 = +40`
- `Score(start, Black) = -0 + 2·20 = +40`
- `-Score(start, Black) = -40 ≠ +40 = Score(start, White)`  ← invariant violated.

The new test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
*asserts* `whiteScore == blackScore > 0`, which is only true *because* the invariant is
broken — it enshrines the bug instead of catching it. (A correct antisymmetric mobility
term would make `Score(start, ·) == 0`.) The test `Score_MirroredPosition_ScoresEquallyForMirroredColor`
likewise passes only by coincidence of equal mirrored mobilities on that particular
symmetric board, not because the contract holds.

**How to fix (one of):**
- Make mobility antisymmetric:
  `perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent))`,
  then negate the whole perspective for Black as already done for material. This restores
  `Score(b, W) == -Score(b, B)` and makes the starting-position score 0.
- Or, if one-sided mobility is intentional, correct the interface/implementation
  documentation to drop the antisymmetry claim and update the starting-position test
  expectation accordingly (and reconcile `Score_MirroredPosition_ScoresEquallyForMirroredColor`,
  which passes only by coincidence).

Either way the test suite must be reconciled: today the tests depend on the exact
behavior that breaks the documented contract.

---

## Finding 2 — Dead code: parsed side-to-move is never used (quality)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** `TryParsePlacement` parses and returns `out PlayerColor sideToMove`
(the `if (fields.Length >= 2) { sideToMove = fields[1] == "b" ? ... }` block), but the
sole caller discards it: `TryParsePlacement(board, out var grid, out _)` in `Score`.

**What is wrong:**
The FEN side-to-move token is parsed and returned, then thrown away by the only caller.
Mobility is counted for the *requested* `side` argument, not for the side to move in the
FEN, so the parsed token has no effect on any result. This is dead code that misleads
readers into thinking side-to-move influences the score.

**How to fix:**
- Remove the `out PlayerColor sideToMove` parameter from `TryParsePlacement` and the
  `fields[1]` parsing block, changing the signature to
  `TryParsePlacement(string fen, out Piece?[,] grid)`; update the call site from
  `out _` to drop the third argument. Or,
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
  with bounds-checked grid access (`InBounds` uses unsigned cast guards) and no
  I/O/authz/secrets. Malformed input degrades gracefully to 0, matching the interface
  contract. ✓
