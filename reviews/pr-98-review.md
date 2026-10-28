# Automated pre-human review — PR #98

**Verdict: APPROVE**

**Reviewed:** `reviews/pr-96-review.md` (new file, 129 lines)
**Base:** `d7d3d04` on `origin/main` (post-revert baseline)
**Branch under review:** adds the PR #96 review artifact

## Scope of the diff

Documentation-only: a single new markdown file under `reviews/` recording the automated pre-human
review verdict for PR #96 (itself a documentation-only review of the PR #94 artifact, which in turn
reviewed the PR #92 artifact, and so on down the nested-review chain). The diff contains no
executable code, no build/config/schema changes, no dependency changes, and no new API surface.
As with the prior nested-review PRs, the review effort went to independently re-verifying the
PR #96 document's factual claims about the codebase and about the PR #94 artifact, since an
inaccurate review artifact would mislead the next engineer.

## Verification approach

All substantive claims were re-checked in this review: the codebase claims against the working
tree at `d7d3d04` (commit graph via `git log`; targeted identifier searches; full-file reads for
every line-number and count claim), and the claims about the PR #94 artifact's wording against the
verdict commit `2d4e129` on `task/01m60aw0jgqppb0phpr5jh3ezg/reviewer`. Every codebase row, both
finding premises, and both finding texts reproduced exactly.

## Claim-by-claim verification (re-confirmed against the tree at `d7d3d04`)

| # | Document claim | Verified |
|---|----------------|----------|
| 1 | Base sits at post-revert baseline `d7d3d04` atop `1a3c3e0`, with `6e3ca6f` reverted away | Yes — `git log` shows exactly `d7d3d04` (Revert "Add basic heuristic AI opponent for solo play") directly atop `1a3c3e0` (Add baseline CI workflow), which sits atop `6e3ca6f` (Add basic heuristic AI opponent); working tree clean, no `reviews/` directory at the base, consistent with the file being new |
| 2 | The sole `using Chess;` match is the Infrastructure adapter, line 1 | Yes — exactly one match at `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs:1`; the only other `Chess.*` reference is the `using EngineMove = Chess.Move;` alias in the same file (line 4); zero matches in `ChessMvp.Domain` |
| 3 | Gera.Chess (Geras1mleo) v1.2.0 at `ChessMvp.Infrastructure.csproj` line 8 | Yes — line 8 reads `<PackageReference Include="Gera.Chess" Version="1.2.0" />` exactly |
| 4 | `board.Move` guarded by try/catch on `ChessPieceNotFoundException` (line 46) and `ChessGameEndedException` (line 50) | Yes — both catches confirmed at exactly those lines, each returning `MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove)`, plus the `!applied` fallback returning the same |
| 5 | `GetLegalDestinations` uses `OrdinalIgnoreCase` and returns an empty set on invalid FEN | Yes — `TryLoadFromFen` failure returns `new HashSet<string>()` (line 77); the `Where` comparison (line 81) and `ToHashSet(StringComparer.OrdinalIgnoreCase)` (line 83) both use `OrdinalIgnoreCase` |
| 6 | Draw scoping deliberate (`AutoEndgameRules.FiftyMoveRule` only) with an explanatory comment | Yes — `private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;` at line 18, preceded by a four-line comment (lines 14–17) stating insufficient-material and threefold-repetition draws are deliberately deferred per the product spec |
| 7 | Domain seam: `GameService.SubmitMoveAsync` lives in the domain | Yes — `server/src/ChessMvp.Domain/Services/GameService.cs:68` exactly (the `public async Task<Game> SubmitMoveAsync(` signature); the controller call site is at line 93 of `ChessMvp.Api/Controllers/GamesController.cs` — exact |
| 8 | Prior AI attempt fully removed (`IChessAi`/`GreedyHeuristicChessAi`/`GameOpponent`/`IsVsAi`) | Yes — zero matches for any of those identifiers across the tree |
| 9 | Adapter tests exist alongside `GameServiceTests.cs` covering `SubmitMoveAsync` semantics | Yes — `GerasimleoChessRulesEngineAdapterTests.cs` present with exactly 19 `[Fact]`/`[Theory]` attributes (18 `[Fact]` + 1 `[Theory]` at line 62); `GameServiceTests.cs` contains exactly **11** `SubmitMoveAsync_*` tests, at lines 93/102/115/126/137/151/164/179/210/238/260 — matching the document's Finding 1 enumeration name-for-name and line-for-line |
| 10 | Swap-seam promise (only adapter + tests touch `Chess.*`) | Yes — Gera.Chess engine identifiers (`ChessBoard.TryLoadFromFen`, `EndgameType`, the two exception types, `EngineMove`) appear only in the Infrastructure adapter on the server; the `ChessBoard` matches in `client/src/` are that app's own React component (`client/src/components/board/ChessBoard.tsx`, imported locally), not Gera.Chess types — the clarification the document records is accurate |

## Findings

No actionable issues in the added document. Both findings it raises were independently confirmed
as genuine defects in the *PR #94* artifact (commit `2d4e129`), not errors of the PR #96 document
itself:

- **Finding 1 (confirmed):** the PR #94 artifact's row 9 does state, in its "Verified: Yes" cell,
  that `GameServiceTests.cs` has "its own `SubmitMoveAsync_*` coverage (12 such tests)". The file
  contains exactly 11 such tests. This review reproduced the count by two independent methods that
  agree with the document's three: direct enumeration of the `SubmitMoveAsync_*` declarations → 11
  (lines 93/102/115/126/137/151/164/179/210/238/260, exactly the list the document gives); and
  attribute accounting — 18 `[Fact]` attributes total in `GameServiceTests.cs`, of which 7 cover
  non-`SubmitMoveAsync` members (`CreateGameAsync_ReturnsWaitingGameWithWhiteToken`, three
  `JoinGameAsync_*`, `GetGameAsync_UnknownGame_ThrowsGameNotFoundException`, two
  `GetMoveHistoryAsync_*`) → 18 − 7 = 11. The 19-attribute adapter-test count and its 18+1 split
  with the `[Theory]` at line 62 also reproduce exactly. The correction is accurate and the fix
  guidance (change "12" to "11", preferably with the enumerated list) is correct.
- **Finding 2 (confirmed):** the PR #94 artifact's Correctness bullet does contain the quoted
  blanket claim that "every substantive factual statement about the codebase in the PR #92 artifact
  is accurate … each re-verified line-for-line in this review," and its Quality bullet does call
  the table "independently reproducible" — while row 9 carried the wrong 12-vs-11 figure forward
  marked "Yes". The overstatement critique is therefore valid, and the suggested fix (annotate the
  correction relative to the PR #92 artifact and/or soften the blanket phrasing) is appropriate.

The PR #96 document's own content is accurate throughout: every row-9 line number, every test name
in the Finding 1 enumeration, the attribute counts, the commit-graph claims, and the row-10 swap-seam
nuance all reproduce exactly as written.

## Notes on the rest of the diff (no issues)

- **Correctness:** no logic introduced; no missed edge cases. The document correctly downgrades row 9
  from "Yes" to "**Partially**" and correctly keeps every other row as verified — this review
  independently confirmed all nine other rows.
- **Quality:** the document is self-evidencing rather than merely assertive — it enumerates the 11
  test names with exact line numbers and gives three independent cross-check methods for the count,
  which is what lets this review re-verify it cheaply. No dead content, no accidental complexity,
  no duplication. No new behavior is introduced, so no new tests are expected.
- **Security:** no secrets, credentials, tokens, or sensitive payload. The file contains only commit
  SHAs, branch names, and file paths — standard metadata for this repo's review artifacts. A static
  markdown file has no injection or authz surface.

## Recommendation

Approve. The artifact is accurate, its two findings are genuine and correctly scoped as bounded,
documentation-only corrections to `reviews/pr-94-review.md` (well-suited to dispatch as a separate
fix task), and no code changes are requested or needed.

## Review delivery note

The `submit_pr_review` API submission was rejected by GitHub with `self_review_forbidden` (the
submitting identity authored this PR), which is terminal per the harness — not retried. This verdict
is therefore delivered as the committed review artifact per this repository's established
convention, which is the reviewer branch's actual deliverable. The verdict is unchanged: APPROVE.
