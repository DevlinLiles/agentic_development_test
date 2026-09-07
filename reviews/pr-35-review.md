# Pre-Human Review — PR #35

**Verdict: REQUEST_CHANGES**

Automated pre-human review pass over the PR #35 diff (heuristic position evaluator:
`IHeuristicEvaluator`, `HeuristicEvaluator`, and `HeuristicEvaluatorTests`). CI is green, but the
implementation violates its own published interface contract, and the unit test encodes that
violation as expected behavior. Findings below are concrete and remediable; no code was changed by
this review (remediation is dispatched separately).

---

## Finding 1 — Antisymmetry invariant is violated by the mobility term (Correctness / Contract)

**File:** `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs`
**Also:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (`Score`)

**What is wrong.**
The interface contract states the invariant unconditionally:

> A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`.
> A positive score means the position favors `side`; a negative score favors the opponent.

The implementation does **not** satisfy this. In `HeuristicEvaluator.Score`, the material +
piece-square total is made perspective-relative by negation (`-whiteScore` for Black), which *is*
antisymmetric. But the mobility term is then added **only for the requested side**:

```csharp
var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
...
perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
return perspective;
```

For any position where both sides have pseudo-legal moves (i.e. essentially every legal position),
`Score(board, White)` and `Score(board, Black)` are **both** shifted by a *positive* constant, so
`Score(board, White) != -Score(board, Black)`. Concretely, on the starting position the
material/placement terms are 0 and each side has 20 pseudo-legal moves, so:

- `Score(start, White) = 0 + 2*20 = +40`
- `Score(start, Black) = -0 + 2*20 = +40`

…which is `+40 == +40`, not `+40 == -40`. This breaks the interface's antisymmetry claim *and* its
"positive favors `side`" semantics: a perfectly symmetric, equal position reports a positive score
for **both** sides simultaneously — self-contradictory.

The class-level XML remark half-acknowledges this ("antisymmetric … for the material and placement
components"), but the **public interface** (`IHeuristicEvaluator`) states the invariant without that
qualification, so the implementation fails to meet its own contract. Any consumer that compares
`Score(board, White)` vs `Score(board, Black)` to decide who is better (a natural use of such an
evaluator) gets a wrong, always-positive-for-the-mover answer.

**How to fix.** Pick one and apply it consistently across the interface doc, the implementation, and
the tests:

- **Preferred:** make the whole score antisymmetric by making mobility differential, e.g.
  `MobilityWeight * (CountPseudoLegalMoves(grid, side) - CountPseudoLegalMoves(grid, opponent))`.
  Then a symmetric position correctly scores `0` and `Score(W) == -Score(B)` holds globally,
  satisfying the interface as written.
- **Alternative:** if one-sided mobility is intentional, revise the `IHeuristicEvaluator` contract
  to drop the unconditional antisymmetry claim, state explicitly that mobility is counted only for
  the evaluated side, and redefine the "positive favors side" guarantee to apply only to the
  material + placement components. The class remark already moves in this direction — push it up to
  the interface.

Either way, the test in Finding 3 must be updated to match the chosen semantics.

---

## Finding 2 — Private duplicate `Piece` / `PieceType` types diverge from the domain model (Quality)

**File:** `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` (bottom of file:
`private readonly struct Piece` and `private enum PieceType`)

**What is wrong.**
The evaluator defines its own private `Piece` struct and `PieceType` enum, while the same file's
interface (`IHeuristicEvaluator`) already depends on `ChessMvp.Domain.Entities.PlayerColor` from the
domain. The result is a parallel, disconnected piece model: the evaluator's `PieceType`/`Piece` will
not track changes to the domain's piece semantics (e.g. piece additions, value renames), and the
two models can silently drift. The stated rationale ("stay self-contained … without referencing a
third-party engine's piece type") does not apply to the *domain* entities, which are first-party and
already in use via `PlayerColor`.

**How to fix.** Reuse the domain `Piece` / `PieceType` entities (the same `ChessMvp.Domain.Entities`
namespace that already supplies `PlayerColor`) instead of the private mirrors. If a hard decoupling
requirement genuinely forbids that, document the exact constraint in the remark and add a test that
fails if the two enums drift (e.g. assert the evaluator's piece set equals the domain's).

---

## Finding 3 — Test encodes the contract violation instead of guarding the invariant (Quality / Missing tests for new behavior)

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs`
(`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`)

**What is wrong.**
The test asserts `Assert.Equal(whiteScore, blackScore)` for the symmetric starting position. That is
the *symptom* of Finding 1 (broken antisymmetry), encoded as expected behavior. It directly
contradicts the interface's `Score(board, White) == -Score(board, Black)` guarantee, which — if
enforced — would require `Assert.Equal(whiteScore, -blackScore)` (and a symmetric position would
score `0`, not a positive value). As written, the suite would stay green even if someone later
"fixed" the invariant, i.e. it actively guards the bug.

Additionally, there is **no test asserting antisymmetry on an asymmetric position** (the most
important invariant of the evaluator), and **no test scoring material from Black's perspective** —
every material test (`Score_CapturingOpponentQueen_*`, `Score_CentralizedQueen_*`) is evaluated for
White only, so the `-whiteScore` branch for Black material is uncovered.

**How to fix.**
- Replace the starting-position assertion with one consistent with the chosen semantics from
  Finding 1 (antisymmetric → `Assert.Equal(0, whiteScore)` and `Assert.Equal(whiteScore, -blackScore)`;
  one-sided → drop the `Equal(white, black)` claim and assert the documented behavior instead).
- Add an antisymmetry test on an asymmetric position, e.g.
  `Assert.Equal(_sut.Score(asym, White), -_sut.Score(asym, Black))` (if the invariant is restored).
- Add a Black-perspective material test mirroring `Score_CapturingOpponentQueen_*` to cover the
  negation branch.

---

## Non-blocking notes (no change required)

- `CountPawnMoves` counts empty-square diagonals as pseudo-legal pawn moves (documented as an
  intentional relaxation). This inflates pawn mobility by a constant per pawn; acceptable for a
  tie-breaker, but note it does not cancel in asymmetric pawn structures. Fine to keep given the
  inline justification.
- FEN parsing is bounds-checked and degrades to `0` on malformed input (verified by
  `Score_MalformedBoard_ReturnsNeutralZero`); no injection / untrusted-input surface was identified —
  the evaluator consumes internally-produced FEN, not direct user input. No security findings.

---

## Summary

| # | Severity | Category | Location |
|---|----------|----------|----------|
| 1 | Blocking | Correctness / Contract | `IHeuristicEvaluator.cs` (contract) vs `HeuristicEvaluator.Score` (mobility term) |
| 2 | Major | Quality | `HeuristicEvaluator.cs` (private `Piece` / `PieceType`) |
| 3 | Major | Quality / Missing tests | `HeuristicEvaluatorTests.cs` (starting-position assertion; no antisymmetry / Black-perspective tests) |

Recommend REQUEST_CHANGES: restore the antisymmetry invariant (or correct the contract), eliminate
the duplicate piece types, and add tests that guard the invariant rather than the bug.
