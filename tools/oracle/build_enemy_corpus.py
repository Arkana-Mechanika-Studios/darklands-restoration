"""Original-byte corpus for the battlefield builder's enemy records (R004 0E76): monster-type-lookup,
monsters-repair and groups-repair, chained as a generator uses them.

usage: python -I build_enemy_corpus.py <DARKLAND.EXE> <game folder> <out enemies.json>

The enemy buffer (segment 0x6000) is set up as battlefield-builder-entry does (darkland.enm at +0xB4C0,
the pointers in DS:DB29..DB35, darkland.lst at segment 0x7000), cleared by the original
monsters-reset and groups-reset, then each scenario runs a chain of original calls, carrying the
memory from call to call. Stored: the calls and their inputs, and per scenario the resulting
kind and group records, counts and random state (results only; no game bytes).
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
HOLDRAND = 0x7B20
ENEMY_SEG, ITEM_SEG = 0x6000, 0x7000
MONSTER_TOTAL, GROUP_TOTAL = 0xDAE5, 0xDB05
KINDS, GROUPS, TEMPLATES = 0x180, 0xA200, 0xB4C0
FN = {"monsters-reset": "04CC", "monster-type-lookup": "06AA", "groups-reset": "1060", "monsters-repair": "122C",
      "groups-repair": "1D90"}


class Machine:
    def __init__(self, exe: bytes, enm: bytes, lst: bytes):
        self.exe = exe
        self.enemy = bytearray(0x10000)
        self.enemy[TEMPLATES:TEMPLATES + len(enm)] = enm
        self.items = bytearray(0x10000)
        self.items[:len(lst)] = lst
        self.ds = {}  # offset -> byte for the DS variables we track
        ptrs = struct.pack("<HHHHHHH", 0, KINDS, GROUPS, TEMPLATES, 0xED54, 0xF504, 3)
        self.put_ds(0xDB29, ptrs)
        self.put_ds(0xDADD, struct.pack("<H", ENEMY_SEG))
        self.put_ds(0xDAE3, struct.pack("<H", ITEM_SEG))
        self.put_ds(MONSTER_TOTAL, bytes(64))
        self.put_ds(HOLDRAND, bytes(4))

    def put_ds(self, offset: int, data: bytes) -> None:
        for k, b in enumerate(data):
            self.ds[offset + k] = b

    def call(self, name: str, args) -> int:
        memory = [{"segment": "%04X" % ENEMY_SEG, "offset": "0000", "hex": bytes(self.enemy).hex()},
                  {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": bytes(self.items[:0x4000]).hex()}]
        for off in sorted(self.ds):
            memory.append({"offset": "%04X" % off, "hex": "%02X" % self.ds[off]})
        res = run(self.exe, {"unit": name, "entry": {"segment": "0E76", "offset": FN[name]}, "sections": [136, 4],
                             "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args],
                             "follow": True, "track_all": True, "max_instructions": 4000000})
        for c in res["memChanged"]:
            lin, val = int(c["linear"], 16), int(c["after"], 16)
            if ENEMY_SEG * 16 <= lin < ENEMY_SEG * 16 + 0x10000:
                self.enemy[lin - ENEMY_SEG * 16] = val
            elif DS * 16 <= lin < DS * 16 + 0x10000 and (lin - DS * 16) in self.ds:
                self.ds[lin - DS * 16] = val
        return int(res["final"]["ax"], 16)

    def counts(self, base: int):
        return [struct.unpack("<h", bytes([self.ds[base + 2 * i], self.ds[base + 2 * i + 1]]))[0] for i in range(16)]


def scenario(m: Machine, r: random.Random):
    state = r.getrandbits(32)
    m.put_ds(HOLDRAND, struct.pack("<I", state))
    calls = []
    levels = r.randint(1, 3)
    for _ in range(r.randint(1, 7)):
        level = r.randint(0, levels - 1)
        t = r.choice([r.randint(0, 0x51), r.randint(0, 0x51), r.randint(0, 0x51), -1, 0x52, 0x4D])
        q = r.randint(0, 6)
        ax = m.call("monster-type-lookup", [level, t, q])
        calls.append({"call": "monster-type-lookup", "args": [level, t, q], "ax": (ax ^ 0x8000) - 0x8000})
    kinds = m.counts(MONSTER_TOTAL)
    groups = []
    for level in range(levels):
        n = r.randint(0, 3)
        recs = []
        for g in range(n):
            rec = bytearray(m.enemy[GROUPS + (level * 10 + g) * 0x1E:GROUPS + (level * 10 + g + 1) * 0x1E])
            for j in range(3):
                rec[8 + j] = r.choice([0xFF, r.randint(0, max(0, kinds[level] - 1)), r.randint(0, 11)])
                rec[0x0B + j] = r.choice([0, 1, 3, 6, 0xFF])
            rec[7] = r.choice([0, 0, 1])
            rec[0x14] = r.choice([0xFF, r.randint(0, 11)])
            struct.pack_into("<hhh", rec, 0, r.randint(0, 1200), r.randint(0, 600), r.choice([0, 16, 32]))
            m.enemy[GROUPS + (level * 10 + g) * 0x1E:GROUPS + (level * 10 + g + 1) * 0x1E] = rec
            recs.append(rec.hex())
        m.put_ds(GROUP_TOTAL + 2 * level, struct.pack("<h", n))
        groups.append(recs)
    seed = r.randint(0, 0x7FFF)
    m.call("monsters-repair", [levels, seed])
    calls.append({"call": "monsters-repair", "args": [levels, seed]})
    m.call("groups-repair", [levels])
    calls.append({"call": "groups-repair", "args": [levels]})
    kinds, gcount = m.counts(MONSTER_TOTAL), m.counts(GROUP_TOTAL)
    out_kinds = [[bytes(m.enemy[KINDS + (l * 12 + k) * 0xD6:KINDS + (l * 12 + k + 1) * 0xD6]).hex() for k in range(kinds[l])]
                 for l in range(levels)]
    out_groups = [[bytes(m.enemy[GROUPS + (l * 10 + g) * 0x1E:GROUPS + (l * 10 + g + 1) * 0x1E]).hex() for g in range(gcount[l])]
                  for l in range(levels)]
    holdrand = struct.unpack("<I", bytes(m.ds[HOLDRAND + k] for k in range(4)))[0]
    return {"state": state, "levels": levels, "calls": calls, "groups": groups,
            "kinds": out_kinds, "finalGroups": out_groups, "holdrand": holdrand}


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    game = Path(sys.argv[2])
    enm = (game / "DARKLAND.ENM").read_bytes()
    lst = (game / "DARKLAND.LST").read_bytes()
    r = random.Random(20261019)
    base = Machine(exe, enm, lst)
    base.call("monsters-reset", [])
    base.call("groups-reset", [])
    snapshot = (bytes(base.enemy), dict(base.ds))
    scenarios = []
    for _ in range(int(sys.argv[4]) if len(sys.argv) > 4 else 60):
        m = Machine(exe, enm, lst)
        m.enemy[:] = snapshot[0]
        m.ds = dict(snapshot[1])
        scenarios.append(scenario(m, r))
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND,
              "cases": [{"function": "enemy_scenario", "inputs": s, "ax": "0000", "changed": {}} for s in scenarios]}
    Path(sys.argv[3]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(scenarios))


if __name__ == "__main__":
    main()
