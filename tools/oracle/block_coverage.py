"""Basic-block and branch coverage of the original functions the oracle corpora execute.

usage: python -I -B block_coverage.py <DARKLAND.EXE> <game folder> <work dir> [options]

  --only a,b        run only these corpus builders (names below)
  --skip-run        do not run the builders: use the coverage already in <work dir>/<name>/cov
  --lines FILE      machine-local source lines of the reference decompilation (JSON {record: {payload
                    hex: "file:line"}}); default <work dir>/ref-lines.json when present. They are written
                    only to local/faithfulness/branches-ref-lines.csv (git-ignored), never to the outputs
  --unreachable F   the dormant/unreachable list (default docs/faithfulness/unreachable.csv)
  --out DIR         where BRANCHES.md, branches.csv and branches-missed.csv go
                    (default docs/faithfulness)
  --exclude a,b     leave these coverage sources out

Every corpus builder runs as it does for its corpus, with DL_COVERAGE_DIR set: run_unit.run then
records the executed instructions, translated blocks and conditional-branch outcomes of every
case in original coordinates (record + payload offset; root: linear address). The corpus each
builder writes goes to the work dir and is compared with the committed one (the coverage is
only meaningful for the cases the C# replays). Any further <work dir>/<name>/cov folder (e.g. the
card oracle's) is added as a coverage source too.

The blocks and branches of each executed function are enumerated statically (cfg.py) from the
EXE bytes, inside the function's extent from docs/source-map/original-functions.csv. Functions
are named by their coordinate (RNNN+0xPPPP / root SSSS:OOOO).
"""
from __future__ import annotations

import argparse
import bisect
import csv
import json
import os
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
from run_unit import Rtlink  # noqa: E402
import cfg as cfgmod  # noqa: E402

CORPUS = REPO / "tests" / "Darklands.Oracle.Tests" / "Corpus"
# name -> (script, needs the game folder, corpus file, extra arguments)
BUILDERS = {
    "core": ("build_core_corpus.py", False, "core.json", []),
    "rules": ("build_rules_corpus.py", False, "rules.json", []),
    "saints": ("build_saints_corpus.py", False, "saints.json", []),
    "trading": ("build_trading_corpus.py", False, "trading.json", []),
    "chargen": ("build_chargen_corpus.py", False, "chargen.json", []),
    "names": ("build_names_corpus.py", False, "names.json", []),
    "battle": ("build_battle_corpus.py", False, "battle.json", []),
    "tactics": ("build_tactics_corpus.py", False, "tactics.json", []),
    "path": ("build_path_corpus.py", False, "path.json", []),
    "enemy": ("build_enemy_corpus.py", True, "enemies.json", ["120"]),
    "city": ("build_city_corpus.py", True, "city.json", ["40"]),
    "wild": ("build_wild_corpus.py", True, "wild.json", ["96"]),
    "mine": ("build_mine_corpus.py", True, "mine.json", ["400"]),
    "castle": ("build_castle_corpus.py", True, "castle.json", ["100"]),
    "special": ("build_special_corpus.py", True, "special.json", []),
}
# the order of the report: the rules first
PRIORITY = ["rules", "saints", "trading", "core", "chargen", "names", "battle", "tactics", "path", "enemy", "city",
            "wild", "mine", "castle", "special", "cards"]


def without_nulls(v):
    """A corpus as the tests read it: a key whose value is null is the same as no key."""
    if isinstance(v, dict):
        return {k: without_nulls(x) for k, x in v.items() if x is not None}
    if isinstance(v, list):
        return [without_nulls(x) for x in v]
    return v


def run_builders(exe, game, work, names, python):
    report = {}
    for name in names:
        script, needs_game, corpus, extra = BUILDERS[name]
        d = work / name
        cov = d / "cov"
        cov.mkdir(parents=True, exist_ok=True)
        for f in cov.glob("cov-*.jsonl"):
            f.unlink()
        out = d / corpus
        argv = [python, "-I", "-B", str(HERE / script), str(exe)] + ([str(game)] if needs_game else []) + [str(out)] + extra
        env = dict(os.environ, DL_COVERAGE_DIR=str(cov))
        t = time.time()
        r = subprocess.run(argv, env=env, capture_output=True, text=True)
        took = time.time() - t
        same = (out.exists() and (CORPUS / corpus).exists()
                and without_nulls(json.loads(out.read_text())) == without_nulls(json.loads((CORPUS / corpus).read_text())))
        report[name] = {"seconds": round(took), "exit": r.returncode, "same_as_committed": same,
                        "tail": (r.stdout + r.stderr)[-400:]}
        (d / "run.json").write_text(json.dumps(report[name], indent=1))
        print("%-8s %5ds exit %d corpus %s" % (name, took, r.returncode, "identical" if same else "DIFFERS"), flush=True)
    return report


def load_coverage(work):
    """{source: {record: {"insns": set, "edges": set}}} from every <work>/<name>/cov folder."""
    out = {}
    for d in sorted(work.iterdir()):
        cov = d / "cov"
        if not cov.is_dir():
            continue
        recs = defaultdict(lambda: {"insns": set(), "edges": set()})
        runs = 0
        for f in cov.glob("cov-*.jsonl"):
            for line in f.open():
                line = line.strip()
                if not line:
                    continue
                runs += 1
                j = json.loads(line)
                for rec, pays in j["insns"].items():
                    recs[rec]["insns"].update(pays)
                for rec, edges in j["edges"].items():
                    recs[rec]["edges"].update(tuple(e) for e in edges)
        if runs:
            out[d.name] = dict(recs)
            out[d.name]["_runs"] = runs
    return out


class Verdicts:
    """docs/faithfulness/unreachable.csv: function, record, payload (hex, or * for the whole function),
    kind (block / taken / fallthrough, or *), verdict, justification, csharp (where the C# keeps it)."""

    def __init__(self, path):
        self.items, self.whole = {}, {}
        if path.exists():
            for r in csv.DictReader(path.open(encoding="utf-8")):
                if r["payload"] == "*":
                    self.whole[r["function"]] = r
                else:
                    self.items[(r["record"], int(r["payload"], 16), r["kind"])] = r

    def get(self, function, record, payload, kind):
        return self.items.get((record, payload, kind)) or self.items.get((record, payload, "*")) or self.whole.get(function)


def load_unreachable(path):
    return Verdicts(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("work")
    ap.add_argument("--only")
    ap.add_argument("--skip-run", action="store_true")
    ap.add_argument("--exclude", default="", help="coverage sources to leave out (comma-separated folder names)")
    ap.add_argument("--lines")
    ap.add_argument("--unreachable", default=str(REPO / "docs" / "faithfulness" / "unreachable.csv"))
    ap.add_argument("--out", default=str(REPO / "docs" / "faithfulness"))
    a = ap.parse_args()
    exe_path, work = Path(a.exe), Path(a.work)
    work.mkdir(parents=True, exist_ok=True)
    names = a.only.split(",") if a.only else list(BUILDERS)
    if not a.skip_run:
        run_builders(exe_path, Path(a.game), work, names, sys.executable)
    exe = exe_path.read_bytes()
    rt = Rtlink(exe)
    sources = {k: v for k, v in load_coverage(work).items() if k not in a.exclude.split(",")}
    lines_path = Path(a.lines) if a.lines else work / "ref-lines.json"
    lines = json.loads(lines_path.read_text()) if lines_path.exists() else {}
    unreachable = load_unreachable(Path(a.unreachable))

    functions = cfgmod.load_functions(REPO / "docs" / "source-map" / "original-functions.csv", rt)
    by_rec = defaultdict(list)
    for fn in functions:
        by_rec[fn.record].append(fn)
    for rec in by_rec:
        by_rec[rec].sort(key=lambda f: f.payload(f.linear))
    starts = {rec: [f.payload(f.linear) for f in fns] for rec, fns in by_rec.items()}

    # merge the sources; remember which sources reach each function
    insns, edges = defaultdict(set), defaultdict(set)
    reached = defaultdict(set)
    for src, recs in sources.items():
        for rec, v in recs.items():
            if rec.startswith("_") or rec == "?" or rec not in by_rec:
                continue
            insns[rec] |= v["insns"]
            edges[rec] |= v["edges"]
            st = starts[rec]
            for p in v["insns"]:
                k = bisect.bisect_right(st, p) - 1
                if k >= 0:
                    fn = by_rec[rec][k]
                    if p < fn.payload(fn.linear) + fn.size:
                        reached[fn.name].add(src)

    rows, missed_rows = [], []
    totals = defaultdict(lambda: [0, 0, 0, 0, 0])
    fn_by_name = {f.name: f for f in functions}
    for name in sorted(reached, key=lambda n: (fn_by_name[n].record != "root", fn_by_name[n].record, fn_by_name[n].linear)):
        fn = fn_by_name[name]
        rec = fn.record
        lo = fn.payload(fn.linear)
        hi = lo + fn.size
        ex = {p for p in insns[rec] if lo <= p < hi}
        base = fn.linear - lo
        g0 = cfgmod.build(exe, rt, fn)
        extra = [p + base for p in ex if (p + base) not in g0.insns]
        g = cfgmod.build(exe, rt, fn, extra) if extra else g0
        blocks = sorted(g.blocks)
        hit_blocks = [b for b in blocks if (b - base) in ex]
        outcomes, hit_out = [], []
        e = edges[rec]
        for j, t, f, m in g.branches:
            for kind, dst in (("taken", t), ("fallthrough", f)):
                if t == f and kind == "fallthrough":
                    continue
                outcomes.append((j, kind, dst, m))
                if (j - base, dst - base) in e:
                    hit_out.append((j, kind))
        hit_set = set(hit_out)
        rl = lines.get(rec, {})

        def line_of(lin):
            p = lin - base
            best = None
            for k in range(p, max(lo, p - 64) - 1, -1):
                if "%04X" % k in rl:
                    best = rl["%04X" % k]
                    break
            return best or ""

        missed_blocks = [b for b in blocks if b not in hit_blocks]
        dormant = 0
        for b in missed_blocks:
            u = unreachable.get(name, rec, b - base, "block")
            dormant += u is not None
            missed_rows.append({"function": name, "record": rec, "payload": "%04X" % (b - base), "kind": "block",
                                "coordinate": fn.coordinate(b), "line": line_of(b),
                                "first_insn": "%s %s" % (g.insns[b].mnemonic, g.insns[b].op_str),
                                "status": (u["verdict"] if u else "open"), "sources": " ".join(sorted(reached[name]))})
        for j, kind, dst, m in outcomes:
            if (j, kind) in hit_set:
                continue
            u = unreachable.get(name, rec, j - base, kind)
            missed_rows.append({"function": name, "record": rec, "payload": "%04X" % (j - base), "kind": kind,
                                "coordinate": fn.coordinate(j), "line": line_of(j),
                                "first_insn": "%s %s" % (m, g.insns[j].op_str),
                                "status": (u["verdict"] if u else "open"), "sources": " ".join(sorted(reached[name]))})
        row = {"function": name, "record": rec, "coordinate": fn.coordinate(fn.linear), "size": fn.size,
               "status": fn.status, "sources": " ".join(sorted(reached[name])),
               "blocks_hit": len(hit_blocks), "blocks": len(blocks), "outcomes_hit": len(hit_set),
               "outcomes": len(outcomes), "missed_dormant": dormant,
               "unresolved_jumps": len(g.unresolved), "dynamic_leaders": len(extra),
               "missed_blocks": " ".join("%04X" % (b - base) for b in missed_blocks)}
        rows.append(row)
        for src in list(reached[name]) + ["ALL"]:
            t = totals[src]
            t[0] += 1
            t[2] += len(blocks)
            t[4] += len(outcomes)
            if src == "ALL":
                t[1] += len(hit_blocks)
                t[3] += len(hit_set)
            else:   # what this source alone exercises
                si, se = sources[src][rec]["insns"], sources[src][rec]["edges"]
                t[1] += sum(1 for b in blocks if (b - base) in si)
                t[3] += sum(1 for j, kind, dst, _m in outcomes if (j - base, dst - base) in se)

    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    with (out / "branches.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)
    with (out / "branches-missed.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=["function", "record", "payload", "kind", "coordinate",
                                          "first_insn", "status", "sources"], extrasaction="ignore")
        w.writeheader()
        w.writerows(missed_rows)
    if lines:   # the reference's source lines stay machine-local
        local = REPO / "local" / "faithfulness"
        local.mkdir(parents=True, exist_ok=True)
        with (local / "branches-ref-lines.csv").open("w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=["function", "record", "payload", "kind", "line"], extrasaction="ignore")
            w.writeheader()
            w.writerows(m for m in missed_rows if m["line"])
    summary = {src: {"functions": t[0], "blocks_hit": t[1], "blocks": t[2], "outcomes_hit": t[3], "outcomes": t[4],
                     "runs": sources.get(src, {}).get("_runs")} for src, t in totals.items()}
    (out / "branches-summary.json").write_text(json.dumps(summary, indent=1, sort_keys=True) + "\n")
    write_markdown(out / "BRANCHES.md", rows, missed_rows, summary, work)
    a_ = summary["ALL"]
    print("functions %d  blocks %d/%d  outcomes %d/%d" % (a_["functions"], a_["blocks_hit"], a_["blocks"],
                                                         a_["outcomes_hit"], a_["outcomes"]))


def pct(a, b):
    return "%.1f%%" % (100.0 * a / b) if b else "-"


def write_markdown(path, rows, missed, summary, work):
    runs = {}
    for d in work.iterdir():
        if (d / "run.json").exists():
            runs[d.name] = json.loads((d / "run.json").read_text())
            if d.name in BUILDERS:   # the corpus as the builder wrote it, against the committed one
                corpus = BUILDERS[d.name][2]
                mine, theirs = d / corpus, CORPUS / corpus
                runs[d.name]["same_as_committed"] = (mine.exists() and theirs.exists() and
                                                     without_nulls(json.loads(mine.read_text())) == without_nulls(json.loads(theirs.read_text())))
    before_path = path.parent / "branches-summary-before.json"
    before = json.loads(before_path.read_text()) if before_path.exists() else {}
    L = ["# Branch coverage of the original code by the oracles", "",
         "Generated by `tools/oracle/block_coverage.py` (do not edit by hand). Every corpus builder of",
         "`tools/oracle` runs its cases on the unchanged DARKLAND.EXE with block and branch recording",
         "(`run_unit.py`, `DL_COVERAGE_DIR`); the basic blocks and conditional branches of each executed",
         "original function are enumerated with capstone from the EXE bytes within its extent",
         "(`docs/source-map/original-functions.csv`). A block is exercised when its first instruction",
         "ran; a branch outcome (taken / fall-through) when the instruction after the branch was its",
         "target / the next instruction. `branches.csv` has one row per function, `branches-missed.csv`",
         "one row per never-exercised block or branch outcome, with its status: `open`, or the verdict",
         "of `unreachable.csv` (dormant / unreachable / presentation, with the justification).", "",
         "Coordinates: `RNNN+0xPPPP (SSSS:OOOO)` = record, payload offset, link-time address; root",
         "functions `root SSSS:OOOO`.", "",
         "## Totals by source", "",
         "Each source's own share (what its cases exercise in the functions they reach); `ALL` is the union.",
         "The `before` columns are the coverage before the gaps were worked on (branches-summary-before.json).", "",
         "| source | runs | functions | blocks | outcomes | blocks before | outcomes before | corpus regenerated |",
         "|---|---|---|---|---|---|---|---|"]
    order = [s for s in PRIORITY if s in summary] + sorted(s for s in summary if s not in PRIORITY and s != "ALL") + ["ALL"]
    for s in order:
        t = summary[s]
        r = runs.get(s)
        b = before.get(s)
        L.append("| %s | %s | %d | %d/%d (%s) | %d/%d (%s) | %s | %s | %s |" % (
            s, t["runs"] or "", t["functions"], t["blocks_hit"], t["blocks"], pct(t["blocks_hit"], t["blocks"]),
            t["outcomes_hit"], t["outcomes"], pct(t["outcomes_hit"], t["outcomes"]),
            "%d/%d (%s)" % (b["blocks_hit"], b["blocks"], pct(b["blocks_hit"], b["blocks"])) if b else "",
            "%d/%d (%s)" % (b["outcomes_hit"], b["outcomes"], pct(b["outcomes_hit"], b["outcomes"])) if b else "",
            "" if r is None else ("identical" if r["same_as_committed"] else "differs") + " (%ds)" % r["seconds"]))
    open_ = [m for m in missed if m["status"] == "open"]
    L += ["", "Never-exercised items: %d, of which open %d, justified in `unreachable.csv` %d." % (
        len(missed), len(open_), len(missed) - len(open_)), ""]
    L += ["## Per function", "",
          "Blocks and outcomes exercised / total; the never-exercised blocks follow (payload offsets in the",
          "record; the outcomes are in `branches-missed.csv`).", ""]
    by_fn = defaultdict(list)
    for m in missed:
        if m["kind"] == "block":
            by_fn[m["function"]].append(m)
    for r in rows:
        L.append("### %s `%s`" % (r["function"], r["coordinate"]))
        L.append("")
        L.append("%s, reached by %s: blocks %d/%d, outcomes %d/%d%s" % (
            r["status"] or "no status", r["sources"], r["blocks_hit"], r["blocks"],
            r["outcomes_hit"], r["outcomes"], ", unresolved indirect jumps %d" % r["unresolved_jumps"]
            if r["unresolved_jumps"] else ""))
        ms = by_fn.get(r["function"], [])
        if ms:
            L.append("")
            for m in ms:
                L.append("- `%s+0x%s` `%s`%s" % (m["record"], m["payload"], m["first_insn"],
                                                "" if m["status"] == "open" else " (%s)" % m["status"]))
        L.append("")
    path.write_text("\n".join(L) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
