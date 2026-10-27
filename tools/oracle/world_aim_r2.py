"""R2: aimed direct calls of the travel map's functions from map states (jobs in world_fuzz.py's output layout, so world_calls.py export and the
WorldCalls test take them): the cell buffer is filled by the original's own R030+0x20D2 for a view (map_draw.py's setup), the marker DS:2866/2868 is
put where a case needs it, the function is called, and only that call's coverage is kept.

usage: python -I -B world_aim_r2.py camp <states folder> <out folder> --states pressburg-map-map[,...] [--cap 150] [--seed 1]

camp  R030+0x25BE (camp watch): one job per signature (the terrain code under the marker, the first settlement icon the 10 x 10 scan finds and
      whether the scan wraps into the next row or past the buffer) over a grid of views of the whole map.
"""
from __future__ import annotations

import argparse
import base64
import gzip
import json
import random
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import map_draw as md  # noqa: E402
import world_calls as wc  # noqa: E402
import battle_calls as bc  # noqa: E402
from dosmachine import DS, Stop  # noqa: E402

CAMP = "R030+0x25BE"
CTX = [0x2866, 0x2868, 0x286A, 0x286C, 0x286E]


def b64(data: bytes) -> str:
    return base64.b64encode(gzip.compress(data, 6)).decode()


def camp_signature(g: bytes, x: int, y: int, st) -> tuple | None:
    """(terrain under the marker, first settlement (i, j) of the scan or None, wrapped) for the marker, or None when the scan reads past the 70 bytes."""
    w = lambda o: st.m.word(DS, o)
    row = (y + 11) // 4
    odd = (row + w(0x286E) + w(0x286C) - 1) % 2
    col = (x + (16 if odd else 24)) // 16
    if (row + 9) * 19 + col + 9 >= len(g):
        return None
    for i in range(10):
        for j in range(10):
            if g[(row + j) * 19 + col + i] == 29:
                return g[row * 19 + col], (i, j), col + i > 18 or row + j > 49
    return g[row * 19 + col], None, False


def camp(states: Path, out: Path, names, cap: int, seed: int):
    rng = random.Random(seed)
    folder = out / CAMP.replace("+", "_")
    (folder / "cov").mkdir(parents=True, exist_ok=True)
    for name in names:
        st = wc.WState(states / (name + ".state"), coverage=True, near=[])
        rec = md.Recorder(st)
        base = md.view_regs(st)
        sav = st.sav
        st.restore()
        random0 = bc.u32(st.m.read(DS, bc.HOLDRAND, 4))
        views = [(base["column"], base["rows"], base["top"])]
        for top in range(0, base["height"] - 0x83, 0x41):
            for column in range(1, base["width"] - 0x11, 24):
                views.append((column, 0x29, top))
        seen, kept, t0 = set(), [], time.time()
        cpu0 = time.process_time()
        for view in views:
            if len(kept) >= cap:
                break
            try:
                md.setup(st, rec, *view)
            except Stop:
                continue
            g = md.cells(st) + bytes(md.after(st))
            fresh = {}
            for y in range(0x14, 0xAB):
                for x in range(0x14, 0xE3):
                    sig = camp_signature(g, x, y, st)
                    if sig is not None and sig not in seen and sig not in fresh:
                        fresh[sig] = (x, y)
            picks = rng.sample(sorted(fresh.items(), key=str), min(len(fresh), 6))
            for sig, (x, y) in picks:
                md.setup(st, rec, *view)
                m = st.m
                m.put_word(DS, 0x2866, x)
                m.put_word(DS, 0x2868, y)
                m.cov_insns, m.cov_edges, m._last_block_jcc = {}, set(), None
                ds0 = {a: m.word(DS, a) for a in CTX}
                r = st._call_plain(CAMP, [], None, False)
                if r["error"]:
                    continue
                seen.add(sig)
                kept.append({"args": [], "derived": True, "ops": [], "ax": r["ax"], "dx": r["dx"], "random": r["random"], "random0": random0, "draws": r["draws"],
                             "seeds": [], "sav_before": b64(sav), "sav_after": None, "ds": ds0, "changed": {}, "seconds": r["seconds"],
                             "instructions": r["instructions"], "coverage": r["coverage"], "iteration": len(kept), "novel": True, "sig": str(sig), "view": view})
        elapsed, cpu = time.time() - t0, time.process_time() - cpu0
        (folder / (name + ".json")).write_text(json.dumps({"state": name, "function": CAMP, "kept": kept, "seconds": elapsed, "cpu": cpu, "iterations": len(kept)}), encoding="utf-8")
        with open(folder / "cov" / ("cov-%s.jsonl" % name), "w", encoding="utf-8") as f:
            for item in kept:
                f.write(json.dumps({"unit": CAMP, "state": "%s#%d" % (name, item["iteration"]), "seconds": item["seconds"], **item["coverage"]}) + "\n")
        print(name, "kept", len(kept), "signatures", "%.0f s wall, %.0f s cpu" % (elapsed, cpu), flush=True)
        st.m = None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("what", choices=["camp"])
    ap.add_argument("states")
    ap.add_argument("out")
    ap.add_argument("--states", dest="names", required=True)
    ap.add_argument("--cap", type=int, default=150)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    if a.what == "camp":
        camp(Path(a.states), Path(a.out), a.names.split(","), a.cap, a.seed)


if __name__ == "__main__":
    main()
