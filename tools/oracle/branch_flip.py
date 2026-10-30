"""Condition-directed mutation pilot: for each open branch outcome, find the memory operand its condition tests.

usage: python -I -B branch_flip.py <out.json> <cards: R081=94,95,96,97 ...>   (see the pilot driver in local/)

For an open `taken`/`fallthrough` item of a function (docs/faithfulness/branches-missed.csv) the block of the
conditional jump is decoded (cfg.py); the flag-setting `cmp`/`test`/`or`/`and` before the jump is found, a register
operand is traced back within the block to its last `mov` from a direct memory operand (no data flow beyond the
block), and a value is chosen that makes the jump go the wanted way (flags evaluated for 8/16-bit compares).
An open `block` with a single conditional predecessor is treated as that predecessor's edge.
Result per item: {"mut": [segment-space, address, width, value]} or {"skip": reason}.
"""
from __future__ import annotations

import collections
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import capstone.x86_const as cx  # noqa: E402
import cfg as cfgmod  # noqa: E402

REG = {cx.X86_REG_AX: ("ax", 2), cx.X86_REG_BX: ("bx", 2), cx.X86_REG_CX: ("cx", 2), cx.X86_REG_DX: ("dx", 2),
       cx.X86_REG_SI: ("si", 2), cx.X86_REG_DI: ("di", 2),
       cx.X86_REG_AL: ("ax", 1), cx.X86_REG_AH: ("ax", 1), cx.X86_REG_BL: ("bx", 1), cx.X86_REG_BH: ("bx", 1),
       cx.X86_REG_CL: ("cx", 1), cx.X86_REG_CH: ("cx", 1), cx.X86_REG_DL: ("dx", 1), cx.X86_REG_DH: ("dx", 1)}
FLAGSETTERS = {"cmp", "test", "or", "and", "sub", "add", "dec", "inc", "xor", "neg"}


def flags(op: str, a: int, b: int, w: int):
    m = (1 << (8 * w)) - 1
    sb = 1 << (8 * w - 1)
    a &= m
    b &= m
    if op in ("cmp", "sub"):
        r = (a - b) & m
        cf = a < b
        of = bool(((a ^ b) & (a ^ r)) & sb)
    elif op == "test" or op == "and":
        r = a & b
        cf = of = False
    elif op == "or":
        r = a | b
        cf = of = False
    else:
        return None
    return {"zf": r == 0, "sf": bool(r & sb), "cf": cf, "of": of, "pf": bin(r & 0xFF).count("1") % 2 == 0}


def jtaken(m: str, f) -> bool | None:
    z, s, c, o, p = f["zf"], f["sf"], f["cf"], f["of"], f["pf"]
    t = {"je": z, "jz": z, "jne": not z, "jnz": not z, "jb": c, "jae": not c, "jbe": c or z, "ja": not (c or z),
         "jl": s != o, "jge": s == o, "jle": z or s != o, "jg": not z and s == o, "js": s, "jns": not s,
         "jo": o, "jno": not o, "jp": p, "jnp": not p}
    return t.get(m)


def direct_mem(op):
    """(segment, address, width) of a direct memory operand (no base/index) else None."""
    if op.type != cx.X86_OP_MEM or op.mem.base or op.mem.index:
        return None
    seg = {cx.X86_REG_DS: "ds", 0: "ds", cx.X86_REG_ES: "es", cx.X86_REG_CS: "cs", cx.X86_REG_SS: "ss"}.get(op.mem.segment, "?")
    return seg, op.mem.disp & 0xFFFF, op.size


def classify_mem(op):
    """reason string for a non-direct memory operand."""
    b = op.mem.base
    if b in (cx.X86_REG_BP,):
        return "local"
    if op.mem.segment == cx.X86_REG_ES or op.mem.segment == cx.X86_REG_FS:
        return "far-pointer"
    return "indirect"


def trace(insns, k, reg_ref):
    """Source of register operand reg_ref used at insns[k]: ('imm', v, d) | ('mem', seg, addr, width, d) | ('call', insn) | ('skip', why).
    d is the sum of the dec/inc/add/sub-immediate steps applied to the loaded value in between."""
    name, w = REG[reg_ref]
    d = 0
    for j in range(k - 1, -1, -1):
        i = insns[j]
        m = i.mnemonic
        if m in ("call", "lcall"):
            if name in ("ax", "dx", "cx"):
                return ("call", i)
            continue
        ops = i.operands
        if m == "cbw" and name == "ax":
            continue
        if m in ("cwd", "cwde") and name == "dx":
            return ("skip", "sign-extend")
        if m in ("push", "cmp", "test", "jmp") or not ops:
            continue
        if not (ops[0].type == cx.X86_OP_REG and ops[0].reg in REG and REG[ops[0].reg][0] == name):
            if m in ("mul", "imul", "div", "idiv", "xchg", "lds", "les") and name in ("ax", "dx"):
                return ("skip", "computed-" + m)
            continue
        if m == "dec":
            d -= 1
            continue
        if m == "inc":
            d += 1
            continue
        if m in ("sub", "add") and ops[1].type == cx.X86_OP_IMM:
            d += ops[1].imm if m == "add" else -ops[1].imm
            continue
        if m == "mov":
            s = ops[1]
            if s.type == cx.X86_OP_IMM:
                return ("imm", s.imm, d)
            if s.type == cx.X86_OP_MEM:
                dm = direct_mem(s)
                if dm:
                    return ("mem",) + dm + (d,)
                return ("skip", classify_mem(s))
            if s.type == cx.X86_OP_REG:
                return ("skip", "register-copy")
        if m == "xor" and ops[1].type == cx.X86_OP_REG and ops[1].reg == ops[0].reg:
            return ("imm", 0, d)
        return ("skip", "computed-" + m)
    return ("skip", "no-def-in-block")


def chain(g, pred, lst):
    """Instructions of a block preceded by the blocks that only fall into it through a conditional jump."""
    out = [g.insns[x] for x in lst]
    first = lst[0]
    for _ in range(40):
        ps = pred.get(first, [])
        if len(ps) != 1 or ps[0][1]:
            break
        jl = ps[0][0]
        pb = next((l for l in g.blocks.values() if jl in l), None)
        if pb is None or pb[0] == first:
            break
        out = [g.insns[x] for x in pb] + out
        first = pb[0]
    return out


# event helpers of the overlay-shared rules record: (arguments by name, in call order) -> event fields
EVENT_HELPERS = {"0xe76, 0x360c": ("tag", "type", "location", 0x1C),        # fact(tag, type, location)
                 "0xe76, 0x3878": ("kind", "location"),                   # event at location
                 "0xe76, 0x392c": ("kind", "type", "location")}           # event type at location
LOCATION_NOW = 0x907E


def helper(insns, call, sa, sb, opf, jm, want_taken, self_test, unary):
    """The tested value is a helper's return: handle the event queries, whose result is true when a started event matches."""
    name = call.op_str
    spec = EVENT_HELPERS.get(name)
    if spec is None:
        return {"skip": "helper-return", "helper": name}
    names = spec[:-1] if isinstance(spec[-1], int) else spec
    c = insns.index(next(x for x in insns if x.address == call.address))
    args = []
    for j in range(c - 1, -1, -1):
        i = insns[j]
        if len(args) == len(names):
            break
        if i.mnemonic != "push":
            return {"skip": "helper-arg-not-push"}
        o = i.operands[0]
        if o.type == cx.X86_OP_IMM:
            args.append(o.imm)
        elif o.type == cx.X86_OP_MEM and direct_mem(o) and direct_mem(o)[1] == LOCATION_NOW:
            args.append("cur")
        else:
            return {"skip": "helper-arg-not-constant"}
    if len(args) < len(names):
        return {"skip": "helper-args-in-predecessor"}
    other = sb if sa[0] == "call" else sa
    kk = None if (self_test or unary) else other[1]
    res = {}
    for v in (0, 1):
        if self_test:
            f = flags(opf, v, v, 2)
        elif sa[0] == "call":
            f = flags(opf, v, kk, 2)
        else:
            f = flags(opf, kk, v, 2)
        t = jtaken(jm, f) if f else None
        if t is None:
            return {"skip": "jcc-" + jm}
        res[v] = t
    if res[1] == want_taken and res[0] != want_taken:
        ev = dict(zip(names, args))
        if spec[-1] == 0x1C and isinstance(spec[-1], int):
            ev["kind"] = 0x1C
        return {"event": ev, "helper": name}
    if res[0] == want_taken and res[1] != want_taken:
        return {"skip": "event-absent-side"}
    return {"skip": "helper-indifferent"}


def analyse(fn, g, jcc_lin, want_taken, pred):
    """Return ({"mut"...} | {"skip"...}) for the jump at jcc_lin going the wanted way."""
    lst = next((l for l in g.blocks.values() if jcc_lin in l), None)
    if lst is None:
        return {"skip": "no-block"}
    insns = chain(g, pred, lst)
    k = next(n for n, x in enumerate(insns) if x.address == g.insns[jcc_lin].address)
    jm = insns[k].mnemonic
    for s in range(k - 1, -1, -1):
        i = insns[s]
        if i.mnemonic in FLAGSETTERS:
            break
        if i.mnemonic in ("call", "lcall"):
            return {"skip": "flags-from-call"}
    else:
        return {"skip": "flags-from-predecessor"}
    op = i.mnemonic
    ops = i.operands
    if op in ("neg", "xor") or (op in ("add",) and ops[0].type == cx.X86_OP_REG and ops[0].reg == cx.X86_REG_SP):
        return {"skip": "arith-flags", "helper": op + " " + i.op_str}
    a = ops[0]
    w = a.size
    if op in ("dec", "inc"):
        b = None
        sub_imm = 1
        opf = "sub" if op == "dec" else "add"
    else:
        b = ops[1]
        sub_imm = None
        opf = op
    def src(o):
        if o.type == cx.X86_OP_IMM:
            return ("imm", o.imm, 0)
        if o.type == cx.X86_OP_MEM:
            d = direct_mem(o)
            return ("mem",) + d + (0,) if d else ("skip", classify_mem(o))
        if o.type == cx.X86_OP_REG:
            return trace(insns, s, o.reg)
        return ("skip", "operand")
    sa = src(a)
    if b is None:
        sb = ("imm", sub_imm, 0)
    else:
        sb = src(b)
    self_test = b is not None and b.type == cx.X86_OP_REG and a.type == cx.X86_OP_REG and a.reg == b.reg and op in ("test", "or", "and")
    if self_test:
        sb = sa
    for x in (sa, sb):
        if x[0] == "skip":
            return {"skip": x[1]}
        if x[0] == "call":
            return helper(insns, x[1], sa, sb, opf, jm, want_taken, self_test, b is None)
    if sa[0] == "mem" and sb[0] == "mem" and not self_test:
        return {"skip": "mem-vs-mem"}
    if sa[0] == "imm" and sb[0] == "imm":
        return {"skip": "constant-compare"}
    if self_test or sa[0] == "mem":
        mem, other, mem_first = sa, sb, True
    else:
        mem, other, mem_first = sb, sa, False
    if mem[1] != "ds":
        return {"skip": "segment-" + mem[1]}
    addr, mw, dl = mem[2], mem[3], mem[4]
    mw = min(mw, w) if mw else w
    kk = None if self_test else other[1]
    cands = list(range(0, 13)) + [0xFF, 0xFFFF, 0x7FFF, 0x8000, 0x80, 0x7F]
    if kk is not None:
        cands += [kk, kk - 1, kk + 1]
    msk = (1 << (8 * mw)) - 1
    for c in cands:
        val = c & msk                       # value at the flag setter
        if self_test:
            f = flags(opf, val, val, mw)
        elif mem_first:
            f = flags(opf, val, kk, mw)
        else:
            f = flags(opf, kk, val, mw)
        t = jtaken(jm, f) if f else None
        if t is None:
            return {"skip": "jcc-" + jm}
        if t == want_taken:
            return {"mut": ["ds", addr, mw, (val - dl) & msk], "at": hex(i.address)}
    return {"skip": "no-value"}


def run(recs_cards, opens, funcs, exe, rt, out):
    """recs_cards: {record: [cards]}; opens: {function: set((payload, kind))} -> results per item."""
    res = {}
    fnmap = {f.name: f for f in funcs}
    for name, items in opens.items():
        fn = fnmap.get(name)
        if fn is None:
            continue
        base = fn.linear - fn.payload(fn.linear)
        g = cfgmod.build(exe, rt, fn)
        pred = collections.defaultdict(list)
        for j, t, f, m in g.branches:
            if t != f:
                pred[t].append((j, True))
                pred[f].append((j, False))
        jt = {j - base: (j, t, f) for j, t, f, m in g.branches}
        for payload, kind in sorted(items):
            key = "%s|%s|%s" % (name, payload, kind)
            p = int(payload, 16)
            if kind in ("taken", "fallthrough"):
                if p not in jt:
                    res[key] = {"skip": "not-a-branch-of-the-decode"}
                    continue
                res[key] = analyse(fn, g, jt[p][0], kind == "taken", pred)
            else:
                ps = pred.get(p + base, [])
                if len(ps) != 1:
                    res[key] = {"skip": "block-with-%d-edges" % len(ps)}
                    continue
                r = analyse(fn, g, ps[0][0], ps[0][1], pred)
                r["via"] = hex(ps[0][0] - base)
                res[key] = r
    return res
