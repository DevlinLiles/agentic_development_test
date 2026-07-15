"""Convenience runner for the QA-gate parser regression tests.

The canonical regression suite lives in ``tests_parser/test_parser.py`` and is run via
``python3 -m unittest tests_parser.test_parser``. This module simply re-invokes that suite so a
developer can also do ``python3 -m pytest server/_verify_parser.py`` (or ``python3 _verify_parser.py``)
and exercise the same coverage/skip gate logic that ``run-qa-coverage.sh`` embeds.
"""
import os
import sys
import unittest

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from tests_parser import test_parser  # noqa: E402  (path-adjusted import above)


def load_tests(loader, tests, pattern):  # noqa: D401  (unittest protocol)
    return loader.loadTestsFromModule(test_parser)


if __name__ == "__main__":
    unittest.main(module=test_parser, exit=False)
