"""Print the machine-local mapping entry of original coordinates: what the reference decompilation calls
the function, its readable names, its source file and revision (local/reference-map.csv, git-ignored).

usage: python -I resolve.py R044+0x0123 'root 0410:0008' 'thunk 09C0:190F' [...]

A coordinate may also be given as a readable name (card_load) or a link-time address with its record
(R044 1838:0123). Exits with an error when local/reference-map.csv is absent: the published repository
carries only the coordinates (docs/source-map/original-functions.csv); see docs/source-map/README.md for
how the local mapping is made.
"""
import csv
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MAPPING = os.path.join(REPO, 'local', 'reference-map.csv')


def main(args):
    if not args:
        sys.exit(__doc__)
    if not os.path.isfile(MAPPING):
        sys.exit('resolve: %s is missing: the mapping to the reference decompilation is machine-local '
                 '(see docs/source-map/README.md)' % os.path.relpath(MAPPING, REPO))
    with open(MAPPING, newline='', encoding='utf-8') as fh:
        rows = list(csv.DictReader(fh))
    by = {}
    for r in rows:
        by.setdefault(r['coordinate'], r)
        by.setdefault(r['reference_name'], r)
        rec = 'root' if r['coordinate'].startswith(('root', 'thunk')) else r['coordinate'][:4]
        by.setdefault('%s %s' % (rec, r['linked']), r)
        for n in filter(None, (r['readable_names'] + ';' + r['c_names'] + ';' + r['crt_names']).split(';')):
            by.setdefault(n, r)
    status = 0
    for a in args:
        r = by.get(a) or by.get(a.upper().replace('0X', '0x').replace('ROOT', 'root').replace('THUNK', 'thunk'))
        if r is None:
            print('%s: not in the local mapping' % a)
            status = 1
            continue
        print(a)
        for k, v in r.items():
            if v:
                print('  %-20s %s' % (k, v))
    return status


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
