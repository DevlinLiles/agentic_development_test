# Automated Pre-Human Review — PR #27

**Verdict: APPROVE**

## Scope

Reviewed the PR #27 diff: base `main` (@ `07fc8dc`) → head (@ `2697372`). The diff is a single
commit adding `reviews/pr-26-review.md` (186 lines) — the automated pre-human review verdict for
PR #26. PR #27 therefore carries the PR #26 verdict; this pass verifies that verdict's accuracy
against the actual committed source before a human reviewer sees it.

## Rationale

The added verdict is accurate, well-structured, and actionable — ready for a human reviewer as-is.

All four findings it raises were independently verified against the committed source:

1. **BLOCKER — files stranded under a nested `.worktrees/` path.** The heuristic evaluator files
   are committed at `.worktrees/01m1vy27727029etgw6qp81jdc/server/...`, not the project `server/`
   tree. Confirmed: `server/src/ChessMvp.Domain/Abstractions/` contains no `IHeuristicEvaluator.cs`,
   `server/src/ChessMvp.Domain/Services/` contains no `HeuristicEvaluator.cs`, and no `*.cs` under
   the real tree references `IHeuristicEvaluator`. The new interface/implementation never compile
   and the six new tests never run in the project build, so "CI green" is a false positive.
   Correctly flagged as a build-breaking blocker with a concrete relocation fix.

2. **Antisymmetry invariant violated by the mobility term.** `HeuristicEvaluator.Score` computes
   the negated material/placement score, then adds `MobilityWeight * CountPseudoLegalMoves(grid,
   sideToMoveFor: side)` (`HeuristicEvaluator.cs`, the `perspective +=` line). For the symmetric
   starting position both sides have equal move counts, so `Score(start, White) == +M` and
   `Score(start, Black) == +M` — i.e. `White == Black`, not `White == -Black`. The interface
   `<remarks>` states the contract as unqualified antisymmetry (`Score(board, White) ==
   -Score(board, Black)`), so the contract/behavior mismatch is real. The offered fixes (make
   mobility opponent-relative, or correct the docs) are both sound.

3. **Test asserts behavior that contradicts the documented invariant.**
   `HeuristicEvaluatorTests.Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
   asserts `Assert.Equal(whiteScore, blackScore)` with both `> 0` — correct for the implementation
   but incompatible with the interface's documented antisymmetry (which would require
   `whiteScore == -blackScore`). Correctly flagged as a contract/test smell, with a reconcile
   recommendation that tracks whichever fix is chosen in #2.

4. **Pawn mobility counts empty diagonals as moves.** `CountPawnMoves` counts a diagonal target
   whenever `target is null || target.Color != color`. For knight/king/slider counts an empty
   target is a genuine pseudo-legal move; for a pawn an empty diagonal is only an *attack*, not a
   move (a pawn cannot move diagonally onto an empty square). Counting it overstates pawn mobility
   relative to the other piece types and the method's own "pseudo-legal move" definition. Correctly
   flagged as a lower-severity internal inconsistency with a consistency-first fix.

Each finding carries a concrete file/method reference and a how-to-fix. The file introduces no
security concerns: it contains no secrets, no untrusted-input handling, and no injection surface
(the FEN strings quoted are inert test fixtures).

No actionable issues were found in the PR #27 diff itself.

**Verdict: APPROVE** — the PR #26 verdict it carries is correct and ready for a human reviewer.
