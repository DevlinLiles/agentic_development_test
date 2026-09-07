# Automated pre-human review — PR #37

**PR:** #37 — Add heuristic evaluator with material, piece-square tables, and mobility scoring
**Head:** `07fc8dc` (diff against parent `43a5502`)
**Verdict:** REQUEST_CHANGES

## Scope reviewed

The PR diff adds three new files (646 insertions, no edits to existing code):

- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`
- `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

Reviewed for correctness, quality, and security. No security concerns (input is a FEN string parsed defensively; unparseable input returns a neutral `0`; no injection/authz/secret-handling surface). Findings below are correctness/quality only.

## Findings

### 1. The advertised antisymmetry invariant is broken by the mobility term

**File:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` (interface `<remarks>`) and `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (`Score` / class `<remarks>`)

**What is wrong:**
The interface contract promises an antisymmetric evaluator:

> "A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."

The material and piece-square components *are* antisymmetric — `EvaluateForWhite` is summed once and negated for Black. But the mobility term is **not** antisymmetric. In `Score(...)`:

```csharp
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
return perspective;
```

`CountPseudoLegalMoves(grid, side)` is counted independently for each side. On any asymmetric board, White's pseudo-legal move count differs from Black's, so:

```
Score(board, White) =  whiteScore + W * mobWhite
Score(board, Black) = -whiteScore + W * mobBlack
Score(board, White) + Score(board, Black) = W * (mobWhite + mobBlack)   // != 0
```

so `Score(board, White) != -Score(board, Black)`. The class-level `<remarks>` implicitly acknowledges this by qualifying "for the material and placement components," but the **interface** advertises antisymmetry as the expected contract, and the implementation does not satisfy it. Any caller relying on the documented invariant (e.g. a negamax/minimax-style AI that derives `Score(board, opponent) = -Score(board, side)`) gets subtly wrong scores for positions where the two sides' mobilities differ — which is the normal case.

This is the same class of invariant violation previously flagged on PR #19 ("antisymmetry invariant broken").

**How to fix (remediation is a separate task — do not fix here):**
Either (a) make mobility antisymmetric — e.g. compute mobility for *both* sides once and use `mobWhite - mobBlack`, added from White's perspective and negated for Black, so the whole `perspective` is negated consistently — or (b) correct the interface and class docs to drop the antisymmetry claim and state explicitly that the mobility term is added per-side and the overall score is *not* antisymmetric, and document the consequence for negamax-style callers. Option (a) preserves the contract that the interface advertises and is the safer choice.

### 2. No test covers the antisymmetry invariant on an asymmetric position

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

**What is wrong:**
`Score_MirroredPosition_ScoresEquallyForMirroredColor` and `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` only exercise *symmetric* positions, where White and Black mobilities happen to be equal, so the broken invariant from Finding #1 is never exposed. There is no test asserting `Score(board, White) == -Score(board, Black)` on an **asymmetric** position — exactly the case where the invariant fails.

**How to fix:** Add a test that, for an asymmetric but legal position (e.g. White has a developed knight and Black does not), asserts `Score(board, White) == -Score(board, Black)`. This will currently fail, demonstrating the bug, and will guard the fix for Finding #1. (Whether this is a separate test or folded into the fix for #1, the invariant must be covered.)

## Non-blocking notes (not required for approval)

- `CountPawnMoves` counts empty diagonal squares as pseudo-legal "attacks" for the mobility term. This is a reasonable, documented relaxation for a tie-breaker and is fine as-is; just flagging it is intentional.
- The private `PieceType`/`Piece` mirrors domain types to keep the evaluator decoupled from any third-party move generator; acceptable and self-contained.
- FEN parsing is robust: whitespace split, 8-rank validation, per-rank file-count overflow check, graceful `return false` → neutral `0`. Good defensive handling of untrusted input.

## Verdict

REQUEST_CHANGES — the documented antisymmetry invariant is violated by the per-side mobility term, and no test guards it on an asymmetric board. Fix the invariant (or correct the contract) and add a covering test.
