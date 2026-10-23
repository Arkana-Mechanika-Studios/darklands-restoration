"""Coverage-guided inputs for single battle functions of the ORIGINAL, called directly from captured fight states (battle_calls.py).

usage: python -I -B battle_fuzz.py <states folder> <out folder> --functions R023+0x00B8,R014+0x08FE --states street-p300,castle-p600
           [--iterations 400] [--seed 1] [--baseline baseline.pkl]

Each function is called from each state with generated arguments and generated changes to the machine's memory (the combatant records'
fields the rewrite models, the per-combatant tables of the battle's save, the battle's scalars, the level image: MUTABLE below). A run
that reaches a block or branch outcome neither the baseline coverage (the folders of local/faithfulness/branches-work) nor an earlier
run reached is kept: <out>/<function>/<state>.json holds the kept inputs and what the original did (return value, bytes written, draws of
the random generator), <out>/<function>/cov/cov-<state>.jsonl their coverage (battle_oracle's cov format, one line per kept run).
The kept inputs are what the rewrite is asked to reproduce (BattleCallsTests in tests/Darklands.Battles.Tests).
"""
from __future__ import annotations

import argparse
import json
import os
import pickle
import random
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import battle_calls as bc  # noqa: E402
from dosmachine import DS, P  # noqa: E402
from orig_side import COMBATANTS, RECORD  # noqa: E402

BAT = {"R002", "R003"} | {"R%03d" % i for i in range(14, 26)}

# the record fields the rewrite models (CombatantRecords.Read / ReadAnimation): offset, width
RECORD_FIELDS = [(0x04, 2), (0x06, 2), (0x08, 2), (0x0A, 2), (0x0C, 1), (0x0D, 1), (0x10, 2), (0x14, 1), (0x16, 1), (0x18, 1), (0x19, 1),
                 (0x1A, 1), (0x1B, 1), (0x1D, 1), (0x1E, 1), (0x1F, 1), (0x20, 1), (0x21, 1), (0x22, 1), (0x23, 1), (0x24, 1),
                 (0x48, 1), (0x49, 1), (0x4A, 1), (0x4B, 1), (0x4C, 1), (0x4D, 1), (0x4E, 1), (0x4F, 1), (0x50, 1), (0x51, 1), (0x52, 1),
                 (0x53, 1), (0x54, 1), (0x55, 1), (0x56, 1), (0x57, 1), (0x58, 1), (0x59, 1), (0x5A, 1), (0x5B, 1), (0x5C, 1)] \
    + [(0x5D + k, 1) for k in range(14)] + [(0x6B + k, 1) for k in range(19)]
# the per-combatant tables of the battle file (BattleVariables): name, DS offset, element width, elements
TABLES = [("sequence length", 0xC96B, 1, 13), ("wait steps", 0xCA1C, 1, 13), ("push direction", 0xCA9A, 1, 13), ("pusher", 0xCAA7, 1, 13),
          ("loop set up", 0xE05F, 1, 13), ("restart loop", 0xE052, 1, 13), ("aim set up", 0xE06C, 1, 13), ("half width", 0xC80F, 1, 13), ("half height", 0xC831, 1, 13), ("moving", 0xA6D3, 1, 13), ("exchange", 0xCA57, 1, 13), ("state", 0xCA64, 1, 13), ("attack facing", 0xD3FE, 1, 13),
          ("damage shown", 0xD4C7, 1, 13), ("damage number", 0xD4E5, 1, 13), ("was hit", 0xD4F2, 1, 13), ("floor", 0xA6B4, 1, 13),
          ("gave up", 0xD5E9, 1, 13), ("engaged", 0xDA36, 1, 13), ("blocked by", 0xD5F6, 2, 13), ("flags", 0xA696, 2, 13),
          ("place", 0xA6C4, 1, 13), ("path index", 0xD6FB, 1, 13), ("orders", 0xD9E6, 2, 39),
          ("modifier held", 0xA53B, 1, 13, 0x12), ("modifier held until", 0xA542, 2, 13, 0x12), ("modifier skills until", 0xA540, 2, 13, 0x12),
          ("watched facing count", 0xD5C6, 1, 13), ("queued step", 0xCA30, 1, 13),
          ("menu enable", 0x9609, 1, 18, 0x14), ("member door", 0xC64C, 1, 5), ("member door opens", 0xC678, 1, 5), ("member door side", 0xC6EF, 1, 5),
          ("member stairs", 0xC77E, 1, 5), ("member chest", 0xC784, 1, 5), ("member dissolve", 0xC7CD, 1, 5),
          ("member exit zone", 0xC7FE, 1, 5), ("member stairs back", 0xC8B2, 1, 5), ("member skills used", 0xD68D, 2, 5)] \
    + [("clock %d" % n, a, 2, 13) for n, a in enumerate((0xD9CA, 0xD9B0, 0xD996, 0xD97C, 0xD962, 0xD948, 0xDABD, 0xDAA3, 0xDA89, 0xDA6F, 0xDA55))]
SCALARS = [("missile count", 0xC180, 1), ("map search target", 0xD6CD, 1), ("activations count", 0xD6CC, 1), ("alarm", 0xD6F4, 1), ("difficulty", 0x906A, 1), ("group move", 0xD610, 1), ("activations", 0xD6F0, 1), ("delayed start", 0xD842, 1), ("ignore people", 0xC600, 1),
           ("enemy saw", 0xC4FD, 1), ("stopped", 0xCA29, 1), ("lock is door", 0xCA99, 1), ("real combat", 0xA6C1, 1), ("floor shown", 0x11CA, 1),
           ("enemy floor", 0xC651, 1), ("clock", 0x07D4, 2), ("result", 0xA893, 2), ("result card", 0xCA3D, 2), ("timed exit", 0xDA4D, 2),
           ("end at", 0xDA45, 2), ("party rethink", 0xDA53, 2), ("enemy rethink", 0xDA51, 2), ("activations at", 0xDA4F, 2),
           ("group cursor", 0xDA4B, 2), ("combat box retry", 0xDA49, 2), ("alarm at", 0xDA47, 2), ("delayed start at", 0xC2BB, 2),
           ("leader", 0x907B, 1), ("active member", 0x17B0, 1),
           ("picker", 0xE1CA, 1), ("next to pile", 0xC865, 1), ("recoverables", 0xC783, 1), ("disarm text", 0x970B, 2)]
MUTABLE = {"records": RECORD_FIELDS, "tables": TABLES, "scalars": SCALARS}
POOL = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16, 20, 32, 40, 64, 100, 127, 128, 200, 254, 255]
FLOOR_SEG = 0x20A5          # link-time segment of the level image (0x33FC bytes)
FLOOR_SIZE = 0x33FC


def value(rng: random.Random, width: int) -> int:
    r = rng.random()
    if r < 0.6:
        v = rng.choice(POOL)
    elif r < 0.8:
        v = rng.randint(0, 255)
    else:
        v = rng.choice([0xFFFF, 0x8000, 0x7FFF, rng.randint(0, 0x3FF)])
    return v & ((1 << (8 * width)) - 1)


# what the game puts in some record fields (a weapon number past the tables, an animation slot past the eleven, a facing past the eight are not states of a battle)
WEAPONS = [0xFF] + list(range(63))
RECORD_DOMAIN = {0x16: list(range(11)), 0x18: [0xFF, 2, 3, 4, 5], 0x19: list(range(8)), 0x1A: list(range(8)), 0x22: WEAPONS, 0x51: WEAPONS,
                 0x4A: [0xFF] + list(range(13)), 0x10: [0, 1, 2, 4, 8, 0x10, 0x12, 0x20, 0x22, 0x40, 0x80, 0x82, 0x92, 0x100, 0x102, 0x400, 0x401],
                 0x0C: [0, 1, 2, 3, 4, 5, 6, 8, 10, 20, 30, 40], 0x1B: [0, 1, 2, 3, 4, 5, 6, 7, 8], 0x1E: [4, 6, 8, 10], 0x1F: [4, 6, 8, 10]}
PATH_BYTES = [0xFF, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x01, 0x12, 0x23]
TABLE_DOMAIN = {0xC96B: list(range(1, 11)), 0xCA1C: [0, 0, 1, 2, 3], 0xCA9A: list(range(10)), 0xCAA7: list(range(13)), 0xC180: list(range(18)), 0xCA57: [0, 1, 2, 3, 4, 5, 6, 7, 0x0E], 0xCA64: [0, 1, 3, 7, 9, 10, 11, 12, 13], 0xA6C4: list(range(11)), 0xD5C6: [0, 7],
                0xCA30: PATH_BYTES, 0xD6FB: list(range(32)), 0xD3FE: list(range(8)), 0xD4E5: list(range(31)), 0xD4C7: list(range(31)),
                0xD9E6: RECORD_DOMAIN[0x10], 0xC64C: [0xFF] + list(range(4)), 0xC6EF: list(range(4)), 0xC77E: [0xFF] + list(range(6)),
                0xC784: [0xFF] + list(range(20)), 0xC7CD: [0xFF] + list(range(4)), 0xC7FE: [0xFF] + list(range(6)), 0xC8B2: list(range(2)), 0x9609: [1, 2], 0xC783: list(range(11)), 0xE1CA: list(range(5)), 0xD68D: [0, 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x0F, 0x1F, 0x7F]}
FLOOR_VALUES = [0, 0, 0, 1, 2, 0x9D, 0xFF]
FLOOR_TABLES = {0xA6B4, 0x11CA, 0xC651, 0xC623}
# bytes the rewrite keeps as booleans: only 0 and 1 are states of the game
BOOL_BYTES = {0xC865, 0xE05F, 0xE052, 0xE06C, 0xA53B, 0xA6D3, 0xD4F2, 0xD5E9, 0xDA36, 0xC678, 0xA6C1, 0xD610, 0xD6F0, 0xD842, 0xC600, 0xC4FD, 0xCA29, 0xCA99, 0x9084, 0xE0A2, 0xD697, 0xE13C}


def modelled(addr: int, width: int) -> bool:
    """True when the rewrite models the DS bytes (a record field, a table, a scalar): a change elsewhere would not reach it."""
    spans = [(COMBATANTS + RECORD * k + off, w) for k in range(13) for off, w in RECORD_FIELDS]
    spans += [(base + (t[4] if len(t) > 4 else w) * i, w) for t in TABLES for (_, base, w, n) in [t[:4]] for i in range(n)]
    spans += [(base, w) for _, base, w in SCALARS]
    return any(a <= addr and addr + width <= a + w for a, w in spans)


def aimed_ops(coord: str) -> list[dict]:
    """Changes aimed at the open outcomes of the function (tools/oracle/branch_flip.py: for a branch whose flags come from a global the
    value that sends it the other way): a DS byte or word, one op each."""
    import csv
    import branch_flip
    import cfg as cfgmod
    from run_unit import Rtlink
    repo = HERE.parent.parent
    exe = (bc.GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = [f for f in cfgmod.load_functions(repo / "docs" / "source-map" / "original-functions.csv", rt) if f.name == coord]
    opens = {coord: {(r["payload"], r["kind"]) for r in csv.DictReader(open(repo / "docs" / "faithfulness" / "branches-missed.csv", encoding="utf-8"))
                     if r["function"] == coord and r["status"] == "open"}}
    out = []
    for key, r in branch_flip.run(None, opens, funcs, exe, rt, None).items():
        m = r.get("mut")
        if m and m[0] == "ds" and m[2] in (1, 2) and m[1] < 0xFE00 and modelled(m[1], m[2]):
            out.append({"w": "ds", "o": m[1], "hex": (m[3] & ((1 << (8 * m[2])) - 1)).to_bytes(m[2], "little").hex(), "aim": key})
    return out


def pack(v: int, width: int) -> str:
    return (v & ((1 << (8 * width)) - 1)).to_bytes(width, "little").hex()


class Fuzzer:
    def __init__(self, st: "bc.State", coord: str, args_spec, rng: random.Random, baseline, probes: int = 12):
        self.probes = probes
        self.aimed = [] if os.environ.get("DARKLANDS_BATTLE_FUZZ_NOAIM") else aimed_ops(coord)       # NOAIM: without branch_flip.py's values (to see what they add)
        self.st, self.coord, self.args_spec, self.rng = st, coord, args_spec, rng
        self.seen_i = {k: set(v) for k, v in baseline[0].items()}
        self.seen_e = {k: {tuple(e) for e in v} for k, v in baseline[1].items()}
        self.positions = [(int.from_bytes(st.record(k)[4:6], "little", signed=True), int.from_bytes(st.record(k)[6:8], "little", signed=True))
                          for k in range(13)]
        self.records = [bytes(st.record(k)) for k in range(13)]        # the state's own, kept: a machine that faulted is not read again
        self.alive = [k for k in range(13) if self.records[k][0x14] != 0]
        self.act_count = st.m.byte(DS, 0xD6CC)
        self.live_positions = [self.positions[k] for k in self.alive] or self.positions
        self.slots = sorted({self.records[k][0x16] for k in self.alive})
        self.doors = st.ds(0, 0)  # placeholder, the door count is read from the level image below
        self.door_count = st.m.byte(P(FLOOR_SEG), 0x1F52)
        levels = max(1, st.m.byte(DS, 0xD661))          # DS:D661: the battlefield's levels; a floor number past them is not a state of the game
        self.floor_values = [0, 0] + list(range(1, levels)) + [0x9D, 0xFF]

    # ---- generation
    def prep(self, args) -> list[dict]:
        """Changes that make the call one of the game's: the combatant of a facing test has a target, a shooter a missile weapon."""
        rng = self.rng
        if self.coord == "R014+0x306A" and args:
            return [{"w": "ds", "o": COMBATANTS + RECORD * args[0] + 0x4A, "hex": "%02x" % rng.randrange(13)}]
        if self.coord == "R018+0x0000" and args and args[0] < 13:
            return [{"w": "ds", "o": COMBATANTS + RECORD * args[0] + 0x22, "hex": "%02x" % rng.randrange(0x17, 0x22)}]
        return []

    def gen_args(self) -> list[int]:
        args = [f(self.rng, self) for f in self.args_spec]
        if self.coord in ("R014+0xB18E", "R014+0xB244") and self.rng.random() < 0.8:
            rec = self.records[args[0] % 13]                # the weapon it shoots or the ammunition type, with the quality it has
            args[1], args[2] = self.rng.choice([(rec[0x22], rec[0x5A]), (0x40, 25), (0x41, 25), (rec[0x22], 25)])
        return args

    def scenario(self) -> list[dict]:
        """Several coordinated changes at once: whole vectors of statuses, floors, the result, the timers."""
        rng = self.rng
        ops = []
        pick = "doors" if self.coord in DOOR_FUNCTIONS and rng.random() < 0.6 else rng.choice(BIAS[self.coord]) if self.coord in BIAS and rng.random() < 0.7 else rng.choice(["statuses", "floors", "result", "times", "kill enemies", "kill party", "positions", "flags", "tables", "doors", "doors", "map", "missiles"])
        if pick == "statuses":
            for k in range(13):
                st = rng.choice([1, 1, 1, 2, 3, 5, 0]) if k < 5 else rng.choice([1, 1, 0, 0, 2, 3])
                ops.append({"w": "ds", "o": COMBATANTS + RECORD * k + 0x14, "hex": "%02x" % st})
        elif pick == "floors":
            base = rng.choice(self.floor_values[:len(self.floor_values) - 2])
            for k in range(13):
                f = base if rng.random() < 0.7 else rng.choice(self.floor_values)
                ops.append({"w": "ds", "o": 0xA6B4 + k, "hex": "%02x" % f})
            ops.append({"w": "ds", "o": 0xC651, "hex": "%02x" % rng.choice(self.floor_values[:len(self.floor_values) - 2])})
        elif pick == "doors":
            # door records (14 bytes from +0x1F54: +0 a word that is positive for a real door, +6 the wall group, +7 the lock, +8/+9 and +10/+11 the cells
            # (x, y in the low six bits, the side in the high bits of the first pair), +0x0C the twin) and their number
            count = rng.randrange(1, 5)
            records = []
            ops.append({"w": "floor", "o": 0x1F52, "hex": "%02x" % count})
            for d in range(count):
                rec = bytearray(14)
                rec[0:2] = (rng.randrange(1, 0x7FFF)).to_bytes(2, "little")
                if self.coord in ("R014+0xD04E", "R014+0xDC14"):
                    x, y, w, h = self.box()                 # the door's box (x, y words, w, h bytes) where a member stands
                    rec[0:2], rec[2:4], rec[4], rec[5] = max(x, 1).to_bytes(2, "little"), y.to_bytes(2, "little"), w, h
                rec[6] = rng.choice([0xFF, 0, 1, 2, 3])
                rec[7] = rng.choice([0, 0, 1, 2, 5])
                side = rng.choice([(0x80, 0), (0x40, 0), (0, 0x80), (0, 0x40)])         # a real door has exactly one side flag, in one of the two bytes
                rec[8] = rng.randrange(40) | side[0]
                rec[9] = rng.randrange(40) | side[1]
                rec[10] = rng.choice([0, rng.randrange(40)])
                rec[11] = rng.choice([0, rng.randrange(40)])
                rec[12] = rng.choice([0xFF, 0xFF, 0, 1, 2, 3])
                ops.append({"w": "floor", "o": 0x1F54 + 14 * d, "hex": bytes(rec).hex()})
                records.append(rec)
            # the twin flags the level switch computed from the links (the second far segment, +0xC00): a door that is the twin of another
            twins = bytearray(40)
            for rec in records:
                if rec[12] != 0xFF and rec[12] < 40:
                    twins[rec[12]] = 1
            ops.append({"w": "far2", "o": 0xC00, "hex": bytes(twins).hex()})
        elif pick in ("regions", "triggers", "stairs", "zones"):
            ops.extend(self.features(pick))
        elif pick == "pack":
            ops.extend(self.packs())
        elif pick == "recov":
            count = rng.randrange(0, 11)
            ops.append({"w": "ds", "o": 0xC783, "hex": "%02x" % count})
            entries = bytearray()
            for _ in range(count):
                kind = rng.choice([0x17, 0x18, 0x19, 0x1A, 0x40, 0x41, 0x17, 0x40])
                item = {0x17: 0x17, 0x18: 0x18, 0x19: 0x19, 0x1A: 1, 0x40: 0x21, 0x41: 0x22}[kind]
                entries += item.to_bytes(2, "little") + bytes([kind, rng.choice([0x19, 0x19, 0x14, 0x28, 0xF0]), rng.choice([1, 1, 2, 254, 255]), rng.choice([1, 3])])
            if entries:
                ops.append({"w": "floor", "o": 0x3954, "hex": bytes(entries).hex()})
        elif pick == "effects":
            ops.extend(self.effects())
        elif pick == "immune":
            for k in range(13):
                ops.append({"w": "ds", "o": 0xC927 + 2 * k, "hex": pack(rng.choice([0, 0, 0, 1, 2, 0x200, 0x10, 0x40, 0xA00, rng.getrandbits(12)]), 2)})
            ops.append({"w": "ds", "o": 0xDA89 + 2 * rng.randrange(13), "hex": pack(rng.choice([0, 1, rng.randrange(0x10000)]), 2)})
        elif pick == "menus":
            for n in range(18):
                ops.append({"w": "ds", "o": 0x9609 + 0x14 * n, "hex": "%02x" % rng.choice([1, 2])})
        elif pick == "missiles":
            # missiles in flight: the table at DS:944D (0x1A bytes each; +8 position, +0xC goal, +0x10 target, +0x11 done, +0x12 facing, +0x13 shooter,
            # +0x14 sprite, +0x15 spell, +0x16 power, +0x17 speed, +0x18 floor) and their count at DS:C180
            count = rng.randrange(0, 18)
            ops.append({"w": "ds", "o": 0xC180, "hex": "%02x" % count})
            for i in range(count):
                rec = bytearray(0x1A)
                near = self.positions[rng.randrange(13)]
                rec[8:12] = (near[0] + rng.choice([-20, 0, 20])).to_bytes(2, "little", signed=True) + (near[1] + rng.choice([-10, 0, 10])).to_bytes(2, "little", signed=True)
                far_ = self.positions[rng.randrange(13)]
                rec[12:16] = far_[0].to_bytes(2, "little", signed=True) + far_[1].to_bytes(2, "little", signed=True)
                rec[0x10] = rng.choice([0xFF] + list(range(13)))
                rec[0x11] = rng.choice([0, 1, 0x80, 0x81])
                rec[0x12] = rng.randrange(8)
                rec[0x13] = rng.choice([0xFF, 0xFE] + list(range(13)))
                rec[0x14] = rng.randrange(10)
                rec[0x15] = rng.randrange(20)
                rec[0x16] = rng.randrange(100)
                rec[0x17] = rng.choice([15, 20, 30, 120])
                rec[0x18] = rng.choice([0, 0, 0, 1])
                if rec[0x10] == 0xFF and (rec[0x14] & 0x7F) < 6 and (rec[0x11] & 0x7F) == 0:
                    # a weapon missile at a spot: missile_launch leaves done 0 only for a hit exchange, rolled against a target (R018+0x0212..0x024D)
                    rec[0x11] |= 1
                ops.append({"w": "ds", "o": 0x944D + 0x1A * i, "hex": bytes(rec).hex()})
        elif pick == "map":
            # not DS:E13C (walls ignored): the original sets it only around one collision test (R014+0xF5D5..0xF5E3)
            for name, base, w in (("map strict walls", 0xE0A2, 1), ("map searching", 0xD697, 1), ("ignore people", 0xC600, 1)):
                ops.append({"w": "ds", "o": base, "hex": "%02x" % rng.randrange(2)})
            ops.append({"w": "ds", "o": 0xD6CD, "hex": "%02x" % rng.choice([0xFF, rng.randrange(13)])})
        elif pick == "result":
            ops.append({"w": "ds", "o": 0xA893, "hex": pack(rng.choice([0xFFFF, 0, 2, 3, 4, 5, 6, 7, 8]), 2)})
            ops.append({"w": "ds", "o": 0xA6C1, "hex": pack(rng.randrange(2), 1)})
        elif pick == "times":
            # the clock a few ticks on from the state's (a battle's clock rises one or a few ticks a pass; a far jump leaves the per-combatant,
            # effect and next-tick timers behind, a state the game never has: the whole tick R014+0x0F58 then differs only through them)
            clock = (struct.unpack("<H", self.st.ds(0x07D4, 2))[0] + rng.choice([0, 1, 1, 2, 8])) & 0xFFFF
            ops.append({"w": "ds", "o": 0x07D4, "hex": pack(clock, 2)})
            for name, base, w in SCALARS:
                if name in ("timed exit", "end at", "party rethink", "enemy rethink", "activations at", "group cursor", "combat box retry", "alarm at", "delayed start at"):
                    ops.append({"w": "ds", "o": base, "hex": pack((clock + rng.choice([-300, -9, -1, 0, 1, 9, 300, 0x8000])) & 0xFFFF, 2)})
        elif pick in ("kill enemies", "kill party"):
            for k in (range(5, 13) if pick == "kill enemies" else range(5)):
                ops.append({"w": "ds", "o": COMBATANTS + RECORD * k + 0x14, "hex": "%02x" % rng.choice([0, 2, 3, 5])})
        elif pick == "positions":
            # where combatants of this battle stand, moved a few pixels: a spot the level lets one stand on (a random point of the map is often a wall)
            for k in self.alive:
                x, y = rng.choice(self.live_positions)
                x, y = x + rng.choice([-16, -8, -4, 0, 4, 8, 16]), y + rng.choice([-8, -4, 0, 4, 8])
                ops.append({"w": "ds", "o": COMBATANTS + RECORD * k + 4, "hex": pack(x & 0xFFFF, 2) + pack(y & 0xFFFF, 2)})
        elif pick == "flags":
            for k in range(13):
                ops.append({"w": "ds", "o": 0xA696 + 2 * k, "hex": pack(rng.choice([0, 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000, rng.randrange(0x10000)]), 2)})
        else:
            name, base, w, n, *stride = rng.choice(TABLES)
            for i in range(n):
                ops.append({"w": "ds", "o": base + (stride[0] if stride else w) * i, "hex": pack(self.val(base, w), w)})
        return ops

    def effects(self) -> list[dict]:
        """The effect field: clouds, slowing areas and markers (DS:A2EB 10 bytes each, A342 10, A396 8) with their counts, near combatants; the spot Inside tests (DS:FE80)."""
        rng = self.rng
        ops = []
        clock = self.st.m.word(DS, 0x07D4)

        def spot():
            x, y = rng.choice(self.live_positions)
            return (x + rng.choice([-24, -8, 0, 8, 24])) & 0xFFFF, (y + rng.choice([-12, -4, 0, 4, 12])) & 0xFFFF

        for base, count_at, size, kind in ((0xA2EB, 0xA33D, 10, "cloud"), (0xA342, 0xA395, 10, "area"), (0xA396, 0xA531, 8, "marker")):
            count = rng.choice([0, 1, 1, 2, 3, 5, 8])
            ops.append({"w": "ds", "o": count_at, "hex": "%02x" % count})
            data = bytearray()
            for _ in range(8):
                x, y = spot()
                rec = bytearray(size)
                rec[0:4] = x.to_bytes(2, "little") + y.to_bytes(2, "little")
                if kind == "cloud":
                    rec[4] = rng.choice([0x11, 0x22, 0x33, 0x44, 0x21, 0x32, 0x43, 0x2A, 0x49])
                    rec[5] = rng.choice([0, 1, 2, 3, 4, 5, 5, 6, 9, 12])
                    rec[7] = rng.choice([0, 0, 1])
                    rec[8:10] = ((clock + rng.choice([-5, 0, 3, 40, 300])) & 0xFFFF).to_bytes(2, "little")
                elif kind == "area":
                    rec[4] = rng.choice([0x49, 0x29, 0x19, 0x39])
                    rec[5] = rng.choice([4, 5, 6, 0, 9])
                    rec[6] = rng.choice([0, 0, 1])
                    rec[8:10] = ((clock + rng.choice([-5, 0, 3, 40, 300])) & 0xFFFF).to_bytes(2, "little")
                else:
                    rec[4] = rng.choice([0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0])
                    rec[5] = rng.choice([0, 1, 2, 3])
                    rec[6] = rng.choice([0, 0, 1])
                data += rec
            ops.append({"w": "ds", "o": base, "hex": bytes(data).hex()})
        x, y = spot()
        ops.append({"w": "ds", "o": 0xFE80, "hex": (x.to_bytes(2, "little") + y.to_bytes(2, "little") + bytes([rng.choice([0x11, 0x22, 0x33, 0x44, 0x49, 0x2A, 0x00, 0x55])])).hex()})
        return ops

    def box(self, size_w=(4, 40), size_h=(4, 40)) -> tuple:
        """A box around where a combatant stands (x, y words; w, h bytes)."""
        rng = self.rng
        x, y = rng.choice(self.live_positions)
        w, h = rng.randrange(*size_w), rng.randrange(*size_h)
        return (x - rng.randrange(0, w) if rng.random() < 0.9 else rng.randrange(1, 1000)) & 0xFFFF, (y - rng.randrange(0, h)) & 0xFFFF, w, h

    def features(self, pick: str) -> list[dict]:
        """Records of the level image that the places of the party are tested against (the file's layout: R024+0x0000)."""
        rng = self.rng
        ops = []
        if pick == "regions":
            count = rng.randrange(1, 5)
            ops.append({"w": "floor", "o": 0x300C, "hex": "%02x" % count})
            for k in range(count):
                x, y, w, h = self.box()
                rec = bytearray(0x2C)
                rec[0:2], rec[2:4], rec[4], rec[5] = x.to_bytes(2, "little"), y.to_bytes(2, "little"), w, h
                rec[8] = rng.choice([0, 0, 0x14, 0x32, 0x50, 0x8A, rng.randrange(256)])
                rec[9] = rng.choice([0, 0, 0x14, 0x32, 0x50, rng.randrange(256)])
                rec[0x0B] = rng.randrange(5)
                rec[0x0C] = rng.randrange(8)
                ops.append({"w": "floor", "o": 0x300E + 0x2C * k, "hex": bytes(rec).hex()})
        elif pick == "triggers":
            count = rng.randrange(1, 6)
            ops.append({"w": "floor", "o": 0x2184, "hex": "%02x" % count})
            for k in range(count):
                x, y, w, h = self.box()
                rec = bytearray(14)
                rec[0:2], rec[2:4], rec[4], rec[5] = x.to_bytes(2, "little"), y.to_bytes(2, "little"), w, h
                rec[6:8] = rng.choice([0, 1, 2, 4, 5, 6, 7, 8, 4, 2, 6, 7, 8, 0, 5]).to_bytes(2, "little")
                lists = max(1, self.st.m.byte(P(FLOOR_SEG), 0x2488))
                rec[10:12] = rng.choice([rng.randrange(lists), rng.randrange(50), 0, 1, 2, 3, rng.randrange(0x10000)]).to_bytes(2, "little")
                rec[12:14] = rng.choice([0, 1, 2, rng.randrange(40), rng.randrange(0x10000)]).to_bytes(2, "little")
                ops.append({"w": "floor", "o": 0x2186 + 14 * k, "hex": bytes(rec).hex()})
        elif pick == "stairs":
            count = rng.randrange(1, 4)
            ops.append({"w": "floor", "o": 0x2FB6, "hex": "%02x" % count})
            for k in range(count):
                x, y, w, h = self.box()
                rec = bytearray(14)
                rec[0:2], rec[2:4], rec[4], rec[5] = x.to_bytes(2, "little"), y.to_bytes(2, "little"), w, h
                ops.append({"w": "floor", "o": 0x2FB8 + 14 * k, "hex": bytes(rec).hex()})
        else:
            count = rng.randrange(1, 4)
            ops.append({"w": "floor", "o": 0x3326, "hex": "%02x" % count})
            for k in range(count):
                x, y, w, h = self.box((4, 60), (4, 60))
                rec = bytes(x.to_bytes(2, "little") + y.to_bytes(2, "little") + w.to_bytes(2, "little") + h.to_bytes(2, "little") + bytes(2))
                ops.append({"w": "floor", "o": 0x3328 + 10 * k, "hex": rec.hex()})
        return ops

    def packs(self) -> list[dict]:
        """The packs (2E38 segment: member k 003E + 0x180 k, enemy k 0BDE + 0x60 k, six bytes an entry: code word, type, quality, count, weight) and their counts (record +0x7E)."""
        rng = self.rng
        ops = []
        for k in rng.sample(range(13), rng.randrange(1, 4)):
            rec = self.records[k]
            weapon, quality = rec[0x22], rec[0x5A]
            n = rng.choice([0, 1, 2, 3, 6, 16] if k >= 5 else [0, 1, 2, 3, 6, 20, 64])
            entries = bytearray()
            for _ in range(n):
                t = rng.choice(["weapon", "weapon", "ammo", "ammo", "water", "any", "potion", "potion", "potion"])
                if t == "weapon":
                    e = (weapon, weapon, quality if rng.random() < 0.8 else rng.randrange(256), rng.choice([1, 1, 2, 5]), 1)
                elif t == "ammo":
                    e = (rng.choice([0x21, 0x22, 1]), rng.choice([0x40, 0x41]), 25 if rng.random() < 0.8 else rng.randrange(256), rng.choice([1, 1, 2, 12, 20]), 1)
                elif t == "potion":
                    p = rng.randrange(10)
                    e = (0x5F + p, 0x84 + p, rng.choice([20, 25, 30, 35, 40, 45, 49, rng.randrange(256)]), rng.choice([1, 2, 3, 5]), 1)
                elif t == "water":
                    e = (99, rng.choice([99, 0x50]), rng.choice([20, 40, 80, rng.randrange(256)]), rng.choice([1, 2]), 1)
                else:
                    e = (rng.randrange(0x80), rng.randrange(0x80), rng.randrange(256), rng.choice([1, 2, 3]), rng.randrange(256))
                entries += bytes([e[0] & 0xFF, 0, e[1] & 0xFF, e[2] & 0xFF, e[3] & 0xFF, e[4] & 0xFF])
            at = 0x3E + 0x180 * k if k < 5 else 0xBDE + 0x60 * k
            if entries:
                ops.append({"w": "far3", "o": at, "hex": bytes(entries).hex()})
            ops.append({"w": "ds", "o": COMBATANTS + RECORD * k + 0x7E, "hex": "%02x" % n})
        return ops

    def val(self, base: int, w: int, index: int = 0) -> int:
        """A value for the table or scalar at base: a floor number, a boolean, or any."""
        if base in FLOOR_TABLES:
            return self.rng.choice(self.floor_values)
        if base in BOOL_BYTES:
            return self.rng.randrange(2)
        if base == 0xD6CC:
            return self.rng.randrange(self.act_count + 1)         # no more hidden encounters than the level's list holds
        if base in TABLE_DOMAIN:
            return self.rng.choice(TABLE_DOMAIN[base])
        return value(self.rng, w)

    def gen_op(self) -> dict:
        rng = self.rng
        if self.aimed and rng.random() < 0.3:
            return rng.choice(self.aimed)
        r = rng.random()
        if r < 0.45:
            k = rng.choice(self.alive) if self.alive and rng.random() < 0.8 else rng.randrange(13)
            off, w = rng.choice(RECORD_FIELDS)
            if off in (0x04, 0x06, 0x08, 0x0A):
                other = rng.choice(self.live_positions)
                v = other[((off - 4) // 2) % 2] + rng.choice([-20, -8, -2, 0, 2, 8, 20, 40])
            elif off == 0x14:
                v = rng.choice([0, 1, 1, 1, 2, 3, 5])          # the battle code writes 0, 1, 2, 3 and 5 only
            elif off == 0x4A and k >= 5:
                v = rng.choice([0xFF] + list(range(5)))      # an enemy targets a member (R018+0x0706)
            elif off == 0x16:
                v = rng.choice(self.slots)          # an animation slot the state has loaded (an empty one is not a state of a battle)
            elif off in RECORD_DOMAIN:
                v = rng.choice(RECORD_DOMAIN[off])
            else:
                v = value(rng, w)
            return {"w": "ds", "o": COMBATANTS + RECORD * k + off, "hex": pack(v, w)}
        if r < 0.75:
            name, base, w, n, *stride = rng.choice(TABLES)
            i = rng.randrange(n)
            return {"w": "ds", "o": base + (stride[0] if stride else w) * i, "hex": pack(self.val(base, w), w)}
        if r < 0.85:
            name, base, w = rng.choice(SCALARS)
            return {"w": "ds", "o": base, "hex": pack(self.val(base, w), w)}
        if r < 0.92:
            a, b = rng.randrange(13), rng.randrange(13)
            if self.records[a][0x14] != 0 and a != b:
                a = b                                      # a live combatant copied over another slot is one character twice
            rec = self.records[a]
            return {"w": "ds", "o": COMBATANTS + RECORD * b, "hex": rec.hex()}
        if r < 0.95:
            # a path: packed steps (low nibble the direction, high nibble the sprite's facing) ended by 0xFF
            k = rng.choice(self.alive) if self.alive else 0
            steps = [rng.choice([0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, rng.randrange(256) & 0x9F]) for _ in range(rng.choice([0, 1, 2, 5, 12]))]
            return {"w": "floor", "o": 0x6238 + 250 * k, "hex": bytes(steps + [0xFF]).hex()}
        # the level image: the door records (14 bytes from 0x1F54), their open flags (+0x1F54 - 40?) and cells
        if rng.random() < 0.5 and self.door_count:
            d = rng.randrange(self.door_count)
            return {"w": "floor", "o": 0x1F54 + 14 * d + rng.randrange(14), "hex": pack(value(rng, 1), 1)}
        return {"w": "floor", "o": rng.randrange(FLOOR_SIZE), "hex": pack(value(rng, 1), 1)}

    def writes(self, ops):
        out = []
        for op in ops:
            seg = DS if op["w"] == "ds" else self.st.far2_seg if op["w"] == "far2" else P(bc.PACK_SEG) if op["w"] == "far3" else P(FLOOR_SEG)
            out.append((seg, op["o"], bytes.fromhex(op["hex"])))
        return out

    def run(self, args, ops) -> dict:
        r = self.st.call(self.coord, args, self.writes(ops))
        return r

    def novelty(self, r) -> int:
        n = 0
        for k, v in r["coverage"]["insns"].items():
            if k in BAT:
                n += len(set(v) - self.seen_i.setdefault(k, set()))
        for k, v in r["coverage"]["edges"].items():
            if k in BAT:
                n += len({tuple(e) for e in v} - self.seen_e.setdefault(k, set()))
        return n

    def absorb(self, r):
        for k, v in r["coverage"]["insns"].items():
            if k in BAT:
                self.seen_i.setdefault(k, set()).update(v)
        for k, v in r["coverage"]["edges"].items():
            if k in BAT:
                self.seen_e.setdefault(k, set()).update(tuple(e) for e in v)

    def fuzz(self, iterations: int):
        rng = self.rng
        corpus = [{"args": self.gen_args(), "ops": []}]
        kept = []
        probes_left = self.probes       # runs that add nothing to the coverage are kept too, a few: the rewrite is asked about them as well (leads)
        t0 = time.time()
        for it in range(iterations):
            base = rng.choice(corpus) if rng.random() < 0.7 else corpus[-1]
            ops = list(base["ops"])
            args = list(base["args"])
            kind = rng.random()
            if kind < 0.25:
                args = self.gen_args()
            if kind > 0.2 or not ops:
                if rng.random() < 0.5:
                    ops.extend(self.scenario())
                else:
                    for _ in range(rng.choice([1, 1, 2, 3])):
                        ops.append(self.gen_op())
            if len(ops) > 60:
                ops = ops[-60:]
            ops = ops + self.prep(args)
            try:
                r = self.run(args, ops)
            except Exception as e:      # a machine fault of the original on odd bytes: not an input
                continue
            if r["error"]:
                continue
            novel = self.novelty(r)
            probe = not novel and probes_left > 0 and ops and rng.random() < 0.15
            if novel or probe:
                probes_left -= 0 if novel else 1
                self.absorb(r)
                item = {"args": args, "ops": ops, "ax": r["ax"], "dx": r["dx"], "changed": {str(k): v for k, v in r["changed"].items()}, "far_changed": {str(k): v for k, v in r["far_changed"].items()}, "far2_changed": {str(k): v for k, v in r["far2_changed"].items()}, "far3_changed": {str(k): v for k, v in r["far3_changed"].items()},
                        "random": r["random"], "draws": r["draws"], "seconds": r["seconds"], "instructions": r["instructions"],
                        "coverage": r["coverage"], "iteration": it, "novel": bool(novel)}
                kept.append(item)
                corpus.append({"args": args, "ops": ops})
        self.elapsed = time.time() - t0
        return kept


# argument generators: (rng, fuzzer) -> word
who = lambda rng, f: rng.randrange(13)
member = lambda rng, f: rng.randrange(5)
enemy = lambda rng, f: 5 + rng.randrange(8)
door = lambda rng, f: rng.randrange(4)
flag = lambda rng, f: rng.randrange(2)
small = lambda rng, f: rng.randrange(8)
anything = lambda rng, f: rng.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 0xFFFF, rng.randrange(0x10000)])
buffer = lambda rng, f: 0xFE80          # a DS offset above the stack for a function's output words
DOOR_FUNCTIONS = {"R023+0x00B8", "R023+0x04AC"}
# per function: the scenarios that matter most to it (70% of the scenarios drawn for it)
BIAS = {"R014+0xD04E": ["doors", "doors", "pack", "menus"], "R014+0xD402": ["regions", "regions", "pack", "menus"], "R014+0xD6DA": ["triggers"],
        "R014+0xDC14": ["doors", "regions", "triggers", "stairs", "zones", "pack", "menus"], "R014+0xD328": ["stairs", "menus"], "R014+0xD60A": ["zones", "menus"],
        "R014+0x1BA0": ["pack", "recov", "missiles"], "R018+0x03AE": ["recov"]}
FX_BIAS = ["effects", "effects", "effects", "immune", "pack", "statuses", "times", "tables"]
for _c in ("R019+0x0000", "R019+0x0300", "R019+0x058A", "R019+0x01E2", "R019+0x0260", "R019+0x02B4", "R019+0x0968", "R019+0x0914", "R019+0x0A7C", "R019+0x0AAC", "R019+0x0B00", "R019+0x0B44",
           "R019+0x0B9E", "R019+0x0BF8", "R019+0x0C28", "R019+0x0DFE", "R019+0x0C80", "R014+0x469E"):
    BIAS[_c] = FX_BIAS
for _c in ("R014+0xB0D8", "R014+0xB18E", "R014+0xB244"):
    BIAS[_c] = ["pack", "pack", "pack", "tables"]
spotx = lambda rng, f: rng.choice(f.live_positions)[0] + rng.choice([-24, -8, 0, 8, 24])
spoty = lambda rng, f: rng.choice(f.live_positions)[1] + rng.choice([-12, -4, 0, 4, 12])
target_ = lambda rng, f: rng.choice([0xFFFF, rng.randrange(13), rng.randrange(13)])
dur_ = lambda rng, f: rng.choice([10, 20, 25, 30, 35, 40, 45, 49, 60])
slot8 = lambda rng, f: rng.choice([0, 0, 0, 1, 1, 2, 3, 4, 7])
leaderish = lambda rng, f: rng.choice([0, 0, 0, rng.randrange(5), rng.randrange(13)])
FUNCTIONS = {
    "R018+0x0000": [who, lambda rng, f: rng.choice([0xFFFF] + list(range(13))), lambda rng, f: 0, lambda rng, f: 0, lambda rng, f: 0],
    "R014+0x481A": [who],
    "R014+0x9C4C": [who],
    "R014+0x0DEE": [],
    "R014+0xFADC": [],
    "R014+0x10232": [who, who],
    "R014+0x2A4A": [],
    "R014+0x45AE": [],
    "R014+0x4442": [],
    "R014+0x0F58": [],
    "R014+0x0D86": [],
    "R014+0x2FA2": [who],
    "R014+0x306A": [who],
    "R018+0x04B6": [buffer],
    "R014+0x08FE": [],
    "R014+0x0694": [],
    "R023+0x00B8": [door, member],
    "R023+0x04AC": [door, flag, flag],
    "R023+0x03CA": [member, lambda r, f: r.randrange(40), lambda r, f: r.randrange(40), lambda r, f: r.randrange(5)],
    "R023+0x0734": [lambda r, f: r.randrange(40), lambda r, f: r.randrange(40), lambda r, f: r.randrange(5), lambda r, f: r.choice([0, 1, 1, 2])],
    "R014+0x1624": [who, lambda rng, f: rng.randrange(11)],
    "R014+0x2CCA": [who],
    "R014+0x2324": [who, anything],
    "R018+0x0706": [],
    "R018+0x04B6": [who],
    "R018+0x0000": [who, who, small, small, small],
    "R018+0x03AE": [lambda r, f: r.choice([0x17, 0x18, 0x19, 0x1A, 0x40, 0x41]), lambda r, f: r.choice([0x19, 0x19, 0x14, 0x28, 0xF0, r.randrange(256)])],
    "R014+0x1BA0": [who],
    "R014+0x0F58": [],
    "R014+0x4442": [],
    "R019+0x0000": [spotx, spoty, who, lambda r, f: r.choice([0, 1, 2, 3, 4, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 12, 14]), dur_, target_, flag],
    "R019+0x0300": [slot8, dur_, target_, flag], "R019+0x058A": [slot8, lambda r, f: r.choice([6, 7, 8, 6, 7, 8, 9]), who, dur_, target_, flag],
    "R019+0x01E2": [slot8], "R019+0x0260": [slot8], "R019+0x02B4": [slot8],
    "R019+0x0968": [who, lambda r, f: r.choice([r.randrange(0x80), 0x4B, 0x4C, 0x43, 0x44, 0x48, 0x50, 0x54]), lambda r, f: r.randrange(256), lambda r, f: r.choice([1, 2, 5, 10, 20, 40, 100])],
    "R019+0x0914": [who], "R019+0x0A7C": [who], "R019+0x0AAC": [who, lambda r, f: r.choice([0, 10, 25, 50, 100]), lambda r, f: r.randrange(2)],
    "R019+0x0B00": [who], "R019+0x0B44": [who, lambda r, f: r.choice([0, 10, 25, 50, 100]), lambda r, f: r.randrange(7)],
    "R019+0x0B9E": [who], "R019+0x0BF8": [who], "R019+0x0C28": [who, lambda r, f: r.randrange(2)],
    "R019+0x0DFE": [who, lambda r, f: r.randrange(40), lambda r, f: r.choice([10, 20, 30, 40, 50])], "R019+0x0C80": [lambda r, f: r.randrange(5)],
    "R014+0x469E": [lambda r, f: 0xFE80, who],
    "R014+0xB0D8": [who], "R014+0xB18E": [who, lambda r, f: r.randrange(256), lambda r, f: r.randrange(256)], "R014+0xB244": [who, lambda r, f: r.randrange(256), lambda r, f: r.randrange(256)],
    "R014+0x116E": [], "R014+0x1F00": [who], "R014+0xF67A": [], "R014+0xFC50": [], "R014+0x10146": [], "R014+0xED10": [],
    "R014+0x4188": [lambda r, f: r.choice(f.live_positions)[0] + r.choice([-16, 0, 16, 32]), lambda r, f: r.choice(f.live_positions)[1] + r.choice([-8, 0, 8, 16]), lambda r, f: r.randrange(8)],
    "R014+0xF88E": [who], "R014+0xF9D8": [who], "R014+0x47AE": [who, who],
    "R014+0xAED0": [who, lambda r, f: r.choice([0xFFFF] + list(range(63))), flag],
    "R014+0xD04E": [leaderish], "R014+0xD402": [leaderish], "R014+0xD6DA": [leaderish], "R014+0xDC14": [], "R014+0xD328": [leaderish], "R014+0xD60A": [leaderish],
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("states")
    ap.add_argument("out")
    ap.add_argument("--functions", required=True)
    ap.add_argument("--states-list", dest="state_names", required=True)
    ap.add_argument("--iterations", type=int, default=400)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--baseline", default=None, help="pickle of (insns, edges) per record, the coverage a run must beat to be kept (battle_calls_measure.py baseline makes it); default <DARKLANDS_LOCAL>/batch/battlecalls/baseline.pkl")
    a = ap.parse_args()
    baseline = pickle.load(open(a.baseline or Path(os.environ["DARKLANDS_LOCAL"]) / "batch" / "battlecalls" / "baseline.pkl", "rb"))
    funcs = a.functions.split(",")
    out = Path(a.out)
    for name in a.state_names.split(","):
        st = bc.State(Path(a.states) / (name + ".state"), near=funcs)
        for coord in funcs:
            folder = out / coord.replace("+", "_")
            if (folder / (name + ".json")).exists():
                continue                    # done by an earlier run of this command
            rng = random.Random("%s|%s|%d" % (name, coord, a.seed))
            try:
                cpu = time.process_time()
                fz = Fuzzer(st, coord, FUNCTIONS[coord], rng, baseline)
                kept = fz.fuzz(a.iterations)
                fz.cpu = time.process_time() - cpu
            except Exception as e:          # the machine faulted beyond a call (unicorn): this pair is left, the state is loaded again
                print(coord, name, "FAILED", type(e).__name__, str(e)[:100], flush=True)
                st = bc.State(Path(a.states) / (name + ".state"), near=funcs)
                continue
            (folder / "cov").mkdir(parents=True, exist_ok=True)
            (folder / (name + ".json")).write_text(json.dumps({"state": name, "function": coord, "kept": kept, "seconds": fz.elapsed, "cpu": fz.cpu, "iterations": a.iterations}), encoding="utf-8")
            with open(folder / "cov" / ("cov-%s.jsonl" % name), "w", encoding="utf-8") as f:
                for n, item in enumerate(kept):
                    f.write(json.dumps({"unit": coord, "state": "%s#%d" % (name, item["iteration"]), "seconds": item["seconds"], **item["coverage"]}) + "\n")
            print(coord, name, "kept", len(kept), "of", a.iterations, "%.0f s wall, %.0f s cpu" % (fz.elapsed, fz.cpu), flush=True)


if __name__ == "__main__":
    main()
