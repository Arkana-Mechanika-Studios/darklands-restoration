#!/usr/bin/env python3
"""Faithfulness registers: scans src/ for DIVERGENCE / BUG / UNCERTAIN / DORMANT markers and generates

  docs/faithfulness/DIVERGENCES.md, ORIGINAL_BUGS.md, UNCERTAIN.md, DIVERGENCE_REVIEW.md
  docs/faithfulness/divergences.csv, original_bugs.csv, uncertain.csv, dormant.csv

Usage (from anywhere inside the repository):
  python tools/faithfulness/registers.py            regenerate the files
  python tools/faithfulness/registers.py --check    exit 1 when the files on disk are out of date
  python tools/faithfulness/registers.py --stats    print counts only

Hand-maintained inputs, next to this script:
  intros/*.md          the short introductions of the generated documents
  overrides.csv        id,class[,area]   a classification (or area) the heuristics get wrong
  review_notes.csv     id,action,determinism,verified,analysis,stale,done   the recommendation for each entry of the review list;
                       `done` says what was changed once an entry is made faithful (its marker may then be gone)

Standard library only.
"""
from __future__ import annotations

import bisect
import csv
import hashlib
import io
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import scan  # noqa: E402

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
OUT = ROOT / "docs" / "faithfulness"

AREAS = ["rules", "cards", "events", "travel", "battles", "persistence", "screens/presentation", "host/audio"]
CORE_AREAS = AREAS[:6]
DIV_CLASSES = ["undefined-memory", "host", "not-modelled", "deliberate-fix", "other"]
REVIEW_CLASSES = ("not-modelled", "deliberate-fix", "other")
PREFIX = {"DIVERGENCE": "D", "BUG": "B", "UNCERTAIN": "U", "DORMANT": "Z"}


# --------------------------------------------------------------------------- areas

def area_of(file: str) -> str:
    p = file.removeprefix("src/")
    proj, _, rest = p.partition("/")
    proj = proj.removeprefix("Darklands.")
    if proj in ("Audio", "Platform") or proj.startswith("Host"):
        return "host/audio"
    if proj == "Presentation":
        return "screens/presentation"
    if proj == "Cards":
        return "cards"
    if proj == "Persistence":
        return "persistence"
    if proj == "Content":
        if rest.startswith(("Formats/Graphics", "Formats/Battles", "Formats/Audio")):
            return "screens/presentation"
        return "persistence"
    if proj == "Simulation":
        if rest.startswith("Overland"):
            return "travel"
        if rest.startswith("CardDispatcher"):
            return "cards"
        return "rules"
    if proj == "Core":
        if rest.startswith("Battles"):
            return "battles"
        if rest.startswith("Events"):
            return "events"
        if rest.startswith("World"):
            return "travel"
        return "rules"
    return "rules"


# --------------------------------------------------------------------------- classification

_F = re.compile(r"for ever|forever|would hang|hang the|instead of crashing|gives up after|search stops|stops after|loop here ends|ends after", re.I)
_N_STRONG = re.compile(r"not (yet )?(written|modelled|reproduced|implemented|decoded|connected|read yet|ported|played)|is not here|are not here|not run\b|not asked", re.I)
_U = re.compile(
    r"garbage|uninitiali[sz]ed|stack|stale|left-?overs?|whatever|out of bounds|outside the|past (the|its|a|an|that)\b|beyond|"
    r"before (the|its) (table|list|records|first|start)|\[-1\]|\[−1\]|index(ed)? -1|slot -1|\(-1\)|\(−1\)|"
    r"null (far )?pointer|unchecked|range-checked|negative index|unwritten|stray pointer|previous (card|battle)|never assigned|"
    r"reads? (the )?(bytes?|word|record)|keeps the last record|read\b.{0,40}\bwrong|without (a )?check|bounds", re.I)
_H = re.compile(
    r"redraw|screen|palette|mouse|pointer|key\b|keys\b|click|timer|retrace|interrupt|\bfiles?\b|\bDOS\b|clock|time\(0\)|sound|music|"
    r"audio|driver|draws?\b|drawn|rendering|font|glyph|ring buffer|host|menu bar|transition|cursor", re.I)
_N_WEAK = re.compile(r"left out|belongs? to|is the (screen|presentation|movement|host)|the (screen|host|caller|presentation)'s|not (kept|modelled)|skipped", re.I)


def heuristic_class(note: str) -> str:
    if _F.search(note):
        return "deliberate-fix"
    if _N_STRONG.search(note):
        return "not-modelled"
    if _U.search(note):
        return "undefined-memory"
    if _H.search(note):
        return "host"
    if _N_WEAK.search(note):
        return "not-modelled"
    return "other"


# --------------------------------------------------------------------------- original-function resolution

class Index:
    """docs/source-map/original-functions.csv: a coordinate (RNNN+0xPPPP, root SSSS:OOOO) resolves to the
    entry at that coordinate, else to the function (or thunk) that contains it."""

    def __init__(self, root: Path):
        self.names, rows = scan.load_index(root)
        self.by_image = defaultdict(list)  # record -> sorted [(payload, coordinate)]
        self.root_by_seg = defaultdict(list)  # root (and thunk) segment -> sorted [(offset, coordinate)]
        for r in rows:
            m = re.fullmatch(r"(R\d{3})\+0x([0-9A-F]+)", r["coordinate"])
            if m:
                self.by_image[m.group(1)].append((int(m.group(2), 16), r["coordinate"]))
            else:
                seg, off = r["linked"].split(":")
                self.root_by_seg[seg.upper()].append((int(off, 16), r["coordinate"]))
        for lst in list(self.by_image.values()) + list(self.root_by_seg.values()):
            lst.sort()

    def resolve(self, ref: str):
        if ref in self.names:
            return ref
        m = re.fullmatch(r"root ([0-9A-F]{4}):([0-9A-F]{4})", ref)
        if m:
            lst = self.root_by_seg.get(m.group(1))
            if lst:
                off = int(m.group(2), 16)
                i = bisect.bisect_right(lst, (off, "￿")) - 1
                if i >= 0:
                    return lst[i][1]
            return None
        m = re.fullmatch(r"R(\d{3})\+0x([0-9A-F]+)", ref)
        if m:
            lst = self.by_image.get("R" + m.group(1))
            if lst:
                off = int(m.group(2), 16)
                i = bisect.bisect_right(lst, (off, "￿")) - 1
                if i >= 0:
                    return lst[i][1]
        return None

    def status(self, coordinate: str) -> str:
        r = self.names[coordinate]
        return r["status"] or r["kind"]


# --------------------------------------------------------------------------- the register

class Entry:
    pass


def make_id(m: scan.Marker, taken: set) -> str:
    h = hashlib.sha1(f"{m.file}|{m.kind}|{m.note[:100]}".encode("utf-8")).hexdigest()[:6]
    i = f"{PREFIX[m.kind]}-{h}"
    n = 2
    base = i
    while i in taken:
        i = f"{base}-{n}"
        n += 1
    taken.add(i)
    return i


def read_csv(path: Path):
    if not path.exists():
        return []
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def build(root: Path = ROOT):
    index = Index(root)
    flags = scan.load_flags(root)
    markers = scan.scan_tree(root)
    overrides = {r["id"]: r for r in read_csv(HERE / "overrides.csv")}
    taken: set = set()
    texts = {}
    entries = []
    for m in markers:
        e = Entry()
        e.m = m
        e.kind = m.kind
        e.id = make_id(m, taken)
        e.file, e.line = m.file, m.line
        e.type, e.member = m.types, m.member
        e.note = m.note
        e.form = "start" if m.start_form else "inline"
        e.qualifier = m.qualifier
        e.area = area_of(m.file)
        e.cls = ""
        ov = overrides.get(e.id)
        if m.kind == "DIVERGENCE":
            e.cls = heuristic_class(m.note)
        if ov:
            if ov.get("class"):
                e.cls = ov["class"]
            if ov.get("area"):
                e.area = ov["area"]
        e.core = e.area in CORE_AREAS
        resolved = []
        e.refs, e.orig_where, e.orig_line = m.orig_refs, m.orig_where, m.orig_line
        for where, oline, refs, text in m.candidates:
            res = []
            for ref in refs:
                r = index.resolve(ref)
                if r and r not in res:
                    res.append(r)
            if res:
                resolved = res
                e.refs, e.orig_where, e.orig_line = refs, where, oline
                break
        e.resolved = resolved
        # BUG fix flags
        e.flags = []
        if m.kind == "BUG":
            if m.file not in texts:
                texts[m.file] = (root / m.file).read_text(encoding="utf-8-sig").splitlines()
            lines = texts[m.file]
            lo = max(0, m.line - 7)
            hi = m.scope_end if m.scope_end and m.scope_end >= m.line else m.line + 40
            window = "\n".join(lines[lo:hi])
            found = sorted(set(re.findall(r"\b(Fix[A-Z]\w+)\b", window)) & flags)
            e.flags = found
        entries.append(e)
    # BUG: the flag nearest after the marker wins when several flags sit in the window
    return index, flags, entries


# --------------------------------------------------------------------------- output helpers

def csv_text(header, rows):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\n")
    w.writerow(header)
    for r in rows:
        w.writerow(r)
    return buf.getvalue()


def orig_cells(e: Entry, index: Index):
    names = e.resolved
    first = names[:6]
    coords = [index.names[n]["linked"] for n in first]
    status = [index.status(n) for n in first]
    more = f" (+{len(names) - 6} more)" if len(names) > 6 else ""
    return ";".join(first) + more.replace(" ", ""), ";".join(coords), ";".join(status)


def loc(e: Entry) -> str:
    t = e.type + ("." if e.type and e.member else "") + e.member
    return t or "(file level)"


def sort_key(e: Entry):
    return (AREAS.index(e.area), e.file, e.line)


def orig_md(e: Entry, index: Index) -> str:
    if not e.resolved:
        refs = ", ".join(f"`{r}`" for r in e.refs[:3]) or "none"
        return f"original: unresolved ({refs})"
    shown = [f"`{n}` ({index.names[n]['linked']}, {index.status(n)})" for n in e.resolved[:3]]
    more = f" +{len(e.resolved) - 3} more" if len(e.resolved) > 3 else ""
    return "original: " + ", ".join(shown) + more


def entry_md(e: Entry, index: Index, extra: str = "") -> str:
    tag = e.cls if e.cls else e.kind.lower()
    head = f"- **{e.id}** `{tag}`{extra} - `{e.file}:{e.line}` `{loc(e)}`  \n  {orig_md(e, index)}  \n  why: {e.note}\n"
    return head


def read_intro(name: str) -> str:
    p = HERE / "intros" / name
    return p.read_text(encoding="utf-8").rstrip("\n") + "\n\n" if p.exists() else ""


def table(counter_rows, header):
    out = ["| " + " | ".join(header) + " |", "|" + "|".join("---" for _ in header) + "|"]
    for r in counter_rows:
        out.append("| " + " | ".join(str(c) for c in r) + " |")
    return "\n".join(out) + "\n\n"


def grouped_sections(entries, index, by, order, title_fmt, extra=None):
    """Core areas first, then presentation/host; within an area by `by`."""
    out = []
    core = [e for e in entries if e.core]
    rest = [e for e in entries if not e.core]
    for heading, group in (("Core logic", core), ("Presentation and host", rest)):
        if not group:
            continue
        out.append(f"## {heading} ({len(group)})\n\n")
        for area in AREAS:
            ae = [e for e in group if e.area == area]
            if not ae:
                continue
            out.append(f"### {area} ({len(ae)})\n\n")
            if by:
                for c in order:
                    ce = [e for e in ae if by(e) == c]
                    if not ce:
                        continue
                    out.append(f"#### {title_fmt(c)} ({len(ce)})\n\n")
                    for e in sorted(ce, key=sort_key):
                        out.append(entry_md(e, index, extra(e) if extra else ""))
                    out.append("\n")
            else:
                for e in sorted(ae, key=sort_key):
                    out.append(entry_md(e, index, extra(e) if extra else ""))
                out.append("\n")
    return "".join(out)


# --------------------------------------------------------------------------- documents

def gen_divergences(entries, index):
    divs = [e for e in entries if e.kind == "DIVERGENCE"]
    out = ["# Divergences from the original\n\n", "<!-- generated by tools/faithfulness/registers.py; do not edit by hand -->\n\n"]
    out.append(read_intro("divergences.md"))
    rows = []
    for a in AREAS:
        rows.append([a, "core" if a in CORE_AREAS else "other"] + [sum(1 for e in divs if e.area == a and e.cls == c) for c in DIV_CLASSES] + [sum(1 for e in divs if e.area == a)])
    rows.append(["total", ""] + [sum(1 for e in divs if e.cls == c) for c in DIV_CLASSES] + [len(divs)])
    out.append(table(rows, ["area", "kind"] + DIV_CLASSES + ["all"]))
    out.append(grouped_sections(divs, index, lambda e: e.cls, DIV_CLASSES, lambda c: c))
    return "".join(out)


def bug_fix_text(e):
    return ", flag " + ", ".join(f"`{f}`" for f in e.flags) if e.flags else ", no fix flag"


def gen_bugs(entries, index, flags):
    bugs = [e for e in entries if e.kind == "BUG"]
    out = ["# Original bugs reproduced\n\n", "<!-- generated by tools/faithfulness/registers.py; do not edit by hand -->\n\n"]
    out.append(read_intro("bugs.md"))
    rows = []
    for a in AREAS:
        n = [e for e in bugs if e.area == a]
        if n:
            rows.append([a, len(n), sum(1 for e in n if e.flags), sum(1 for e in n if not e.flags)])
    rows.append(["total", len(bugs), sum(1 for e in bugs if e.flags), sum(1 for e in bugs if not e.flags)])
    out.append(table(rows, ["area", "bugs", "with a fix flag", "without"]))
    out.append(grouped_sections(bugs, index, None, None, None, bug_fix_text))
    used = {f for e in bugs for f in e.flags}
    unused = sorted(flags - used)
    out.append("## Fix flags without a BUG marker nearby\n\n")
    out.append("`OriginalBehaviour` flags that no `// BUG:` marker above refers to (the quirk is documented in `OriginalBehaviour.cs` or at the flag's use):\n\n")
    for f in unused:
        out.append(f"- `{f}`\n")
    out.append("\n")
    return "".join(out)


def gen_uncertain(entries, index):
    unc = [e for e in entries if e.kind == "UNCERTAIN"]
    out = ["# Uncertain readings\n\n", "<!-- generated by tools/faithfulness/registers.py; do not edit by hand -->\n\n"]
    out.append(read_intro("uncertain.md"))
    rows = [[a, "core" if a in CORE_AREAS else "other", sum(1 for e in unc if e.area == a)] for a in AREAS]
    rows.append(["total", "", len(unc)])
    out.append(table(rows, ["area", "kind", "uncertain"]))
    out.append(grouped_sections(unc, index, None, None, None))
    return "".join(out)


def gen_review(entries, index, notes):
    divs = [e for e in entries if e.kind == "DIVERGENCE" and e.core]
    out = ["# Divergence review list\n\n", "<!-- generated by tools/faithfulness/registers.py from review_notes.csv; do not edit by hand -->\n\n"]
    out.append(read_intro("review.md"))
    a_items = [e for e in divs if e.cls in REVIEW_CLASSES]
    b_items = [e for e in divs if e.cls == "undefined-memory"]
    missing = [e for e in a_items + b_items if e.id not in notes]

    def row_counts(items, key):
        c = Counter(notes[e.id][key] if e.id in notes else "(none)" for e in items)
        return ", ".join(f"{k}: {v}" for k, v in sorted(c.items()))

    out.append("## Summary\n\n")
    out.append(f"- Part A (not-modelled, deliberate-fix, other in core logic): {len(a_items)} entries; recommendations: {row_counts(a_items, 'action')}.\n")
    out.append(f"- Part B (undefined-memory in core logic): {len(b_items)} entries; determinism: {row_counts(b_items, 'determinism')}.\n")
    stale = [e for e in divs if notes.get(e.id, {}).get("stale")]
    if stale:
        out.append(f"- Stale markers (the divergence is gone, the marker still here): {len(stale)}.\n")
    out.append(f"- Entries without a recommendation yet: {len(missing)}.\n\n")
    if stale:
        out.append("## Stale markers\n\n")
        out.append("Markers whose divergence is gone; remove or update them.\n\n")
        for e in sorted(stale, key=sort_key):
            out.append(f"- **{e.id}** `{e.file}:{e.line}` `{loc(e)}` - {notes[e.id]['stale']}\n")
        out.append("\n")

    def note_md(e):
        n = notes.get(e.id)
        s = f"- **{e.id}** `{e.cls}` - `{e.file}:{e.line}` `{loc(e)}`  \n  {orig_md(e, index)}  \n  divergence: {e.note}  \n"
        if n and n.get("done"):
            s += f"  **done**: {n['done']}\n"
        elif n and n.get("stale"):
            s += f"  **stale marker - remove/update** (`{e.file}:{e.line}`; {n['stale']}): {n['analysis']}\n"
        elif n:
            if e.cls in REVIEW_CLASSES:
                s += f"  recommendation: **{n['action']}**{' (checked against the original)' if n['verified'] == 'yes' else ''} - {n['analysis']}\n"
            else:
                s += f"  deterministic: **{n['determinism']}**{' (checked against the original)' if n['verified'] == 'yes' else ''}; action: **{n['action']}** - {n['analysis']}\n"
        else:
            s += "  recommendation: **NEEDS REVIEW**\n"
        return s

    work = [e for e in a_items + b_items if e.id in notes and not notes[e.id].get("stale") and not notes[e.id].get("done")]
    out.append("## Work list\n\n")
    out.append("Entries to be made faithful: first the deterministic undefined-memory ones (`reproduce`, `reproduce-later`), then the unimplemented features (`implement`, `implement-later`). Each line gives the C# location, the original function and what to do.\n\n")
    for title in ("reproduce", "reproduce-later", "implement", "implement-later"):
        ws = sorted([e for e in work if notes[e.id]["action"] == title], key=sort_key)
        out.append(f"### {title} ({len(ws)})\n\n")
        for e in ws:
            names = ", ".join(f"`{n}`" for n in e.resolved[:3]) or "(none)"
            out.append(f"- **{e.id}** `{e.file}:{e.line}` `{loc(e)}` - original {names} ({e.area}) - {notes[e.id]['analysis']}\n")
        out.append("\n")

    done = sorted((k for k, n in notes.items() if n.get("done")))
    out.append(f"## Done ({len(done)})\n\n")
    out.append("Entries made faithful (the marker was replaced by the original behaviour, or reworded where a part stays out of reach): what changed, then the review's analysis.\n\n")
    for k in done:
        out.append(f"- **{k}** (was `{notes[k]['action']}`) - {notes[k]['done']}  \n  analysis: {notes[k]['analysis']}\n")
    out.append("\n")

    out.append("## Part A. Threats to 100% faithfulness (not-modelled, deliberate-fix, other)\n\n")
    out.append("Each is a core-logic divergence where the C# does something the original does not. Action: `implement` (make it as the original), `justify` (keep, with the reason given), `implement-later` (blocked on a missing subsystem), `remove-marker` (the divergence is gone; the marker is to be removed).\n\n")
    for area in CORE_AREAS:
        ae = [e for e in a_items if e.area == area]
        if not ae:
            continue
        out.append(f"### {area} ({len(ae)})\n\n")
        for c in REVIEW_CLASSES:
            ce = sorted([e for e in ae if e.cls == c], key=sort_key)
            if ce:
                out.append(f"#### {c} ({len(ce)})\n\n")
                out.extend(note_md(e) for e in ce)
                out.append("\n")
    out.append("## Part B. Undefined memory: is the original outcome deterministic?\n\n")
    out.append("Where the original reads or writes outside its defined data, the outcome can still be a fixed function of the program state (a table that follows in the data image, a known stack leftover, a global the previous card left), in which case it can be reproduced exactly. `deterministic`: `yes` (a fixed value or a pure function of modelled state), `partly` (fixed in the shipped data or reachable states, unreproducible elsewhere), `no` (depends on unmodelled memory such as heap addresses or machine state), `n/a` (not reachable in play).\n\n")
    for area in CORE_AREAS:
        ae = [e for e in b_items if e.area == area]
        if not ae:
            continue
        out.append(f"### {area} ({len(ae)})\n\n")
        for e in sorted(ae, key=sort_key):
            out.append(note_md(e))
        out.append("\n")
    return "".join(out), missing


def gen_csvs(entries, index):
    files = {}
    h = ["id", "class", "area", "core", "file", "line", "type", "member", "original", "original_coordinates", "original_status", "original_comment_line", "form", "note"]
    divs = []
    for e in sorted((e for e in entries if e.kind == "DIVERGENCE"), key=sort_key):
        o, c, s = orig_cells(e, index)
        divs.append([e.id, e.cls, e.area, "core" if e.core else "other", e.file, e.line, e.type, e.member, o, c, s, e.orig_line, e.form, e.note])
    files["divergences.csv"] = csv_text(h, divs)
    hb = ["id", "area", "core", "fix_flag", "file", "line", "type", "member", "original", "original_coordinates", "original_status", "original_comment_line", "form", "note"]
    bugs = []
    for e in sorted((e for e in entries if e.kind == "BUG"), key=sort_key):
        o, c, s = orig_cells(e, index)
        bugs.append([e.id, e.area, "core" if e.core else "other", ";".join(e.flags) if e.flags else "none", e.file, e.line, e.type, e.member, o, c, s, e.orig_line, e.form, e.note])
    files["original_bugs.csv"] = csv_text(hb, bugs)
    hu = ["id", "area", "core", "file", "line", "type", "member", "original", "original_coordinates", "original_status", "original_comment_line", "form", "note"]
    for kind, name in (("UNCERTAIN", "uncertain.csv"), ("DORMANT", "dormant.csv")):
        rows = []
        for e in sorted((e for e in entries if e.kind == kind), key=sort_key):
            o, c, s = orig_cells(e, index)
            rows.append([e.id, e.area, "core" if e.core else "other", e.file, e.line, e.type, e.member, o, c, s, e.orig_line, e.form, e.note])
        files[name] = csv_text(hu, rows)
    return files


def generate(root: Path = ROOT):
    index, flags, entries = build(root)
    notes = {r["id"]: r for r in read_csv(HERE / "review_notes.csv")}
    files = {}
    files["DIVERGENCES.md"] = gen_divergences(entries, index)
    files["ORIGINAL_BUGS.md"] = gen_bugs(entries, index, flags)
    files["UNCERTAIN.md"] = gen_uncertain(entries, index)
    review, missing = gen_review(entries, index, notes)
    files["DIVERGENCE_REVIEW.md"] = review
    files.update(gen_csvs(entries, index))
    ids = {e.id for e in entries}
    done_ids = {k for k, n in notes.items() if n.get("done")}
    stale = sorted(i for i in [k for k in notes if k not in done_ids] + [r["id"] for r in read_csv(HERE / "overrides.csv") if r["id"] not in done_ids] if i not in ids)
    return files, entries, index, missing, stale


def stats(entries):
    print("markers:", dict(Counter(e.kind for e in entries)))
    divs = [e for e in entries if e.kind == "DIVERGENCE"]
    print("divergences by class:", dict(Counter(e.cls for e in divs)))
    print("divergences by area:", dict(Counter(e.area for e in divs)))
    t = defaultdict(Counter)
    for e in divs:
        t[e.area][e.cls] += 1
    for a in AREAS:
        print(f"  {a:22}", dict(t[a]))
    bugs = [e for e in entries if e.kind == "BUG"]
    print("bugs with flag:", sum(1 for e in bugs if e.flags), "without:", sum(1 for e in bugs if not e.flags))
    unres = [e for e in entries if e.kind in ("DIVERGENCE", "BUG") and not e.resolved]
    print("DIVERGENCE/BUG without a resolvable original:", len(unres))
    for e in unres:
        print("   ", e.id, e.file, e.line, e.refs[:2], e.orig_where)


def main(argv):
    files, entries, index, missing, stale = generate()
    if "--stats" in argv:
        stats(entries)
        print("review entries without a recommendation:", len(missing), " stale ids:", stale)
        return 0
    OUT.mkdir(parents=True, exist_ok=True)
    if "--check" in argv:
        bad = []
        for name, text in files.items():
            p = OUT / name
            cur = p.read_bytes().decode("utf-8").replace("\r\n", "\n") if p.exists() else None
            if cur != text:
                bad.append(name)
        if bad or stale:
            print("faithfulness registers are out of date: " + ", ".join(bad) + (f"; stale ids in overrides/review notes: {stale}" if stale else ""))
            print("run: python tools/faithfulness/registers.py")
            return 1
        print("faithfulness registers are up to date")
        return 0
    for name, text in files.items():
        (OUT / name).write_bytes(text.encode("utf-8"))
    print(f"wrote {len(files)} files to {OUT}")
    if missing:
        print(f"review entries without a recommendation: {len(missing)}")
    if stale:
        print("stale ids:", stale)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
