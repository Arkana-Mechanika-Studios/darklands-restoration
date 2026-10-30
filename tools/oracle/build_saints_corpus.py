"""Original-byte corpus for the saints: the saint table, each saint's table values and chance,
and the help of many saints.

usage: python -I build_saints_corpus.py <DARKLAND.EXE> <out saints.json>

* "table": saint-handler-tables (R136+0xAFC7, 136 far pointers to RTLink thunks): the R039 function each
  saint number reaches, read from the thunk's far jump.
* "values": what = 0..6 of every saint function (the two unlisted ones too).
* "chance": what = 7 with generated Virtue, favor and party (St.Agnes asks for able women).
* "help": what = 8 executed with its whole call tree from the original bytes, except event-create and
  event-here-test (stubbed and recorded: they allocate event records through DOS). Party records,
  purse, bankers' standings, saints-learned, locations and the random state are seeded; every
  changed byte is recorded.

Only results are stored; no game bytes.
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
from run_unit import Rtlink, run  # noqa: E402
from build_rules_corpus import (DS, HOLDRAND, LOCATION_NOW, PARTY_MAP_COLUMN, PARTY_MAP_ROW, LEADER, PARTY_HEADCOUNT, REPORT, PARTY,  # noqa: E402
                                LOC_SEG, LOC_SIZE, ds, h16, locations, w)

SAINT_SEG = 0x165C
SECTIONS = [136, 29, 39]
SAINT_MSGS = 0x01BD
PURSE = 0x906B
FUGGER, MEDICI = 0x4BB6, 0x4BB8
SAINTS_KNOWN_SEG, SAINTS_KNOWN_OFF = 0x2E38, 0x082C
UNLISTED = [0x75D8, 0x7812]
# Saints whose help is executed: no equipment or item checks (gear-bonus-sums, item-tally-by-flags read the
# item list, which is not seeded), no closest-city (the party is always at a location), and not
# St.John Nepomuk in Prague (an uninitialised location). Saints that read the member record directly
# (+0x03, +0x41, saints-learned) are not asked for −1: the original reads before the party there.
DIRECT = {1, 20, 27, 30, 45, 75, 90, 103, 104}
HELP = [0, 1, 2, 3, 4, 6, 7, 9, 12, 13, 14, 20, 23, 27, 28, 30, 38, 41, 44, 45, 50, 57, 59, 62, 64, 73, 74, 75, 76,
        77, 78, 90, 94, 99, 100, 103, 104, 105, 108, 109, 110, 112, 113, 114, 122, 125, 135]


def saint_table(exe: bytes) -> list[int]:
    rt = Rtlink(exe)
    mem = bytearray(0x100000)
    mem[0:len(rt.root)] = rt.root
    for idx in SECTIONS:
        s = rt.sections[idx]
        img = exe[s["file_offset"]:s["file_offset"] + s["length"]]
        mem[s["load_seg"] * 16:s["load_seg"] * 16 + len(img)] = img
    seg = struct.unpack_from("<H", mem, DS * 16 + 0x7E2C)[0]
    offsets = []
    for i in range(136):
        off, sg = struct.unpack_from("<HH", mem, seg * 16 + 0x2937 + 4 * i)
        thunk = mem[sg * 16 + off:sg * 16 + off + 10]
        assert thunk[0] == 0xE8 and thunk[3] == 0xEA, thunk.hex()
        target_off, target_seg = struct.unpack_from("<HH", thunk, 4)
        assert target_seg == SAINT_SEG and thunk[8] == 39, thunk.hex()
        offsets.append(target_off)
    return offsets


def party(r, size=5):
    members, memory = [], []
    for i in range(5):
        rec = bytearray(0x80)
        rec[0] = r.choice([1, 1, 1, 1, 0, 2, 5]) if i < size else 0
        rec[3] = r.randint(0, 1)
        rec[0x10] = r.randint(0, 255)
        rec[0x3B], rec[0x3C] = r.choice([25, 100, 120, 127]), r.randint(0, 127)
        rec[0x41] = r.randint(0, 255)
        for k in range(7):
            rec[0x50 + k] = r.randint(1, 99)
            rec[0x49 + k] = r.randint(1, rec[0x50 + k])   # at most the maximum (see SaintsOracleTests)
        for k in range(19):
            rec[0x57 + k] = r.choice([0, 1, 15, 50, 98, 99, r.randint(0, 99)])
        members.append(rec.hex())
        memory.append(ds(PARTY + 0x80 * i, rec.hex()))
    return members, memory


def cases(r, table):
    out = []
    for off in table + UNLISTED:
        for what in range(7):
            out.append(("values", off, [h16(0), h16(0), h16(what)], [], {"offset": off, "what": what}))
    for saint, off in enumerate(table):
        for _ in range(4):
            size = r.randint(1, 5)
            members, memory = party(r, size)
            who, x = r.randint(0, size - 1), r.choice([0, r.randint(0, 99), r.randint(0, 99)])
            memory += [ds(PARTY_HEADCOUNT, w(size))]
            out.append(("chance", off, [h16(who), h16(x), h16(7)], memory,
                        {"saint": saint, "members": members, "size": size, "who": who, "x": x}))
    for saint in HELP:
        for _ in range(6):
            size = r.randint(1, 5)
            members, memory = party(r, size)
            who = r.choice(list(range(size)) + ([] if saint in DIRECT else [-1]))
            leader = r.randint(0, size - 1)
            location_now = r.choice([0x05, 0x45, 0x36, 0x25, 0x55, 0x4C, 2, 150] + ([] if saint == 78 else [0x46]))
            recs, locs = locations(r, 2, location_now)
            state = r.getrandbits(32)
            purse = [r.choice([0, 0, 3, 300, 1000]), r.choice([0, 0, 7, 19]), r.randint(0, 11)]
            bankers = [r.randint(-50, 50), r.randint(-50, 50)]
            known = bytes(r.randint(0, 255) for _ in range(5 * 0x14))
            memory += locs + [ds(LEADER, "%02X" % leader), ds(PARTY_HEADCOUNT, w(size)), ds(REPORT, w(0)),
                              ds(SAINT_MSGS, "00"), ds(LOCATION_NOW, w(location_now)),
                              ds(HOLDRAND, struct.pack("<I", state).hex()),
                              ds(PURSE, struct.pack("<hhh", *purse).hex()),
                              ds(FUGGER, struct.pack("<hh", *bankers).hex()),
                              {"segment": "%04X" % SAINTS_KNOWN_SEG, "offset": "%04X" % SAINTS_KNOWN_OFF, "hex": known.hex()}]
            out.append(("help", table[saint], [h16(who), h16(0), h16(8)], memory,
                        {"saint": saint, "members": members, "size": size, "who": who, "leader": leader,
                         "curLoc": location_now, "locations": recs, "state": state, "purse": purse,
                         "bankers": bankers, "known": known.hex()}))
    return out


def arguments(call) -> list[int]:
    """The callee's arguments: R029's own near calls are `push cs; call`, so the first word is CS."""
    words = [int(a, 16) for a in call["args"]]
    if words[0] == 0x0E76:
        words = words[1:]
    return words[:10 if call["callee"] == "event-create" else 2]


def run_case(exe: bytes, function: str, off: int, args: list, memory: list, inputs: dict, extra: dict | None = None):
    """One case run on the original: (the corpus case, the loaded images)."""
    spec = {"unit": "saint_%04x" % off, "entry": {"segment": "%04X" % SAINT_SEG, "offset": "%04X" % off},
            "sections": SECTIONS, "ds": DS, "memory": memory, "args": args, "follow": True,
            "stub_targets": {"0E76:2C4E": "event-create", "0E76:2930": "event-here-test"},
            "arg_words": {"event-create": 11, "event-here-test": 3},
            "track_all": function == "help"}
    if extra:   # stubs a flipped case adds (the party in the open country: closest-city)
        spec["stub_targets"] = {**spec["stub_targets"], **extra.get("stub_targets", {})}
        spec["results"] = extra["results"]
    try:
        res = run(exe, spec)
    except SystemExit as e:
        raise SystemExit("%s %04x %s: %s" % (function, off, inputs.get("saint"), e))
    case = {"function": function, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
            "changed": {c["linear"]: c["after"] for c in (res["memChanged"] or [])}}
    if function == "help":
        case["events"] = [[c["callee"]] + arguments(c) for c in res["calls"] if c["callee"] in ("event-create", "event-here-test")]
    return case, res["loadedImages"]


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    table = saint_table(exe)
    result, images = [], None
    for function, off, args, memory, inputs in cases(r, table):
        case, images = run_case(exe, function, off, args, memory, inputs)
        result.append(case)
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % PARTY, "holdrand": "%04X" % HOLDRAND,
        "locationSegment": "%04X" % LOC_SEG, "locationSpacing": "%04X" % LOC_SIZE,
        "saintTable": ["R039+0x%04X" % ((0x165C - 0x1462) * 16 + off) for off in table],   # record 39 is linked at 1462
        "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
