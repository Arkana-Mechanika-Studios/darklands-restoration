"""Open items of docs/faithfulness/branches-missed.csv that coverage runs hit (the gain of a set of sessions).

usage (cwd = a checkout): python -I -B measure_new_outcomes.py <cov-helper cov.py folder> <out.json> <cov folder>...
Each cov folder holds cov-*.jsonl (lockstep.py --coverage writes <session>/cov/cov-0.jsonl only if every state agrees: a differing session has none). A folder with several records counts their union.
Prints per folder the open items it hits, the union, the items only it hits, and the union by record.
"""
import collections
import json
import sys

helper, out = sys.argv[1], sys.argv[2]
folders = sys.argv[3:]
sys.path.insert(0, helper)
sys.path.insert(0, "tools/oracle")
import cov  # noqa: E402

# Each folder's records are streamed into one merged coverage: holding the parsed records of a large
# corpus at once ran the machine out of memory.
labels = {}
for f in folders:
    acc = None
    for j in cov.cov_records([f]):
        acc = cov.merge([j], acc)
    if acc:
        labels[f] = acc
opens = cov.open_items(None)
res, touched = cov.hits_merged(labels, opens)
union = set().union(*res.values()) if res else set()
fn2rec = {f.name: f.record for f in cov.FUNCS}
print("sessions with coverage:", len(labels), "of", len(folders), "; open items hit (union):", len(union))
only = {}
for f, s in res.items():
    others = set().union(*[v for k, v in res.items() if k != f]) if len(res) > 1 else set()
    only[f] = len(s - others)
    print("%6d hit %6d only  %s" % (len(s), only[f], f))
byrec = collections.Counter(fn2rec.get(it[0], "?") for it in union)
print("by record:", byrec.most_common(20))
byfn = collections.Counter(it[0] for it in union)
print("by function:", byfn.most_common(25))
json.dump({"union": sorted("|".join(x) for x in union), "per": {k: sorted("|".join(x) for x in v) for k, v in res.items()}}, open(out, "w"))
