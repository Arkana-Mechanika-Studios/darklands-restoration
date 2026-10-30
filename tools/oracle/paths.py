"""Where the shared, git-ignored local/ folder is (see tools/local_paths.py: local_dir).

Worktrees hold no junction or symlink to local/ (an incident on 2026-10-30 deleted it through one): tools find it through
DARKLANDS_LOCAL, else <DARKLANDS_ROOT>/code/local, else <repository>/local.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from local_paths import local_dir  # noqa: E402,F401
