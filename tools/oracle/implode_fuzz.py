"""Coverage-guided inputs for the battle file's compression library, the original's bytes (R003, frame 1016): implode (1016:04C6) on generated
data and parameters, explode (1016:0D07) on the compressed results and on damaged copies of them. A tester addition (it uses build_implode_corpus.py's
runner and changes no original-side module).

usage: python -I -B implode_fuzz.py <out folder> [--iterations 400] [--seed 1] [--baseline baseline.pkl] [--what implode,explode]

A run that reaches an instruction or branch outcome of the battle records that neither the baseline (battle_calls_measure.py baseline) nor an earlier run
reached is kept. <out>/jobs.json holds the kept inputs and what the original did (AX, the bytes written); <out>/cov/cov-implode.jsonl their coverage lines
(unit "implode" / "explode", state "n<iteration>"). The rewrite's side is tests/Darklands.Battles.Tests/BattleCalls/CompressionCallsTests.cs.
"""
from __future__ import annotations

import argparse
import json
import os
import pickle
import random
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_implode_corpus as bic  # noqa: E402
from run_unit import run  # noqa: E402
import struct  # noqa: E402

GAME = HERE.parent.parent.parent / "game" / "DARKLAND"
BAT = {"R002", "R003"} | {"R%03d" % i for i in range(14, 26)}
DICTS = [0x400, 0x800, 0x1000]
ODD_DICTS = [0, 1, 0x300, 0x401, 0x7FF, 0x801, 0xFFF, 0x1001, 0x2000, 0x8000, 0xFFFF]


def one(exe, spec_extra, entry, args, mem, read=True):
    spec = {"unit": entry[0], "entry": {"segment": "1016", "offset": entry[1]}, "sections": bic.SECTIONS, "ds": bic.DS, "memory": mem, "args": args,
            "follow": True, "coverage": True, **spec_extra,
            "read": [{"segment": "%04X" % bic.DS, "offset": "%04X" % bic.WRITTEN, "length": 2}, {"segment": "%04X" % bic.OUTPUT, "offset": "0000", "length": 0xFFF0}]}
    return run(exe, spec)


def implode(exe, data: bytes, ctype: int, dsize: int, stale: bytes):
    mem = bic.common_memory(data, stale, bic.IMPLODE_WORK)
    mem.append({"segment": "%04X" % bic.ARGS, "offset": "0000", "hex": struct.pack("<HH", ctype & 0xFFFF, dsize & 0xFFFF).hex()})
    w = bic.w
    args = [w(2), w(bic.ARGS), w(0), w(bic.ARGS), w(0), w(bic.WORK), w(bic.WRITER), "0E76", w(bic.READER), "0E76"]
    res = one(exe, {"max_instructions": 60_000_000}, ("implode", "04C6"), args, mem)
    return int(res["final"]["ax"], 16), bic.output_of(res), res


def explode(exe, packed: bytes):
    mem = bic.common_memory(packed, b"", 0)
    w = bic.w
    args = [w(0), w(bic.WORK), w(bic.WRITER), "0E76", w(bic.READER), "0E76"]
    res = one(exe, {"max_instructions": 30_000_000}, ("explode", "0D07"), args, mem)
    return int(res["final"]["ax"], 16), bic.output_of(res), res


class Seen:
    def __init__(self, baseline):
        self.i = {k: set(v) for k, v in baseline[0].items()}
        self.e = {k: {tuple(e) for e in v} for k, v in baseline[1].items()}

    def novelty(self, cov) -> int:
        n = 0
        for k, v in cov["insns"].items():
            if k in BAT:
                n += len(set(v) - self.i.setdefault(k, set()))
        for k, v in cov["edges"].items():
            if k in BAT:
                n += len({tuple(e) for e in v} - self.e.setdefault(k, set()))
        return n

    def absorb(self, cov):
        for k, v in cov["insns"].items():
            if k in BAT:
                self.i.setdefault(k, set()).update(v)
        for k, v in cov["edges"].items():
            if k in BAT:
                self.e.setdefault(k, set()).update(tuple(e) for e in v)


def shape(r: random.Random, kind: str, n: int) -> bytes:
    """Data shapes of build_implode_corpus plus ones aimed at the library's corners: matches at the dictionary's far end, lengths at the code boundaries,
    two-byte matches, runs ending at the buffer's end, repeats of the last bytes of a 0x1000 load."""
    if kind in ("random", "few", "runs", "text", "records", "sparse"):
        return bic.generate(r, kind, n)
    if kind == "far":          # a block repeated at a distance near the dictionary's size
        dist = r.choice([0x3FF, 0x400, 0x401, 0x7FF, 0x800, 0x801, 0xFFF, 0x1000, 0x1001, 0x300, 2, 1, 3])
        block = bytes(r.randrange(256) for _ in range(r.choice([2, 3, 4, 8, 20, 0x80, 0x207, 0x208, 0x20A])))
        base = bytearray(r.randrange(256) for _ in range(dist + len(block)))
        base[0:len(block)] = block
        base += block
        return bytes((base + bytes(n))[:max(n, len(base))])
    if kind == "short":        # two- and three-byte repeats at assorted distances
        out = bytearray(r.randrange(256) for _ in range(r.randint(8, 40)))
        while len(out) < n:
            d = r.choice([1, 2, 3, 5, 9, 17, 40, 100, 300, 700, 1000, 2000, 4000])
            d = min(d, len(out))
            out += out[-d:][:r.choice([2, 2, 3, 4, 5, 9, 10, 17, 18, 40, 100, 0x206, 0x207, 0x208])] + bytes([r.randrange(256)])
        return bytes(out[:n])
    if kind == "edge":         # a long run of one byte of exactly the lengths where the length code changes
        out = bytearray()
        while len(out) < n:
            out += bytes([r.randrange(256)]) * r.choice([2, 3, 4, 5, 6, 7, 8, 9, 10, 17, 18, 25, 26, 40, 41, 72, 73, 136, 137, 264, 265, 520, 521, 522, 523, 1000, 4096, 4097])
        return bytes(out[:n])
    if kind == "bytes":        # every byte value, in order, repeated (ASCII literal codes of all lengths)
        return bytes((i * r.choice([1, 1, 3, 7, 255]) + r.randrange(2)) & 0xFF for i in range(n))
    raise ValueError(kind)


KINDS = ["random", "few", "runs", "text", "records", "sparse", "far", "short", "edge", "bytes"]
SIZES = [0, 1, 2, 3, 4, 5, 9, 10, 31, 0x100, 0x1FF, 0x200, 0x20A, 0x400, 0x7FF, 0x800, 0x801, 0xFFF, 0x1000, 0x1001, 0x1100, 0x1FFF, 0x2000, 0x2001, 0x2400]


def mutate_packed(r: random.Random, packed: bytes) -> bytes:
    p = bytearray(packed)
    op = r.randrange(9)
    if op == 0 and p:
        for _ in range(r.randint(1, 3)):
            p[r.randrange(len(p))] ^= 1 << r.randrange(8)
    elif op == 1 and p:
        p = p[:r.randrange(len(p) + 1)]
    elif op == 2 and len(p) >= 2:
        p[0] = r.choice([0, 1, 2, 3, 0xFF, r.randrange(256)])
    elif op == 3 and len(p) >= 2:
        p[1] = r.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 0xFF, r.randrange(256)])
    elif op == 4 and p:
        p[r.randrange(len(p))] = r.randrange(256)
    elif op == 5 and p:
        i = r.randrange(len(p))
        p[i:i] = bytes(r.randrange(256) for _ in range(r.randint(1, 6)))
    elif op == 6 and p:
        i = r.randrange(len(p))
        del p[i:i + r.randint(1, 4)]
    elif op == 7 and p:
        p[-1:] = bytes([0xFF, 0xFF, 0xFF])
    else:
        p = bytearray(bytes(r.randrange(256) for _ in range(r.randint(0, 30))))
        if p:
            p[0] = r.randrange(2)
            if len(p) > 1:
                p[1] = r.choice([4, 5, 6])
    return bytes(p)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--iterations", type=int, default=400)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--baseline", default=os.environ.get("BATTLE_CALLS_BASELINE"))
    ap.add_argument("--what", default="implode,explode")
    ap.add_argument("--prior", action="append", default=[], help="an earlier output folder: its coverage counts as seen, its compressed streams seed the explode corpus")
    ap.add_argument("--time-limit", type=float, default=0, help="stop after this many wall seconds (0: none)")
    a = ap.parse_args()
    exe = (GAME / "DARKLAND.EXE").read_bytes()
    seen = Seen(pickle.load(open(a.baseline, "rb")))
    r = random.Random("implode|%d" % a.seed)
    prior_packed = []
    for folder in a.prior:
        for line in open(Path(folder) / "cov" / "cov-implode.jsonl", encoding="utf-8"):
            seen.absorb(json.loads(line))
        for job in json.loads((Path(folder) / "jobs.json").read_text(encoding="utf-8"))["jobs"]:
            if job.get("packed"):
                prior_packed.append(bytes.fromhex(job["packed"]))
    out = Path(a.out)
    (out / "cov").mkdir(parents=True, exist_ok=True)
    jobs, covlines = [], []
    corpus_packed = [b""] + prior_packed
    cpu0 = time.process_time()
    t0 = time.time()
    what = a.what.split(",")
    for it in range(a.iterations):
        if a.time_limit and time.time() - t0 > a.time_limit:
            break
        do_implode = "implode" in what and (r.random() < 0.45 or "explode" not in what or len(corpus_packed) < 3)
        try:
            if do_implode:
                kind, n = r.choice(KINDS), r.choice(SIZES)
                data = shape(r, kind, n)
                if r.random() < 0.2:
                    data = bytearray(data)
                    for _ in range(r.randint(1, 5)):
                        if data:
                            data[r.randrange(len(data))] = r.randrange(256)
                    data = bytes(data)
                ctype = r.choice([0, 0, 1, 1, 1, 2, 3, 0xFFFF]) if r.random() < 0.9 else r.randrange(0x10000)
                dsize = r.choice(DICTS) if r.random() < 0.9 else r.choice(ODD_DICTS)
                if r.random() < 0.8:
                    ctype = r.choice([0, 1])
                if r.random() < 0.85:
                    dsize = r.choice(DICTS)
                stale = bytes(r.randrange(256) for _ in range(r.choice([1, 7, 16]))) if r.random() < 0.6 else b"\0"
                if len(data) > 0x2600:
                    data = data[:0x2600]
                ax, packed, res = implode(exe, data, ctype, dsize, stale)
                entry = {"function": "implode", "type": ctype, "dictionary": dsize, "data": data.hex(), "stale": stale.hex(), "ax": ax, "packed": packed.hex(), "kind": kind}
            else:
                base = r.choice(corpus_packed)
                packed = mutate_packed(r, base) if (r.random() < 0.85 and base) else base
                ax, plain, res = explode(exe, packed)
                entry = {"function": "explode", "packed": packed.hex(), "ax": ax, "plain": plain.hex()}
        except Exception as e:      # a machine fault on odd bytes is not an input
            print("fault", it, type(e).__name__, str(e)[:80], flush=True)
            continue
        if res.get("error") or res["final"].get("error"):
            continue
        cov = res["coverage"]
        novel = seen.novelty(cov)
        if novel:
            seen.absorb(cov)
            entry["iteration"], entry["novel"] = it, novel
            entry["id"] = "%s|n%d" % (entry["function"], it)
            jobs.append(entry)
            covlines.append({"unit": entry["function"], "state": "n%d" % it, **cov})
            if entry["function"] == "implode" and entry["ax"] == 0 and entry["packed"]:
                corpus_packed.append(bytes.fromhex(entry["packed"]))
            elif entry["function"] == "explode":
                corpus_packed.append(bytes.fromhex(entry["packed"]))
            print(it, entry["function"], "+%d" % novel, "kept", len(jobs), flush=True)
        elif do_implode and entry["ax"] == 0 and entry["packed"] and r.random() < 0.1:
            corpus_packed.append(bytes.fromhex(entry["packed"]))
    (out / "jobs.json").write_text(json.dumps({"jobs": jobs, "cpu": time.process_time() - cpu0, "wall": time.time() - t0, "iterations": a.iterations}), encoding="utf-8")
    with open(out / "cov" / "cov-implode.jsonl", "w", encoding="utf-8") as f:
        for line in covlines:
            f.write(json.dumps(line) + "\n")
    print("done: kept %d of %d, %.0f s cpu" % (len(jobs), a.iterations, time.process_time() - cpu0))


if __name__ == "__main__":
    main()
