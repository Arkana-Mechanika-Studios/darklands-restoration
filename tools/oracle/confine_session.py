"""Cut a generated session where the ORIGINAL left the screen it walks: the walk's later inputs land on other screens (menus whose
clicks the tester's fixed font metrics place differently, text boxes that never read a click), so they are not the screen's test.

usage: python -I -B confine_session.py <session .steps> <original.log of a run of it> <vector> <out .steps> --after STATE
<vector> is the overlay entry of the screen (as original.log's stacks print it, e.g. R037+0x4664 for the character generator, R038+0x0000
for the inn's roster). The input steps are paired in order with the log's input lines
(key / click / option / move); the first input of the walk (after `state:STATE`) that the original
took outside <vector>, and every step after it, are dropped; the session ends with `state:end`. A log that ends with the program's exit
(a fatal error of the original) also drops the input the original died on. Prints the number of walk inputs kept.
"""
import argparse
import re
from pathlib import Path

INPUT = ("key", "click", "rclick", "rdown", "rup", "option", "move")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("steps")
    ap.add_argument("log")
    ap.add_argument("vector")
    ap.add_argument("out")
    ap.add_argument("--after", required=True, help="the state step that ends the way into the screen (generator, roster)")
    a = ap.parse_args()
    lines = Path(a.steps).read_text(encoding="utf-8").splitlines()
    log = Path(a.log).read_text(encoding="utf-8", errors="replace").splitlines()
    taken = [l for l in log if re.match(r"(key|click|rclick|rdown|rup|option|move) ", l)]
    died = any(l.startswith("end: program exit") for l in log)
    out, n, walk, kept, cut = [], 0, False, 0, False
    for l in lines:
        s = l.strip()
        kind = s.split(":")[0]
        if kind in INPUT:
            if walk:
                if n >= len(taken) or a.vector not in taken[n] or (died and n == len(taken) - 1):
                    cut = True
                    break
                kept += 1
            n += 1
        elif s == "state:" + a.after:
            walk = True
        elif s == "state:end":
            continue
        out.append(l)
    if out and out[0].startswith("# Generated"):
        out[0] += "; confined to %s by the original's log: %d walk inputs kept" % (a.vector, kept)
    out.append("state:end")
    Path(a.out).write_text("\n".join(out) + "\n", encoding="utf-8")
    print("%s: %d walk inputs kept%s" % (Path(a.out).stem, kept, ", cut" if cut else ""))


if __name__ == "__main__":
    main()
