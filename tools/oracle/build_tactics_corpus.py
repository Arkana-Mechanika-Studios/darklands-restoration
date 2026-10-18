"""Original-byte corpus for the tactical engine's geometry (R014): cbt_collisions (1462:000A),
line_blocked (1D69:000C) and distance3 (root 047C:1120).

usage: python -I build_tactics_corpus.py <DARKLAND.EXE> <out tactics.json>

Each case runs the original function from the unchanged EXE (sections 136 data + 14 code) on a
generated floor (cells around the tested spot in far 20A5:0000), generated furniture shapes and
combatants. Only inputs and results are stored; no game bytes.
"""
from __future__ import annotations

import hashlib
import json
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import run  # noqa: E402

DS = 0x321A
FLOOR_SEG = 0x20A5
COMBATANTS = 0x9C55
CBT_FLOOR, HALF_W, HALF_H, MOVING = 0xA6B4, 0xC80F, 0xC831, 0xA6D3
FURN_H, FURN_W, FURN_X, FURN_Y, SIGHT = 0xC11D, 0xC12F, 0xD7BE, 0xD802, 0xC1CF
STRICT, SEARCHING, SEARCH_TARGET, NO_SCENERY, NO_PEOPLE = 0xE0A2, 0xD697, 0xD6CD, 0xE13C, 0xC600
LAST, PARTS = 0xD708, 0xE11C
LOS_X0, LOS_Y0, LOS_X1, LOS_Y1 = 0xE0A5, 0xE0A3, 0xE0A9, 0xE0A7
FLOOR_SHOWN = 0x11CA


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def ds(offset: int, data: bytes) -> dict:
    return {"offset": "%04X" % offset, "hex": data.hex()}


def wall(r) -> int:
    return r.choice([0, 0, 0, 0, 1, 2, 5, 11, 12, 13, 15])


def cell(r, furnitures: int) -> int:
    v = wall(r) | wall(r) << 8 | wall(r) << 16 | wall(r) << 24
    if r.random() < 0.35:
        v |= r.randint(1, furnitures) << 4 | r.randint(0, 3) << 28
    v |= r.choice([0, 0, 0, r.randint(0, 15)]) << 12 | r.choice([0, 0, r.randint(0, 15)]) << 20
    if r.random() < 0.06:
        v |= 0x80000000
    return v


def scene(r, centre):
    """Cells of the 7 x 7 block around the centre cell, furniture shapes, combatants near the centre."""
    cx, cy = centre
    cells = {}
    for x in range(cx - 3, cx + 4):
        for y in range(cy - 3, cy + 4):
            cells[(x, y)] = cell(r, 15)
    shapes = [(r.randint(2, 40), r.randint(2, 24), [r.randint(0, 31) for _ in range(4)], [r.randint(0, 15) for _ in range(4)])
              for _ in range(16)]
    recs = []
    for k in range(13):
        rec = bytearray(0x80)
        struct.pack_into("<h", rec, 4, cx * 32 + r.randint(-40, 72))
        struct.pack_into("<h", rec, 6, cy * 16 + r.randint(-20, 36))
        rec[0x14] = r.choice([1, 1, 1, 0, 2, 3])
        rec[0x1C] = k
        rec[0x59] = r.choice([0, 20, 45, 89, 90, 120, r.randint(0, 100)])
        rec[0x6F] = r.choice([0, 30, 95, r.randint(0, 100)])
        recs.append(rec)
    floors = bytes(r.choice([0, 0, 0, 1]) for _ in range(13))
    half_w = bytes(r.choice([5, 6, 5, 10, r.randint(2, 12)]) for _ in range(13))
    half_h = bytes(r.choice([2, 3, 2, 4, r.randint(1, 6)]) for _ in range(13))
    moving = bytes(r.choice([0, 0, 1]) for _ in range(13))
    return cells, shapes, recs, floors, half_w, half_h, moving


def memory_of(cells, shapes, recs, floors, half_w, half_h, moving, sight):
    mem = []
    for (x, y), v in cells.items():
        mem.append({"segment": "%04X" % FLOOR_SEG, "offset": "%04X" % ((x * 40 + y) * 4), "hex": struct.pack("<I", v).hex()})
    mem.append(ds(FURN_H, bytes(s[1] for s in shapes)))
    mem.append(ds(FURN_W, bytes(s[0] for s in shapes)))
    mem.append(ds(FURN_X, bytes(b for s in shapes for b in s[2])))
    mem.append(ds(FURN_Y, bytes(b for s in shapes for b in s[3])))
    mem.append(ds(SIGHT, bytes([sight])))
    mem += [ds(COMBATANTS + 0x80 * k, rec) for k, rec in enumerate(recs)]
    mem += [ds(CBT_FLOOR, floors), ds(HALF_W, half_w), ds(HALF_H, half_h), ds(MOVING, moving), ds(FLOOR_SHOWN, b"\x00")]
    return mem


def inputs_of(cells, shapes, recs, floors, half_w, half_h, moving, sight):
    return {"cells": [[x, y, v] for (x, y), v in cells.items()], "shapes": [[s[0], s[1], s[2], s[3]] for s in shapes],
            "combatants": [rec.hex() for rec in recs], "floors": floors.hex(), "halfW": half_w.hex(), "halfH": half_h.hex(),
            "moving": moving.hex(), "sight": sight}


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    out = []
    images = None
    for n in range(500):
        centre = (r.randint(4, 35), r.randint(4, 35))
        sc = scene(r, centre)
        sight = r.choice([1, 4, 8, 12, 16])
        mem = memory_of(*sc, sight)
        inputs = inputs_of(*sc, sight)
        k = r.randint(0, 12)
        strict, searching, target, scenery, people = r.choice([0, 1]), r.choice([0, 0, 1]), r.choice([0xFF, r.randint(0, 12)]), \
            r.choice([0, 0, 0, 1]), r.choice([0, 0, 0, 1])
        recs = sc[2]
        rec = recs[k]
        x = centre[0] * 32 + r.randint(0, 31)
        y = centre[1] * 16 + r.randint(0, 15)
        struct.pack_into("<h", rec, 4, x)
        struct.pack_into("<h", rec, 6, y)
        mem.append(ds(COMBATANTS + 0x80 * k, rec))
        mem += [ds(STRICT, bytes([strict])), ds(SEARCHING, bytes([searching])), ds(SEARCH_TARGET, bytes([target])),
                ds(NO_SCENERY, bytes([scenery])), ds(NO_PEOPLE, bytes([people])),
                ds(PARTS, bytes([0xA5]) * 32), ds(LAST, bytes([0xA5, 0xA5]))]
        inputs.update({"who": k, "x": x, "y": y, "strict": strict, "searching": searching, "target": target if target != 0xFF else -1,
                       "noScenery": scenery, "noPeople": people})
        inputs["combatants"] = [q.hex() for q in recs]
        res = run(exe, {"unit": "cbt_collisions", "entry": {"segment": "1462", "offset": "000A"}, "sections": [136, 14],
                        "ds": DS, "memory": mem, "args": [h16(COMBATANTS + 0x80 * k)], "follow": True})
        images = res["loadedImages"]
        changed = {int(c["offset"], 16): int(c["after"], 16) for c in res["dsChanged"]}

        def word(off):
            return changed.get(off, 0xA5) | changed.get(off + 1, 0xA5) << 8

        out.append({"function": "cbt_collisions", "inputs": inputs, "ax": res["final"]["ax"],
                    "parts": [word(PARTS + 2 * p) for p in range(16)], "last": word(LAST)})
    for n in range(400):
        centre = (r.randint(5, 34), r.randint(5, 34))
        sc = scene(r, centre)
        sight = r.choice([1, 4, 8, 12, 16])
        mem = memory_of(*sc, sight)
        inputs = inputs_of(*sc, sight)
        x0 = centre[0] * 32 + r.randint(-60, 90)
        y0 = centre[1] * 16 + r.randint(-30, 45)
        x1 = centre[0] * 32 + r.randint(-60, 90)
        y1 = centre[1] * 16 + r.randint(-30, 45)
        a = r.choice([-1, -1, -2, r.randint(0, 12)])
        b = r.choice([-1, r.randint(0, 12)])
        mode = r.choice([0, 0x80, 0x100])
        mem += [ds(LOS_X0, struct.pack("<h", x0)), ds(LOS_Y0, struct.pack("<h", y0)), ds(LOS_X1, struct.pack("<h", x1)),
                ds(LOS_Y1, struct.pack("<h", y1))]
        inputs.update({"from": [x0, y0], "to": [x1, y1], "a": a, "b": b, "mode": mode})
        res = run(exe, {"unit": "line_blocked", "entry": {"segment": "1D69", "offset": "000C"}, "sections": [136, 14],
                        "ds": DS, "memory": mem, "args": [h16(a), h16(b), h16(mode)], "follow": True})
        out.append({"function": "line_blocked", "inputs": inputs, "ax": res["final"]["ax"]})
    for n in range(300):
        a = r.choice([0, 1, -1, 32767, -32768, r.randint(-3000, 3000), r.randint(-200, 200)])
        b = r.choice([0, 1, -1, 32767, -32768, r.randint(-3000, 3000), r.randint(-200, 200)])
        res = run(exe, {"unit": "distance3", "entry": {"segment": "047C", "offset": "1120"}, "sections": [136],
                        "ds": DS, "memory": [], "args": [h16(a), h16(b), "0000"], "follow": True})
        out.append({"function": "distance3", "inputs": {"a": a, "b": b}, "ax": res["final"]["ax"]})
    corpus = {"authority": "test_only_original_byte_execution", "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "loadedImages": images, "cases": out}
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(out))


if __name__ == "__main__":
    main()
