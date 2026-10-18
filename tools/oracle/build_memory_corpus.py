"""Original-byte corpus for the rules that read outside their data: the event scans that end at slot 299
(event_remove, loc_facts_clear, event_48_max return what AX holds), set_member_bit with who = -1 and has_item's
formula byte below and above the formula table.

usage: python -I build_memory_corpus.py <DARKLAND.EXE> <out memory.json>

Event records live in a scratch segment and the 300 far pointers of events[] (2E38:1B2C) point at them: slots
0..298 at one filler record of kind 0x50, slot 299 at the case's record. event_free's free_buffer is stubbed (it
would call DOS). Only results are stored; no game bytes.
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
DATA = 0x2E38                     # far data frame (DS:7E30 and DS:7E1E hold it)
EVENTS = 0x1B2C                   # events[] far pointers in 2E38
REC_SEG = 0x8000                  # scratch segment for event records
FILLER, LAST = 0x0000, 0x0040     # filler record and slot 299's record
FN = {
    "event_remove": ("0E76", "3cde"), "loc_facts_clear": ("0E76", "3d92"), "event_48_max": ("0E76", "4e0c"),
    "set_member_bit": ("0E76", "1302"), "has_item": ("0E76", "118e"),
}
FIELDS = {"type": 0x00, "owner": 0x1A, "location": 0x1C, "value": 0x1E, "kind": 0x22, "tag": 0x28}


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def record(ev: dict) -> str:
    b = bytearray(0x30)
    for k, off in FIELDS.items():
        struct.pack_into("<h", b, off, ((ev.get(k, 0) + 0x8000) & 0xFFFF) - 0x8000)
    return b.hex()


def table() -> str:
    t = bytearray()
    for i in range(300):
        t += struct.pack("<HH", LAST if i == 299 else FILLER, REC_SEG)
    return t.hex()


def event_memory(ev: dict) -> list:
    return [{"segment": "%04X" % REC_SEG, "offset": "%04X" % FILLER, "hex": record({"kind": 0x50})},
            {"segment": "%04X" % REC_SEG, "offset": "%04X" % LAST, "hex": record(ev)},
            {"segment": "%04X" % DATA, "offset": "%04X" % EVENTS, "hex": table()}]


def pick(r, real):
    """-1 (any) a third of the time, else the event's value or another."""
    x = r.random()
    return -1 if x < 0.34 else real if x < 0.75 else real + r.randint(1, 3)


def cases(r):
    out = []
    for _ in range(120):
        ev = {"kind": r.choice([0x40, 0x41]), "type": r.randint(0, 5), "location": r.randint(0, 20),
              "owner": r.randint(-2, 4), "tag": r.randint(0, 9), "value": r.randint(0, 30)}
        a = [ev["kind"], pick(r, ev["type"]), pick(r, ev["location"]), pick(r, ev["owner"]), pick(r, ev["tag"])]
        out.append(("event_remove", event_memory(ev), [h16(x) for x in a], {"event": ev, "args": a}))
    for _ in range(80):
        ev = {"kind": r.choice([0x1C, 0x08]), "type": r.randint(0, 5), "location": r.randint(0, 20),
              "owner": -2, "tag": r.randint(0, 9), "value": r.randint(0, 30)}
        a = [pick(r, ev["tag"]), pick(r, ev["location"])]
        out.append(("loc_facts_clear", event_memory(ev), [h16(x) for x in a], {"event": ev, "args": a}))
    for _ in range(80):
        ev = {"kind": 0x48, "type": r.randint(0, 6), "location": 0, "owner": r.randint(0, 4), "tag": 0,
              "value": r.randint(0, 30)}
        a = [r.choice([ev["owner"], r.randint(0, 4)]), r.choice([ev["type"], r.randint(0, 6)])]
        out.append(("event_48_max", event_memory(ev), [h16(x) for x in a], {"event": ev, "args": a}))
    for who in (-1, 0, 4, -2):
        for bit in (0, 7, 27, 30, 100, 159):
            out.append(("set_member_bit", [], [h16(who), h16(bit)], {"who": who, "bit": bit}))
    # has_item: member who carries nothing; the byte at 2E38:075F + who * 0x16 + code is set to 1 (one case per code)
    for who, code in ((0, 0x47), (0, 0x10), (1, 0x5F), (4, 0x75), (4, 0x80), (2, 0x60)):
        byte = 0x75F + who * 0x16 + code
        mem = [{"segment": "%04X" % DATA, "offset": "%04X" % byte, "hex": "01"}]
        out.append(("has_item", mem, [h16(who), h16(code)], {"who": who, "code": code, "byte": byte}))
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    result, images = [], None
    for name, memory, args, inputs in cases(r):
        seg, off = FN[name]
        res = run(exe, {"unit": name, "entry": {"segment": seg, "offset": off}, "sections": [29, 136], "ds": DS,
                        "memory": memory, "args": args, "follow": True, "track_all": True,
                        "stub_targets": {"05BA:00C2": "free_buffer"}})
        images = res["loadedImages"]
        changed = {c["linear"]: c["after"] for c in res["memChanged"]}
        result.append({"function": name, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                       "changed": changed if name in ("set_member_bit", "has_item") else {},
                       "freed": sum(1 for c in res["calls"] if c["callee"] == "free_buffer")})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % 0, "holdrand": "%04X" % 0x7B20,
        "farData": "%04X" % DATA, "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
