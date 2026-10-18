"""The structure of every original function, read from the unchanged DARKLAND.EXE bytes.

For each `function` row of docs/source-map/original-functions.csv the function is decoded by
recursive descent (tools/oracle/cfg.py) and these facts are kept, in original coordinates:

  calls     every call the function makes, in address order: near and far calls, calls through
            RTLink thunks (resolved to the thunk's target), jumps out of the extent (tail calls),
            and indirect calls (`*`: through a register or memory), each with the constant words it
            pushes as arguments (first argument first; null for a value computed at run time or an
            address: the offset of a far pointer or of a string literal in DGROUP)
  cmps      the immediate operands of `cmp` / `test` (the thresholds and flags the code tests)
  stores    the immediates the function writes to memory (`mov [..], imm`: flags, next cards, counts)
  imms      every immediate operand of the function's data instructions (not call or jump targets,
            frame sizes or return pops), as a set
  branches  the number of conditional branches, switches (with their case count)

A call target is resolved by its link-time address: the calling record first, then the records
loaded below it (its RTLink parents), then the root. structure.py compares these facts with the C#.

  python -I -B tools/faithfulness/structure_exe.py <DARKLAND.EXE> <out.json>
"""
from __future__ import annotations

import csv
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE.parent / "oracle"))

from capstone import x86_const as cx  # noqa: E402

import cfg  # noqa: E402
from run_unit import EXE_SHA256, Rtlink, sha256  # noqa: E402

INDEX = REPO / "docs" / "source-map" / "original-functions.csv"
DATA_IMAGE, DGROUP = 136, 0x321A


def string_starts(exe, rt):
    """The DGROUP offsets where a text starts (after a NUL, at least three characters, then a NUL): a word
    pushed with one of these values is the address of a string literal, not a constant."""
    s = rt.sections[DATA_IMAGE]
    base = s["file_offset"] + (DGROUP - s["load_seg"]) * 16
    seg = exe[base:base + 0x10000]
    out = set()
    for v in range(1, len(seg)):
        if seg[v - 1] != 0 or seg[v] == 0:
            continue
        end = seg.find(0, v, v + 256)
        if end - v >= 3 and all(32 <= b < 127 or b >= 0x80 or b in (9, 10, 13) for b in seg[v:end]):
            out.add(v)
    return out


def load_index(rt):
    """(record, linear) -> coordinate for every function; root linear -> thunk target coordinate."""
    funcs, thunks = {}, {}
    for r in csv.DictReader(open(INDEX, encoding="utf-8")):
        seg, off = (int(x, 16) for x in r["linked"].split(":"))
        lin = seg * 16 + off
        if r["kind"] == "function":
            rec = "root" if r["coordinate"].startswith("root") else r["coordinate"][:4]
            funcs[(rec, lin)] = r["coordinate"]
        elif r["kind"] == "thunk":
            thunks[lin] = r["target"].split(" (")[0]
    return funcs, thunks


def chain(rt, record):
    """The record and the records loaded below it, nearest first, then the root."""
    out = []
    if record != "root":
        i = int(record[1:])
        seen = set()
        while 0 <= i < len(rt.sections) and i not in seen:
            seen.add(i)
            out.append("R%03d" % i)
            p = rt.sections[i]["parent"]
            if p == i or p >= len(rt.sections):
                break
            i = p
    out.append("root")
    return out


def resolve(funcs, thunks, rt, record, lin):
    if lin in thunks:
        return thunks[lin]
    for rec in chain(rt, record):
        c = funcs.get((rec, lin))
        if c:
            return c
    return "?%05X" % lin


def analyse(exe: bytes, rt, fn, funcs, thunks, strings):
    g = cfg.build(exe, rt, fn)
    base = fn.frame * 16
    calls, cmps, stores, imms = [], [], [], set()
    regs, pushes = {}, []   # constant registers and the words pushed and not yet taken by a call, within a block
    segments = {cx.X86_REG_DS, cx.X86_REG_SS, cx.X86_REG_ES, cx.X86_REG_CS}

    def args(after):
        """The arguments of a call: as many pushed words as the caller pops after it (`add sp, n`, or `pop`s);
        the words pushed before them are an outer call's, evaluated around this one. Without a cleanup
        (callee pops, or a tail jump) every pending word."""
        k = len(pushes)
        nxt = g.insns.get(after)
        if nxt is not None and nxt.mnemonic == "add" and nxt.op_str.startswith("sp, ") and nxt.operands[1].type == cx.X86_OP_IMM:
            k = min(k, (nxt.operands[1].imm & 0xFFFF) // 2)
        elif nxt is not None and nxt.mnemonic == "pop" and nxt.operands[0].type == cx.X86_OP_REG:
            n = 0
            while nxt is not None and nxt.mnemonic == "pop" and n < 2:
                n += 1
                nxt = g.insns.get(after + nxt.size * n)
            k = min(k, n)
        out = list(reversed(pushes[len(pushes) - k:]))
        del pushes[len(pushes) - k:]
        return out

    prev_segment = False
    for lin in sorted(g.insns):
        ins = g.insns[lin]
        m = ins.mnemonic
        pay = fn.payload(lin)
        if lin in g.blocks:
            regs.clear()
            pushes.clear()
        ops = ins.operands
        if m not in ("call", "lcall", "jmp", "ljmp", "enter", "ret", "retf") and m not in cfg.CONDITIONAL:
            imms.update(o.imm & (0xFF if o.size == 1 else 0xFFFF) for o in ops if o.type == cx.X86_OP_IMM)
        after_segment = prev_segment
        prev_segment = m == "push" and ops[0].type == cx.X86_OP_REG and ops[0].reg in segments
        if m == "push":
            if ops[0].type == cx.X86_OP_IMM:
                # the offset of a far pointer (`push ds; push offset`) is an address, not a constant
                # and so is the offset of a string literal
                v = ops[0].imm & 0xFFFF
                pushes.append(None if after_segment or v in strings else v)
            elif ops[0].type == cx.X86_OP_REG:
                pushes.append(None if ops[0].reg in segments else regs.get(ops[0].reg))
            else:
                pushes.append(None)
        elif m == "mov" and len(ops) == 2 and ops[1].type == cx.X86_OP_IMM:
            if ops[0].type == cx.X86_OP_REG and ops[0].size == 2:
                regs[ops[0].reg] = ops[1].imm & 0xFFFF
            elif ops[0].type == cx.X86_OP_MEM:
                stores.append(ops[1].imm & (0xFF if ops[0].size == 1 else 0xFFFF))
        elif ops and ops[0].type == cx.X86_OP_REG and m not in ("cmp", "test"):
            regs.pop(ops[0].reg, None)
        if m in ("call", "lcall"):
            ops = ins.operands
            if m == "lcall" and len(ops) == 2 and ops[0].type == cx.X86_OP_IMM:
                t = resolve(funcs, thunks, rt, fn.record, (ops[0].imm & 0xFFFF) * 16 + (ops[1].imm & 0xFFFF))
            elif m == "call" and ops and ops[0].type == cx.X86_OP_IMM:
                t = resolve(funcs, thunks, rt, fn.record, base + (ops[0].imm & 0xFFFF))
            else:
                t = "*"
            calls.append([pay, t, args(lin + ins.size)])
            regs.clear()
        elif m in ("jmp", "ljmp"):
            ops = ins.operands
            t = None
            if m == "ljmp" and len(ops) == 2 and ops[0].type == cx.X86_OP_IMM:
                t = (ops[0].imm & 0xFFFF) * 16 + (ops[1].imm & 0xFFFF)
            elif ops and ops[0].type == cx.X86_OP_IMM:
                t = base + (ops[0].imm & 0xFFFF)
            if t is not None and not (fn.linear <= t < fn.linear + fn.size):
                calls.append([pay, resolve(funcs, thunks, rt, fn.record, t), args(None)])
        elif m in ("cmp", "test") and len(ins.operands) == 2 and ins.operands[1].type == cx.X86_OP_IMM:
            v = ins.operands[1].imm & (0xFF if ins.operands[1].size == 1 else 0xFFFF)
            cmps.append(v)
    return {
        "calls": calls,
        "cmps": cmps,
        "stores": stores,
        "imms": sorted(imms),
        "branches": len(g.branches),
        "switches": [len(t) for _, t in g.switches],
        "unresolved": len(g.unresolved),
    }


def main(argv):
    if len(argv) != 3:
        sys.exit(__doc__)
    exe = Path(argv[1]).read_bytes()
    if sha256(exe) != EXE_SHA256:
        sys.exit("not DARKLAND.EXE 483.07 (SHA-256 differs)")
    rt = Rtlink(exe)
    funcs, thunks = load_index(rt)
    strings = string_starts(exe, rt)
    out = {}
    for fn in cfg.load_functions(INDEX, rt):
        try:
            out[fn.name] = analyse(exe, rt, fn, funcs, thunks, strings)
        except Exception as e:  # a function the decoder cannot read is reported, not fatal
            out[fn.name] = {"error": str(e)}
    Path(argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(argv[2]).write_text(json.dumps(out, separators=(",", ":")), encoding="utf-8")
    print("%d functions -> %s" % (len(out), argv[2]))


if __name__ == "__main__":
    main(sys.argv)
