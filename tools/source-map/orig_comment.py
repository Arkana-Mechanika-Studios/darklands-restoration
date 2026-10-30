"""Print the `// orig:` comment for original coordinates (docs/TRACEABILITY.md).

usage: python -I orig_comment.py R014+0x9F12 'root 0410:0008' [...]

Each argument is a coordinate of docs/source-map/original-functions.csv (RNNN+0xPPPP, root SSSS:OOOO,
thunk 09C0:XXXX, R136+0xPPPP); with the machine-local mapping local/reference-map.csv present, a
readable name of the reference decompilation (card-run, party-wipe ...) is accepted too. The comment
gives the coordinate, the link-time address and the source status:

  // orig: R014+0x9F12 (18BF:0102) draft
"""
import csv
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(REPO, 'tools'))
import local_paths  # noqa: E402
INDEX = os.path.join(REPO, 'docs', 'source-map', 'original-functions.csv')
MAPPING = os.path.join(local_paths.local_dir(REPO), 'reference-map.csv')


def load():
    with open(INDEX, newline='', encoding='utf-8') as fh:
        rows = {r['coordinate']: r for r in csv.DictReader(fh)}
    names = {}
    if os.path.isfile(MAPPING):
        with open(MAPPING, newline='', encoding='utf-8') as fh:
            for r in csv.DictReader(fh):
                for n in filter(None, r['readable_names'].split(';') + r['c_names'].split(';')):
                    names.setdefault(n, r['coordinate'])
    return rows, names


def comment(rows, names, arg):
    co = arg if arg in rows else names.get(arg)
    if co is None:
        sys.exit('orig_comment: %s is not a coordinate of the index%s' % (
            arg, '' if os.path.isfile(MAPPING) else ' (readable names need local/reference-map.csv)'))
    r = rows[co]
    prefix = arg + ' ' if arg != co else ''
    linked = '' if co.startswith(('root', 'thunk')) else ' (%s)' % r['linked']
    return '// orig: %s%s%s %s' % (prefix, co, linked, r['status'] or r['kind'])


if __name__ == '__main__':
    rows, names = load()
    for a in sys.argv[1:]:
        print(comment(rows, names, a))
