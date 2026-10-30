"""Original-byte corpus for the event table's work file around a battle: cards-teardown writes EVENTS.TMP and
empties the table, world-read reads it back into slots 0..n-1.

usage: python -I build_eventsfile_corpus.py <DARKLAND.EXE> <out eventsfile.json>

cards-teardown (R043+0x054E) and world-read (R043+0x0100) run from the unchanged bytes with every call stubbed: the
DOS and C run-time file calls are served from files kept here in memory (only EVENTS.TMP exists for world-read, so its
other files take their error paths, whose message and exit are stubbed too), alloc hands out fresh segments and
release-work-buffer is recorded. The 300 far pointers of events[] (2E38:1B2C) point at records in a scratch segment. Stored
per case: the table before (record bytes or null per slot), what EVENTS.TMP holds after cards-teardown, and the table
after (per slot: null, "kept" = the pointer that was there, or the bytes of a new record). Records are random bytes;
no game bytes are stored.
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
DATA = 0x2E38                     # far data frame of events[]
EVENTS = 0x1B2C                   # events[] far pointers in 2E38
SLOTS = 300
REC_SEG = 0x7000                  # scratch segment of the table's records (0x30 bytes each, 300 fit)
ALLOC_SEG = 0x8000                # alloc's fresh 0x30-byte blocks (3 paragraphs each)
SECTIONS = [29, 43, 136]
FN = {"R043+0x054E": ("1462", "054e"), "R043+0x0100": ("1462", "0100")}
DOS = {  # far targets of the file calls (R043 and the C run-time in 06A1 / 05BA)
    "06A1:2C6B": "findfirst", "06A1:2C9E": "dos_open", "06A1:2C3D": "dos_creat", "06A1:2CB6": "dos_read",
    "06A1:2CBD": "dos_write", "06A1:2C28": "dos_close", "06A1:1D2A": "fdopen", "06A1:0716": "fread",
    "06A1:1DE2": "fseek", "06A1:05E2": "fclose", "06A1:0900": "printf", "06A1:01D7": "exit",
    "05BA:0004": "alloc", "05BA:00C2": "release-work-buffer",
}


def table_memory(slots: list) -> list:
    mem, ptrs = [], bytearray()
    for i, rec in enumerate(slots):
        if rec is None:
            ptrs += struct.pack("<HH", 0, 0)
        else:
            ptrs += struct.pack("<HH", i * 0x30, REC_SEG)
            mem.append({"segment": "%04X" % REC_SEG, "offset": "%04X" % (i * 0x30), "hex": rec})
    mem.append({"segment": "%04X" % DATA, "offset": "%04X" % EVENTS, "hex": ptrs.hex()})
    return mem


class Files:
    """The files the stubs serve: name -> bytes, open handles -> [name, position]."""

    def __init__(self, files: dict):
        self.files = dict(files)
        self.handles = {}
        self.next_seg = ALLOC_SEG
        self.freed = []

    def hooks(self):
        def string(uc, off):
            b = bytes(uc.mem_read(DS * 16 + off, 64))
            return b.split(b"\0")[0].decode("latin-1").lower()

        def put(uc, off, value):
            uc.mem_write(DS * 16 + off, struct.pack("<H", value & 0xFFFF))

        def findfirst(uc, a):
            return (0 if string(uc, a[0]) in self.files else 0x12), 0

        def dos_open(uc, a):
            name = string(uc, a[0])
            if name not in self.files:
                return 2, 0
            h = 5 + len(self.handles)
            self.handles[h] = [name, 0]
            put(uc, a[2], h)
            return 0, 0

        def dos_creat(uc, a):
            name = string(uc, a[0])
            self.files[name] = b""
            h = 5 + len(self.handles)
            self.handles[h] = [name, 0]
            put(uc, a[2], h)
            return 0, 0

        def dos_read(uc, a):
            if a[0] not in self.handles:
                put(uc, a[4], 0)
                return 6, 0   # invalid handle (a file that was not there)
            name, pos = self.handles[a[0]]
            data = self.files[name][pos:pos + a[3]]
            uc.mem_write(a[2] * 16 + a[1], data)
            self.handles[a[0]][1] = pos + len(data)
            put(uc, a[4], len(data))
            return 0, 0

        def dos_write(uc, a):
            name, pos = self.handles[a[0]]
            data = bytes(uc.mem_read(a[2] * 16 + a[1], a[3]))
            f = self.files[name]
            self.files[name] = f[:pos] + data + f[pos + len(data):]
            self.handles[a[0]][1] = pos + len(data)
            put(uc, a[4], len(data))
            return 0, 0

        def fdopen(uc, a):
            return 0x4000 + a[0], 0   # FILE* = 0x4000 + handle

        def fread(uc, a):
            h = a[3] - 0x4000
            name, pos = self.handles[h]
            data = self.files[name][pos:pos + a[1] * a[2]]
            uc.mem_write(DS * 16 + a[0], data)
            self.handles[h][1] = pos + len(data)
            return len(data) // max(a[1], 1), 0

        def fseek(uc, a):
            self.handles[a[0] - 0x4000][1] = a[1] | a[2] << 16
            return 0, 0

        def alloc(uc, a):
            seg = self.next_seg
            self.next_seg += (a[0] + 15) // 16
            return seg, 0

        def release_work_buffer(uc, a):
            self.freed.append(a[0])
            return 0, 0

        def nothing(uc, a):
            return 0, 0

        return {"findfirst": findfirst, "dos_open": dos_open, "dos_creat": dos_creat, "dos_read": dos_read,
                "dos_write": dos_write, "dos_close": nothing, "fdopen": fdopen, "fread": fread, "fseek": fseek,
                "fclose": nothing, "printf": nothing, "exit": nothing, "alloc": alloc, "release-work-buffer": release_work_buffer}


def table_after(exe, spec, files: Files, before: list):
    res = run(exe, spec, files.hooks())
    after = []
    raw = bytes.fromhex(res["reads"][0])
    recs = res["reads"][1:]
    for i in range(SLOTS):
        off, seg = struct.unpack_from("<HH", raw, 4 * i)
        if off == 0 and seg == 0:
            after.append(None)
        elif seg == REC_SEG and off == i * 0x30 and before[i] is not None:
            after.append("kept")
        else:
            assert off == 0 and seg >= ALLOC_SEG, (i, hex(seg), hex(off))
            after.append(recs[(seg - ALLOC_SEG) // 3])
    return res, after


def spec_for(name: str, slots: list) -> dict:
    seg, off = FN[name]
    reads = [{"segment": "%04X" % DATA, "offset": "%04X" % EVENTS, "length": 4 * SLOTS}]
    reads += [{"segment": "%04X" % (ALLOC_SEG + 3 * k), "offset": "0000", "length": 0x30} for k in range(SLOTS)]
    return {"unit": name, "entry": {"segment": seg, "offset": off}, "sections": SECTIONS, "ds": DS,
            "memory": table_memory(slots), "args": [], "read": reads, "stub_targets": DOS,
            "arg_words": {"dos_read": 5, "dos_write": 5}, "max_instructions": 2000000}


def random_table(r, fill: float) -> list:
    return [r.randbytes(0x30).hex() if r.random() < fill else None for _ in range(SLOTS)]


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261019)
    cases, images = [], None
    shutdowns = [[None] * SLOTS, [r.randbytes(0x30).hex() for _ in range(SLOTS)]]
    shutdowns += [random_table(r, f) for f in (0.02, 0.1, 0.3, 0.5, 0.8, 0.97)]
    for before in shutdowns:
        files = Files({})
        res, after = table_after(exe, spec_for("cards-teardown", before), files, before)
        images = res["loadedImages"]
        tmp = files.files["events.tmp"]
        n = struct.unpack_from("<H", tmp, 0)[0]
        cases.append({"function": "cards-teardown", "inputs": {"table": before}, "ax": "0000", "changed": {},
                      "file": [tmp[2 + 0x30 * k:2 + 0x30 * (k + 1)].hex() for k in range(n)], "count": n,
                      "fileLength": len(tmp), "after": after, "freed": len(files.freed)})
    for n, fill in ((0, 0.2), (1, 0.0), (5, 0.1), (40, 0.3), (40, 0.0), (120, 0.6), (299, 0.05), (300, 0.5), (17, 1.0)):
        battle = random_table(r, fill)
        file = [r.randbytes(0x30).hex() for _ in range(n)]
        files = Files({"events.tmp": struct.pack("<H", n) + b"".join(bytes.fromhex(x) for x in file)})
        res, after = table_after(exe, spec_for("world-read", battle), files, battle)
        cases.append({"function": "world-read", "inputs": {"table": battle, "file": file}, "ax": "0000", "changed": {},
                      "after": after, "freed": len(files.freed)})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % 0, "holdrand": "%04X" % 0x7B20,
        "farData": "%04X" % DATA, "loadedImages": images, "cases": cases,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(cases))


if __name__ == "__main__":
    main()
