"""Original-byte corpus for the personal-name tables (overlay R029).

usage: python -I -B build_names_corpus.py <DARKLAND.EXE> <out names.json>

Runs, from the unchanged EXE (the data image R136 holds the name tables):
  random_name   R029+0x5CC4  (seed, kind 0..6, slot) -> the name left in var_named[slot] (2E38:1A96 + 30 slot)
  name_to_slot  R029+0x1FB8  kinds 6, 7, 8 (index) -> the string left in var_named[slot]
The C run-time (srand, rand, strcpy, strcat) runs for real; time() is replaced by a scripted clock.
Only inputs and results are stored; no game text: a name is stored as its length and SHA-256.
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
PARTY_SEED = 0x9C4A
FAR, NAMED, NAMED_SIZE = 0x2E38, 0x1A96, 30
TIME = "06A1:23A8"
SECTIONS = [136, 29]


def w(v: int) -> str:
    return struct.pack("<H", v & 0xFFFF).hex()


def named(slot: int) -> dict:
    return {"segment": "%04X" % FAR, "offset": "%04X" % (NAMED + NAMED_SIZE * slot), "length": NAMED_SIZE}


def digest(raw: bytes):
    text = raw.split(b"\0", 1)[0]
    return len(text), hashlib.sha256(text).hexdigest()


def random_name_case(exe, r):
    seed, party, kind, slot, clock = r.randint(0, 0xFFFF), r.randint(0, 0xFFFF), r.choice([0, 1, 2, 3, 4, 5, 6, 7]), \
        r.randint(0, 4), r.randint(0, 0xFFFF)
    res = run(exe, {
        "unit": "random_name", "entry": {"segment": "1367", "offset": "0DB4"}, "sections": SECTIONS, "ds": DS,
        "args": ["%04X" % seed, "%04X" % kind, "%04X" % slot], "follow": True,
        "memory": [{"offset": "%04X" % PARTY_SEED, "hex": w(party)}],
        "stub_targets": {TIME: "time"}, "results": {"time": [{"ax": clock, "dx": 0}]},
        "read": [named(slot)]})
    length, sha = digest(bytes.fromhex(res["reads"][0]))
    return {"function": "random_name", "inputs": {"seed": seed, "party": party, "kind": kind, "slot": slot, "clock": clock},
            "length": length, "sha256": sha, "ax": res["final"]["ax"], "changed": {}}


def name_to_slot_case(exe, r):
    kind, count = r.choice([(6, 16), (7, 38), (8, 31)])
    index, slot = r.randint(0, count - 1), r.randint(0, 4)
    res = run(exe, {
        "unit": "name_to_slot", "entry": {"segment": "0E76", "offset": "1FB8"}, "sections": SECTIONS, "ds": DS,
        "args": ["%04X" % kind, "%04X" % index, "%04X" % slot], "follow": True, "read": [named(slot)]})
    length, sha = digest(bytes.fromhex(res["reads"][0]))
    return {"function": "name_to_slot", "inputs": {"kind": kind, "index": index, "slot": slot}, "length": length, "sha256": sha, "ax": res["final"]["ax"], "changed": {}}


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261018)
    cases = [random_name_case(exe, r) for _ in range(400)]
    cases += [name_to_slot_case(exe, r) for _ in range(150)]
    corpus = {"authority": "test_only_original_byte_execution", "source": "tools/oracle/build_names_corpus.py",
              "dataSegment": "%04X" % DS, "scratchA": "0000", "holdrand": "%04X" % HOLDRAND, "cases": cases}
    Path(sys.argv[2]).write_text(json.dumps(corpus, separators=(",", ":")) + "\n")
    print(len(cases), "cases")


if __name__ == "__main__":
    main()
