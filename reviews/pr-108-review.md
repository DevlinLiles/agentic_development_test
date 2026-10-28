# Automated pre-human review — PR #108

**Verdict: APPROVE**

**Reviewed:** `reviews/pr-107-review.md` (new file, 103 lines)
**Base:** `d7d3d04` on `origin/main` (post-revert baseline)
**Branch under review:** adds the PR #107 review artifact

## Scope of the diff

Documentation-only: a single new markdown file under `reviews/` recording the automated pre-human
review verdict for PR #107 (itself a documentation-only review of the PR #105 artifact, which in
turn reviewed the PR #103 artifact, and so on down the nested-review chain). The diff contains no
executable code, no build/config/schema changes, no dependency changes, and no new API surface.
As with the prior nested-review PRs, the review effort went to independently re-verifying the
PR #107 document's factual claims about the codebase and about the PR #94/PR #105 artifacts, since
an inaccurate review artifact would mislead the next engineer.

## Verification approach

Every load-bearing claim was independently re-derived in this review against the working tree at
`d7d3d04` (the actual HEAD of this worktree, confirmed via `git status`/`git log`). Methods: the
commit graph via `git log` and `git log --all` (confirming the upstream artifacts at `9b2fd36`,
`b185b4d`, `0dfd40b`, and `2d4e129` exist and that `d7d3d04` is the true tip of this worktree's
branch); a full read of the Infrastructure adapter (covering its row-2/4/5/6 line-number claims in
one pass); a full read of `ChessMvp.Infrastructure.csproj` (row 3); targeted identifier searches
for the domain seam, the removed-AI identifiers, and the engine identifiers (rows 7, 8, 10);
attribute-pattern searches over both test files for every count claim (row 9); and direct `git diff`
reads of the PR #94 artifact at `2d4e129`, the PR #92 artifact at `24a11b4` (62 lines), the PR #102
artifact at `b185b4d` (91 lines), the PR #103 artifact at `0dfd40b` (95 lines), and the PR #105
artifact at `9b2fd36` (100 lines) to confirm both finding premises and the document's
characterization of the files it reviewed. All claims checked reproduced exactly.

## Claim-by-claim verification (independently re-derived against the tree at `d7d3d04`)

| # | Document claim | Verified |
|---|----------------|----------|
| 1 | Base sits at post-revert baseline `d7d3d04` atop `1a3c3e0`, with `6e3ca6f` reverted away | Yes — `git log` shows exactly `d7d3d04` (Revert "Add basic heuristic AI opponent for solo play") directly atop `1a3c3e0` (Add baseline CI workflow), which sits atop `6e3ca6f` (Add basic heuristic AI opponent); working tree clean at that commit with no `reviews/` directory, consistent with the file being new |
| 2 | The sole `using Chess;` match is the Infrastructure adapter, line 1 | Yes — exactly one match at `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs:1`; the only other `Chess.*` reference is the `using EngineMove = Chess.Move;` alias in the same file (line 4) |
| 3 | Gera.Chess (Geras1mleo) v1.2.0 at `ChessMvp.Infrastructure.csproj` line 8 | Yes — full read confirms line 8 reads `<PackageReference Include="Gera.Chess" Version="1.2.0" />` exactly |
| 4 | `board.Move` guarded by try/catch on `ChessPieceNotFoundException` (line 46) and `ChessGameEndedException` (line 50) | Yes — full read confirms both catches at exactly those lines, each returning `MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove)`, plus the `!applied` fallback returning the same |
| 5 | `GetLegalDestinations` uses `OrdinalIgnoreCase` and returns an empty set on invalid FEN | Yes — `TryLoadFromFen` failure returns `new HashSet<string>()` (line 77); the `Where` comparison (line 81) and `ToHashSet(StringComparer.OrdinalIgnoreCase)` (line 83) both use `OrdinalIgnoreCase` |
| 6 | Draw scoping deliberate (`AutoEndgameRules.FiftyMoveRule` only) with an explanatory comment | Yes — `private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;` at line 18, preceded by the four-line comment (lines 14–17) stating insufficient-material and threefold-repetition draws are deliberately deferred per the product spec |
| 7 | Domain seam: `GameService.SubmitMoveAsync` lives in the domain | Yes — `server/src/ChessMvp.Domain/Services/GameService.cs:68` exactly (the `public async Task<Game> SubmitMoveAsync(` signature); the controller call site is at line 93 of `ChessMvp.Api/Controllers/GamesController.cs` |
| 8 | Prior AI attempt fully removed (`IChessAi`/`GreedyHeuristicChessAi`/`GameOpponent`/`IsVsAi`) | Yes — zero matches for any of those identifiers across the server tree |
| 9 | Adapter tests exist alongside `GameServiceTests.cs` covering `SubmitMoveAsync` semantics | Yes — `GerasimleoChessRulesEngineAdapterTests.cs` present with exactly 19 `[Fact]`/`[Theory]` attributes (18 `[Fact]` + 1 `[Theory]` at line 62); `GameServiceTests.cs` contains exactly **11** `SubmitMoveAsync_*` tests at lines 93/102/115/126/137/151/164/179/210/238/260, cross-checked by attribute accounting (18 test methods − 7 non-`SubmitMoveAsync` = 11) |
| 10 | Swap-seam promise (only adapter + tests touch `Chess.*`) | Yes — every engine identifier cited (`ChessBoard.TryLoadFromFen`, `EndgameType`, both exception types, `EngineMove`) occurs in the adapter file only, on the server; the `ChessBoard` matches in `client/src/` are the client's own React component, consistent with the claim as scoped to the server |

## Findings

No actionable issues in the added document. Both findings it raises were independently re-confirmed
in this review as genuine defects in the *PR #94* artifact (commit `2d4e129`, read directly via
`git diff`), not errors of the PR #107 document itself:

- **Finding 1 (re-confirmed):** the PR #94 artifact's row 9 cell states verbatim that
  `GameServiceTests.cs` has "its own `SubmitMoveAsync_*` coverage (12 such tests)". The actual
  count is 11. This review reproduced the 11 by direct enumeration at exactly the lines the PR #107
  document lists (93/102/115/126/137/151/164/179/210/238/260) and by attribute accounting — 18 test
  methods in `GameServiceTests.cs`, 7 covering non-`SubmitMoveAsync` members
  (`CreateGameAsync_ReturnsWaitingGameWithWhiteToken`, three `JoinGameAsync_*`,
  `GetGameAsync_UnknownGame_ThrowsGameNotFoundException`, two `GetMoveHistoryAsync_*`) → 18 − 7 = 11.
  The 19-attribute adapter-test count and its 18+1 split with the `[Theory]` at line 62 also
  reproduce exactly. The PR #107 document's correction is accurate.
- **Finding 2 (re-confirmed):** the PR #94 artifact's Correctness bullet does claim that "every
  substantive factual statement about the codebase in the PR #92 artifact is accurate … each
  re-verified line-for-line in this review," and its Quality bullet does call the table
  "independently reproducible" — while its row 9 carried the wrong 12-vs-11 figure forward marked
  as verified. (The PR #92 artifact at `24a11b4` itself omits the figure entirely, so the "12" was
  introduced by the PR #94 artifact.) The overstatement critique is valid, and the suggested fix
  (annotate the correction and/or soften the blanket phrasing) is appropriate.

The document's claim that its PR #105 predecessor re-derived rows 3/7/8/10 rather than deferring
them — a claim the PR #105 artifact itself asserts and this review traced — was checked here and is
true: the csproj line 8, the `GameService.cs:68` seam with the `GamesController.cs:93` call site,
the zero removed-AI matches, and the adapter-only engine identifiers all reproduce against the tree
at `d7d3d04`. The PR #102 artifact at `b185b4d` really does mark rows 3/7/8/10 "Not re-derived in
this pass," so the PR #105/PR #103 documents' characterization of it is accurate.

## Notes on the rest of the diff (no issues)

- **Correctness:** no logic introduced; no missed edge cases. The document's corrections to the
  PR #94 artifact are accurate, its row-9 verification reflects the true 11-test count, and its
  re-derivation of the rows its predecessors deferred is genuine. It does not repeat the
  blanket-assertion failure mode it documents.
- **Quality:** the document is self-evidencing rather than merely assertive — it enumerates the 11
  test names with exact line numbers and gives multiple independent cross-check methods for the
  count, which is what let this review re-verify it cheaply. No dead content, no accidental
  complexity, no duplication. No new behavior is introduced, so no new tests are expected.
- **Security:** no secrets, credentials, tokens, or sensitive payload. The file contains only commit
  SHAs, branch names, and file paths — standard metadata for this repo's review artifacts. A static
  markdown file has no injection or authz surface.

## Recommendation

Approve. The artifact is accurate, both of its findings are genuine and correctly scoped as bounded,
documentation-only corrections to `reviews/pr-94-review.md` (well-suited to dispatch as a separate
fix task), and no code changes are requested or needed.

## Review delivery note

The `submit_pr_review` API submission was rejected by GitHub with `self_review_forbidden` (the
submitting identity authored this PR), which is terminal per the harness — not retried. This verdict
is therefore delivered as the committed review artifact per this repository's established
convention, which is the reviewer branch's actual deliverable. The verdict is unchanged: APPROVE.
