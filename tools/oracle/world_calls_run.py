"""Export, rewrite test and agreeing coverage of the world calls in one command (the fuzzing is world_fuzz.py, run by hand, one process a worker).

usage: python -I -B world_calls_run.py <work folder> [--states-folder <folder>] [--game <game folder>] [--skip-test]
           (<work>/fuzz* hold world_fuzz.py's output: <function>/<state>.json and <function>/cov/)

  1. world_calls.py export: <work>/jobs.json.gz (the kept inputs with the saved games they start from)
  2. dotnet test tests/Darklands.WorldCalls.Tests (WorldCallsTests: the rewrite makes the same calls): <work>/results.tsv, excluded.txt
  3. <work>/cov-agree/cov-<function>.jsonl: the coverage lines of the runs the rewrite agreed with (the ones that count for the integrator);
     the open items they reach are measured with world_calls_measure.py.
  4. the per-function table (jobs, agree, differ, skip, gap, exception; original CPU seconds from the fuzz output)
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PY = [sys.executable, "-I", "-B"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("work")
    ap.add_argument("--states-folder", default=None)
    ap.add_argument("--game", default=str(REPO.parent / "game" / "DARKLAND"))
    ap.add_argument("--skip-test", action="store_true")
    a = ap.parse_args()
    work = Path(a.work)
    states_folder = Path(a.states_folder or work / "states")
    folders = sorted(str(p) for fz in work.glob("fuzz*") if fz.is_dir() for p in fz.glob("*") if p.is_dir())
    jobs = work / "jobs.json.gz"
    subprocess.run(PY + [str(HERE / "world_calls.py"), "export", str(jobs), "--states", str(states_folder)] + folders, check=True)
    t1 = time.time()
    if not a.skip_test:
        env = dict(os.environ, DARKLANDS_WORLD_CALLS=str(jobs), DARKLANDS_WORLD_CALLS_OUT=str(work), DARKLANDS_DIR=a.game)
        subprocess.run(["dotnet", "test", str(REPO / "tests" / "Darklands.WorldCalls.Tests"), "-c", "Release", "--logger", "console;verbosity=detailed"],
                       check=False, env=env, cwd=REPO)
    test_seconds = time.time() - t1
    excluded = set((work / "excluded.txt").read_text(encoding="utf-8").split()) if (work / "excluded.txt").exists() else set()
    agree = work / "cov-agree"
    agree.mkdir(exist_ok=True)
    kept = dropped = 0
    per_function = collections.defaultdict(lambda: collections.Counter())
    for folder in folders:
        name = Path(folder).name
        with (agree / ("cov-" + name + ".jsonl")).open("a", encoding="utf-8") as f:
            for line_file in sorted(Path(folder).glob("cov/cov-*.jsonl")):
                for line in line_file.read_text(encoding="utf-8").splitlines():
                    j = json.loads(line)
                    if "%s|%s" % (j["unit"], j["state"]) in excluded:
                        dropped += 1
                    else:
                        kept += 1
                        f.write(line + "\n")
        for jf in sorted(Path(folder).glob("*.json")):
            d = json.loads(jf.read_text(encoding="utf-8"))
            per_function[d["function"]]["cpu"] += d["cpu"]
            per_function[d["function"]]["iterations"] += d["iterations"]
            per_function[d["function"]]["kept"] += len(d["kept"])
    verdicts = collections.defaultdict(collections.Counter)
    if (work / "results.tsv").exists():
        for line in (work / "results.tsv").read_text(encoding="utf-8").splitlines():
            ident, verdict, *_ = line.split("\t") + [""]
            verdicts[ident.split("|")[0]][verdict] += 1
    print("%-14s %7s %6s %6s | %5s %6s %5s %4s %4s" % ("function", "iters", "kept", "cpu s", "agree", "DIFFER", "skip", "gap", "exc"))
    for fn in sorted(per_function):
        v, p = verdicts[fn], per_function[fn]
        print("%-14s %7d %6d %6.0f | %5d %6d %5d %4d %4d" % (fn, p["iterations"], p["kept"], p["cpu"], v["agree"], v["DIFFER"], v["SKIP"], v["GAP"], v["EXCEPTION"]))
    print("rewrite side: %.0f s; coverage lines kept %d, dropped %d (differ or not judged)" % (test_seconds, kept, dropped))


if __name__ == "__main__":
    main()
