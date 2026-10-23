"""Opt-in larger sweep of the battlefield-generator corpora (city, wild, mine, castle, special).

usage: python -I -B sweep_generators.py <DARKLAND.EXE> <game folder> <out dir> <seed> [--scale N] [--workers W] [--only a,b]

Runs the committed corpus builders with DL_SWEEP_SEED=<seed> (the builders' random streams are offset
by it; seed 0 is the committed corpus), each with DL_COVERAGE_DIR=<out>/<name>-s<seed>/cov, and the
scenario count of block_coverage.BUILDERS times --scale. The corpus written (<out>/<name>-s<seed>/<corpus file>)
is replayed by the C# tests through the CITY_CORPUS / WILD_CORPUS / MINE_CORPUS / CASTLE_CORPUS /
SPECIAL_CORPUS environment variables (absolute path). Wall seconds per builder go to run.json.
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
sys.path.insert(0, str(HERE))
from block_coverage import BUILDERS  # noqa: E402

ARG_STYLE = {"city": "count", "wild": "count+workers", "mine": "count+workers", "castle": "count+workers",
             "special": "workers"}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("out")
    ap.add_argument("seed", type=int)
    ap.add_argument("--scale", type=float, default=1.0)
    ap.add_argument("--workers", type=int, default=2)
    ap.add_argument("--grid", type=int, default=0, help="DL_SWEEP_GRID=n (wild: n pairs of the forceMap x forceSide grid, the base list being 1 scenario); name suffix -g")
    ap.add_argument("--only", default="city,wild,mine,castle,special")
    a = ap.parse_args()
    for name in a.only.split(","):
        script, _, corpus, extra = BUILDERS[name]
        d = Path(a.out) / ("%s-%s%d" % (name, "g" if a.grid else "s", a.seed))
        (d / "cov").mkdir(parents=True, exist_ok=True)
        for f in (d / "cov").glob("cov-*.jsonl"):
            f.unlink()
        out = d / corpus
        argv = [sys.executable, "-I", "-B", str(HERE / script), a.exe, a.game, str(out)]
        style = ARG_STYLE[name]
        if style.startswith("count"):
            argv.append("1" if a.grid and name == "wild" else str(max(1, int(int(extra[0]) * a.scale))))
        if style.endswith("workers"):
            argv.append(str(a.workers))
        env = dict(os.environ, DL_COVERAGE_DIR=str(d / "cov"), DL_SWEEP_SEED=str(a.seed), **({"DL_SWEEP_GRID": str(a.grid)} if a.grid else {}))
        t = time.time()
        r = subprocess.run(argv, env=env, capture_output=True, text=True)
        took = time.time() - t
        (d / "run.json").write_text(json.dumps({"seconds": round(took), "workers": a.workers, "exit": r.returncode, "same_as_committed": False,
                                                "argv": argv[3:], "tail": (r.stdout + r.stderr)[-400:]}, indent=1))
        print("%-8s seed %d %5ds exit %d" % (name, a.seed, took, r.returncode), flush=True)


if __name__ == "__main__":
    main()
