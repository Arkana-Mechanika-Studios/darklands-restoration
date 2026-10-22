"""The structure of every original function, read from the unchanged DARKLAND.EXE bytes.

For each `function` row of docs/source-map/original-functions.csv the function is decoded by
recursive descent (tools/oracle/cfg.py) and these facts are kept, in original coordinates:

  calls     every call the function makes, in address order: near and far calls, calls through
            RTLink thunks (resolved to the thunk's target), jumps out of the extent (tail calls),
            and indirect calls (`*`: through a register or memory), each with the constant words it
            pushes as arguments (first argument first; null for a value computed at run time or an
            address: the offset of a far pointer or of a string literal in DGROUP)
  cmps      the immediate operands of `cmp` / `test` (the thresholds and flags the code tests), not the bound
            of a pointer walk (`cmp bx, 1FDCh`: an address, BX a base or index of the function's memory operands),
            nor the range test of a jump table (`cmp ax, 20h; ja` before a 21h-entry `jmp cs:[bx+..]`: the case
            labels are the values), nor the bound of a counter the function steps by a constant of 2 or more
            (`add word ptr [bp-0Ah], 0A0h` ... `cmp word ptr [bp-0Ah], 1864h`: a row offset, n times the stride)
  stores    the immediates the function writes to memory (`mov [..], imm`: flags, next cards, counts)
  imms      every immediate operand of the function's data instructions (not call or jump targets,
            frame sizes or return pops), as a set
  bytes     the immediates of byte operations on memory or on a high register half (`or byte ptr
            [bx+1], 4`): MSC sets, clears and tests a word's high flags with them (0x400 here)
  branches  the number of conditional branches, switches (with their case count)
  after     for each call, the calls a path of the function's control flow can reach after it (the
            original's order: a shared tail at a lower address still runs after the code that jumps to it)
  pics      the card pictures it names: n for each far pointer pic_names[n] it pushes (`push es:[0A47h + 4n]`,
            card records only), which the port keeps as picture numbers
  seed      whether the function reads or writes the C run-time's random seed DS:7B20 (rand, srand)
  dr, dw    the DGROUP bytes it reads and writes by their address (`mov [99E6h], al`: a global by name)
  iw        whether it writes anything else outside its frame (through a pointer or an index, into
            another segment, a string store, a port or an interrupt): what it writes is not known
  writes    whether the function writes memory outside its stack frame (a store not based on BP or SP,
            a string store, an interrupt); with its calls, structure.py tells the pure queries

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
SEED = (0x7B20, 0x7B22)   # the long rand (root 06A1:29B6) steps
# the card pictures' far name pointers, 4 bytes each, in the segment the card scripts load into ES
PIC_NAMES = 0x0A47


def string_starts(exe, rt):
    """The DGROUP offsets where a text starts (after a NUL, at least three characters, then a NUL; one or two
    from 0x1000 up, where no game constant is pushed: the " " a screen writes between columns): a word
    pushed with one of these values is the address of a string literal, not a constant."""
    s = rt.sections[DATA_IMAGE]
    base = s["file_offset"] + (DGROUP - s["load_seg"]) * 16
    seg = exe[base:base + 0x10000]
    out = set()
    for v in range(1, len(seg)):
        if seg[v - 1] != 0 or seg[v] == 0:
            continue
        end = seg.find(0, v, v + 256)
        if ((end - v >= 3 or (end - v >= 1 and v >= 0x1000))
                and all(32 <= b < 127 or b >= 0x80 or b in (9, 10, 13) for b in seg[v:end])):
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


def reachable_calls(g, call_lins):
    """For each call (by address), the indices of the calls reachable after it along the control flow."""
    switches = dict(g.switches)
    block_of, succ = {}, {}
    for lead, insns in g.blocks.items():
        for a in insns:
            block_of[a] = lead
        last = g.insns[insns[-1]]
        m, nxt = last.mnemonic, insns[-1] + last.size
        if m in cfg.CONDITIONAL:
            out = [x for b in g.branches if b[0] == insns[-1] for x in (b[1], b[2])]
        elif m in cfg.JUMPS:
            op = last.operands[0] if last.operands else None
            if insns[-1] in switches:
                out = switches[insns[-1]]
            elif op is not None and op.type == cx.X86_OP_IMM and m == "jmp":
                out = [insns[-1] - last.address + (op.imm & 0xFFFF)]   # (the instruction's linear base + the target IP)
            else:
                out = []
        elif m in cfg.ENDS or (m == "int" and last.op_str == "0x20"):
            out = []
        else:
            out = [nxt]
        succ[lead] = [x for x in out if x in g.blocks]
    by_block = {}
    for k, a in enumerate(call_lins):
        by_block.setdefault(block_of.get(a), []).append((a, k))
    after = []
    for k, a in enumerate(call_lins):
        lead = block_of.get(a)
        found = {j for b, j in by_block.get(lead, []) if b > a}
        seen, work = set(), list(succ.get(lead, []))
        while work:
            b = work.pop()
            if b in seen:
                continue
            seen.add(b)
            found.update(j for _, j in by_block.get(b, []))
            work.extend(succ.get(b, []))
        after.append(sorted(found))
    return after


def analyse(exe: bytes, rt, fn, funcs, thunks, strings):
    g = cfg.build(exe, rt, fn)
    base = fn.frame * 16
    calls, cmps, stores, imms, byte_imms = [], [], [], set(), set()
    call_lins = []
    high = {cx.X86_REG_AH, cx.X86_REG_BH, cx.X86_REG_CH, cx.X86_REG_DH}
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
    memory = [o.mem for i in g.insns.values() for o in i.operands if o.type == cx.X86_OP_MEM]
    pointers = ({m.base for m in memory} | {m.index for m in memory}) - {0, cx.X86_REG_BP, cx.X86_REG_SP}

    def place(o):
        """A register or a frame slot, by identity (None for anything else)."""
        if o.type == cx.X86_OP_REG:
            return ("r", o.reg)
        if o.type == cx.X86_OP_MEM and o.mem.base == cx.X86_REG_BP and o.mem.index == 0:
            return ("m", o.mem.disp)
        return None

    stepped = {place(i.operands[0]) for i in g.insns.values()
               if i.mnemonic in ("add", "sub") and len(i.operands) == 2 and i.operands[1].type == cx.X86_OP_IMM
               and 2 <= (i.operands[1].imm & 0xFFFF) < 0x8000}
    stepped -= {None, ("r", cx.X86_REG_SP)}
    bounds = {len(t) - 1 for _, t in g.switches}
    writes = seed = iw = False
    pics = set()
    card = "R044" <= fn.record <= "R135"
    dr, dw = set(), set()
    stack = {cx.X86_REG_BP, cx.X86_REG_SP}
    changes_ds = any(i.operands and i.operands[0].type == cx.X86_OP_REG and i.operands[0].reg == cx.X86_REG_DS
                     and i.mnemonic in ("mov", "pop") or i.mnemonic == "lds" for i in g.insns.values())
    ds_dgroup = True
    rmw = {"add", "sub", "adc", "sbb", "inc", "dec", "or", "and", "xor", "neg", "not", "shl", "shr", "sar", "sal",
           "rol", "ror", "rcl", "rcr", "xchg"}
    for lin in sorted(g.insns):
        ins = g.insns[lin]
        m = ins.mnemonic
        pay = fn.payload(lin)
        if lin in g.blocks:
            regs.clear()
            pushes.clear()
            ds_dgroup = lin == fn.linear or not changes_ds
        ops = ins.operands
        if m == "mov" and len(ops) == 2 and ops[0].type == cx.X86_OP_REG and ops[0].reg == cx.X86_REG_DS:
            ds_dgroup = ops[1].type == cx.X86_OP_REG and regs.get(ops[1].reg) == DGROUP
        elif m == "pop" and ops and ops[0].type == cx.X86_OP_REG and ops[0].reg == cx.X86_REG_DS:
            ds_dgroup = True   # (push ds ... pop ds)
        elif m == "lds":
            ds_dgroup = False
        if m not in ("lea", "call", "lcall", "jmp", "ljmp") and not m.startswith("j"):
            for k, o in enumerate(ops):
                if o.type != cx.X86_OP_MEM or o.mem.base in stack or o.mem.segment == cx.X86_REG_SS:
                    continue
                named = (o.mem.base == 0 and o.mem.index == 0 and ds_dgroup
                         and o.mem.segment in (0, cx.X86_REG_DS))
                cells = {(o.mem.disp + b) & 0xFFFF for b in range(max(o.size, 1))}
                written = k == 0 and (m in rmw or m in ("mov", "pop") or m.startswith("set"))
                read = not (k == 0 and m in ("mov", "pop"))
                if written:
                    if named:
                        dw.update(cells)
                    else:
                        iw = True
                if read and named:
                    dr.update(cells)
        if m in ("stosb", "stosw", "movsb", "movsw", "out", "outsb", "outsw") or m.startswith(("rep", "int")):
            iw = True
        if m not in ("call", "lcall", "jmp", "ljmp", "enter", "ret", "retf") and m not in cfg.CONDITIONAL:
            imms.update(o.imm & (0xFF if o.size == 1 else 0xFFFF) for o in ops if o.type == cx.X86_OP_IMM)
            if (len(ops) == 2 and ops[1].type == cx.X86_OP_IMM and ops[0].size == 1
                    and (ops[0].type == cx.X86_OP_MEM or (ops[0].type == cx.X86_OP_REG and ops[0].reg in high))):
                byte_imms.add(ops[1].imm & 0xFF)
        after_segment = prev_segment
        prev_segment = m == "push" and ops[0].type == cx.X86_OP_REG and ops[0].reg in segments
        if (card and m == "push" and ops[0].type == cx.X86_OP_MEM and ops[0].mem.segment == cx.X86_REG_ES
                and ops[0].mem.base == 0 and ops[0].mem.index == 0 and ops[0].mem.disp >= PIC_NAMES
                and (ops[0].mem.disp - PIC_NAMES) % 4 == 0):
            pics.add((ops[0].mem.disp - PIC_NAMES) // 4)
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
        if any(o.type == cx.X86_OP_MEM and o.mem.base == 0 and o.mem.index == 0 and (o.mem.disp & 0xFFFF) in SEED
               and o.mem.segment in (0, cx.X86_REG_DS) for o in ops):
            seed = True
        if (m in ("stosb", "stosw", "movsb", "movsw", "int", "out", "outsb", "outsw") or m.startswith(("rep", "int"))
                or (ops and ops[0].type == cx.X86_OP_MEM and ops[0].mem.base not in stack
                    and m not in ("cmp", "test", "push", "lea", "les", "lds", "call", "lcall", "jmp", "ljmp")
                    and not m.startswith("j"))):
            writes = True
        if m in ("call", "lcall"):
            ops = ins.operands
            if m == "lcall" and len(ops) == 2 and ops[0].type == cx.X86_OP_IMM:
                t = resolve(funcs, thunks, rt, fn.record, (ops[0].imm & 0xFFFF) * 16 + (ops[1].imm & 0xFFFF))
            elif m == "call" and ops and ops[0].type == cx.X86_OP_IMM:
                t = resolve(funcs, thunks, rt, fn.record, base + (ops[0].imm & 0xFFFF))
            else:
                t = "*"
            calls.append([pay, t, args(lin + ins.size)])
            call_lins.append(lin)
            regs.clear()
        elif m in ("jmp", "ljmp"):
            ops = ins.operands
            t = None
            if m == "ljmp" and len(ops) == 2 and ops[0].type == cx.X86_OP_IMM:
                t = (ops[0].imm & 0xFFFF) * 16 + (ops[1].imm & 0xFFFF)
            elif ops and ops[0].type == cx.X86_OP_IMM:
                t = base + (ops[0].imm & 0xFFFF)
            # (`jmp far 0:0`: a graphics-driver entry the loader patches at run time, not a call of root 0000:0000)
            if t is not None and t != 0 and not (fn.linear <= t < fn.linear + fn.size):
                calls.append([pay, resolve(funcs, thunks, rt, fn.record, t), args(None)])
                call_lins.append(lin)
        elif (m in ("cmp", "test") and len(ops) == 2 and ops[1].type == cx.X86_OP_IMM
              and not (ops[0].type == cx.X86_OP_REG and ops[0].reg in pointers and ops[0].size == 2
                       and (ops[1].imm & 0xFFFF) >= 0x100)
              and not (m == "cmp" and place(ops[0]) in stepped)
              and not (m == "cmp" and (ops[1].imm & 0xFFFF) in bounds
                       and getattr(g.insns.get(lin + ins.size), "mnemonic", None) in ("ja", "jbe"))):
            v = ins.operands[1].imm & (0xFF if ins.operands[1].size == 1 else 0xFFFF)
            cmps.append(v)
    return {
        "calls": calls,
        "cmps": cmps,
        "stores": stores,
        "imms": sorted(imms),
        "bytes": sorted(byte_imms),
        "branches": len(g.branches),
        "switches": [len(t) for _, t in g.switches],
        "unresolved": len(g.unresolved),
        "writes": writes,
        "seed": seed,
        "pics": sorted(pics),
        "dr": sorted(dr),
        "dw": sorted(dw),
        "iw": iw,
        "after": reachable_calls(g, call_lins),
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
