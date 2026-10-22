"""A DOS machine for running original Darklands code with its overlays, files and memory.

run_unit.py runs one function with every call scripted or followed inside the images loaded at
the start. Card scripts need more: they call the rules of sibling overlays through RTLink
vectors (an overlay call replaces the card and the card engine in memory and loads them back on
return), read and write files through the C run-time (INT 21h) and allocate DOS memory. This
machine provides:

* the root image, the data image (record 136, DGROUP link-time 321A) and any chain of overlays loaded where DOS and the
  overlay manager would put them: the program's PSP at PSP_SEG, the image at LOAD_SEG = PSP_SEG + 0x10, every link-time
  segment increased by LOAD_SEG through the EXE's own relocation items (the MZ table for the root, the section tables'
  items for the overlays and the data), as DOS does; the vector table (0000:0000), the BIOS data area (0040:0000), the
  PSP, the environment and the program's memory block exist as on a DOS machine. P(seg) turns a link-time segment of
  the notation (root 0000:0CAA, "R014+0x3166" = 0E76:3166) into the segment in memory;
* RTLink thunks (`thunk 09C0:XXXX` in the root): a call loads the target record and its parents
  over what was there and returns through a trampoline that loads the caller's records back (as
  the overlay manager does, from the EXE image);
* INT 21h: files (a virtual file system over the game folder: reads come from the folder, writes
  stay in memory), DOS memory blocks (48h/49h/4Ah with memory control blocks), date/time, DTA,
  find first/next, console output (captured); any other interrupt stops the run with the call
  stack, so that presentation code is stubbed explicitly at a function entry;
* stubs at function entries (the function returns at once with the handler's AX/DX) and hooks
  at any address; nested calls of original functions from a stub (`machine.call` inside a
  handler works: the handler runs outside the emulation);
* coverage in original coordinates (record + payload), from Unicorn's translated blocks
  (UC_HOOK_BLOCK) decoded with capstone: every instruction of an executed block, and for a block
  that ends in a conditional branch the block that runs next (its outcome).
"""
from __future__ import annotations

import csv
import fnmatch
import json
import os
import struct
from pathlib import Path

import capstone
import unicorn
from unicorn import x86_const as x86

from run_unit import CONDITIONAL, EXE_SHA256, Rtlink, sha256

# Where DOSBox-X puts DARKLAND.EXE (tools/dosbox-x/.../validation/gameplay-20261009/REPORT.txt: "the Darklands main MCB at 0812, owner
# PSP 0813; the standard EXE loader base for that PSP is 0823", the environment block at 07C9, the same in two builds): above the vector
# table (0000:0000..03FF), the BIOS data area (0040:0000..00FF), the DOS kernel and the command interpreter, ~584 KB of conventional memory
# above it (the program block ends at 4A3D = 0823 + 421A, DGROUP's top; its blocks follow). DOS 5/6 with drivers gives 0600-0A00.
PSP_SEG = 0x0813
LOAD_SEG = PSP_SEG + 0x10
ENV_SEG = 0x07CA                # the environment block 07C9 (72 paragraphs, its owner is the PSP)
BASE = LOAD_SEG * 16            # linear address of the root image's first byte


def P(seg: int) -> int:
    """The segment in memory of a link-time segment (the notation's, the EXE's own): the load segment is added, as DOS's relocation does."""
    return seg + LOAD_SEG


DS = P(0x321A)
STACK_TOP = 0xFE00              # the game's stack: SS = DGROUP, SP = (415A - 321A) * 16 + 0A00
SENTINEL_SEG = 0xF000          # F000:00nn = return of a top-level/nested call at depth nn
TRAMPOLINE_SEG = 0xF100        # F100:nnnn = return of an overlay call
HEAP_START, HEAP_END = P(0x4220), 0x9FF0    # DOS memory for mem_alloc (above the 64 KB of DGROUP, up to the top of conventional memory)
REG = {n: getattr(x86, "UC_X86_REG_" + n) for n in ("AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP", "CS", "DS", "ES", "SS", "IP", "EFLAGS")}
REPO = Path(__file__).resolve().parent.parent.parent


class Stop(Exception):
    """The run cannot go on (an interrupt nobody handles, the instruction budget, a guard)."""


def linear(seg: int, off: int) -> int:
    return (seg << 4) + off


class Vfs:
    """Files of the game folder, read-only on disk; writes and new files live in memory."""

    # never read from the player's folder: what the running game writes (its temporary files, saved games)
    HIDDEN = ("*.tmp", "saves/*", "*.log")

    def __init__(self, folder: str | None):
        self.folder = Path(folder) if folder else None
        self.files: dict[str, bytearray] = {}     # name (lower case, no drive) -> contents
        self.deleted: set[str] = set()
        self.handles: dict[int, list] = {}        # handle -> [name, position]
        self.console = bytearray()

    @staticmethod
    def norm(name: str) -> str:
        return name.replace("/", "\\").lower().lstrip(".\\")

    def hidden(self, n: str) -> bool:
        return any(fnmatch.fnmatch(n.replace("\\", "/"), h) for h in self.HIDDEN)

    def get(self, name: str) -> bytearray | None:
        n = self.norm(name)
        if n in self.files:
            return self.files[n]
        if n in self.deleted or self.folder is None or self.hidden(n):
            return None
        p = self.disk_path(n)
        if p is None:
            return None
        self.files[n] = bytearray(p.read_bytes())
        return self.files[n]

    def disk_path(self, n: str) -> Path | None:
        """The file of the game folder a normalised name stands for (DOS is case-insensitive), or None."""
        if self.folder is None:
            return None
        p = self.folder / n.replace("\\", os.sep)
        if not p.is_file():
            parent = p.parent
            if not parent.is_dir():
                return None
            hits = [c for c in parent.iterdir() if c.name.lower() == p.name.lower()]
            if not hits or not hits[0].is_file():
                return None
            p = hits[0]
        return p

    def same_on_disk(self, n: str, data) -> bool:
        """Whether a cached file still holds the game folder's bytes (a snapshot then need not keep them)."""
        if self.hidden(n):
            return False
        p = self.disk_path(n)
        return p is not None and p.read_bytes() == bytes(data)

    def put(self, name: str, data: bytes):
        n = self.norm(name)
        self.files[n] = bytearray(data)
        self.deleted.discard(n)

    def names(self, pattern: str):
        n = self.norm(pattern)
        folder, _, mask = n.rpartition("\\")
        out = set()
        for k in self.files:
            kf, _, kn = k.rpartition("\\")
            if kf == folder and fnmatch.fnmatch(kn, mask):
                out.add(k)
        if self.folder is not None:
            d = self.folder / folder.replace("\\", os.sep) if folder else self.folder
            if d.is_dir():
                for c in d.iterdir():
                    k = ((folder + "\\") if folder else "") + c.name.lower()
                    if (c.is_file() and fnmatch.fnmatch(c.name.lower(), mask) and k not in self.deleted
                            and not self.hidden(k)):
                        out.add(k)
        return sorted(out)

    def open(self, name: str, create=False) -> int | None:
        if create:
            self.put(name, b"")
        elif self.get(name) is None:
            return None
        h = 5
        while h in self.handles:
            h += 1
        self.handles[h] = [self.norm(name), 0]
        return h


class Machine:
    def __init__(self, exe: bytes, game_folder: str | None = None, coverage: bool = False,
                 index_csv: Path | None = None, max_instructions: int = 50_000_000):
        if sha256(exe) != EXE_SHA256:
            raise SystemExit("not the canonical 483.07 DARKLAND.EXE")
        self.exe = exe
        self.rt = Rtlink(exe)
        self.uc = unicorn.Uc(unicorn.UC_ARCH_X86, unicorn.UC_MODE_16)
        self.uc.mem_map(0, 0x100000)
        self.root = self.relocated_root()
        self.uc.mem_write(BASE, self.root)
        self.images = {}
        self.resident: list[int] = []
        self.vfs = Vfs(game_folder)
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
        self.stubs: dict[int, object] = {}       # linear -> handler(machine) -> (ax, dx) or None (handled itself)
        self.hooks: dict[int, object] = {}       # linear -> handler(machine) (before the instruction)
        self.names: dict[int, str] = {}
        self.thunks: dict[int, tuple] = {}       # thunk linear -> (record, target seg, target off, name)
        self.trampolines: dict[int, tuple] = {}
        self.pending = None
        self.before_stub = None                  # handler(machine, stub handler) before a stub that runs outside the emulation (snapshots)
        self._block = None
        self.depth = 0
        self.count = 0
        self.budget = max_instructions
        self.blocks: list = []
        self.mcbs: dict[int, int] = {}           # segment -> paragraphs
        self.dta = (DS, 0x0080)
        self.find_state: list[str] = []
        self.coverage = coverage
        self._decoded = {}
        self.cov_insns, self.cov_edges = {}, set()   # linear -> code bytes; (jcc linear, next linear)
        self._last_block_jcc = None
        self.cov_insn_rec: dict[str, set] = {}
        self.cov_edge_rec: dict[str, set] = {}
        self.call_stack: list[str] = []
        self.log: list[str] = []
        import collections
        self.recent = collections.deque(maxlen=64)
        self.dos_memory()
        self.load(136)
        self.scratch_sp = self.crt_init()        # strings for arguments sit at the top of the stack
        self._load_thunks(index_csv or REPO / "docs" / "source-map" / "original-functions.csv")
        self.uc.hook_add(unicorn.UC_HOOK_INTR, self._on_int)
        self.uc.hook_add(unicorn.UC_HOOK_INSN, self._on_port, None, 1, 0, x86.UC_X86_INS_IN)
        self.uc.hook_add(unicorn.UC_HOOK_INSN, self._on_port, None, 1, 0, x86.UC_X86_INS_OUT)
        self.uc.hook_add(unicorn.UC_HOOK_BLOCK, self._on_block)
        self._hooked = set()
        for t in self.thunks:
            self._hook_address(t)
        # the call sentinels (F000:00nn) and the overlay-call trampolines (F100:nnnn): one hook for the range,
        # added before any emulation (Unicorn must not get new hooks while it runs)
        self.uc.hook_add(unicorn.UC_HOOK_CODE, self._on_code, begin=SENTINEL_SEG * 16, end=TRAMPOLINE_SEG * 16 + 0xFFFF)
        self.uc.reg_write(REG["SS"], DS)
        self.uc.reg_write(REG["SP"], STACK_TOP)
        self.uc.reg_write(REG["DS"], DS)
        self.uc.reg_write(REG["ES"], DS)

    def relocated_root(self) -> bytes:
        """The root image as DOS loads it: LOAD_SEG added to the word of each item of the MZ relocation table."""
        root = bytearray(self.rt.root)
        for off, seg in self.rt.reloc_table:
            at = seg * 16 + off
            struct.pack_into("<H", root, at, (struct.unpack_from("<H", root, at)[0] + LOAD_SEG) & 0xFFFF)
        return bytes(root)

    def dos_memory(self):
        """What a DOS machine has around the program: the vector table (every vector an IRET at first), the BIOS data area, the program's PSP
        (INT 20h, the top of memory, the environment, the handle table, the empty command tail), the environment with the program's name,
        and the memory control blocks of both."""
        self.uc.mem_write(linear(0xF800, 0x30), bytes([0xCF]))      # IRET (the machine's own segment, orig_side.py puts the rest)
        for n in range(256):
            self.set_vector(n, 0xF800, 0x30)
        self.write(0x40, 0x13, struct.pack("<H", 640))               # memory size in KB
        self.write(0x40, 0x10, struct.pack("<H", 0x0021))            # equipment: a diskette drive, the 80x25 colour adapter
        env = b"COMSPEC=C:\\COMMAND.COM\0PATH=C:\\\0\0" + struct.pack("<H", 1) + b"C:\\DARKLAND\\DARKLAND.EXE\0"
        self.write(ENV_SEG - 1, 0, b"M" + struct.pack("<HH", PSP_SEG, 72) + bytes(11))
        self.write(ENV_SEG, 0, env)
        self.write(PSP_SEG - 1, 0, b"M" + struct.pack("<HH", PSP_SEG, HEAP_START - PSP_SEG) + bytes(11))
        psp = bytearray(0x100)
        psp[0:2] = b"\xCD\x20"
        struct.pack_into("<H", psp, 2, 0xA000)                       # the top of conventional memory
        psp[0x50:0x53] = b"\xCD\x21\xCB"                             # the function dispatcher
        struct.pack_into("<H", psp, 0x2C, ENV_SEG)
        struct.pack_into("<HHH", psp, 0x32, 20, 0x0018, PSP_SEG)     # 20 handles, the table at PSP:0018
        psp[0x18:0x38] = bytes([0, 1, 2, 3, 4]) + bytes([0xFF] * 15)
        psp[0x80:0x82] = b"\x00\r"
        self.write(PSP_SEG, 0, bytes(psp))

    def set_vector(self, n: int, seg: int, off: int):
        self.uc.mem_write(n * 4, struct.pack("<HH", off, seg))

    def vector(self, n: int) -> tuple[int, int]:
        off, seg = struct.unpack("<HH", bytes(self.uc.mem_read(n * 4, 4)))
        return seg, off

    def crt_init(self) -> int:
        """What the C run-time start-up (root 06A1:0055..0095) leaves for the near heap: the heap
        descriptor DS:7726 {DGROUP, -, base = the stack top, start, rover, end}, an empty free block
        and the end mark at the top of the stack, the top of DGROUP (DS:7724) and DS:7760 = DGROUP.
        Returns the stack pointer the program starts with (below the heap's first words)."""
        top = STACK_TOP
        self.put_word(DS, 0x7724, 0xFFFF)
        self.put_word(DS, 0x7726, DS)
        self.put_word(DS, 0x772A, top)
        self.put_word(DS, top - 2, 0xFFFE)
        self.put_word(DS, 0x7730, top - 2)
        self.put_word(DS, top - 4, 0x0001)
        self.put_word(DS, 0x772C, top - 4)
        self.put_word(DS, 0x772E, top - 4)
        self.put_word(DS, 0x7720, top - 4)
        self.put_word(DS, 0x7760, DS)
        return top - 4

    # ---- images and overlays --------------------------------------------------------------
    def image(self, idx: int) -> bytes:
        if idx not in self.images:
            s = self.rt.sections[idx]
            img = bytearray(self.exe[s["file_offset"]:s["file_offset"] + s["length"]])
            for k in range(s["reloc_count"]):         # the section's own relocation items, as the overlay manager applies them
                off, seg = struct.unpack_from("<HH", self.exe, s["reloc_offset"] + 4 * k)
                at = seg * 16 + off - s["load_seg"] * 16
                struct.pack_into("<H", img, at, (struct.unpack_from("<H", img, at)[0] + LOAD_SEG) & 0xFFFF)
            self.images[idx] = bytes(img)
        return self.images[idx]

    def extent(self, idx: int):
        s = self.rt.sections[idx]
        return BASE + s["load_seg"] * 16, BASE + s["load_seg"] * 16 + s["length"]

    def load(self, idx: int):
        """Load record idx (its parents must be resident); records it overlaps leave memory."""
        lo, hi = self.extent(idx)
        if idx != 136:
            self._flush_coverage()
            self.resident = [r for r in self.resident if r == 136 or not (self.extent(r)[0] < hi and lo < self.extent(r)[1])]
        self.uc.mem_write(lo, self.image(idx))
        if idx not in self.resident:
            self.resident.append(idx)
        self.uc.ctl_remove_cache(lo, hi)
        self._decoded = {k: v for k, v in self._decoded.items() if not (lo <= k < hi)}

    def chain(self, idx: int) -> list[int]:
        out = []
        while idx != 0xFFFF and idx is not None:
            out.append(idx)
            idx = self.rt.sections[idx]["parent"]
        return list(reversed(out))

    def ensure(self, idx: int):
        for r in self.chain(idx):
            if r not in self.resident:
                self.load(r)

    def _load_thunks(self, path: Path):
        """Thunks and functions of the original index (docs/source-map/original-functions.csv), named by coordinate."""
        self.functions = {}
        for r in csv.DictReader(open(path, encoding="utf-8")):
            seg, off = (int(x, 16) + (LOAD_SEG if i == 0 else 0) for i, x in enumerate(r["linked"].split(":")))
            if r["kind"] == "thunk" and r["target"]:
                # target: "RNNN+0xPPPP (SSSS:OOOO)" or "root SSSS:OOOO"
                t, rec = r["target"], r["target"][:4]
                loc = t.split(" ")[1] if rec == "root" else t.split("(")[1].rstrip(")")
                ts, to = (int(x, 16) + (LOAD_SEG if i == 0 else 0) for i, x in enumerate(loc.split(":")))
                self.thunks[linear(seg, off)] = (None if rec == "root" else int(rec[1:4]), ts, to, r["coordinate"],
                                                 t.split(" (")[0])
            if r["kind"] == "function":
                rec = "root" if r["coordinate"].startswith("root") else r["coordinate"][:4]
                self.names[linear(seg, off)] = r["coordinate"]
                self.functions.setdefault(rec, []).append((linear(seg, off), r["coordinate"]))

    # ---- hooks -----------------------------------------------------------------------------
    def _hook_address(self, lin: int):
        if self.depth:
            raise RuntimeError("a hook is added while the machine runs")
        if lin not in self._hooked:
            self._hooked.add(lin)
            self.uc.hook_add(unicorn.UC_HOOK_CODE, self._on_code, begin=lin, end=lin)

    def owner(self, seg: int, off: int, record=None):
        """The record a link-time address belongs to: root, or the one overlay with a function there."""
        lin = linear(seg, off)
        if record is not None:
            return record
        if BASE <= lin < BASE + len(self.root):
            return "root"
        recs = sorted({rec for rec, fs in self.functions.items() if rec != "root" and any(a == lin for a, _ in fs)})
        if len(recs) != 1:
            raise ValueError("%04X:%04X is a function of %s: name the record" % (seg, off, recs or "no record"))
        return recs[0]

    def _active(self, owner: str) -> bool:
        return owner == "root" or int(owner[1:]) in self.resident

    def stub(self, seg: int, off: int, handler, name: str | None = None, record=None):
        """handler(machine) -> (AX, DX) at the entry of the far function seg:off (a segment in memory, see P; of its record only:
        another overlay's code at that address runs as it is)."""
        lin = linear(seg, off)
        self.stubs.setdefault(lin, []).append((self.owner(seg, off, record), handler))
        if name:
            self.names.setdefault(lin, name)
        self._hook_address(lin)

    def hook(self, seg: int, off: int, handler, record=None):
        lin = linear(seg, off)
        self.hooks.setdefault(lin, []).append((self.owner(seg, off, record), handler))
        self._hook_address(lin)

    def reg(self, name: str) -> int:
        return self.uc.reg_read(REG[name])

    def set_reg(self, name: str, v: int):
        self.uc.reg_write(REG[name], v & 0xFFFF)

    def word(self, seg: int, off: int) -> int:
        return struct.unpack("<H", bytes(self.uc.mem_read(linear(seg, off & 0xFFFF), 2)))[0]

    def sword(self, seg: int, off: int) -> int:
        return struct.unpack("<h", bytes(self.uc.mem_read(linear(seg, off & 0xFFFF), 2)))[0]

    def byte(self, seg: int, off: int) -> int:
        return self.uc.mem_read(linear(seg, off & 0xFFFF), 1)[0]

    def read(self, seg: int, off: int, n: int) -> bytes:
        return bytes(self.uc.mem_read(linear(seg, off), n))

    def write(self, seg: int, off: int, data: bytes):
        self.uc.mem_write(linear(seg, off), bytes(data))

    def put_word(self, seg: int, off: int, v: int):
        self.write(seg, off, struct.pack("<H", v & 0xFFFF))

    def cstr(self, seg: int, off: int, n: int = 256) -> bytes:
        return self.read(seg, off, n).split(b"\0")[0]

    def arg(self, k: int) -> int:
        """Word k of the arguments of the far function being entered (stack: ret off, ret seg, args)."""
        return self.word(self.reg("SS"), self.reg("SP") + 4 + 2 * k)

    def sarg(self, k: int) -> int:
        v = self.arg(k)
        return v - 0x10000 if v & 0x8000 else v

    def far_return(self, ax: int = 0, dx: int = 0, near: bool = False):
        ss, sp = self.reg("SS"), self.reg("SP")
        ip = self.word(ss, sp)
        if near:
            self.set_reg("SP", sp + 2)
            cs = self.reg("CS")
        else:
            cs = self.word(ss, sp + 2)
            self.set_reg("SP", sp + 4)
        self.set_reg("AX", ax)
        self.set_reg("DX", dx)
        self.uc.reg_write(REG["CS"], cs)
        self.uc.reg_write(REG["IP"], ip)

    def _on_code(self, uc, address, size, _):
        if address >> 4 == SENTINEL_SEG and address - SENTINEL_SEG * 16 == self.depth:
            uc.emu_stop()
            self.pending = ("return",)
            return
        if address in self.trampolines:
            self._trampoline(address)
            return
        for owner, h in self.hooks.get(address, ()):
            if self._active(owner):
                h(self)
        h = next((h for owner, h in self.stubs.get(address, ()) if self._active(owner)), None)
        if h is not None:
            if getattr(h, "outside", False):
                uc.emu_stop()
                self.pending = ("stub", h)
                return
            r = h(self)
            if r is not None:
                self.far_return(*r)
            return
        if address in self.thunks:
            self._thunk(address)

    def _thunk(self, address):
        rec, ts, to, name, target = self.thunks[address]
        ss, sp = self.reg("SS"), self.reg("SP")
        ret_ip, ret_cs = self.word(ss, sp), self.word(ss, sp + 2)
        before = list(self.resident)
        k = 0
        while linear(TRAMPOLINE_SEG, k) in self.trampolines:
            k += 1
        tl = linear(TRAMPOLINE_SEG, k)
        self.trampolines[tl] = (ret_cs, ret_ip, before, target)
        self.put_word(ss, sp, k)
        self.put_word(ss, sp + 2, TRAMPOLINE_SEG)
        if rec is not None:
            self.ensure(rec)
        self.call_stack.append(target)
        self.uc.reg_write(REG["CS"], ts)
        self.uc.reg_write(REG["IP"], to)

    def _trampoline(self, address):
        ret_cs, ret_ip, before, target = self.trampolines.pop(address)
        if self.call_stack:
            self.call_stack.pop()
        for r in before:
            if r not in self.resident:
                self.ensure(r)
        self.uc.reg_write(REG["CS"], ret_cs)
        self.uc.reg_write(REG["IP"], ret_ip)

    def valid(self, lin: int) -> bool:
        if BASE <= lin < BASE + len(self.root):
            return True
        if SENTINEL_SEG * 16 <= lin < TRAMPOLINE_SEG * 16 + 0x10000:
            return True
        return any(self.extent(r)[0] <= lin < self.extent(r)[1] for r in self.resident if r != 136)

    def _on_block(self, uc, address, size, _):
        self._block = (address, self.count, self._last_block_jcc)      # what a snapshot taken at this block's first instruction must undo
        self.recent.append(address)
        if not self.valid(address):
            self.pending = ("stop", "execution outside the loaded code at %05X (from %s)" % (
                address, " ".join("%05X" % a for a in list(self.recent)[-8:-1])))
            uc.emu_stop()
            return
        self.count += size   # bytes, a cheap bound
        if self.count > self.budget * 3:
            self.pending = ("stop", "instruction budget")
            uc.emu_stop()
            return
        if not self.coverage:
            return
        if self._last_block_jcc is not None:
            self.cov_edges.add((self._last_block_jcc, address))
            self._last_block_jcc = None
        ent = self._decoded.get(address)
        if ent is None:
            code = bytes(uc.mem_read(address, size))
            ins = list(self.md.disasm(code, 0))
            ent = self._decoded[address] = [(address + i.address, code[i.address:i.address + i.size], i.mnemonic) for i in ins]
        for lin, code, mn in ent:
            if lin not in self.cov_insns:
                self.cov_insns[lin] = code
        if ent and ent[-1][2] in CONDITIONAL:
            self._last_block_jcc = ent[-1][0]

    def _flush_coverage(self):
        if not self.coverage or not self.cov_insns:
            return
        order = list(reversed([r for r in self.resident if r != 136]))
        resolved = {}
        for lin, code in self.cov_insns.items():
            rec, pay = None, lin
            if BASE <= lin and lin - BASE + len(code) <= len(self.root) and self.root[lin - BASE:lin - BASE + len(code)] == code:
                rec, pay = "root", lin - BASE
            else:
                for idx in order + [s["index"] for s in self.rt.sections if s["index"] not in order and s["index"] != 136]:
                    lo, hi = self.extent(idx)
                    if lo <= lin and lin + len(code) <= hi and self.image(idx)[lin - lo:lin - lo + len(code)] == code:
                        rec, pay = "R%03d" % idx, lin - lo
                        break
            if rec is None:
                continue
            resolved[lin] = (rec, pay)
            self.cov_insn_rec.setdefault(rec, set()).add(pay)
        for j, n in self.cov_edges:
            if j in resolved and n in resolved and resolved[j][0] == resolved[n][0]:
                self.cov_edge_rec.setdefault(resolved[j][0], set()).add((resolved[j][1], resolved[n][1]))
        self.cov_insns, self.cov_edges = {}, set()
        self._last_block_jcc = None

    def coverage_result(self):
        self._flush_coverage()
        return {"insns": {k: sorted(v) for k, v in self.cov_insn_rec.items()},
                "edges": {k: sorted(v) for k, v in self.cov_edge_rec.items()}}

    # ---- interrupts --------------------------------------------------------------------------
    def _on_int(self, uc, intno, _):
        if intno != 0x21:
            self.pending = ("stop", "INT %02Xh AX=%04X at %04X:%04X" % (intno, self.reg("AX"), self.reg("CS"), self.reg("IP")))
            uc.emu_stop()
            return
        try:
            self._dos()
        except Stop as e:
            self.pending = ("stop", str(e))
            uc.emu_stop()

    def _on_port(self, uc, port, size, *rest):
        self.pending = ("stop", "port I/O %04X at %04X:%04X" % (port, self.reg("CS"), self.reg("IP")))
        uc.emu_stop()
        return 0

    def function_at(self, lin: int) -> str:
        """The original function holding linear address lin, among the root and the resident records."""
        import bisect
        recs = ["root"] if BASE <= lin < BASE + len(self.root) else [
            "R%03d" % r for r in reversed(self.resident) if r != 136 and self.extent(r)[0] <= lin < self.extent(r)[1]]
        for rec in recs:
            fs = sorted(self.functions.get(rec, []))
            k = bisect.bisect_right([a for a, _ in fs], lin) - 1
            if k >= 0:
                return "%s+%X" % (fs[k][1], lin - fs[k][0])
        return "%05X" % lin

    def _carry(self, on: bool, ax: int | None = None):
        f = self.uc.reg_read(REG["EFLAGS"])
        self.uc.reg_write(REG["EFLAGS"], (f | 1) if on else (f & ~1))
        if ax is not None:
            self.set_reg("AX", ax)

    def _dsdx_name(self):
        return self.cstr(self.reg("DS"), self.reg("DX")).decode("latin-1")

    def _dos(self):
        ah = self.reg("AX") >> 8
        al = self.reg("AX") & 0xFF
        v = self.vfs
        if ah == 0x48:
            want = self.reg("BX")
            seg = self.alloc(want)
            if seg is None:
                self.set_reg("BX", self.largest())
                self._carry(True, 8)
            else:
                self._carry(False, seg)
        elif ah == 0x49:
            seg = self.reg("ES")
            if seg in self.mcbs:
                del self.mcbs[seg]
                self._carry(False)
            else:
                self._carry(True, 9)
        elif ah == 0x4A:
            seg, want = self.reg("ES"), self.reg("BX")
            self.log.append("resize %04X to %04X paragraphs" % (seg, want))
            if seg not in self.mcbs:
                # the program's own block (PSP .. DGROUP and its near heap): it may grow up to the DOS heap
                if seg + want <= HEAP_START:
                    self._carry(False)
                else:
                    self.set_reg("BX", HEAP_START - seg)
                    self._carry(True, 8)
            else:
                nxt = min([s for s in self.mcbs if s > seg] + [HEAP_END])
                if seg + want + 1 <= nxt:
                    self.mcbs[seg] = want
                    self._carry(False)
                else:
                    self.set_reg("BX", nxt - seg - 1)
                    self._carry(True, 8)
        elif ah == 0x3D:
            h = v.open(self._dsdx_name())
            self._carry(h is None, 2 if h is None else h)
        elif ah == 0x3C:
            h = v.open(self._dsdx_name(), create=True)
            self._carry(False, h)
        elif ah == 0x3E:
            h = self.reg("BX")
            if h in v.handles:
                del v.handles[h]
            self._carry(False, 0)
        elif ah == 0x3F:
            h, n = self.reg("BX"), self.reg("CX")
            if h not in v.handles:
                raise Stop("read from handle %d" % h)
            name, pos = v.handles[h]
            data = v.get(name)
            chunk = bytes(data[pos:pos + n])
            self.write(self.reg("DS"), self.reg("DX"), chunk)
            v.handles[h][1] = pos + len(chunk)
            self._carry(False, len(chunk))
        elif ah == 0x40:
            h, n = self.reg("BX"), self.reg("CX")
            chunk = self.read(self.reg("DS"), self.reg("DX"), n)
            if h in (1, 2):
                v.console += chunk
                self._carry(False, n)
            elif h in v.handles:
                name, pos = v.handles[h]
                data = v.get(name)
                if n == 0:
                    del data[pos:]
                else:
                    if pos > len(data):
                        data.extend(bytes(pos - len(data)))
                    data[pos:pos + n] = chunk
                v.handles[h][1] = pos + n
                self._carry(False, n)
            else:
                raise Stop("write to handle %d" % h)
        elif ah == 0x42:
            h = self.reg("BX")
            if h not in v.handles:
                raise Stop("seek on handle %d" % h)
            off = (self.reg("CX") << 16) | self.reg("DX")   # DOS: an unsigned 32-bit position
            name, pos = v.handles[h]
            size = len(v.get(name))
            new = (off if al == 0 else pos + off if al == 1 else size + off) & 0xFFFFFFFF
            v.handles[h][1] = new
            self._carry(False, new & 0xFFFF)
            self.set_reg("DX", new >> 16)
        elif ah == 0x41:
            n = v.norm(self._dsdx_name())
            ok = v.get(n) is not None
            v.files.pop(n, None)
            v.deleted.add(n)
            self._carry(not ok, 0 if ok else 2)
        elif ah == 0x44:
            h = self.reg("BX")
            if al == 0:
                self.set_reg("DX", 0x80D3 if h <= 4 else 0x0002)
                self._carry(False, 0)
            else:
                self._carry(False, 0)
        elif ah == 0x43:
            ok = v.get(self._dsdx_name()) is not None
            if ok and al == 0:
                self.set_reg("CX", 0x20)
            self._carry(not ok, 0 if ok else 2)
        elif ah == 0x1A:
            self.dta = (self.reg("DS"), self.reg("DX"))
        elif ah == 0x2F:
            self.uc.reg_write(REG["ES"], self.dta[0])
            self.set_reg("BX", self.dta[1])
        elif ah in (0x4E, 0x4F):
            if ah == 0x4E:
                self.find_state = v.names(self._dsdx_name())
            if not self.find_state:
                self._carry(True, 0x12)
            else:
                n = self.find_state.pop(0)
                data = v.get(n)
                rec = bytearray(43)
                rec[21] = 0x20
                struct.pack_into("<HHI", rec, 22, 0, 0x2121, len(data))
                base = n.rpartition("\\")[2].upper().encode()[:12]
                rec[30:30 + len(base)] = base
                self.write(self.dta[0], self.dta[1], rec)
                self._carry(False, 0)
        elif ah == 0x2A:
            self.set_reg("CX", 1992)
            self.set_reg("DX", 0x0101)
            self.set_reg("AX", 0x2A03)
        elif ah == 0x2C:
            self.set_reg("CX", 0x0C00)
            self.set_reg("DX", 0x0000)
        elif ah == 0x30:
            self.set_reg("AX", 0x0005)
        elif ah == 0x25:
            self.set_vector(al, self.reg("DS"), self.reg("DX"))
        elif ah == 0x35:
            seg, off = self.vector(al)
            self.uc.reg_write(REG["ES"], seg)
            self.set_reg("BX", off)
        elif ah == 0x09:
            seg, off = self.reg("DS"), self.reg("DX")
            v.console += self.read(seg, off, 512).split(b"$")[0]
        elif ah == 0x4C:
            raise Stop("program exit %d: %s" % (al, v.console[-300:].decode("latin-1")))
        else:
            raise Stop("INT 21h AH=%02Xh at %04X:%04X" % (ah, self.reg("CS"), self.reg("IP")))

    def alloc(self, paras: int) -> int | None:
        seg = HEAP_START + 1
        for s in sorted(self.mcbs):
            if seg + paras < s:   # room before this block (s - 1 is its MCB)
                break
            seg = max(seg, s + self.mcbs[s] + 1)
        if seg + paras > HEAP_END:
            return None
        self.mcbs[seg] = paras
        self.uc.mem_write(linear(seg - 1, 0), b"M" + bytes(15))
        self.uc.mem_write(linear(seg, 0), bytes(paras * 16))
        return seg

    def largest(self) -> int:
        seg, best = HEAP_START + 1, 0
        for s in sorted(self.mcbs):
            best = max(best, s - seg - 1)
            seg = max(seg, s + self.mcbs[s] + 1)
        return max(best, HEAP_END - seg)

    # ---- calls -------------------------------------------------------------------------------
    def call(self, seg: int, off: int, args=(), regs=None, buffer: int = 0) -> tuple[int, int]:
        """Run the far function seg:off with word arguments (first argument first) until it returns.
        buffer: bytes reserved on the stack above the call; an argument "BUF" is replaced by its DGROUP
        offset, and its contents after the call are in self.buffer."""
        saved = {n: self.uc.reg_read(r) for n, r in REG.items()}
        self.depth += 1
        depth = self.depth
        sent = linear(SENTINEL_SEG, depth)
        ss = self.reg("SS")
        sp = self.reg("SP") if depth > 1 else self.scratch_sp
        sp = (sp - 0x40) & 0xFFFF if depth > 1 else sp   # keep the interrupted frame's words
        buf = None
        if buffer:
            sp = (sp - buffer) & 0xFFFE
            buf = sp
            self.write(ss, buf, bytes(buffer))
        words = [(buf if a == "BUF" else a) & 0xFFFF for a in args]
        sp -= 2 * len(words)
        self.write(ss, sp, struct.pack("<%dH" % len(words), *words))
        sp -= 4
        self.write(ss, sp, struct.pack("<HH", depth, SENTINEL_SEG))
        self.uc.reg_write(REG["SP"], sp)
        self.uc.reg_write(REG["DS"], DS)
        self.uc.reg_write(REG["ES"], DS)
        for k, v in (regs or {}).items():
            self.uc.reg_write(REG[k], v)
        self.uc.reg_write(REG["CS"], seg)
        self.uc.reg_write(REG["IP"], off)
        try:
            self._drive()
            ax, dx = self.reg("AX"), self.reg("DX")
            if buf is not None:
                self.buffer = self.read(ss, buf, buffer)
        finally:
            self.depth -= 1
            if depth > 1:   # a nested call leaves the interrupted run as it was
                for n, r in REG.items():
                    self.uc.reg_write(r, saved[n])
        return ax, dx

    def _drive(self, first=None):
        """Run the emulation until the current call returns; first: the handler of a stub that was about to run when a snapshot was taken."""
        pending = ("stub", first) if first is not None else None
        while True:
            if pending is None:
                self.pending = None
                start = linear(self.reg("CS"), self.reg("IP"))
                self.uc.emu_start(start, 0x100000)
                p = self.pending
                if p is None:
                    raise Stop("emulation ended at %04X:%04X" % (self.reg("CS"), self.reg("IP")))
            else:
                p, pending = pending, None
            if p[0] == "return":
                break
            if p[0] == "stop":
                raise Stop(p[1] + self.where())
            if p[0] == "stub":
                if self.before_stub is not None:
                    self.before_stub(self, p[1])
                r = p[1](self)
                if r is not None:
                    self.far_return(*r)

    def resume(self, first=None):
        """Carry on the top-level call that a snapshot was taken in (the machine's state restored): (AX, DX) when it returns."""
        try:
            self._drive(first)
            return self.reg("AX"), self.reg("DX")
        finally:
            self.depth -= 1

    def snapshot(self):
        """Everything a later run depends on: memory, registers, files, DOS memory, overlays."""
        v = self.vfs
        return (bytes(self.uc.mem_read(0, 0x100000)), self.uc.context_save(),
                {k: bytes(d) for k, d in v.files.items()}, set(v.deleted), {h: list(x) for h, x in v.handles.items()},
                dict(self.mcbs), list(self.resident), self.dta, self.scratch_sp)

    def restore(self, snap):
        mem, ctx, files, deleted, handles, mcbs, resident, dta, scratch = snap
        self.uc.mem_write(0, mem)
        self.uc.context_restore(ctx)
        self.uc.ctl_remove_cache(0, 0x100000)
        self._decoded = {}
        v = self.vfs
        v.files = {k: bytearray(d) for k, d in files.items()}
        v.deleted = set(deleted)
        v.handles = {h: list(x) for h, x in handles.items()}
        v.console = bytearray()
        self.mcbs = dict(mcbs)
        self.resident = list(resident)
        self.dta = dta
        self.scratch_sp = scratch
        self.trampolines = {}
        self.call_stack = []
        self.count = 0

    def push_string(self, text: str | bytes) -> int:
        """A zero-terminated string at the top of the stack (DGROUP offset), below earlier ones."""
        data = (text.encode("latin-1") if isinstance(text, str) else text) + bytes(1)
        self.scratch_sp = (self.scratch_sp - len(data)) & ~1
        self.write(DS, self.scratch_sp, data)
        return self.scratch_sp

    def where(self) -> str:
        ss, sp, bp = self.reg("SS"), self.reg("SP"), self.reg("BP")
        frames = []
        for _ in range(12):
            if not (0 < bp < 0xFFFE):
                break
            ip, cs = self.word(ss, bp + 2), self.word(ss, bp + 4)
            frames.append("%04X:%04X %s" % (cs, ip, self.function_at(linear(cs, ip))))
            bp = self.word(ss, bp)
        return "; at %s; overlays %s; vectors %s; frames %s" % (
            self.function_at(linear(self.reg("CS"), self.reg("IP"))), self.resident, self.call_stack[-6:], " < ".join(frames))
