# Automated Pre-Human Review — PR #97

**Verdict: REQUEST_CHANGES**

**Scope reviewed:** the complete one-file diff — the addition of `reviews/pr-95-review.md` (the automated pre-human review verdict for PR #95, +35 lines, blob `1367888`, commit `3591ae0`). PR #97 makes no code, config, lockfile, or test changes; it is purely a review artifact, so this pass audited the verdict file for factual accuracy.

**Submission note:** the `submit_pr_review` GitHub submission of this verdict may be rejected with `self_review_forbidden` (the submitting identity authored PR #97), which is terminal. Per repository convention, the committed verdict file is the review deliverable, so the verdict is recorded here as well — the same limitation the PR #72–#74 verdict files document (`pr-75-review.md`, re-confirmed in this pass, carries no such note).

## Methodology

PR #97's only content is a review of PR #95 (itself a review of PR #91, which reviews PR #86 → PR #79 → PR #76 → PR #75 → PR #74 → #64 verdict chain), so this pass re-verified the file's material claims against the real repository history — in particular `d7d3d04..3591ae0` (the PR #97 head itself), `d7d3d04..ac8fb12` (the PR #95 head it audits), `d7d3d04..adb3361` (the PR #91 head), `d7d3d04..d8bc7a7` (the PR #79 verdict file), `d7d3d04..34eb4c5` (the PR #76 head), `d7d3d04..49619a9` (the PR #75 head), and `d7d3d04..e7202f1` / `d7d3d04..1054972` (the PR #87 and PR #90 heads, read in full in this pass because the file cites the "PR #86–#91" passes as verification precedent) — rather than trusting the file's own prose or the verdict chain's earlier attestations. Diff scopes and blobs were re-derived with `git diff --raw` and `git diff --stat`; the decisive cited lines were read back with `git log -L` rather than trusting the file's paraphrase of them.

## Findings — one defect found

### Finding 1 — overstated corroboration set in the inherited-claims paragraph (minor)

**File:** `reviews/pr-95-review.md`, the "Claims inherited from deeper in the chain" paragraph (line 25 of the new file).

The paragraph lists the claims not re-derived in its own pass — "the `pr-76-review.md` line 7/36 quotes, the `452cfce` commit-message claim, the `9944a79` range stat, PR #64 findings 1–2 and 4–5, and the `c795980` breakdown" — and states they "were previously verified by the PR #72–#74 **and PR #86–#91** passes." That set is overstated, in two independently sufficient ways, both re-derived in this pass:

- **The PR #87 and PR #90 passes verify none of the listed claims.** `reviews/pr-87-review.md` (`e7202f1`) and `reviews/pr-90-review.md` (`1054972`), each read in full in this pass, review the parallel Gera.Chess-adapter chain — the PR #84 and PR #89 artifacts, via code-claim verification tables about `using Chess;`, the csproj package reference, adapter catch lines, `GameService.SubmitMoveAsync`, and adapter test counts. Neither file mentions `pr-76-review.md`, `452cfce`, `9944a79`, any PR #64 finding, or `c795980`. Including passes that are silent on the subject in a verifying set for these claims is the exact `pr-76-review.md` line-7 pattern this chain's own verdicts established must be corrected before merge.
- **The only two passes in the cited set that touch the subject declined three of the five listed claim-groups.** The PR #86 pass (`adb3361`) and the PR #91 pass (`ac8fb12`) did re-verify the `pr-76-review.md` line 7/36 locations and quotes and the `452cfce` commit message, but both explicitly state that the `9944a79` range stat, PR #64 findings 1–2 and 4–5, and the `c795980` breakdown "were not [all] re-derived line-by-line in this pass." For those three claim-groups the accurate corroboration set is PR #72–#74 only.

This also falsifies the file's "Findings — no defects found" assertion that it "makes no claim about repository state that this pass could falsify," and it qualifies the Quality bullet claiming the file "does not reproduce the defect class it reviews" — that is true of its submission-note scoping ("PR #72–#74", verified accurate), but the line-25 sentence reproduces precisely that defect class for the inherited-claims scoping.

**Fix:** re-scope line 25 to the accurate per-claim provenance, e.g. "they were previously verified by the PR #72–#74 passes, and the `pr-76-review.md` line 7/36 quotes and the `452cfce` commit-message claim were additionally re-verified by the PR #86 and PR #91 passes" — dropping PR #87–#90 from the verifying set and dropping PR #86/#91 for the `9944a79`, PR #64 findings 1–2 and 4–5, and `c795980` items.

### Severity rationale

Minor in isolation — one clause in a markdown review record; nothing here affects any code, config, lockfile, or test, and the file's APPROVE of `reviews/pr-91-review.md` remains substantively sound, since every decisive claim of that artifact re-verified in this pass checks out. But per the standard this chain itself set in PR #73, PR #79 Finding 1, and PR #86 Finding 1, a certification artifact whose value proposition is accurate verification provenance should not merge with a corroboration set drawn over passes silent on the subject — the very defect class the file reviews and claims not to reproduce.

## What checked out (directly re-verified in this pass)

- **Diff fidelity — verified.** `git diff --raw d7d3d04..3591ae0` is exactly one added file `reviews/pr-95-review.md`, blob `1367888`, and `git diff --stat` reports `1 file changed, 35 insertions(+)` — matching the supplied PR #97 diff exactly, so the PR #97 range contains no code, config, lockfile, or test changes.
- **Diff fidelity of the reviewed artifact — verified.** `git diff d7d3d04..ac8fb12` is exactly one new file `reviews/pr-91-review.md`, +40 lines, blob `922839b` — matching the file's stated scope for PR #95 exactly.
- **Nested chain fidelities — verified.** `d7d3d04..adb3361` is exactly one new file `pr-86-review.md`, +56 lines, blob `9ea9f07`; `d7d3d04..d8bc7a7` is exactly one new file `pr-79-review.md`, +49 lines, blob `3799202`; `d7d3d04..34eb4c5` is exactly one new file `pr-76-review.md`, +40 lines; `d7d3d04..49619a9` is exactly one new file `pr-75-review.md`, +37 lines, blob `f5a29f3` — all matching the file's stated scopes.
- **The certified line-19/line-33 pair — independently confirmed.** Line 19 of the committed `pr-79-review.md` at `d8bc7a7` (read back with `git log -L 19,19`) states verbatim that in `pr-75-review.md` "the strings 'Submission note', 'self_review_forbidden', and 'terminal' appear nowhere in its 37 lines," and line 33 of the committed `pr-75-review.md` at `49619a9` (read back with `git log -L 33,33`) reads verbatim "Documents its own submission limitation (the `self_review_forbidden` terminal rejection) transparently rather than silently omitting it." — so the absolute is falsified for two of the three strings, exactly as the file claims. Both cited line numbers (19 and 33) are exact, and `pr-75-review.md` genuinely carries no submission-note paragraph (its header block goes directly from "**Scope reviewed:**" to "## Methodology").
- **The certified findings against `pr-76-review.md` — independently confirmed, correctly located.** Line 7 of `pr-76-review.md` at `34eb4c5` is the Submission-note paragraph carrying the "PR #72–#75" corroboration set, and line 36 is the quality bullet "Documents its own submission limitation (the `self_review_forbidden` terminal rejection) transparently rather than silently omitting it." — both exactly as the reviewed file states.
- **Verdict correctness of the reviewed artifact — verified.** `pr-91-review.md` at `ac8fb12` carries `**Verdict: APPROVE**`, and the `pr-86-review.md` it reviews carries `**Verdict: REQUEST_CHANGES**` with the line-19 finding and its one-line fix — consistent with the file's account.
- **The file's own submission-note scoping — verified accurate.** Line 7 scopes the documented-limitation set to "PR #72–#74", which is the accurate set (`pr-72`/`pr-73`/`pr-74` carry the note; `pr-75-review.md` does not) — so the file applies the correction it certifies for `pr-76-review.md` there.

Claims inherited from deeper in the chain beyond those above (the `9944a79` range stat, PR #64 findings 1–2 and 4–5, and the `c795980` breakdown) were not re-derived in this pass; per the explicit declinations re-read in the PR #86 and PR #91 artifacts, they rest on the PR #72–#74 passes, and none of them is contradicted by any check performed here.

## Recommendation

REQUEST_CHANGES, limited to the one-clause re-scoping of the "PR #86–#91" corroboration set on line 25 of `reviews/pr-95-review.md`. No code, config, lockfile, or test remediation is implicated by PR #97. The separate bounded remediation task for `pr-79-review.md` (line 19 rewording) should proceed as the reviewed file describes, and the PR #64 REQUEST_CHANGES findings (Computer games stall; dead `GetAllLegalMoves` surface; duplicated SA password; temporary scaffolding; lockfile churn — as characterized in the chain and not re-derived in this pass) remain in their own separate bounded remediation task, unaffected by this PR.
