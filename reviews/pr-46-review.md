# Automated Pre-Human Review — PR #46

- **Verdict:** REQUEST_CHANGES
- **Head:** `07fc8dc` — Add heuristic evaluator with material, piece-square tables, and mobility scoring
- **Scope:** diff of the PR head against its base (server Domain evaluator + tests, client opponent-type selector/API/types, Dockerfile HEALTHCHECK hardening, Storybook scaffolding).

CI is green, but the diff ships a broken documented invariant and an AI-opponent feature that is not wired end-to-end. Findings below. Each is concrete (file, symbol, what is wrong, how to fix); remediation should be dispatched as a separate bounded fix task.

---

## Finding 1 — Correctness: the documented antisymmetry invariant is broken by the mobility term

**Files**
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` — interface `<remarks>`: *"A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."*
- `server/src/ChessMvp.Domain/Services/HeuristicEvaluator.cs` — `Score(string board, PlayerColor side)`:
  ```csharp
  var perspective = side == PlayerColor.White ? whiteScore : -whiteScore;
  perspective += MobilityWeight * CountPseudoLegalMoves(grid, sideToMoveFor: side);
  return perspective;
  ```

**What is wrong**
The material + piece-square part (`EvaluateForWhite`) is genuinely antisymmetric: `Score_mpl(W) = whiteScore`, `Score_mpl(B) = -whiteScore`, so `Score_mpl(W) == -Score_mpl(B)`. The mobility term is **not**. It counts pseudo-legal moves for the *requested side only* and adds them positively, so:

- `Score(W) = whiteScore + W·mob(W)`
- `Score(B) = -whiteScore + W·mob(B)`
- `-Score(B) = whiteScore - W·mob(B) ≠ Score(W)` unless `mob(W) == -mob(B)` (impossible for non-negative counts).

The invariant `Score(board, White) == -Score(board, Black)` therefore fails on **every** position where the two sides' mobilities differ, and even on the symmetric starting position strict antisymmetry fails (both sides receive `+mobility` rather than `+/−`). The class `<remarks>` hedges ("antisymmetric … for the material and placement components"), but the interface advertises antisymmetry unconditionally and the class opens with "The implementation is antisymmetric" — both are false for the full `Score`.

This is not merely a doc nit: the interface explicitly states the evaluator is *"used by an `IChessAiPlayer` to score a board position"*, and antisymmetry is a hard requirement for negamax-based search (you need `Score(side) == -Score(opponent)` or move selection is unsound). An AI player built on this evaluator would score positions inconsistently across perspectives.

**How to fix**
Fold mobility into the White-relative score symmetrically and drop the separate per-side addition:
```csharp
var whiteScore = EvaluateForWhite(grid);
whiteScore += MobilityWeight * (CountPseudoLegalMoves(grid, PlayerColor.White)
                              - CountPseudoLegalMoves(grid, PlayerColor.Black));
return side == PlayerColor.White ? whiteScore : -whiteScore;
```
This preserves `Score(W) == -Score(B)` for all positions. Then correct the interface/class docs to match (or keep them as-is once the code actually satisfies them).

---

## Finding 2 — Quality/Tests: the antisymmetry invariant is never tested on an asymmetric position

**File:** `server/tests/ChessMvp.Domain.Tests/HeuristicEvaluatorTests.cs` — `Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility`.

**What is wrong**
The only color-symmetry test asserts `whiteScore == blackScore` on the **symmetric** starting position, where `mob(W) == mob(B)` and `whiteScore == 0`, so equality holds *despite* the broken antisymmetry (both values equal `+W·mob`, not `+/−W·mob`). No test asserts the documented `Score(board, White) == -Score(board, Black)` on an **asymmetric** position, which is exactly the case Finding 1 breaks. The existing test thus gives false confidence.

**How to fix**
Add a test that, for an asymmetric position (e.g. `7k/8/8/8/8/2q5/3P4/4K3 w - - 0 1` or any non-mirrored layout), asserts `Assert.Equal(_sut.Score(board, White), -_sut.Score(board, Black))`. This will fail on the current code and pass once Finding 1 is fixed, locking the invariant in.

---

## Finding 3 — Correctness/Quality: the AI-opponent feature is not wired end-to-end (dangling client + dead evaluator)

**Files**
- `client/src/api/gamesApi.ts` — `createGame()` sends `POST /api/games` with body `{ opponentType: "Ai" }` for AI games.
- `client/src/types/gameTypes.ts` — comment: *"Mirrors the server-side `OpponentType` enum (serialized as string via `JsonStringEnumConverter`)."*
- `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` / `Services/HeuristicEvaluator.cs` — introduced with a doc referencing `IChessAiPlayer`, but no consumer.

**What is wrong**
This PR's diff contains **no** server-side change that accepts `opponentType`: the only server edits are the two Dockerfile HEALTHCHECK lines and the new Domain evaluator files. There is no `OpponentType` enum, no create-game DTO/endpoint parameter, and no `IChessAiPlayer` implementation that consumes `IHeuristicEvaluator` anywhere in the PR. Consequently:

1. The client comment's claim that `OpponentType` *"mirrors the server-side enum"* is not satisfied by this PR — there is nothing on the server to mirror. Selecting "Computer (AI)" sends a JSON body that the server (as changed by this PR) does not read, so it silently produces an ordinary two-player game. The headline user-facing feature is a no-op.
2. `HeuristicEvaluator`/`IHeuristicEvaluator` are dead code: nothing constructs or calls them outside their own unit tests. The branch history (`6e3ca6f` add → `d7d3d04` revert "basic heuristic AI opponent") indicates the server AI path was previously added and reverted; this PR re-adds an evaluator and a client selector but does **not** restore the server endpoint/player that would make either functional.

(If server-side `OpponentType`/endpoint support was merged to the base in an earlier PR, the reviewer should confirm that base support exists and honors the body; as presented by this diff alone, the AI path is unimplemented.)

**How to fix**
Either (a) complete the feature in scope: add the server `OpponentType` enum + create-game request DTO, have the endpoint create an AI-driven second seat, and add an `IChessAiPlayer` that uses `IHeuristicEvaluator` to choose moves — with tests for the endpoint and the player; or (b) if this PR is intentionally evaluator-only, remove the client `opponentType` request body + selector wiring (and the "mirrors server enum" comment) so the UI does not advertise a non-existent mode, and mark the evaluator as library-only until a consumer lands. Do not ship a UI control that silently does nothing.

---

## Non-blocking notes (no change required)

- `client/Dockerfile` and `server/src/ChessMvp.Api/Dockerfile`: switching the HEALTHCHECK target from `localhost` to `127.0.0.1` is a sound, deterministic fix (avoids `localhost` IPv6/IPv4 ambiguity). Fine.
- `client/.storybook/main.ts` / `preview.ts`: minimal Storybook scaffolding; the new `OpponentTypeSelector.stories.tsx` is a reasonable smoke visual. No issue, but note Storybook is a new dev dependency surface — ensure CI installs/builds it without regressing build times (out of scope for this review).
- `HeuristicEvaluator` FEN parsing degrades gracefully (returns 0) on malformed input and the `Score_MalformedBoard_ReturnsNeutralZero` theory covers it; good. The private `Piece`/`PieceType` mirror types keep the service self-contained; acceptable.

---

## Summary

The evaluator's core material + piece-square logic is correct and well-tested, but the mobility term violates the interface's own antisymmetry contract (Finding 1) and that invariant is not tested where it would fail (Finding 2). Separately, the AI-opponent UX is shipped without the server support or AI player that would make it work, leaving both the client control and the evaluator dangling (Finding 3). Requesting changes; recommend a bounded fix task address Findings 1–3 before human review.
