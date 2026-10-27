"""Narrow a lockstep session that differs: delta debugging over its input units (the smallest set of steps that still gives a difference).

usage: python -I -B ddmin_session.py <steps file> <out folder> --save <saved game> [--recordings DIR] [--max-runs 40] [--timeout 900]

The steps up to `state:map` (the way into the screen) are kept; the rest is cut into units (a key / click / move / option with its waits, or a
bracket: shift / ctrl on..off, a held key kdown..kup, the right button rdown..rup, with what lies between). `state:` steps are dropped but the last
one (`state:end`). A try "reproduces" when lockstep.py ends with a first divergence (not "none" and not a script error). The result is written to
<out>/min.steps and each try's log to <out>/try-<n>.log.
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
GAME = HERE.parent.parent.parent / "game" / "DARKLAND"
OPEN = {"shift:on": "shift:off", "ctrl:on": "ctrl:off"}
OPEN_PREFIX = {"rdown": "rup", "kdown": "kup"}


def units_of(lines):
    units, cur, closer = [], [], None
    for l in lines:
        kind = l.split(":")[0]
        if l.startswith("state:"):
            continue
        if closer is not None:
            cur.append(l)
            if l == closer or kind == closer:
                closer = None
            continue
        if kind == "wait":
            cur.append(l)
            continue
        if cur:
            units.append(cur)
        cur = [l]
        if l in OPEN:
            closer = OPEN[l]
        elif kind in OPEN_PREFIX:
            closer = OPEN_PREFIX[kind]
    if cur:
        units.append(cur)
    return units


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("steps")
    ap.add_argument("out")
    ap.add_argument("--save", required=True)
    ap.add_argument("--recordings")
    ap.add_argument("--max-runs", type=int, default=40)
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--game", default=str(GAME))
    a = ap.parse_args()
    lines = [l.strip() for l in Path(a.steps).read_text(encoding="utf-8").splitlines() if l.strip() and not l.startswith("#")]
    cut = lines.index("state:map") + 1
    prefix, body = lines[:cut], [l for l in lines[cut:] if not l.startswith("option:")]      # (an option step needs a menu on the screen)
    units = units_of(body)
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    runs = [0]

    def reproduces(sub) -> bool:
        runs[0] += 1
        n = runs[0]
        steps = out / ("try-%d.steps" % n)
        steps.write_text("\n".join(prefix + [l for u in sub for l in u] + ["state:end"]) + "\n", encoding="utf-8")
        cmd = [sys.executable, "-I", "-B", "-u", str(HERE / "lockstep.py"), a.game + "/DARKLAND.EXE", a.game, str(steps), str(out / ("try-%d" % n)), "--save", a.save]
        if a.recordings:
            cmd += ["--recordings", a.recordings]
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=a.timeout)
            text = r.stdout
        except subprocess.TimeoutExpired:
            text = "timeout"
        (out / ("try-%d.log" % n)).write_text(text, encoding="utf-8")
        last = text.strip().splitlines()[-1] if text.strip() else ""
        hit = "first divergence:" in last and "first divergence: none" not in last and "menu has" not in last and "script error" not in last
        print("try %d: %d units -> %s (%s)" % (n, len(sub), "reproduces" if hit else "no", last[-80:]), flush=True)
        return hit

    if not reproduces(units):
        print("the session does not reproduce")
        return
    n = 2
    while len(units) >= 2 and runs[0] < a.max_runs:
        size = max(1, len(units) // n)
        chunks = [units[i:i + size] for i in range(0, len(units), size)]
        reduced = False
        for k in range(len(chunks)):
            if runs[0] >= a.max_runs:
                break
            rest = [u for j, c in enumerate(chunks) if j != k for u in c]
            if rest and reproduces(rest):
                units, n, reduced = rest, max(n - 1, 2), True
                break
        if not reduced:
            if n >= len(units):
                break
            n = min(len(units), n * 2)
    (out / "min.steps").write_text("\n".join(prefix + [l for u in units for l in u] + ["state:end"]) + "\n", encoding="utf-8")
    print("minimal: %d units in %d runs -> %s" % (len(units), runs[0], out / "min.steps"))


if __name__ == "__main__":
    main()
