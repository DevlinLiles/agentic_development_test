"""pytest configuration: make the repo root importable for tests.

Tests import ``chess_engine`` as a top-level package. With no installed
package metadata, we ensure the repo root is on ``sys.path``.
"""

from __future__ import annotations

import sys
from pathlib import Path

_ROOT = Path(__file__).resolve().parent.parent
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))
