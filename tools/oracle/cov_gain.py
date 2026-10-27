"""What new coverage folders reach of the open items that a baseline does not (memory-safe).

usage: python -I -B cov_gain.py --baseline <dir | branches-work> [--baseline ...] --new <cov dir> [--new ...]
                                [--exclude <file of 'unit|state' lines>] [--open docs/faithfulness/branches-missed.csv]
                                [--out gained.json] [--exe DARKLAND.EXE] [--max-rss-gb 3] [--min-free-gb 2]

Every cov-*.jsonl below the given folders (searched recursively; 'branches-work' is local/faithfulness/branches-work of the
main checkout, or $DARKLANDS_LOCAL) is streamed line by line and folded straight into ONE merged coverage for the baseline
and one for the new folders: per record a bytearray over the payload offsets (executed instructions) and a set of packed
(from, to) edges. Nothing per run is kept, so memory stays at the size of the union (a few tens of MB), not of the corpus.
Only the records that have open items are folded. Runs whose 'unit|state' line is in the --exclude file (the runs the rewrite
did not agree with) are skipped in the new folders and in the baseline alike.

The open items (status open in branches-missed.csv: block / taken / fallthrough) are mapped to executed instructions and
branch edges by battle_calls_measure.Items.reached (the same cfg.py construction as block_coverage.py, dynamic leaders
included); the result is the open items the new folders reach and the baseline does not, per record and function.

Guards: refuses to start with less than --min-free-gb of free memory and aborts when this process passes --max-rss-gb.
"""
from __future__ import annotations

import argparse
import collections
import ctypes
import glob
import json
import os
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import battle_calls_measure as bcm  # noqa: E402

REPO = bcm.REPO
CHECK_EVERY = 200   # lines between RSS checks


# ---- memory guard -----------------------------------------------------------------------------------------------
def free_bytes():
    try:
        import psutil
        return psutil.virtual_memory().available
    except ImportError:
        pass

    class MS(ctypes.Structure):
        _fields_ = [("l", ctypes.c_ulong), ("load", ctypes.c_ulong)] + [(n, ctypes.c_ulonglong) for n in
                    ("tp", "ap", "tpf", "apf", "tv", "av", "aev")]
    m = MS()
    m.l = ctypes.sizeof(MS)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    return m.ap


def rss_bytes():
    try:
        import psutil
        return psutil.Process().memory_info().rss
    except ImportError:
        pass

    class PMC(ctypes.Structure):
        _fields_ = [("cb", ctypes.c_ulong), ("pf", ctypes.c_ulong)] + [(n, ctypes.c_size_t) for n in
                    ("peak_ws", "ws", "qpp", "qp", "qnpp", "qnp", "pagefile", "peak_pagefile")]
    k = ctypes.windll.kernel32
    k.GetCurrentProcess.restype = ctypes.c_void_p
    p = PMC()
    p.cb = ctypes.sizeof(PMC)
    ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.c_void_p(k.GetCurrentProcess()), ctypes.byref(p), p.cb)
    return p.ws


class Guard:
    def __init__(self, max_rss):
        self.max_rss, self.peak = max_rss, 0

    def check(self, where=""):
        r = rss_bytes()
        self.peak = max(self.peak, r)
        if r > self.max_rss:
            sys.exit("cov_gain: aborting, memory %.2f GB passed the %.2f GB limit (%s); raise --max-rss-gb only if you know why"
                     % (r / 2**30, self.max_rss / 2**30, where))


# ---- compact merged coverage --------------------------------------------------------------------------------------
class Bits:
    """Executed payload offsets of one record: a byte per offset."""

    def __init__(self):
        self.b = bytearray()

    def add_all(self, pays):
        m = max(pays) + 1 if pays else 0
        if m > len(self.b):
            self.b.extend(bytes(m - len(self.b)))
        b = self.b
        for p in pays:
            b[p] = 1

    def __contains__(self, p):
        return 0 <= p < len(self.b) and self.b[p] == 1

    def in_range(self, lo, hi):
        b = self.b
        return [lo + i for i, v in enumerate(b[lo:hi]) if v]

    def __len__(self):
        return self.b.count(1)

    def or_with(self, other):
        n = len(other.b)
        if n > len(self.b):
            self.b.extend(bytes(n - len(self.b)))
        b, o = self.b, other.b
        for i in [i for i, v in enumerate(o) if v]:
            b[i] = 1


class Edges:
    def __init__(self):
        self.s = set()

    def __contains__(self, e):
        return ((e[0] << 32) | e[1]) in self.s


class Merged:
    """Union of the runs read: record -> Bits / Edges; .get(record) is what Items.reached expects."""

    def __init__(self, records):
        self.records = records
        self.ins = {}
        self.edg = {}
        self.runs = self.skipped = 0

    def fold(self, j):
        recs = self.records
        for r, pays in j["insns"].items():
            if r in recs and pays:
                self.ins.setdefault(r, Bits()).add_all(pays)
        for r, es in j["edges"].items():
            if r in recs and es:
                self.edg.setdefault(r, Edges()).s.update([(e[0] << 32) | e[1] for e in es])
        self.runs += 1

    def count(self):
        return sum(len(v) for v in self.ins.values()), sum(len(v.s) for v in self.edg.values())


class View:
    def __init__(self, d):
        self.d = d

    def get(self, rec, default=()):
        return self.d.get(rec, default)


def resolve(spec, local):
    if spec == "branches-work":
        return str(local / "faithfulness" / "branches-work")
    return spec


def read_exclude(path):
    out = set()
    if path:
        for line in Path(path).read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line:
                out.add(line)
    return out


def stream(folders, merged, exclude, guard, label):
    files = 0
    for folder in folders:
        for f in sorted(glob.glob(os.path.join(folder, "**", "cov*.jsonl"), recursive=True)):
            files += 1
            n = 0
            with open(f, encoding="utf-8") as fh:
                for line in fh:
                    if not line.strip():
                        continue
                    n += 1
                    if n % CHECK_EVERY == 0:
                        guard.check("%s %s line %d" % (label, f, n))
                    j = json.loads(line)
                    if exclude:
                        u, s = j.get("unit"), j.get("state")
                        keys = ("%s|%s" % (u, s),) if s is not None else ("%s|None" % u, "%s|" % u, "%s" % u)
                        if any(k in exclude for k in keys):
                            merged.skipped += 1
                            continue
                    merged.fold(j)
            guard.check(f)
    return files


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--baseline", action="append", default=[], help="cov folder (searched recursively) or 'branches-work'")
    ap.add_argument("--new", action="append", default=[], help="cov folder (searched recursively)")
    ap.add_argument("--baseline-cache", help="pickle of the merged coverage of the --baseline folders (written when missing, read when there): the "
                    "corpus is streamed once for several measures; extra folders for one measure go in --extra-baseline")
    ap.add_argument("--extra-baseline", action="append", default=[], help="cov folder streamed on top of the cached baseline (not cached)")
    ap.add_argument("--exclude", help="file of 'unit|state' lines: runs to leave out")
    ap.add_argument("--open", default=str(REPO / "docs" / "faithfulness" / "branches-missed.csv"))
    ap.add_argument("--out", help="write the gained items (function|payload|kind) as JSON")
    ap.add_argument("--exe", help="DARKLAND.EXE (default: the project's game folder)")
    ap.add_argument("--local", default=os.environ.get("DARKLANDS_LOCAL") or str(REPO.parent / "code" / "local"))
    ap.add_argument("--max-rss-gb", type=float, default=3.0)
    ap.add_argument("--min-free-gb", type=float, default=2.0)
    a = ap.parse_args()
    if not a.new:
        sys.exit("cov_gain: give at least one --new folder")
    free = free_bytes()
    if free < a.min_free_gb * 2**30:
        sys.exit("cov_gain: only %.2f GB of memory free, need %.2f GB; close something first" % (free / 2**30, a.min_free_gb))
    guard = Guard(int(a.max_rss_gb * 2**30))
    t0 = time.time()
    local = Path(a.local)
    items = bcm.Items(records=None, csv_path=a.open, game=Path(a.exe).parent if a.exe else None)
    records = {items.funcs[n].record for n in items.opens if n in items.funcs}
    exclude = read_exclude(a.exclude)
    base, new = Merged(records), Merged(records)
    if a.baseline_cache and Path(a.baseline_cache).exists():
        import pickle
        with open(a.baseline_cache, "rb") as fh:
            base = pickle.load(fh)
        bf = 0
        if base.records != records:
            sys.exit("cov_gain: the cached baseline was made for other records; delete %s" % a.baseline_cache)
    else:
        bf = stream([resolve(b, local) for b in a.baseline], base, exclude, guard, "baseline")
        if a.baseline_cache:
            import pickle
            with open(a.baseline_cache, "wb") as fh:
                pickle.dump(base, fh, protocol=4)
    if a.extra_baseline:
        bf += stream([resolve(b, local) for b in a.extra_baseline], base, exclude, guard, "baseline+")
    print("baseline: %d files, %d runs (%d excluded), %d instructions, %d edges, %.0fs" % ((bf, base.runs, base.skipped) + base.count() + (time.time() - t0,)), flush=True)
    nf = stream([resolve(n, local) for n in a.new], new, exclude, guard, "new")
    print("new:      %d files, %d runs (%d excluded), %d instructions, %d edges" % ((nf, new.runs, new.skipped) + new.count()), flush=True)

    # the new folders reach an item that the baseline does not
    already = items.reached(View(base.ins), View(base.edg))
    # the union is what block_coverage maps (the dynamic leaders of both)
    union_i = {r: Bits() for r in set(base.ins) | set(new.ins)}
    for r in union_i:
        for src in (base.ins.get(r), new.ins.get(r)):
            if src:
                union_i[r].or_with(src)
    union_e = {}
    for r in set(base.edg) | set(new.edg):
        union_e[r] = Edges()
        for src in (base.edg.get(r), new.edg.get(r)):
            if src:
                union_e[r].s |= src.s
    reached_all = items.reached(View(union_i), View(union_e))
    got = reached_all - already
    guard.check("mapping")
    total = sum(len(v) for v in items.opens.values())
    per = collections.Counter(g[0] for g in got)
    per_rec = collections.Counter(items.funcs[g[0]].record for g in got)
    print("open items: %d; the baseline reaches %d of them; the new folders add %d, in %d functions / %d records"
          % (total, len(already), len(got), len(per), len(per_rec)))
    print("by record: " + " ".join("%s:%d" % kv for kv in sorted(per_rec.items())))
    for name, n in per.most_common(60):
        print("  %-14s %4d  of %d open" % (name, n, len(items.opens[name])))
    print("runs read: baseline %d, new %d; peak memory %.0f MB; %.0fs" % (base.runs, new.runs, guard.peak / 2**20, time.time() - t0))
    if a.out:
        json.dump(sorted("|".join(g) for g in got), open(a.out, "w"))


if __name__ == "__main__":
    main()
