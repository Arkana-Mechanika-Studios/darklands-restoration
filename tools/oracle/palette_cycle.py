"""palette_cycle: the ORIGINAL's palette cycles (root 047C:0362 take, 047C:03DA start, 047C:040B stop, 047C:042A the step the timer
interrupt's frame work root 047C:000A calls on every frame while DS:07D8 != 0) called directly on a machine state, with the VGA DAC
(ports 3C7 / 3C8 / 3C9) modelled here, for the rewrite's test (tests/Darklands.Battles.Tests/PaletteCycleTests.cs).

usage: python -I -B palette_cycle.py <out.json.gz> <state> [frames]

Cases: the battlefield's three cycles (R002+0x04D2..0x04FA: 0362(1, 4, 0x10, 0x15), 0362(2, 6, 0x18, 0x1E), 0362(4, 5, 0x16, 0x17), then
R014+0x02D5..0x02EB: 03DA(1), 03DA(-2), 03DA(4)); then each ENEMYPAL.DAT scheme with a cycle (+0x31 != 0xFF) as enemy-placement-entry takes it
(R025+0x0958..0x098D: 0362(d, c, a, b) with a, b, c, d = +0x31, +0x32, +0x33, +0x34, then 03DA(d)) on top of the battlefield's; a stop of
slot 3 (R014+0x0D99) half way; a second take of slot 3 by the next scheme (the three schemes of an enemy share d = 3). The DAC starts as a
pattern (and the scheme's 48 bytes at its range, as the palette upload R025+0x094B leaves it); after every 042A call the DAC is kept.
Job: {"name", "dac" (768 bytes before), "calls": [["take", d, c, a, b] | ["start", d] | ["stop", d] | ["frame"]],
"after": [768 bytes after each "frame" call]}."""
from __future__ import annotations

import gzip
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import orig_side  # noqa: E402
import world_calls as wc  # noqa: E402
from dosmachine import P  # noqa: E402

ROOT = P(0x047C)
DAC = bytearray(768)
INDEX = {"read": 0, "write": 0}
_port = orig_side.Session._on_port


def _on_port(self, uc, port, size, *rest):
    """The DAC's ports (index registers 3C7 for reads, 3C8 for writes, data 3C9: three bytes per colour), the rest as the session has them."""
    if len(rest) == 2 and port in (0x3C7, 0x3C8, 0x3C9):
        value = rest[0] & 0xFF
        if port == 0x3C7:
            INDEX["read"] = value * 3
        elif port == 0x3C8:
            INDEX["write"] = value * 3
        else:
            DAC[INDEX["write"] % 768] = value & 0x3F
            INDEX["write"] += 1
        return
    if len(rest) == 1 and port == 0x3C9:
        value = DAC[INDEX["read"] % 768]
        INDEX["read"] += 1
        return value
    return _port(self, uc, port, size, *rest)


orig_side.Session._on_port = _on_port


def call(st, off, args):
    st.m.count, st.m.budget = 0, 1_000_000
    st.invoke(ROOT, off, [a & 0xFFFF for a in args], False)


def run(st, calls):
    """Runs the calls; returns the DAC after each frame."""
    after = []
    for c in calls:
        kind = c[0]
        if kind == "take":
            call(st, 0x0362, c[1:])
        elif kind == "start":
            call(st, 0x03DA, c[1:])
        elif kind == "stop":
            call(st, 0x040B, c[1:])
        elif kind == "frame":
            call(st, 0x042A, [])
            after.append(list(DAC))
    return after


def main():
    out, state = Path(sys.argv[1]), Path(sys.argv[2])
    frames = int(sys.argv[3]) if len(sys.argv) > 3 else 200
    st = wc.WState(state, coverage=False, near=[])
    game = Path(wc.GAME)
    pal = next(p for p in game.iterdir() if p.name.upper() == "ENEMYPAL.DAT").read_bytes()
    schemes = [pal[53 * k:53 * k + 53] for k in range(len(pal) // 53)]
    base = [(i * 7 + 3) & 0x3F for i in range(768)]
    field = [["take", 1, 4, 0x10, 0x15], ["take", 2, 6, 0x18, 0x1E], ["take", 4, 5, 0x16, 0x17], ["start", 1], ["start", -2], ["start", 4]]
    jobs = [{"name": "battlefield", "dac": base, "calls": field + [["frame"]] * frames}]
    cycling = [k for k, s in enumerate(schemes) if s[0x31] != 0xFF]
    for n, k in enumerate(cycling):
        s = schemes[k]
        dac = list(base)
        dac[s[0]:s[0] + 48] = list(s[1:49])
        a, b, c, d = s[0x31], s[0x32], s[0x33], s[0x34]
        calls = field + [["take", d, c, a, b], ["start", d]] + [["frame"]] * (frames // 2)
        # the next cycling scheme takes the same slot (an enemy's schemes share d) or the slot is stopped (R014+0x0D99)
        nxt = schemes[cycling[(n + 1) % len(cycling)]]
        if n % 2 == 0:
            calls += [["take", nxt[0x34], nxt[0x33], nxt[0x31], nxt[0x32]], ["start", nxt[0x34]]]
        else:
            calls += [["stop", d]]
        calls += [["frame"]] * (frames - frames // 2)
        jobs.append({"name": "scheme %d" % k, "dac": dac, "calls": calls})
    for job in jobs:
        st.restore()
        DAC[:] = bytes(job["dac"])
        INDEX["read"] = INDEX["write"] = 0
        # every slot stopped first (the state's own cycles, if any, are not part of the job)
        for d in range(1, 9):
            call(st, 0x040B, [d])
        job["after"] = run(st, job["calls"])
        print(job["name"], "frames", len(job["after"]), flush=True)
    with gzip.open(out, "wt", encoding="utf-8") as f:
        json.dump({"jobs": jobs}, f)


if __name__ == "__main__":
    main()
