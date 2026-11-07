"""Directed mutation of the open branch outcomes of the card scripts (opt-in; writes nothing into the committed corpora).

For each open outcome at the coverage frontier (its conditional jump ran, only the other way: the sibling outcome is exercised)
the compare that decides it is read statically, its operand traced to where the value comes from (a saved-game word, a member's
field, a helper's return such as a random roll or an event query, a local set earlier in the function), and, where the operand
is a value a start state or a controlled service result holds, run keys are written that put the value on each side of the
threshold (the boundary pair: the wanted side's value nearest the other side, and that one). Handled: a random roll (`R<k>_<v>`:
the k-th draw of the run returns v, applied identically on both sides: card_oracle.py draws_of, ForcedDraws.cs), the prayer's
result (Y parts), the battle's result (B parts), an event at the current place (ev / er atoms), the saved-game words of
GLOBAL_ATOMS (pc, px, py, ml, sz; tests/Darklands.Cards.Tests/Oracle/CardOracleAtoms.cs). The draw index and the operand's
value at the compare come from a watch run of the reaching run on the original (a read-only hook at the flag setter).
The keys are run like any card-oracle key: the original (card_oracle.py, with coverage) and the C# replay
(CardOracleTests.EveryCardAgreesWithTheOriginal); only agreeing runs count. Existing keys and recordings are unchanged.

usage: python -I -B mutate_branch.py classify <branches-missed.csv> <out.json> [--records 44-135]
       python -I -B mutate_branch.py reach <classified.json> <cov root> [<cov root> ...] --out <reach.json>
       python -I -B mutate_branch.py keys <reach.json | jobs.json> <keys.txt> [--per-item 2]   (then CardAimSearch.ExportKeys)
       python -I -B mutate_branch.py watch <classified.json> <reach.json> <states> <watch.json>
       python -I -B mutate_branch.py jobs <classified.json> <watch.json> <jobs.json>
       python -I -B mutate_branch.py run <jobs.json> <states> <work dir> [--workers N]   (-> work/cov, work/corpus.json.gz)
       python -I -B mutate_branch.py table <classified.json> <jobs.json> <work dir>   (after the replay and cov_gain.py --out work/gained.json)
The oracle steps (watch, run) and the replay hold the heavy lock of the helpers' rules.
"""
from __future__ import annotations

import collections
import csv
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
import capstone.x86_const as cx  # noqa: E402
import cfg as cfgmod  # noqa: E402
from branch_flip import FLAGSETTERS, REG, direct_mem, flags, jtaken  # noqa: E402
from run_unit import Rtlink  # noqa: E402

GAME = Path(__import__("os").environ.get("DARKLANDS_DIR", REPO.parent.parent / "game" / "DARKLAND"))

# helpers whose return a start state or a forced draw decides (link-time address -> kind)
RANDOM = {"0000:0C70": "below", "0410:0008": "scaled", "06A1:29B6": "raw"}


def load_index(rt):
    """linked 'SSSS:OOOO' -> coordinate (a thunk resolved to its target), and the names the C#'s `orig:` notes give."""
    linked = {}
    for r in csv.DictReader(open(REPO / "docs" / "source-map" / "original-functions.csv", encoding="utf-8")):
        if r["kind"] == "thunk" and r["target"]:
            linked[r["linked"]] = r["target"].split(" ")[0]
        elif r["kind"] == "function":
            linked[r["linked"]] = r["coordinate"]
    names = {}
    pat = re.compile(r"orig: ([a-z][a-z0-9-]+) (R\d{3}\+0x[0-9A-F]+|root [0-9A-F]{4}:[0-9A-F]{4})")
    for f in (REPO / "src").rglob("*.cs"):
        for m in pat.finditer(f.read_text(encoding="utf-8", errors="replace")):
            names.setdefault(m.group(2), m.group(1))
    return linked, names


def call_target(ins):
    """'SSSS:OOOO' of a far call with an immediate target, else None."""
    if ins.mnemonic != "lcall" or len(ins.operands) != 2:
        return None
    a, b = ins.operands
    if a.type != cx.X86_OP_IMM or b.type != cx.X86_OP_IMM:
        return None
    return "%04X:%04X" % (a.imm & 0xFFFF, b.imm & 0xFFFF)


def pushed_args(insns, k, n=4):
    """The immediate / direct-memory arguments pushed before the call at insns[k] (first argument first)."""
    out = []
    for j in range(k - 1, max(-1, k - 12), -1):
        i = insns[j]
        if i.mnemonic in ("call", "lcall") or len(out) == n:
            break
        if i.mnemonic != "push":
            continue
        o = i.operands[0]
        if o.type == cx.X86_OP_IMM:
            out.append(o.imm & 0xFFFF)
        elif o.type == cx.X86_OP_MEM and direct_mem(o):
            out.append("ds:%04X" % direct_mem(o)[1])
        elif o.type == cx.X86_OP_MEM and o.mem.base == cx.X86_REG_BP:
            out.append("bp%+d" % o.mem.disp)
        else:
            out.append("?")
    return out


class Fn:
    def __init__(self, exe, rt, fn):
        self.fn = fn
        self.g = cfgmod.build(exe, rt, fn)
        self.base = fn.linear - fn.payload(fn.linear)
        self.order = sorted(self.g.insns)
        self.pos = {a: n for n, a in enumerate(self.order)}
        self.pred = collections.defaultdict(list)
        for j, t, f, m in self.g.branches:
            if t != f:
                self.pred[t].append((j, True))
                self.pred[f].append((j, False))
        self.block_of = {}
        for lead, lst in self.g.blocks.items():
            for a in lst:
                self.block_of[a] = lead

    def chain(self, lin):
        """The instructions of the block of lin, preceded by the blocks that reach it only by falling through or one jump."""
        lead = self.block_of[lin]
        lst = self.g.blocks[lead]
        out = [self.g.insns[x] for x in lst if x <= lin]
        first = lead
        for _ in range(8):
            ps = self.pred.get(first, [])
            prev = self.order[self.pos[first] - 1] if self.pos[first] > 0 else None
            falls = prev is not None and self.g.insns[prev].mnemonic not in ("jmp", "ljmp", "ret", "retf") and prev in self.block_of
            srcs = [p for p, _ in ps] + ([prev] if falls and prev not in [p for p, _ in ps] else [])
            if len(srcs) != 1:
                break
            pb = self.block_of.get(srcs[0])
            if pb is None or pb == first:
                break
            out = [self.g.insns[x] for x in self.g.blocks[pb] if x <= srcs[0]] + out
            first = pb
        return out

    def stores_to_local(self, disp):
        """Every instruction of the function that writes [bp+disp] (mov / inc / dec / add / sub / and / or)."""
        out = []
        for a in self.order:
            i = self.g.insns[a]
            if not i.operands:
                continue
            o = i.operands[0]
            if o.type == cx.X86_OP_MEM and o.mem.base == cx.X86_REG_BP and o.mem.index == 0 and o.mem.disp == disp \
                    and i.mnemonic in ("mov", "inc", "dec", "add", "sub", "and", "or", "xor", "pop"):
                out.append(a)
        return out


def describe(F, insns, k, op, linked, names, depth=0):
    """Where the value of operand op of insns[k] comes from."""
    if op.type == cx.X86_OP_IMM:
        return {"src": "imm", "v": op.imm & 0xFFFF}
    if op.type == cx.X86_OP_MEM:
        dm = direct_mem(op)
        if dm:
            return {"src": "global", "seg": dm[0], "addr": "%04X" % dm[1], "w": dm[2]}
        if op.mem.base == cx.X86_REG_BP and not op.mem.index:
            if op.mem.disp > 0:
                return {"src": "argument", "bp": op.mem.disp, "w": op.size}
            if depth > 1:
                return {"src": "local", "bp": op.mem.disp, "w": op.size, "stores": "deep"}
            stores = []
            for a in F.stores_to_local(op.mem.disp):
                si = F.g.insns[a]
                if si.mnemonic == "mov" and si.operands[1].type == cx.X86_OP_IMM:
                    stores.append({"src": "imm", "v": si.operands[1].imm & 0xFFFF})
                elif si.mnemonic == "mov" and si.operands[1].type == cx.X86_OP_REG:
                    ch = F.chain(a)
                    stores.append(describe(F, ch, len(ch) - 1, si.operands[1], linked, names, depth + 1))
                elif si.mnemonic == "mov":
                    ch = F.chain(a)
                    stores.append(describe(F, ch, len(ch) - 1, si.operands[1], linked, names, depth + 1))
                else:
                    stores.append({"src": "update-" + si.mnemonic})
            return {"src": "local", "bp": op.mem.disp, "w": op.size, "stores": stores}
        if op.mem.segment == cx.X86_REG_ES:
            return {"src": "far-pointer"}
        return {"src": "indexed", "disp": "%04X" % (op.mem.disp & 0xFFFF), "w": op.size}
    if op.type == cx.X86_OP_REG and op.reg in REG:
        name, w = REG[op.reg]
        d = 0
        for j in range(k - 1, -1, -1):
            i = insns[j]
            m = i.mnemonic
            if m in ("call", "lcall"):
                if name in ("ax", "dx"):
                    t = call_target(i)
                    coord = linked.get(t) if t else None
                    out = {"src": "return", "call": t, "callee": coord, "name": names.get(coord), "args": pushed_args(insns, j), "d": d}
                    if t in RANDOM:
                        out["random"] = RANDOM[t]
                    return out
                continue
            ops = i.operands
            if m in ("push", "cmp", "test", "jmp") or not ops:
                if m == "cbw" and name == "ax":
                    continue
                continue
            if not (ops[0].type == cx.X86_OP_REG and ops[0].reg in REG and REG[ops[0].reg][0] == name):
                if m in ("mul", "imul", "div", "idiv", "lds", "les") and name in ("ax", "dx"):
                    return {"src": "computed-" + m}
                continue
            if m in ("dec", "inc"):
                d += -1 if m == "dec" else 1
                continue
            if m in ("sub", "add") and ops[1].type == cx.X86_OP_IMM:
                d += ops[1].imm if m == "add" else -ops[1].imm
                continue
            if m == "mov":
                r = describe(F, insns, j, ops[1], linked, names, depth)
                if d:
                    r["d"] = d
                return r
            if m in ("xor", "sub") and ops[1].type == cx.X86_OP_REG and ops[1].reg == ops[0].reg:
                return {"src": "imm", "v": 0, "d": d}
            if m in ("sub", "add") and ops[1].type == cx.X86_OP_MEM:
                return {"src": "difference", "of": describe(F, insns, j, ops[1], linked, names, depth)}
            return {"src": "computed-" + m}
        return {"src": "from-predecessor"}
    return {"src": "operand"}


def analyse(F, jcc_lin, linked, names):
    insns = F.chain(jcc_lin)
    k = len(insns) - 1
    jm = insns[k].mnemonic
    for s in range(k - 1, -1, -1):
        i = insns[s]
        if i.mnemonic in FLAGSETTERS:
            break
        if i.mnemonic in ("call", "lcall"):
            return {"kind": "flags-from-call", "jcc": jm}
    else:
        return {"kind": "flags-from-predecessor", "jcc": jm}
    ops = i.operands
    res = {"jcc": jm, "op": i.mnemonic, "at": "%04X" % (F.fn.frame * 16 + i.address - F.base),
           "insn": "%s %s" % (i.mnemonic, i.op_str), "w": ops[0].size if ops else 0}
    res["a"] = describe(F, insns, s, ops[0], linked, names) if ops else {"src": "none"}
    if len(ops) > 1:
        res["b"] = describe(F, insns, s, ops[1], linked, names)
    elif i.mnemonic in ("dec", "inc"):
        res["b"] = {"src": "imm", "v": 1}
    if len(ops) > 1 and ops[0].type == cx.X86_OP_REG and ops[1].type == cx.X86_OP_REG and ops[0].reg == ops[1].reg:
        res["self"] = True
    res["kind"] = category(res)
    return res


def leaf(d):
    """The decisive source of a descriptor (a local with a single store kind resolves to that store)."""
    if d.get("src") == "local" and isinstance(d.get("stores"), list):
        kinds = {json.dumps({k: v for k, v in s.items() if k not in ("v", "d", "args")}, sort_keys=True) for s in d["stores"]}
        if len(d["stores"]) and all(s.get("src") == "imm" for s in d["stores"]):
            return {"src": "local-constants", "values": sorted({s["v"] for s in d["stores"]})}
        nonimm = [s for s in d["stores"] if s.get("src") != "imm"]
        if len(nonimm) == 1 or (nonimm and len(kinds) <= 2):
            return leaf(nonimm[0])
        return {"src": "local-mixed"}
    if d.get("src") == "difference":
        return leaf(d["of"])
    return d


def category(r):
    a, b = leaf(r.get("a", {})), leaf(r.get("b", {}))
    srcs = [x for x in (a, b) if x.get("src") != "imm"]
    if not srcs:
        return "constant"
    x = srcs[0]
    s = x["src"]
    if s == "return":
        if x.get("random"):
            return "random"
        return "helper:" + (x.get("name") or x.get("callee") or str(x.get("call")))
    if s == "global":
        return "global"
    return s


def classify(missed, out, lo=44, hi=135):
    exe = (GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    linked, names = load_index(rt)
    rows = collections.defaultdict(dict)
    for r in csv.DictReader(open(missed, encoding="utf-8")):
        if not r["record"].startswith("R") or not lo <= int(r["record"][1:]) <= hi:
            continue
        rows[r["function"]][(r["payload"], r["kind"])] = r["status"]
    res = {}
    for name, items in sorted(rows.items()):
        fn = funcs.get(name)
        if fn is None:
            continue
        F = Fn(exe, rt, fn)
        jt = {j - F.base: (j, t, f) for j, t, f, m in F.g.branches}
        for (payload, kind), status in sorted(items.items()):
            if status != "open" or kind == "block":
                continue
            p = int(payload, 16)
            other = "fallthrough" if kind == "taken" else "taken"
            frontier = (payload, other) not in items      # the jump ran: the other outcome is exercised
            key = "%s|%s|%s" % (name, payload, kind)
            if p not in jt:
                res[key] = {"frontier": frontier, "kind": "not-a-branch"}
                continue
            a = analyse(F, jt[p][0], linked, names)
            a["frontier"] = frontier
            a["want_taken"] = kind == "taken"
            res[key] = a
    json.dump(res, open(out, "w"), indent=1)
    fr = [v for v in res.values() if v["frontier"]]
    print("open outcomes", len(res), "frontier", len(fr))
    for k, n in collections.Counter(v["kind"] for v in fr).most_common(40):
        print("%5d  %s" % (n, k))


def reach(classified, roots, out, keep=6):
    """The card runs (unit, state) whose coverage holds the conditional jump of each frontier outcome (streams cov-*.jsonl line by
    line; per item the `keep` cheapest runs, by their recorded seconds)."""
    import glob
    import heapq
    import os
    import time
    res = json.load(open(classified))
    exe = (GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    want = collections.defaultdict(dict)      # record -> payload of the jump -> [item keys]
    for key, v in res.items():
        if not v.get("frontier") or v.get("kind") == "not-a-branch":
            continue
        name, payload, _ = key.split("|")
        want[funcs[name].record].setdefault(int(payload, 16), []).append(key)
    best = collections.defaultdict(list)       # item -> heap of (-seconds, n, unit, state, folder)
    n = 0
    t0 = time.time()
    unit_re = re.compile(r'{"unit": ?"(card [0-9A-F]+)", ?"state": ?"([^"]*)"')
    sec_re = re.compile(r'"seconds": ?([0-9.]+)')
    files = []
    for root in roots:
        files += sorted(glob.glob(os.path.join(root, "**", "cov-*.jsonl"), recursive=True))
    for fi, f in enumerate(files):
        folder = os.path.dirname(f)
        with open(f, encoding="utf-8") as fh:
            for line in fh:
                m = unit_re.match(line)
                if not m:
                    continue
                recs = [r for r in want if '"%s":' % r in line]
                if not recs:
                    continue
                j = json.loads(line)
                sec = float(j.get("seconds", 0) or 0)
                for r in recs:
                    have = j["insns"].get(r)
                    if not have:
                        continue
                    hit = set(have).intersection(want[r])
                    for p in hit:
                        for item in want[r][p]:
                            h = best[item]
                            n += 1
                            entry = (-sec, n, m.group(1), m.group(2), folder)
                            if len(h) < keep:
                                heapq.heappush(h, entry)
                            elif entry[0] > h[0][0]:
                                heapq.heapreplace(h, entry)
        if fi % 200 == 0:
            print("%d/%d files, %d items reached, %ds" % (fi, len(files), len(best), time.time() - t0), flush=True)
    outd = {k: [{"unit": u, "state": s, "seconds": -ns, "folder": fo} for ns, _, u, s, fo in sorted(h, reverse=True)] for k, h in best.items()}
    json.dump(outd, open(out, "w"), indent=0)
    print("reached", len(outd), "of", sum(1 for v in res.values() if v.get("frontier")))


# ---- runs -------------------------------------------------------------------------------------------------------------------
HAND = re.compile(r"^M(\d+)L(\d+)(H?)(?:S(\d+))?$")
NOSTATE = re.compile(r"^(F\d+\.\d+|P\d+(_\d+)*|B\d(_\d)*|Y[cfa](_[cfa])*|T\$\w+|R\d+_\d+)$")


def parse_state(state):
    """A coverage line's state: 'key' (a plain + probe unit) or 'key M1L7HS2' (a handler call) -> (key, hand | None); None: not rerunnable."""
    p = state.split(" ")
    if len(p) == 1:
        return p[0], None
    if len(p) == 2 and HAND.match(p[1]):
        m = HAND.match(p[1])
        return p[0], {"menu": int(m.group(1)), "line": int(m.group(2)), "hints": bool(m.group(3)),
                      "slot": int(m.group(4)) if m.group(4) else None}
    return None


def hand_spec(h):
    return "M%dL%d%s%s" % (h["menu"], h["line"], "H" if h.get("hints") else "", "S%d" % h["slot"] if h.get("slot") is not None else "")


def state_key(key):
    """The state file name of a run key (the parts that change no state dropped), as CardAimSearch.StateKey."""
    if "-V" not in key:
        return key
    head, rest = key.split("-V", 1)
    variant, option = rest.rsplit("-O", 1)
    keep = [p for p in variant.split("+") if not NOSTATE.match(p)]
    return head + ("-V" + "+".join(keep) if keep else "") + "-O" + option


def with_parts(key, parts):
    """The run key with variant parts added: L3-O5 + [R2_7] -> L3-VR2_7-O5."""
    if not parts:
        return key
    head, option = key.rsplit("-O", 1)
    if "-V" in head:
        return head + "+" + "+".join(parts) + "-O" + option
    return head + "-V" + "+".join(parts) + "-O" + option


def option_of(key):
    return int(key.rsplit("-O", 1)[1])


def operand_value(m, ins, op):
    """The value of a capstone operand on the machine (before the instruction runs)."""
    from dosmachine import linear as lin
    if op.type == cx.X86_OP_IMM:
        return op.imm & 0xFFFF
    if op.type == cx.X86_OP_REG:
        name = ins.reg_name(op.reg).upper()
        if name in ("AL", "BL", "CL", "DL"):
            return m.reg(name[0] + "X") & 0xFF
        if name in ("AH", "BH", "CH", "DH"):
            return m.reg(name[0] + "X") >> 8
        return m.reg(name)
    if op.type == cx.X86_OP_MEM:
        ea = op.mem.disp
        for r in (op.mem.base, op.mem.index):
            if r:
                ea += m.reg(ins.reg_name(r).upper())
        seg = {cx.X86_REG_ES: "ES", cx.X86_REG_SS: "SS", cx.X86_REG_CS: "CS"}.get(op.mem.segment)
        if seg is None:
            seg = "SS" if op.mem.base == cx.X86_REG_BP else "DS"
        b = bytes(m.uc.mem_read(lin(m.reg(seg), ea & 0xFFFF), op.size))
        return int.from_bytes(b, "little")
    return None


def worker_oracle(states, coverage):
    import card_oracle as co
    return co.Oracle((GAME / "DARKLAND.EXE").read_bytes(), str(GAME), Path(states), coverage=coverage)


def watch(classified, reachf, states, out, per_item=2):
    """Runs the reaching run of each frontier item on the original with a hook at the item's flag setter: per item the first hits'
    (draws so far, operand values) and whether the jump went the wanted way."""
    import time
    from dosmachine import P
    res = json.load(open(classified))
    rch = json.load(open(reachf))
    exe = (GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    o = worker_oracle(states, False)
    import capstone
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    md.detail = True
    hooks = {}                       # item -> (seg, ip, record)
    by_run = collections.defaultdict(set)
    for item, v in res.items():
        if not v.get("frontier") or "at" not in v or item not in rch:
            continue
        name = item.split("|")[0]
        fn = funcs[name]
        ip = fn.entry_ip + (int(v["at"], 16) - fn.payload(fn.linear))
        hooks[item] = (fn.frame, ip, fn.record)
        n = 0
        for r in rch[item]:
            ps = parse_state(r["state"])
            if ps is None or not (Path(states) / ("state-%s.sav" % state_key(ps[0]))).exists():
                continue
            by_run[(int(r["unit"].split()[1], 16), r["state"])].add(item)
            n += 1
            if n >= per_item:
                break
    hits = collections.defaultdict(list)
    current = {"items": set()}
    code = {}

    def make(item, seg, ip, rec):
        def h(m):
            if item not in current["items"] or len(hits[item]) >= 4 or o.run is None:
                return
            b = bytes(m.uc.mem_read((m.reg("CS") << 4) + m.reg("IP"), 16))
            ins = next(md.disasm(b, m.reg("IP")))
            vals = [operand_value(m, ins, op) for op in ins.operands]
            hits[item].append({"card": current["card"], "run": current["run"], "draws": o.run.draws, "vals": vals, "insn": "%s %s" % (ins.mnemonic, ins.op_str),
                               "menus": o.run.menus, "mlog": [list(x) for x in o.run.menu_log]})
        return h

    for item, (seg, ip, rec) in hooks.items():
        o.m.hook(P(seg), ip, make(item, seg, ip, rec), record=rec)
    t0 = time.time()
    for (card, state), items in sorted(by_run.items()):
        key, hand = parse_state(state)
        current["items"] = items
        current["card"] = card
        for probe in ((True,) if hand else (False, True)):
            current["run"] = "%s %s" % ("hand" if hand else ("probe" if probe else "plain"), state)
            try:
                o.run_card(card, key, option_of(key), probe, hand)
            except Exception as e:      # noqa: BLE001 - the oracle's failures are data here
                print("watch failed", card, state, str(e)[:120], flush=True)
                o = worker_oracle(states, False)
                for item2, (seg, ip, rec) in hooks.items():
                    o.m.hook(P(seg), ip, make(item2, seg, ip, rec), record=rec)
    json.dump({"hits": hits, "runs": {"%X|%s" % k: sorted(v) for k, v in by_run.items()}}, open(out, "w"), indent=0)
    print("watched", len(by_run), "runs,", len(hits), "items hit,", round(time.time() - t0), "s")


def values_for(v, hit):
    """The values of the decisive operand that put the compare on each side of its threshold: [(value, taken), ...], the wanted side first."""
    vals = hit["vals"]
    op, jm = v["op"], v["jcc"]
    w = max(1, v.get("w") or 2)
    a_dec = leaf(v.get("a", {})).get("src") != "imm"
    if op in ("dec", "inc"):
        other = 1
    elif v.get("self"):
        other = None
    elif len(vals) > 1:
        other = vals[1] if a_dec else vals[0]
    else:
        return []
    opf = {"dec": "sub", "inc": "add"}.get(op, op)
    cur = vals[0] if a_dec else (vals[1] if len(vals) > 1 else None)
    cands = set(range(-2, 13)) | {0x7F, 0x80, 0xFF, 0x7FFF, 0x8000, 0xFFFF}
    for base in (other, cur):
        if isinstance(base, int):
            cands |= {base - 2, base - 1, base, base + 1, base + 2}
    msk = (1 << (8 * w)) - 1
    out = {}
    for c in sorted(cands):
        val = c & msk
        if v.get("self"):
            f = flags(opf, val, val, w)
        elif a_dec:
            f = flags(opf, val, other, w)
        else:
            f = flags(opf, other, val, w)
        t = jtaken(jm, f) if f else None
        if t is not None:
            out.setdefault(t, []).append(c)
    if True not in out or False not in out:
        return [(out.get(v["want_taken"], [None])[0], v["want_taken"])] if v["want_taken"] in out else []
    # the boundary: the wanted side's value nearest the other side's, and that one
    want, other_side = out[v["want_taken"]], out[not v["want_taken"]]
    best = min(((abs(x - y), x, y) for x in want for y in other_side if -2 <= x <= 0x8000 and -2 <= y <= 0x8000), default=None)
    if best is None:
        return [(want[0], v["want_taken"])]
    return [(best[1], v["want_taken"]), (best[2], not v["want_taken"])]


GLOBAL_ATOMS = {"A88D": "pc", "E3D0": "px", "E3D2": "py", "A891": "ml", "A67E": "sz"}


def mutations(v, hit):
    """Run key parts that put the decisive operand at each value of values_for: [(parts, value, taken), ...] or (None, reason)."""
    src = leaf(v.get("a", {}))
    if src.get("src") == "imm":
        src = leaf(v.get("b", {}))
    kind = v["kind"]
    pairs = values_for(v, hit)
    if not pairs:
        return None, "no-threshold"
    d = src.get("d", 0) or 0
    out = []
    if kind == "random":
        k = hit["draws"] - 1
        if k < 0:
            return None, "no-draw-before"
        n = src.get("args", [None])[0]
        for val, t in pairs:
            roll = val - d
            if src["random"] == "below":
                if not isinstance(n, int) or not 0 <= roll < max(n, 2):
                    continue
                raw = roll
            elif src["random"] == "raw":
                if not 0 <= roll < 0x8000:
                    continue
                raw = roll
            else:   # scaled: ((raw << 1) & 0xFFFF) * n >> 16
                if not isinstance(n, int):
                    continue
                raw = next((r for r in range(0x8000) if ((((r << 1) & 0xFFFF) * n) >> 16) == roll), None)
                if raw is None:
                    continue
            out.append((["R%d_%d" % (k, raw)], val, t))
    elif kind == "helper:call-on-saint":
        for val, t in pairs:
            y = {0xFFFF: "c", -1: "c", 0: "f", 1: "a"}.get(val - d if val - d != 0xFFFF else -1)
            if y:
                out.append((["Y" + y], val, t))
    elif kind == "helper:battle-driver":
        for val, t in pairs:
            if 0 <= val - d <= 5:
                out.append((["B%d" % (val - d)], val, t))
    elif kind == "global" and src.get("addr") in GLOBAL_ATOMS:
        atom = GLOBAL_ATOMS[src["addr"]]
        for val, t in pairs:
            x = val - d
            if atom == "sz" and not 1 <= x <= 5:
                continue
            if x < 0 or x > 0x7FFF:
                continue
            cur = hit["vals"][0] if hit.get("vals") else None
            a = "ad" if atom == "sz" and isinstance(cur, int) and x > cur else atom      # the party grown (copies of the last member) or cut
            out.append((["%s%d" % (a, x)], val, t))
    elif kind == "helper:event-at-place" and isinstance(src.get("args", [None])[0], int) and src["args"][1:2] == ["ds:907E"]:
        ev = src["args"][0]
        for val, t in pairs:
            out.append(((["ev%d" % ev] if val - d else ["er%d" % ev]), val, t))
    elif kind in FACTS:
        for val, t in pairs:
            p = fact_parts(kind, src.get("args", []), signed(val - d, v.get("w") or 2))
            if p:
                out.append((p, val, t))
    elif kind in ("helper:place-query", "helper:location-now-query"):
        args = src.get("args", [])
        here = kind == "helper:location-now-query" or args[:1] == ["ds:907E"]
        what = args[0] if kind == "helper:location-now-query" else (args[1] if len(args) > 1 else None)
        for val, t in pairs:
            x = signed(val - d, v.get("w") or 2)
            if not here:
                continue
            if what == 0 and -99 <= x <= 99:
                out.append((["rp%d" % x], val, t))
            elif what == 1 and 0 <= x <= 5:
                out.append((["rp%d" % BAND_REP[x]], val, t))
            elif what == 32 and -128 <= x <= 127:
                out.append((["lf%d" % (x & 0xFF)], val, t))
    elif kind == "helper:card-choice-menu":
        return menu_choice(v, hit, pairs, d)
    elif kind == "global" and src.get("addr") == "EE6E":
        return popup_box(v, hit, pairs)
    elif kind == "global" and src.get("addr") == "EE74":
        # reputation-now: the card copies place-query(location-now, 0) into it at its start (CardContext.ReputationHere)
        for val, t in pairs:
            x = signed(val - d, v.get("w") or 2)
            if -99 <= x <= 99:
                out.append((["rp%d" % x], val, t))
    elif kind == "global" and CHAINED.match(src.get("addr", "")):
        return menu_sweep(v, hit)
    else:
        return None, "kind-not-handled"
    return (out, None) if out else (None, "no-buildable-value")


# values a card sets earlier in its own run (the next card, card statics, the settings block ...): the menus before the compare are swept
CHAINED = re.compile(r"^(E48A|E7D8|EE53|A67C|A893|9084|8DEE|EE8C|906B|5[78][0-9A-F]{2})$")


def menu_sweep(v, hit, cap=12):
    """A value set earlier in the same run: each menu before the compare chooses each other offered line in turn (the P part: the earlier
    menus' choices kept, the later ones the policy's), in the run's own kind (plain / probe); at most cap runs."""
    mlog = hit.get("mlog") or []
    n = hit.get("menus") or 0
    if not mlog or n != len(mlog):
        return None, "no-menu-log"
    hand = parse_state(hit["run"].split(" ", 1)[1])[1]
    out = []
    for m in range(n, 0, -1):                 # the last menu first: nearest the compare
        if hand is not None and hand["menu"] <= m:
            continue
        block, enabled, choice, kind = mlog[m - 1]
        for line in enabled:
            if line == choice:
                continue
            out.append((["P" + "_".join(str(x) for x in _menu_prefix(mlog, m) + [enabled.index(line)])], None, v["want_taken"], {}))
    out = out[:cap]
    return (out, None) if out else (None, "no-other-line")


# ---- place facts and queries, menus (the second pass) -----------------------------------------------------------------------
QUEST, FACT, RESOLVED = 0x08, 0x1C, 0x24
BAND_REP = {0: 60, 1: 30, 2: 0, 3: -20, 4: -50, 5: -80}      # a standing in each band of place-query 1 (LocationRules.ReputationBand)
# helper -> (event kind or None: the first argument, argument names first-first); the args name the matching fields of the event
FACTS = {
    "helper:place-flag-value": (FACT, ("tag", "type", "loc"), "value"),
    "helper:place-flag-value-any": (FACT, ("tag", "type"), "value"),
    "helper:place-flag": (FACT, ("tag", "type", "loc"), "bool"),
    "helper:place-flag-any": (FACT, ("tag", "type"), "bool"),
    "helper:resolved-quest-at-owner": (RESOLVED, ("tag", "owner", "loc"), "bool"),
    "helper:resolved-quest-at": (RESOLVED, ("tag", "loc"), "bool"),
    "helper:resolved-quest-exists": (RESOLVED, ("tag",), "bool"),
    "helper:open-quest-owner-value": (QUEST, ("tag", "owner", "value"), "bool"),
    "helper:open-quest-at-owner": (QUEST, ("tag", "owner", "loc"), "bool"),
    "helper:open-quest-at": (QUEST, ("tag", "loc"), "bool"),
    "helper:open-quest-exists": (QUEST, ("tag",), "bool"),
    "helper:event-kind-at": (None, ("kind", "type", "loc"), "bool"),
    "helper:event-kind": (None, ("kind", "type"), "bool"),
    "helper:event-begun-flag": (None, ("kind",), "bool"),
    "helper:event-datum-at": (None, ("kind", "loc"), "value"),
}


def signed(x, w=2):
    x &= (1 << (8 * w)) - 1
    return x - (1 << (8 * w)) if x >> (8 * w - 1) else x


def fact_parts(kind, args, x):
    """The ev / er atoms (CardOracleAtoms) that make the helper return x: the matching events removed, and (x != 0) one started with the
    fields the arguments name (a place argument of location-now: the event's default place, here). None: an argument not constant."""
    ek, names, how = FACTS[kind]
    f = {}
    for n, a in zip(names, args):
        if a == "ds:907E" and n in ("loc", "value"):
            f[n] = "here"
        elif isinstance(a, int):
            f[n] = signed(a)
        else:
            return None
    if len(f) < len(names):
        return None
    k = ek if ek is not None else f.get("kind")
    if not isinstance(k, int) or k < 0:
        return None
    rm = "er%d_%d_%d" % (k, f.get("type", -1), f.get("tag", -1))
    if x == 0:
        return [rm]
    if how == "value" and x == 9999:
        return None
    value = x if how == "value" else ("9999" if f.get("value") == "here" else f.get("value", 0))
    loc = f.get("loc", "here")
    owner = f.get("owner", -2)
    p = "ev%d_%d_%d_%s_%d" % (k, f.get("type", 0), f.get("tag", 0), value, owner)
    if loc != "here":
        p += "_%d" % loc
    return [rm, p]


def _menu_prefix(mlog, n):
    """The P part values that repeat the run's choices in its menus 1..n-1 (each choice's index among the enabled lines; a forced menu: 0)."""
    out = []
    for block, enabled, choice, kind in mlog[:n - 1]:
        out.append(enabled.index(choice) if choice in enabled else 0)
    return out


def menu_choice(v, hit, pairs, d):
    """card-choice-menu's return compared: the run's menu (the last before the compare) chooses the wanted line: by the menu path (P part:
    the earlier menus' choices kept) when the line is offered, else by a handler call of that line (hand M<n>L<line>)."""
    mlog = hit.get("mlog") or []
    n = hit.get("menus") or 0
    if not mlog or n != len(mlog):
        return None, "no-menu-log"
    block, enabled, choice, kind = mlog[-1]
    hand = parse_state(hit["run"].split(" ", 1)[1])[1]
    out = []
    # the lines 0..19 on each side of the compare (not values_for's boundary: an unsigned test would give 0xFFFF), the nearest two wanted
    vals = hit["vals"]
    other = vals[1] if len(vals) > 1 else None
    if not isinstance(other, int) or v["op"] != "cmp":
        return None, "no-threshold"
    sides = {}
    for line in range(20):
        f = flags("sub", (line + d) & 0xFFFF, other, 2)
        t = jtaken(v["jcc"], f) if f else None
        if t is not None:
            sides.setdefault(t, []).append(line)
    want = sorted(sides.get(v["want_taken"], []), key=lambda x: abs(x - other))[:2]
    for line in want:
        val, t = line + d, v["want_taken"]
        if hand is not None and hand["menu"] == n:
            out.append(([], val, t, {"hand": dict(hand, line=line, slot=None)}))
        elif line in enabled and (hand is None or hand["menu"] > n):
            out.append((["P" + "_".join(str(x) for x in _menu_prefix(mlog, n) + [enabled.index(line)])], val, t, {}))
        elif hand is None:
            out.append(([], val, t, {"hand": {"menu": n, "line": line, "hints": False, "slot": None}}))
    return (out, None) if out else (None, "no-buildable-value")


_MSG = {}


def _popup_kinds(card, key, block):
    import card_oracle as co
    if not _MSG:
        _MSG["m"] = co.MessageFiles(GAME / "MSGFILES")
        _MSG["names"] = co.card_names()
    name = co.texts_of(key) or _MSG["names"].get(card)
    return _MSG["m"].popup_kinds(name, block) if name else []


def one_of(pairs, v):
    return next((val for val, t in pairs if t == v["want_taken"]), None)


def _donors(block):
    """(card file, line) pairs: card message files whose block `block` has an item / saint box marker on a line (sorted by name)."""
    import card_oracle as co
    _popup_kinds(0, "L3-O0", 0)
    out = []
    for name in sorted(set(_MSG["names"].values())):
        kinds = _MSG["m"].popup_kinds(name, block)
        lines = [i for i, k in enumerate(kinds) if k is not None]
        if lines and name.startswith("$") and (name + ".msg")[:12].lower() in _MSG["m"].entries:
            out.append((name, lines[0]))
    return out


def popup_box(v, hit, pairs):
    """popup-menu-box (DS:EE6E) compared: 1 when the choice was made in an item / saint pop-up box. The run's last menu chooses a line whose
    marker opens a box: an offered one in a probing run by the menu path (the box opens when a member has the item / knows the saint),
    every such line by a handler call with the box's slot 0 (hand M<n>L<line>S0, whoever holds it); 0: the same line in a plain run."""
    mlog = hit.get("mlog") or []
    n = hit.get("menus") or 0
    if not mlog or n != len(mlog):
        return None, "no-menu-log"
    block, enabled, choice, kind = mlog[-1]
    kind_run, state = hit["run"].split(" ", 1)
    key, hand = parse_state(state)
    kinds = _popup_kinds(hit["card"], key, block)
    lines = [i for i, k in enumerate(kinds) if k is not None]
    out = []
    if not lines:
        # the card's own file has no box line in that block: a card that keeps the previous card's texts (key part T$..) shows another
        # file's block; the handler call of a box line of such a file (the card-run of a card that loads its own resets them: no box)
        if hand is not None and hand["menu"] != n:
            return None, "no-box-line"
        for donor, line in _donors(block)[:2]:
            out.append((["T" + donor], one_of(pairs, v), v["want_taken"], {"hand": {"menu": n, "line": line, "hints": False, "slot": 0}}))
        return (out, None) if out else (None, "no-box-line")
    want = {t: val for val, t in pairs}
    one = want.get(v["want_taken"])
    for line in lines:
        if hand is not None and hand["menu"] == n:
            out.append(([], one, v["want_taken"], {"hand": dict(hand, line=line, slot=0)}))
            continue
        if hand is not None and hand["menu"] < n:
            continue
        if line in enabled:
            p = ["P" + "_".join(str(x) for x in _menu_prefix(mlog, n) + [enabled.index(line)])]
            out.append((p, one, v["want_taken"], {"probe": True}))
            out.append((p, want.get(not v["want_taken"]), not v["want_taken"], {"probe": False}))
        if hand is None:
            out.append(([], one, v["want_taken"], {"hand": {"menu": n, "line": line, "hints": False, "slot": 0}}))
    return (out, None) if out else (None, "no-buildable-value")


def jobs(classified, watchf, out):
    res = json.load(open(classified))
    w = json.load(open(watchf))
    jl, why = [], collections.Counter()
    for item, v in res.items():
        if not v.get("frontier"):
            continue
        hs = w["hits"].get(item)
        if not hs:
            why["no-hit" if "at" in v else v.get("kind")] += 1
            continue
        muts, reason = None, "no-hit"
        for hit in hs[:3]:            # the first hit that gives a buildable mutation
            muts, reason = mutations(v, hit)
            if muts is not None:
                break
        if muts is not None and not any(m[2] == v["want_taken"] for m in muts):
            muts, reason = None, "no-wanted-value"      # only the side that already ran could be built
        if muts is None:
            why[reason] += 1
            continue
        kind, state = hit["run"].split(" ", 1)
        key, hand = parse_state(state)
        card = hit["card"]
        seen = set()
        for m in muts:
            parts, val, taken = m[:3]
            extra = m[3] if len(m) > 3 else {}
            mk = with_parts(key, parts)
            h = extra.get("hand", hand)
            probe = extra.get("probe", kind != "plain") or h is not None
            j = {"card": card, "key": mk, "option": option_of(key), "probe": probe, "item": item, "value": val, "taken": taken}
            if h:
                j["hand"] = h
            sig = (mk, probe, json.dumps(h, sort_keys=True))
            if sig in seen:
                continue
            seen.add(sig)
            jl.append(j)
        why["jobs"] += 1
    json.dump(jl, open(out, "w"), indent=0)
    print(len(jl), "jobs;", dict(why))


def run_jobs(jobsf, states, work, workers=4):
    """The jobs on the original with coverage (one cov line per run), the corpus for the C# replay (CardOracleTests)."""
    import gzip
    import os
    import time
    from multiprocessing import Pool
    jl = json.load(open(jobsf))
    os.makedirs(os.path.join(work, "cov"), exist_ok=True)
    by = collections.defaultdict(list)
    for j in jl:
        by[j["card"]].append(j)
    t0 = time.time()
    res = {}
    with Pool(workers, initializer=_init, initargs=(states, os.path.join(work, "cov")), maxtasksperchild=8) as p:
        for card, v in p.imap_unordered(_work, sorted(by.items(), key=lambda x: -len(x[1]))):
            res[card] = v
            print("card %X %d runs %.1fs" % (card, len(by[card]), v["seconds"]), flush=True)
    import card_oracle as co
    clock = worker_oracle(states, False).clock()
    corpus = {"authority": "test_only_original_byte_execution", "source": "tools/oracle/card_oracle.py (mutate_branch.py)", "clock": "%04X" % clock,
              "sound": True, "cards": {"%03X" % k: res[k] for k in sorted(res)}}
    with gzip.open(os.path.join(work, "corpus.json.gz"), "wt", encoding="utf-8") as f:
        json.dump(corpus, f, separators=(",", ":"), sort_keys=True)
    wall = time.time() - t0
    json.dump({"wall": wall, "workers": workers, "runs": len(jl)}, open(os.path.join(work, "time.json"), "w"))
    print("wall", round(wall), "s")


_W = {}


def _init(states, cov):
    _W["o"] = worker_oracle(states, True)
    _W["states"], _W["cov"] = states, cov


def _work(item):
    import os
    import time
    import card_oracle as co
    card, jl = item
    o = _W["o"]
    runs = {}
    secs = 0.0
    with open(os.path.join(_W["cov"], "cov-%d.jsonl" % os.getpid()), "a") as f:
        for j in jl:
            hand = j.get("hand")
            name = ("hand %s %s" % (j["key"], hand_spec(hand))) if hand else ("%s %s" % ("probe" if j["probe"] else "plain", j["key"]))
            if name in runs:
                continue
            t = time.process_time()
            try:
                r = o.run_card(card, j["key"], j["option"], j["probe"], hand)
            except Exception as e:      # noqa: BLE001
                _W["o"] = o = worker_oracle(_W["states"], True)
                r = {"trace": ["error %s" % str(e)[:200]], "end": "error", "random": "0", "draws": 0}
            runs[name] = r
            c = o.m.coverage_result()
            o.m.cov_insn_rec, o.m.cov_edge_rec = {}, {}
            dt = round(time.process_time() - t, 2)
            secs += dt
            f.write(json.dumps({"unit": "card %X" % card, "state": name.split(" ", 1)[1], "tag": j["item"], "seconds": dt, **c},
                               separators=(",", ":")) + "\n")
    return card, {"function": o.cards[card][3], "record": "R%03d" % o.cards[card][0], "seconds": round(secs, 1), **co.share_texts(runs)}


def table(classified, jobsf, work):
    """Per frontier outcome: closed (an agreeing mutated run reached it), attempted and not closed, differing, not attempted (why)."""
    import os
    res = json.load(open(classified))
    jl = json.load(open(jobsf))
    gained = set(json.load(open(os.path.join(work, "gained.json"))))
    bad = collections.defaultdict(set)          # item -> runs that differ / differ for a listed reason
    names = {}
    for j in jl:
        h = j.get("hand")
        names[(j["card"], ("hand %s %s" % (j["key"], hand_spec(h))) if h else ("%s %s" % ("probe" if j["probe"] else "plain", j["key"])))] = j["item"]
    for f in ("diffs.tsv", "justified.tsv"):
        p = os.path.join(work, f)
        if os.path.exists(p):
            for line in open(p, encoding="utf-8"):
                c = line.rstrip("\n").split("\t")
                if len(c) >= 2 and (int(c[0], 16), c[1]) in names:
                    bad[names[(int(c[0], 16), c[1])]].add(f.split(".")[0] + ": " + (c[2] if len(c) > 2 else ""))
    tried = collections.defaultdict(list)
    for j in jl:
        tried[j["item"]].append(j)
    rows = collections.Counter()
    by_kind = collections.defaultdict(collections.Counter)
    detail = {}
    for item, v in res.items():
        if not v.get("frontier"):
            continue
        kind = v["kind"]
        if item in gained:
            st = "closed"
        elif item in tried and bad.get(item):
            st = "differs"
        elif item in tried:
            st = "tried, not reached"
        else:
            st = "not attempted"
        rows[st] += 1
        by_kind[kind][st] += 1
        detail[item] = {"kind": kind, "status": st, "jobs": len(tried.get(item, [])), "bad": sorted(bad.get(item, ()))}
    side = len(gained - {k for k, v in res.items() if v.get("frontier")})
    json.dump(detail, open(os.path.join(work, "table.json"), "w"), indent=0)
    print("frontier outcomes by status:", dict(rows))
    print("other open items reached (behind the gates):", side, " total gained:", len(gained))
    for k, c in sorted(by_kind.items(), key=lambda kv: -sum(kv[1].values())):
        print("  %-34s %s" % (k, dict(c)))


def main(argv):
    cmd = argv[0]
    if cmd == "table":
        table(argv[1], argv[2], argv[3])
    elif cmd == "watch":
        watch(argv[1], argv[2], argv[3], argv[4])
    elif cmd == "jobs":
        jobs(argv[1], argv[2], argv[3])
    elif cmd == "run":
        run_jobs(argv[1], argv[2], argv[3], int(argv[argv.index("--workers") + 1]) if "--workers" in argv else 4)
    elif cmd == "keys":
        # the state keys the jobs or the reaching runs need (for CardAimSearch.ExportKeys)
        d = json.load(open(argv[1]))
        ks = set()
        if isinstance(d, list):
            ks = {state_key(j["key"]) for j in d}
        else:
            per = int(argv[argv.index("--per-item") + 1]) if "--per-item" in argv else 2
            for item, rs in d.items():
                n = 0
                for r in rs:
                    ps = parse_state(r["state"])
                    if ps is None:
                        continue
                    ks.add(state_key(ps[0]))
                    n += 1
                    if n >= per:
                        break
        open(argv[2], "w").write("\n".join(sorted(ks)) + "\n")
        print(len(ks), "keys")
    elif cmd == "reach":
        roots = [a for a in argv[2:] if not a.startswith("--")]
        roots = roots[:roots.index(argv[argv.index("--out") + 1])] if "--out" in argv else roots
        reach(argv[1], roots, argv[argv.index("--out") + 1])
    elif cmd == "classify":
        lo, hi = 44, 135
        if "--records" in argv:
            lo, hi = (int(x) for x in argv[argv.index("--records") + 1].split("-"))
        classify(argv[1], argv[2], lo, hi)
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
