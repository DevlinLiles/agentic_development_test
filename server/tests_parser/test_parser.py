import os
import sys
import unittest
import glob
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER_DIR = os.path.dirname(HERE)  # server/


def _run_gate(results_dir, threshold, impl_assemblies):
    """Mirror of the heredoc parser in server/run-qa-coverage.sh."""
    failures = []

    cobertura_files = glob.glob(os.path.join(results_dir, "**", "coverage.cobertura.xml"), recursive=True)
    if not cobertura_files:
        return [f"no coverage.cobertura.xml found under {results_dir}"]
    root = ET.parse(cobertura_files[0]).getroot()

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
            continue
        pct = 100.0 * agg["covered"] / agg["lines"]
        if pct < threshold:
            failures.append(f"module {asm} line coverage {pct:.2f}% < threshold {threshold:.2f}%")

    skipped_total = 0
    for trx in glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True):
        r = ET.parse(trx).getroot()
        for u in r.iter("UnitTestResult"):
            if u.attrib.get("outcome", "") == "NotExecuted":
                skipped_total += 1
    if skipped_total > 0:
        failures.append(f"{skipped_total} skipped/xfailed test(s) remain")

    return failures


class ParserLogicTests(unittest.TestCase):
    IMPL = ["ChessMvp.Domain.dll", "ChessMvp.Infrastructure.dll"]
    FIXTURES = os.path.join(SERVER_DIR, "_fixtures")

    def test_passing_threshold_70(self):
        # Domain 75%, Infrastructure ~77.7% -> both >= 70, no skips -> PASS (empty failures).
        self.assertEqual(_run_gate(self.FIXTURES, 70.0, self.IMPL), [])

    def test_failing_threshold_80(self):
        # Domain 75% < 80 and Infrastructure 77.7% < 80 -> FAIL on both modules.
        failures = _run_gate(self.FIXTURES, 80.0, self.IMPL)
        self.assertEqual(len(failures), 2)
        self.assertIn("ChessMvp.Domain.dll", failures[0])
        self.assertIn("ChessMvp.Infrastructure.dll", failures[1])

    def test_api_assembly_excluded_from_gate(self):
        # ChessMvp.Api.dll appears in the cobertura but is NOT in IMPL, so it must never
        # produce a failure entry even when its single line is uncovered.
        failures = _run_gate(self.FIXTURES, 70.0, self.IMPL)
        self.assertFalse(any("ChessMvp.Api" in f for f in failures))

    def test_skip_detection(self):
        # Fixture trx has no NotExecuted; inject a skipped outcome and confirm the gate flags it.
        import tempfile, shutil
        tmp = tempfile.mkdtemp()
        shutil.copy(os.path.join(self.FIXTURES, "coverage.cobertura.xml"), tmp)
        with open(os.path.join(tmp, "qa.trx"), "w") as f:
            f.write('<?xml version="1.0"?><TestRun><Results>'
                    '<UnitTestResult testName="A" outcome="Passed"/>'
                    '<UnitTestResult testName="SKIP" outcome="NotExecuted"/>'
                    '</Results></TestRun>')
        failures = _run_gate(tmp, 70.0, self.IMPL)
        self.assertTrue(any("skipped/xfailed" in f for f in failures))
        shutil.rmtree(tmp)


if __name__ == "__main__":
    unittest.main()
