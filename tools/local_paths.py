"""Machine-local paths of the tools (the reference decompilation checkout, a Python interpreter, the audit folder).

Each path is looked up in this order: the environment variable, then the file local/paths.json at the
repository root (git-ignored), else a clear error.

  key        environment variable
  reference  DARKLANDS_REFERENCE   root of the reference decompilation checkout (what `ref:` paths are relative to)
  python     DARKLANDS_PYTHON      Python interpreter with the reference's dependencies
  audit      DARKLANDS_AUDIT       folder of the compiler audit results

local/paths.json example: {"reference": "/path/to/reference", "python": "/path/to/python", "audit": "/path/to/audit"}
"""
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JSON_FILE = os.path.join(REPO, 'local', 'paths.json')
ENV = {'reference': 'DARKLANDS_REFERENCE', 'python': 'DARKLANDS_PYTHON', 'audit': 'DARKLANDS_AUDIT'}


def get(key):
    """Return the configured path for `key` ('reference', 'python' or 'audit'); exit with a clear error if unset."""
    var = ENV[key]
    value = os.environ.get(var)
    if value:
        return value
    try:
        with open(JSON_FILE, encoding='utf-8') as fh:
            value = json.load(fh).get(key)
    except FileNotFoundError:
        value = None
    except (OSError, ValueError) as e:
        sys.exit('cannot read %s: %s' % (JSON_FILE, e))
    if value:
        return value
    sys.exit('%s is not set: set the environment variable %s or the key "%s" in %s' % (key, var, key, JSON_FILE))


def reference():
    return get('reference')
