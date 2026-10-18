"""Static basic blocks and conditional branches of original Darklands functions.

Each function of docs/source-map/original-functions.csv (record, linked address, size) is
decoded with capstone from the unchanged EXE bytes by recursive descent from its entry,
inside its extent [entry, entry + size): conditional branches have two outcomes (taken,
fall-through), jumps and returns end a block, calls fall through, `jmp cs:[bx+table]`
switch tables of MSC are read (the bound comes from the `cmp reg, n` / `ja` guard before
them). Coordinates are those of TRACEABILITY.md: record (root / RNNN) and payload offset
(root: linear address).
"""
from __future__ import annotations

import csv
import re
import struct
from dataclasses import dataclass, field
from pathlib import Path

import capstone
from capstone import x86_const as cx

from run_unit import CONDITIONAL, Rtlink

ENDS = {"ret", "retf", "iret", "hlt"}
JUMPS = {"jmp", "ljmp"}


@dataclass
class Function:
    name: str
    record: str          # "root" or "R029"
    frame: int           # linked frame (CS)
    entry_ip: int        # linked offset
    size: int
    load_seg: int        # record load frame (0 for root)
    status: str          # matched / unmatched / draft / asm (the index's source status)

    @property
    def linear(self) -> int:
        return self.frame * 16 + self.entry_ip

    def payload(self, linear: int) -> int:
        return linear - self.load_seg * 16 if self.record != "root" else linear

    def coordinate(self, linear: int) -> str:
        ip = linear - self.frame * 16
        if self.record == "root":
            return "root %04X:%04X" % (self.frame, ip)
        return "%s+0x%04X (%04X:%04X)" % (self.record, self.payload(linear), self.frame, ip)


@dataclass
class Cfg:
    blocks: dict = field(default_factory=dict)      # leader linear -> [insn linears]
    branches: list = field(default_factory=list)    # (jcc linear, taken linear, fall linear, mnemonic)
    switches: list = field(default_factory=list)    # (jmp linear, [target linears])
    unresolved: list = field(default_factory=list)  # indirect jumps without a table
    insns: dict = field(default_factory=dict)       # linear -> capstone insn
    outside: set = field(default_factory=set)       # branch targets outside the extent (tail jumps)


def load_functions(csv_path: Path, rt: Rtlink) -> list[Function]:
    out = []
    for r in csv.DictReader(open(csv_path, encoding="utf-8")):
        if r["kind"] != "function" or not r["size"]:
            continue
        seg, off = (int(x, 16) for x in r["linked"].split(":"))
        rec = "root" if r["coordinate"].startswith("root") else r["coordinate"][:4]
        load = 0 if rec == "root" else rt.sections[int(rec[1:])]["load_seg"]
        out.append(Function(r["coordinate"], rec, seg, off, int(r["size"]), load, r["status"]))
    return out


def image(exe: bytes, rt: Rtlink, fn: Function) -> bytes:
    if fn.record == "root":
        return rt.root[fn.linear:fn.linear + fn.size]
    s = rt.sections[int(fn.record[1:])]
    p = fn.payload(fn.linear)
    return exe[s["file_offset"] + p:s["file_offset"] + p + fn.size]


_TABLE = re.compile(r"word ptr cs:\[(bx|si|di)( \+ (0x[0-9a-f]+|\d+))?\]")


def build(exe: bytes, rt: Rtlink, fn: Function, extra_leaders=()) -> Cfg:
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    md.detail = True
    code = image(exe, rt, fn)
    lo, hi = fn.linear, fn.linear + fn.size
    base = fn.frame * 16
    cfg = Cfg()
    leaders = {lo} | {a for a in extra_leaders if lo <= a < hi}
    work = sorted(leaders)
    seen_table = set()

    def decode(lin):
        if lin in cfg.insns:
            return cfg.insns[lin]
        k = lin - lo
        ins = next(md.disasm(code[k:k + 16], lin - base), None)
        if ins is None or k + ins.size > len(code):
            return None
        cfg.insns[lin] = ins
        return ins

    def lin_of(ip):
        return base + (ip & 0xFFFF)

    succ_edges = {}
    while work:
        a = work.pop()
        prev = []   # recent instructions for the switch guard
        while lo <= a < hi:
            if a in cfg.insns:
                break   # joined code decoded before
            ins = decode(a)
            if ins is None:
                break
            prev.append(ins)
            nxt = a + ins.size
            m = ins.mnemonic
            if m in CONDITIONAL:
                t = lin_of(ins.operands[0].imm)
                cfg.branches.append((a, t, nxt, m))
                for x in (t, nxt):
                    if lo <= x < hi:
                        if x not in leaders:
                            leaders.add(x)
                            work.append(x)
                    else:
                        cfg.outside.add(x)
                break
            if m in JUMPS:
                op = ins.operands[0]
                if op.type == cx.X86_OP_IMM and m == "jmp":
                    t = lin_of(op.imm)
                    if lo <= t < hi:
                        if t not in leaders:
                            leaders.add(t)
                            work.append(t)
                    else:
                        cfg.outside.add(t)
                elif op.type == cx.X86_OP_MEM and m == "jmp" and a not in seen_table:
                    seen_table.add(a)
                    targets = switch_targets(ins, prev, code, lo, hi, base, cfg)
                    if targets:
                        cfg.switches.append((a, targets))
                        for t in targets:
                            if t not in leaders:
                                leaders.add(t)
                                work.append(t)
                    else:
                        cfg.unresolved.append(a)
                elif a not in seen_table:
                    seen_table.add(a)
                    cfg.unresolved.append(a)
                break
            if m in ENDS or (m == "int" and ins.op_str in ("0x20",)):
                break
            if m == "call":
                op = ins.operands[0]
                if op.type == cx.X86_OP_IMM:
                    t = lin_of(op.imm)
                    if lo <= t < hi and t != lo and t not in leaders:
                        leaders.add(t)
                        work.append(t)
            a = nxt
            if a in leaders:
                break
    # blocks: split the decoded instructions at leaders
    order = sorted(cfg.insns)
    cur, prev_end = None, None
    for a in order:
        ins = cfg.insns[a]
        if a in leaders or cur is None or prev_end != a:
            cur = a
            cfg.blocks[cur] = []
        cfg.blocks[cur].append(a)
        prev_end = a + ins.size
        if ins.mnemonic in CONDITIONAL or ins.mnemonic in JUMPS or ins.mnemonic in ENDS:
            cur = None
    return cfg


def switch_targets(ins, prev, code, lo, hi, base, cfg):
    m = _TABLE.search(ins.op_str)
    if not m:
        return []
    table_ip = int(m.group(3), 0) if m.group(3) else None
    if table_ip is None:
        return []
    count = None
    for p in reversed(prev[-8:-1]):
        if p.mnemonic == "cmp" and len(p.operands) == 2 and p.operands[1].type == cx.X86_OP_IMM:
            count = (p.operands[1].imm & 0xFFFF) + 1
            break
    tl = base + table_ip
    out = []
    k = 0
    while True:
        if count is not None and k >= count:
            break
        at = tl + 2 * k - lo
        if at < 0 or at + 2 > len(code) or k > 255:
            break
        t = base + struct.unpack_from("<H", code, at)[0]
        if not (lo <= t < hi) or (count is None and (tl + 2 * k) in cfg.insns):
            break
        out.append(t)
        k += 1
    return sorted(set(out))
