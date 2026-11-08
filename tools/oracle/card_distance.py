"""Branch-distance search in the ORIGINAL for the open frontier compares of the card scripts (opt-in; writes nothing into the corpora).

For each card with open frontier outcomes (mutate_branch.py classify: the compare deciding each one, its flag setter and jump) the
card's reaching runs (seeds) are run on the original with
- a read-only hook at each compare's flag setter: both operand values, the draws and menus so far, and the branch distance (how far
  the decisive operand is from sending the jump the wanted way: 0 = it goes that way);
- the provenance of the saved game's bytes: the loader's reads (INT 21h AH=3Fh) map memory to file offsets of the loaded .SAV, through
  the temporary files it writes and reads back (charactr.tmp ...); a memory-read hook on those bytes logs which save offsets the card
  read before each compare.
Then per target (many per card at once): input-to-state candidates (a save offset read before the compare that holds the operand's
value is set to the values that flip it, mutate_branch.values_for), then a hill-climb over the bytes read before the compare, the
forced draws before it (R parts) and the menu choices (P parts), fitness = the branch distance. Every run that sends a target's jump
the wanted way is a hit: written as a saved game (<out>/saves/<id>/FUZZ.SAV, raw) and a run key (<out>/kept.jsonl), which the
C# then reads with its own loader (CardOracleStates, S keys), exports canonically and replays (the confirm chain of CardFuzz); only
agreeing runs count, and the coverage is the original's own trace of the confirming run.

usage: python -I -B card_distance.py <classified.json> <seeds.json> <states> <out dir> [--cards 97,A0] [--evals 80] [--workers 4]
   seeds.json: {card hex: [{"state": "key" | "key M1L7..", "items": [frontier item keys]}]} (the reaching runs); states: the seeds'
   start states (CardAimSearch.ExportKeys). The oracle runs hold the heavy lock of the helpers' rules.
"""
from __future__ import annotations

import collections
import hashlib
import json
import os
import random
import re
import shutil
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import capstone  # noqa: E402
import capstone.x86_const as cx  # noqa: E402
from branch_flip import flags, jtaken  # noqa: E402
from dosmachine import P, linear  # noqa: E402
from mutate_branch import GAME, REPO, Rtlink, cfgmod, leaf, operand_value, parse_state, state_key, values_for  # noqa: E402
from unicorn import UC_HOOK_MEM_READ  # noqa: E402

SAVE_NAME = "saves\\oracle.sav"
# the key parts the search changes or keeps: state-free (texts, forced line, battle and prayer results) and the values a saved game does
# not hold (generator, bankers): kept in the confirming key (CardFuzz.KeptPart)
KEPT = re.compile(r"^(F\d+\.\d+|B\d(_\d)*|Y[cfa](_[cfa])*|T\$\w+|s\d+|(rn|fu|me|ha)-?\d+)$")
STATEFUL_KEPT = re.compile(r"^(s\d+|(rn|fu|me|ha)-?\d+)$")
INTERESTING = [0, 1, 2, 3, 4, 5, 7, 8, 10, 16, 20, 32, 50, 64, 99, 100, 127, 128, 255, 256, 1000, 0x7FFF, 0x8000, 0xFFFF]
DRAWS = [0, 1, 327, 3276, 8191, 8192, 10922, 16383, 16384, 21845, 24575, 24576, 29490, 32440, 32766, 32767]


def parts_of(key):
    if "-V" not in key:
        return []
    return key.split("-V", 1)[1].rsplit("-O", 1)[0].split("+")


def option_of(key):
    return int(key.rsplit("-O", 1)[1])


def path_of(key):
    for p in parts_of(key):
        if re.fullmatch(r"P\d+(_\d+)*", p):
            return [int(x) for x in p[1:].split("_")]
    return []


def draws_of(key):
    out = {}
    for p in parts_of(key):
        if re.fullmatch(r"R\d+_\d+", p):
            k, v = (int(x) for x in p[1:].split("_"))
            out[k] = v
    return out


def run_key(head, option, kept, path, draws):
    parts = list(kept) + (["P" + "_".join(map(str, path))] if path else []) + ["R%d_%d" % (k, v) for k, v in sorted(draws.items())]
    return "%s-V%s-O%d" % (head, "+".join(parts), option) if parts else "%s-O%d" % (head, option)


def signed(x, w):
    m = 1 << (8 * w)
    x &= m - 1
    return x - m if x >= m >> 1 else x


def distance(v, vals):
    """The branch distance of one hit: 0 when the jump goes the wanted way, else the smallest change of either operand that sends it
    there (over the candidate values of values_for), else a large number."""
    op, jm, w = v["op"], v["jcc"], max(1, v.get("w") or 2)
    opf = {"dec": "sub", "inc": "add"}.get(op, op)
    if op in ("dec", "inc"):
        a, b = (vals[0] if vals else 0), 1
    elif v.get("self") or len(vals) < 2:
        a = b = vals[0] if vals else 0
    else:
        a, b = vals[0], vals[1]
    if a is None or b is None:
        return 1 << 20
    f = flags(opf, a, b, w) if opf in ("cmp", "sub", "test", "and", "or") else None
    if f is None:
        return 1 << 20
    t = jtaken(jm, f)
    if t is None:
        return 1 << 20
    if t == v["want_taken"]:
        return 0
    best = 1 << 19
    for target in [hv for hv, side in values_for(v, {"vals": vals}) if side == v["want_taken"] and hv is not None]:
        for cur in ((a, b) if not v.get("self") else (a,)):
            best = min(best, abs(signed(target, w) - signed(cur, w)))
    return max(best, 1)


class Search:
    """One worker's oracle, with the provenance of the loaded saved game and the compare hooks of one card's targets."""

    def __init__(self, states: Path, classified: dict, funcs):
        import card_oracle as co
        self.co = co
        self.states = states
        self.cls = classified
        self.funcs = funcs
        self.o = co.Oracle((GAME / "DARKLAND.EXE").read_bytes(), str(GAME), states, coverage=False)
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
        self.md.detail = True
        self.memprov: dict[int, int] = {}
        self.fileprov = collections.defaultdict(dict)
        self.provs: dict[str, dict] = {}
        self.reads: list[int] = []
        self.watch_reads = False
        self.hooked_ranges: set[tuple[int, int]] = set()
        self.targets: dict[str, dict] = {}
        self.hits = collections.defaultdict(list)
        self.hooked: set[str] = set()
        self._wrap()

    # ---- provenance ----------------------------------------------------------------------------------------
    def _wrap(self):
        m = self.o.m
        orig = m._dos

        def dos_inner():
            ah = (m.reg("AX") >> 8) & 0xFF
            if ah not in (0x3F, 0x40):
                return orig()
            h, n = m.reg("BX"), m.reg("CX")
            ent = m.vfs.handles.get(h)
            at = linear(m.reg("DS"), m.reg("DX"))
            pos = ent[1] if ent is not None else 0          # before the call moves it on
            r = orig()
            if ent is None:
                return r
            name = m.vfs.norm(ent[0])
            done = m.reg("AX") if ah == 0x3F else n
            if ah == 0x3F:
                fp = self.fileprov.get(name)
                for i in range(done):
                    so = pos + i if name == SAVE_NAME else (fp.get(pos + i) if fp else None)
                    if so is None:
                        self.memprov.pop(at + i, None)
                    else:
                        self.memprov[at + i] = so
            elif name != SAVE_NAME:
                fp = self.fileprov[name]
                for i in range(done):
                    so = self.memprov.get(at + i)
                    if so is None:
                        fp.pop(pos + i, None)
                    else:
                        fp[pos + i] = so
            return r
        def dos():
            try:
                return dos_inner()
            except Exception:
                import traceback
                traceback.print_exc()
                raise
        m._dos = dos

    def boot(self, key):
        """The oracle's boot of a state key, with the provenance of its bytes (kept per key, as the snapshots)."""
        o = self.o
        sk = self.co.split_key(key)[0]
        if sk in o.snapshots and sk in self.provs:
            mem, files = self.provs[sk]
        else:
            o.snapshots.pop(sk, None)
            self.memprov, self.fileprov = {}, collections.defaultdict(dict)
            o.boot(sk)
            mem, files = dict(self.memprov), {k: dict(v) for k, v in self.fileprov.items()}
            self.provs[sk] = (mem, files)
            for k in [k for k in self.provs if k not in o.snapshots]:
                del self.provs[k]
        self.memprov = dict(mem)
        self.fileprov = collections.defaultdict(dict, {k: dict(v) for k, v in files.items()})

    def _hook_reads(self, mem):
        """Memory-read hooks over the runs of addresses that hold save bytes, for one card run (removed after it: left in place,
        the next boot's loader ran into unloaded code)."""
        addrs = sorted(mem)
        start = prev = None
        ranges = []
        for a in addrs:
            if start is None:
                start = prev = a
            elif a <= prev + 16:
                prev = a
            else:
                ranges.append((start, prev))
                start = prev = a
        if start is not None:
            ranges.append((start, prev))
        return [self.o.m.uc.hook_add(UC_HOOK_MEM_READ, self._on_read, begin=lo, end=hi) for lo, hi in ranges]

    def _on_read(self, uc, access, addr, size, value, ud):
        if not self.watch_reads:
            return
        for a in range(addr, addr + size):
            so = self.memprov.get(a)
            if so is not None and (not self.reads or self.reads[-1] != so):
                self.reads.append(so)

    # ---- targets ---------------------------------------------------------------------------------------------
    def hook_targets(self, items):
        for item in items:
            if item in self.hooked or item not in self.cls:
                continue
            v = self.cls[item]
            fn = self.funcs[item.split("|")[0]]
            ip = fn.entry_ip + (int(v["at"], 16) - fn.payload(fn.linear))
            self.hooked.add(item)
            self.o.m.hook(P(fn.frame), ip, self._make(item, v), record=fn.record)

    def _make(self, item, v):
        def h(m):
            if item not in self.targets or self.o.run is None or len(self.hits[item]) >= 6:
                return
            b = bytes(m.uc.mem_read((m.reg("CS") << 4) + m.reg("IP"), 16))
            ins = next(self.md.disasm(b, m.reg("IP")))
            vals = [operand_value(m, ins, op) for op in ins.operands]
            self.hits[item].append({"vals": vals, "dist": distance(v, vals), "reads": len(self.reads), "draws": self.o.run.draws,
                                    "menus": self.o.run.menus})
        return h

    def run(self, card, key, hand, items):
        """One run of the card on the original: per target its hits (vals, distance, reads so far, draws, menus) and the reads."""
        self.targets = {i: True for i in items}
        self.hits = collections.defaultdict(list)
        self.reads = []
        self.boot(key)
        self.watch_reads = True
        handles = self._hook_reads(self.memprov)
        try:
            res = self.o.run_card(card, key, option_of(key), True, hand)
        except Exception as e:      # noqa: BLE001 - the oracle's failures are data here
            res = {"end": "error %s" % str(e)[:120], "draws": 0}
            self.__init__(self.states, self.cls, self.funcs)      # a fresh machine, the card's compares hooked again
            self.hook_targets(items)
        finally:
            self.watch_reads = False
            for h in handles:
                try:
                    self.o.m.uc.hook_del(h)
                except Exception:  # noqa: BLE001 - a machine made anew above has none of them
                    pass
        return {"hits": dict(self.hits), "reads": list(self.reads), "end": res.get("end"), "draws": res.get("draws", 0)}


def best_dist(r, item):
    hs = r["hits"].get(item) or []
    return min((h["dist"] for h in hs), default=None)


def search_card(card, seeds, s: Search, out, evals, rng, log):
    """The search of one card: returns the hits (run key, items it sent the wanted way)."""
    items = sorted({i for sd in seeds for i in sd["items"] if i in s.cls})
    s.hook_targets(items)
    open_items = set(items)
    found = []
    t0 = time.time()
    nruns = 0
    base = []
    for sd in seeds[:8]:
        key, hand = parse_state(sd["state"]) or (None, None)
        if key is None:
            continue
        sk = state_key(key)
        if not (s.states / ("state-%s.sav" % sk)).exists():
            continue
        r = s.run(card, key, hand, items)
        nruns += 1
        base.append((sd, key, hand, r))
        for i in list(open_items):
            if best_dist(r, i) == 0:
                open_items.discard(i)          # the seed already goes that way (another of its hits): not a frontier any more here
    # per target: candidates from the base runs, then a hill-climb on the best one
    sav_cache = {}

    def sav_of(key):
        sk = state_key(key)
        if sk not in sav_cache:
            sav_cache[sk] = (s.states / ("state-%s.sav" % sk)).read_bytes()
        return sav_cache[sk]

    def evaluate(card, key, hand, sav, path, draws, items_now):
        nonlocal nruns
        opt = option_of(key)
        kept = [p for p in parts_of(key) if KEPT.match(p)]
        h = hashlib.sha1(sav).hexdigest()[:12]
        cand_head = "Sdz" + h
        stateful = [p for p in kept if STATEFUL_KEPT.match(p)]
        cand_state = run_key(cand_head, opt, stateful, [], {})
        dst = s.states / ("state-%s.sav" % cand_state)
        if not dst.exists():
            dst.write_bytes(sav)
            shutil.copyfile(s.states / ("state-%s.json" % state_key(key)), s.states / ("state-%s.json" % cand_state))
        ckey = run_key(cand_head, opt, kept, path, draws)
        r = s.run(card, ckey, hand, items_now)
        nruns += 1
        return ckey, r, ("fz" + h, kept, path, draws, opt)

    order = sorted(open_items)
    rng.shuffle(order)
    for item in order:
        if item not in open_items:
            continue
        if nruns >= evals or time.time() - t0 > evals * 3:
            break
        v = s.cls[item]
        # the base run nearest to flipping it
        cands = [(best_dist(r, item), sd, key, hand, r) for sd, key, hand, r in base if best_dist(r, item) is not None]
        if not cands:
            continue
        d0, sd, key, hand, r = min(cands, key=lambda x: x[0])
        hit = min(r["hits"][item], key=lambda x: x["dist"])
        sav = sav_of(key)
        path, draws = path_of(key), draws_of(key)
        tries = []
        # input-to-state: an offset read before the compare that holds the operand's value
        before = r["reads"][:hit["reads"]]
        recent = list(dict.fromkeys(reversed(before)))[:400]
        w = max(1, v.get("w") or 2)
        wanted = [hv for hv, side in values_for(v, hit) if side == v["want_taken"] and hv is not None]
        for x in hit["vals"]:
            if x is None:
                continue
            for o in recent:
                if o + w > len(sav):
                    continue
                cur = int.from_bytes(sav[o:o + w], "little")
                cur1 = sav[o]
                for want in wanted[:2]:
                    if cur == (x & ((1 << 8 * w) - 1)):
                        new = bytearray(sav)
                        new[o:o + w] = (want & ((1 << 8 * w) - 1)).to_bytes(w, "little")
                        tries.append((bytes(new), path, draws))
                    elif w == 2 and cur1 == (x & 0xFF) and x < 0x100 and 0 <= want < 0x100:
                        new = bytearray(sav)
                        new[o] = want
                        tries.append((bytes(new), path, draws))
                if len(tries) >= 6:
                    break
        # the draws before the compare (a value drawn there) and the menu choices before it
        if v.get("kind") in ("random",) or hit["draws"] > 0:
            for k in range(max(0, hit["draws"] - 3), hit["draws"]):
                for val in rng.sample(DRAWS, 3):
                    tries.append((sav, path, {**draws, k: val}))
        if hit["menus"] > 0:
            for _ in range(3):
                p = (path + [0] * hit["menus"])[:max(len(path), hit["menus"])]
                p[rng.randrange(hit["menus"])] = rng.randrange(20)
                tries.append((sav, p, draws))
        n_i2s = len(tries)
        best = (d0, sav, path, draws, hit)
        solved = False
        steps = 0
        per_item = max(6, evals // max(1, len(order)))
        while not solved and nruns < evals and steps < per_item:
            if tries:
                csav, cpath, cdraws = tries.pop(0)
            else:
                # hill-climb from the best so far: a byte read before the compare, a draw or a choice before it
                _, bsav, bpath, bdraws, bhit = best
                csav, cpath, cdraws = bsav, bpath, bdraws
                pick = rng.random()
                if pick < 0.7 and recent:
                    o = recent[min(int(rng.expovariate(1 / 12)), len(recent) - 1)]
                    new = bytearray(bsav)
                    cur = int.from_bytes(bsav[o:o + 2], "little") if o + 2 <= len(bsav) else bsav[o]
                    nv = rng.choice([cur + rng.randint(-16, 16), rng.choice(INTERESTING), cur ^ (1 << rng.randrange(16))])
                    if o + 2 <= len(bsav) and rng.random() < 0.5:
                        new[o:o + 2] = (nv & 0xFFFF).to_bytes(2, "little")
                    else:
                        new[o] = nv & 0xFF
                    csav = bytes(new)
                elif pick < 0.85 and bhit["draws"] > 0:
                    cdraws = {**bdraws, rng.randrange(bhit["draws"]): rng.randrange(0x8000)}
                elif bhit["menus"] > 0:
                    cpath = (bpath + [0] * bhit["menus"])[:max(len(bpath), bhit["menus"])]
                    cpath[rng.randrange(bhit["menus"])] = rng.randrange(20)
                else:
                    break
            steps += 1
            ckey, cr, spec = evaluate(card, key, hand, csav, cpath, cdraws, items)
            flipped = [i for i in open_items if best_dist(cr, i) == 0]
            if flipped:
                found.append({"card": card, "key": ckey, "hand": hand, "items": flipped, "spec": spec, "sav": csav, "seed": sd["state"]})
                open_items -= set(flipped)
                solved = item in flipped
            dn = best_dist(cr, item)
            if dn is not None and dn < best[0]:
                hits = min(cr["hits"][item], key=lambda x: x["dist"])
                best = (dn, csav, cpath, cdraws, hits)
        log.write("  %s d0 %d vals %s reads %d i2s %d steps %d best %d %s\n" % (item, d0, hit["vals"], hit["reads"], n_i2s, steps, best[0], "solved" if solved else ""))
    log.write("card %X targets %d runs %d hits %d solved %d %.0fs\n" % (card, len(items), nruns, len(found), len(items) - len(open_items), time.time() - t0))
    log.flush()
    return found


_W = {}


def _init(states, classified, seed):
    exe = (GAME / "DARKLAND.EXE").read_bytes()
    rt = Rtlink(exe)
    funcs = {f.name: f for f in cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)}
    _W["s"] = Search(Path(states), json.load(open(classified)), funcs)
    _W["seed"] = seed


def _work(args):
    card, seeds, out, evals = args
    rng = random.Random(_W["seed"] * 1000 + card)
    with open(os.path.join(out, "search-%d.log" % os.getpid()), "a") as log:
        try:
            found = search_card(card, seeds, _W["s"], out, evals, rng, log)
        except Exception as e:      # noqa: BLE001
            import traceback
            log.write("card %X failed %s\n%s\n" % (card, str(e)[:200], traceback.format_exc()[-1500:]))
            found = []
    rows = []
    for f in found:
        fid, kept, path, draws, opt = f["spec"]
        d = os.path.join(out, "saves", fid)
        os.makedirs(d, exist_ok=True)
        if not os.path.exists(os.path.join(d, "FUZZ.SAV")):
            open(os.path.join(d, "FUZZ.SAV"), "wb").write(f["sav"])
        hand = None
        if f["hand"]:
            h = f["hand"]
            hand = "M%dL%d%s%s" % (h["menu"], h["line"], "H" if h.get("hints") else "", "S%d" % h["slot"] if h.get("slot") is not None else "")
        rows.append({"card": card, "key": run_key("S" + fid, opt, kept, path, draws), "option": opt, "probe": True, "hand": hand,
                     "items": f["items"], "seed": f["seed"], "search_key": f["key"]})
    return card, rows


def main(argv):
    from multiprocessing import Pool
    classified, seedsf, states, out = argv[:4]
    evals = int(argv[argv.index("--evals") + 1]) if "--evals" in argv else 80
    workers = int(argv[argv.index("--workers") + 1]) if "--workers" in argv else 4
    seed = int(argv[argv.index("--seed") + 1]) if "--seed" in argv else 1
    seeds = json.load(open(seedsf))
    cards = list(seeds)
    if "--cards" in argv:
        want = argv[argv.index("--cards") + 1].split(",")
        cards = [c for c in cards if c in want]
    os.makedirs(os.path.join(out, "saves"), exist_ok=True)
    t0 = time.time()
    work = [(int(c, 16), seeds[c], out, evals) for c in cards]
    if workers == 1:
        # in this process (a multiprocessing child's first boot ran the loader into unloaded code; the driver starts one
        # process per card subset instead)
        _init(states, classified, seed)
        results = map(_work, work)
        kept_name = "kept-%d.jsonl" % os.getpid()
    else:
        results = None
        kept_name = "kept.jsonl"
    with (Pool(workers, initializer=_init, initargs=(states, classified, seed), maxtasksperchild=6) if workers > 1 else open(os.devnull)) as p, \
            open(os.path.join(out, kept_name), "a") as kf:
        for card, rows in (results if results is not None else p.imap_unordered(_work, work)):
            for r in rows:
                kf.write(json.dumps(r) + "\n")
            kf.flush()
            print("card %X hits %d (%.0fs)" % (card, len(rows), time.time() - t0), flush=True)
    print("wall", round(time.time() - t0), "s")


if __name__ == "__main__":
    main(sys.argv[1:])
