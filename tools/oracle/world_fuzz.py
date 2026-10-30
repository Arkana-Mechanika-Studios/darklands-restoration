"""Coverage-guided inputs for single functions of the ORIGINAL outside battle, called directly from captured session states (world_calls.py).

usage: python -I -B world_fuzz.py <states folder> <out folder> --functions R029+0x0F54,R030+0x1300 --states-list wildcamp-map,pressburg-map-walked
           [--iterations 150] [--seed 1] [--records R029,R030,R039,R043,root] [--baseline baseline.pkl]

Each function is called from each state with generated word arguments and generated edits to the state's saved game (the header's variables,
the characters' attributes, skills, packs and saints, the timed events, the places' records: SAV below; the original loads the edited bytes,
so every edit is a state the game can write). A run that reaches an instruction or branch outcome of a record in --records that neither the
baseline (the folders of local/faithfulness/branches-work, world_calls_measure.py baseline) nor an earlier run reached is kept (and a few that
add nothing, "probes": the rewrite is asked about those too). <out>/<function>/<state>.json holds the kept inputs and what the original did
(AX/DX, the generator, the srand calls, the saved games before and after), <out>/<function>/cov/cov-<state>.jsonl their coverage.
"""
from __future__ import annotations

import argparse
import base64
import gzip
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
from paths import local_dir  # noqa: E402
import world_calls as wc  # noqa: E402

HEAD_END, CHAR, EVENT, PLACE = 0x189, 0x22A, 0x30, 0x3A
STATS = [0, 1, 2, 3, 5, 8, 10, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90, 95, 99]          # attributes, maxima and skills of a game stay below 100
POOL = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 16, 20, 25, 30, 32, 40, 50, 64, 99, 100, 127, 128, 150, 200, 254, 255]


def w16(v: int) -> bytes:
    return struct.pack("<H", v & 0xFFFF)


class Save:
    """A saved game as its sections (savdiff.py's cut) with the structural edits the fuzzer makes."""

    def __init__(self, data: bytes):
        self.head = bytearray(data[:HEAD_END])
        n = struct.unpack_from("<H", data, 0xF1)[0]
        at = HEAD_END
        self.chars = [bytearray(data[at + k * CHAR:at + (k + 1) * CHAR]) for k in range(n)]
        at += n * CHAR
        m = struct.unpack_from("<H", data, at)[0]
        at += 2
        self.events = [bytearray(data[at + k * EVENT:at + (k + 1) * EVENT]) for k in range(m)]
        at += m * EVENT
        p = struct.unpack_from("<H", data, at)[0]
        at += 2
        self.places = [bytearray(data[at + k * PLACE:at + (k + 1) * PLACE]) for k in range(p)]
        at += p * PLACE
        self.cache = bytes(data[at:])

    def build(self) -> bytes:
        return (bytes(self.head) + b"".join(bytes(c) for c in self.chars) + w16(len(self.events)) + b"".join(bytes(e) for e in self.events)
                + w16(len(self.places)) + b"".join(bytes(p) for p in self.places) + self.cache)

    def apply(self, op: dict):
        s = op["s"]
        if s == "head":
            self.head[op["o"]:op["o"] + len(op["hex"]) // 2] = bytes.fromhex(op["hex"])
        elif s == "char":
            if op["i"] < len(self.chars):
                self.chars[op["i"]][op["o"]:op["o"] + len(op["hex"]) // 2] = bytes.fromhex(op["hex"])
        elif s == "event":
            if op["i"] < len(self.events):
                self.events[op["i"]][op["o"]:op["o"] + len(op["hex"]) // 2] = bytes.fromhex(op["hex"])
        elif s == "event+":
            if len(self.events) < 300:
                self.events.append(bytearray(bytes.fromhex(op["hex"])))
        elif s == "event-":
            if op["i"] < len(self.events):
                del self.events[op["i"]]
        elif s == "place":
            if op["i"] < len(self.places):
                self.places[op["i"]][op["o"]:op["o"] + len(op["hex"]) // 2] = bytes.fromhex(op["hex"])
        else:
            raise ValueError(s)

    def word(self, section: str, i: int, o: int) -> int:
        rec = {"head": [self.head], "char": self.chars, "event": self.events, "place": self.places}[section][i]
        return struct.unpack_from("<H", rec, o)[0]


# header fields (offsets in the file): name, offset, width, domain
HEADER = [("year", 0x68, 2, list(range(1400, 1450, 3)) + [1400, 1401, 1402, 1447]), ("month", 0x6A, 2, list(range(12))), ("day", 0x6C, 2, list(range(0, 31))),
          ("hour", 0x6E, 2, [0, 1, 5, 6, 12, 18, 21, 22, 23]), ("florins", 0x70, 2, POOL + [1000, 5000]), ("groschen", 0x72, 2, [0, 1, 5, 11, 12, 19, 20, 30]),
          ("pfennigs", 0x74, 2, [0, 1, 5, 11, 12, 19, 20, 30]), ("fame", 0x7A, 2, POOL + [300, 500, 1000, 5000]), ("x", 0x7E, 2, [0, 5, 100, 200, 300, 400, 500]),
          ("y", 0x80, 2, [0, 5, 30, 60, 100, 150]), ("bank florins", 0x8C, 2, POOL), ("stone", 0x92, 2, [0, 1]), ("leader", 0xA1, 1, [0, 1, 2, 3, 4]),
          ("map mode", 0xA4, 2, [0, 1, 3])]
CHAR_BYTES = [("status", 0x14, 1, [0, 1, 1, 1, 1, 2, 3, 4, 5]), ("age", 0x12, 2, [16, 18, 20, 30, 40, 50, 60, 70, 80]), ("sex", 0x17, 1, [0, 1]),
              ("encumbrance", 0x49, 1, [0, 0, 3, 10, 20, 100, 200]), ("weapon potions", 0x48, 1, [0, 1, 5, 20]), ("armour potions", 0x55, 1, [0, 1, 5, 20])]
LOC_BYTES = [("flags", 0x14, 1, [0, 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0xFF]), ("size", 0x11, 1, [0, 1, 2, 3, 4, 5, 6]), ("reputation", 0x12, 2, [0, 5, 20, 50, 100, 0xFFFF, 0xFFEC]),
             ("local0", 0x15, 1, [0, 1, 2, 5, 10, 0xFF]), ("local1", 0x16, 1, [0, 1, 2, 5, 10, 0xFF]), ("local2", 0x17, 1, [0, 1, 2, 5, 10, 0xFF]),
             ("mark", 0x1A, 2, [0, 1, 2, 3, 0xFFFF]), ("recorded0", 0x1C, 2, POOL), ("recorded1", 0x1E, 2, POOL), ("recorded2", 0x20, 2, POOL),
             ("cache", 0x18, 2, [0xFFFF, 0, 1, 2])]
EVENT_KINDS = [0x08, 0x11, 0x12, 0x13, 0x1C, 0x1D, 0x24, 0x28, 0x2A, 0x2B, 0x3B, 0x3D, 0x43, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x51, 0x52, 0x5D, 0x5F, 0x60, 0x61]


class Fuzzer:
    def __init__(self, st: "wc.WState", coord: str, argspec, rng: random.Random, baseline, records, probes: int = 6):
        self.st, self.coord, self.argspec, self.rng, self.records, self.probes = st, coord, argspec, rng, records, probes
        self.seen_i = {k: set(v) for k, v in baseline[0].items() if k in records}
        self.seen_e = {k: {tuple(e) for e in v} for k, v in baseline[1].items() if k in records}
        self.sav = Save(st.sav)
        self.errors = {}
        self.codes = sorted({struct.unpack_from("<H", c, 0xAA + 6 * i)[0] for c in self.sav.chars for i in range(min(c[0x7E], 64))}) or [0]
        self.places = len(self.sav.places)
        w = lambda o: sorted({struct.unpack_from("<H", e, o)[0] for e in self.sav.events})        # what the state's events hold, for the queries
        self.ekinds, self.etypes, self.eowners, self.etags, self.elocs, self.evalues = w(0x22), w(0x00), w(0x1A), w(0x28), w(0x1C), w(0x1E)

    # ---- edits
    def value(self, dom):
        return self.rng.choice(dom)

    def gen_op(self) -> dict:
        rng = self.rng
        r = rng.random()
        s = self.sav
        if self.coord in DS_EDITS and rng.random() < 0.3:
            o, w, dom = rng.choice(DS_EDITS[self.coord])
            return {"s": "ds", "o": o, "hex": rng.choice(dom).to_bytes(w, "little").hex()}
        if r < 0.22:
            name, o, w, dom = rng.choice([h for h in HEADER if not (self.coord in MAP_STATE and h[0] in ("x", "y", "map mode"))])
            v = self.value(dom)
            if name == "year":
                return {"s": "head", "o": o - 0x00, "hex": w16(v).hex()}
            return {"s": "head", "o": o, "hex": v.to_bytes(w, "little").hex()}
        if r < 0.55:
            k = rng.randrange(len(s.chars)) if s.chars else 0
            q = rng.random()
            if q < 0.25:
                name, o, w, dom = rng.choice(CHAR_BYTES)
                return {"s": "char", "i": k, "o": o, "hex": self.value(dom).to_bytes(w, "little").hex()}
            if q < 0.55:
                base = rng.choice([0x5D, 0x64, 0x6B])          # attributes, maxima, skills
                n = 7 if base != 0x6B else 19
                return {"s": "char", "i": k, "o": base + rng.randrange(n), "hex": "%02x" % rng.choice(STATS)}
            if q < 0.8:
                i = rng.randrange(64)
                if rng.random() < 0.4:
                    return {"s": "char", "i": k, "o": 0x7E, "hex": "%02x" % rng.choice([0, 1, 2, 3, 5, 10, 16, 40, 63, 64, 0xFF])}
                field = rng.choice([0, 0, 2, 3, 4, 5])
                if field == 0:
                    return {"s": "char", "i": k, "o": 0xAA + 6 * i, "hex": w16(rng.choice(self.codes + [0, 1, rng.randrange(200)])).hex()}
                return {"s": "char", "i": k, "o": 0xAA + 6 * i + field, "hex": "%02x" % rng.choice(POOL)}
            if q < 0.9:
                return {"s": "char", "i": k, "o": 0x80 + rng.randrange(0x14), "hex": "%02x" % rng.choice([0, 0xFF, 0x80, 0x01, rng.randrange(256)])}
            return {"s": "char", "i": k, "o": 0x94 + rng.randrange(0x16), "hex": "%02x" % rng.choice([0, 0xFF, 1, 2, 4, 7, rng.randrange(256)])}
        if r < 0.75:
            if s.events and r < 0.66:
                e = rng.randrange(len(s.events))
                f = rng.choice([("kind", 0x22, 2), ("type", 0x00, 2), ("owner", 0x1A, 2), ("loc", 0x1C, 2), ("value", 0x1E, 2), ("extra", 0x20, 2), ("flags", 0x24, 2),
                                ("handler", 0x26, 2), ("tag", 0x28, 2), ("d1", 0x2A, 2), ("d2", 0x2C, 2), ("d3", 0x2E, 2), ("end hour", 0x12, 2), ("end day", 0x14, 2),
                                ("end month", 0x16, 2), ("end year", 0x18, 2), ("start day", 0x0C, 2)])
                name, o, w = f
                v = rng.choice(EVENT_KINDS) if name == "kind" else rng.choice([0, 1, 2, 3, 4, 5, 7, 9, 0xFFFF, 0xFFFE]) if name in ("owner", "handler", "flags") \
                    else rng.randrange(self.places) if name in ("loc", "d1", "d2") and rng.random() < 0.7 else rng.choice(POOL + [1400, 1401, 1402, 0xFFFF])
                return {"s": "event", "i": e, "o": o, "hex": w16(v).hex()}
            if r < 0.7:
                rec = bytearray(EVENT)
                year = struct.unpack_from("<H", s.head, 0x68)[0]
                for o, v in ((0x22, rng.choice(EVENT_KINDS)), (0x00, rng.choice(POOL[:12])), (0x1A, rng.choice([0xFFFE, 0xFFFE, 0, 1, 2, 3, 4])),
                             (0x1C, rng.choice([rng.randrange(self.places), 0xFFFF])), (0x1E, rng.choice(POOL)), (0x28, rng.choice(POOL[:12])),
                             (0x24, rng.choice([0, 1])), (0x26, rng.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 9])), (0x2A, rng.choice(POOL)), (0x2C, rng.choice(POOL)),
                             (0x2E, rng.choice(POOL)), (0x18, year + rng.choice([0, 0, 0, 1])), (0x16, rng.randrange(12)), (0x14, rng.randrange(31)), (0x12, rng.randrange(24)),
                             (0x08, year - 1), (0x10, year - 1), (0x0E, 0), (0x0C, 0), (0x0A, 0)):
                    struct.pack_into("<H", rec, o, v & 0xFFFF)
                return {"s": "event+", "hex": rec.hex()}
            return {"s": "event-", "i": rng.randrange(max(1, len(s.events)))}
        p = rng.randrange(self.places) if rng.random() < 0.5 else self.location_now()
        name, o, w, dom = rng.choice(LOC_BYTES)
        return {"s": "place", "i": p, "o": o, "hex": self.value(dom).to_bytes(w, "little").hex()}

    def location_now(self) -> int:
        v = struct.unpack_from("<h", self.sav.head, 0x7C)[0]
        return v if 0 <= v < self.places else 0

    def scenario(self) -> list:
        """Several edits that go together."""
        return [self.gen_op() for _ in range(self.rng.choice([2, 3, 4, 6]))]

    def gen_args(self):
        return [g(self.rng, self) for g in self.argspec]

    def edited(self, ops):
        s = Save(self.st.sav)
        for op in ops:
            if op["s"] != "ds":
                s.apply(op)
        return s.build()

    def derive(self, args, ops):
        """Arguments that follow the state (the month the monthly update is called for is the date's).
        R029+0x281C: its callers (R029+0x5537, R029+0x55CD) increment month-now DS:00E4 and pass it, and the function reads DS:00E4
        itself for the birthdays: the argument is the loaded month (DS:00E4 = the header's), never another (12 would need DS:00E4 = 12)."""
        if self.coord == "R029+0x281C":
            head = bytearray(self.st.sav[:HEAD_END])
            for op in ops:
                if op["s"] == "head":
                    head[op["o"]:op["o"] + len(op["hex"]) // 2] = bytes.fromhex(op["hex"])
            return [struct.unpack_from("<H", head, 0x6A)[0]]
        return args

    def run(self, args, ops, saves=False) -> dict:
        args = self.derive(args, ops)
        ds = [(op["o"], bytes.fromhex(op["hex"])) for op in ops if op["s"] == "ds"]
        return self.st.call(self.coord, args, sav_edit=self.edited(ops), ds_edits=ds, ctx=CTX.get(self.coord, ()), saves=saves)

    def novelty(self, r) -> int:
        n = 0
        for k, v in r["coverage"]["insns"].items():
            if k in self.records:
                n += len(set(v) - self.seen_i.setdefault(k, set()))
        for k, v in r["coverage"]["edges"].items():
            if k in self.records:
                n += len({tuple(e) for e in v} - self.seen_e.setdefault(k, set()))
        return n

    def absorb(self, r):
        for k, v in r["coverage"]["insns"].items():
            if k in self.records:
                self.seen_i.setdefault(k, set()).update(v)
        for k, v in r["coverage"]["edges"].items():
            if k in self.records:
                self.seen_e.setdefault(k, set()).update(tuple(e) for e in v)

    def fuzz(self, iterations: int):
        rng = self.rng
        corpus = [{"args": self.gen_args(), "ops": []}]
        kept = []
        probes_left = self.probes
        t0 = time.time()
        for it in range(iterations):
            base = rng.choice(corpus) if rng.random() < 0.7 else corpus[-1]
            ops = list(base["ops"])
            args = list(base["args"])
            kind = rng.random()
            if it == 0:
                ops, args = [], self.gen_args()           # the state as it is
            else:
                if kind < 0.35:
                    args = self.gen_args()
                if kind > 0.15 or not ops:
                    if rng.random() < 0.4:
                        ops.extend(self.scenario())
                    else:
                        for _ in range(rng.choice([1, 1, 2, 3])):
                            ops.append(self.gen_op())
                if len(ops) > 50:
                    ops = ops[-50:]
            try:
                r = self.run(args, ops)
            except Exception as e:      # a machine fault of the original on odd bytes: not an input
                self.errors[(type(e).__name__ + " " + str(e))[:70]] = self.errors.get((type(e).__name__ + " " + str(e))[:70], 0) + 1
                if type(e).__name__ in ("UcError", "OSError"):
                    self.st.rebuild()
                continue
            if r["error"]:
                self.errors["stopped: " + r["error"][:60]] = self.errors.get("stopped: " + r["error"][:60], 0) + 1
                continue
            novel = self.novelty(r)
            probe = not novel and probes_left > 0 and rng.random() < 0.15
            if novel or probe:
                probes_left -= 0 if novel else 1
                self.absorb(r)
                r = self.run(args, ops, saves=True)           # the saved games before and after (only the kept runs need them)
                if r["error"] or "sav_after" not in r:
                    continue
                kept.append({"args": self.derive(args, ops), "derived": True, "ops": ops, "ax": r["ax"], "dx": r["dx"], "random": r["random"], "random0": r["random0"], "draws": r["draws"], "seeds": r["seeds"],
                             "sav_before": b64(r["sav_before"]), "sav_after": b64(r["sav_after"]) if r.get("sav_after") not in (None, r["sav_before"]) else None,
                             "ds": r["ds"], "changed": {str(a): v for a, v in r["changed"].items() if any(lo <= a < lo + n for lo, n in WATCH.get(self.coord, ()))}, "seconds": r["seconds"], "instructions": r["instructions"], "coverage": r["coverage"], "iteration": it, "novel": bool(novel)})
                corpus.append({"args": args, "ops": ops})
        self.elapsed = time.time() - t0
        return kept


def b64(data: bytes) -> str:
    return base64.b64encode(gzip.compress(data, 6)).decode()


# ---- argument generators: (rng, fuzzer) -> word
def who_(rng, f):
    return rng.choice([0xFFFE, 0xFFFF, 0, 1, 2, 3, 4, rng.randrange(5), rng.randrange(5)])


def member_(rng, f):
    return rng.randrange(5)


def code_(rng, f):
    return rng.choice(f.codes + [rng.randrange(256)])


def mask_lo(rng, f):
    return rng.choice([1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000, 0xFFFF, rng.randrange(0x10000)])


def mask_hi(rng, f):
    return rng.choice([0, 0, 0, 1, 2, 4, 0x8000, 0xFFFF, rng.randrange(0x10000)])


def hours_(rng, f):
    return rng.choice([1, 1, 2, 3, 5, 8, 12, 24, 48, 100, 200, 400, 800, 1000])


def month_(rng, f):
    return rng.randrange(12)


def anyword(rng, f):
    return rng.choice([0, 1, 2, 3, 4, 5, 8, 10, 0xFFFF, rng.randrange(0x10000)])


def place_(rng, f):
    return rng.choice([rng.randrange(f.places), rng.randrange(f.places), f.location_now(), 0xFFFF])


def kind_(rng, f):
    return rng.choice(f.ekinds if f.ekinds and rng.random() < 0.6 else EVENT_KINDS + [rng.randrange(0x70), 0xFFFF])


def small_(rng, f):
    return rng.choice([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0xFFFF, 0xFFFE])


def type_(rng, f):
    return rng.choice(f.etypes if f.etypes and rng.random() < 0.6 else [0, 1, 2, 3, 4, 5, 8, 0x1C, 0xFFFF])


def owner_(rng, f):
    return rng.choice(f.eowners if f.eowners and rng.random() < 0.6 else [0xFFFE, 0xFFFF, 0, 1, 2, 3, 4])


def tag_(rng, f):
    return rng.choice(f.etags if f.etags and rng.random() < 0.6 else [0, 1, 2, 3, 4, 5, 7, 10, 0xFFFF])


def evalue_(rng, f):
    return rng.choice(f.evalues if f.evalues and rng.random() < 0.5 else [0, 1, 2, 5, 100, 0xFFFF])


def eloc_(rng, f):
    return rng.choice(f.elocs if f.elocs and rng.random() < 0.6 else [rng.randrange(f.places), f.location_now(), 0xFFFF])


def amount_(rng, f):
    return rng.choice(POOL)


def marker_x(rng, f):
    base = struct.unpack_from("<H", f.st.base, 0x2866)[0]
    return min(max(base + rng.choice([-24, -16, -8, -4, -2, -1, 0, 0, 1, 2, 4, 8, 16, 24]), 0xB), 0xF7)


def marker_y(rng, f):
    base = struct.unpack_from("<H", f.st.base, 0x2868)[0]
    return min(max(base + rng.choice([-12, -8, -4, -2, -1, 0, 0, 1, 2, 4, 8, 12]), 0xD), 0xB1)


def terrain_(rng, f):
    return rng.choice(list(range(0, 30)) + [50, 0xFFFF])


FUNCTIONS = {
    "R030+0x1300": [marker_x, marker_y],
    **{c: [lambda r, f: r.choice([0, 1, 2, 3, 4, 0xFFFF]), lambda r, f: 0, lambda r, f: 8] for c in
       ("R039+0x501E", "R039+0x4AD0", "R039+0x8EAC", "R039+0x55B2", "R039+0x619A")},
    "R030+0x25BE": [],
    "R030+0x2DD6": [],
    "R030+0x2AC8": [terrain_],
    "R030+0x2D44": [terrain_, lambda r, f: r.choice([0xFFFF, r.randrange(f.places), r.randrange(f.places), 0x19E])],
    "R029+0x0F54": [who_, mask_lo, mask_hi],
    "R029+0x0C76": [who_, code_],
    "R029+0x0DD8": [who_, mask_lo, mask_hi],
    "R029+0x255A": [hours_],
    "R029+0x281C": [month_],
    "R029+0x2E74": [owner_, eloc_, kind_, type_, evalue_, tag_, lambda r, f: r.randrange(4)],
    "R029+0x37E2": [kind_, eloc_],
    "R029+0x3AEC": [kind_, type_, eloc_],
    "R029+0x3C28": [kind_, type_, owner_, tag_, anyword],
    "R029+0x0792": [who_, amount_],
    "R029+0x1260": [lambda r, f: r.choice([0xFFFE, 0xFFFF, 0, 1, 2, 3, 4]), lambda r, f: r.randrange(160)],
    "R029+0x3878": [kind_, eloc_],
    "R029+0x3350": [small_],
    "R029+0x3404": [small_, small_, place_],
    "R029+0x2B36": [kind_, hours_],
    "R029+0x2A32": [kind_, small_, hours_],
    "R029+0x2D5C": [lambda r, f: r.choice([0xFFFE, 0, 1, 2]), place_, small_, kind_, small_, anyword, small_, hours_, lambda r, f: r.randrange(2), small_],
    "R029+0x38D2": [kind_, type_], "R029+0x392C": [kind_, type_, eloc_], "R029+0x3A20": [kind_, eloc_], "R029+0x3996": [kind_, owner_],
    "R029+0x39F0": [kind_], "R029+0x3A86": [kind_, type_], "R029+0x3B62": [kind_, type_, eloc_, owner_, tag_, anyword],
    "R029+0x3CDE": [kind_, type_, eloc_, owner_, tag_], "R029+0x353E": [tag_, owner_, evalue_], "R029+0x35AA": [tag_, type_],
    "R029+0x360C": [tag_, type_, eloc_], "R029+0x3676": [tag_], "R029+0x37B8": [kind_], "R029+0x08F2": [who_, lambda r, f: r.randrange(7), lambda r, f: r.choice([0, 10, 25, 50, 100, 150])],
    "R029+0x4C04": [lambda r, f: r.choice([0xFFFE, 0xFFFF, 0, 1, 2, 3, 4]), lambda r, f: r.randrange(7), lambda r, f: r.choice([0, 1, 2, 5, 10, 20, 0xFFFB, 0xFFF6]), lambda r, f: r.choice([1, 6, 12, 24, 48])],
    "R029+0x4AA4": [small_],
    "R039+0x0BF6": [],
    "R039+0x0D46": [],
    "R039+0x0EBE": [lambda r, f: r.choice([0xFFFF, 1, 0]), lambda r, f: r.choice([0xFFFF, 1, 0]), lambda r, f: r.choice([0xFFFF, 1, 0])],
    "R039+0x1C22": [],
    "R039+0x1712": [lambda r, f: r.choice([0, 3, 4, 5, 6, 7, 8, 10, 1, 2, 9, 11])],
    "R029+0x54D8": [hours_],
    "R029+0x5590": [lambda r, f: r.choice([1, 1, 2, 3, 7, 10, 30, 60])],
}


# functions whose every caller runs in a city (location-now a place): not called from a state in the open country.
# R039+0x0EBE: its 36 call sites (thunk 09C0:217F) are all in the uprising cards R065, R068, R074, R076, R089, R090, R091.
IN_A_CITY = {"R039+0x0EBE"}
# functions that read the map view's buffer: the party stays where the view is
MAP_STATE = {"R030+0x1300", "R030+0x25BE"}
# DS words the jobs carry for the rewrite (read just before the call): the map view's registers, the travel counters ...
# DS bytes a function writes that the jobs keep (offset, length): what the rewrite is asked to reproduce besides the saved game
WATCH = {"R030+0x2DD6": [(0x2860, 2), (0xE48C, 768)]}
CTX = {"R030+0x2DD6": [0xE2, 0xE4, 0x2860] + [0xE48C + 2 * k for k in range(384)], "R030+0x1300": [0x2866, 0x2868, 0x286A, 0x286C, 0x286E, 0x8672, 0xE488, 0xD843]}
# globals the saved game does not hold that a function reads, to change: (offset, width, values)
DS_EDITS = {"R030+0x1300": [(0x8672, 2, [0, 10, 30, 50, 55, 58, 59, 60, 100, 150]), (0xE488, 2, [0, 1, 5, 9, 10, 11]),
                            (0xD843, 2, list(range(0, 28)))]}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("states")
    ap.add_argument("out")
    ap.add_argument("--functions", required=True)
    ap.add_argument("--states-list", dest="state_names", required=True)
    ap.add_argument("--iterations", type=int, default=150)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--records", default="R029,R030,R039,R043,root")
    ap.add_argument("--baseline", default=None)
    a = ap.parse_args()
    baseline = pickle.load(open(a.baseline or Path(local_dir()) / "batch" / "worldcalls" / "baseline.pkl", "rb"))
    records = set(a.records.split(","))
    funcs = a.functions.split(",")
    out = Path(a.out)
    for name in a.state_names.split(","):
        st = wc.WState(Path(a.states) / (name + ".state"), near=funcs)
        for coord in funcs:
            folder = out / coord.replace("+", "_")
            if (folder / (name + ".json")).exists():
                continue
            if coord in IN_A_CITY and struct.unpack_from("<h", st.sav, 0x7C)[0] < 0:
                print(coord, name, "skipped: called only in a city, the state is in the open country (location-now -1)", flush=True)
                continue
            rng = random.Random("%s|%s|%d" % (name, coord, a.seed))
            try:
                cpu = time.process_time()
                fz = Fuzzer(st, coord, FUNCTIONS[coord], rng, baseline, records)
                kept = fz.fuzz(a.iterations)
                fz.cpu = time.process_time() - cpu
            except Exception as e:
                print(coord, name, "FAILED", type(e).__name__, str(e)[:200], flush=True)
                st = wc.WState(Path(a.states) / (name + ".state"), near=funcs)
                continue
            (folder / "cov").mkdir(parents=True, exist_ok=True)
            (folder / (name + ".json")).write_text(json.dumps({"state": name, "function": coord, "kept": kept, "seconds": fz.elapsed, "cpu": fz.cpu, "iterations": a.iterations}), encoding="utf-8")
            with open(folder / "cov" / ("cov-%s.jsonl" % name), "w", encoding="utf-8") as f:
                for item in kept:
                    f.write(json.dumps({"unit": coord, "state": "%s#%d" % (name, item["iteration"]), "seconds": item["seconds"], **item["coverage"]}) + "\n")
            print(coord, name, "kept", len(kept), "of", a.iterations, "%.0f s wall, %.0f s cpu" % (fz.elapsed, fz.cpu), ("errors " + json.dumps(fz.errors)) if fz.errors else "", flush=True)


if __name__ == "__main__":
    main()
