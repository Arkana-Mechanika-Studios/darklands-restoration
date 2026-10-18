"""Original-byte corpus for the battle file's compression: the PKWARE DCL 1.02 implode and explode the battle-state
overlay links in (record 3, frame 1016), run from the unchanged EXE bytes on generated data.

usage: python -I build_implode_corpus.py <DARKLAND.EXE> <out implode.json>

implode (1016:04C6, far pascal (read, write, work, &type, &size)) reads through the overlay's memory reader 0E76:0000
(DS:D90F / DS:D919) and writes through its memory writer 0E76:006A (DS:D913 / DS:D917), so the whole call runs from original
bytes; the compressed bytes are read back from the output segment. The work area is filled with a repeated pattern before
the call (the original's buffer is not cleared between calls: the search may read past the end of the data). explode
(1016:0D07, (read, write, work)) runs on the case's compressed data and on damaged copies (cut, wrong header), for its
return codes. Only inputs and results are stored; no game bytes.
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
SECTIONS = [3, 136]
ARGS = 0x4F00      # type and size words
INPUT = 0x5000     # data to compress / compressed data
WORK = 0x6000      # the library's work area (0x89B8 bytes for implode)
OUTPUT = 0x7000    # what the writer collects (up to 64 KB)
READER, WRITER = 0x0000, 0x006A   # 0E76:0000 memory reader, 0E76:006A memory writer
SOURCE, TARGET, WRITTEN, LEFT = 0xD90F, 0xD913, 0xD917, 0xD919   # DS: the block read, the output, the count written, what is left
IMPLODE_WORK = 0x89B8


def w(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def common_memory(data: bytes, stale: bytes, work_size: int) -> list:
    mem = [{"segment": "%04X" % INPUT, "offset": "0000", "hex": data.hex()} if data else None,
           {"offset": "%04X" % SOURCE, "hex": struct.pack("<HH", 0, INPUT).hex()},
           {"offset": "%04X" % LEFT, "hex": struct.pack("<H", len(data)).hex()},
           {"offset": "%04X" % TARGET, "hex": struct.pack("<HH", 0, OUTPUT).hex()},
           {"offset": "%04X" % WRITTEN, "hex": "0000"}]
    if stale:
        mem.append({"segment": "%04X" % WORK, "offset": "0000", "hex": (stale * (work_size // len(stale) + 1))[:work_size].hex()})
    return [m for m in mem if m]


def output_of(res: dict) -> bytes:
    size = struct.unpack("<H", bytes.fromhex(res["reads"][0]))[0]
    return bytes.fromhex(res["reads"][1])[:size]


def implode(exe: bytes, data: bytes, ctype: int, dsize: int, stale: bytes) -> tuple[int, bytes]:
    mem = common_memory(data, stale, IMPLODE_WORK)
    mem.append({"segment": "%04X" % ARGS, "offset": "0000", "hex": struct.pack("<HH", ctype, dsize).hex()})
    # pascal: the first argument is pushed first, so the last one is nearest the return address
    args = [w(2), w(ARGS), w(0), w(ARGS), w(0), w(WORK), w(WRITER), "0E76", w(READER), "0E76"]
    res = run(exe, {"unit": "implode", "entry": {"segment": "1016", "offset": "04C6"}, "sections": SECTIONS, "ds": DS,
                    "memory": mem, "args": args, "follow": True, "max_instructions": 60_000_000,
                    "read": [{"segment": "%04X" % DS, "offset": "%04X" % WRITTEN, "length": 2},
                             {"segment": "%04X" % OUTPUT, "offset": "0000", "length": 0xFFF0}]})
    return int(res["final"]["ax"], 16), output_of(res)


def explode(exe: bytes, packed: bytes) -> tuple[int, bytes]:
    mem = common_memory(packed, b"", 0)
    args = [w(0), w(WORK), w(WRITER), "0E76", w(READER), "0E76"]
    res = run(exe, {"unit": "explode", "entry": {"segment": "1016", "offset": "0D07"}, "sections": SECTIONS, "ds": DS,
                    "memory": mem, "args": args, "follow": True, "max_instructions": 30_000_000,
                    "read": [{"segment": "%04X" % DS, "offset": "%04X" % WRITTEN, "length": 2},
                             {"segment": "%04X" % OUTPUT, "offset": "0000", "length": 0xFFF0}]})
    return int(res["final"]["ax"], 16), output_of(res)


def generate(r: random.Random, kind: str, n: int) -> bytes:
    if kind == "random":
        return bytes(r.randrange(256) for _ in range(n))
    if kind == "few":       # a small alphabet: many short repetitions
        alphabet = [r.randrange(256) for _ in range(r.randint(2, 6))]
        return bytes(r.choice(alphabet) for _ in range(n))
    if kind == "runs":      # long runs of one byte (repetitions at distance 1 and the longest lengths)
        out = bytearray()
        while len(out) < n:
            out += bytes([r.randrange(256)]) * r.randint(1, 700)
        return bytes(out[:n])
    if kind == "text":
        words = [bytes(r.choice(b"etaoinshrdlu") for _ in range(r.randint(2, 8))) for _ in range(30)]
        out = bytearray()
        while len(out) < n:
            out += r.choice(words) + r.choice([b" ", b", ", b".\r\n"])
        return bytes(out[:n])
    if kind == "records":   # fixed-size records that differ in a few bytes (like the battle's tables and levels)
        rec = bytearray(r.randrange(256) for _ in range(r.choice([13, 26, 30, 0x80, 0x122])))
        out = bytearray()
        while len(out) < n:
            for _ in range(r.randint(0, 3)):
                rec[r.randrange(len(rec))] = r.randrange(256)
            out += rec
        return bytes(out[:n])
    if kind == "sparse":    # mostly zeros with scattered values (floor images)
        out = bytearray(n)
        for _ in range(n // 9):
            out[r.randrange(n)] = r.randrange(256)
        return bytes(out)
    raise ValueError(kind)


def cases(r: random.Random) -> list:
    out = []
    plan = [("empty", 0, 0x800, 0), ("random", 1, 0x800, 0), ("few", 3, 0x400, 0), ("text", 40, 0x800, 1)]
    for kind in ("random", "few", "runs", "text", "records", "sparse"):
        for _ in range(3):
            out_size = r.choice([r.randint(2, 300), r.randint(300, 2500)])
            plan.append((kind, out_size, r.choice([0x400, 0x800, 0x1000]), r.choice([0, 0, 1])))
    # past one 0x1000 load (the dictionary moves), and past 0x800 bytes of output (the writer is called mid-way)
    for kind, n in (("records", 0x1978), ("sparse", 0x33FC + 31), ("random", 0x1100), ("few", 0x2400), ("runs", 0x1800),
                    ("text", 0x1A00)):
        plan.append((kind, n, 0x800, 0))
    plan += [("text", 0x1300, 0x400, 1), ("records", 0x2100, 0x1000, 0), ("random", 50, 0x300, 0), ("random", 50, 0x800, 2)]
    for kind, n, dsize, ctype in plan:
        data = b"" if kind == "empty" else generate(r, kind, n)
        stale = bytes(r.randrange(256) for _ in range(r.choice([1, 7, 16]))) if r.random() < 0.6 else b"\0"
        out.append({"kind": kind, "type": ctype, "dictionary": dsize, "data": data, "stale": stale})
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261018)
    result = []
    for c in cases(r):
        code, packed = implode(exe, c["data"], c["type"], c["dictionary"], c["stale"])
        entry = {"function": "implode", "inputs": {"kind": c["kind"], "type": c["type"], "dictionary": c["dictionary"],
                                                  "data": c["data"].hex(), "stale": c["stale"].hex()},
                 "ax": w(code), "changed": {}, "packed": packed.hex()}
        result.append(entry)
        if code != 0 or not packed:
            continue
        ecode, plain = explode(exe, packed)
        result.append({"function": "explode", "inputs": {"packed": packed.hex()}, "ax": w(ecode), "changed": {},
                       "plain": plain.hex()})
        if len(result) % 5 == 0:
            for damaged in (packed[:max(1, len(packed) // 2)], packed[:4], bytes([packed[0], 7]) + packed[2:],
                            bytes([2]) + packed[1:]):
                ecode, plain = explode(exe, damaged)
                result.append({"function": "explode", "inputs": {"packed": damaged.hex()}, "ax": w(ecode), "changed": {},
                               "plain": plain.hex()})
        print(c["kind"], len(c["data"]), "->", len(packed), flush=True)
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "0000", "holdrand": "0000", "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
