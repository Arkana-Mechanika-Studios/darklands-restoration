"""What the direct battle calls gain: the open outcomes of docs/faithfulness/branches-missed.csv (battle records R002 R003 R014..R025) that
coverage reaches and the counted folders did not.

usage: python -I -B battle_calls_measure.py baseline [--local <local folder>]
       python -I -B battle_calls_measure.py measure <out.json> <cov folder>[::<excluded file>] ... [--local <local folder>]

baseline   merges the cov lines of every folder of <local>/faithfulness/branches-work except the card folders (cards*) into
           <local>/batch/battlecalls/baseline.pkl: (instructions, branch edges) per record, battle records only. battle_fuzz.py keeps a
           run only if it beats this coverage.
measure    the open items (function, payload, block / taken / fallthrough) the given cov folders reach: lines "unit|state" listed in the
           excluded file (the runs the rewrite did not agree with) are left out. The open list is, by construction, what the counted
           folders did not reach; the baseline is checked against it all the same (items it reaches are not counted as gains).
           Writes the reached items to <out.json> and prints the count per function.
A cov line is {"unit", "state", "insns": {record: [payload ...]}, "edges": {record: [[from, to] ...]}} (run_unit.py's / battle_oracle.py's cov format).
"""
from __future__ import annotations

import argparse
import bisect
import collections
import csv
import glob
import json
import os
import pickle
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import cfg as cfgmod  # noqa: E402
from run_unit import Rtlink  # noqa: E402

REPO = HERE.parent.parent
GAME = REPO.parent / "game" / "DARKLAND"
BAT = {"R002", "R003"} | {"R%03d" % i for i in range(14, 26)}


def lines(folder):
    for f in glob.glob(os.path.join(folder, "**", "cov*.jsonl"), recursive=True):
        for line in open(f, encoding="utf-8"):
            if line.strip():
                yield json.loads(line)


def restrict(j):
    return {"insns": {r: v for r, v in j["insns"].items() if r in BAT}, "edges": {r: v for r, v in j["edges"].items() if r in BAT}}


def baseline(local: Path):
    work = local / "faithfulness" / "branches-work"
    ins, edg = collections.defaultdict(set), collections.defaultdict(set)
    for d in sorted(os.listdir(work)):
        if d.startswith("cards") or not (work / d).is_dir():
            continue
        for j in lines(str(work / d)):
            r = restrict(j)
            for k, v in r["insns"].items():
                ins[k] |= set(v)
            for k, v in r["edges"].items():
                edg[k] |= {tuple(e) for e in v}
    out = ({k: sorted(v) for k, v in ins.items()}, {k: sorted(v) for k, v in edg.items()})
    target = local / "batch" / "battlecalls" / "baseline.pkl"
    target.parent.mkdir(parents=True, exist_ok=True)
    pickle.dump(out, open(target, "wb"))
    print("baseline: %d instructions, %d edges in %d records -> %s" % (sum(map(len, out[0].values())), sum(map(len, out[1].values())), len(out[0]), target))


class Items:
    """The open items and what a set of coverage reaches of them."""

    def __init__(self, records=BAT, csv_path=None, game=None):
        """records: the record names whose open items are wanted (None: all); csv_path: the open list (default branches-missed.csv)."""
        exe = ((Path(game) if game else GAME) / "DARKLAND.EXE").read_bytes()
        self.exe, self.rt = exe, Rtlink(exe)
        self.funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", self.rt)}
        self.opens = collections.defaultdict(set)
        for r in csv.DictReader(open(csv_path or REPO / "docs" / "faithfulness" / "branches-missed.csv", encoding="utf-8")):
            if r["status"] == "open" and (records is None or r["record"] in records):
                self.opens[r["function"]].add((r["payload"], r["kind"]))

    def reached(self, ins, edg):
        """{(function, payload, kind)} of the open items the merged coverage (record -> set of payloads / of (from, to)) reaches."""
        out = set()
        for name, items in self.opens.items():
            fn = self.funcs.get(name)
            if fn is None:
                continue
            rec = fn.record
            lo = fn.payload(fn.linear)
            hi = lo + fn.size
            base = fn.linear - lo
            src = ins.get(rec, ())
            ex = set(src.in_range(lo, hi)) if hasattr(src, "in_range") else {p for p in src if lo <= p < hi}
            if not ex:
                continue
            g0 = cfgmod.build(self.exe, self.rt, fn)
            extra = [p + base for p in ex if (p + base) not in g0.insns]
            g = cfgmod.build(self.exe, self.rt, fn, extra) if extra else g0
            wanted = {}
            for b in g.blocks:
                wanted[("%04X" % (b - base), "block")] = (b - base, None)
            for j, t, f, m in g.branches:
                for kind, dst in (("taken", t), ("fallthrough", f)):
                    if t == f and kind == "fallthrough":
                        continue
                    wanted[("%04X" % (j - base), kind)] = (j - base, dst - base)
            for it in items:
                w = wanted.get(it)
                if w and ((w[0] in ins.get(rec, ())) if it[1] == "block" else ((w[0], w[1]) in edg.get(rec, ()))):
                    out.add((name,) + it)
        return out


def merged(specs):
    ins, edg = collections.defaultdict(set), collections.defaultdict(set)
    runs = 0
    only = set(os.environ["BATTLE_CALLS_STATES"].split(",")) if os.environ.get("BATTLE_CALLS_STATES") else None      # a subset of the states, to see the growth
    for spec in specs:
        folder, _, excluded = spec.partition("::")
        skip = set(Path(excluded).read_text(encoding="utf-8").split()) if excluded and os.path.exists(excluded) else set()
        for j in lines(folder):
            if "%s|%s" % (j.get("unit"), j.get("state")) in skip or (only is not None and str(j.get("state")).split("#")[0] not in only):
                continue
            runs += 1
            r = restrict(j)
            for k, v in r["insns"].items():
                ins[k] |= set(v)
            for k, v in r["edges"].items():
                edg[k] |= {tuple(e) for e in v}
    return ins, edg, runs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("what", choices=["baseline", "measure"])
    ap.add_argument("rest", nargs="*")
    ap.add_argument("--local", default=os.environ.get("DARKLANDS_LOCAL"))
    a = ap.parse_args()
    local = Path(a.local)
    if a.what == "baseline":
        baseline(local)
        return
    out, specs = a.rest[0], a.rest[1:]
    items = Items()
    total = sum(len(v) for v in items.opens.values())
    base_i, base_e = pickle.load(open(local / "batch" / "battlecalls" / "baseline.pkl", "rb"))
    already = items.reached({k: set(v) for k, v in base_i.items()}, {k: {tuple(e) for e in v} for k, v in base_e.items()})
    ins, edg, runs = merged(specs)
    got = items.reached(ins, edg) - already
    per = collections.Counter(g[0] for g in got)
    print("open items of the battle records: %d (the baseline reaches %d of them); %d runs reach %d more, in %d functions" % (total, len(already), runs, len(got), len(per)))
    for name, n in per.most_common(40):
        print("  %-14s %4d  of %d open" % (name, n, len(items.opens[name])))
    json.dump(sorted("|".join(g) for g in got), open(out, "w"))


if __name__ == "__main__":
    main()
