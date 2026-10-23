"""The whole pilot in one command: battle functions called directly from captured fight states, the original and the rewrite compared.

usage: python -I -B battle_calls_run.py <work folder> --functions R014+0x08FE,... --states street-p300,... [--iterations 500] [--workers 2]
           [--skip-fuzz] [--skip-test]

  1. battle_fuzz.py (the original, coverage-guided changes of the machine's memory) per worker, over the states split between them:
     <work>/fuzz/<function>/<state>.json (the kept inputs and the original's outcome) and .../cov/ (their coverage)
  2. battle_calls.py export: <work>/jobs.json.gz (the kept inputs with the states they start from)
  3. dotnet test tests/Darklands.Battles.Tests (BattleCallsTests: the rewrite makes the same calls): <work>/results.tsv, excluded.txt
  4. <work>/cov-agree/<function>.jsonl: the coverage lines of the runs the rewrite agreed with (the ones that count); the open items they reach
     are measured with the batch folder's measure script (docs/faithfulness/branches-missed.csv against the counted folders).

The states come from `battle_calls.py capture` (fights of battle_oracle.FIGHTS played in the DOS machine). Everything here is a tester: no
original-side module is changed, so no recording expires.
"""
from __future__ import annotations

import argparse
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
    ap.add_argument("--states-folder", default=None, help="folder of the captured .state files (default <work>/states)")
    ap.add_argument("--functions", required=True)
    ap.add_argument("--states", required=True)
    ap.add_argument("--iterations", type=int, default=500)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--workers", type=int, default=2)
    ap.add_argument("--baseline", default=None)
    ap.add_argument("--game", default=str(REPO.parent / "game" / "DARKLAND"))
    ap.add_argument("--skip-fuzz", action="store_true")
    ap.add_argument("--skip-test", action="store_true")
    a = ap.parse_args()
    work = Path(a.work)
    work.mkdir(parents=True, exist_ok=True)
    states_folder = Path(a.states_folder or work / "states")
    names = a.states.split(",")
    t0 = time.time()
    if not a.skip_fuzz:
        procs = []
        for w in range(a.workers):
            mine = names[w::a.workers]
            if not mine:
                continue
            cmd = PY + [str(HERE / "battle_fuzz.py"), str(states_folder), str(work / "fuzz"), "--functions", a.functions, "--states-list", ",".join(mine),
                        "--iterations", str(a.iterations), "--seed", str(a.seed)] + (["--baseline", a.baseline] if a.baseline else [])
            procs.append(subprocess.Popen(cmd, stdout=open(work / ("fuzz-%d.log" % w), "w"), stderr=subprocess.STDOUT))
        for p in procs:
            p.wait()
    fuzz_seconds = time.time() - t0
    folders = sorted(str(p) for p in (work / "fuzz").glob("*") if p.is_dir())
    jobs = work / "jobs.json.gz"
    subprocess.run(PY + [str(HERE / "battle_calls.py"), "export", str(jobs), "--states", str(states_folder)] + folders, check=True)
    t1 = time.time()
    if not a.skip_test:
        env = dict(os.environ, DARKLANDS_BATTLE_CALLS=str(jobs), DARKLANDS_BATTLE_CALLS_OUT=str(work), DARKLANDS_DIR=a.game)
        subprocess.run(["dotnet", "test", str(REPO / "tests" / "Darklands.Battles.Tests"), "-c", "Release", "--filter", "FullyQualifiedName~BattleCallsTests",
                        "--logger", "console;verbosity=detailed"], check=False, env=env, cwd=REPO)
    test_seconds = time.time() - t1
    excluded = set((work / "excluded.txt").read_text(encoding="utf-8").split()) if (work / "excluded.txt").exists() else set()
    agree = work / "cov-agree"
    agree.mkdir(exist_ok=True)
    kept = dropped = 0
    for folder in folders:
        out = agree / ("cov-" + Path(folder).name + ".jsonl")
        with out.open("w", encoding="utf-8") as f:
            for line_file in sorted(Path(folder).glob("cov/cov-*.jsonl")):
                for line in line_file.read_text(encoding="utf-8").splitlines():
                    j = json.loads(line)
                    if "%s|%s" % (j["unit"], j["state"]) in excluded:
                        dropped += 1
                    else:
                        kept += 1
                        f.write(line + "\n")
    print("original side: %.0f s wall (%d workers); rewrite side: %.0f s; coverage lines kept %d, dropped %d (differ or not judged)" % (
        fuzz_seconds, a.workers, test_seconds, kept, dropped))


if __name__ == "__main__":
    main()
