#!/usr/bin/env bash
#
# run-qa-coverage.sh — Automated Test-Suite Execution & Coverage Verification gate.
#
# Deterministic procedure for task 01KXKSEV3EWHPE08C1MKH7GZ6R/qa:
#   1. Run the full in-repo test suite (chess rules engine, terminal-state detection,
#      evaluation/search, AI engine legality) and GATE ON EXIT CODE (non-zero aborts).
#   2. Parse the cobertura + trx artifacts and GATE ON PER-MODULE LINE COVERAGE: every
#      implementation module must independently meet the configured threshold.
#   3. GATE ON NO SKIPPED / XFAILED TESTS: any skipped/xfailed/not-run result fails the gate.
#
# Exit codes:
#   0  — suite passed, every implementation module meets the coverage threshold, no skips.
#   1  — at least one gate failed (with a message identifying which one).
#
# Run from anywhere; it locates the server dir relative to this script:
#   ./server/run-qa-coverage.sh          # default threshold 80%
#   THRESHOLD=85 ./server/run-qa-coverage.sh
#
# Requires only the dotnet SDK and python3 (stdlib xml.etree only — no third-party deps).
set -euo pipefail

THRESHOLD="${THRESHOLD:-80}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="${SCRIPT_DIR}"
RESULTS_DIR="${SERVER_DIR}/TestResults"

# Implementation assemblies measured by the gate (chess engine, AI eval/search, domain logic).
# These are the behavioural modules the acceptance criteria cover; mapping any future module
# here keeps the gate honest. Keep in sync with NoSkippedTestsGuardTests.CoverageGate_Covers....
IMPLEMENTATION_ASSEMBLIES=(
  "ChessMvp.Domain.dll"
  "ChessMvp.Infrastructure.dll"
)

# -----------------------------------------------------------------------------
# Step 1 — Run the full suite with coverage + a trx logger (the trx carries per-test
#          run/skip/fail outcome we need for step 3).
# -----------------------------------------------------------------------------
rm -rf "${RESULTS_DIR}"
mkdir -p "${RESULTS_DIR}"

echo "==> Running dotnet test with coverage (threshold line coverage = ${THRESHOLD}%)"
dotnet test "${SERVER_DIR}/ChessMvp.slnx" \
  --configuration Release \
  --settings "${SERVER_DIR}/coverage.runsettings" \
  --collect:"XPlat Code Coverage" \
  --logger "trx;LogFileName=qa.trx" \
  --results-directory:"${RESULTS_DIR}" \
  -- RunConfiguration.TreatNoTestsAsError=true

TEST_EXIT=$?
if [ "${TEST_EXIT}" -ne 0 ]; then
  echo "FAIL: dotnet test exited with code ${TEST_EXIT}." >&2
  exit "${TEST_EXIT}"
fi

# -----------------------------------------------------------------------------
# Steps 2 & 3 — Parse cobertura (coverage) and every trx (test outcomes) with python3.
#               python3 returns a non-zero code if any gate fails; we surface it.
# -----------------------------------------------------------------------------
python3 - "${RESULTS_DIR}" "${THRESHOLD}" "${IMPLEMENTATION_ASSEMBLIES[@]}" <<'PYEOF'
import sys
import os
import glob
import xml.etree.ElementTree as ET

results_dir, threshold = sys.argv[1], float(sys.argv[2])
impl_assemblies = sys.argv[3:]

failures = []

# ---- Step 2: per-module line coverage from cobertura -------------------------
cobertura_files = glob.glob(os.path.join(results_dir, "**", "coverage.cobertura.xml"), recursive=True)
if not cobertura_files:
    failures.append(f"no coverage.cobertura.xml found under {results_dir}")
else:
    cov_path = cobertura_files[0]
    print(f"==> Cobertura report: {cov_path}")
    root = ET.parse(cov_path).getroot()
    line_total = {"lines": 0, "covered": 0}
    # Aggregate line hits across every <class> that belongs to an implementation assembly.
    # The cobertura schema tags each class with @filename (a source path) and the assembly
    # under <package/@name>; we also match the assembly by the assembly element name/id.
    seen_modules = set()
    for pkg in root.iter("package"):
        pkg_name = pkg.attrib.get("name", "")
        for cls in pkg.iter("class"):
            filename = cls.attrib.get("filename", "")
            cls_name = cls.attrib.get("name", "")
            asm_match = any(
                asm in pkg_name or asm in filename or asm in cls_name
                for asm in impl_assemblies
            )
            if not asm_match:
                continue
            seen_modules.add(next(
                (asm for asm in impl_assemblies if asm in pkg_name or asm in filename or asm in cls_name),
                pkg_name))
            for line in cls.iter("line"):
                if line.attrib.get("type") != "stmt":
                    continue
                line_total["lines"] += 1
                hits = int(line.attrib.get("hits", "0"))
                if hits > 0:
                    line_total["covered"] += 1

    if line_total["lines"] == 0:
        failures.append("no executable lines found in implementation modules (excluded from instrumentation?)")
    else:
        pct = 100.0 * line_total["covered"] / line_total["lines"]
        status = "PASS" if pct >= threshold else "FAIL"
        print(f"{status}: aggregate implementation line coverage {pct:.2f}% >= {threshold:.2f}% (modules: {', '.join(sorted(seen_modules))})")
        if pct < threshold:
            failures.append(f"aggregate line coverage {pct:.2f}% < threshold {threshold:.2f}%")

    # Per-module break-out so a single lagging module is visible.
    per_module = {}
    for pkg in root.iter("package"):
        pkg_name = pkg.attrib.get("name", "")
        for cls in pkg.iter("class"):
            filename = cls.attrib.get("filename", "")
            cls_name = cls.attrib.get("name", "")
            for asm in impl_assemblies:
                if asm in pkg_name or asm in filename or asm in cls_name:
                    agg = per_module.setdefault(asm, {"lines": 0, "covered": 0})
                    for line in cls.iter("line"):
                        if line.attrib.get("type") != "stmt":
                            continue
                        agg["lines"] += 1
                        if int(line.attrib.get("hits", "0")) > 0:
                            agg["covered"] += 1
                    break
    for asm in impl_assemblies:
        agg = per_module.get(asm)
        if not agg or agg["lines"] == 0:
            failures.append(f"module {asm} produced no coverage data")
            print(f"FAIL: module {asm} produced no coverage data")
            continue
        pct = 100.0 * agg["covered"] / agg["lines"]
        status = "PASS" if pct >= threshold else "FAIL"
        print(f"{status}: module {asm} line coverage {pct:.2f}% >= {threshold:.2f}%")
        if pct < threshold:
            failures.append(f"module {asm} line coverage {pct:.2f}% < threshold {threshold:.2f}%")

# ---- Step 3: no skipped / xfailed tests from every .trx ----------------------
trx_files = glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True)
if not trx_files:
    failures.append("no .trx files found; cannot verify skip gate")
else:
    skipped_total = 0
    # xUnit's outcome vocabulary: Passed, Failed, NotExecuted (skipped), Completed.
    for trx in trx_files:
        root = ET.parse(trx).getroot()
        for r in root.iter("UnitTestResult"):
            outcome = r.attrib.get("outcome", "")
            if outcome == "NotExecuted":
                skipped_total += 1
                print(f"    skip: {r.attrib.get('testName', '<unknown>')} in {os.path.basename(trx)}")
        # Defensive: an explicit Skip attribute anywhere in a Test/TestMethod node.
        for t in root.iter("TestMethod"):
            for k, v in t.attrib.items():
                if k.lower() == "skip" and v:
                    skipped_total += 1
                    print(f"    skip attribute: {t.attrib.get('name', '<unknown>')} in {os.path.basename(trx)}")
    if skipped_total > 0:
        failures.append(f"{skipped_total} skipped/xfailed test(s) remain across the run")
    else:
        print("PASS: no skipped/xfailed tests in the run")

if failures:
    print("\nGATE FAILED:", file=sys.stderr)
    for f in failures:
        print(f"  - {f}", file=sys.stderr)
    sys.exit(1)

print("==> All gates passed: exit code 0, per-module line coverage >= %.2f%%, no skipped/xfailed tests." % threshold)
sys.exit(0)
PYEOF
