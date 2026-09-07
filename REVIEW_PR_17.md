# Automated Pre-Human Review — PR #17

**State:** REQUEST_CHANGES
**Reviewer:** automated review agent
**Base:** `43a5502` → **Head:** `07fc8dc` ("Add heuristic evaluator with material, piece-square tables, and mobility scoring")
**Scope:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`, `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`

## Summary

The heuristic evaluator (material + piece-square tables + mobility) is well-structured, pure/deterministic, and the core scoring logic is sound. However, the new test suite contains a broken assertion that will fail when run, and the interface documents a contract (antisymmetry) that the implementation does not satisfy. These should be fixed before a human reviewer invests time. No security issues found (pure in-memory FEN evaluation; no injection, authz, secret handling, or untrusted I/O surface).

## Findings

### Finding 1 — Incorrect test: "malformed" InlineData is a valid position and asserts 0 == ~68 (blocker)

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`
**Line:** 102 (the `[InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")]` case of `Score_MalformedBoard_ReturnsNeutralZero`, method lines 103–107)

**What is wrong:** The trailing comment says `// missing ranks / malformed`, but the string is neither. It splits into exactly 8 ranks (`rnbqkbnr / pppppppp / 8 / 8 / 8 / 8 / PPPPPPPP / RNBQKBNR`), each summing to 8 squares — i.e. it is the **standard starting position placement** (just without the side-to-move field, which `TryParsePlacement` does not require). The parser therefore succeeds, and `Score` returns a positive mobility-based value, not `0`.

Tracing `CountPseudoLegalMoves` for White from the start position under this implementation (which counts pawn diagonals to *empty* squares — see Finding 3): pawn pushes/doubles ≈ 16, pawn empty-diagonals ≈ 14, knight moves = 4, sliders/king = ~0 → ~34 pseudo-legal moves → `MobilityWeight(2) × 34 = 68`. Material and piece-square terms cancel by symmetry, so `Score("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", White) ≈ 68`.

This is corroborated by the author's own `Score_AfterKnownPawnPush_ChangesScore` test, which asserts 1.e4 scores *lower* than the start — that only holds if the start is a positive mobility value, i.e. ≫ 0.

Result: `Assert.Equal(0, _sut.Score(board, PlayerColor.White))` fails for this `InlineData` case.

**How to fix:** Replace this case with a genuinely malformed board. Examples that `TryParsePlacement` rejects (returns `false` → `Score` returns `0`):
- A 7-rank string: `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP"` (triggers `ranks.Length != 8`).
- A rank that does not sum to 8: `"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPP/RNBQKBNR"` (triggers the `fileIndex != 8` guard).

### Finding 2 — Interface contract claims antisymmetry that the mobility term violates (medium)

**Files/lines:**
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs:16` — `<c>Score(board, White) == -Score(board, Black)</c>`.
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs:13–15` — "it is the evaluator's own assets (material, placement, mobility) minus the opponent's … antisymmetric".

**What is wrong:** The mobility term is one-sided:
```
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
```
It adds only the *evaluated* side's pseudo-legal move count (always ≥ 0); the opponent's mobility is never subtracted. Therefore:
- `Score(W) = whiteScore + 2·mob(W)`
- `Score(B) = -whiteScore + 2·mob(B)`
- `Score(W) + Score(B) = 2·(mob(W) + mob(B)) > 0` for any position with moves, so `Score(W) ≠ -Score(B)`.

Only the material and piece-square components are antisymmetric. The class remarks hedge with "for the material and placement components", but the interface remarks state the antisymmetry guarantee unconditionally, and the class summary line "evaluator's own assets (material, placement, mobility) minus the opponent's" is inaccurate for mobility. The existing test `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` implicitly confirms the break: it asserts `whiteScore == blackScore > 0` (both positive), not `whiteScore == -blackScore`.

This matters because a future caller building a negamax/minimax on top of this evaluator would rely on the documented zero-sum/antisymmetric property and get biased scores (the side to move is effectively credited free mobility).

**How to fix (either):**
- (a) Correct the docs: drop the unconditional antisymmetry guarantee from `IHeuristicEvaluator` and reword the implementation remarks to state that only the material + piece-square components are antisymmetric, and that mobility is a one-sided side-to-move bonus. Or,
- (b) Make the evaluation zero-sum: `perspective += MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent));` (and update the `Score_StartingPosition…` test, which currently depends on the one-sided behavior, accordingly).

### Finding 3 — Pawn mobility counts diagonal moves to empty squares as moves (low / quality)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`, method `CountPawnMoves`

**What is wrong:** The diagonal branch counts a target square when `target is null || target.Color != color`. A pawn cannot move diagonally onto an *empty* square (only capture diagonally, or en passant, which is unmodeled). This is labelled a "pseudo-legal relaxation", but standard pseudo-legal pawn move generation excludes empty-square diagonals, so this inflates pawn mobility by +2 per central pawn (and is what pushes the start-position mobility to ~34 instead of the true ~20). It is symmetric across colors so it does not bias one side, but it weakens the accuracy of the mobility tie-breaker and is the reason Finding 1's value is ~68 rather than ~40.

**How to fix:** Count a diagonal only when an enemy piece is present: `if (target is not null && target.Color != color) count++;` — or, if the over-count is intentional, document the deviation from standard pseudo-legal generation explicitly.

## Note on CI status

The task brief states CI is green, yet Finding 1 is a failing assertion (≈68 vs expected 0). The only way both can be true is if `ChessMvp.Domain.Tests` is not actually being built/executed on the CI runner (consistent with the `net10.0` target vs. the SDK available in this environment). The human reviewer should confirm that the Domain test project is wired into the CI workflow and genuinely runs; otherwise the suite is a false green and Finding 1 is a latent failure waiting to surface.

## Verdict

REQUEST_CHANGES — fix the broken `Score_MalformedBoard_ReturnsNeutralZero` case (Finding 1) and reconcile the antisymmetry documentation/contract (Finding 2); Finding 3 is a recommended cleanup.
