# Automated Pre-Human Review — PR #30

**Verdict: REQUEST_CHANGES**

PR #30 adds a single file, `reviews/pr-29-review.md`, an automated pre-human review
verdict for PR #29 (state: REQUEST_CHANGES, three findings). Meta-reviewing that verdict
against the actual PR #29 code in the repository, the verdict's primary finding cites an
interface contract and an API surface that do not exist in the real code, so the
REQUEST_CHANGES rationale is built on an inaccurate model of the repository. The review
must be re-derived against the true source before it can be trusted by a human reviewer.

## Findings

### 1. [Correctness] Headline finding cites an interface contract that does not exist
**File under review:** `reviews/pr-29-review.md` (Finding 1, quoting `server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` "lines 13-14")

The verdict's Finding 1 quotes the interface as stating:

> *"A symmetric implementation is antisymmetric, i.e. `Score(board, White) == -Score(board, Black)`."*

and bases its REQUEST_CHANGES on that "documented antisymmetry invariant." The actual
`server/src/ChessMvp.Domain/Abstractions/IHeuristicEvaluator.cs` is 20 lines, declares a
single method `double Evaluate(string fen)`, and contains **no** antisymmetry statement.
Its documented contract is instead: *"A heuristic score in pawn units from White's
perspective: positive values favor White and negative values favor Black. The magnitude
and exact composition are implementation-defined."* There is no `Score(board, side)`
member on the interface at all — the only member is `Evaluate(string fen)`, which takes no
side argument. The line range "lines 13-14" cited in the verdict does not contain the
quoted text (the file ends at line 20 and never mentions antisymmetry).

Because the interface imposes no antisymmetry requirement, the verdict's central claim
that "the mobility term breaks the antisymmetry contract the interface documents" is not
supportable against the real code. The implementation's own XML doc comment does assert
antisymmetry for the material/placement components, so a *real* finding would have to cite
the implementation comment (and reconcile it with the `Evaluate(fen)` signature that has no
`side` parameter), not a fabricated interface line.

**How to fix:** Re-derive Finding 1 against the actual interface. Either (a) drop the
interface-line citation and restate the issue purely in terms of the implementation's own
doc comment and the `Evaluate(string fen)` contract ("score from White's perspective"), or
(b) if a `Score(board, side)` overload genuinely exists in the PR #29 head, quote the exact
file/blob hash and line numbers that were inspected, because the interface file checked
into this repository does not contain them.

### 2. [Correctness] Verdict references an API (`Score(board, side)`) and test API that do not match the public interface
**File under review:** `reviews/pr-29-review.md` (Findings 1 & 2; e.g. "`Score(board, White)`", "`_sut.Score(board, PlayerColor.White)`")

Throughout, the verdict describes calls as `Score(board, PlayerColor.White)` and
`_sut.Score(board, PlayerColor.White)`. The interface the PR ships is
`double Evaluate(string fen)` — a single FEN argument, no `PlayerColor`, and no `Score`
method name. A reader (or the remediation task) following the verdict's "file, line, what
is wrong" guidance would look for a `Score(...)` method and a `PlayerColor` parameter that
are not part of the interface contract. This makes the findings non-actionable as written:
the remediation agent cannot locate the cited code.

**How to fix:** Quote the exact member the implementation exposes (`Evaluate(string fen)`)
and frame each finding in terms of that signature. If the implementation internally has a
`Score`/perspective helper, cite the concrete private member and its real line numbers
rather than presenting it as the public API.

### 3. [Quality] Test-case claims (file/line and behavior) are unverifiable and likely mis-attributed
**File under review:** `reviews/pr-29-review.md` (Finding 2, citing `HeuristicEvaluatorTests.cs` "InlineData line 99; assertion line 103"; Finding 1 citing test "lines 24-25")

The verdict asserts specific line numbers (99, 103, 24-25) and a test name
`Score_StartingPosition_FavorsNeitherSideAndIsPositiveFromMobility` with a quoted body
(`Assert.Equal(whiteScore, blackScore)` plus `whiteScore > 0`). These are presented as
ground truth ("This demonstrates the new tests were never actually executed"). Given that
Finding 1 already misrepresents the interface (see Finding 1 above), the test citations
cannot be assumed accurate either, and a REQUEST_CHANGES that rests partly on a
"would-fail-if-run" test claim must be backed by the real test source. A pre-human review
that tells a human "the tests were never run" while itself citing nonexistent source lines
undermines the very reliability it is meant to provide.

**How to fix:** Re-open the actual `HeuristicEvaluatorTests.cs` at the PR #29 head, quote
the real `InlineData`/assertion lines with their true line numbers, and confirm the test
name and body verbatim. If the "malformed board" case is in fact malformed, show the real
string; if it is not, state the real string that is used. Do not carry forward line numbers
from a code model that has already been shown to diverge from the repository.

## Summary

The PR #29 review file is the entirety of PR #30's diff. Its REQUEST_CHANGES verdict is
predicated on an interface contract (`Score(board, White) == -Score(board, Black)` at
`IHeuristicEvaluator.cs` lines 13-14) that is not present in the checked-in interface, and
on an API surface (`Score(board, PlayerColor)`) that does not match the shipped
`Evaluate(string fen)` signature. Before a human reviewer acts on this verdict it must be
regenerated against the actual source so that every cited file, line, and symbol is real.
No code changes are requested in the implementation PRs here — only a corrected review file.
