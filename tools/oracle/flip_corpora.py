"""Corpus adapters of flip_units.py: for a corpus, its base cases as run_unit specs, the spec rebuilt from a case's inputs,
and the mapping of a mutation of the spec's state back to the inputs (only to the fields the C# replay test models;
anything else is rejected, since the C# side could not see it).

An adapter has:
  name                      corpus name (branches-work folder, Corpus/<name>.json)
  bases() -> [Base]         the corpus's cases (function, spec, hooks, inputs, case) in generation order
  rebuild(base, inputs) -> (spec, hooks)    the spec of a case with other inputs
  remap(base, diffs) -> (inputs, reason)    inputs after the mutation (None and the reason when the mutation is not
                                            expressible in the corpus's inputs)
  run_case(exe, base, inputs) -> case       the corpus case, as the builder stores it
"""
from __future__ import annotations

import copy
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))


class Base:
    def __init__(self, label, function, spec, hooks, inputs, meta=None):
        self.label, self.function, self.spec, self.hooks, self.inputs, self.meta = label, function, spec, hooks, inputs, meta or {}


def patch_at(spec, idx):
    return spec["memory"][idx]


class Saints:
    name = "saints"
    corpus = "saints"

    def __init__(self, exe):
        import build_saints_corpus as b
        import build_rules_corpus as rules
        self.b, self.rules, self.exe = b, rules, exe
        self.table = b.saint_table(exe)

    # member record bytes the C# test models: status, sex, charm strength, armour qualities, charms, attributes, maxima, skills
    MODELED = {0, 3, 0x10, 0x3B, 0x3C, 0x41} | set(range(0x49, 0x50)) | set(range(0x50, 0x57)) | set(range(0x57, 0x6A))

    def bases(self):
        b = self.b
        out = []
        for n, (function, off, args, memory, inputs) in enumerate(b.cases(random.Random(20261017), self.table)):
            if function == "values":
                continue
            spec, hooks = self.spec_of(function, off, args, memory)
            out.append(Base("saints-%d-%s-%d" % (n, function, inputs["saint"]), function, spec, hooks, inputs,
                            {"off": off, "args": args, "memory": memory}))
        return out

    def spec_of(self, function, off, args, memory, wild=False):
        b = self.b
        spec = {"unit": "saint_%04x" % off, "entry": {"segment": "%04X" % b.SAINT_SEG, "offset": "%04X" % off},
                "sections": b.SECTIONS, "ds": b.DS, "memory": memory, "args": args, "follow": True,
                "stub_targets": {"0E76:2C4E": "event_new", "0E76:2930": "event_here"},
                "arg_words": {"event_new": 11, "event_here": 3}}
        if wild is not False:
            spec["stub_targets"]["09C0:1FA9"] = "nearest_city"
            spec["results"] = {"nearest_city": [{"ax": wild, "dx": 0}] * 12}
        return spec, None

    # -- the memory of a case from its inputs (checked against the generator's in selftest)
    def memory_of(self, function, inputs):
        b, rules = self.b, self.rules
        ds = rules.ds
        memory = [ds(b.PARTY + 0x80 * i, hx) for i, hx in enumerate(inputs["members"])]
        if function == "chance":
            return memory + [ds(b.PARTY_SIZE, rules.w(inputs["size"]))]
        for i, hx in inputs["locations"].items():
            i = int(i)
            memory.append({"segment": "%04X" % rules.LOC_TABLE_SEG, "offset": "%04X" % (rules.LOC_TABLE_OFF + 4 * i),
                           "hex": struct.pack("<HH", i * rules.LOC_SIZE, rules.LOC_SEG).hex()})
            memory.append({"segment": "%04X" % rules.LOC_SEG, "offset": "%04X" % (i * rules.LOC_SIZE), "hex": hx})
        memory += [ds(b.LEADER, "%02X" % inputs["leader"]), ds(b.PARTY_SIZE, rules.w(inputs["size"])), ds(b.REPORT, rules.w(0)),
                   ds(b.SAINT_MSGS, "00"), ds(b.CUR_LOC, rules.w(inputs["curLoc"])),
                   ds(b.HOLDRAND, struct.pack("<I", inputs["state"]).hex()),
                   ds(b.PURSE, struct.pack("<hhh", *inputs["purse"]).hex()),
                   ds(b.FUGGER, struct.pack("<hh", *inputs["bankers"]).hex()),
                   {"segment": "%04X" % b.SAINTS_KNOWN_SEG, "offset": "%04X" % b.SAINTS_KNOWN_OFF, "hex": inputs["known"]}]
        if "x" in inputs:   # the party in the open country: its map position (nearest_city R030+0x271A reads it)
            memory += [ds(rules.PARTY_X, rules.w(inputs["x"])), ds(rules.PARTY_Y, rules.w(inputs["y"]))]
        return memory

    def args_of(self, function, inputs):
        h = self.b.h16
        if function == "chance":
            return [h(inputs["who"]), h(inputs["x"]), h(7)]
        return [h(inputs["who"]), h(0), h(8)]

    def selftest(self, bases):
        for bs in bases:
            m = self.memory_of(bs.function, bs.inputs)
            assert m == bs.meta["memory"], bs.label
            assert self.args_of(bs.function, bs.inputs) == bs.meta["args"], bs.label

    def rebuild(self, base, inputs):
        memory = self.memory_of(base.function, inputs)
        return self.spec_of(base.function, base.meta["off"], self.args_of(base.function, inputs), memory, inputs["near"] if "x" in inputs else False)

    def extra(self, base, uni=None):
        """Domain-aware variants: the party in the open country (cur_loc -1) at (x, y); nearest_city (R030, an overlay that
        cannot be loaded with R039) is stubbed to answer `near`, a city placed next to the party."""
        if base.function != "help":
            return
        if base.inputs["saint"] not in self.b.DIRECT and base.inputs["who"] != -2:
            inp = copy.deepcopy(base.inputs)    # who -2: the whole party (as the rules corpus's add_attr / practice_skill)
            inp["who"] = -2
            yield inp
        for v in range(3):
            r = random.Random(1000 * base.inputs["saint"] + v)
            inp = copy.deepcopy(base.inputs)
            inp["curLoc"] = -1
            inp["x"], inp["y"] = r.randint(150, 330), r.randint(300, 930)
            inp["near"] = r.randint(0, 91)
            locs = dict(inp["locations"])
            rec = bytearray.fromhex(locs.get(str(inp["near"]), bytearray(0x3A).hex()))
            if str(inp["near"]) not in locs:
                struct.pack_into("<h", rec, 0x12, r.randint(-99, 99))
                rec[0x14] = r.choice([0, 1, 2, 4, 0x20])
            struct.pack_into("<h", rec, 0, 0)
            struct.pack_into("<hh", rec, 4, inp["x"] + 1, inp["y"])
            locs[str(inp["near"])] = rec.hex()
            inp["locations"] = locs
            yield inp
        # the places the saint's function compares the current location with
        fname = "R039+0x%04X" % ((0x165C - 0x1462) * 16 + base.meta["off"])
        r = random.Random(77 * base.inputs["saint"])
        for cur in sorted(c for c in (uni.consts.get(fname, ()) if uni else ()) if 0 <= c < 414 and c != base.inputs["curLoc"]):
            inp = copy.deepcopy(base.inputs)
            inp["curLoc"] = cur
            if str(cur) not in inp["locations"]:
                rec = bytearray(0x3A)
                struct.pack_into("<h", rec, 0x12, r.randint(-99, 99))
                rec[0x14] = r.choice([0, 1, 2, 4, 0x20, 0x80])
                inp["locations"][str(cur)] = rec.hex()
            yield inp

    # -- mutation -> inputs
    def remap(self, base, spec, diffs):
        """(inputs, None) or (None, reason): the base's inputs with the mutated state."""
        inp = copy.deepcopy(base.inputs)
        mem = base.meta["memory"]
        members = len(inp["members"])
        for d in diffs:
            if d[0] == "arg":
                j, v = d[1], d[2]
                v = v - 0x10000 if v & 0x8000 else v
                if j == 0:
                    lo = -2 if base.function == "help" and inp["saint"] not in self.b.DIRECT else 0
                    if not lo <= v < inp["size"]:
                        return None, "who-out-of-domain"
                    inp["who"] = v
                elif j == 1 and base.function == "chance":
                    inp["x"] = v
                else:
                    return None, "arg-fixed"
                continue
            if d[1] == "x":
                return None, "free-ds-byte"
            _, kind, a, k, value = d
            patch = mem[a]
            if a < members:
                if k not in self.MODELED:
                    return None, "unmodeled-member-byte"
                rec = bytearray.fromhex(inp["members"][a])
                rec[k] = value
                inp["members"][a] = rec.hex()
                continue
            if base.function == "chance":
                return None, "size"
            nloc = len(inp["locations"])
            at = a - members
            if at < 2 * nloc:
                if at % 2 == 0:
                    return None, "location-table"
                key = list(inp["locations"])[at // 2]
                if k not in (0x12, 0x13):
                    return None, "unmodeled-location-byte"
                rec = bytearray.fromhex(inp["locations"][key])
                rec[k] = value
                inp["locations"][key] = rec.hex()
                continue
            f = at - 2 * nloc
            if f == 0:
                if k != 0 or not value < inp["size"]:
                    return None, "leader-out-of-domain"
                inp["leader"] = value
            elif f == 4:
                raw = bytearray(struct.pack("<h", inp["curLoc"]))
                raw[k] = value
                cur = struct.unpack("<h", raw)[0]
                if str(cur) not in inp["locations"]:
                    return None, "curloc-without-record"
                inp["curLoc"] = cur
            elif f == 5:
                raw = bytearray(struct.pack("<I", inp["state"]))
                raw[k] = value
                inp["state"] = struct.unpack("<I", raw)[0]
            elif f == 6:
                raw = bytearray(struct.pack("<hhh", *inp["purse"]))
                raw[k] = value
                inp["purse"] = list(struct.unpack("<hhh", raw))
            elif f == 7:
                raw = bytearray(struct.pack("<hh", *inp["bankers"]))
                raw[k] = value
                inp["bankers"] = list(struct.unpack("<hh", raw))
            elif f == 8:
                raw = bytearray.fromhex(inp["known"])
                raw[k] = value
                inp["known"] = raw.hex()
            else:
                return None, "unmodeled-field-%d" % f
        return inp, None


def _saints_run_case(self, exe, base, inputs):
    """(corpus case, spec) of the base's function with other inputs."""
    spec, hooks = self.rebuild(base, inputs)
    extra = {"stub_targets": spec["stub_targets"], "results": spec["results"]} if "results" in spec else None
    case, images = self.b.run_case(exe, base.function, base.meta["off"], spec["args"], spec["memory"], inputs, extra)
    return case, spec, hooks, images


Saints.run_case = _saints_run_case


class InputAdapter:
    """Adapters that mutate the inputs directly (flat, semantic inputs): mode "inputs"."""
    mode = "inputs"

    def leaf_ok(self, base, path):
        return True

    def valid(self, base, inputs):
        return True

    def selftest(self, bases):
        pass

    def domain(self, base, path):
        key = (base.function, path)
        if key not in self._dom:
            vals = set()
            for b in self._all:
                if b.function != base.function:
                    continue
                node = b.inputs
                try:
                    for p in path:
                        node = node[p]
                except (KeyError, IndexError, TypeError):
                    continue
                if isinstance(node, int):
                    vals.add(node)
            self._dom[key] = vals
        return self._dom[key]


class Trading(InputAdapter):
    name = "trading"
    corpus = "trading"

    def __init__(self, exe):
        import build_trading_corpus as b
        self.b, self.exe = b, exe
        self.table_seg = b.item_table_segment(exe)
        self.sites = b.stub_sites(exe)
        self._dom, self._all = {}, []

    def bases(self):
        out = []
        for n, (kind, function, memory, args, inputs) in enumerate(self.b.all_cases(self.table_seg)):
            f = "stock" if kind == "stock" else function
            base = Base("trading-%d-%s" % (n, f), f, None, None, inputs, {"kind": kind})
            base.spec, base.hooks = self.rebuild(base, inputs)
            out.append(base)
        self._all = out
        return out

    def rebuild(self, base, inputs):
        b = self.b
        if base.function == "stock":
            return b.stock_spec(b.stock_memory(self.table_seg, inputs), self.sites), None
        memory, args, _ = b.case(random.Random(1), self.table_seg, base.function, copy.deepcopy(inputs))
        return b.plain_spec(base.function, memory, args), None

    def run_case(self, exe, base, inputs):
        spec, hooks = self.rebuild(base, inputs)
        res = self._run(exe, spec)
        if base.function == "stock":
            return self.b.stock_record(inputs, res), spec, hooks, None
        return self.b.plain_record(base.function, inputs, res), spec, hooks, res["loadedImages"]

    def _run(self, exe, spec):
        from run_unit import run
        return run(exe, spec)


class Chargen(InputAdapter):
    name = "chargen"
    corpus = "chargen"

    def __init__(self, exe):
        import build_chargen_corpus as b
        self.b, self.exe = b, exe
        self._dom, self._all = {}, []

    def bases(self):
        b = self.b
        cases, defs = b.cases(random.Random(20261017))
        self.defs = defs
        self.def_memory = []
        for k, (t, wt, q) in enumerate(defs):
            rec = bytearray(0x30)
            rec[0x1E], rec[0x25], rec[0x26] = t, wt, q
            self.def_memory.append(b.far(b.ITEM_DEFS + 4 * k, struct.pack("<HH", k * 0x30, b.ITEM_SEG).hex()))
            self.def_memory.append(b.far(k * 0x30, rec.hex(), b.ITEM_SEG))
        out = []
        for n, (name, memory, args, inputs) in enumerate(cases):
            inputs.pop("extra", None)
            base = Base("chargen-%d-%s" % (n, name), name, None, None, inputs, {"args": args, "memory": memory})
            base.spec, base.hooks = self.rebuild(base, inputs)
            if base.spec["memory"] != memory:
                raise SystemExit("chargen rebuild differs at case %d %s" % (n, name))
            if name == "career_events":   # the open outcomes of the corpus's functions are all in career_events
                out.append(base)
        self._all = out
        return out

    def memory_char(self, i):
        b, w, ds = self.b, self.b.w, self.b.ds
        return [ds(b.AGE, w(i["age"])), ds(b.ATTRS, bytes(a & 0xFF for a in i["attributes"]).hex()),
                ds(b.SKILLS, bytes(s & 0xFF for s in i["skills"]).hex()), ds(b.TAKEN, bytes(i["taken"]).hex()),
                ds(b.LAST, w(i["last"])), ds(b.PREVIOUS, w(i["previous"])), ds(b.THIRD, w(i["third"])),
                ds(b.BACKGROUND, w(i["background"])), ds(b.SEX, "%02X" % i["female"]), ds(b.LIST, "AA" * 40),
                ds(b.COUNT, w(0x5555)), ds(b.CURSOR, w(7))]

    def memory_know(self, i):
        b = self.b
        return [b.far(b.SAINTS, i["saints"]), b.far(b.FORMULAS, i["formulas"]),
                b.ds(b.HOLDRAND, struct.pack("<I", i["state"]).hex()), b.ds(b.ITEM_COUNT, "%02X" % i["kitCount"])]

    def rebuild(self, base, i):
        b = self.b
        if base.function == "occupation_list":
            memory, args = self.memory_char(i), []
        elif base.function == "serve_term":
            memory = (self.memory_char(i) + self.memory_know(i) + self.def_memory +
                      [b.ds(b.POINTS, b.w(i["points"])), b.ds(b.TERM_POINTS, b.w(0x1234))])
            args = ["%04X" % i["occupation"]]
        else:
            memory, args = self.memory_know(i) + self.def_memory, ["%04X" % i["occupation"]]
        spec = {"unit": base.function, "entry": {"segment": "18C8", "offset": b.FN[base.function]}, "sections": b.SECTIONS,
                "ds": b.DS, "memory": memory, "args": args, "follow": True, "track_all": True}
        return spec, None

    def run_case(self, exe, base, inputs):
        from run_unit import run
        spec, hooks = self.rebuild(base, inputs)
        res = run(exe, spec)
        case = {"function": base.function, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                "changed": {c["linear"]: c["after"] for c in res["memChanged"]}}
        return case, spec, hooks, None


class CorpusInputs(InputAdapter):
    """Bases = the cases of the committed corpus (their stored inputs); rebuild(inputs) gives the spec."""

    def stored(self):
        import json
        root = HERE.parents[1] / "tests/Darklands.Oracle.Tests/Corpus" / (self.corpus + ".json")
        return json.loads(root.read_text())["cases"]

    def bases(self):
        out = []
        for n, c in enumerate(self.stored()):
            if c["function"] not in self.functions:
                continue
            base = Base("%s-%d-%s" % (self.name, n, c["function"]), c["function"], None, None, copy.deepcopy(c["inputs"]), {"stored": c})
            base.spec, base.hooks = self.rebuild(base, base.inputs)
            out.append(base)
        self._all = out
        return out

    def run_case(self, exe, base, inputs):
        from run_unit import run
        spec, hooks = self.rebuild(base, inputs)
        res = run(exe, spec, hooks=hooks)
        return self.record(base, inputs, res), spec, hooks, res["loadedImages"]


class Battle(CorpusInputs):
    name = "battle"
    corpus = "battle"
    functions = {"attack_rate", "to_hit_roll", "hit_damage", "damage_queue", "damage_apply", "weapon_ready", "skill_try_improve"}

    def __init__(self, exe):
        import build_battle_corpus as b
        self.b, self.exe = b, exe
        self._dom, self._all = {}, []

    def rebuild(self, base, i):
        b = self.b
        ds = b.ds
        recs = [bytes.fromhex(x) for x in i["combatants"]]
        memory = [ds(b.COMBATANTS + 0x80 * k, rec) for k, rec in enumerate(recs)]
        memory += [{"segment": "%04X" % b.REACH_SEG, "offset": "%04X" % b.REACH_OFF, "hex": i["reach"]},
                   ds(b.ATTACK_RATE, bytes.fromhex(i["rates"])), ds(b.QUEUED, bytes.fromhex(i["queued"])),
                   ds(b.SHOWN, bytes.fromhex(i["shown"])), ds(b.REAL_COMBAT, bytes([i["real"]])),
                   ds(b.DRAGON_FIGHT, bytes([i.get("dragon", 0)])), ds(b.CLOUDS, b"\x00"),
                   ds(b.HOLDRAND, struct.pack("<I", i["state"])), ds(b.COMBATANTS + 13 * 0x80 + 0x14, b"\x00")]
        name = base.function
        results = {}
        if name == "to_hit_roll":
            results = {"cloud_blocks_line": [{"ax": i["cloud"]}]}
        if name == "skill_try_improve":
            memory.append(ds(b.DIFFICULTY, bytes([i["difficulty"]])))
        if name == "weapon_ready":
            memory.append(ds(b.ANIM_FILE_TABLE, b"".join(struct.pack("<H", b.SCRATCH + 4 * k) for k in range(7))))
            if i.get("dragonCode"):
                memory.append({"segment": "%04X" % b.CODES_SEG, "offset": "%04X" % b.DRAGON_CODE, "hex": i["dragonCode"].encode().hex()})
        args = [b.h16(a) for a in i["args"]]
        seg, sections = ("1EF8", [136, 14, 21]) if name == "skill_try_improve" else (b.SEG, [136, 14])
        spec = {"unit": name, "entry": {"segment": seg, "offset": b.FN[name]}, "sections": sections, "ds": b.DS,
                "memory": memory, "args": args, "follow": True, "callsites": b.CALLSITES,
                "stub": list(b.CALLSITES.values()), "results": results}
        return spec, None

    def record(self, base, inputs, res):
        b = self.b
        return {"function": base.function, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                "changed": {"%05X" % (b.DS * 16 + int(c["offset"], 16)): c["after"] for c in res["dsChanged"]}}

    def selftest(self, bases):
        from run_unit import run
        for base in bases[::max(1, len(bases) // 40)]:
            spec, hooks = self.rebuild(base, base.inputs)
            res = run(self.exe, spec, hooks=hooks)
            rec = self.record(base, base.inputs, res)
            st = base.meta["stored"]
            assert rec["ax"] == st["ax"] and rec["changed"] == st["changed"], base.label


class Tactics(CorpusInputs):
    name = "tactics"
    corpus = "tactics"
    functions = {"cbt_collisions", "line_blocked"}

    def __init__(self, exe):
        import build_tactics_corpus as b
        self.b, self.exe = b, exe
        self._dom, self._all = {}, []

    def scene_memory(self, i):
        b = self.b
        cells = {(x, y): v for x, y, v in i["cells"]}
        shapes = [(s[0], s[1], s[2], s[3]) for s in i["shapes"]]
        recs = [bytearray.fromhex(x) for x in i["combatants"]]
        return b.memory_of(cells, shapes, recs, bytes.fromhex(i["floors"]), bytes.fromhex(i["halfW"]),
                           bytes.fromhex(i["halfH"]), bytes.fromhex(i["moving"]), i["sight"])

    def rebuild(self, base, i):
        b = self.b
        mem = self.scene_memory(i)
        if base.function == "cbt_collisions":
            target = i["target"] if i["target"] != -1 else 0xFF
            mem += [b.ds(b.STRICT, bytes([i["strict"]])), b.ds(b.SEARCHING, bytes([i["searching"]])),
                    b.ds(b.SEARCH_TARGET, bytes([target])), b.ds(b.NO_SCENERY, bytes([i["noScenery"]])),
                    b.ds(b.NO_PEOPLE, bytes([i["noPeople"]])), b.ds(b.PARTS, bytes([0xA5]) * 32),
                    b.ds(b.LAST, bytes([0xA5, 0xA5]))]
            spec = {"unit": "cbt_collisions", "entry": {"segment": "1462", "offset": "000A"}, "sections": [136, 14],
                    "ds": b.DS, "memory": mem, "args": [b.h16(b.COMBATANTS + 0x80 * i["who"])], "follow": True}
        else:
            x0, y0 = i["from"]
            x1, y1 = i["to"]
            mem += [b.ds(b.LOS_X0, struct.pack("<h", x0)), b.ds(b.LOS_Y0, struct.pack("<h", y0)),
                    b.ds(b.LOS_X1, struct.pack("<h", x1)), b.ds(b.LOS_Y1, struct.pack("<h", y1))]
            spec = {"unit": "line_blocked", "entry": {"segment": "1D69", "offset": "000C"}, "sections": [136, 14],
                    "ds": b.DS, "memory": mem, "args": [b.h16(i["a"]), b.h16(i["b"]), b.h16(i["mode"])], "follow": True}
        return spec, None

    def record(self, base, inputs, res):
        b = self.b
        if base.function == "line_blocked":
            return {"function": "line_blocked", "inputs": inputs, "ax": res["final"]["ax"]}
        changed = {int(c["offset"], 16): int(c["after"], 16) for c in res["dsChanged"]}

        def word(off):
            return changed.get(off, 0xA5) | changed.get(off + 1, 0xA5) << 8

        return {"function": "cbt_collisions", "inputs": inputs, "ax": res["final"]["ax"],
                "parts": [word(b.PARTS + 2 * p) for p in range(16)], "last": word(b.LAST)}

    def selftest(self, bases):
        from run_unit import run
        for base in bases[::max(1, len(bases) // 40)]:
            spec, hooks = self.rebuild(base, base.inputs)
            res = run(self.exe, spec, hooks=hooks)
            rec = self.record(base, base.inputs, res)
            st = base.meta["stored"]
            assert rec["ax"] == st["ax"] and rec.get("parts") == st.get("parts") and rec.get("last") == st.get("last"), base.label


class Core(CorpusInputs):
    name = "core"
    corpus = "core"
    functions = {"money_cmp"}

    def __init__(self, exe):
        import build_core_corpus as b
        self.b, self.exe = b, exe
        self._dom, self._all = {}, []

    def rebuild(self, base, i):
        b = self.b
        memory = [{"offset": "%04X" % b.A, "hex": b.money_hex(i["a"])}, {"offset": "%04X" % b.B, "hex": b.money_hex(i["b"])}]
        spec = {"unit": base.function, "entry": {"segment": b.S029, "offset": b.FN[base.function][1]}, "sections": [29, 136],
                "ds": b.DS, "memory": memory, "args": [b.h16(b.A), b.h16(b.B)], "follow": True, "track_all": True}
        return spec, None

    def record(self, base, inputs, res):
        return {"function": base.function, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                "changed": {c["linear"]: c["after"] for c in res["memChanged"]}}

    def selftest(self, bases):
        from run_unit import run
        for base in bases[::5]:
            spec, hooks = self.rebuild(base, base.inputs)
            res = run(self.exe, spec)
            rec = self.record(base, base.inputs, res)
            st = base.meta["stored"]
            assert rec["ax"] == st["ax"] and rec["changed"] == st["changed"], base.label


def _chargen_extra(self, base, uni=None):
    """career_events with other random states: a kit entry is merged only when the same item code is added twice
    in one career, which few states do."""
    if base.function != "career_events":
        return
    for v in range(14):
        inp = copy.deepcopy(base.inputs)
        inp["state"] = random.Random(base.inputs["state"] * 31 + v).getrandbits(32)
        yield inp


Chargen.extra = _chargen_extra


def _saints_extra_fields(self, base, uni=None):
    """Field extremes of a help case, alone and with the whole party / the open country: the known-saints bits, the purse,
    the bankers' standings, the place's standing, the members' skills and attributes, the party size and leader."""
    if base.function != "help":
        return
    inp0 = base.inputs
    size = inp0["size"]
    cur = str(inp0["curLoc"])
    muts = []
    muts.append(("known0", lambda i: i.update(known="00" * 100)))
    muts.append(("knownF", lambda i: i.update(known="ff" * 100)))
    muts.append(("purse0", lambda i: i.update(purse=[0, 0, 0])))
    muts.append(("purseH", lambda i: i.update(purse=[9999, 19, 11])))
    muts.append(("bankL", lambda i: i.update(bankers=[-100, -100])))
    muts.append(("bankH", lambda i: i.update(bankers=[100, 100])))

    def rep(v):
        def f(i):
            if cur in i["locations"]:
                rec = bytearray.fromhex(i["locations"][cur])
                struct.pack_into("<h", rec, 0x12, v)
                i["locations"][cur] = rec.hex()
        return f

    muts.append(("repL", rep(-99)))
    muts.append(("repH", rep(99)))

    def members(f):
        def g(i):
            out = []
            for hx in i["members"]:
                rec = bytearray.fromhex(hx)
                f(rec)
                out.append(rec.hex())
            i["members"] = out
        return g

    def skills(v):
        def f(rec):
            for k in range(19):
                rec[0x57 + k] = v
        return f

    def attrs(v):
        def f(rec):
            for k in range(7):
                rec[0x50 + k] = max(v, 1)
                rec[0x49 + k] = max(1, min(v, rec[0x50 + k]))
        return f

    muts.append(("skills0", members(skills(0))))
    muts.append(("skills99", members(skills(99))))
    muts.append(("attrs99", members(attrs(99))))
    muts.append(("attrs1", members(attrs(1))))
    muts.append(("female", members(lambda rec: rec.__setitem__(3, 1))))
    muts.append(("male", members(lambda rec: rec.__setitem__(3, 0))))
    muts.append(("leader", lambda i: i.update(leader=(i["leader"] + 1) % i["size"])))
    for name, f in muts:
        for extra in (None, "all", "wild"):
            inp = copy.deepcopy(inp0)
            f(inp)
            if extra in ("all", "wild"):
                if inp["saint"] in self.b.DIRECT and extra == "all":
                    continue
                if extra == "all":
                    inp["who"] = -2
                else:
                    r = random.Random(zlib_crc(name) + inp["saint"])
                    inp["curLoc"] = -1
                    inp["x"], inp["y"] = r.randint(150, 330), r.randint(300, 930)
                    inp["near"] = r.randint(0, 91)
                    rec = bytearray.fromhex(inp["locations"].get(str(inp["near"]), bytearray(0x3A).hex()))
                    struct.pack_into("<h", rec, 0, 0)
                    struct.pack_into("<hh", rec, 4, inp["x"] + 1, inp["y"])
                    inp["locations"][str(inp["near"])] = rec.hex()
            if inp != inp0:
                yield inp


def zlib_crc(s):
    import zlib
    return zlib.crc32(s.encode())


_saints_extra_base = Saints.extra


def _saints_extra_all(self, base, uni=None):
    yield from _saints_extra_base(self, base, uni)
    yield from _saints_extra_fields(self, base, uni)


Saints.extra = _saints_extra_all


class Path_(CorpusInputs):
    """path_find on the 60 generated scenes (the sweep's runs cost seconds: only a few bases are worth it)."""
    name = "path"
    corpus = "path"
    functions = {"path_find"}

    def __init__(self, exe):
        import build_path_corpus as b
        self.b, self.exe = b, exe
        self._dom, self._all = {}, []

    def rebuild(self, base, i):
        b = self.b
        cells = {(x, y): v for x, y, v in i["cells"]}
        shapes = [(s[0], s[1], s[2], s[3]) for s in i["shapes"]]
        recs = [bytearray.fromhex(x) for x in i["combatants"]]
        mem = [{"segment": "%04X" % b.FLOOR_SEG, "offset": "%04X" % ((x * 40 + y) * 4), "hex": struct.pack("<I", v).hex()}
               for (x, y), v in cells.items()]
        mem.append({"segment": "%04X" % b.FLOOR_SEG, "offset": "%04X" % b.NDOORS, "hex": "0000"})
        mem.append({"segment": "%04X" % b.FLOOR_SEG, "offset": "%04X" % b.PATH, "hex": "ee" * 250})
        ds = b.ds
        mem += [ds(b.FURN_H, bytes(s[1] for s in shapes)), ds(b.FURN_W, bytes(s[0] for s in shapes)),
                ds(b.FURN_X, bytes(v for s in shapes for v in s[2])), ds(b.FURN_Y, bytes(v for s in shapes for v in s[3]))]
        mem += [ds(b.COMBATANTS + 0x80 * k, bytes(rec)) for k, rec in enumerate(recs)]
        mem += [ds(b.CBT_FLOOR, bytes.fromhex(i["floors"])), ds(b.HALF_W, bytes.fromhex(i["halfW"])),
                ds(b.HALF_H, bytes.fromhex(i["halfH"])), ds(b.MOVING, bytes.fromhex(i["moving"]))]
        sx = bytes(v & 0xFF for v in i["stepX"])
        sy = bytes(v & 0xFF for v in i["stepY"])
        mem += [ds(b.STEP_X + 10 * s, sx) for s in range(11)] + [ds(b.STEP_Y + 10 * s, sy) for s in range(11)]
        mem += [ds(off, struct.pack("<H", v)) for off, v in b.WORK.items()]
        mem += [ds(0xE0A2, b"\x00"), ds(0xE13C, b"\x00"), ds(0xC600, b"\x00"), ds(0xD697, b"\x00")]
        spec = {"unit": "path_find", "entry": {"segment": "12F7", "offset": "000A"}, "sections": [136, 14], "ds": b.DS,
                "memory": mem, "args": [b.h16(i["who"])], "follow": True, "track_all": True, "callsites": b.CALLSITES,
                "stub": list(set(b.CALLSITES.values())), "max_instructions": 30000000}
        return spec, None
