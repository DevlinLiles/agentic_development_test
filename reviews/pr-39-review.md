# Automated pre-human review — PR #39

**PR:** #39 — Add heuristic evaluator with material, piece-square tables, and mobility scoring
**Head:** `07fc8dc` (diff against base `43a5502`)
**Verdict:** REQUEST_CHANGES

## Scope reviewed

The PR diff adds three new files (646 insertions, no edits to existing code):

- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Reviewed for correctness, quality, and security. No security concerns: the only input is a FEN string, which is parsed defensively (whitespace split, 8-rank validation, per-rank file-count overflow guard, graceful `return false` → neutral `0` on any malformed placement); there is no injection, authz, or secret-handling surface. Findings below are correctness/quality only.

## Findings

### 1. The documented antisymmetry invariant is broken by the per-side mobility term

**File:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`, line 16 (interface `<remarks>`)
**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, lines 130–136 (`Score`)

**What is wrong:**
The interface contract advertises an antisymmetric evaluator:

> "A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."

The material and piece-square components *are* antisymmetric — `EvaluateForWhite(grid)` is summed once (White assets positive, Black negative) and negated for Black at line 130. But the mobility term added afterward is **not** antisymmetric:

```csharp
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
return perspective;
```

`CountPseudoLegalMoves(grid, side)` is computed independently for whichever side is requested. On any asymmetric board the two sides' pseudo-legal move counts differ, so:

```
Score(board, White) =  whiteScore + W * mobWhite
Score(board, Black) = -whiteScore + W * mobBlack
Score(board, White) + Score(board, Black) = W * (mobWhite + mobBlack)   // != 0
```

Therefore `Score(board, White) != -Score(board, Black)`. The class-level `<remarks>` half-acknowledges this by qualifying "for the material and placement components," but the **interface** advertises antisymmetry as the expected contract and the implementation does not satisfy it. Any caller relying on the documented invariant (e.g. a negamax/minimax-style AI that derives `Score(board, opponent) = -Score(board, side)`) gets subtly wrong scores for the normal case where the two sides' mobilities differ. This is the same class of invariant violation previously flagged on PR #19 ("antisymmetry invariant broken").

**How to fix (remediation is a separate task — do not fix here):**
Either (a) make mobility antisymmetric — compute mobility for *both* sides once and use `mobWhite - mobBlack` added from White's perspective, so the entire `perspective` value is negated consistently for Black — preserving the contract the interface advertises (preferred); or (b) correct the interface and class docs to drop the antisymmetry claim and state explicitly that the mobility term is added per-side and the overall score is *not* antisymmetric, documenting the consequence for negamax-style callers.

### 2. No test guards the antisymmetry invariant on an asymmetric position

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`, lines 18–78 (test class)

**What is wrong:**
Every position exercised by the test suite is *symmetric* — the starting position (`Score_StartingPosition…`, line 21) and the color-mirrored king+pawn pair (`Score_MirroredPosition…`, lines 67–78). On symmetric positions White and Black mobilities happen to be equal, so the broken invariant from Finding #1 is never exposed. There is no test asserting `Score(board, White) == -Score(board, Black)` on an **asymmetric** position — exactly the case where the invariant fails.

**How to fix:** Add a test that, for an asymmetric but legal position (e.g. White has a developed knight and Black does not), asserts `Score(board, White) == -Score(board, Black)`. This will currently fail, demonstrating the bug, and will guard the fix for Finding #1.

### 3. The starting-position test's symmetry assertion is incidental, not invariant-driven

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`, lines 21–27

**What is wrong:**
`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` asserts `Assert.Equal(whiteScore, blackScore)` (line 27) on the grounds that "the only nonzero term is mobility, which is identical for both colors." That equality holds *only* because the two sides' pseudo-legal move counts coincide on the symmetrical starting position — i.e. it is a consequence of the very per-side (non-antisymmetric) mobility design flagged in Finding #1, not of the antisymmetry the interface promises. A reader could reasonably believe this test is guarding the documented invariant; it is not, and it will continue to pass after a correct antisymmetric fix only by coincidence of symmetry. The test should either (preferred) be paired with the asymmetric invariant test from Finding #2, or its comment should be corrected to state that equality here is due to positional symmetry rather than the evaluator's antisymmetry contract.

**How to fix:** Clarify the comment to reflect that equality follows from the board's symmetry (equal per-side mobility), and add the asymmetric invariant test from Finding #2 so the actual contract is guarded.

## Non-blocking notes (not required for approval)

- `CountPawnMoves` counts empty diagonal squares as pseudo-legal "attacks" for the mobility tie-breaker. This is a reasonable, documented relaxation and is fine as-is.
- The private `PieceType`/`Piece` mirrors domain types to keep the evaluator decoupled from any third-party move generator; acceptable and self-contained.
- FEN parsing is robust and defensive against untrusted input — good handling.

## Verdict

REQUEST_CHANGES — the documented antisymmetry invariant is violated by the per-side mobility term, and no test guards that invariant on an asymmetric board. Fix the invariant (or correct the contract) and add a covering asymmetric-position test.
