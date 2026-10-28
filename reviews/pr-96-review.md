# Automated pre-human review — PR #96

**Verdict: REQUEST_CHANGES**

**Reviewed:** `reviews/pr-94-review.md` (new file, 64 lines)
**Base:** `d7d3d04` on `origin/main` (post-revert baseline)
**Branch under review:** adds the PR #94 review artifact

## Scope of the diff

Documentation-only: a single new markdown file under `reviews/` recording the automated pre-human
review verdict for PR #94 (itself a documentation-only review of the PR #92 artifact, which in turn
reviewed the PR #90 artifact, and so on down the nested-review chain). The diff contains no
executable code, no build/config/schema changes, no dependency changes, and no new API surface.
As with the prior nested-review PRs, the review effort went to independently re-verifying the
PR #94 document's factual claims about the codebase, since an inaccurate review artifact would
mislead the next engineer.

## Verification approach

All substantive claims in the PR #94 artifact were re-checked against the tree at `d7d3d04`
(`git log` for the commit graph; targeted identifier searches and full-file reads for every
line-number and count claim). The commit-graph claims, every byte-precise line-number reference,
the identifier-absence claims, and the attribute-count claims were all reproducible — except one
test-count figure, detailed below.

## Claim-by-claim verification (re-confirmed against the tree at `d7d3d04`)

| # | Document claim | Verified |
|---|----------------|----------|
| 1 | Base sits at post-revert baseline `d7d3d04` atop `1a3c3e0`, with `6e3ca6f` reverted away | Yes — `git log` shows `d7d3d04` (Revert "Add basic heuristic AI opponent for solo play") directly atop `1a3c3e0` (Add baseline CI workflow), which sits atop `6e3ca6f` (Add basic heuristic AI opponent); working tree clean, no `reviews/` directory at the base, consistent with the file being new |
| 2 | The sole `using Chess;` match is the Infrastructure adapter, line 1 | Yes — exactly one match at `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs:1`; the only other `Chess.*` reference is the `using EngineMove = Chess.Move;` alias in the same file (line 4); zero matches in `ChessMvp.Domain` |
| 3 | Gera.Chess (Geras1mleo) v1.2.0 at `ChessMvp.Infrastructure.csproj` line 8 | Yes — line 8 reads `<PackageReference Include="Gera.Chess" Version="1.2.0" />` exactly |
| 4 | `board.Move` guarded by try/catch on `ChessPieceNotFoundException` (line 46) and `ChessGameEndedException` (line 50) | Yes — both catches confirmed at exactly those lines, each returning `MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove)`, plus the `!applied` fallback returning the same |
| 5 | `GetLegalDestinations` uses `OrdinalIgnoreCase` and returns an empty set on invalid FEN | Yes — `TryLoadFromFen` failure returns `new HashSet<string>()` (line 77); the `Where` comparison (line 81) and `ToHashSet(StringComparer.OrdinalIgnoreCase)` (line 83) both use `OrdinalIgnoreCase` |
| 6 | Draw scoping deliberate (`AutoEndgameRules.FiftyMoveRule` only) with an explanatory comment | Yes — `private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;` at line 18, preceded by a four-line comment (lines 14–17) stating insufficient-material and threefold-repetition draws are deliberately deferred per the product spec |
| 7 | Domain seam: `GameService.SubmitMoveAsync` lives in the domain | Yes — `server/src/ChessMvp.Domain/Services/GameService.cs:68` exactly (the `public async Task<Game> SubmitMoveAsync(` signature); the controller call site is at line 93 of `ChessMvp.Api/Controllers/GamesController.cs` — exact |
| 8 | Prior AI attempt fully removed (`IChessAi`/`GreedyHeuristicChessAi`/`GameOpponent`/`IsVsAi`) | Yes — zero matches for any of those identifiers across the tree |
| 9 | Adapter tests exist alongside `GameServiceTests.cs` covering `SubmitMoveAsync` semantics | **Partially** — `GerasimleoChessRulesEngineAdapterTests.cs` present with exactly 19 `[Fact]`/`[Theory]` attributes (18 `[Fact]` + 1 `[Theory]` at line 62), that part is correct; but the `SubmitMoveAsync_*` count is wrong: `GameServiceTests.cs` contains **11** such tests, not 12 (see Finding 1) |
| 10 | Swap-seam promise (only adapter + tests touch `Chess.*`) | Yes — Gera.Chess engine identifiers (`ChessBoard.TryLoadFromFen`, `EndgameType`, the two exception types, `EngineMove`) appear only in the Infrastructure adapter on the server; the `ChessBoard` matches in `client/src/` are that app's own React component (`client/src/components/board/ChessBoard.tsx`, imported locally), not Gera.Chess types — the clarification the PR #94 document records is accurate |

## Findings

Two actionable issues, both in the added document itself (documentation corrections; suitable for
a bounded fix task — not fixed in this review per policy).

### Finding 1 — wrong test count in row 9 (off-by-one), marked "Verified: Yes"

**File:** `reviews/pr-94-review.md`, claim-by-claim verification table, row 9.

**What is wrong:** The row's verification cell asserts — as confirmed fact — that
`GameServiceTests.cs` has "its own `SubmitMoveAsync_*` coverage (12 such tests)". The file contains
exactly **11** such tests:

1. `SubmitMoveAsync_UnknownGame_ThrowsGameNotFoundException` (line 93)
2. `SubmitMoveAsync_TokenMatchesNeitherSeat_ThrowsInvalidSlotTokenException` (line 102)
3. `SubmitMoveAsync_GameWaitingForPlayer2_ThrowsGameNotActiveException` (line 115)
4. `SubmitMoveAsync_GameAlreadyEnded_ThrowsGameNotActiveException` (line 126)
5. `SubmitMoveAsync_NotThatPlayersTurn_ThrowsNotYourTurnException` (line 137)
6. `SubmitMoveAsync_PromotionMoveWithoutPromotionPiece_ThrowsPromotionRequiredException` (line 151)
7. `SubmitMoveAsync_IllegalMove_ThrowsAndDoesNotSaveOrNotify` (line 164)
8. `SubmitMoveAsync_LegalMove_UpdatesStateSavesAndNotifiesOnce` (line 179)
9. `SubmitMoveAsync_CheckmateMove_EndsGameWithCorrectResult` (line 210)
10. `SubmitMoveAsync_StalemateMove_EndsGameAsDraw` (line 238)
11. `SubmitMoveAsync_FiftyMoveDraw_EndsGameAsDraw` (line 260)

**Cross-checks (three independent methods, all agree on 11):**
- Direct enumeration of `SubmitMoveAsync_*` method declarations in
  `server/tests/ChessMvp.Domain.Tests/GameServiceTests.cs` → 11.
- Attribute accounting: the file has 18 `[Fact]` attributes total; 7 of them cover non-`SubmitMoveAsync`
  members (`CreateGameAsync`, three `JoinGameAsync`, `GetGameAsync`, two `GetMoveHistoryAsync`);
  18 − 7 = 11.
- A repo-wide search for `SubmitMoveAsync_*` finds no 12th match anywhere in the tree (all 11 live in
  `GameServiceTests.cs`; the only other `SubmitMoveAsync` occurrences are the domain signature, the
  controller call site, and in-test invocation expressions, none of which are test methods).

**Impact:** The document's stated purpose is that "an inaccurate review artifact would mislead the
next engineer," and row 9 is explicitly presented as an independently re-verified count. A reader
(trustingly) citing "12 `SubmitMoveAsync_*` tests" downstream propagates the error, and a future
diff that adds a test would be miscounted against a wrong baseline.

**How to fix:** Change "(12 such tests)" to "(11 such tests)" — or, better, enumerate the 11 test
names as this review does so the figure is self-evidencing. No code change is required; the test
suite itself is healthy and complete.

### Finding 2 — blanket-accuracy statement in the Correctness bullet is overstated

**File:** `reviews/pr-94-review.md`, `Findings` section, `Correctness` bullet.

**What is wrong:** The bullet states that "every substantive factual statement about the codebase in
the PR #92 artifact is accurate, including the byte-precise line-number references … each
re-verified line-for-line in this review against the tree at `d7d3d04` via targeted searches."
The 12-vs-11 `SubmitMoveAsync_*` count was evidently carried over from the PR #92 artifact without
being re-counted — an actual recount yields 11 — so at least one PR #92 figure was *not*
independently re-verified even though row 9 was marked "Yes". The blanket claim therefore
overstates the verification performed.

**How to fix:** Alongside the Finding 1 correction, annotate the correction relative to the PR #92
artifact (e.g., a short note that the PR #92 figure of 12 was corrected to 11 on recount) and/or
soften "every substantive factual statement" so the reader is not told all upstream figures were
independently re-confirmed. The Quality bullet's claim that the table is "independently
reproducible" needs the same treatment for row 9.

## Notes on the rest of the diff (no issues)

- **Correctness:** no logic introduced; every other factual statement re-verified accurate,
  including csproj line 8, adapter lines 1/4/14–18/46/50/77/81/83, GameService line 68,
  GamesController line 93, the zero-match AI-identifier claims, the 19-attribute adapter-test count
  with its 18+1 split, and the commit-graph claims about `d7d3d04`/`1a3c3e0`/`6e3ca6f`.
- **Quality:** the row-10 nuance the document records (client `ChessBoard.tsx` shares a name with
  the Gera.Chess `ChessBoard` type, so naive whole-tree identifier searches show extra, unrelated
  matches) is accurate and useful for the next reviewer. No dead or duplicated content beyond the
  two findings above.
- **Security:** no secrets, credentials, tokens, or sensitive payload. The file contains only
  commit SHAs, branch names, and file paths — standard metadata for this repo's review artifacts.
  A static markdown file has no injection or authz surface.

## Recommendation

Request changes: a one-word count correction plus a one-sentence annotation in
`reviews/pr-94-review.md`. Both are documentation-only fixes that can be dispatched as a bounded
fix task; no code changes are requested and none were made in this review.

## Review delivery note

The `submit_pr_review` API submission was rejected by GitHub with `self_review_forbidden` (the
submitting identity authored this PR), which is terminal per the harness. This verdict is therefore
delivered as the committed review artifact per this repository's established convention, which is
the reviewer branch's actual deliverable. The verdict is unchanged: REQUEST_CHANGES.
