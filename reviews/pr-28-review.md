# Automated Pre-Human Review — PR #28

**Verdict: APPROVE**

## Scope

Reviewed the PR #28 diff: base `main` (@ `07fc8dc`) → head (@ `f1bf68d`). The diff is a single
commit adding `reviews/pr-27-review.md` (55 lines) — the automated pre-human review verdict for
PR #27, rendered as **APPROVE**. PR #28 therefore carries the PR #27 verdict; this pass verifies
that verdict's accuracy against the actual committed source before a human reviewer sees it.

The PR #27 verdict endorses the PR #26 verdict (REQUEST_CHANGES, four findings) as correct and
ready for a human reviewer. To validate that APPROVE, each of the four PR #26 findings it
endorses was independently re-checked against the committed source.

## Verification

1. **BLOCKER — heuristic evaluator files stranded under a nested `.worktrees/` path.** Confirmed
   true. The three files are committed at
   `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`,
   `.worktrees/01m1vy27727029etgw6qp81jdc/server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs`,
   and `.worktrees/01m1vy27727029etgw6qp81jdc/server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`.
   The real project tree `server/src/ChessMvp.Domain/Abstractions/` contains no
   `IHeuristicEvaluator.cs`, and no `*.cs` under the real tree references `IHeuristicEvaluator`
   (a repo-wide search returns zero matches). The new interface/implementation therefore never
   compile and the six new tests never run in the project build — "CI green" is a false positive.
   The PR #27 verdict correctly flags this as a build-breaking blocker with a concrete relocation
   fix. Accurate.

2. **Antisymmetry invariant violated by the side-relative mobility term.** Confirmed true. The
   interface `<remarks>` documents the contract as unqualified antisymmetry
   (`Score(board, White) == -Score(board, Black)`). `HeuristicEvaluator.Score` negates the
   material+piece-square score for Black (antisymmetric), then adds
   `MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side)` — a side-relative term. For
   the symmetric starting position both sides have equal pseudo-legal move counts, so
   `Score(start, White) == +M` and `Score(start, Black) == +M` (both positive), i.e. `White == Black`,
   not `White == -Black`. A negamax/minimax consumer relying on the documented invariant would get
   wrong ordering. The offered fixes (opponent-relative mobility, or correcting the interface docs)
   are sound. Accurate.

3. **Test asserts behavior contradicting the documented invariant.** Confirmed true.
   `HeuristicEvaluatorTests.Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`
   asserts `Assert.Equal(whiteScore, blackScore)` with both `> 0`. This matches the actual
   (non-antisymmetric) implementation but contradicts the interface's documented antisymmetry,
   which would require `whiteScore == -blackScore` (hence both `0` for equal values). No test
   exercises a clearly asymmetric position from both color perspectives to pin down the intended
   side-relative behavior — the case an AI consumer cares about. Correctly flagged as a
   contract/test smell with a reconcile recommendation. Accurate.

4. **Pawn mobility counts empty diagonals as moves.** Confirmed true. `CountPawnMoves`'s diagonal
   block counts a target whenever `target is null || target.Color != color`. For knight/king/
   sliders an empty target is a genuine pseudo-legal move; for a pawn an empty diagonal is only an
   *attack*, not a move (a pawn cannot move diagonally onto an empty square). Counting it
   overstates pawn mobility relative to the other piece types and the method's own "pseudo-legal
   move" definition. Correctly flagged as a lower-severity internal inconsistency with a
   consistency-first fix. Accurate.

## Rationale

All four findings the PR #27 verdict endorses were independently verified against the committed
source and are accurate, each carrying a concrete file/method reference and a how-to-fix. The
verdict's scope statement (base `main` @ `07fc8dc` → head @ `2697372`, single commit adding
`reviews/pr-26-review.md`) is correct, and its APPROVE conclusion — that the PR #26 REQUEST_CHANGES
verdict it carries is correct and ready for a human reviewer — is justified.

The PR #28 diff itself introduces no security concerns: it is a markdown verdict file containing no
secrets, no untrusted-input handling, and no injection surface (the FEN strings it quotes are inert
test fixtures). No actionable issues were found in the PR #28 diff.

**Verdict: APPROVE** — the PR #27 verdict it carries is correct and ready for a human reviewer.
