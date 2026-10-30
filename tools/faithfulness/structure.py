#!/usr/bin/env python3
"""Structural comparison of the C# with the original functions it cites (static, no execution).

Every original function the C# implements is compared with the C# units that implement it (members,
local functions and card handlers with an `// orig:` comment citing it; tools/faithfulness/structure-scan).
The facts of the function come from the unchanged EXE bytes (structure_exe.py), those of the C# from
Roslyn, with the port's untraced helpers followed and its traced members standing for what they cite:

  draws     one side draws random numbers (rand or one of its wrappers: roll-n, roll-below, roll-one-to-n), the other
            draws neither itself nor in what it calls (two calls deep in the C#'s callees, three in the original)
  args      a call both sides make passes a constant the other side never uses (roll-below(100) / Below(10))
  stores    the C# assigns a constant that appears nowhere in the original's code (a next card, a flag), nor
            among the stores of a function both sides call (the port may store its result in the caller), nor
            among the card pictures it names (the port keeps the picture shown as a number)
  missing   the original calls a game-logic function the C# neither calls (even two calls deep), installs (a
            handler nested in it: a card's option action), names, nor does in place (a function of the same record
            whose every call the C# makes and every stored constant it stores: an action written in its menu line);
            a card's reset function (card-option-enabled, card-option-handler and the pop-up tables) is CardMenu's set-up; a leaf that writes
            nothing (member-stat, daylight-test: an accessor) only returns data, which the port may read directly
  extra     the C# calls a traced function the original does not call, even three calls deep (a property
            read is data access, not a call: the original reads memory there; a card's handler that only the
            handler tables reach is installed by the C#'s menu line, not called, nor a line action its card's
            menu loop calls (the line runs it); a function of the same record
            whose work the original does in place is the port's one copy of repeated code; as for `missing`, the
            game logic is compared with the game logic only, the screens with both)
  order     two calls both sides make are made one way round in the C# and only the other way in the
            original: in the original's control flow (a call reached after another on some path), in the C#'s
            evaluation order (a call after its receiver and arguments; C evaluates arguments right to left).
            The body of a lambda or a delegate is not ordered against the code around it, nor two arms of one
            branch against each other, nor code after a return; each member is held against the functions it
            cites. Two calls that touch nothing of each other (what they read and write by address, with their
            callees; neither writes through a pointer) have no order, nor a draw and a call that never reaches
            the random seed
  consts    a `cmp`/`test` immediate of the original that appears nowhere in the C# (a threshold, a flag); a byte
            is a signed char, a key code is matched by its character, a far pointer to a thunk is a handler
  decisions the C# has fewer than half the original's conditional branches (switch cases counted; a card
            function's menu loop, which CardMenu.Run runs, left out)

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

LOCAL = Path(local_paths.local_dir(str(REPO))) / "faithfulness"
OUT = REPO / "docs" / "faithfulness"
INDEX = REPO / "docs" / "source-map" / "original-functions.csv"
COVERAGE = OUT / "coverage.csv"
REVIEWED = OUT / "structure-reviewed.csv"
RAND = "root 06A1:29B6"   # the C run-time's rand()
RANDOM = {RAND}           # and its wrappers (roll-n, roll-below, roll-one-to-n), found in the EXE by random_helpers()
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
# does). A call is accepted as the helper when the C# uses one of the methods; for limit-to-range that holds only
# while lo <= hi (Math.Clamp throws otherwise, and the original returns hi above hi, else the larger of lo and
# v), which this check does not see.
BCL_EQUIVALENTS = {
    "R029+0x4F1A": ({"Math.Clamp", "Math.Min", "Math.Max"}, "limit-to-0-99: v above 99 gives 99, below 0 gives 0"),
    "R029+0x4F38": ({"Math.Clamp", "Math.Min", "Math.Max"}, "limit-to-0-100: v above 100 gives 100, below 0 gives 0"),
    "R029+0x4F76": ({"Math.Clamp", "Math.Min", "Math.Max"}, "limit-to-range(lo, hi, v): hi above hi, lo below lo (signed)"),
    "R029+0x4F94": ({"Math.Max"}, "larger-of(a, b), signed"),
    "R029+0x4FA4": ({"Math.Min"}, "smaller-of(a, b), signed"),
    "root 0410:001A": ({"Math.Clamp", "Math.Min", "Math.Max"}, "clamp(v, lo, hi): lo below lo, hi above hi (signed)"),
}
# The original's services the port does not call function by function: the graphics driver's thunks, the file, catalog
# and picture loading, the text output and the C run-time library. The port reads the game files once (Darklands.Content)
# and draws on a canvas, so a `missing` call into them is not a lead (what is drawn is checked visually).
SERVICES = ("root 047C:", "root 0617:", "root 05D9:", "root 0265:", "root 06A1:")
# Functions the port does with the language or the host: (function, how); not leads for `missing` and `extra`.
IMPLICIT = {
    "R029+0x50DE": "purse-duplicate (d = s, word by word): the port's Money is a value, copied by assignment",
    "root 0000:1338": "quit-check: the loops' check for a quit request, which the host makes at every frame"
                      " (GameUi.Present)",
    "R000+0x083E": "the menu bar's set-up (DS:9419 with its menus): the screens' menu bar, reached through their"
                   " interfaces",
}
# Original functions the port reaches through one member: (the functions, why). card-run R043+0x0A9A (with the
# card's own name, as every card script calls it) is card-run-near R043+0x0BA0 plus a redraw of the party column
# (root 0000:1588, 0000:1716) and the MEM.LOG line (a registered DIVERGENCE); the C#'s ShowPicture (card-run-near)
# draws the party column every time, so a card's card-run and its ShowPicture are one call here.
SAME_CALL = [({"R043+0x0A9A", "R043+0x0BA0"}, "card-run / card-run-near: ShowPicture draws the party column too"),
             ({"R029+0x1A7E", "R029+0x1A8E"}, "location-now-query(q) is place-query(location-now, q)"),
             ({"R029+0x5654", "R029+0x56A6"}, "time-band-now() is time-of-day-band(DS:E0), the same ladder on the current hour")]
ONE_CALL = {t: min(g) for g, _ in SAME_CALL for t in g}
# Code that never draws, whatever it calls: the C run-time library (only rand and srand use the seed; none of its
# functions calls them, and its calls through pointers are int86's `int n / retf`, the heap's and the exit's) and the
# graphics driver's thunks (they dispatch into the asm driver, which calls no game code); and exit-to-system, which
# returns on a no and ends the program on a yes (its shutdown calls the sound driver through a pointer).
SEEDLESS = ("root 06A1:", "root 047C:", "root 0000:13A0")
CARDS = ("R044", "R135")   # the card overlays: one card cannot call another's code (only through R029 / R043)
MERGE_LIMIT = 6   # a unit citing more functions (a summary, or one handler for many identical options) merges none
PRESENTATION = ("src/Darklands.Presentation/", "src/Darklands.Host.", "src/Darklands.Platform/", "src/Darklands.Audio")


def exe_path(arg):
    if arg:
        return arg
    if os.environ.get("DARKLANDS_EXE"):
        return os.environ["DARKLANDS_EXE"]
    try:
        game = json.load(open(Path(local_paths.local_dir(str(REPO))) / "paths.json", encoding="utf-8")).get("game")
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


EXTENTS: dict = collections.defaultdict(list)   # record -> [(first payload, end payload, function)]


def load_index():
    thunks, kinds = {}, {}
    for r in csv.DictReader(open(INDEX, encoding="utf-8")):
        kinds[r["coordinate"]] = r["kind"]
        if r["kind"] == "thunk":
            thunks[r["coordinate"]] = r["target"].split(" (")[0]
        elif r["kind"] == "function" and r["size"] and not r["coordinate"].startswith("root"):
            start = int(r["coordinate"][7:], 16)
            EXTENTS[r["coordinate"][:4]].append((start, start + int(r["size"]), r["coordinate"]))
    return thunks, kinds


def containing(c):
    """The function an overlay address lies in (`R032+0x00FE`, a piece of R032+0x0000), or None."""
    m = re.fullmatch(r"(R\d{3})\+0x([0-9A-Fa-f]+)", c)
    if not m:
        return None
    at = int(m.group(2), 16)
    return next((f for lo, hi, f in EXTENTS.get(m.group(1), ()) if lo <= at < hi), None)


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


def exclusive(a, b):
    """Two C# calls in different arms of one branch (dicts branch -> arm): they never both run."""
    return any(b["arms"].get(k, v) != v for k, v in a["arms"].items())


def stays(a, b):
    """b can run after a: b is inside every scope a cannot leave (a block with a return after a) within the same walk."""
    return all(not (w in b["at"]) or int(start) <= int(b["at"][w]) < int(end) for w, start, end in a["exits"])


def precedes_cs(seq):
    """precedes() for the C#'s (call, where) in evaluation order: only calls that can both run, one after the other."""
    out = set()
    for i, (x, ax) in enumerate(seq):
        for y, ay in seq[i + 1:]:
            if x != y and not exclusive(ax, ay) and stays(ax, ay):
                out.add((x, y))
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
THUNK_SEGMENT = 0x09C0
THUNK_OFFSETS: set = set()


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

    # a function the C# implements only in the screens, hosts and drivers (members, handlers and types with a trace
    # there; a unit of the game logic counts with the function its trace names first, not the ones its prose goes on
    # to name: "..., the panel's row (root 0000:1716)") is presentation: coverage.csv counts every file naming it
    implemented_in = collections.defaultdict(set)
    for u in units:
        shown = u["file"].startswith(PRESENTATION)
        for c in (u["cites"] if shown else u["cites"][:1]):
            implemented_in[canon(c)].add(shown)

    def role(c):
        r = coverage.get(c, ("runtime", c))[0]
        return "presentation" if r == "logic" and implemented_in.get(c) == {True} else r

    callees = {k: [canon(t) for _, t, _ in v.get("calls", [])] for k, v in exe.items()}
    called_directly = {t for ts in callees.values() for t in ts}
    # a card's line actions: what a card function that runs the menu (card-choice-menu R043+0x1BD2) calls in its own record
    line_actions = {t for k, ts in callees.items() if "R043+0x1BD2" in ts for t in ts if t[:4] == k[:4]}

    # the pure queries: functions that write no memory outside their frame and call only pure functions (rand
    # writes its seed; a call through a pointer or out of the index is taken to write)
    # the functions that may use the random seed (rand, srand, and whatever reaches them, or calls through a pointer):
    # a draw commutes with any call outside them, which neither reads nor moves the sequence
    seeded = {k for k, v in exe.items() if v.get("seed") or (not k.startswith(SEEDLESS) and (
        "error" in v or any(t == "*" or t not in exe for t in callees.get(k, []))))}
    grown = True
    while grown:
        grown = False
        for k in exe:
            if k not in seeded and not k.startswith(SEEDLESS) and any(t in seeded for t in callees.get(k, [])):
                seeded.add(k)
                grown = True
    # what each function reads and writes by address, with its callees; `blind`: it may write anything (through a
    # pointer or an index, into another segment, through a call by pointer or out of the index). Two calls commute when
    # neither is blind and neither writes what the other reads or writes (a read through a pointer is taken not to reach
    # a global the code names by its address: top-value-so-far, the event cursor)
    reads = {k: set(v.get("dr", ())) for k, v in exe.items()}
    writes = {k: set(v.get("dw", ())) for k, v in exe.items()}
    blind = {k for k, v in exe.items()
             if "error" in v or v.get("iw", True) or any(t == "*" for _, t, _ in v.get("calls", []))}
    grown = True
    while grown:
        grown = False
        for k in exe:
            for t in callees.get(k, []):
                if t not in exe:
                    if k not in blind:
                        blind.add(k)
                        grown = True
                    continue
                if t in blind and k not in blind:
                    blind.add(k)
                    grown = True
                if not reads[t] <= reads[k] or not writes[t] <= writes[k]:
                    reads[k] |= reads[t]
                    writes[k] |= writes[t]
                    grown = True

    # a card's reset function: it sets card-option-enabled[0..19] (DS:E99C) to 0 or 1 and points card-option-handler[0..19] (DS:EA14) and the
    # pop-up tables at the card's handlers; CardMenu does that when the card makes its menu (with the card's Enable
    # calls), and the card oracle compares the menus' card-option-enabled states (CardOracleTests)
    resets = {k for k, v in exe.items()
              if CARDS[0] <= k[:4] <= CARDS[1] and 0xEA14 in v.get("imms", []) and not v.get("calls")}

    def commute(x, y):
        return (x not in blind and y not in blind and x in exe and y in exe
                and not writes[x] & (reads[y] | writes[y]) and not writes[y] & reads[x])

    def deep(cs, levels):
        seen, frontier = set(cs), set(cs)
        for _ in range(levels):
            frontier = {x for c in frontier for x in callees.get(c, [])} - seen
            seen |= frontier
        return seen

    units = [u for u in units if u["kind"] != "type"]
    for u in units:
        cites = [canon(c) for c in u["cites"] if kinds.get(thunks.get(c, c)) in ("function", "thunk")]
        u["entry"] = [c for c in dict.fromkeys(cites) if c in exe]
        # a cited address inside a function (a piece the port wrote as its own member, such as one switch of it)
        # joins the comparison of that function, but does not make one: alone it is not the function
        pieces = [canon(containing(c)) for c in u["cites"]
                  if kinds.get(thunks.get(c, c)) not in ("function", "thunk") and containing(c)]
        u["part"] = [c for c in dict.fromkeys(pieces) if c in exe and c not in u["entry"]]
        u["fn"] = u["entry"] + u["part"]
        # callee, args, documented, C# name, reached through a property (data access)
        u["c"] = [(canon(x[1]), x[4], x[5], x[3], x[6]) for x in u["calls"]]
        # the calls of each context (the unit's own code, each lambda body: code that runs when it is invoked)
        u["ctx"] = collections.defaultdict(list)
        for x in u["calls"]:
            if not x[5]:
                u["ctx"][x[7]].append((canon(x[1]), {
                    "arms": dict(a.rsplit(":", 1) for a in x[8] if a[0] not in "@!"),
                    "at": dict(a[1:].split(".") for a in x[8] if a[0] == "@"),
                    "exits": [a[1:].split(".") for a in x[8] if a[0] == "!"]}))
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
        fs = u["entry"]
        if len(fs) <= MERGE_LIMIT:
            for f in fs[1:]:
                a, b = find(f), find(fs[0])
                if a != b and size[a] + size[b] + 2 <= 2 * MERGE_LIMIT:   # chains stay small
                    parent[a] = b
                    size[b] += size[a] + 1
    groups = collections.defaultdict(lambda: ([], []))
    for u in units:
        for f in u["entry"]:
            g = groups[find(f)]
            if u not in g[0]:
                g[0].append(u)
            if f not in g[1]:
                g[1].append(f)
    for u in units:
        for f in u["part"]:
            if find(f) in groups and f in groups[find(f)][1] and u not in groups[find(f)][0]:
                groups[find(f)][0].append(u)
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
        # (not by a member that implements more than MERGE_LIMIT functions at once, a summary: its calls are theirs)
        c_called = [t for u in members if len(u["fn"]) <= MERGE_LIMIT
                    for t, _, d, _, p in u["c"] if t not in own and kinds.get(t) == "function" and not d and not p]
        o_seq = [t for t, _ in o_calls]
        c_seq = [t for t, _ in c_calls]
        mentions = {canon(c) for u in members for c in u["mentions"]}
        # the handlers a member installs (a card's option actions, which the original's card function calls from
        # its switch after the menu) are its calls of what they cite
        installs = {canon(c) for u in members for c in u.get("installs", ())}
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
        same = {t: g for g, _ in SAME_CALL for t in g}
        c_deep |= {x for t in c_deep & set(same) for x in same[t]}
        # a function of the same record that the port does in place (a card's option action written in its menu line):
        # every call it makes is reached and every constant it stores is stored (an empty one trivially)
        c_lits = {v for u in members for v in u["literals"] + u["stores"]}
        records = {g[:4] for g in fns}

        def inlined(t):
            v = exe.get(t)
            if v is not None and "error" not in v and not v.get("calls", True) and not v.get("writes", True):
                return True   # an empty function (`retf`), or a pure accessor the port reads the data of directly
            if (v is not None and "error" not in v and not v.get("writes", True) and callees.get(t)
                    and all(x.startswith(SERVICES) for x in callees[t])):
                return True   # a wrapper of the run-time's services that writes nothing itself (a file catalogue lookup)
            # (a card function of another record is this card's own copy of it: the card overlays cannot call one
            # another, so coverage maps a byte-identical action to the first card that has it)
            return (v is not None and "error" not in v and (t[:4] in records or CARDS[0] <= t[:4] <= CARDS[1])
                    and all(x in c_deep or x in mentions or x.startswith(SERVICES) for x in callees.get(t, []))
                    and all(not value(w) or near(w, c_lits) for w in v.get("stores", [])))

        missing = [t for t in dict.fromkeys(o_seq)
                   if role(t) in compared_roles and t not in c_deep and t not in mentions and t not in installs
                   and t not in RANDOM
                   and not t.startswith(SERVICES) and not inlined(t) and t not in IMPLICIT and t not in resets
                   and not (t in BCL_EQUIVALENTS and BCL_EQUIVALENTS[t][0] & bcl)]
        if missing:
            found["missing"] = " ".join(missing)

        # a shared helper of the port may cite one card's copy of a pattern (a member pop-up hint): from
        # another card that is the same pattern, not a call (the card overlays cannot call one another)
        own_records = {c[:4] for c in own}
        o_deep = deep(own, 3)
        o_deep |= {x for t in o_deep & set(same) for x in same[t]}
        # (a card's hint and pop-up handlers, which its reset function installs through the handler tables and nothing
        # calls directly, are installed by the C#'s menu lines: not calls)
        # (nor a function of the same record whose work the original does in place, the port sharing one copy of code
        # the original repeats: every call it makes is in the original's reach, every constant it stores the original's;
        # nor, from any record, a function that writes nothing itself, a pure accessor the original reads the data of in
        # place or a wrapper of calls the original makes itself: wait-for-hour = hours-remaining-until, pass-hours)
        o_imms = {v for c in own for v in exe[c].get("imms", [])}

        def repeated(t):
            v = exe.get(t)
            if v is None or "error" in v:
                return False
            if t[:4] not in records and (v.get("writes", True) or not set(callees.get(t, [])) <= o_deep):
                return False
            return (all(x in o_deep or x.startswith(SERVICES) for x in callees.get(t, []))
                    and all(not value(w) or near(w, o_imms) for w in v.get("stores", [])))

        # (a member that stands for two functions the port merges, SAME_CALL, calls the one the original calls)
        o_one = {ONE_CALL.get(x, x) for x in o_deep}
        extra = [t for t in dict.fromkeys(c_called)
                 if role(t) in compared_roles and t not in o_deep and t not in mentions and t not in RANDOM
                 and ONE_CALL.get(t, t) not in o_one
                 and not (CARDS[0] <= t[:4] <= CARDS[1] and t[:4] not in own_records)
                 and not (CARDS[0] <= t[:4] <= CARDS[1] and t not in called_directly) and not repeated(t)
                 and t not in line_actions
                 and t not in IMPLICIT]
        if extra:
            found["extra"] = " ".join(extra)

        key = lambda t: ONE_CALL.get(t, t)   # noqa: E731
        common = {key(t) for t in o_seq} & {key(t) for t in c_seq}
        # (within each original function and each C# member: a hint and its option are separate code)
        # (the original's order is its control flow: a call reached after another on some path)
        o_pre_of = {}
        for g in fns:
            targets = [key(canon(t)) for _, t, _ in exe[g]["calls"]]
            after = exe[g].get("after") or [list(range(i + 1, len(targets))) for i in range(len(targets))]
            o_pre_of[g] = {(targets[i], targets[j]) for i, js in enumerate(after) for j in js
                           if targets[i] in common and targets[j] in common and targets[i] != targets[j]}
        swapped = set()
        for u in members:
            # (each member against the original functions it cites itself, not the others merged into the comparison)
            o_pre = set().union(*[o_pre_of[g] for g in u["fn"] if g in o_pre_of])
            # (and within each context of a member: a lambda's code has no order against the code around it)
            # (and never between calls in two arms of one branch)
            c_pre = set().union(*[precedes_cs([(key(t), a) for t, a in seq if key(t) in common])
                                  for seq in u["ctx"].values()])
            # (two calls that touch nothing of each other give the same results in either order; a draw and a call that
            # never reaches the seed, the same sequence of draws)
            swapped |= {(x, y) for x, y in c_pre - o_pre if (y, x) in o_pre and not commute(x, y)
                        and not (x in RANDOM and y not in seeded) and not (y in RANDOM and x not in seeded)}
        swapped = sorted(swapped)
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
        # (a byte operation on a word's high byte: `or byte ptr [x+1], 4` sets 0x400, `and ..., 0FBh` clears it)
        o_all |= {w for c in own for b in exe[c].get("bytes", []) for w in (b << 8, (b << 8) | 0xFF)}

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

        # (a function both sides call may store it in the original where the port stores its result in the caller)
        moved = {v for t in set(o_seq) & set(c_seq) for v in exe.get(t, {}).get("stores", [])}
        # (a card keeps the picture it shows as its number; the original copies the picture's name, image-file-names[n])
        moved |= {v for c in own for v in exe[c].get("pics", [])}
        new_stores = sorted({v for u in members for v in u["stores"] if value(v) and not near(v, o_all | moved)})
        if new_stores:
            found["stores"] = words(new_stores)

        cmps = {v for g in fns for v in exe[g].get("cmps", [])}
        # (a byte immediate is a signed char: 0xFF is -1; a byte test of a word's high half is the word's flag << 8;
        # a key code is the scan code << 8 | the character, which the C# matches by the character; a far pointer to a
        # thunk of segment 09C0, and the thunk's offset, are a handler)
        thunked = THUNK_SEGMENT in cmps
        high = {v for g in fns for v in exe[g].get("bytes", [])}
        lost = sorted(v for v in cmps if value(v) and not near(v, c_all)
                      and not (0x80 <= v < 0x100 and near(v | 0xFF00, c_all))
                      and not (v in high and near(v << 8, c_all))
                      and not (0x100 <= v < 0x8700 and 0x20 < v & 0xFF < 0x7F
                               and {v & 0xFF, ord(chr(v & 0xFF).swapcase())} & c_all)
                      and not (thunked and (v == THUNK_SEGMENT or v in THUNK_OFFSETS)))
        if lost:
            found["consts"] = words(lost)

        o_dec = sum(exe[g]["branches"] + sum(max(0, n - 1) for n in exe[g]["switches"]) for g in fns)
        # (a card function's menu loop: the switch over the line chosen, the pop-up test, the loop's condition and the
        # case bound are CardMenu.Run's, not the card's)
        o_dec -= sum(max(0, max(exe[g]["switches"] or [0]) - 1) + 3 for g in fns
                     if any(t == "R043+0x1BD2" for _, t, _ in exe[g]["calls"]))
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
           "* Constants: a value one off (`x < 6` against `cmp x, 5`), negated or complemented counts as used,",
           "  and so do a signed byte (0xFF as -1), a byte test of a word's high half (0x20 as 0x2000), a BIOS",
           "  key word whose character the C# tests, and a far pointer to a thunk; a jump table's range test and",
           "  the bound of a counter stepped by a stride (a row offset) are not compared. Key words of keys",
           "  without a character (Esc, Enter, arrows) and enum members defined outside the unit are not seen.",
           "* Order is compared per C# context (a lambda, a deferred handler, each arm of a branch) against the",
           "  original's control flow; calls that commute (no shared state read and written, no random draw",
           "  between them) are not ordered. A pure query's end state (a best-member global) is seen only",
           "  through the order of the queries.",
           "* Card scripts: the menu loop (card-choice-menu R043+0x1BD2 and the switch after it) is CardMenu.Run's;",
           "  a card's reset function and the hint and pop-up handlers it installs are the menu lines'; a",
           "  line action reached only through the option table is the line, not a call.",
           "* Decision points are a coarse count: `&&`/`||` chains, loops and switch tables compile to several",
           "  branches each, and the port may replace branches with tables; decisions in followed helpers count.",
           "* Run-time library, graphics, sound and file services (segments 047C, 0617, 05D9, 0265, 06A1) are",
           "  the host's, not compared as calls; the BCL's equivalents (clamp) are accepted, and so is a wrapper",
           "  of those services that writes nothing (a file catalogue lookup). A far jump to 0000:0000 is a",
           "  graphics-driver entry patched at run time, not a call.",
           "* Shared code: an orig line's coordinates after \"tail of\", \"repeated inline in\", \"repeated in\" or",
           "  \"filled by\" (to the end of its block) are sites the member shares code with, not functions it",
           "  implements: a call of the member is followed into, and a member that only shares code is compared",
           "  with its first site. An example (\"e.g. R059+0x0CEC\") stays a cite: a shared handler stands for",
           "  every card's copy. A coordinate in short form (`_0692`) is not read.",
           "* Code done in place: a function the original calls and the port inlines is accepted when it is pure",
           "  (no calls, no writes), or of the same record or a card's copy of another card's function (card",
           "  overlays cannot call one another), with every call in the port's reach and every constant it",
           "  stores in the port; for `extra`, a function that writes nothing and whose calls the original",
           "  makes itself (a wrapper such as wait-for-hour) is accepted the same way.",
           "* Delegates and closures: a call through a delegate field or a lambda stored in a local is seen only",
           "  where the delegate's target is traced; menu-bar handlers given to MenuBar.Item count as calls.",
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
    THUNK_OFFSETS.update(int(t.split(":")[1], 16) for t in thunks if t.startswith("thunk 09C0:"))
    coverage = load_coverage()
    DS_ADDRESSES.update(ds_addresses())
    RANDOM.update(random_helpers(exe))
    rows, units = compare(units, exe, thunks, coverage, kinds)
    write(rows, load_reviewed(), units, exe, coverage)


if __name__ == "__main__":
    main()
