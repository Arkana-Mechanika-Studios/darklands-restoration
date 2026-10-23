"""Coverage-guided flipping of open branch outcomes in the functions the unit oracle calls.

The corpora of tools/oracle/build_*_corpus.py run one original function from synthetic state (run_unit.py). For the
outcomes of docs/faithfulness/branches-missed.csv that lie in the functions such a run touches, this tool takes the
corpus's own cases as bases and looks for inputs that take the other side of a branch:
* extras (flip_corpora.py, per corpus): variants the corpus's generator never builds, from the conditions the open
  branches test (read with branch_flip.py / the compares of the function): the current place, the whole party, the party
  in the open country with a stubbed callee's answer;
* a sweep: the state bytes whose perturbation changes the run's path are found by delta debugging (several perturbations
  over groups of bytes), then set to candidate values (small numbers, sign and byte limits, the base value +-1, the
  immediates of the touched functions' compares); a mutation is mapped back to the corpus's inputs (the fields the C#
  replay test models; the rest is rejected) or, for flat inputs, applied to the inputs directly.
Every kept variant is rebuilt from its inputs alone and run again with coverage (what the C# replay will see).

usage: python -I -B flip_units.py search <corpus> <findings.json> [--max-bases N] [--hours H] [--extras-only]
       python -I -B flip_units.py emit <corpus> <findings.json> <Corpus/<corpus>-flip.json> <cov dir> [leads keys.json]
(DARKLANDS_LOCAL: the local folder with branches-work; DARKLANDS_DIR: the game folder)
"""
from __future__ import annotations

import collections
import copy
import csv
import glob
import hashlib
import itertools
import json
import os
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import capstone  # noqa: E402
import capstone.x86_const as cx  # noqa: E402
import unicorn  # noqa: E402
import run_unit  # noqa: E402
from run_unit import Rtlink  # noqa: E402
import cfg as cfgmod  # noqa: E402

ROOT = HERE.parents[1]
LOCAL = Path(os.environ.get("DARKLANDS_LOCAL", ROOT / "local"))
WORK = LOCAL / "faithfulness" / "branches-work"
EXE_PATH = Path(os.environ.get("DARKLANDS_DIR", "")) / "DARKLAND.EXE"
CORPORA = ["rules", "core", "trading", "chargen", "names", "path", "saints", "tactics", "battle"]
EXE = EXE_PATH.read_bytes()
RT = Rtlink(EXE)
FUNCS = cfgmod.load_functions(ROOT / "docs/source-map/original-functions.csv", RT)
BYREC = collections.defaultdict(list)
for _f in FUNCS:
    BYREC[_f.record].append(_f)


def open_items():
    out = collections.defaultdict(set)
    for r in csv.DictReader(open(ROOT / "docs/faithfulness/branches-missed.csv", encoding="utf-8")):
        if r["status"] == "open":
            out[r["function"]].add((r["payload"], r["kind"]))
    return out


# ---------------------------------------------------------------- targets

class Universe:
    """The open outcomes of a set of functions as coverage keys: ('i', rec, payload) for a block, ('e', rec, jcc, dest) for an edge."""

    def __init__(self, names=None):
        self.opens = open_items()
        self.fnmap = {f.name: f for f in FUNCS}
        self.keys = {}          # key -> [(function, payload, kind)]
        self.consts = {}        # function -> set of immediates of its compares
        self.skipped = []
        for name in (names if names is not None else self.opens):
            fn = self.fnmap.get(name)
            if fn is None or name not in self.opens:
                continue
            self.add_function(fn, self.opens[name])

    def add_function(self, fn, items):
        g = cfgmod.build(EXE, RT, fn)
        base = fn.linear - fn.payload(fn.linear)
        br = {j - base: (t - base, f - base) for j, t, f, m in g.branches}
        cs = set()
        for ins in g.insns.values():
            if ins.mnemonic in ("cmp", "test", "sub", "add", "and") and len(ins.operands) == 2 and ins.operands[1].type == cx.X86_OP_IMM:
                cs.add(ins.operands[1].imm & 0xFFFF)
        self.consts[fn.name] = cs
        for payload, kind in items:
            p = int(payload, 16)
            item = (fn.name, payload, kind)
            if kind == "block":
                self.keys.setdefault(("i", fn.record, p), []).append(item)
            elif p in br:
                t, f = br[p]
                self.keys.setdefault(("e", fn.record, p, t if kind == "taken" else f), []).append(item)
            else:
                self.skipped.append(item)

    def hits(self, coverage):
        """Open items a run's coverage reaches."""
        run_keys = set()
        for rec, ps in coverage["insns"].items():
            for p in ps:
                run_keys.add(("i", rec, p))
        for rec, es in coverage["edges"].items():
            for j, d in es:
                run_keys.add(("e", rec, j, d))
        out = set()
        for k in run_keys & self.keys.keys():
            out.update(self.keys[k])
        return out


# ---------------------------------------------------------------- running

def run_spec(spec, hooks=None):
    """The run's result with coverage, or None when the oracle stops (instruction limit, INT, ...)."""
    spec = dict(spec)
    spec["coverage"] = True
    try:
        return run_unit.run(EXE, spec, hooks=hooks)
    except (SystemExit, unicorn.UcError, struct.error, ValueError, OverflowError):
        return None


def signature(res):
    c = res["coverage"]
    return frozenset([("i", r, p) for r, ps in c["insns"].items() for p in ps] +
                     [("e", r, j, d) for r, es in c["edges"].items() for j, d in es]) | {("a", res["final"]["ax"])}


_IMG = {}


def initial_image(sections):
    """Memory as run_unit loads it (root, then the sections), before the spec's patches."""
    key = tuple(sections)
    if key not in _IMG:
        mem = bytearray(0x100000)
        mem[0:len(RT.root)] = RT.root
        for idx in sections:
            s = RT.sections[idx]
            mem[s["load_seg"] * 16:s["load_seg"] * 16 + s["length"]] = EXE[s["file_offset"]:s["file_offset"] + s["length"]]
        _IMG[key] = mem
    return _IMG[key]


MODES = {"ff": lambda v: 0xFF, "zero": lambda v: 0, "x55": lambda v: v ^ 0x55, "inc": lambda v: (v + 1) & 0xFF}


def direct_operands(coverage):
    """DS offsets of the direct memory operands of the instructions a run executed (byte addresses, widths covered)."""
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    md.detail = True
    out = set()
    for rec, ps in coverage["insns"].items():
        for fn in BYREC.get(rec, []):
            lo = fn.payload(fn.linear)
            mine = [p for p in ps if lo <= p < lo + fn.size]
            if not mine:
                continue
            g = cfgmod.build(EXE, RT, fn)
            base = fn.linear - lo
            for p in mine:
                ins = g.insns.get(p + base)
                if ins is None:
                    continue
                for o in ins.operands:
                    if o.type == cx.X86_OP_MEM and not o.mem.base and not o.mem.index and o.mem.segment in (0, cx.X86_REG_DS):
                        d = o.mem.disp & 0xFFFF
                        out.update(range(d, d + max(1, o.size)))
    return out


class Slots:
    """The bytes of a spec's state that can be perturbed: its memory patches (kind 'm': patch, byte) and the data
    segment bytes no patch covers (kind 'x': offset)."""

    def __init__(self, spec, free=None):
        self.spec = spec
        ds = spec["ds"]
        self.m = [(i, k) for i, p in enumerate(spec.get("memory", [])) for k in range(len(p["hex"]) // 2)]
        covered = set()
        for p in spec.get("memory", []):
            seg = int(p["segment"], 16) if "segment" in p else ds
            if seg == ds:
                o = int(p["offset"], 16)
                covered.update(range(o, o + len(p["hex"]) // 2))
        self.x = [o for o in sorted(free if free is not None else range(0x10000)) if o not in covered]
        self.img = initial_image(spec["sections"])
        self.ds = ds
        self.n = len(self.m) + len(self.x)

    def apply(self, lo, hi, mode):
        """Spec with slots [lo, hi) perturbed."""
        f = MODES[mode]
        spec = dict(self.spec)
        mem = list(spec.get("memory", []))
        by_patch = collections.defaultdict(list)
        xs = []
        for t in range(lo, min(hi, len(self.m))):
            i, k = self.m[t]
            by_patch[i].append(k)
        for t in range(max(lo, len(self.m)), hi):
            xs.append(self.x[t - len(self.m)])
        for i, ks in by_patch.items():
            h = bytearray.fromhex(mem[i]["hex"])
            for k in ks:
                h[k] = f(h[k])
            mem[i] = dict(mem[i], hex=h.hex())
        if xs:
            run_start = xs[0]
            prev = xs[0]
            buf = [f(self.img[self.ds * 16 + xs[0]])]
            for o in xs[1:]:
                if o != prev + 1:
                    mem.append({"offset": "%04X" % run_start, "hex": bytes(buf).hex()})
                    run_start, buf = o, []
                buf.append(f(self.img[self.ds * 16 + o]))
                prev = o
            mem.append({"offset": "%04X" % run_start, "hex": bytes(buf).hex()})
        spec["memory"] = mem
        return spec

    def describe(self, t):
        if t < len(self.m):
            return ("m",) + self.m[t]
        return ("x", self.x[t - len(self.m)])


def discover(spec, hooks, base_sig, free, limit=400):
    """Slots whose perturbation changes the run's coverage (delta-debugging over several perturbations)."""
    sl = Slots(spec, free)
    relevant, runs = set(), 0

    def rec(lo, hi, mode):
        nonlocal runs
        if len(relevant) >= limit:
            return
        res = run_spec(sl.apply(lo, hi, mode), hooks)
        runs += 1
        if res is not None and signature(res) == base_sig:
            return
        if hi - lo == 1:
            relevant.add(lo)
            return
        mid = (lo + hi) // 2
        rec(lo, mid, mode)
        rec(mid, hi, mode)

    for mode in ("ff", "zero", "x55"):
        # chunks, so that one big perturbation does not hide independent ones
        step = 2048
        for lo in range(0, sl.n, step):
            rec(lo, min(sl.n, lo + step), mode)
    return sl, sorted(relevant), runs


GENERIC = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 16, 20, 31, 32, 50, 64, 99, 100, 0x7F, 0x80, 0x81, 0xFE, 0xFF]


def candidates(base, pool):
    c = [(base + d) & 0xFF for d in (-2, -1, 1, 2)] + [base ^ 0x80] + GENERIC
    for k in pool:
        c += [k & 0xFF, (k - 1) & 0xFF, (k + 1) & 0xFF, (k >> 8) & 0xFF]
    out, seen = [], {base}
    for v in c:
        if v not in seen:
            seen.add(v)
            out.append(v)
    return out


def with_byte(sl, t, value):
    """Spec with slot t set to value."""
    spec = dict(sl.spec)
    mem = list(spec.get("memory", []))
    if t < len(sl.m):
        i, k = sl.m[t]
        h = mem[i]["hex"]
        mem[i] = dict(mem[i], hex=h[:2 * k] + "%02X" % value + h[2 * k + 2:])
    else:
        mem.append({"offset": "%04X" % sl.x[t - len(sl.m)], "hex": "%02X" % value})
    spec["memory"] = mem
    return spec


def slot_value(sl, t):
    if t < len(sl.m):
        i, k = sl.m[t]
        return int(sl.spec["memory"][i]["hex"][2 * k:2 * k + 2], 16)
    return sl.img[sl.ds * 16 + sl.x[t - len(sl.m)]]


def sweep(spec0, uni, hooks=None, deadline=None, pre=None):
    """Mutate the bytes whose value decides the run's path; [(items, spec, diffs)] for each distinct set of open outcomes reached."""
    res0 = run_spec(spec0, hooks)
    if res0 is None:
        return [], 0, 0
    base_sig = signature(res0)
    base_hits = uni.hits(res0["coverage"])
    pool = set()
    for rec, ps in res0["coverage"]["insns"].items():
        for fn in BYREC.get(rec, []):
            lo = fn.payload(fn.linear)
            if fn.name in uni.consts and any(lo <= p < lo + fn.size for p in ps):
                pool |= uni.consts[fn.name]
    pool = sorted(pool)
    spec0 = dict(spec0, max_instructions=min(spec0.get("max_instructions", 200000), 6 * res0["instructions"] + 2000))
    sl, relevant, nd = discover(spec0, hooks, base_sig, direct_operands(res0["coverage"]))
    mutants = []
    for j, a in enumerate(spec0.get("args", [])):
        v = int(str(a), 16)
        for c in [0, 1, 2, 3, 4, 0xFFFF, 0x7FFF, 0x8000, v + 1, v - 1, v ^ 0x8000] + list(pool) + [k - 1 for k in pool] + [k + 1 for k in pool]:
            c &= 0xFFFF
            if c != v and (pre is None or pre([("arg", j, c)])):
                s = dict(spec0)
                s["args"] = list(spec0["args"])
                s["args"][j] = "%04X" % c
                mutants.append((s, [("arg", j, c)]))
    for t in relevant:
        bv = slot_value(sl, t)
        for c in candidates(bv, pool):
            d = [("byte",) + sl.describe(t) + (c,)]
            if pre is None or pre(d):
                mutants.append((with_byte(sl, t, c), d))
    found, nruns = {}, nd
    for s, diffs in mutants:
        if deadline and time.time() > deadline:
            break
        res = run_spec(s, hooks)
        nruns += 1
        if res is None:
            continue
        h = uni.hits(res["coverage"]) - base_hits
        if h:
            lst = found.setdefault(frozenset(h), [])
            if len(lst) < 12:
                lst.append((h, s, diffs))
    return [x for lst in found.values() for x in lst], nruns, len(relevant)


# ---------------------------------------------------------------- driver

def leaves(obj, path=()):
    """(path, value, kind) of the integer leaves and the bytes of hex strings of a JSON inputs object."""
    if isinstance(obj, bool):
        return
    if isinstance(obj, int):
        yield path, obj, "int"
    elif isinstance(obj, str):
        if len(obj) >= 2 and len(obj) % 2 == 0 and all(c in "0123456789abcdefABCDEF" for c in obj):
            for k in range(len(obj) // 2):
                yield path + (k,), int(obj[2 * k:2 * k + 2], 16), "byte"
    elif isinstance(obj, dict):
        for k, v in obj.items():
            yield from leaves(v, path + (k,))
    elif isinstance(obj, list):
        for k, v in enumerate(obj):
            yield from leaves(v, path + (k,))


def set_leaf(obj, path, kind, value):
    """Copy of obj with the leaf at path set (a byte leaf is a byte of the hex string at path[:-1])."""
    obj = copy.deepcopy(obj)
    if kind == "byte":
        node = obj
        for p in path[:-2]:
            node = node[p]
        h = node[path[-2]]
        node[path[-2]] = h[:2 * path[-1]] + "%02x" % (value & 0xFF) + h[2 * path[-1] + 2:]
        return obj
    node = obj
    for p in path[:-1]:
        node = node[p]
    node[path[-1]] = value
    return obj


def sweep_inputs(adapter, base, uni, deadline=None, domain=None):
    """The input-level sweep: leaves of the base's inputs perturbed in groups to find those that change the run's
    path, then set to candidate values. [(items, inputs, desc)]."""
    spec0, hooks0 = adapter.rebuild(base, base.inputs)
    res0 = run_spec(spec0, hooks0)
    if res0 is None:
        return [], 0, 0
    base_sig = signature(res0)
    base_hits = uni.hits(res0["coverage"])
    pool = set()
    for rec, ps in res0["coverage"]["insns"].items():
        for fn in BYREC.get(rec, []):
            lo = fn.payload(fn.linear)
            if fn.name in uni.consts and any(lo <= p < lo + fn.size for p in ps):
                pool |= uni.consts[fn.name]
    pool = sorted(pool)
    lv = [x for x in leaves(base.inputs) if adapter.leaf_ok(base, x[0])]
    nruns = 0

    def run_inputs(inp):
        nonlocal nruns
        try:
            s, h = adapter.rebuild(base, inp)
        except (struct.error, ValueError, OverflowError, KeyError, IndexError, TypeError):
            return None
        s = dict(s, max_instructions=min(s.get("max_instructions", 200000), 6 * res0["instructions"] + 2000))
        nruns += 1
        return run_spec(s, h)

    def perturbed(group, mode):
        inp = base.inputs
        for path, v, kind in group:
            nv = {"ff": 0xFF, "zero": 0, "x55": v ^ 0x55, "one": 1, "neg": -1}[mode] if kind == "byte" else \
                {"ff": 0x7FFF, "zero": 0, "x55": v ^ 0x55, "one": 1, "neg": -1}[mode]
            inp = set_leaf(inp, path, kind, nv)
        return inp

    relevant = []

    def rec(group, mode):
        res = run_inputs(perturbed(group, mode))
        if res is None or signature(res) == base_sig:
            return
        if len(group) == 1:
            if group[0] not in relevant:
                relevant.append(group[0])
            return
        mid = len(group) // 2
        rec(group[:mid], mode)
        rec(group[mid:], mode)

    for mode in ("ff", "zero", "x55", "one"):
        for lo in range(0, len(lv), 256):
            rec(lv[lo:lo + 256], mode)
    found = {}
    for path, v, kind in relevant:
        if kind == "byte":
            cands = candidates(v, pool)
        else:
            dom = domain(base, path) if domain else set()
            cands = [c for c in sorted(set([v - 2, v - 1, v + 1, v + 2, 0, 1, -1, 2, 3, 7, 8, 15, 16, 100, 255, 0x7FFF]) | set(dom) | {k for k in pool if k < 0x8000} | {(k - 1) for k in pool if k < 0x8000} | {(k + 1) for k in pool if k < 0x8000}) if c != v]
        for c in cands:
            if deadline and time.time() > deadline:
                break
            inp = set_leaf(base.inputs, path, kind, c)
            if not adapter.valid(base, inp):
                continue
            res = run_inputs(inp)
            if res is None:
                continue
            h = uni.hits(res["coverage"]) - base_hits
            if h:
                lst = found.setdefault(frozenset(h), [])
                if len(lst) < 12:
                    lst.append((h, inp, [("leaf", path, c)]))
    return [x for lst in found.values() for x in lst], nruns, len(relevant)


def edge_sources(uni):
    return {(k[1], k[2]) for k in uni.keys if k[0] == "e"}


def select_bases(bases, uni, per_group=2, limit=None, log=print):
    """Run each base once; group by the open branches it executes; return [(base, executed, res)] best groups first."""
    src = edge_sources(uni)
    groups = collections.defaultdict(list)
    t0 = time.process_time()
    for b in bases:
        res = run_spec(b.spec, b.hooks)
        if res is None:
            continue
        ex = frozenset((rec, p) for rec, ps in res["coverage"]["insns"].items() for p in ps if (rec, p) in src)
        if ex:
            groups[ex].append((b, res))
    log("bases %d, groups %d (%.0fs)" % (len(bases), len(groups), time.process_time() - t0))
    order = sorted(groups.items(), key=lambda kv: -len(kv[0]))
    out = []
    for ex, lst in order:
        out += [(b, ex, res) for b, res in lst[:per_group]]
    return out[:limit] if limit else out


def search(adapter, uni, out_path, max_bases=None, hours=None, per_group=2, log=print, extras_only=False, functions=None):
    bases = adapter.bases()
    if functions:
        bases = [b for b in bases if b.function in functions]
    adapter.selftest(bases)
    findings, seen_items = [], set()
    extra_stats = {"runs": 0, "cpu": 0.0}
    if hasattr(adapter, "extra"):
        t0 = time.process_time()
        for b in bases:
            for inp in adapter.extra(b, uni):
                spec2, hooks2 = adapter.rebuild(b, inp)
                res = run_spec(spec2, hooks2)
                extra_stats["runs"] += 1
                if res is None:
                    continue
                h = uni.hits(res["coverage"])
                new = h - seen_items
                if new:
                    findings.append({"base": b.label, "function": b.function, "inputs": inp, "items": sorted(h), "new": sorted(new)})
                    seen_items |= h
        extra_stats["cpu"] = time.process_time() - t0
        log("extra variants: runs %(runs)d cpu %(cpu).0fs; items %%d" % extra_stats % len(seen_items))
        json.dump({"stats": {"extra": extra_stats}, "findings": findings, "items": sorted(seen_items)}, open(out_path, "w"), indent=1)
    if extras_only:
        return findings, extra_stats
    chosen = select_bases(bases, uni, per_group, max_bases, log)
    deadline = time.time() + hours * 3600 if hours else None
    stats = {"runs": 0, "cpu": 0.0, "bases": 0, "rejected": collections.Counter(), "extra": extra_stats}
    for b, ex, res0 in chosen:
        if deadline and time.time() > deadline:
            break
        t0 = time.process_time()
        if getattr(adapter, "mode", "bytes") == "inputs":
            found, nruns, nrel = sweep_inputs(adapter, b, uni, deadline, getattr(adapter, "domain", None))
        else:
            found, nruns, nrel = sweep(b.spec, uni, b.hooks, deadline, lambda d: adapter.remap(b, None, d)[0] is not None)
        stats["runs"] += nruns
        stats["bases"] += 1
        got = 0
        done_keys = set()
        for items, spec, diffs in found:
            if frozenset(items) in done_keys:
                continue
            inputs, why = (spec, None) if getattr(adapter, "mode", "bytes") == "inputs" else adapter.remap(b, spec, diffs)
            if inputs is None:
                stats["rejected"][why] += 1
                continue
            spec2, hooks2 = adapter.rebuild(b, inputs)
            res = run_spec(spec2, hooks2)
            if res is None:
                stats["rejected"]["rebuilt-run-fails"] += 1
                continue
            h = uni.hits(res["coverage"])
            if not h:
                stats["rejected"]["rebuilt-misses"] += 1
                continue
            new = h - seen_items
            done_keys.add(frozenset(items))
            findings.append({"base": b.label, "function": b.function, "inputs": inputs, "items": sorted(h), "new": sorted(new)})
            seen_items |= h
            got += len(new)
        stats["cpu"] += time.process_time() - t0
        log("%s: runs %d relevant %d findings %d new %d (total items %d, cpu %.0fs)" %
            (b.label, nruns, nrel, len(found), got, len(seen_items), stats["cpu"]))
        json.dump({"stats": {**stats, "rejected": dict(stats["rejected"])}, "findings": findings, "items": sorted(seen_items)},
                  open(out_path, "w"), indent=1)
    return findings, stats


def scope_names():
    """Functions with open outcomes that the unit corpora's runs touch (their coverage in branches-work/<corpus>/cov),
    without the battlefield generators R004..R013 (another sweep's)."""
    opens = open_items()
    byrec = BYREC
    names = set()
    for c in CORPORA:
        touched = collections.defaultdict(set)
        for f in glob.glob(str(WORK / c / "cov" / "cov-*.jsonl")):
            for line in open(f):
                if line.strip():
                    for r, v in json.loads(line)["insns"].items():
                        touched[r].update(v)
        for r, ps in touched.items():
            if r.startswith("R") and 4 <= int(r[1:]) <= 13:
                continue
            for fn in byrec.get(r, []):
                lo = fn.payload(fn.linear)
                if fn.name in opens and any(lo <= p < lo + fn.size for p in ps):
                    names.add(fn.name)
    return names


def finding_key(f):
    return hashlib.md5(json.dumps([f["base"], f["inputs"]], sort_keys=True).encode()).hexdigest()[:12]


def emit(adapter, findings_path, corpus_out, cov_dir, leads_path=None):
    """Corpus of the findings (greedy cover of their items), cases run on the original with the builder's own code;
    one coverage line per case (unit = flip-<corpus>-<n>) in cov_dir. Findings whose key is in leads_path (a JSON list)
    are left out: the C# differs there (listed as leads)."""
    data = json.load(open(findings_path))
    bases = {b.label: b for b in adapter.bases()}
    leads = set(json.load(open(leads_path))) if leads_path and Path(leads_path).exists() else set()
    fs = [f for f in data["findings"] if finding_key(f) not in leads]
    todo = {tuple(i) for f in fs for i in f["items"]}
    chosen = []
    while todo:
        best = max(fs, key=lambda f: len(todo & {tuple(i) for i in f["items"]}))
        gain = todo & {tuple(i) for i in best["items"]}
        if not gain:
            break
        chosen.append(best)
        todo -= gain
    Path(cov_dir).mkdir(parents=True, exist_ok=True)
    cases, images, covlines = [], None, []
    for n, f in enumerate(chosen):
        b = bases[f["base"]]
        case, spec, hooks, images = adapter.run_case(EXE, b, f["inputs"])
        res = run_spec(spec, hooks)
        case["flip"] = {"base": f["base"], "n": n, "key": finding_key(f), "items": ["|".join(i) for i in f["items"]]}
        cases.append(case)
        covlines.append(json.dumps({"unit": "flip-%s-%d" % (adapter.name, n), "error": None, **res["coverage"]}, separators=(",", ":")))
    head = json.loads((ROOT / "tests/Darklands.Oracle.Tests/Corpus" / (adapter.corpus + ".json")).read_text())
    head.pop("cases")
    corpus = {**head, "loadedImages": images or head.get("loadedImages"), "cases": cases}
    Path(corpus_out).write_text(json.dumps(corpus, indent=0) + "\n")
    (Path(cov_dir) / "cov-flip.jsonl").write_text("\n".join(covlines) + "\n")
    print("cases", len(cases), "items", len({tuple(i) for f in chosen for i in f["items"]}))


def adapter_of(name):
    import flip_corpora
    return getattr(flip_corpora, {"path": "Path_"}.get(name, name.capitalize()))(EXE)


def measure(cov_dirs, names=None):
    """Open outcomes (of the in-scope functions) that the coverage lines of the given folders reach: {function: [(payload, kind)]}."""
    uni = Universe(names if names is not None else scope_names())
    got = set()
    for d in cov_dirs:
        for f in glob.glob(str(Path(d) / "cov-*.jsonl")):
            for line in open(f):
                if line.strip():
                    got |= uni.hits(json.loads(line))
    return got


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "search":
        import argparse
        ap = argparse.ArgumentParser()
        ap.add_argument("cmd")
        ap.add_argument("corpus")
        ap.add_argument("out")
        ap.add_argument("--max-bases", type=int)
        ap.add_argument("--hours", type=float)
        ap.add_argument("--per-group", type=int, default=2)
        ap.add_argument("--extras-only", action="store_true")
        ap.add_argument("--functions")
        a = ap.parse_args()
        ad = adapter_of(a.corpus)
        uni = Universe(scope_names())
        print("keys", len(uni.keys), "skipped", len(uni.skipped))
        search(ad, uni, a.out, a.max_bases, a.hours, a.per_group, extras_only=a.extras_only, functions=a.functions.split(",") if a.functions else None)
    elif cmd == "measure":
        got = measure(sys.argv[2:])
        print("open outcomes reached:", len(got))
        for fn in sorted({g[0] for g in got}):
            print(" ", fn, sum(1 for g in got if g[0] == fn))
    elif cmd == "emit":
        ad = adapter_of(sys.argv[2])
        emit(ad, sys.argv[3], sys.argv[4], sys.argv[5], sys.argv[6] if len(sys.argv) > 6 else None)
