"""Narrow a differing generated session to the one input that makes the two games part.

usage: python -I -B localize_lead.py <session .steps> <report.json of its run> <out .steps>
The input steps between the last state step that agreed and the first that differs get a state step after each of them; the rest of the
session is cut after the first differing state. Run the result with lockstep.py (same --save) and read the first state that differs.
"""
import json
import sys
from pathlib import Path

steps = Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
report = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
ignored = {"state", "file", "text state", "texts", "party seed", "bsv kept", "undefined memory"}
first = next((r["state"] for r in report["states"] if "state" in r and set(r) - ignored), None)
if first is None:
    raise SystemExit("the report has no differing state")
out = []
last_agree = None
for l in steps:
    s = l.strip()
    if s.startswith("state:"):
        if s == "state:" + first:
            break
        last_agree = len(out)
    out.append(l)
tail = out[last_agree + 1:] if last_agree is not None else []
head = out[:last_agree + 1] if last_agree is not None else out
res = list(head)
n = 0
for l in tail:
    res.append(l)
    if l.startswith("wait:"):
        n += 1
        res.append("state:i%d" % n)
res.append("state:end")
Path(sys.argv[3]).write_text("\n".join(res) + "\n", encoding="utf-8")
print("window of %d waits after state %s; first differing state was %s" % (n, head[-1] if head else "-", first))
