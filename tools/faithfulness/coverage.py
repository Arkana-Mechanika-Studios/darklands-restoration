#!/usr/bin/env python3
"""Faithfulness coverage: is every piece of the original's logic in the C#?

Builds the set of original functions the game can reach and classifies every original
function (every `function` row of docs/source-map/original-functions.csv, keyed by its
coordinate) as

  implemented          the C# cites it with a trace (its entry coordinate RNNN+0xPPPP / root SSSS:OOOO
                       in src/ or in docs/source-map/map.csv; an RTLink thunk `thunk 09C0:XXXX` or a
                       readable name of the function on an `orig:`/`DORMANT`/... line), or it is a
                       byte-identical clone of a cited function whose callers are all implemented
                       (`via` says which: `clone:<coordinate>`)
  excluded:<reason>    a reviewed entry of docs/faithfulness/exclusions.csv covers it (C run-time
                       library, RTLink, drivers, ...: each row says why it is not game logic or how
                       the C# covers its effect); `excluded:unreferenced` = nothing reaches it
  MISSING              reachable from the game's entry points and neither of the above

The reachable set starts at the real entry points and follows every edge the static analysis can
see:

  * the MZ entry point (RTLink start -> C start-up -> main -> the main loop root 03A1),
  * every RTLink overlay thunk (the 753 thunks, target from the original index),
  * every far function pointer stored in data (relocated seg:off pairs outside decoded code:
    the card table R136+0xD090, the saint functions R136+0xAFC7, every other table), grouped
    into tables and named after the data entry they start at,
  * call edges (near and far calls, thunk calls resolved to their targets),
  * code pointer edges: a far pointer to a function or thunk built in code (relocated segment
    immediate next to an offset immediate: option/item/saint handler tables filled by the card
    scripts, expiry handlers, qsort comparators, interrupt and driver hooks).

Also flagged: functions the C# cites only partially (an implemented function with a callee that
is MISSING).

Outputs docs/faithfulness/coverage.csv and docs/faithfulness/COVERAGE.md. The test
tests/Darklands.Core.Tests/FaithfulnessCoverageTests.cs re-derives the cited coordinates from src/ at
test time and compares the reachable set in coverage.csv with docs/faithfulness/missing-allowlist.txt.

  python -B tools/faithfulness/coverage.py [--reference PATH] [--exe PATH/DARKLAND.EXE] [--write-allowlist]

The static analysis (decoded instructions, call graph, relocations) comes from the reference
decompilation's analysis through the machine-local adapter local/tools/reference_program.py and the
mapping local/reference-map.csv (both git-ignored; see docs/source-map/README.md): the tool exits
with a message when they are absent. Inputs (read-only): DARKLAND.EXE, this repository's src/,
docs/source-map/*.csv, docs/faithfulness/exclusions.csv and not-ported.csv.
"""
import argparse, collections, csv, fnmatch, importlib.util, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.dirname(HERE))
import local_paths  # noqa: E402
OUT_DIR = os.path.join(REPO, 'docs', 'faithfulness')
ADAPTER = os.path.join(local_paths.local_dir(REPO), 'tools', 'reference_program.py')
MAPPING = os.path.join(local_paths.local_dir(REPO), 'reference-map.csv')
DATA_IMAGE = 'R136'
# Phrases that make a comment line a mention, not a trace: the line says the function is not ported.
# FaithfulnessCoverageTests uses the same list.
NEGATIVE = ('not written', 'not yet', 'not ported', 'not implemented', 'not reproduced', 'not modelled', 'not modeled', 'not needed')
COORD_RE = re.compile(r'\bR(\d{3})\+0x([0-9A-Fa-f]{1,5})\b')
ROOTC_RE = re.compile(r'\broot ([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\b')
THUNK_RE = re.compile(r'\bthunk 09C0:([0-9A-Fa-f]{4})\b')
IDENT_RE = re.compile(r'\b([A-Za-z_][A-Za-z0-9_]{3,})\b')
TRACE = ('orig', 'DORMANT', 'DIVERGENCE', 'C:', 'BUG:', 'UNCERTAIN:')

AREAS = [  # (record predicate, area) for the MISSING report, first match wins
    (lambda i: i == 'root', 'resident core (root)'),
    (lambda i: i in ('R000', 'R001'), 'start-up'),
    (lambda i: i in ('R002', 'R003') or 'R014' <= i <= 'R025', 'battles (tactical)'),
    (lambda i: 'R004' <= i <= 'R013', 'battlefield generation'),
    (lambda i: 'R026' <= i <= 'R028', 'heap / swap'),
    (lambda i: i in ('R029', 'R039'), 'rules (strategic core, event toolkit)'),
    (lambda i: i == 'R030', 'travel (overland map)'),
    (lambda i: 'R031' <= i <= 'R038' or i in ('R040', 'R041', 'R042'), 'screens (inventory, barter, alchemy, saints, cache, camp, creation, saves, loot)'),
    (lambda i: i == 'R043', 'card engine'),
    (lambda i: 'R044' <= i <= 'R135', 'cards (event scripts)'),
]


def record_of(coordinate):
    return 'root' if coordinate.startswith('root') else coordinate[:4]


def payload_of(coordinate):
    return '' if coordinate.startswith('root') else coordinate[5:]


def area_of(record):
    for pred, name in AREAS:
        if pred(record):
            return name
    return 'other'


def exclusion_key(coordinate, linked):
    """What exclusions.csv patterns match: `root SSSS:OOOO`, or `RNNN+0xPPPP (SSSS:OOOO)` (an exact
    coordinate matches too)."""
    return coordinate if coordinate.startswith('root') else '%s (%s)' % (coordinate, linked)


# ---------------------------------------------------------------------------------- inputs
def load_index():
    path = os.path.join(REPO, 'docs', 'source-map', 'original-functions.csv')
    with open(path, newline='', encoding='utf-8') as fh:
        rows = list(csv.DictReader(fh))
    funcs = {r['coordinate']: r for r in rows if r['kind'] == 'function'}
    thunks = {r['coordinate']: r['target'].split(' (')[0] for r in rows if r['kind'] == 'thunk'}
    data = {r['coordinate']: r for r in rows if r['kind'] == 'data'}
    return funcs, thunks, data


def csharp_sources():
    for top in ('src',):
        for dp, dn, fn in os.walk(os.path.join(REPO, top)):
            dn[:] = [d for d in dn if d not in ('obj', 'bin')]
            for f in fn:
                if f.endswith('.cs'):
                    p = os.path.join(dp, f)
                    with open(p, encoding='utf-8') as fh:
                        yield os.path.relpath(p, REPO).replace('\\', '/'), fh.read()


def cited_functions(funcs, thunks, aliases, crt_names):
    """original function -> set of C# files / map rows citing it."""
    by_frame = {}  # (record, frame, offset) -> coordinate: the bare handler offsets of a comment block
    for co, r in funcs.items():
        seg, off = r['linked'].split(':')
        by_frame[(record_of(co), seg.upper(), off.upper())] = co
    cites = collections.defaultdict(set)
    kind = {}
    mentioned = set()

    def coordinates(line):
        for rec, pay in COORD_RE.findall(line):
            yield 'R%s+0x%04X' % (rec, int(pay, 16))
        for seg, off in ROOTC_RE.findall(line):
            yield 'root %s:%s' % (seg.upper(), off.upper())

    def scan(text, where):
        for line in text.splitlines():
            # a line that says the function is not ported only mentions it (NEGATIVE)
            low = line.lower()
            target = mentioned if any(p in low for p in NEGATIVE) else None

            def add(n, k):
                if target is not None:
                    target.add(n)
                else:
                    cites[n].add(where)
                    kind.setdefault(n, set()).add(k)

            for co in coordinates(line):
                if co in funcs:
                    add(co, 'coordinate')
            if not any(k in line for k in TRACE):
                continue
            for off in THUNK_RE.findall(line):
                t = thunks.get('thunk 09C0:' + off.upper())
                if t in funcs:
                    add(t, 'thunk')
            for ident in IDENT_RE.findall(line):
                t = aliases.get(ident)
                if t in funcs:
                    add(t, 'alias')
            # run-time library names count when written as a call or a C symbol: 'srand(', '_rand'
            for m in re.finditer(r'(?<![\w.])(_?)([a-z_][a-z0-9_]{2,})(\s*\()?', line):
                t = crt_names.get(m.group(2))
                if t in funcs and (m.group(1) or m.group(3)):
                    add(t, 'crt-name')

    def shorthand(text, where):
        # In a comment block that cites a function, a bare 4-digit offset ("0296, 02dc") names the
        # function at that offset of the same link-time frame (the cards' handler lists).
        block = []
        for line in text.splitlines() + ['']:
            if line.lstrip().startswith('//'):
                block.append(line)
                continue
            if block:
                b = '\n'.join(block)
                frames = set()
                for co in coordinates(b):
                    r = funcs.get(co)
                    if r:
                        frames.add((record_of(co), r['linked'].split(':')[0].upper()))
                if frames:
                    # a line that says it is not ported names nothing (NEGATIVE)
                    toks = [t for l in block if not any(p in l.lower() for p in NEGATIVE)
                            for t in re.findall(r'(?<![\w+:.])(?<!seg )([0-9a-fA-F]{4})(?![\w:])', l)]
                    for tok in toks:
                        for rec, frame in frames:
                            n = by_frame.get((rec, frame, tok.upper()))
                            if n:
                                cites[n].add(where)
                                kind.setdefault(n, set()).add('shorthand')
                block = []

    for path, text in csharp_sources():
        scan(text, path)
        shorthand(text, path)
    with open(os.path.join(REPO, 'docs', 'source-map', 'map.csv'), newline='', encoding='utf-8') as fh:
        for row in csv.DictReader(fh):   # the originals a row maps (and its notes), not its own coordinate column
            for co in coordinates(row['original'] + ' ' + row['notes']):
                if co in funcs:
                    cites[co].add('map.csv')
                    kind.setdefault(co, set()).add('coordinate')
    return cites, kind, mentioned - set(cites)


def load_not_ported():
    """Functions the C# names (so they count as cited) whose behaviour is not ported: MISSING."""
    path = os.path.join(OUT_DIR, 'not-ported.csv')
    if not os.path.isfile(path):
        return {}
    with open(path, newline='', encoding='utf-8') as fh:
        return {r['coordinate']: r['why'] for r in csv.DictReader(fh)}


def load_exclusions():
    path = os.path.join(OUT_DIR, 'exclusions.csv')
    with open(path, newline='', encoding='utf-8') as fh:
        rows = [r for r in csv.DictReader(fh) if r['pattern'] and not r['pattern'].startswith('#')]
    for r in rows:
        if not r['reason'] or not r['why']:
            sys.exit('exclusions.csv: %s has no reason or justification' % r['pattern'])
    return rows


def load_program(reference, exe):
    if not (os.path.isfile(ADAPTER) and os.path.isfile(MAPPING)):
        sys.exit('coverage.py needs the machine-local static analysis: %s and %s are missing '
                 '(see docs/source-map/README.md)' % (os.path.relpath(ADAPTER, REPO), os.path.relpath(MAPPING, REPO)))
    spec = importlib.util.spec_from_file_location('reference_program', ADAPTER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.load(reference, exe, MAPPING)


def data_name(prog, img, lin, data_rows):
    """Where a data location is (DS:xxxx in DGROUP, else RNNN SSSS:OOOO) and the nearest far-data entry of
    the original index at or before it in the data image (its coordinate, +0xN when inside)."""
    here, seg, off = prog.where(img, lin)
    if seg is None:
        return here, '', False
    best = None
    if img == DATA_IMAGE:
        for co, r in data_rows.items():
            s, o = (int(x, 16) for x in r['linked'].split(':'))
            if lin - 0x10000 < s * 16 + o <= lin and (best is None or s * 16 + o > best[1]):
                best = (co, s * 16 + o)
    if best:
        return here, best[0] if best[1] == lin else '%s+0x%X' % (best[0], lin - best[1]), best[1] == lin
    return here, '', False


# ---------------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--reference', default=None, help='reference decompilation checkout (default: DARKLANDS_REFERENCE or local/paths.json)')
    ap.add_argument('--exe', default=os.environ.get('DARKLANDS_EXE'), required='DARKLANDS_EXE' not in os.environ)
    ap.add_argument('--write-allowlist', action='store_true',
                    help='rewrite docs/faithfulness/missing-allowlist.txt with the current MISSING set')
    a = ap.parse_args()
    a.reference = a.reference or local_paths.reference()

    funcs, thunks, data_rows = load_index()
    prog = load_program(a.reference, a.exe)
    cites, cite_kind, mentioned_only = cited_functions(funcs, thunks, prog.aliases, prog.crt)
    excl = load_exclusions()
    not_ported = load_not_ported()
    fj = prog.fj
    index_only = prog.add_index_only(funcs)
    code_refs, data_ptrs = prog.edges()

    # ---- graph
    succ = collections.defaultdict(set)
    for n, f in fj.items():
        for c in f['callees']:
            succ[n].add(c)
    for n, ts in code_refs.items():
        succ[n] |= ts
    for n, ts in prog.index_only_calls(funcs, index_only).items():
        succ[n] |= ts
    pred = collections.defaultdict(set)
    for n, ts in succ.items():
        for t in ts:
            pred[t].add(n)

    roots = collections.defaultdict(set)  # fn -> root kinds
    for n, f in fj.items():
        if 'mz_entry' in f['reached_by']:
            roots[n].add('mz_entry')
    roots['root 03A1:000A'].add('main loop')
    unused_vectors = []
    for v, t in sorted(thunks.items()):
        if t in funcs:
            used = int(v[-4:], 16) in prog.vectors_used
            roots[t].add(v + ('' if used else ' (no code or data names it)'))
            if not used:
                unused_vectors.append((v, t))
    tables = collections.defaultdict(list)  # table start -> [(lin, fn)]
    data_ptrs.sort()
    start = None
    prev = None
    for img, lin, t in data_ptrs:
        # a run of pointers 4 bytes apart is one table; a data entry starting here starts a new one
        if prev is None or prev[0] != img or lin - prev[1] > 4 or data_name(prog, img, lin, data_rows)[2]:
            start = (img, lin)
        tables[start].append((lin, t))
        prev = (img, lin)
    table_names = {}
    for (img, lin), ents in tables.items():
        here, sym, _ = data_name(prog, img, lin, data_rows)
        label = 'table %s%s' % (here, (' (' + sym + ')') if sym else '')
        table_names[(img, lin)] = (here, sym, len(ents), sorted({t for _, t in ents}))
        for _, t in ents:
            roots[t].add(label)

    reach = {}  # fn -> (root kind, parent)
    work = collections.deque()
    # sorted, so that the first path found (reached_from) does not depend on the hash seed
    for r, kinds in sorted(roots.items()):
        if r in funcs or r in fj:
            reach[r] = (sorted(kinds)[0], '')
            work.append(r)
    while work:
        n = work.popleft()
        for t in sorted(succ.get(n, ())):
            if t not in reach:
                reach[t] = (reach[n][0], n)
                work.append(t)

    # ---- clones of cited functions
    cls = prog.clone_classes()
    empty = prog.empty_functions() | {n for n, b in index_only.items() if b[0] in (0xCB, 0xC3)}
    cited = {n for n in funcs if cites.get(n)}
    by_cls = collections.defaultdict(list)
    for n in funcs:
        if n in cls:
            by_cls[cls[n]].append(n)
    clone_of = {}
    changed = True
    while changed:
        changed = False
        for n in funcs:
            if n in cited or n in clone_of or n not in cls:
                continue
            twins = [m for m in by_cls[cls[n]] if m != n and (m in cited or m in clone_of)]
            if not twins:
                continue
            callers = [p for p in pred.get(n, ()) if p in funcs]
            if callers and all(p in cited or p in clone_of for p in callers):
                clone_of[n] = clone_of.get(twins[0], twins[0])
                changed = True

    # ---- classify
    def exclusion(n):
        key = exclusion_key(n, funcs[n]['linked'])
        for r in excl:
            if fnmatch.fnmatchcase(key, r['pattern']) or r['pattern'] == n:
                return r
        return None

    def order(co):
        s, o = (int(x, 16) for x in funcs[co]['linked'].split(':'))
        return (record_of(co), s * 16 + o)

    rows = []
    status = {}
    for n in sorted(funcs, key=order):
        r = funcs[n]
        x = exclusion(n)
        if n in not_ported:
            st, via = 'MISSING', 'cited-not-ported'
        elif n in cited:
            st, via = 'implemented', 'cite:' + ('coordinate' if 'coordinate' in cite_kind[n] else sorted(cite_kind[n])[0])
        elif n in empty and (x is None or x['reason'] != 'game-logic'):
            st, via = 'excluded:empty', ''
        elif n in clone_of:
            st, via = 'implemented', 'clone:' + clone_of[n]
        elif x is not None and x['reason'] != 'game-logic':
            st, via = 'excluded:' + x['reason'], x['pattern']
        elif n not in reach:
            st, via = 'excluded:unreferenced', ''
        else:
            st, via = 'MISSING', 'mentioned-not-ported' if n in mentioned_only else ''
        status[n] = st
        rows.append((n, r, st, via))

    partial = {}
    for n, r, st, via in rows:
        if st == 'implemented':
            miss = sorted(t for t in succ.get(n, ()) if status.get(t) == 'MISSING')
            if miss:
                partial[n] = miss

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(os.path.join(OUT_DIR, 'coverage.csv'), 'w', newline='', encoding='utf-8') as fh:
        w = csv.writer(fh, lineterminator='\n')
        w.writerow(['coordinate', 'record', 'payload', 'linked', 'size', 'kind', 'source_status',
                    'reachable', 'reached_from', 'status', 'via', 'callers', 'missing_callees', 'cited_in'])
        for n, r, st, via in rows:
            rk = reach.get(n)
            callers = sorted(p for p in pred.get(n, ()) if p in funcs)
            w.writerow([n, record_of(n), payload_of(n), r['linked'], r['size'],
                        fj.get(n, {}).get('kind', ''), r['status'],
                        'yes' if rk else 'no', (rk[0] + (' <- ' + rk[1] if rk[1] else '')) if rk else '',
                        st, via, ';'.join(callers[:6]) + (';...' if len(callers) > 6 else ''),
                        ';'.join(partial.get(n, [])), ';'.join(sorted(cites.get(n, ()))[:3])])
    if a.write_allowlist:
        with open(os.path.join(OUT_DIR, 'missing-allowlist.txt'), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write('# Reachable original functions that are neither implemented nor excluded (shrinking allow-list).\n'
                     '# Generated by tools/faithfulness/coverage.py --write-allowlist; remove a line when its function is\n'
                     '# implemented (FaithfulnessCoverageTests fails while a listed function is implemented or excluded).\n')
            for n, r, st, via in rows:
                if st == 'MISSING':
                    fh.write(n + '\n')
    write_report(rows, reach, pred, partial, excl, table_names, funcs, roots, unused_vectors, not_ported)
    c = collections.Counter(st for _, _, st, _ in rows)
    for k, v in sorted(c.items()):
        print('%-32s %5d' % (k, v))
    print('clones', len(clone_of), 'partial', len(partial), 'tables', len(tables), 'code refs', sum(map(len, code_refs.values())))


def write_report(rows, reach, pred, partial, excl, table_names, funcs, roots, unused_vectors, not_ported):
    c = collections.Counter(st for _, _, st, _ in rows)
    reach_n = sum(1 for n, *_ in rows if n in reach)
    L = []
    L.append('# Faithfulness coverage')
    L.append('')
    L.append('Generated by `tools/faithfulness/coverage.py` (do not edit). Every original function of')
    L.append('`docs/source-map/original-functions.csv` is classified; the method and its limits are below.')
    L.append('The per-function table is `coverage.csv`; the reviewed exclusions are `exclusions.csv`.')
    L.append('')
    L.append('## Counts')
    L.append('')
    L.append('| status | functions |')
    L.append('|---|---:|')
    impl_clone = sum(1 for _, _, st, via in rows if st == 'implemented' and via.startswith('clone:'))
    for k in sorted(c, key=lambda k: (k != 'implemented', k != 'MISSING', k)):
        extra = ' (%d of them byte-identical clones of cited functions)' % impl_clone if k == 'implemented' else ''
        L.append('| %s%s | %d |' % (k, extra, c[k]))
    L.append('| **total** | %d |' % len(rows))
    L.append('')
    L.append('Reachable from the entry points: %d functions. Implemented functions with a MISSING callee'
             ' (cited only partially): %d.' % (reach_n, len(partial)))
    L.append('')
    missing = [(n, r) for n, r, st, _ in rows if st == 'MISSING']
    by_area = collections.defaultdict(list)
    for n, r in missing:
        ar = area_of(record_of(n))
        if n.startswith('root'):
            # a resident helper belongs to the area of its callers when they all share one
            cs = {area_of(record_of(p)) for p in pred.get(n, ()) if p in funcs and p in reach}
            if len(cs) == 1 and next(iter(cs)) != ar:
                ar = cs.pop()
        by_area[ar].append((n, r))
    L.append('## MISSING by area')
    L.append('')
    L.append('| area | missing |')
    L.append('|---|---:|')
    for ar in sorted(by_area, key=lambda k: -len(by_area[k])):
        L.append('| %s | %d |' % (ar, len(by_area[ar])))
    L.append('')
    for ar in sorted(by_area):
        L.append('### %s' % ar)
        L.append('')
        L.append('| function | linked | size | status | reached from | callers |')
        L.append('|---|---|---:|---|---|---|')
        for n, r in by_area[ar]:
            callers = sorted(p for p in pred.get(n, ()) if p in funcs)
            rk = reach.get(n, ('', ''))
            L.append('| `%s` | %s | %s | %s | %s | %s |' % (n, r['linked'], r['size'], r['status'], rk[0],
                                                         ', '.join(callers[:4]) + (' ...' if len(callers) > 4 else '')))
        L.append('')
    L.append('## Cited but not ported')
    L.append('')
    L.append('The C# names these functions, but their behaviour is not ported (`not-ported.csv`); they count as MISSING.')
    L.append('')
    L.append('| function | why |')
    L.append('|---|---|')
    for n in sorted(not_ported):
        L.append('| `%s` | %s |' % (n, not_ported[n]))
    L.append('')
    L.append('## Cited only partially')
    L.append('')
    L.append('Implemented (cited) functions whose callee is MISSING: the C# cites the caller, but part of')
    L.append('what it calls is not ported or not traced.')
    L.append('')
    L.append('| function | missing callees |')
    L.append('|---|---|')
    for n in sorted(partial):
        L.append('| `%s` | %s |' % (n, ', '.join('`%s`' % m for m in partial[n])))
    L.append('')
    L.append('## Exclusions')
    L.append('')
    L.append('Patterns match `root SSSS:OOOO` for resident functions and `RNNN+0xPPPP (SSSS:OOOO)` (coordinate and')
    L.append('link-time address) for overlay functions; an exact coordinate matches too.')
    L.append('')
    L.append('| pattern | reason | functions | why |')
    L.append('|---|---|---:|---|')
    used = collections.Counter(via for _, _, st, via in rows if st.startswith('excluded:') and via)
    for x in excl:
        L.append('| `%s` | %s | %d | %s |' % (x['pattern'], x['reason'], used.get(x['pattern'], 0), x['why']))
    L.append('')
    L.append('## Function-pointer tables in data')
    L.append('')
    L.append('Every relocated far pointer to a function or thunk stored outside decoded code, grouped into')
    L.append('runs; each target is an entry point.')
    L.append('')
    L.append('| table | data entry | entries | targets |')
    L.append('|---|---|---:|---|')
    for k in sorted(table_names):
        here, sym, cnt, targets = table_names[k]
        L.append('| %s | %s | %d | %s |' % (here, sym, cnt, ', '.join('`%s`' % t for t in targets[:3]) + (' ...' if len(targets) > 3 else '')))
    L.append('')
    L.append('## Thunks nothing names')
    L.append('')
    L.append('RTLink thunks that no decoded far call, code pointer or data pointer names. They are still')
    L.append('treated as entry points (a thunk is an entry into its overlay); the status of their targets:')
    L.append('')
    L.append('| thunk | target | status |')
    L.append('|---|---|---|')
    st = {n: s for n, _, s, _ in rows}
    for v, t in unused_vectors:
        L.append('| `%s` | `%s` | %s |' % (v, t, st.get(t, '')))
    L.append('')
    L.append(METHOD)
    with open(os.path.join(OUT_DIR, 'COVERAGE.md'), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(L) + '\n')


METHOD = """## Method and limits

* **Entry points**: the MZ entry (RTLink start, C start-up, `main` in R000, the main loop root
  03A1 and the card table it dispatches through), every RTLink thunk (thunk -> target from the
  original index), and every function-pointer table in data (above).
* **Edges**: calls (near/far calls, thunks resolved), and code pointer edges: a relocated segment
  immediate within four instructions of an offset immediate that, together, name a function entry
  or a thunk (handler tables the card scripts fill at run time: `opt_fn`, `item_fn`, `saint_fn`;
  expiry handlers; comparators; hooks).
* **Not seen**: near (16-bit) function pointers and indirect calls through computed addresses
  (none in the C; jump tables are inside functions), code reached only through the drivers loaded
  at run time (MGRAPHIC/sound drivers are separate files). Functions the analysis found only by
  scanning without any reference are `excluded:unreferenced`.
* **Implemented** means *cited with a trace*: an entry coordinate (`RNNN+0xPPPP`, `root SSSS:OOOO`)
  in the C# or the source map, an RTLink thunk or a readable name of the function on a trace line,
  a handler offset in a comment block that cites a function of the same frame. A line that says the
  function is not ported ("not written", "not yet", "not reproduced", "not modelled", "not needed" ...)
  only *mentions* it (such functions are MISSING with `via` = `mentioned-not-ported`), and
  `not-ported.csv` lists functions cited elsewhere whose behaviour is still missing. That the C#
  behaves like the original is shown by the tests and oracles the trace names, not by this report.
  Clones: a function whose bytes are identical to a cited function's (calls and relocated words
  compared by what they name) counts as implemented when every caller of it is implemented (the
  generic card scenes and option handlers repeated in many overlays).
"""

if __name__ == '__main__':
    main()
