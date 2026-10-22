"""Compare two rerun_all.py output folders, ignoring timings and where the original ran: the verdicts (summary.json), every report, trace,
log, saved game and coverage file of every job.

usage: python -I -B diff_runs.py <folder A> <folder B>

Ignored: the seconds (summary.json's wall, report.json's seconds), whether the original was replayed or run live, the out folder's own
path inside rewrite.log, the jobs' stdout logs (<out>/<job>.log: they carry timings and the snapshot lines). Exit 1 on any other difference.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

DROP_KEYS = {"seconds", "original run", "wall"}


def strip(x):
    if isinstance(x, dict):
        return {k: strip(v) for k, v in x.items() if k not in DROP_KEYS}
    if isinstance(x, list):
        return [strip(v) for v in x]
    return x


def content(path: Path, root: Path):
    data = path.read_bytes()
    if path.name == "summary.json":
        summary = json.loads(data.decode("utf-8"))
        summary["jobs"] = [{k: v for k, v in j.items() if k not in ("wall", "original", "seconds")} for j in summary["jobs"]]
        return json.dumps({k: v for k, v in summary.items() if k not in ("seconds", "workers")}, sort_keys=True).encode()
    if path.suffix == ".json":
        return json.dumps(strip(json.loads(data.decode("utf-8"))), sort_keys=True).encode()
    if path.name == "rewrite.log":
        return data.replace(str(root).encode(), b"<out>").replace(str(root).replace("\\", "/").encode(), b"<out>")
    return data


def main():
    a, b = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
    files = lambda root: {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file() and not (p.parent == root and p.suffix == ".log")}
    fa, fb = files(a), files(b)
    bad = ["only in %s: %s" % (a.name, f) for f in sorted(fa - fb)] + ["only in %s: %s" % (b.name, f) for f in sorted(fb - fa)]
    for f in sorted(fa & fb):
        # cs-saves/, cs/ and the trace files are the C# side's and the original's outputs alike: all must agree
        if content(a / f, a) != content(b / f, b):
            bad.append("differs: " + f)
    print("%d files compared, %d differences" % (len(fa & fb), len(bad)))
    for line in bad[:50]:
        print("  " + line)
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
