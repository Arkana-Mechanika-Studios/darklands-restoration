"""C# scanner for the faithfulness markers (DIVERGENCE / BUG / UNCERTAIN / DORMANT).

Reads every src/**/*.cs file, finds each marker in a comment, its note (the marker comment and its
continuation lines), the C# location (file:line, type, member) and the original function(s) named by
the nearest `// orig:` comment.  No classification here; see registers.py.
"""
from __future__ import annotations

import csv
import re
from dataclasses import dataclass, field
from pathlib import Path

KINDS = ("DIVERGENCE", "BUG", "UNCERTAIN", "DORMANT")
# A marker: the keyword at the start of a comment (also "DIVERGENCE (generated):", "DIVERGENCE (from the draft):"),
# or "KEYWORD:" inside a comment line after some text.
START_RE = re.compile(r"^(?P<k>DIVERGENCE|BUG|UNCERTAIN|DORMANT)\b(?P<q>\s*\([^)]*\))?\s*:?\s*(?P<rest>.*)$")
INLINE_RE = re.compile(r"\b(?P<k>DIVERGENCE|BUG|UNCERTAIN|DORMANT)\b(?P<q>\s*\([^)]*\))?:\s*(?P<rest>.*)$")
ORIG_RE = re.compile(r"^orig\b")
# comment lines that end a note
STOP_RE = re.compile(r"^(orig\b|C:|DIVERGENCE\b|BUG\b|UNCERTAIN\b|DORMANT\b)")

REF_ANY = re.compile(r"\bR\d{3}\+0x[0-9A-Fa-f]+\b|\broot\s+[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\b")
REF_ROOT = re.compile(r"\broot\s+([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\b")
REF_OVL = re.compile(r"\bR(\d{3})\+0x([0-9A-Fa-f]+)\b")
CONTROL = ("if", "else", "for", "foreach", "while", "do", "switch", "try", "catch", "finally", "lock", "using",
           "fixed", "checked", "unchecked", "unsafe", "get", "set", "init", "add", "remove", "when")
TYPE_RE = re.compile(r"\b(class|struct|record|interface|enum)\b\s+(?:(?:struct|class)\s+)?([A-Za-z_]\w*)")


@dataclass
class Scope:
    kind: str  # ns | type | member | block
    name: str
    start: int
    end: int = 0


@dataclass
class Marker:
    kind: str
    qualifier: str
    file: str
    line: int
    note: str
    types: str = ""
    member: str = ""
    orig_refs: list = field(default_factory=list)
    orig_where: str = "none"
    orig_line: int = 0
    orig_text: str = ""
    start_form: bool = True
    scope_start: int = 0
    scope_end: int = 0
    candidates: list = field(default_factory=list)  # [(where, line, refs, text)] nearest first: above, then below


def tokenize(text: str):
    """Returns (lines, comments, events): lines[i] = code text with strings and comments blanked;
    comments[i] = (col, text) of the comment on line i or None; events = (line, col, 'open'|'close', header)."""
    n = len(text)
    i = 0
    line = 0
    col0 = 0
    code_lines: list[list[str]] = [[]]
    comments: list = [None]
    events = []
    header: list[str] = []

    def newline():
        code_lines.append([])
        comments.append(None)

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "\n":
            line += 1
            col0 = i + 1
            newline()
            header.append(" ")
            i += 1
            continue
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            comments[line] = (i - col0, text[i:j].rstrip("\r"))
            i = j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n - 2 if j < 0 else j
            seg = text[i:j + 2]
            for ch in seg:
                if ch == "\n":
                    line += 1
                    newline()
            col0 = i  # approximate
            i = j + 2
            continue
        if c == '"':
            k = i - 1
            verbatim = False
            while k >= 0 and text[k] in "@$":
                if text[k] == "@":
                    verbatim = True
                k -= 1
            if text.startswith('"""', i):
                j = text.find('"""', i + 3)
                j = n - 3 if j < 0 else j
                seg = text[i:j + 3]
                i = j + 3
            else:
                j = i + 1
                while j < n:
                    if verbatim:
                        if text[j] == '"' and j + 1 < n and text[j + 1] == '"':
                            j += 2
                            continue
                        if text[j] == '"':
                            break
                        j += 1
                    else:
                        if text[j] == "\\":
                            j += 2
                            continue
                        if text[j] == '"' or text[j] == "\n":
                            break
                        j += 1
                seg = text[i:j + 1]
                i = j + 1
            for ch in seg:
                if ch == "\n":
                    line += 1
                    newline()
            code_lines[line].append('""')
            header.append('""')
            continue
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                if text[j] == "\\":
                    j += 1
                j += 1
            code_lines[line].append("'x'")
            header.append("'x'")
            i = j + 1
            continue
        if c == "{":
            events.append((line, i - col0, "open", " ".join("".join(header).split())))
            header = []
        elif c == "}":
            events.append((line, i - col0, "close", ""))
            header = []
        elif c == ";":
            header = []
        else:
            header.append(c)
        code_lines[line].append(c)
        i += 1
    return ["".join(l) for l in code_lines], comments, events


def classify_header(h: str):
    h = re.sub(r"\[[^\]]*\]", " ", h)
    h = " ".join(h.split())
    if not h:
        return ("block", "")
    m = re.match(r"^(?:[\w\s]*\s)?namespace\s+([\w.]+)$", h)
    if m:
        return ("ns", m.group(1))
    first = re.match(r"^\w+", h)
    if first and first.group(0) in CONTROL and not re.search(r"\b(class|struct|record|interface|enum)\b", h):
        return ("block", "")
    if h.endswith("=>") or ("=>" in h and "(" not in h.split("=>")[0]):
        return ("block", "")
    t = TYPE_RE.search(h)
    if t and "=" not in h.split(t.group(0))[0] and "(" not in h.split(t.group(0))[0]:
        return ("type", t.group(2))
    if re.search(r"\bnew\b", h) or re.search(r"=(?!=)", h.split("(")[0]) or h.endswith(("switch", "with")):
        return ("block", "")
    if "(" in h:
        before = h.split("(")[0]
        ids = re.findall(r"[A-Za-z_]\w*", before)
        if ids and ids[-1] not in ("if", "for", "while", "switch", "foreach", "lock", "using", "catch", "fixed", "return"):
            name = ids[-1]
            if "operator" in before:
                name = "operator"
            return ("member", name)
        return ("block", "")
    ids = re.findall(r"[A-Za-z_]\w*", h)
    if len(ids) >= 2:
        return ("member", ids[-1])
    return ("block", "")


def next_decl_name(lines: list[str], start: int):
    """Name of the declaration that follows line `start` (0-based, exclusive): (kind, name)."""
    i = start + 1
    buf = ""
    while i < len(lines) and i < start + 40:
        s = lines[i].strip()
        i += 1
        if not s:
            continue
        while s.startswith("["):
            s = re.sub(r"^\[[^\]]*\]\s*", "", s)
        if not s:
            continue
        buf = s
        break
    if not buf:
        return ("", "")
    t = TYPE_RE.search(buf)
    if t and "(" not in buf.split(t.group(0))[0]:
        return ("type", t.group(2))
    cut = re.split(r"\(|=>|=|\{|;|,", buf)[0]
    ids = re.findall(r"[A-Za-z_]\w*", cut)
    if ids:
        return ("member", ids[-1])
    return ("", "")


def load_flags(root: Path) -> set[str]:
    p = root / "src/Darklands.Core/OriginalBehaviour.cs"
    return set(re.findall(r"public bool (Fix\w+)", p.read_text(encoding="utf-8-sig")))


def scan_file(path: Path, root: Path) -> list[Marker]:
    rel = path.relative_to(root).as_posix()
    text = path.read_text(encoding="utf-8-sig")
    code, comments, events = tokenize(text)
    nlines = len(code)

    stack: list[Scope] = []
    ev_by_line: dict[int, list] = {}
    for ln, col, kind, header in events:
        ev_by_line.setdefault(ln, []).append((col, kind, header))
    snap: dict[int, list[Scope]] = {}
    for ln in range(nlines):
        for col, kind, header in ev_by_line.get(ln, []):
            if kind == "open":
                k, name = classify_header(header)
                stack.append(Scope(k, name, ln))
            elif stack:
                stack[-1].end = ln
                stack.pop()
        # state after the line's events; a marker line with code before its comment is rare, comment-only lines exact
        snap[ln] = list(stack)

    def comment_text(ln):
        c = comments[ln]
        if not c:
            return None
        return re.sub(r"^//+\s?", "", c[1])

    orig_blocks = []  # (line, refs, text)
    ln = 0
    while ln < nlines:
        t = comment_text(ln)
        if t is not None and ORIG_RE.match(t.strip()):
            parts = [t.strip()]
            j = ln + 1
            while j < nlines and comments[j] and not code[j].strip():
                tj = comment_text(j) or ""
                raw = comments[j][1]
                if raw.startswith("///") != comments[ln][1].startswith("///"):
                    break
                if not re.match(r"^\s{2,}\S", tj) and not REF_ANY.search(tj):
                    break
                if STOP_RE.match(tj.strip()):
                    break
                parts.append(tj.strip())
                j += 1
            full = " ".join(parts)
            refs = [f"root {a.upper()}:{b.upper()}" for a, b in REF_ROOT.findall(full)]
            refs += [f"R{a}+0x{b.upper()}" for a, b in REF_OVL.findall(full)]
            seen = []
            for r in refs:
                if r not in seen:
                    seen.append(r)
            orig_blocks.append((ln, seen, full))
            ln = max(j, ln + 1)
        else:
            ln += 1

    markers: list[Marker] = []
    ln = 0
    while ln < nlines:
        t = comment_text(ln)
        if t is None:
            ln += 1
            continue
        ts = t.strip()
        m = START_RE.match(ts)
        start_form = True
        if not m:
            m = INLINE_RE.search(ts)
            start_form = False
        if not m:
            ln += 1
            continue
        kind = m.group("k")
        qual = (m.group("q") or "").strip()
        parts = [m.group("rest").strip()]
        j = ln + 1
        is_doc = comments[ln][1].startswith("///")
        while j < nlines and comments[j] and not code[j].strip() and comments[j][1].startswith("///") == is_doc:
            tj = comment_text(j).strip()
            if not tj or STOP_RE.match(tj) or INLINE_RE.search(tj):
                break
            if is_doc and tj.startswith("</"):
                break
            parts.append(tj)
            j += 1
        note = " ".join(p for p in parts if p).strip()
        note = re.sub(r"</?(?:c|em|see|paramref|summary|remarks|para)[^>]*>", "", note)
        note = re.sub(r"\s+", " ", note)
        st = snap.get(ln, [])
        types = [s.name for s in st if s.kind == "type"]
        members = [s for s in st if s.kind == "member"]
        member = members[0].name if members else ""
        sc_start = members[0].start + 1 if members else 0
        sc_end = (members[0].end + 1) if members and members[0].end else 0
        if not members:
            k2, nm = next_decl_name(code, j - 1 if j > ln else ln)
            if k2 == "type":
                types = types + [nm]
            elif k2 == "member":
                member = nm
        mk = Marker(kind, qual, rel, ln + 1, note, ".".join(types), member, start_form=start_form,
                    scope_start=sc_start, scope_end=sc_end)
        above = [b for b in orig_blocks if b[0] <= ln]
        below = [b for b in orig_blocks if b[0] > ln]
        if above:
            b = above[-1]
            mk.orig_where = "above"
        elif below:
            b = below[0]
            mk.orig_where = "below"
        else:
            b = None
        if b:
            mk.orig_refs = b[1]
            mk.orig_line = b[0] + 1
            mk.orig_text = b[2]
        mk.candidates = [("above", b[0] + 1, b[1], b[2]) for b in reversed(above)] +                         [("below", b[0] + 1, b[1], b[2]) for b in below[:3]]
        markers.append(mk)
        ln = max(j, ln + 1)
    return markers


def scan_tree(root: Path) -> list[Marker]:
    out: list[Marker] = []
    for p in sorted((root / "src").rglob("*.cs")):
        parts = p.relative_to(root).parts
        if "obj" in parts or "bin" in parts:
            continue
        out.extend(scan_file(p, root))
    return out


def load_index(root: Path):
    names = {}
    rows = []
    with open(root / "docs/source-map/original-functions.csv", newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            names[r["coordinate"]] = r
            if r["kind"] in ("function", "thunk") or (r["kind"] == "address" and "stub slot" in r["note"]):
                rows.append(r)
    return names, rows


if __name__ == "__main__":
    import sys
    from collections import Counter
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    ms = scan_tree(root)
    print(Counter(m.kind for m in ms))
    print(Counter((m.kind, m.start_form) for m in ms))
    print("no orig:", Counter(m.kind for m in ms if m.orig_where == "none"), "below:", Counter(m.kind for m in ms if m.orig_where == "below"))
