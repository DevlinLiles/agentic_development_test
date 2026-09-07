# PR #32 — Automated Pre-Human Review

**State:** REQUEST_CHANGES
**Base:** `43a5502`
**Head:** `07fc8dc` — "Add heuristic evaluator with material, piece-square tables, and mobility scoring"

## Summary

The PR adds a self-contained `HeuristicEvaluator` (material + piece-square tables + mobility) behind
an `IHeuristicEvaluator` abstraction, plus unit tests. The material/placement scoring is sound and
the tests are mostly relationship-based rather than brittle-constant, which is good. CI is green.

However, there is one material correctness defect: the documented antisymmetry invariant is broken
by the mobility term, and the tests encode the broken behavior rather than the contract.

---

## Findings

### 1. Correctness — Antisymmetry invariant broken by the mobility term

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** 125–136 (and the contract it violates at lines 13–18 / `IHeuristicEvaluator.cs:13–17`)

**What is wrong:**

Both the interface and the class XML doc promise antisymmetry:
> `Score(board, White) == -Score(board, Black)`

The material + piece-square part genuinely satisfies this: `EvaluateForWhite` returns a White-relative
score and Black's perspective is obtained by negating it (`side == Black ? -whiteScore : whiteScore`,
line 131), so for those terms `Score(W) == -Score(B)`.

But the mobility term is then added unconditionally from the *requested* side's perspective:

```csharp
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```

This counts moves **only for `side`**, always with a positive sign. It is never the negation of the
opponent's mobility. So on any position where the two sides have different mobility counts,
`Score(W) != -Score(B)`, and the invariant advertised in the public contract is false. Even on the
perfectly symmetric starting position the contract fails: `Score(W) == Score(B) == (some positive
number)`, whereas true antisymmetry would force `Score(W) == 0`.

The PR's own test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
(`HeuristicEvaluatorTests.cs:21–30`) confirms the break — it asserts `whiteScore > 0` **and**
`Assert.Equal(whiteScore, blackScore)`, i.e. `Score(W) == Score(B) > 0`, which is the opposite of
`Score(W) == -Score(B)`. The test encodes the broken behavior rather than the contract.

This is a logic/invariant error: an `IChessAiPlayer` consuming this evaluator under the documented
antisymmetry assumption (e.g. comparing `Score(after) == -Score(before)` across a side change, or
negating a child's score in a negamax search) would get wrong results whenever mobility differs
between sides — which is exactly when mobility is supposed to matter.

**How to fix:**

Pick one consistent contract and implement + test to match it:

- **Option A (honor antisymmetry):** Make mobility antisymmetric. Compute it from White's perspective
  and subtract Black's, e.g.
  ```csharp
  var whiteMobility = CountPseudoLegalMoves(grid, PlayerColor.White);
  var blackMobility = CountPseudoLegalMoves(grid, PlayerColor.Black);
  whiteScore += MobilityWeight * (whiteMobility - blackMobility);
  perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
  ```
  Then a symmetric position yields `0` for both sides, satisfying `Score(W) == -Score(B)`. Update the
  starting-position test to expect `whiteScore == blackScore == 0` (or just `==` and `0`).

- **Option B (drop the antisymmetry claim):** If you intend mobility to be a per-side, non-antisymmetric
  tie-breaker, remove the `Score(board, White) == -Score(board, Black)` claim from both the interface
  and class doc comments and document the actual semantics (the score favors the requested side's own
  mobility only). This is weaker and less composable for negamax-style callers, so Option A is
  preferred.

Either way, the test must assert the *contract*, not the current (buggy) output.

---

### 2. Quality / Correctness — `sideToMove` is parsed but unused (dead code + missed invariant check)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
**Lines:** 120, 367–368, 421

**What is wrong:**

`TryParsePlacement` parses the FEN side-to-move field into an `out PlayerColor sideToMove` (lines
421), and `Score` discards it with `out _` (line 120):

```csharp
if (!TryParsePlacement(board, out var grid, out _)) { return 0; }
```

The `sideToMove` is never used. This is dead code, but it is also a missed correctness opportunity:
mobility is computed for `sideToMoveFor: side` (the *scoring* perspective, line 136), i.e. it counts
the moves of whichever color the caller asks to score, regardless of whose turn it actually is in the
FEN. For a position where it is White to move but the caller asks `Score(board, Black)`, the evaluator
counts *Black's* pseudo-legal moves as Black's mobility — which is reasonable for a static evaluation,
but the fact that the real side-to-move was parsed and thrown away is confusing and invites a future
maintainer to rely on it or to introduce a bug.

**How to fix:**

Either (a) drop the `out PlayerColor sideToMove` parameter entirely and stop parsing `fields[1]`
(clean up the dead path), or (b) if mobility is intended to reflect whose turn it is, pass the parsed
`sideToMove` through and use it instead of the scoring-perspective `side` (and document that
decision). As written, parsing a value only to discard it is accidental complexity.

---

## Notes (non-blocking)

- `TryParsePlacement` uses `fen.Split((char[])null, …)` to split on any whitespace; that is correct
  for FEN. No issue, just noting the idiom is intentional.
- `CountPawnMoves` deliberately does not model en passant and counts empty-diagonal "attacks" as
  pseudo-legal moves — explicitly documented and acceptable for a mobility tie-breaker.
- Piece values and PSTs are the standard Michniewski "Simplified Evaluation Function" tables;
  transcribed correctly. No endgame King table is used (only `KingMiddleGameTable`), which is a
  reasonable simplification for this stage but worth a one-line note in the class doc.

---

## Verdict

REQUEST_CHANGES. Finding #1 is a real correctness/invariant defect that the PR's own tests
demonstrate (they assert behavior that contradicts the documented contract). Finding #2 is dead code
that should be cleaned up. Neither is fixed here; remediation should be dispatched as a bounded fix
task per the review process.
