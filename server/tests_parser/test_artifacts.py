import os
import unittest
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER_DIR = os.path.dirname(HERE)


class RunsettingsAndScriptTests(unittest.TestCase):
    """Sanity checks on the QA-gate artifacts (config + shell script)."""

    def test_coverage_runsettings_is_well_formed_xml(self):
        path = os.path.join(SERVER_DIR, "coverage.runsettings")
        root = ET.parse(path).getroot()  # raises if not well-formed
        # Verify the coverlet threshold knobs the gate depends on are present.
        cfg = root.find(".//Configuration")
        self.assertIsNotNone(cfg, "coverlet <Configuration> block missing")
        threshold = cfg.findtext("Threshold")
        threshold_type = cfg.findtext("ThresholdType")
        self.assertEqual(threshold, "80")
        self.assertEqual(threshold_type, "line")

    def test_runsettings_excludes_test_assemblies(self):
        path = os.path.join(SERVER_DIR, "coverage.runsettings")
        root = ET.parse(path).getroot()
        include_test = root.findtext(".//IncludeTestAssembly")
        self.assertEqual(include_test, "false",
                         "coverage must not include test assemblies in the measured set")

    def test_run_qa_script_exists_and_is_bash(self):
        path = os.path.join(SERVER_DIR, "run-qa-coverage.sh")
        self.assertTrue(os.path.isfile(path), "run-qa-coverage.sh missing")
        with open(path, "r") as f:
            head = f.read(64)
        self.assertIn("#!/usr/bin/env bash", head, "run-qa-coverage.sh must be a bash script")

    def test_run_qa_script_gates_on_all_three_conditions(self):
        with open(os.path.join(SERVER_DIR, "run-qa-coverage.sh"), "r") as f:
            src = f.read()
        # Exit-code gate.
        self.assertIn("if [ \"${TEST_EXIT}\" -ne 0 ]", src)
        # Per-module coverage gate.
        self.assertIn("IMPLEMENTATION_ASSEMBLIES", src)
        # Skipped/xfailed gate.
        self.assertIn("NotExecuted", src)


if __name__ == "__main__":
    unittest.main()
