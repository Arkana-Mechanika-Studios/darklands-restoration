"""Directed mutation of the open branch outcomes of the battles (opt-in; writes nothing into the committed corpora).

The battle call jobs (battle_calls.py, battle_fuzz.py: one original function called from a state captured inside a whole fight) are the
runs. For each open outcome at the coverage frontier of the battle records (R002, R003, R014..R025: its conditional jump ran, only the
other way) the compare that decides it is read statically (mutate_branch.py classify), the recorded jobs whose coverage executed the jump
are found (reach), rerun on the original with a read-only hook at the compare's flag setter (watch: the draws so far, the operands'
values and, for a memory operand, its address), and new jobs are made from them (gen):
  - poke: the decisive operand is a byte or word of the memory a job carries (DS, the level image 20A5, the doors' segment, the packs,
    the wave record) whose value at the compare is the job's start value: the value that sends the jump the wanted way is written there
    (one more op of the job), the boundary value of mutate_branch.values_for;
  - force: the compare ran after the call's k-th draw: the last draws before it are forced to a spread of raw values (battle_calls.py
    State.call(force=...), the job's "force" map, identical on the rewrite's side: BattleCallsTests, ForcedDraw.StateFor).
run calls them on the original (with coverage) and keeps the runs that reach an open item, in battle_fuzz.py's layout
(<work>/fuzz/<function>/<label>.json and cov/), which battle_calls_run.py --skip-fuzz exports and replays on the rewrite: only the
agreeing runs count (cov-agree).

usage: python -I -B mutate_battle.py reach <classified.json>[,<classified.json>] <cov root> [<cov root> ...] --out reach.json
       python -I -B mutate_battle.py watch <classified> <reach.json> <states folder> <watch.json> --jobs <jobs.json.gz> [...]
       python -I -B mutate_battle.py gen <classified> <watch.json> <cands.json>
       python -I -B mutate_battle.py run <cands.json> <states folder> <work> [--workers N] --open <branches-missed.csv>
       python -I -B mutate_battle.py table <classified> <cands.json> <work> [--gained gained.json]
The oracle steps (watch, run) and the replay hold the heavy lock of the helpers' rules.
"""
from __future__ import annotations

import collections
import csv
import gzip
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
import mutate_branch as mb  # noqa: E402

BAT = {"R002", "R003"} | {"R%03d" % i for i in range(14, 26)}
# raw values a forced draw takes: both ends, the small values a modulo roll compares, the quarters a scaled roll does
RAWS = [0, 1, 2, 3, 5, 8, 16, 100, 1000, 4096, 8192, 16383, 16384, 24576, 32000, 32767]


def load_classified(spec):
    res = {}
    for f in spec.split(","):
        res.update(json.load(open(f)))
    return res


def frontier(res):
    return {k: v for k, v in res.items() if v.get("frontier") and v.get("kind") != "not-a-branch" and "at" in v}


# ---- reach -------------------------------------------------------------------------------------------------------------------
def reach(classified, roots, out, keep=6):
    """The battle call runs (unit = the function called, state = label#iteration) whose coverage holds the jump of each frontier item
    (cov-*.jsonl streamed line by line); per item up to `keep`, of different (function, state) pairs first."""
    import glob
    import cfg as cfgmod
    from run_unit import Rtlink
    res = frontier(load_classified(classified))
    exe = (mb.GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    want = collections.defaultdict(dict)
    for key in res:
        name, payload, _ = key.split("|")
        want[funcs[name].record].setdefault(int(payload, 16), []).append(key)
    found = collections.defaultdict(list)
    pairs = collections.defaultdict(set)
    unit_re = re.compile(r'{"unit": ?"([^"]+)", ?"state": ?"([^"]*)"')
    files = []
    for root in roots:
        files += sorted(glob.glob(os.path.join(root, "**", "cov-*.jsonl"), recursive=True))
    t0 = time.time()
    for fi, f in enumerate(files):
        with open(f, encoding="utf-8") as fh:
            for line in fh:
                m = unit_re.match(line)
                if not m:
                    continue
                recs = [r for r in want if '"%s":' % r in line]
                if not recs:
                    continue
                j = json.loads(line)
                for r in recs:
                    have = j["insns"].get(r)
                    if not have:
                        continue
                    for p in set(have).intersection(want[r]):
                        for item in want[r][p]:
                            pair = (m.group(1), m.group(2).split("#")[0])
                            lst = found[item]
                            if len(lst) >= keep or (pair in pairs[item] and len(lst) >= 2):
                                continue
                            pairs[item].add(pair)
                            lst.append({"unit": m.group(1), "state": m.group(2), "folder": os.path.dirname(f)})
        if fi % 50 == 0:
            print("%d/%d files, %d items reached, %ds" % (fi, len(files), len(found), time.time() - t0), flush=True)
    json.dump(found, open(out, "w"), indent=0)
    print("reached", len(found), "of", len(res))


# ---- jobs of the recorded runs ------------------------------------------------------------------------------------------------
def load_jobs(files, ids):
    """{job id: job} for the wanted ids, from battle_calls.py export files (each loaded once, kept only for the wanted jobs)."""
    out = {}
    for f in files:
        with gzip.open(f, "rt", encoding="utf-8") as fh:
            d = json.load(fh)
        for j in d["jobs"]:
            if j["id"] in ids and j["id"] not in out:
                out[j["id"]] = j
        del d
    return out


def writes(st, ops):
    """battle_fuzz.Fuzzer.writes: the ops of a job as State.call's mutations."""
    import battle_calls as bc
    from dosmachine import DS, P
    out = []
    for op in ops:
        if op["w"] in ("loot", "wave"):
            out.append((op["w"], op["o"], bytes.fromhex(op["hex"])))
            continue
        seg = DS if op["w"] == "ds" else st.far2_seg if op["w"] == "far2" else P(bc.PACK_SEG) if op["w"] == "far3" else P(bc.FAR_SEG)
        out.append((seg, op["o"], bytes.fromhex(op["hex"])))
    return out


def region_of(st, seg, off):
    """The job memory a (segment, offset) lies in: ('ds' | 'floor' | 'far2' | 'far3' | 'wave', offset) or None (the stack, elsewhere)."""
    import battle_calls as bc
    from dosmachine import DS, P
    if seg == DS:
        return ("ds", off) if off < ((st.sp0 - 0x1200) & 0xFFFF) else None
    if seg == P(bc.FAR_SEG):
        return ("floor", off)
    if seg == st.far2_seg and 0x0B00 <= off < 0x0D00:
        return ("far2", off)
    if seg == P(bc.PACK_SEG) and off < bc.PACK_SIZE:
        return ("far3", off)
    if seg == st.wave_seg and bc.WAVE_AT <= off < bc.WAVE_AT + bc.WAVE_SIZE:
        return ("wave", off - bc.WAVE_AT)
    return None


def start_bytes(st, region, off, n, ops):
    """The bytes a job starts from at a region's offset (the state's, with the job's ops applied)."""
    import battle_calls as bc
    base = {"ds": st.base, "floor": st.far_base, "far2": st.far2_base, "far3": st.far3_base, "wave": st.wave_base}[region]
    buf = bytearray(base[off:off + n])
    for op in ops:
        if op["w"] != region:
            continue
        data = bytes.fromhex(op["hex"])
        for i, b in enumerate(data):
            if off <= op["o"] + i < off + n:
                buf[op["o"] + i - off] = b
    _ = bc
    return bytes(buf)


def near_of(coords):
    return sorted(set(coords))


def register_loads(F, fn, item, v):
    """{operand index: (frame, ip)} of the `mov reg, [mem]` that last loaded each register operand of the item's flag setter in its chain
    (inc / dec / add / sub of the register in between are allowed: the watch notes the difference)."""
    import capstone.x86_const as cx
    from branch_flip import REG
    lo = fn.payload(fn.linear)
    insns = F.chain(fn.linear - lo + int(item.split("|")[1], 16))
    at_ip = fn.entry_ip + int(v["at"], 16) - lo
    s = next((n for n, i in enumerate(insns) if i.address == at_ip), None)
    out = {}
    if s is None:
        return out
    for idx, op in enumerate(insns[s].operands):
        if op.type != cx.X86_OP_REG or op.reg not in REG:
            continue
        name, width = REG[op.reg]
        for j in range(s - 1, -1, -1):
            i = insns[j]
            if i.mnemonic in ("call", "lcall"):
                break
            ops = i.operands
            if not ops or not (ops[0].type == cx.X86_OP_REG and ops[0].reg in REG and REG[ops[0].reg][0] == name):
                continue
            if i.mnemonic == "mov" and len(ops) > 1 and ops[1].type == cx.X86_OP_MEM and REG[ops[0].reg] == (name, width):
                out[idx] = (fn.frame, i.address)
            elif i.mnemonic in ("inc", "dec") or (i.mnemonic in ("add", "sub") and ops[1].type == cx.X86_OP_IMM):
                continue
            break
    return out


def add_hooks(st, want, make):
    """The watch hooks on a state's machine: the stored state was taken inside a run (depth > 0), and the machine adds hooks only
    between runs, so the depth is set aside while they are added (every call starts from the stored state again)."""
    from dosmachine import P
    depth, st.m.depth = st.m.depth, 0
    try:
        for item, (seg, ip, rec) in want.items():
            st.m.hook(P(seg), ip, make(item), record=rec)
    finally:
        st.m.depth = depth


def mem_operands(m, ins):
    """(segment value, offset, size) of the memory operands of an instruction (None for the others)."""
    import capstone.x86_const as cx
    out = []
    for op in ins.operands:
        if op.type != cx.X86_OP_MEM:
            out.append(None)
            continue
        ea = op.mem.disp
        for r in (op.mem.base, op.mem.index):
            if r:
                ea += m.reg(ins.reg_name(r).upper())
        seg = {cx.X86_REG_ES: "ES", cx.X86_REG_SS: "SS", cx.X86_REG_CS: "CS", cx.X86_REG_DS: "DS"}.get(op.mem.segment)
        if seg is None:
            seg = "SS" if op.mem.base == cx.X86_REG_BP else "DS"
        out.append((m.reg(seg), ea & 0xFFFF, op.size))
    return out


def watch(classified, reachf, states, out, jobfiles, per_item=2):
    """Reruns the reaching jobs on the original with a hook at each item's flag setter: per item the first hits (job, draws so far,
    operand values, memory operands as job regions with their start bytes)."""
    import capstone
    import battle_calls as bc
    import cfg as cfgmod
    from dosmachine import P
    from run_unit import Rtlink
    res = frontier(load_classified(classified))
    rch = json.load(open(reachf))
    exe = (mb.GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    want = {}
    by_job = collections.defaultdict(set)
    for item, runs in rch.items():
        if item not in res:
            continue
        for r in runs[:per_item + 2]:
            by_job[r["unit"] + "|" + r["state"]].add(item)
    jobs = load_jobs(jobfiles, set(by_job))
    print("jobs found", len(jobs), "of", len(by_job), flush=True)
    loads = {}            # (item, operand index) -> (frame, ip, record) of the mov that loaded the register operand from memory
    Fs = {}
    for item, v in res.items():
        fn = funcs[item.split("|")[0]]
        want[item] = (fn.frame, fn.entry_ip + (int(v["at"], 16) - fn.payload(fn.linear)), fn.record)
        if item not in rch:
            continue
        try:
            F = Fs.get(fn.name) or Fs.setdefault(fn.name, mb.Fn(exe, rt, fn))
            for idx, (frame, ip) in register_loads(F, fn, item, v).items():
                loads[(item, idx)] = (frame, ip, fn.record)
        except Exception as e:       # noqa: BLE001 - a function the cfg cannot follow: no load traced
            print("no loads", item, str(e)[:80], flush=True)
    print("register operands traced to a load:", len(loads), flush=True)
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    md.detail = True
    hits = collections.defaultdict(list)
    last_load = {}
    per_job_items = collections.Counter()
    by_state = collections.defaultdict(list)
    for jid in sorted(jobs):
        by_state[jobs[jid]["state"]].append(jid)
    cur = {"items": set(), "job": None, "st": None, "ops": []}

    def make(item):
        def h(m):
            if item not in cur["items"] or len(hits[item]) >= 2 * per_item or sum(1 for x in hits[item] if x["job"] == cur["job"]) >= 2:
                return
            st = cur["st"]
            b = bytes(m.uc.mem_read((m.reg("CS") << 4) + m.reg("IP"), 16))
            ins = next(md.disasm(b, m.reg("IP")))
            vals = [mb.operand_value(m, ins, op) for op in ins.operands]
            mems = []
            for mo in mem_operands(m, ins):
                if mo is None:
                    mems.append(None)
                    continue
                reg = region_of(st, mo[0], mo[1])
                mems.append(None if reg is None else {"w": reg[0], "o": reg[1], "n": mo[2],
                                                      "start": start_bytes(st, reg[0], reg[1], mo[2], cur["ops"]).hex()})
            for idx in range(len(mems)):
                ld = last_load.get((item, idx))
                if mems[idx] is None and ld is not None and ld["job"] == cur["job"] and isinstance(vals[idx], int):
                    mems[idx] = {**ld["mem"], "delta": vals[idx] - ld["value"]}       # the register as loaded, adjusted since by delta
            hits[item].append({"job": cur["job"], "draws": len(st.draws), "vals": vals, "mems": mems, "insn": "%s %s" % (ins.mnemonic, ins.op_str)})
        return h

    def make_load(key):
        def h(m):
            if key[0] not in cur["items"]:
                return
            st = cur["st"]
            b = bytes(m.uc.mem_read((m.reg("CS") << 4) + m.reg("IP"), 16))
            ins = next(md.disasm(b, m.reg("IP")))
            mo = mem_operands(m, ins)[1]
            if mo is None:
                return
            reg = region_of(st, mo[0], mo[1])
            if reg is None:
                last_load.pop(key, None)
                return
            value = int.from_bytes(bytes(m.uc.mem_read(mo[0] * 16 + mo[1], mo[2])), "little")
            last_load[key] = {"job": cur["job"], "value": value,
                              "mem": {"w": reg[0], "o": reg[1], "n": mo[2], "start": start_bytes(st, reg[0], reg[1], mo[2], cur["ops"]).hex()}}
        return h

    def hook_all(st):
        add_hooks(st, want, make)
        add_hooks(st, loads, make_load)

    t0 = time.time()
    totals = {}
    for name, jids in sorted(by_state.items()):
        path = Path(states) / (name + ".state")
        if not path.exists():
            print("no state", name, flush=True)
            continue
        st = bc.State(path, near=near_of(jobs[j]["function"] for j in jids))
        hook_all(st)
        cur["st"] = st
        for jid in jids:
            job = jobs[jid]
            cur["items"], cur["job"], cur["ops"] = by_job[jid], jid, job["ops"]
            try:
                r = st.call(job["function"], job["args"], writes(st, job["ops"]))
            except Exception as e:      # noqa: BLE001 - a machine fault is data here
                print("watch failed", jid, str(e)[:120], flush=True)
                st = bc.State(path, near=near_of(jobs[j]["function"] for j in jids))
                hook_all(st)
                cur["st"] = st
                continue
            same = r["draws"] == job["orig"].get("draws") and r["random"] == job["orig"].get("random")
            totals[jid] = {"draws": len(r["draws"]), "same": same, "error": r["error"], "seconds": round(r["seconds"], 3)}
            per_job_items[jid] = len(by_job[jid])
        print("state", name, len(jids), "jobs, %ds" % (time.time() - t0), flush=True)
    json.dump({"hits": hits, "jobs": jobs, "totals": totals}, open(out, "w"))
    print("watched", len(totals), "jobs,", len(hits), "items hit,", round(time.time() - t0), "s")


# ---- candidates ---------------------------------------------------------------------------------------------------------------
def gen(classified, watchf, out, ks=2, skip=None):
    """skip: an earlier candidates file whose keys are not made again (the next iteration)."""
    res = frontier(load_classified(classified))
    w = json.load(open(watchf))
    done = set(json.load(open(skip))["cands"]) if skip else set()
    cands, why = {}, collections.Counter()
    for item, v in res.items():
        hs = w["hits"].get(item)
        if not hs:
            why["no-hit"] += 1
            continue
        made = 0
        a_dec = mb.leaf(v.get("a", {})).get("src") != "imm"
        idx = 0 if a_dec else 1
        seen_jobs = set()
        for hit in hs:
            if hit["job"] in seen_jobs:
                continue
            seen_jobs.add(hit["job"])
            job = w["jobs"][hit["job"]]
            # poke: the decisive operand in the job's memory, unchanged since the start
            pairs = mb.values_for(v, hit)
            want_vals = [val for val, t in pairs if t == v["want_taken"] and val is not None]
            mem = hit["mems"][idx] if idx < len(hit["mems"]) else None
            src = mb.leaf(v.get("a" if a_dec else "b", {}))
            target = None
            if mem is not None and isinstance(hit["vals"][idx], int) and                     int.from_bytes(bytes.fromhex(mem["start"]), "little") == (hit["vals"][idx] - mem.get("delta", 0)) & ((1 << (8 * mem["n"])) - 1):
                target = (mem["w"], mem["o"], mem["n"], mem.get("delta", 0))
            elif src.get("src") == "global" and src.get("seg") in (None, "ds", 0) and isinstance(hit["vals"][idx], int):
                target = ("ds", int(src["addr"], 16), max(1, src.get("w") or 2), src.get("d", 0) or 0)
            if target and want_vals:
                reg, off, n, d = target
                mask = (1 << (8 * n)) - 1
                xs = [(want_vals[0] - d) & mask]
                if xs[0] == mask and len(pairs) > 1:
                    xs.append(1)            # a flag or count poked with -1: 1 too (the game's bytes hold 0 / 1 / small counts)
                for x in xs:
                    op = {"w": reg, "o": off, "hex": x.to_bytes(n, "little").hex()}
                    key = "%s|P%s:%X=%s" % (hit["job"], reg, off, op["hex"])
                    cands.setdefault(key, {"job": hit["job"], "ops": [op], "force": {}, "items": []})["items"].append(item)
                    made += 1
            # force: the draws just before the compare
            c = hit["draws"]
            for k in range(max(0, c - ks), c):
                for raw in RAWS:
                    key = "%s|R%d_%d" % (hit["job"], k, raw)
                    cands.setdefault(key, {"job": hit["job"], "ops": [], "force": {str(k): raw}, "items": []})["items"].append(item)
                    made += 1
        why["made" if made else ("no-draw-no-poke" if hs else "no-hit")] += 1
    cands = {k: c for k, c in cands.items() if k not in done}
    json.dump({"cands": cands, "jobs": {j: w["jobs"][j] for j in {c["job"] for c in cands.values()}}}, open(out, "w"))
    print("candidates", len(cands), dict(why))


# ---- run ----------------------------------------------------------------------------------------------------------------------
class OpenSet:
    """The open items of the battle records as executed payloads and branch edges (as battle_calls_measure.Items.reached, the cfg built once)."""

    def __init__(self, missed, records=BAT):
        import cfg as cfgmod
        from run_unit import Rtlink
        exe = (mb.GAME / "DARKLAND.EXE").read_bytes()
        rt = Rtlink(exe)
        funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
        opens = collections.defaultdict(set)
        for r in csv.DictReader(open(missed, encoding="utf-8")):
            if r["status"] == "open" and r["record"] in records:
                opens[r["function"]].add((r["payload"], r["kind"]))
        self.blocks = collections.defaultdict(dict)
        self.edges = collections.defaultdict(dict)
        for name, items in opens.items():
            fn = funcs.get(name)
            if fn is None:
                continue
            lo = fn.payload(fn.linear)
            base = fn.linear - lo
            g = cfgmod.build(exe, rt, fn)
            wanted = {}
            for b in g.blocks:
                wanted[("%04X" % (b - base), "block")] = b - base
            for j, t, f, _ in g.branches:
                for kind, dst in (("taken", t), ("fallthrough", f)):
                    if t == f and kind == "fallthrough":
                        continue
                    wanted[("%04X" % (j - base), kind)] = (j - base, dst - base)
            for it in items:
                x = wanted.get(it)
                if x is None:
                    continue
                key = "%s|%s|%s" % (name, it[0], it[1])
                if it[1] == "block":
                    self.blocks[fn.record][x] = key
                else:
                    self.edges[fn.record][x] = key

    def reached(self, cov):
        out = set()
        for rec, d in self.blocks.items():
            for p in cov["insns"].get(rec, ()):
                if p in d:
                    out.add(d[p])
        for rec, d in self.edges.items():
            for e in cov["edges"].get(rec, ()):
                k = d.get(tuple(e))
                if k:
                    out.add(k)
        return out


def run_worker(candsf, states, work, names, missed):
    import battle_calls as bc
    d = json.load(open(candsf))
    cands, jobs = d["cands"], d["jobs"]
    opens = OpenSet(missed)
    by_state = collections.defaultdict(list)
    for key in sorted(cands):
        by_state[jobs[cands[key]["job"]]["state"]].append(key)
    work = Path(work)
    for name in names:
        keys = by_state.get(name, [])
        if not keys:
            continue
        path = Path(states) / (name + ".state")
        coords = near_of(jobs[cands[k]["job"]]["function"] for k in keys)
        st = bc.State(path, near=coords)
        kept = collections.defaultdict(list)
        stats = collections.Counter()
        t0 = time.time()
        for n, key in enumerate(keys):
            c = cands[key]
            job = jobs[c["job"]]
            ops = job["ops"] + c["ops"]
            try:
                r = st.call(job["function"], job["args"], writes(st, ops), force=c["force"])
            except Exception as e:      # noqa: BLE001
                stats["fault"] += 1
                print("fault", key, str(e)[:100], flush=True)
                st = bc.State(path, near=coords)
                continue
            if r["error"]:
                stats["error"] += 1
                continue
            got = opens.reached(r["coverage"])
            hit = set(c["items"]) & got
            stats["hit" if hit else ("other" if got else "none")] += 1
            if not got:
                continue
            item = {"args": job["args"], "ops": ops, "force": c["force"], "ax": r["ax"], "dx": r["dx"],
                    "changed": {str(k): v for k, v in r["changed"].items()}, "far_changed": {str(k): v for k, v in r["far_changed"].items()},
                    "far2_changed": {str(k): v for k, v in r["far2_changed"].items()}, "far3_changed": {str(k): v for k, v in r["far3_changed"].items()},
                    "wave_changed": {str(k): v for k, v in r["wave_changed"].items()}, "loot": r["loot"], "random": r["random"], "draws": r["draws"],
                    "seconds": r["seconds"], "instructions": r["instructions"], "coverage": r["coverage"], "iteration": n, "novel": True,
                    "cand": key, "targets": sorted(c["items"]), "reached": sorted(got)}
            kept[job["function"]].append(item)
        for coord, items in kept.items():
            folder = work / "fuzz" / coord.replace("+", "_")
            (folder / "cov").mkdir(parents=True, exist_ok=True)
            label = name + "-mb"
            (folder / (label + ".json")).write_text(json.dumps({"state": name, "label": label, "function": coord, "kept": items}), encoding="utf-8")
            with open(folder / "cov" / ("cov-%s.jsonl" % label), "w", encoding="utf-8") as f:
                for item in items:
                    f.write(json.dumps({"unit": coord, "state": "%s#%d" % (label, item["iteration"]), "seconds": item["seconds"], **item["coverage"]}) + "\n")
        print("state", name, len(keys), "candidates", dict(stats), "%.0f s" % (time.time() - t0), flush=True)


def run(candsf, states, work, workers, missed):
    d = json.load(open(candsf))
    count = collections.Counter(d["jobs"][c["job"]]["state"] for c in d["cands"].values())
    names = [n for n, _ in count.most_common()]
    lanes = [[] for _ in range(workers)]
    load = [0] * workers
    for n in names:
        i = load.index(min(load))
        lanes[i].append(n)
        load[i] += count[n]
    Path(work).mkdir(parents=True, exist_ok=True)
    procs = []
    for i, lane in enumerate(lanes):
        if not lane:
            continue
        cmd = [sys.executable, "-I", "-B", str(Path(__file__)), "worker", candsf, states, work, ",".join(lane), missed]
        procs.append(subprocess.Popen(cmd, stdout=open(Path(work) / ("run-%d.log" % i), "w"), stderr=subprocess.STDOUT))
    for p in procs:
        p.wait()
    print("run done", [p.returncode for p in procs])


# ---- battlefield generators ---------------------------------------------------------------------------------------------------
# the corpus builders with gen_force.py (opt-in scenario file, forced draws, watches), the records they run, the unit of their run line
GEN = {"castle": ("build_castle_corpus.py", {"R004", "R005", "R006", "R007"}, "layout-battlefield-entry", 100, "CASTLE_CORPUS"),
       "mine": ("build_mine_corpus.py", {"R004", "R009"}, "mine-builder-entry", 400, "MINE_CORPUS"),
       "wild": ("build_wild_corpus.py", {"R004", "R008"}, "wild_main", 96, "WILD_CORPUS")}
CORPUS_FUNCTION = {"castle": "R005+0x0000", "mine": "R009+0x0000"}      # the committed corpora name these cases by coordinate
GEN_RAWS = [0, 1, 3, 16, 8192, 16384, 24576, 32767]
WIDE_RAWS = [0, 16384, 32767]


def gen_scenarios(name, seed, count=None):
    """The builder's own random plan for a sweep seed (seed 0: the committed corpus's scenarios)."""
    import importlib
    os.environ["DL_SWEEP_SEED"] = str(seed)
    mod = importlib.import_module(GEN[name][0][:-3])
    out = mod.make_scenarios(count or GEN[name][3])
    for i, sc in enumerate(out):
        sc.setdefault("index", i)
        sc["origin"] = "s%d#%d" % (seed, sc["index"])
    os.environ.pop("DL_SWEEP_SEED")
    return out


def gen_items(classified, records):
    """Frontier items of the records with the linear address of their flag setter (as run_unit loads the sections: link-time)."""
    import cfg as cfgmod
    from run_unit import Rtlink
    res = frontier(load_classified(classified))
    rt = Rtlink((mb.GAME / "DARKLAND.EXE").read_bytes())
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    out = {}
    for item, v in res.items():
        fn = funcs[item.split("|")[0]]
        if fn.record not in records:
            continue
        lo = fn.payload(fn.linear)
        out[item] = {"at": fn.linear + int(v["at"], 16) - lo, "lo": fn.linear, "hi": fn.linear + fn.size}
    return out


def run_builder(name, scenarios, work, tag, workers, watch=None, coverage=False):
    """The builder on a scenario list, split over `workers` processes of one pool worker each (so each process's run lines follow its
    scenarios' order): -> [(case, coverage line or None)] in the list's order."""
    script, _, unit, _, _ = GEN[name]
    work = Path(work)
    work.mkdir(parents=True, exist_ok=True)
    chunks = [scenarios[i::workers] for i in range(workers)]
    procs = []
    for i, chunk in enumerate(chunks):
        if not chunk:
            continue
        sf = work / ("%s-%d.scen.json" % (tag, i))
        sf.write_text(json.dumps(chunk), encoding="utf-8")
        cov = work / ("%s-%d.cov" % (tag, i))
        if cov.exists():
            for f in cov.glob("*.jsonl"):
                f.unlink()
        cov.mkdir(exist_ok=True)
        env = dict(os.environ, DL_SCENARIOS=str(sf))
        env.pop("DL_COVERAGE_DIR", None)
        if watch:
            env["DL_WATCH"] = ",".join("0x%X" % a for a in watch)
        if coverage:
            env["DL_COVERAGE_DIR"] = str(cov)
        argv = [sys.executable, "-I", "-B", str(HERE / script), str(mb.GAME / "DARKLAND.EXE"), str(mb.GAME), str(work / ("%s-%d.json" % (tag, i)))]
        if name != "special":
            argv += [str(len(chunk)), "1"]
        procs.append((i, chunk, subprocess.Popen(argv, env=env, stdout=open(work / ("%s-%d.log" % (tag, i)), "w"), stderr=subprocess.STDOUT)))
    out = [None] * len(scenarios)
    for i, chunk, p in procs:
        p.wait()
        cases = json.loads((work / ("%s-%d.json" % (tag, i))).read_text())["cases"] if (work / ("%s-%d.json" % (tag, i))).exists() else []
        by_index = {c["inputs"]["index"]: c for c in cases}
        lines = []
        if coverage:
            for f in sorted((work / ("%s-%d.cov" % (tag, i))).glob("cov-*.jsonl")):
                lines += [ln for ln in f.read_text(encoding="utf-8").splitlines() if ('"unit":"%s"' % unit) in ln]
        for n, sc in enumerate(chunk):
            k = i + n * workers
            out[k] = (by_index.get(sc["index"]), lines[n] if n < len(lines) else None)
        if coverage and len(lines) != len(chunk):
            print("warning: chunk %d has %d run lines for %d scenarios" % (i, len(lines), len(chunk)), flush=True)
    return out


def gen_watch(name, classified, work, seeds=(0,), workers=4):
    items = gen_items(classified, GEN[name][1])
    scen = [sc for s in seeds for sc in gen_scenarios(name, s)]
    for k, sc in enumerate(scen):
        sc["index"] = k              # unique over the seeds; "origin" keeps the plan's own
    addr = sorted({v["at"] for v in items.values()})
    t0 = time.time()
    res = run_builder(name, scen, Path(work) / name, "watch", workers, watch=addr)
    hits = collections.defaultdict(list)
    for sc, (case, _) in zip(scen, res):
        if case is None or "watch" not in case:
            continue
        for item, v in items.items():
            for draws, caller in case["watch"].get("%X" % v["at"], [])[:1]:
                if len(hits[item]) < 3:
                    hits[item].append({"scenario": sc, "draws": draws, "caller": caller, "local": caller is not None and v["lo"] <= caller < v["hi"]})
    json.dump({"items": items, "hits": hits}, open(Path(work) / name / "watch.json", "w"))
    print(name, "watched", len(scen), "scenarios,", len(hits), "of", len(items), "items hit, %ds" % (time.time() - t0))


def gen_cands(name, classified, work, wide=False):
    res = frontier(load_classified(classified))
    w = json.load(open(Path(work) / name / "watch.json"))
    cands, why = {}, collections.Counter()
    for item in w["items"]:
        hs = w["hits"].get(item)
        if not hs:
            why["no-hit"] += 1
            continue
        kind = res[item]["kind"]
        hit = next((h for h in hs if h["local"]), None)
        if hit is None and (kind in ("random",) or kind.startswith("helper:")):
            hit = hs[0]
        raws = GEN_RAWS
        if hit is None and wide and hs[0]["draws"] > 0:
            hit, raws = hs[0], WIDE_RAWS            # the latest draw came through a helper: both ends and the middle only
        if hit is None or hit["draws"] < 1:
            why["no-draw-near" if hit is None else "no-draw-before"] += 1
            continue
        k = hit["draws"] - 1
        for raw in raws:
            key = "%s|R%d_%d" % (hit["scenario"]["origin"], k, raw)
            c = cands.setdefault(key, {"scenario": {**hit["scenario"], "force": {str(k): raw}}, "items": []})
            c["items"].append(item)
        why["made"] += 1
    json.dump(cands, open(Path(work) / name / "cands.json", "w"))
    print(name, "candidates", len(cands), dict(why))


def gen_run(name, work, missed, workers=4):
    """The candidates on the original with coverage: <work>/<name>/kept.json (the cases reaching an open item, their coverage lines)."""
    cands = json.load(open(Path(work) / name / "cands.json"))
    keys = sorted(cands)
    scen = []
    for k, key in enumerate(keys):
        sc = dict(cands[key]["scenario"])
        sc["index"] = k
        scen.append(sc)
    opens = OpenSet(missed, records=GEN[name][1])
    t0 = time.time()
    res = run_builder(name, scen, Path(work) / name, "run", workers, coverage=True)
    kept, stats = [], collections.Counter()
    for key, sc, (case, line) in zip(keys, scen, res):
        if case is None or line is None:
            stats["no case"] += 1
            continue
        cov = json.loads(line)
        got = opens.reached(cov)
        stats["hit" if set(cands[key]["items"]) & got else ("other" if got else "none")] += 1
        if got:
            kept.append({"key": key, "case": case, "line": line, "targets": cands[key]["items"], "reached": sorted(got)})
    json.dump(kept, open(Path(work) / name / "kept.json", "w"))
    print(name, len(scen), "runs", dict(stats), "%ds" % (time.time() - t0))


def gen_corpus(name, work, drop=()):
    """kept.json as a corpus file for the C# test (<NAME>_CORPUS) and the kept coverage lines (cov/cov-mb.jsonl), less the dropped keys."""
    kept = [k for k in json.load(open(Path(work) / name / "kept.json")) if k["key"] not in set(drop)]
    first = json.loads((Path(work) / name / "run-0.json").read_text())
    corpus = {k: v for k, v in first.items() if k != "cases"}
    corpus["cases"] = [{**k["case"], "function": CORPUS_FUNCTION.get(name, k["case"]["function"])} for k in kept]
    (Path(work) / name / ("mb-%s.json" % name)).write_text(json.dumps(corpus) + "\n")
    cov = Path(work) / name / "cov-agree"
    cov.mkdir(exist_ok=True)
    with open(cov / "cov-mb.jsonl", "w", encoding="utf-8") as f:
        for k in kept:
            f.write(k["line"] + "\n")
    print(name, "corpus", len(kept), "cases")


# ---- table --------------------------------------------------------------------------------------------------------------------
def table(classified, candsf, work, gained=None):
    """Per kind of frontier outcome: closed (an agreeing run reaches it), tried (a candidate aimed at it, not closed), not attempted."""
    res = frontier(load_classified(classified))
    tried = collections.defaultdict(set)
    for cf in candsf.split(","):
        for key, c in json.load(open(cf))["cands"].items():
            for it in c["items"]:
                tried[it].add("poke" if c["ops"] else "force")
    closed, differ = set(), set()
    for wk in work.split(","):
        excluded = set((Path(wk) / "excluded.txt").read_text(encoding="utf-8").split()) if (Path(wk) / "excluded.txt").exists() else set()
        for f in Path(wk).glob("fuzz/*/*.json"):
            d = json.loads(f.read_text(encoding="utf-8"))
            for item in d["kept"]:
                jid = "%s|%s#%d" % (d["function"], d["label"], item["iteration"])
                (differ if jid in excluded else closed).update(item["reached"])
    differ -= closed
    rows = collections.defaultdict(collections.Counter)
    for it, v in res.items():
        k = v["kind"] if not v["kind"].startswith("helper:") else "helper"
        rows[k]["all"] += 1
        if it in closed:
            rows[k]["closed"] += 1
        elif it in differ:
            rows[k]["only differing runs"] += 1
        elif it in tried:
            rows[k]["tried, not reached"] += 1
        else:
            rows[k]["not attempted"] += 1
    tot = collections.Counter()
    print("| kind | frontier | closed | tried, not reached | only differing/not judged runs | not attempted |")
    print("|---|---:|---:|---:|---:|---:|")
    for k, c in sorted(rows.items(), key=lambda x: -x[1]["all"]):
        tot.update(c)
        print("| %s | %d | %d | %d | %d | %d |" % (k, c["all"], c["closed"], c["tried, not reached"], c["only differing runs"], c["not attempted"]))
    print("| total | %d | %d | %d | %d | %d |" % (tot["all"], tot["closed"], tot["tried, not reached"], tot["only differing runs"], tot["not attempted"]))
    behind = {x for x in closed if x not in res}
    print("open items reached by agreeing runs: %d (of them frontier outcomes %d, others %d)" % (len(closed), len(closed & set(res)), len(behind)))


def main(argv):
    cmd = argv[1]
    if cmd == "reach":
        out = argv[argv.index("--out") + 1]
        roots = [a for a in argv[3:argv.index("--out")]]
        reach(argv[2], roots, out)
    elif cmd == "watch":
        jf = argv[argv.index("--jobs") + 1:]
        watch(argv[2], argv[3], argv[4], argv[5], jf)
    elif cmd == "gen":
        gen(argv[2], argv[3], argv[4], int(argv[argv.index("--ks") + 1]) if "--ks" in argv else 2,
            argv[argv.index("--skip") + 1] if "--skip" in argv else None)
    elif cmd == "run":
        workers = int(argv[argv.index("--workers") + 1]) if "--workers" in argv else 4
        run(argv[2], argv[3], argv[4], min(4, workers), argv[argv.index("--open") + 1])
    elif cmd == "worker":
        run_worker(argv[2], argv[3], argv[4], argv[5].split(","), argv[6])
    elif cmd == "gen-watch":
        seeds = [int(x) for x in argv[argv.index("--seeds") + 1].split(",")] if "--seeds" in argv else [0]
        gen_watch(argv[2], argv[3], argv[4], seeds)
    elif cmd == "gen-cands":
        gen_cands(argv[2], argv[3], argv[4], "--wide" in argv)
    elif cmd == "gen-run":
        gen_run(argv[2], argv[3], argv[argv.index("--open") + 1])
    elif cmd == "gen-corpus":
        drop = argv[argv.index("--drop") + 1].split(",") if "--drop" in argv else ()
        gen_corpus(argv[2], argv[3], drop)
    elif cmd == "table":
        table(argv[2], argv[3], argv[4], argv[argv.index("--gained") + 1] if "--gained" in argv else None)
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
