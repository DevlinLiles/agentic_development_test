# Automated pre-human review — PR #120

**Verdict: APPROVE**

**Reviewed:** `reviews/pr-118-review.md` (new file, 121 lines)
**Base:** `d7d3d04` on `origin/main` (post-revert baseline)
**Branch under review:** adds the PR #118 review artifact

## Scope of the diff

Documentation-only: a single new markdown file under `reviews/` recording the automated pre-human
review verdict for PR #118 (itself a documentation-only review of the PR #117 artifact, which in
turn reviewed the PR #116 artifact, and so on down the nested-review chain). The diff contains no
executable code, no build/config/schema changes, no dependency changes, and no new API surface.
As with the prior nested-review PRs, the review effort went to independently re-verifying the
PR #118 document's factual claims about the codebase and the upstream review artifacts, since an
inaccurate review artifact would mislead the next engineer.

## Verification approach

First, the provided diff was matched against the actual PR head: the commit graph confirms the
PR #118 artifact commit (`d7acc2d`, "Add automated pre-human review verdict for PR #118
(APPROVE)") exists and `git diff d7d3d04..d7acc2d --stat` shows exactly one new file,
`reviews/pr-118-review.md`, 121 insertions — matching the diff under review in scope and size.
The document-under-review's cited commits were then confirmed on the graph via
`git log --no-walk --stat`: `deb1b05` (PR #116 verdict) adds exactly
`reviews/pr-116-review.md`, 112 lines; `86e46d1` (PR #115 verdict) adds exactly
`reviews/pr-115-review.md`, 103 lines; `af8bd48` (PR #114 verdict) adds exactly
`reviews/pr-114-review.md`, 97 lines; `575cd5d` (PR #113 verdict) adds exactly
`reviews/pr-113-review.md`, 94 lines; `4ff59ee` (PR #111 verdict) adds exactly
`reviews/pr-111-review.md`, 89 lines; and `e2e0ffe` (PR #110 verdict) adds exactly
`reviews/pr-110-review.md`, 105 lines — all matching the PR #118 document's description of the
artifacts it cites. Every load-bearing claim in the PR #118 document was then independently
re-derived against the working tree (the reviewer worktree, clean at `d7d3d04`, the implementation
head). Methods: `git log --all --oneline --date-order` and `git log --no-walk --format="%h parents=%p"`
for the commit graph (confirming the cited upstream artifacts at `9b2fd36`, `b185b4d`, `0dfd40b`,
`2d4e129`, and `b293a43` all exist, resolve to the PR #105/#102/#103/#94/#107 verdict commits, and
each sits directly atop `d7d3d04`); targeted identifier searches for the `using Chess;`/`EngineMove`
seam (row 2), the Gera.Chess package reference (row 3), the removed-AI identifiers (row 8), and the
domain seam (row 7); a full read plus line-anchored search of the Infrastructure adapter (covering
its row-2/4/5/6 line-number claims); attribute-pattern and test-method enumeration over both test
files for every count claim (row 9); and a direct full `git log -p` read of the upstream PR #94
artifact at `2d4e129` to confirm the correction premise. All claims checked reproduced exactly.

## Claim-by-claim verification (independently re-derived against the working tree)

| # | Document claim | Verified |
|---|----------------|----------|
| 1 | Base sits at post-revert baseline `d7d3d04` atop `1a3c3e0`, with `6e3ca6f` reverted away | Yes — `git log --no-walk --format="%h parents=%p"` shows `d7d3d04` parented directly on `1a3c3e0`, which is parented directly on `6e3ca6f`; the PR #115 artifact commit `86e46d1` sits directly atop `d7d3d04` on the graph |
| 2 | The sole `using Chess;` match is the Infrastructure adapter, line 1 | Yes — exactly one match at `server/src/ChessMvp.Infrastructure/ChessRulesEngine/GerasimleoChessRulesEngineAdapter.cs:1`; the only other `Chess.*` reference is the `using EngineMove = Chess.Move;` alias in the same file (line 4) |
| 3 | Gera.Chess (Geras1mleo) v1.2.0 at `ChessMvp.Infrastructure.csproj` line 8 | Yes — targeted search confirms exactly one match, at `server/src/ChessMvp.Infrastructure/ChessMvp.Infrastructure.csproj:8`, reading `<PackageReference Include="Gera.Chess" Version="1.2.0" />` exactly |
| 4 | `board.Move` guarded by try/catch on `ChessPieceNotFoundException` (line 46) and `ChessGameEndedException` (line 50) | Yes — full read confirms both catches at exactly those lines, each returning `MoveApplicationResult.Illegal(MoveFailureReason.IllegalMove)`, plus the `!applied` fallback returning the same |
| 5 | `GetLegalDestinations` uses `OrdinalIgnoreCase` and returns an empty set on invalid FEN | Yes — `TryLoadFromFen` failure returns `new HashSet<string>()` (line 77); the `Where` comparison (line 81) and `ToHashSet(StringComparer.OrdinalIgnoreCase)` (line 83) both use `OrdinalIgnoreCase` |
| 6 | Draw scoping deliberate (`AutoEndgameRules.FiftyMoveRule` only) with an explanatory comment | Yes — `private const AutoEndgameRules EnabledDrawRules = AutoEndgameRules.FiftyMoveRule;` at line 18, preceded by the four-line comment (lines 14–17) stating insufficient-material and threefold-repetition draws are deliberately deferred per the product spec |
| 7 | Domain seam: `GameService.SubmitMoveAsync` lives in the domain | Yes — `server/src/ChessMvp.Domain/Services/GameService.cs:68` exactly (the `public async Task<Game> SubmitMoveAsync(` signature); the API call site is `ChessMvp.Api/Controllers/GamesController.cs:93` |
| 8 | Prior AI attempt fully removed (`IChessAi`/`GreedyHeuristicChessAi`/`GameOpponent`/`IsVsAi`) | Yes — zero matches for any of those identifiers across the server tree |
| 9 | True `SubmitMoveAsync_*` count is **11**, adapter tests carry 19 attributes | Yes — `GameServiceTests.cs` contains exactly **11** `SubmitMoveAsync_*` tests at lines 93/102/115/126/137/151/164/179/210/238/260; the file has exactly 18 test methods, 7 covering non-`SubmitMoveAsync` members → 18 − 7 = 11; `GerasimleoChessRulesEngineAdapterTests.cs` has exactly 19 attributes (18 `[Fact]` + 1 `[Theory]` at line 62) |
| 10 | Swap-seam promise (only adapter touches the engine identifiers) | Yes — every engine identifier cited (`ChessBoard.TryLoadFromFen`, `EndgameType`, both exception types, `EngineMove`) occurs in the adapter file only, on the server |

## Findings

No actionable issues in the added document. Its central verification — the row-9 count correction
chain (PR #113 corrected the PR #94 artifact; PR #114 re-confirmed it; PR #115 re-confirmed the
re-confirmation; PR #116 re-confirmed it again; PR #117 re-confirmed it once more; PR #118
re-confirmed it once more again) — was independently re-confirmed in this review as accurate:

- **Row 9 count (re-confirmed):** the true `SubmitMoveAsync_*` count in `GameServiceTests.cs` is
  **11**. This review reproduced the 11 by direct enumeration at exactly the lines the PR #118
  document lists (93/102/115/126/137/151/164/179/210/238/260) and by method accounting — exactly 18
  test methods in `GameServiceTests.cs`, 7 covering non-`SubmitMoveAsync` members
  (`CreateGameAsync_ReturnsWaitingGameWithWhiteToken` at line 38, three `JoinGameAsync_*` at
  lines 52/60/69, `GetGameAsync_UnknownGame_ThrowsGameNotFoundException` at line 283, two
  `GetMoveHistoryAsync_*` at lines 291/299) → 18 − 7 = 11. The 19-attribute adapter-test count and
  its 18+1 split with the `[Theory]` at line 62 also reproduce exactly (the `[Theory]` attribute
  sits immediately above `TryApplyMove_CastlingWithClearPathAndRights_IsAccepted` at line 65). The
  upstream PR #94 artifact (read directly at `2d4e129` via full `git log -p`) verbatim states "its
  own `SubmitMoveAsync_*` coverage (12 such tests)" — the defect is genuinely in that artifact, and
  every correction and re-confirmation in the chain (PR #113 through PR #118) is accurate.
- **Cited artifact sizes (re-confirmed):** `deb1b05` adds `reviews/pr-116-review.md` at exactly 112
  lines, `86e46d1` adds `reviews/pr-115-review.md` at exactly 103 lines, `af8bd48` adds
  `reviews/pr-114-review.md` at exactly 97 lines, `575cd5d` adds `reviews/pr-113-review.md` at
  exactly 94 lines, `4ff59ee` adds `reviews/pr-111-review.md` at exactly 89 lines, and `e2e0ffe`
  adds `reviews/pr-110-review.md` at exactly 105 lines — matching the PR #118 document's
  description of each cited artifact. The cited upstream SHAs (`9b2fd36`, `b185b4d`, `0dfd40b`,
  `2d4e129`, `b293a43`) all exist on the commit graph, each resolving to the
  PR #105/#102/#103/#94/#107 verdict commits respectively, each parented directly on `d7d3d04`.
- **Cited document content (re-confirmed):** the upstream PR #94 artifact at `2d4e129` was read
  directly in this review (full patch) and contains the ten-row claim table with row 9 stating
  "12 such tests" verbatim — the premise of the PR #118 document's row-9 correction chain is
  accurate, as is its scoping of the defect to that upstream artifact rather than to any of the
  intervening re-confirmation documents.

## Notes on the rest of the diff (no issues)

- **Correctness:** no logic introduced; no missed edge cases. The document's row-9 verification
  reflects the true 11-test count, its line-number claims (rows 2–7) all reproduce exactly, and
  its zero-match claim for the removed AI identifiers (row 8) is accurate. Its swap-seam claim
  (row 10) is accurate for the identifiers it cites.
- **Quality:** the document is self-evidencing rather than merely assertive — it enumerates the 11
  tests with exact line numbers and gives multiple independent cross-check methods for the count,
  which is what let this review re-verify it cheaply. Its review-delivery note transparently records
  the `self_review_forbidden` API outcome rather than silently omitting it, which this review can
  confirm is the correct convention (the same terminal outcome recurred for PR #119 and again for
  PR #120). No dead content, no accidental complexity, no duplication. No new behavior is
  introduced, so no new tests are expected.
- **Security:** no secrets, credentials, tokens, or sensitive payload. The file contains only
  commit SHAs, branch names, and file paths — standard metadata for this repo's review artifacts.
  A static markdown file has no injection or authz surface.

## Recommendation

Approve. The artifact is accurate, every load-bearing claim reproduces against the codebase and
the commit graph, and no code changes are requested or needed.

## Review delivery note

The `submit_pr_review` API submission for PR #120 was rejected by GitHub with
`self_review_forbidden` (the submitting identity authored this PR), which is terminal per the
harness — not retried. This verdict is therefore delivered as the committed review artifact per
this repository's established convention, which is the reviewer branch's actual deliverable.
The verdict is unchanged: APPROVE.
