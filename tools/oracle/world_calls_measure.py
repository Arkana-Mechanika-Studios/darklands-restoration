"""What the direct world calls gain: the open outcomes of docs/faithfulness/branches-missed.csv (any record, the areas travel R030, rules R029 R039,
cards R043 and the resident root first) that coverage reaches and the counted folders did not (battle_calls_measure.py's measure, for these records).

usage: python -I -B world_calls_measure.py baseline [--local <local folder>]
       python -I -B world_calls_measure.py measure <out.json> <cov folder>[::<excluded file>] ... [--local <local folder>] [--records R029,R030,...]

baseline   merges the cov lines of every folder of <local>/faithfulness/branches-work (the card folders too: R043 is the card engine) into
           <local>/batch/worldcalls/baseline.pkl: (instructions, branch edges) per record, every record. world_fuzz.py keeps a run only if it beats it.
measure    the open items the given cov folders reach beyond the baseline (lines "unit|state" of the excluded file are left out), per function and
           per area; --records restricts the count (default: all records; the report lists the areas apart).
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import pickle
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from paths import local_dir  # noqa: E402
import battle_calls_measure as bcm  # noqa: E402

AREAS = {"travel R030": {"R030"}, "rules R029/R039": {"R029", "R039"}, "cards R043": {"R043"}, "resident root": {"root"}}


def baseline(local: Path):
    work = local / "faithfulness" / "branches-work"
    ins, edg = collections.defaultdict(set), collections.defaultdict(set)
    n = 0
    for d in sorted(os.listdir(work)):
        if not (work / d).is_dir():
            continue
        for j in bcm.lines(str(work / d)):
            n += 1
            for k, v in j["insns"].items():
                ins[k].update(v)
            for k, v in j["edges"].items():
                edg[k].update(tuple(e) for e in v)
    out = ({k: sorted(v) for k, v in ins.items()}, {k: sorted(v) for k, v in edg.items()})
    target = local / "batch" / "worldcalls" / "baseline.pkl"
    target.parent.mkdir(parents=True, exist_ok=True)
    pickle.dump(out, open(target, "wb"))
    print("baseline: %d cov lines, %d instructions, %d edges in %d records -> %s" % (n, sum(map(len, out[0].values())), sum(map(len, out[1].values())), len(out[0]), target))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("what", choices=["baseline", "measure"])
    ap.add_argument("rest", nargs="*")
    ap.add_argument("--local", default=local_dir())
    ap.add_argument("--records")
    a = ap.parse_args()
    local = Path(a.local)
    if a.what == "baseline":
        baseline(local)
        return
    out, specs = a.rest[0], a.rest[1:]
    # battle_calls_measure restricts to its BAT set: every record here
    import csv
    every = {r["record"] for r in csv.DictReader(open(bcm.REPO / "docs" / "faithfulness" / "branches-missed.csv", encoding="utf-8"))}
    bcm.BAT = set(a.records.split(",")) if a.records else every
    items = bcm.Items()
    total = sum(len(v) for v in items.opens.values())
    base_i, base_e = pickle.load(open(local / "batch" / "worldcalls" / "baseline.pkl", "rb"))
    already = items.reached({k: set(v) for k, v in base_i.items() if k in bcm.BAT}, {k: {tuple(e) for e in v} for k, v in base_e.items() if k in bcm.BAT})
    ins, edg, runs = bcm.merged(specs)
    got = items.reached(ins, edg) - already
    per = collections.Counter(g[0] for g in got)
    print("open items: %d (the baseline reaches %d of them); %d runs reach %d more, in %d functions" % (total, len(already), runs, len(got), len(per)))
    for area, recs in AREAS.items():
        mine = [g for g in got if (g[0].split("+")[0] if g[0][0] == "R" else "root") in recs]
        print("  %-18s %4d of %d open" % (area, len(mine), sum(len(v) for k, v in items.opens.items() if (k.split("+")[0] if k[0] == "R" else "root") in recs)))
    for name, n in per.most_common(40):
        print("  %-26s %4d  of %d open" % (name, n, len(items.opens[name])))
    json.dump(sorted("|".join(g) for g in got), open(out, "w"))


if __name__ == "__main__":
    main()
