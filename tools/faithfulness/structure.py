#!/usr/bin/env python3
"""Structural comparison of the C# with the original functions it cites (static, no execution).

Every original function the C# implements is compared with the C# units that implement it (members,
local functions and card handlers with an `// orig:` comment citing it; tools/faithfulness/structure-scan).
The facts of the function come from the unchanged EXE bytes (structure_exe.py), those of the C# from
Roslyn, with the port's untraced helpers followed and its traced members standing for what they cite:

  draws     one side draws random numbers (rand or one of its wrappers: random_n, rand_mod, rand_1_n), the other
            draws neither itself nor in what it calls (two calls deep in the C#'s callees, three in the original)
  args      a call both sides make passes a constant the other side never uses (rand_mod(100) / Below(10))
  stores    the C# assigns a constant that appears nowhere in the original's code (a next card, a flag)
  missing   the original calls a game-logic function the C# neither calls (even two calls deep) nor names
  extra     the C# calls a traced function the original does not call, even three calls deep (a property
            read is data access, not a call: the original reads memory there)
  order     two calls both sides make are made one way round in the C# and only the other way in the
            original (address order there, source order here; C evaluates arguments right to left)
  consts    a `cmp`/`test` immediate of the original that appears nowhere in the C# (a threshold, a flag)
  decisions the C# has fewer than half the original's conditional branches (switch cases counted)

All the C# that cites a function is compared with it together (parts split over several members, wrappers);
functions one member implements together (a card option and its hint) are compared as one. Calls and stores
under a `// DIVERGENCE:` or `// DORMANT:` comment are already registered and not held against the C#.

A finding is a lead, not a verdict: a call through a function pointer, a call the port moved to the caller,
a table the port reads instead of a comparison all show up. A reviewed lead that is in order is listed in
docs/faithfulness/structure-reviewed.csv (coordinate, check, detail, reason) and drops out of the open
counts while its detail stays the same.

  python -I -B tools/faithfulness/structure.py [--exe DARKLAND.EXE] [--skip-scan]

The EXE defaults to $DARKLANDS_EXE, else the `game` folder of local/paths.json. Intermediate files go to
local/faithfulness/ (git-ignored); the outputs are docs/faithfulness/STRUCTURE.md and structure.csv.
"""
from __future__ import annotations

import argparse
import collections
import csv
import json
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import local_paths  # noqa: E402
from coverage import AREAS  # noqa: E402

LOCAL = REPO / "local" / "faithfulness"
OUT = REPO / "docs" / "faithfulness"
INDEX = REPO / "docs" / "source-map" / "original-functions.csv"
COVERAGE = OUT / "coverage.csv"
REVIEWED = OUT / "structure-reviewed.csv"
RAND = "root 06A1:29B6"   # the C run-time's rand()
RANDOM = {RAND}           # and its wrappers (random_n, rand_mod, rand_1_n), found in the EXE by random_helpers()
CHECKS = ["draws", "args", "stores", "missing", "extra", "order", "consts", "decisions"]
MEANING = {
    "draws": "one side draws random numbers, the other never does",
    "args": "a shared call passes a constant the other side never uses",
    "stores": "the C# assigns a constant that appears nowhere in the original's code",
    "missing": "a game-logic call of the original the C# does not make",
    "extra": "a traced call the original does not make (three calls deep)",
    "order": "two shared calls in the opposite order",
    "consts": "a `cmp`/`test` constant of the original that appears nowhere in the C#",
    "decisions": "fewer than half the original's conditional branches",
}
# The original's own min/max/clamp helpers, which the port writes with System.Math: (methods, what the helper
# does). A call is accepted as the helper when the C# uses one of the methods; for clamp_range that holds only
# while lo <= hi (Math.Clamp throws otherwise, and the original returns hi above hi, else the larger of lo and
# v), which this check does not see.
BCL_EQUIVALENTS = {
    "R029+0x4F1A": ({"Math.Clamp", "Math.Min", "Math.Max"}, "clamp99: v above 99 gives 99, below 0 gives 0"),
    "R029+0x4F38": ({"Math.Clamp", "Math.Min", "Math.Max"}, "clamp100: v above 100 gives 100, below 0 gives 0"),
    "R029+0x4F76": ({"Math.Clamp", "Math.Min", "Math.Max"}, "clamp_range(lo, hi, v): hi above hi, lo below lo (signed)"),
    "R029+0x4F94": ({"Math.Max"}, "imax(a, b), signed"),
    "R029+0x4FA4": ({"Math.Min"}, "imin(a, b), signed"),
}
CARDS = ("R044", "R135")   # the card overlays: one card cannot call another's code (only through R029 / R043)
MERGE_LIMIT = 6   # a unit citing more functions (a summary, or one handler for many identical options) merges none
PRESENTATION = ("src/Darklands.Presentation/", "src/Darklands.Host.", "src/Darklands.Platform/", "src/Darklands.Audio")


def exe_path(arg):
    if arg:
        return arg
    if os.environ.get("DARKLANDS_EXE"):
        return os.environ["DARKLANDS_EXE"]
    try:
        game = json.load(open(REPO / "local" / "paths.json", encoding="utf-8")).get("game")
    except FileNotFoundError:
        game = None
    if not game:
        sys.exit("DARKLAND.EXE: pass --exe, set DARKLANDS_EXE or the key \"game\" in local/paths.json")
    return os.path.join(local_paths.resolve(game), "DARKLAND.EXE")


def run_extractors(exe):
    LOCAL.mkdir(parents=True, exist_ok=True)
    subprocess.run([sys.executable, "-I", "-B", str(HERE / "structure_exe.py"), exe, str(LOCAL / "structure-exe.json")],
                   check=True)
    subprocess.run(["dotnet", "run", "-c", "Release", "--project", str(HERE / "structure-scan"), "--",
                    str(REPO), str(LOCAL / "structure-cs.json")], check=True)


def load_index():
    thunks, kinds = {}, {}
    for r in csv.DictReader(open(INDEX, encoding="utf-8")):
        kinds[r["coordinate"]] = r["kind"]
        if r["kind"] == "thunk":
            thunks[r["coordinate"]] = r["target"].split(" (")[0]
    return thunks, kinds


def load_coverage():
    """coordinate -> (role, canonical coordinate): role "logic", "runtime" (the C run-time library, cited by
    name, or excluded with a reason) or "presentation" (implemented only by the screens, hosts and drivers);
    the canonical coordinate of a byte-identical clone is the function it clones."""
    out = {}
    for r in csv.DictReader(open(COVERAGE, encoding="utf-8")):
        canon = r["via"][6:] if r["via"].startswith("clone:") else r["coordinate"]
        files = [f for f in r["cited_in"].split(";") if f.startswith("src/")]
        if r["status"] != "implemented" or r["via"] == "cite:crt-name":
            role = "runtime"
        elif files and all(f.startswith(PRESENTATION) for f in files):
            role = "presentation"
        else:
            role = "logic"
        out[r["coordinate"]] = (role, canon)
    return out


def load_reviewed():
    out = {}
    if REVIEWED.exists():
        for r in csv.DictReader(open(REVIEWED, encoding="utf-8")):
            out[(r["coordinate"], r["check"], r["detail"])] = r["reason"]
    return out


def precedes(seq):
    """The pairs (x, y) of distinct items such that some x comes before some y."""
    first, last = {}, {}
    for k, x in enumerate(seq):
        first.setdefault(x, k)
        last[x] = k
    return {(x, y) for x in first for y in first if x != y and first[x] < last[y]}


def random_helpers(exe):
    """The original's small wrappers of rand(): functions whose only calls are to rand, with few branches."""
    return {k for k, v in exe.items()
            if v.get("calls") and len(v["calls"]) <= 2 and v["branches"] <= 3 and all(t == RAND for _, t, _ in v["calls"])}


def ds_addresses():
    """The DGROUP addresses the C# names (`DS:27F4`, the screen window): the original pushes them where the
    port passes objects."""
    out = set()
    for path in (REPO / "src").rglob("*.cs"):
        if "obj" not in path.parts and "bin" not in path.parts:
            out.update(int(m, 16) for m in re.findall(r"\bDS:([0-9A-Fa-f]{4})\b", path.read_text(encoding="utf-8")))
    return out


DS_ADDRESSES: set = set()


def value(v):
    """A constant worth comparing: 0 and 1 are flags of every kind; words from 0x8000 to -17 and the DGROUP
    addresses the C# names are taken for data addresses (strings, buffers, windows) the original pushes
    where the port passes objects."""
    return (1 < v < 0x8000 or v >= 0xFFF0) and v not in DS_ADDRESSES


def near(v, pool):
    """v, or the same bound written one off (`x < 6` / `cmp x, 5`), negated or as a complement mask."""
    return any(w in pool for w in (v, (v + 1) & 0xFFFF, (v - 1) & 0xFFFF, (-v) & 0xFFFF, (~v) & 0xFFFF))


def words(vs):
    return " ".join("0x%X" % v for v in vs)


def compare(units, exe, thunks, coverage, kinds):
    def canon(c):
        c = thunks.get(c, c)
        return coverage.get(c, (None, c))[1]

    def role(c):
        return coverage.get(c, ("runtime", c))[0]

    callees = {k: [canon(t) for _, t, _ in v.get("calls", [])] for k, v in exe.items()}

    def deep(cs, levels):
        seen, frontier = set(cs), set(cs)
        for _ in range(levels):
            frontier = {x for c in frontier for x in callees.get(c, [])} - seen
            seen |= frontier
        return seen

    units = [u for u in units if u["kind"] != "type"]
    for u in units:
        cites = [canon(c) for c in u["cites"] if kinds.get(thunks.get(c, c)) in ("function", "thunk")]
        u["fn"] = [c for c in dict.fromkeys(cites) if c in exe]
        # callee, args, documented, C# name, reached through a property (data access)
        u["c"] = [(canon(x[1]), x[4], x[5], x[3], x[6]) for x in u["calls"]]
    # every unit that cites a function is compared as part of it: a wrapper's call of the function is internal
    # to the comparison, and a member that cites the function it calls only adds its other calls as `extra`;
    # functions one unit implements together (a card option and its hint, a function and the helper the port
    # folded into it) are compared as one: union-find over the units' citations
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    size = collections.Counter()
    for u in units:
        fs = u["fn"]
        if len(fs) <= MERGE_LIMIT:
            for f in fs[1:]:
                a, b = find(f), find(fs[0])
                if a != b and size[a] + size[b] + 2 <= 2 * MERGE_LIMIT:   # chains stay small
                    parent[a] = b
                    size[b] += size[a] + 1
    groups = collections.defaultdict(lambda: ([], []))
    for u in units:
        for f in u["fn"]:
            g = groups[find(f)]
            if u not in g[0]:
                g[0].append(u)
            if f not in g[1]:
                g[1].append(f)
    literals_of = collections.defaultdict(set)   # what the C# that cites a function uses and calls
    calls_of = collections.defaultdict(set)
    for u in units:
        for f in u["fn"]:
            literals_of[f].update(u["literals"])
            calls_of[f].update(t for t, _, _, _, _ in u["c"])

    rows = []
    for key in sorted(groups, key=lambda k: sorted(groups[k][1])[0]):
        members, fns = groups[key]
        fns = sorted(fns)
        f = fns[0]
        own = set(fns) | {c for u in members for c in u["fn"]}
        presenting = all(u["file"].startswith(PRESENTATION) for u in members)
        compared_roles = {"logic", "presentation"} if presenting else {"logic"}
        o_calls = [(canon(t), a) for g in fns for _, t, a in exe[g]["calls"]]
        o_calls = [(t, a) for t, a in o_calls if t not in own]
        c_every = [(t, a, d, p) for u in members for t, a, d, _, p in u["c"] if t not in own and kinds.get(t) == "function"]
        c_calls = [(t, a) for t, a, d, _ in c_every if not d]
        # what the C# calls, not what it reads through a property (a lazily loaded table, a computed field)
        c_called = [t for t, _, d, p in c_every if not d and not p]
        o_seq = [t for t, _ in o_calls]
        c_seq = [t for t, _ in c_calls]
        mentions = {canon(c) for u in members for c in u["mentions"]}
        found = {}

        # draws: one side draws itself, and the other draws neither itself nor in what it calls (two deep in
        # what the C# calls; three in the original, since the C# reaches a draw through untraced helpers)
        c_deep = deep({t for t, _, _, _ in c_every}, 2)
        o_draws = sum(1 for t in o_seq if t in RANDOM)
        c_draws = sum(1 for t, _, _, _ in c_every if t in RANDOM)
        # (a traced helper of the port may draw what the original draws in its caller: an argument)
        c_inner = {x for t, _, _, _ in c_every for x in calls_of.get(t, ())}
        if ((o_draws and not c_draws and not RANDOM & (c_deep | c_inner))
                or (c_draws and not o_draws and not RANDOM & deep(own, 3))):
            found["draws"] = "original %d call sites, C# %d" % (o_draws, c_draws)

        bcl = {x[1][4:] for u in members for x in u["calls"] if x[1].startswith("bcl:")}
        missing = [t for t in dict.fromkeys(o_seq)
                   if role(t) in compared_roles and t not in c_deep and t not in mentions and t not in RANDOM
                   and not (t in BCL_EQUIVALENTS and BCL_EQUIVALENTS[t][0] & bcl)]
        if missing:
            found["missing"] = " ".join(missing)

        # a shared helper of the port may cite one card's copy of a pattern (a member pop-up hint): from
        # another card that is the same pattern, not a call (the card overlays cannot call one another)
        own_records = {c[:4] for c in own}
        o_deep = deep(own, 3)
        extra = [t for t in dict.fromkeys(c_called)
                 if role(t) != "runtime" and t not in o_deep and t not in mentions and t not in RANDOM
                 and not (CARDS[0] <= t[:4] <= CARDS[1] and t[:4] not in own_records)]
        if extra:
            found["extra"] = " ".join(extra)

        common = set(o_seq) & set(c_seq)
        # (within each original function and each C# member: a hint and its option are separate code)
        o_pre = set().union(*[precedes([canon(t) for _, t, _ in exe[g]["calls"] if canon(t) in common]) for g in fns])
        c_pre = set().union(*[precedes([t for t, _, d, _, _ in u["c"] if t in common and not d]) for u in members])
        swapped = sorted((x, y) for x, y in c_pre - o_pre if (y, x) in o_pre)
        if swapped:
            found["order"] = "; ".join("C# %s before %s" % p for p in swapped[:6]) + (" ..." if len(swapped) > 6 else "")

        o_args, c_args = collections.defaultdict(set), collections.defaultdict(set)
        for t, a in o_calls:
            o_args[t].update(v for v in a if v is not None and value(v))
        for t, a in c_calls:
            c_args[t].update(v for v in (a or []) if v is not None and value(v))
        c_all = ({v for u in members for v in u["literals"] + u["consts"] + u["stores"]}
                 | {v for vs in c_args.values() for v in vs}
                 | {v for t, _, _, _ in c_every for v in literals_of.get(t, ())})
        o_all = {v for c in own for v in exe[c].get("imms", [])} | {v for vs in o_args.values() for v in vs}

        diffs = []
        for t in sorted(common):
            if not o_args[t] or not c_args[t]:
                continue
            lost = sorted(v for v in o_args[t] - c_args[t] if not near(v, c_all))
            new = sorted(v for v in c_args[t] - o_args[t] if not near(v, o_all))
            if lost or new:
                diffs.append("%s: original %s, C# %s" % (t, words(lost) or "-", words(new) or "-"))
        if diffs:
            found["args"] = "; ".join(diffs)

        new_stores = sorted({v for u in members for v in u["stores"] if value(v) and not near(v, o_all)})
        if new_stores:
            found["stores"] = words(new_stores)

        lost = sorted(v for v in {v for g in fns for v in exe[g].get("cmps", [])} if value(v) and not near(v, c_all))
        if lost:
            found["consts"] = words(lost)

        o_dec = sum(exe[g]["branches"] + sum(max(0, n - 1) for n in exe[g]["switches"]) for g in fns)
        c_dec = sum(u["decisions"] for u in members)
        if o_dec >= 4 and c_dec * 2 < o_dec:
            found["decisions"] = "original %d, C# %d" % (o_dec, c_dec)

        rows.append({"coordinate": f, "functions": fns, "members": members, "found": found})
    return rows, units


def area(coord):
    rec = "root" if coord.startswith("root") else coord[:4]
    for pred, name in AREAS:
        if pred(rec):
            return name
    return "other"


def write(rows, reviewed, units, exe, coverage):
    OUT.mkdir(parents=True, exist_ok=True)
    open_counts, rev_counts = collections.Counter(), collections.Counter()
    lines = []
    for r in rows:
        first = r["members"][0]
        for check in CHECKS:
            if check not in r["found"]:
                continue
            detail = r["found"][check]
            reason = reviewed.get((r["coordinate"], check, detail))
            (rev_counts if reason else open_counts)[check] += 1
            lines.append({"coordinate": r["coordinate"], "with": " ".join(r["functions"][1:]), "check": check,
                          "detail": detail,
                          "csharp": "; ".join(dict.fromkeys(u["member"] for u in r["members"])),
                          "file": "%s:%d" % (first["file"], first["line"]),
                          "status": "reviewed" if reason else "open", "reason": reason or ""})
    lines.sort(key=lambda x: (CHECKS.index(x["check"]), x["status"] != "open", x["coordinate"]))
    with open(OUT / "structure.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, ["coordinate", "with", "check", "detail", "csharp", "file", "status", "reason"],
                           lineterminator="\n")
        w.writeheader()
        w.writerows(lines)

    compared = {c for r in rows for c in r["functions"]}
    implemented = {c for c, (rl, canon) in coverage.items() if rl != "runtime" and c == canon and c in exe}
    clean = sum(1 for r in rows if not r["found"])
    by_area = collections.defaultdict(collections.Counter)
    for x in lines:
        if x["status"] == "open":
            by_area[area(x["coordinate"])][x["check"]] += 1

    md = ["# Structure of the C# against the original", "",
          "Generated by `tools/faithfulness/structure.py` (do not edit by hand). A static check, nothing runs:",
          "every original function the C# implements is compared with the C# that implements it (the members,",
          "local functions and card handlers whose `// orig:` comment cites it): its calls with their constant",
          "arguments, the constants it tests and writes, and its conditional branches, read from the unchanged",
          "DARKLAND.EXE bytes, against the same facts of the C# (Roslyn; the port's untraced helpers followed,",
          "its traced members standing for what they cite). It covers every cited function at once, including",
          "paths no oracle case reaches. A finding is a lead to review, not a verdict: `structure.csv` lists",
          "each one; a reviewed lead that is in order goes to `structure-reviewed.csv` with the reason.", "",
          "## Scope", "",
          "| | count |", "|---|---:|",
          "| C# units with a trace (types left out) | %d |" % len(units),
          "| original functions compared | %d |" % len(compared),
          "| comparisons (functions one C# unit implements together are compared as one) | %d |" % len(rows),
          "| comparisons without a finding | %d |" % clean,
          "| implemented functions not compared (cited only in statements, types or map.csv) | %d |"
          % len(implemented - compared), "",
          "## Findings", "",
          "| check | what it means | open | reviewed |", "|---|---|---:|---:|"]
    for c in CHECKS:
        md.append("| `%s` | %s | %d | %d |" % (c, MEANING[c], open_counts[c], rev_counts[c]))
    md += ["", "## Open findings by area", "", "| area | " + " | ".join(CHECKS) + " |",
           "|---|" + "---:|" * len(CHECKS)]
    for a in sorted(by_area):
        md.append("| %s | %s |" % (a, " | ".join(str(by_area[a][c]) for c in CHECKS)))
    md += ["", "## Limits", "",
           "* Calls through function pointers (option handlers, saint and item tables, `qsort` comparators) are",
           "  not compared as calls; a handler the port installs is compared as the function it cites.",
           "* Call sites are compared as sets and pairwise orders, not counted: the original C repeats code (one",
           "  loop per case) where the port shares a helper, and the reverse.",
           "* Code the port moved to a caller or callee shows as `missing` on one side and `extra` on the other;",
           "  calls two levels deep (three for `extra`) and coordinates named in the C#'s comments are accepted.",
           "* Constants: a value one off (`x < 6` against `cmp x, 5`), negated or complemented counts as used;",
           "  the port may test through a table or an enum range, and switch tables test the case span.",
           "* Decision points are a coarse count: `&&`/`||` chains, loops and switch tables compile to several",
           "  branches each, and the port may replace branches with tables.",
           "* Functions cited only at statement level, in types or in map.csv are not compared (see Scope).",
           ""]
    (OUT / "STRUCTURE.md").write_text("\n".join(md), encoding="utf-8", newline="\n")
    print("%d functions in %d comparisons, %d without findings; open: %s"
          % (len(compared), len(rows), clean, dict(open_counts)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--exe")
    ap.add_argument("--skip-scan", action="store_true", help="reuse the intermediate files in local/faithfulness/")
    a = ap.parse_args()
    if not a.skip_scan:
        run_extractors(exe_path(a.exe))
    exe = json.load(open(LOCAL / "structure-exe.json", encoding="utf-8"))
    units = json.load(open(LOCAL / "structure-cs.json", encoding="utf-8"))
    thunks, kinds = load_index()
    coverage = load_coverage()
    DS_ADDRESSES.update(ds_addresses())
    RANDOM.update(random_helpers(exe))
    rows, units = compare(units, exe, thunks, coverage, kinds)
    write(rows, load_reviewed(), units, exe, coverage)


if __name__ == "__main__":
    main()
