"""Original-byte corpus for the path search (R014 12F7: route-search with its trackers, waypoint
pruning, the AI's path hook and the shortcut pass).

usage: python -I build_path_corpus.py <DARKLAND.EXE> <out path.json>

Each case runs the original route-search from the unchanged EXE (sections 136 data + 14 code) on a
generated floor and generated combatants and step tables. The work-area offsets are those
level-read (R020 05E6) sets. Only inputs and results are stored; no game bytes.
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
FIGHTER_FLOOR_LEVEL, HALF_W, HALF_H, MOVING = 0xA6B4, 0xC80F, 0xC831, 0xA6D3
FURN_H, FURN_W, FURN_X, FURN_Y = 0xC11D, 0xC12F, 0xD7BE, 0xD802
STEP_X, STEP_Y = 0xC681, 0xC6F4
WORK_SEG = 0x9000
LENGTH, WAYPOINTS = 0xE118, 0xE11A
PATH = 0x71BA
NDOORS = 0x1F52
# the busy-cursor calls (12F7:03B6 on, 03D2 off) are the screen's: stubbed
# and so are the EMS page mappings of the work area (root 05F8:0117), which is plain memory here
CALLSITES = {"12ff5": "busy_on", "13369": "busy_on", "138f4": "busy_off", "132a6": "ems-page-map", "13339": "ems-page-map",
             "13350": "ems-page-map"}
# the work-area layout level-read (R020 1EF8:05E6) sets (offsets in the work segment)
WORK = {0xE0EA: 0, 0xE0EC: 0x9C4, 0xE0EE: 0x9CE, 0xE0F0: 0x9D8, 0xE0F2: 0x9E2, 0xE0F4: 0x9F6, 0xE0F6: 0xA0A, 0xE0F8: 0xA1E,
        0xE0FA: 0xA32, 0xE0FC: 0xA46, 0xE0FE: 0xA5A, 0xE100: 0xA64, 0xE102: 0xA6E, 0xE104: 0xD8E, 0xE106: 0x10AE, 0xE108: 0x13CE,
        0xE10A: 0x13D8, 0xE10C: 0x14D2, 0xE10E: 0x1522, 0xE110: 0x1572, 0xE112: 0x15C2, 0xE114: 0x1752, 0xD6F7: WORK_SEG,
        0xD6F5: WORK_SEG}


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def ds(offset: int, data: bytes) -> dict:
    return {"offset": "%04X" % offset, "hex": data.hex()}


def wall(r) -> int:
    return r.choice([0] * 12 + [1, 2, 5, 11, 12, 15])


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261018)
    out = []
    images = None
    base_x = [-1, 0, 3, 2, 1, 0, -3, -2, 1, -1]
    base_y = [-1, -1, 0, 1, 1, 1, 0, -1, -1, 1]
    for n in range(int(sys.argv[3]) if len(sys.argv) > 3 else 60):
        cx, cy = r.randint(8, 31), r.randint(8, 31)
        cells = {}
        for x in range(cx - 7, cx + 8):
            for y in range(cy - 7, cy + 8):
                v = wall(r) | wall(r) << 8 | wall(r) << 16 | wall(r) << 24
                if r.random() < 0.08:
                    v |= r.randint(1, 15) << 4 | r.randint(0, 3) << 28
                if r.random() < 0.03 or abs(x - cx) == 7 or abs(y - cy) == 7:
                    v |= 0x80000000
                cells[(x, y)] = v
        shapes = [(r.randint(4, 30), r.randint(2, 14), [r.randint(4, 28) for _ in range(4)], [r.randint(2, 14) for _ in range(4)])
                  for _ in range(16)]
        scale = r.choice([1, 1, 1, 2, 4])
        steps_x = [v * scale for v in base_x]
        steps_y = [v * scale for v in base_y]
        who = r.choice([0, 1, 2, 3, 4, r.randint(5, 12)])
        recs, floors, half_w, half_h, moving = [], bytearray(13), bytearray(13), bytearray(13), bytearray(13)
        for k in range(13):
            rec = bytearray(0x80)
            struct.pack_into("<h", rec, 4, cx * 32 + r.randint(-150, 150))
            struct.pack_into("<h", rec, 6, cy * 16 + r.randint(-80, 80))
            rec[0x14] = r.choice([1, 1, 0, 2])
            rec[0x16] = k if k < 5 else r.choice([5, 6])
            rec[0x18] = 5
            rec[0x19] = r.randint(0, 7)
            rec[0x1C] = k
            rec[0x4A] = 0xFF
            recs.append(rec)
            floors[k] = r.choice([0, 0, 0, 0, 1])
            half_w[k] = r.choice([5, 5, 6, 10])
            half_h[k] = r.choice([2, 2, 3, 4])
            moving[k] = r.choice([0, 0, 1])
        me = recs[who]
        me[0x14] = 1
        floors[who] = 0
        struct.pack_into("<h", me, 4, cx * 32 + r.randint(4, 28))
        struct.pack_into("<h", me, 6, cy * 16 + r.randint(4, 12))
        target = r.choice([-1, -1, r.randint(0, 12)])
        if target == who:
            target = -1
        if target >= 0:
            tx, ty = struct.unpack_from("<hh", recs[target], 4)
            me[0x4A] = target
        else:
            tx = cx * 32 + r.randint(-170, 170)
            ty = cy * 16 + r.randint(-90, 90)
        struct.pack_into("<h", me, 8, tx)
        struct.pack_into("<h", me, 0x0A, ty)
        mem = [{"segment": "%04X" % FLOOR_SEG, "offset": "%04X" % ((x * 40 + y) * 4), "hex": struct.pack("<I", v).hex()}
               for (x, y), v in cells.items()]
        mem.append({"segment": "%04X" % FLOOR_SEG, "offset": "%04X" % NDOORS, "hex": "0000"})
        mem.append({"segment": "%04X" % FLOOR_SEG, "offset": "%04X" % PATH, "hex": "ee" * 250})
        mem += [ds(FURN_H, bytes(s[1] for s in shapes)), ds(FURN_W, bytes(s[0] for s in shapes)),
                ds(FURN_X, bytes(b for s in shapes for b in s[2])), ds(FURN_Y, bytes(b for s in shapes for b in s[3]))]
        mem += [ds(COMBATANTS + 0x80 * k, rec) for k, rec in enumerate(recs)]
        mem += [ds(FIGHTER_FLOOR_LEVEL, bytes(floors)), ds(HALF_W, bytes(half_w)), ds(HALF_H, bytes(half_h)), ds(MOVING, bytes(moving))]
        sx = bytes(v & 0xFF for v in steps_x)
        sy = bytes(v & 0xFF for v in steps_y)
        mem += [ds(STEP_X + 10 * s, sx) for s in range(11)] + [ds(STEP_Y + 10 * s, sy) for s in range(11)]
        mem += [ds(off, struct.pack("<H", v)) for off, v in WORK.items()]
        mem += [ds(0xE0A2, b"\x00"), ds(0xE13C, b"\x00"), ds(0xC600, b"\x00"), ds(0xD697, b"\x00")]
        res = run(exe, {"unit": "route-search", "entry": {"segment": "12F7", "offset": "000A"}, "sections": [136, 14],
                        "ds": DS, "memory": mem, "args": [h16(who)], "follow": True, "track_all": True,
                        "callsites": CALLSITES, "stub": list(set(CALLSITES.values())),
                        "max_instructions": 30000000})
        images = res["loadedImages"]
        changed = {int(c["linear"], 16): int(c["after"], 16) for c in res["memChanged"]}

        def byte(linear, default=0):
            return changed.get(linear, default)

        length = byte(DS * 16 + LENGTH) | byte(DS * 16 + LENGTH + 1) << 8
        path = bytes(byte(FLOOR_SEG * 16 + PATH + k, 0xEE) for k in range(250))
        trackers = None
        if "--trackers" in sys.argv:
            # route-tracker-table alone (12F7:03EA) on the same scene, the searcher's record copied to a scratch spot
            scratch = 0xF800
            res2 = run(exe, {"unit": "route-tracker-table", "entry": {"segment": "12F7", "offset": "03EA"}, "sections": [136, 14],
                             "ds": DS, "memory": mem + [ds(scratch, bytes(me))], "args": [h16(scratch)], "follow": True,
                             "track_all": True, "callsites": CALLSITES, "stub": list(set(CALLSITES.values())),
                             "max_instructions": 30000000})
            ch2 = {int(c["linear"], 16): int(c["after"], 16) for c in res2["memChanged"]}
            e116 = ch2.get(DS * 16 + 0xE116, 0) | ch2.get(DS * 16 + 0xE117, 0) << 8
            winner = ch2.get(DS * 16 + 0x84E6, 0) | ch2.get(DS * 16 + 0x84E7, 0) << 8
            steps = bytes(ch2.get(WORK_SEG * 16 + (winner & 0xFF) * 250 + k, 0) for k in range(min(e116 + 1, 250))) if int(res2["final"]["ax"], 16) & 0x7FFF == 1 else b""
            trackers = {"ax": res2["final"]["ax"], "length": e116, "steps": steps.hex()}
        out.append({"function": "route-search", "ax": res["final"]["ax"], "length": length, "path": path.hex(), "trackers": trackers, "inputs": {
            "cells": [[x, y, v] for (x, y), v in cells.items()], "shapes": [[s[0], s[1], s[2], s[3]] for s in shapes],
            "combatants": [rec.hex() for rec in recs], "floors": bytes(floors).hex(), "halfW": bytes(half_w).hex(),
            "halfH": bytes(half_h).hex(), "moving": bytes(moving).hex(), "stepX": steps_x, "stepY": steps_y, "who": who}})
    corpus = {"authority": "test_only_original_byte_execution", "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "loadedImages": images, "cases": out}
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(out), "found", sum(1 for c in out if int(c["ax"], 16) == 1))


if __name__ == "__main__":
    main()
