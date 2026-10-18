"""Original-byte corpus for the battle's effects (R019 and the R014 cloud tests): effect_spell
(1EF8:0000, with everything it calls: the cloud, flash and blast effects, the modifiers, the
worn armour, damage_queue/damage_apply, weapon_ready, consume_item), the area test
R014+0x469E, cloud_blocks_line (1EC9:0142) and the Throw list (1EF8:0C80).

usage: python -I build_effects_corpus.py <DARKLAND.EXE> <out effects.json>

Each case runs the original function from the unchanged EXE (sections 136 data, 14, 19) on a
generated floor (cells round the spot, furniture shapes), thirteen generated combatants with
their packs, modifiers and immunities, and generated effect lists. Every call is executed for
real except the sound (0E76:465E) and the party panel redraw (root 0000:1716), which are
stubbed. Only inputs and results are stored; no game bytes.
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
HOLDRAND = 0x7B20
COMBATANTS = 0x9C55
CBT_FLOOR, HALF_W, HALF_H = 0xA6B4, 0xC80F, 0xC831
FURN_H, FURN_W, FURN_X, FURN_Y, SIGHT = 0xC11D, 0xC12F, 0xD7BE, 0xD802, 0xC1CF
FLOOR_SHOWN, CLOCK, DELAYED = 0x11CA, 0x07D4, 0xD842
CLOUDS, CLOUD_COUNT = 0xA2EB, 0xA33D
AREAS, AREA_COUNT = 0xA342, 0xA395
MARKERS, MARKER_COUNT = 0xA396, 0xA531
MODS, IMMUNE, FLASH_UNTIL, ACTION_STATE = 0xA532, 0xC927, 0xDA89, 0xCA64
FLASH_SEG, FLASH_OFF = 0x20A5, 0x35EE
FLASH1, FLASH2 = 0xD4C3, 0xD4C5
ATTACK_RATE, QUEUED, SHOWN = 0xD493, 0xD70C, 0xD4C7
REAL_COMBAT, DEMO = 0xA6C1, 0xC21A
ANIM_FILE_TABLE, SCRATCH = 0x1174, 0xFD00
INV_SEG, PARTY_INV, ENEMY_INV = 0x2E38, 0x003E, 0x0DBE   # enemy i: R136+0xE50E[i] = 0x0DBE + (i - 5) * 0x60
MENU, MENU_STATE, MENU_NAMES = 0x9A47, 0x97DD, 0x114E
STUBS = {"0E76:465E": "sound", "0000:1716": "party_panel_rows"}
ARMOUR = [0xFF, 0x43, 0x44, 0x48, 0x4C, 0x50, 0x54]


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def ds(offset: int, data: bytes) -> dict:
    return {"offset": "%04X" % offset, "hex": data.hex()}


def far(seg: int, offset: int, data: bytes) -> dict:
    return {"segment": "%04X" % seg, "offset": "%04X" % offset, "hex": data.hex()}


def wall(r) -> int:
    return r.choice([0, 0, 0, 0, 0, 0, 1, 2, 5, 11, 12])


def cell(r) -> int:
    v = wall(r) | wall(r) << 8 | wall(r) << 16 | wall(r) << 24
    if r.random() < 0.15:
        v |= r.randint(1, 15) << 4 | r.randint(0, 3) << 28
    if r.random() < 0.03:
        v |= 0x80000000
    return v


def sb(r) -> int:
    return r.choice([0, 1, 5, 10, 20, 35, 50, 70, 99, r.randint(-20, 120)]) & 0xFF


def scene(r, centre, spread):
    cx, cy = centre
    cells = {}
    for x in range(cx - 3, cx + 4):
        for y in range(cy - 3, cy + 4):
            cells[(x, y)] = cell(r)
    shapes = [(r.randint(2, 30), r.randint(2, 16), [r.randint(0, 31) for _ in range(4)], [r.randint(0, 15) for _ in range(4)])
              for _ in range(16)]
    recs, packs = [], []
    for k in range(13):
        rec = bytearray(0x80)
        struct.pack_into("<h", rec, 4, cx * 32 + 16 + r.randint(-spread, spread))
        struct.pack_into("<h", rec, 6, cy * 16 + 8 + r.randint(-spread // 2, spread // 2))
        struct.pack_into("<h", rec, 8, cx * 32 + r.randint(-40, 40))
        struct.pack_into("<h", rec, 0x0A, cy * 16 + r.randint(-20, 20))
        struct.pack_into("<H", rec, 0x10, r.choice([0, 1, 0x12, 0x06, 0x0A, 0x22, 0x80, 0x100, 0x102]))
        rec[0x14] = r.choice([1, 1, 1, 1, 1, 2, 3, 0])
        rec[0x16] = k if k < 5 else r.choice([5, 6])
        rec[0x1A] = r.randint(0, 7)
        rec[0x19] = rec[0x1A]
        rec[0x22] = r.choice([0xFF, r.randint(0, 33), 0x1B])
        rec[0x24] = r.choice([0, 0, 50, 70, 90, r.randint(0, 100)])
        rec[0x48] = r.choice([0, 0x10, 0x23])
        rec[0x4A] = r.choice([0xFF, r.randint(0, 12)])
        for j in range(2):
            rec[0x4B + j] = r.choice(ARMOUR)
            rec[0x4D + j] = r.choice([0, 1, 2, 3, 4, 5, 6, 7, 8])
            rec[0x4F + j] = r.choice([25, 1, 3, 5, 10, 20, 30, 40, 0xF0])
        rec[0x51] = r.randint(0, 33)
        rec[0x53] = r.choice([0x13, 0x24, 0x15])
        rec[0x55] = r.choice([0, 0x80, 3, 0x85])
        rec[0x57] = r.choice([0x1D, 0x9D, 0x10])
        rec[0x5A] = sb(r)
        rec[0x5C] = r.choice([0x5F, 0x60, 0x61, 0xFF])
        for j in range(7):
            rec[0x5D + j] = r.randint(-3, 99) & 0xFF
            rec[0x64 + j] = r.randint(1, 99)
        for j in range(19):
            rec[0x6B + j] = sb(r)
        # the pack: the worn armour (so the wear finds it), throwable potions, other things
        pack = []
        for j in range(2):
            if rec[0x4B + j] != 0xFF and r.random() < 0.8:
                pack.append((0x30 + j, rec[0x4B + j], rec[0x4F + j], r.choice([1, 1, 2]), 0))
        for _ in range(r.choice([0, 1, 2, 4, 8])):
            pot = r.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
            pack.append((0x5F + pot, 0x84 + pot, r.choice([15, 20, 25, 30, 35, 40, 45, 49, 50]), r.randint(1, 5), 0))
        for _ in range(r.choice([0, 1, 3])):
            pack.append((r.randint(0, 0x90), r.randint(0, 0xA0), r.randint(0, 60), r.randint(1, 3), 0))
        pack = pack[: (64 if k < 5 else 16)]
        rec[0x7E] = len(pack)
        recs.append(rec)
        packs.append(pack)
    floors = bytes(r.choice([0, 0, 0, 0, 1]) for _ in range(13))
    immune = [r.choice([0, 0, 0, 1, 2, 0x200, 0x10, 0x40, 0xA00, r.getrandbits(12)]) for _ in range(13)]
    mods = []
    for _ in range(13):
        m = bytearray(0x12)
        if r.random() < 0.3:
            m[0] = r.randint(0, 20)
            m[1] = r.randint(0, 20)
            for j in range(7):
                m[2 + j] = r.randint(0, 15)
            m[9] = r.choice([0, 1])
            struct.pack_into("<H", m, 0x0A, r.choice([0, 0x12, 0x22, 0x80]))
            struct.pack_into("<H", m, 0x0C, r.choice([65000, r.randint(0, 3000)]))
            struct.pack_into("<H", m, 0x0E, r.choice([65000, r.randint(0, 3000)]))
            struct.pack_into("<H", m, 0x10, r.choice([65000, r.randint(0, 3000)]))
        else:
            struct.pack_into("<HHH", m, 0x0C, 65000, 65000, 65000)
        mods.append(m)
    return cells, shapes, recs, packs, floors, immune, mods


def effects(r, centre):
    cx, cy = centre
    out = []
    for kind, size, count in (("clouds", 10, r.choice([0, 0, 1, 2, 3, 7, 8])), ("areas", 10, r.choice([0, 0, 1, 2, 8])),
                              ("markers", 8, r.choice([0, 0, 1, 3, 8]))):
        recs = bytearray()
        for _ in range(9):
            rec = bytearray(size)
            struct.pack_into("<hh", rec, 0, cx * 32 + r.randint(-60, 60), cy * 16 + r.randint(-30, 30))
            if kind == "clouds":
                t = r.choice([0, 1, 2, 3, 4, 5, 5, 0])
                rec[4] = r.choice([0x49, 0x41, 0x44, 0x29, 0x22, 0x19, 0x11])
                rec[5] = t
                rec[7] = r.choice([0, 0, 1])
                struct.pack_into("<H", rec, 8, r.randint(0, 4000))
            elif kind == "areas":
                rec[4] = r.choice([0x49, 0x41, 0x44])
                rec[5] = r.choice([4, 5, 6])
                rec[6] = r.choice([0, 0, 1])
                struct.pack_into("<H", rec, 8, r.randint(0, 4000))
            else:
                rec[4] = r.randint(0x40, 0x44)
                rec[5] = r.randint(0, 2)
                rec[6] = r.choice([0, 0, 1])
            recs += rec
        out.append((kind, count, bytes(recs)))
    return out


def memory_of(cells, shapes, recs, packs, floors, immune, mods, sight, lists, clock, state, codes):
    mem = []
    for (x, y), v in cells.items():
        mem.append(far(FLOOR_SEG, (x * 40 + y) * 4, struct.pack("<I", v)))
    mem += [ds(FURN_H, bytes(s[1] for s in shapes)), ds(FURN_W, bytes(s[0] for s in shapes)),
            ds(FURN_X, bytes(b for s in shapes for b in s[2])), ds(FURN_Y, bytes(b for s in shapes for b in s[3])),
            ds(SIGHT, bytes([sight]))]
    mem += [ds(COMBATANTS + 0x80 * k, rec) for k, rec in enumerate(recs)]
    mem.append(ds(COMBATANTS + 13 * 0x80 + 0x14, b"\x00"))
    mem += [ds(CBT_FLOOR, floors), ds(HALF_W, bytes([5] * 13)), ds(HALF_H, bytes([2] * 13)), ds(FLOOR_SHOWN, b"\x00"),
            ds(CLOCK, struct.pack("<H", clock)), ds(DELAYED, b"\x01"), ds(HOLDRAND, struct.pack("<I", state)),
            ds(IMMUNE, b"".join(struct.pack("<H", v) for v in immune)), ds(MODS, b"".join(mods)),
            ds(FLASH_UNTIL, bytes(26)), ds(ACTION_STATE, bytes([7] * 13)), far(FLASH_SEG, FLASH_OFF, bytes(13 * 6)),
            ds(FLASH1, struct.pack("<H", 0x1234)), ds(FLASH2, struct.pack("<H", 0x5678)),
            ds(ATTACK_RATE, bytes([0x40] * 13)), ds(QUEUED, bytes(26)), ds(SHOWN, bytes(13)), ds(REAL_COMBAT, b"\x01"),
            ds(DEMO, b"\x00")]
    for kind, count, data in lists:
        base, counter = {"clouds": (CLOUDS, CLOUD_COUNT), "areas": (AREAS, AREA_COUNT), "markers": (MARKERS, MARKER_COUNT)}[kind]
        # the ninth record is the original's bytes after the array: keep them as they are in the image
        mem += [ds(base, data[: 8 * (10 if kind != "markers" else 8)]), ds(counter, bytes([count]))]
    # anim_file[slot]: the weapon code of each animation slot ("PT" for a caster)
    mem.append(ds(ANIM_FILE_TABLE, b"".join(struct.pack("<H", SCRATCH + 4 * k) for k in range(7))))
    for k, code in enumerate(codes):
        mem.append(ds(SCRATCH + 4 * k, code.encode() + b"\x00"))
    for k, pack in enumerate(packs):
        data = b"".join(struct.pack("<HBBBB", *it) for it in pack)
        if not data:
            continue
        if k < 5:
            mem.append(far(INV_SEG, PARTY_INV + k * 0x180, data))
        else:
            mem.append(far(INV_SEG, ENEMY_INV + (k - 5) * 0x60, data))
    return mem


def reads():
    return [{"segment": "%04X" % FLASH_SEG, "offset": "%04X" % FLASH_OFF, "length": 13 * 6},
            {"segment": "%04X" % INV_SEG, "offset": "%04X" % PARTY_INV, "length": 5 * 0x180},
            {"segment": "%04X" % INV_SEG, "offset": "%04X" % ENEMY_INV, "length": 8 * 0x60}]


def inputs_of(cells, shapes, recs, packs, floors, immune, mods, sight, lists, clock, state, codes):
    return {"cells": [[x, y, v] for (x, y), v in cells.items()], "shapes": [[s[0], s[1], s[2], s[3]] for s in shapes],
            "combatants": [rec.hex() for rec in recs], "packs": [[list(it) for it in p] for p in packs], "floors": floors.hex(),
            "immune": immune, "mods": [m.hex() for m in mods], "sight": sight,
            "lists": {kind: {"count": count, "hex": data.hex()} for kind, count, data in lists}, "clock": clock,
            "state": state, "codes": codes}


def case(r):
    centre = (r.randint(6, 33), r.randint(6, 33))
    spread = r.choice([10, 24, 40, 64])
    sc = scene(r, centre, spread)
    sight = r.choice([1, 4, 8, 12, 16])
    lists = effects(r, centre)
    clock = r.choice([0, 100, 1000, 30000, 49990])
    state = r.getrandbits(32)
    codes = [r.choice(["SW", "PT", "BW", "PT"]) for _ in range(7)]
    mem = memory_of(*sc, sight, lists, clock, state, codes)
    inputs = inputs_of(*sc, sight, lists, clock, state, codes)
    return centre, mem, inputs


def changed(res):
    return {"%05X" % (DS * 16 + int(c["offset"], 16)): c["after"] for c in res["dsChanged"]}


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261018)
    out, images = [], None
    common = {"sections": [136, 14, 19], "ds": DS, "follow": True, "stub_targets": STUBS, "max_instructions": 3000000}
    for _ in range(int(sys.argv[3]) if len(sys.argv) > 3 else 320):
        centre, mem, inputs = case(r)
        x = centre[0] * 32 + 16 + r.randint(-20, 20)
        y = centre[1] * 16 + 8 + r.randint(-10, 10)
        typ = r.choice([0, 1, 2, 3, 4, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 12, 14])
        dur = r.choice([10, 20, 25, 30, 35, 40, 45, 49, 60])
        thrower = r.randint(0, 12)
        target = r.choice([-1, r.randint(0, 12)])
        side = r.choice([0, 0, 0, 1])
        args = [x, y, thrower, typ, dur, target, side]
        inputs["args"] = args
        res = run(exe, dict(common, unit="effect_spell", entry={"segment": "1EF8", "offset": "0000"}, memory=mem,
                            args=[h16(a) for a in args], read=reads()))
        images = res["loadedImages"]
        party, enemies = bytes.fromhex(res["reads"][1]), bytes.fromhex(res["reads"][2])
        packs = [(party[k * 0x180:] if k < 5 else enemies[(k - 5) * 0x60:])[: 6 * len(inputs["packs"][k])].hex() for k in range(13)]
        out.append({"function": "effect_spell", "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                    "calls": [c["callee"] for c in res["calls"]], "changed": changed(res), "flash": res["reads"][0], "packs": packs})
    for _ in range(200):
        centre, mem, inputs = case(r)
        rec = bytearray(10)
        i = r.randint(0, 12)
        px, py = struct.unpack_from("<hh", bytes.fromhex(inputs["combatants"][i]), 4)
        struct.pack_into("<hh", rec, 0, px + r.randint(-24, 24), py + r.randint(-12, 12))
        rec[4] = r.choice([0x49, 0x29, 0x19, 0x40, 0x10, 0x30, 0x00, 0x59])
        mem.append(ds(0xFC00, bytes(rec)))
        inputs["spot"] = rec.hex()
        inputs["args"] = [i]
        res = run(exe, dict(common, unit="in_area", entry={"segment": "0E76", "offset": "469E"}, memory=mem, args=[h16(0xFC00), h16(i)]))
        out.append({"function": "in_area", "inputs": inputs, "ax": res["final"]["ax"], "changed": {}})
    for _ in range(200):
        centre, mem, inputs = case(r)
        a, t = r.randint(0, 12), r.randint(0, 12)
        inputs["args"] = [a, t]
        res = run(exe, dict(common, unit="cloud_blocks_line", entry={"segment": "1EC9", "offset": "0142"}, memory=mem,
                            args=[h16(a), h16(t)], trace={"%05x" % (0x1EC9 * 16 + 0x146): "framed"}))
        w = res["trace"][0]["frame"]
        sw = lambda v: v - 0x10000 if v >= 0x8000 else v
        inputs["residue"] = [sw(w[21]), sw(w[20]), sw(w[19]), sw(w[18])]
        out.append({"function": "cloud_blocks_line", "inputs": inputs, "ax": res["final"]["ax"], "changed": {}})
    for _ in range(200):
        # to_hit_roll's missile branch with clouds about: its rand_1_n(100) leaves the words cloud_blocks_line starts from
        centre, mem, inputs = case(r)
        a, t = r.randint(0, 12), r.randint(0, 12)
        recs = [bytearray.fromhex(x) for x in inputs["combatants"]]
        struct.pack_into("<H", recs[a], 0x10, r.choice([0x80, 0x80, 0x40, 0]))
        recs[a][0x14] = 1
        inputs["combatants"] = [x.hex() for x in recs]
        mem.append(ds(COMBATANTS + 0x80 * a, bytes(recs[a])))
        rates = bytes(r.choice([100, 90, 50]) for _ in range(13))
        reach = bytes(r.choice([0, 0, 1]) for _ in range(13 * 13))
        mem += [ds(ATTACK_RATE, rates), far(0x20A5, 0x7900, reach)]
        inputs["rates"] = rates.hex()
        inputs["reach"] = reach.hex()
        inputs["args"] = [a, t]
        res = run(exe, dict(common, unit="to_hit_roll", entry={"segment": "18BF", "offset": "02A8"}, memory=mem,
                            args=[h16(a), h16(t), h16(0xFE00)]))
        out.append({"function": "to_hit_clouds", "inputs": inputs, "ax": res["final"]["ax"], "changed": changed(res)})
    for _ in range(100):
        centre, mem, inputs = case(r)
        i = r.randint(0, 4)
        names = b"".join(struct.pack("<H", 0x7000 + 2 * k) for k in range(10))
        mem.append(ds(MENU_NAMES, names))
        mem.append(ds(MENU, bytes(0x16 * 21)))
        inputs["args"] = [i]
        res = run(exe, dict(common, unit="throw_list", entry={"segment": "1EF8", "offset": "0C80"}, memory=mem, args=[h16(i)]))
        out.append({"function": "throw_list", "inputs": inputs, "ax": res["final"]["ax"], "changed": changed(res)})
    corpus = {"authority": "test_only_original_byte_execution", "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % COMBATANTS, "holdrand": "%04X" % HOLDRAND,
              "loadedImages": images, "cases": out}
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(out))


if __name__ == "__main__":
    main()
