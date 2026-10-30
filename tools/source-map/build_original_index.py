#!/usr/bin/env python3
"""Regenerate docs/source-map/original-functions.csv, the neutral index of original coordinates.

One row per original entry the C# can cite (function, RTLink overlay thunk, far data, bare code
address), keyed by its coordinate in DARKLAND.EXE (see docs/TRACEABILITY.md):

  coordinate  R044+0x0123 (record + payload offset), root SSSS:OOOO, thunk 09C0:XXXX, R136+0xPPPP (far data)
  linked      SSSS:OOOO, the link-time address
  size        bytes (empty when unknown)
  kind        function / thunk / data / address
  status      matched / unmatched / draft / asm (functions and addresses; empty for thunks and data)
  evidence    where the status comes from: bytematch-600a (per-function MSC 6.00A byte comparison),
              audit (a per-function recheck), port-check (an earlier port's check); '+'-joined when several agree
  target      thunks: the coordinate the thunk jumps to
  note        free text (thunk atlas status, containing function, draft byte result, ...)

The rows are derived from the machine-local mapping local/reference-map.csv (git-ignored; see
docs/source-map/README.md), which this script only reads: its first eight columns are the published
ones. Standard library only.

  python tools/source-map/build_original_index.py [--map local/reference-map.csv] [--out PATH]
"""
import argparse, csv, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.dirname(HERE))
import local_paths  # noqa: E402
COLUMNS = ['coordinate', 'linked', 'size', 'kind', 'status', 'evidence', 'target', 'note']
COORD_RE = re.compile(r'(R\d{3}\+0x[0-9A-F]{4,5}|root [0-9A-F]{4}:[0-9A-F]{4}|thunk 09C0:[0-9A-F]{4})')
KINDS = {'function', 'thunk', 'data', 'address'}
STATUSES = {'matched', 'unmatched', 'draft', 'asm', ''}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--map', default=os.path.join(local_paths.local_dir(REPO), 'reference-map.csv'))
    ap.add_argument('--out', default=os.path.join(REPO, 'docs', 'source-map', 'original-functions.csv'))
    a = ap.parse_args()
    if not os.path.isfile(a.map):
        sys.exit('build_original_index: %s is missing (the local mapping; see docs/source-map/README.md)' % a.map)
    with open(a.map, newline='', encoding='utf-8') as fh:
        rows = list(csv.DictReader(fh))
    seen = set()
    for r in rows:
        c = r['coordinate']
        if not COORD_RE.fullmatch(c) or c in seen:
            sys.exit('build_original_index: bad or repeated coordinate %r' % c)
        seen.add(c)
        if r['kind'] not in KINDS or r['status'] not in STATUSES:
            sys.exit('build_original_index: %s: kind %r status %r' % (c, r['kind'], r['status']))
    with open(a.out, 'w', newline='', encoding='utf-8') as fh:
        w = csv.writer(fh, lineterminator='\n')
        w.writerow(COLUMNS)
        for r in rows:
            w.writerow([r[k] for k in COLUMNS])
    print('%d rows -> %s' % (len(rows), os.path.relpath(a.out, REPO)))


if __name__ == '__main__':
    main()
