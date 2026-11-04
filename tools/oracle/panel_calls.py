"""Direct calls of the ORIGINAL's party panel bars, member-gauge-bars root 0000:1A9C(i), from a captured state, with generated member values,
the driver's line and text entries recorded instead of drawn (a tools addition: no original-side module changes, no recording expires).

usage: python -I -B panel_calls.py <state> <out folder> [--seed 1] [--jobs 600]

Each job writes member i's record of the party table DS:9C69 + 0x80 i (status +0x00, the attributes +0x49.., their maximums +0x50..: endurance,
strength and divine favour are attributes 0, 1 and 6), clears its fighter flags DS:A696 + 2 i (no place note: that one is the battle screen's,
BattleScreen.DrawRowNote) and sets the panel page window's colour (DS:[0168] + 0x0C, the colour the member's name left: the values and the "+"
are drawn in it). The member's action word (+0x10 of the combatant-shaped record DS:9C55 + 0x80 i), its target (+0x4A), fighter-moving
DS:A6D3 + i and the target's record are set too: with an attack order (0x1BE), standing and a target, the bars also show the target's values. Recorded: gfx-line-draw 047C:0D62(window, x0, y0, x1, y1, colour) and gfx-text-draw 047C:055A(window, x, y, string) with the
window's words. The rewrite's test (tests/Darklands.Rendering.Tests/PanelBarsCallsTests.cs) draws them and PartyPanel's bars for the same
member, and compares the row's pixels.
"""
from __future__ import annotations

import argparse
import gzip
import json
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import world_calls as wc  # noqa: E402
from text_calls import Recorder  # noqa: E402
from dosmachine import DS, linear  # noqa: E402

BARS = "root 0000:1A9C"
PARTY, RECORD = 0x9C69, 0x80
STATUS, ATTR, ATTR_MAX = 0x00, 0x49, 0x50
FLAGS = 0xA696
ATTRS = (0, 1, 6)          # endurance, strength, divine favour


class PanelRecorder(Recorder):
    """The text entries of Recorder and the line entry."""

    def __init__(self, st: wc.WState):
        super().__init__(st)
        m = self.m
        depth, m.depth = m.depth, 0          # (as map_draw: the entry's code hook is added between two calls)
        try:
            m.stub(wc.P(0x047C), 0x0D62, self.line, "gfx-line-draw", record="root")
        finally:
            m.depth = depth

    def line(self, m):
        self.calls.append(["line", m.sarg(1), m.sarg(2), m.sarg(3), m.sarg(4), m.arg(5) & 0xFF, self.window(m.arg(0))])
        return (0, 0)


def value(r: random.Random, top: int) -> int:
    return r.choice((0, 1, 2, top - 1, top, top + 1, top + 25, r.randint(0, top), r.randint(0, 127), 127, 99))


def values(r: random.Random):
    mx = [r.choice((1, 2, 15, 16, 17, 30, 31, 32, 60, 99, 127, r.randint(1, 127))) for _ in ATTRS]
    return [max(0, min(127, value(r, m))) for m in mx], mx


def member(r: random.Random) -> dict:
    status = r.choice((1, 1, 1, 2, 3, 5))
    cur, mx = values(r)
    m = {"row": r.randrange(5), "status": status, "current": cur, "maximum": mx, "colour": r.choice((0x0E, 0x0F, 0x0F, 7))}
    if r.random() < 0.5:
        tc, tm = values(r)
        m["target"] = {"index": r.randrange(13), "action": r.choice((0x02, 0x04, 0x08, 0x10, 0x20, 0x80, 0x100, 0x01, 0x40, 0x200, 0)),
                       "moving": int(r.random() < 0.2), "current": tc, "maximum": tm}
    return m


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("state")
    ap.add_argument("out")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--jobs", type=int, default=600)
    ap.add_argument("--game", default=str(wc.GAME))
    a = ap.parse_args()
    r = random.Random(a.seed)
    st = wc.WState(Path(a.state), a.game, coverage=True)
    rec = PanelRecorder(st)
    out = Path(a.out)
    (out / "cov").mkdir(parents=True, exist_ok=True)
    page = st.m.word(DS, 0x0168)
    jobs = []
    # the edges first: every bar's lost length (0 .. 16, the 15 of a full loss with a value left, above the maximum), then drawn members
    edges = []
    for mx in (1, 16, 30, 99):
        for cur in sorted({0, 1, mx // 2, mx - 1, mx, mx + 1, min(127, mx + 40)}):
            edges.append({"row": len(edges) % 5, "status": 1, "current": [cur, min(127, cur + 1), max(0, cur - 1)], "maximum": [mx, mx, mx], "colour": 0x0F})
    edges += [{"row": k, "status": 5, "current": [5, 5, 5], "maximum": [9, 9, 9], "colour": 7} for k in range(5)]
    members = edges + [member(r) for _ in range(a.jobs)]
    with open(out / "cov" / "cov-panel-calls.jsonl", "w", encoding="utf-8") as cov:
        for k, s in enumerate(members):
            i = s["row"]
            base = PARTY + RECORD * i
            win = bytearray(st.m.read(DS, page, 18))
            struct.pack_into("<h", win, 0x0C, s["colour"])
            edits = [(base + STATUS, bytes([s["status"]])), (FLAGS + 2 * i, bytes(2)), (page, bytes(win))]
            t = s.get("target")
            # the combatant-shaped record starts 0x14 before the status: action word +0x10, target +0x4A (0xFF: nobody)
            edits += [(base - 0x14 + 0x10, struct.pack("<H", t["action"] if t else 0)), (base - 0x14 + 0x4A, bytes([t["index"] if t else 0xFF])),
                      (0xA6D3 + i, bytes([t["moving"] if t else 0]))]
            if t and t["index"] != i:
                tb = PARTY + RECORD * t["index"]
                for n, att in enumerate(ATTRS):
                    edits += [(tb + ATTR + att, bytes([t["current"][n]])), (tb + ATTR_MAX + att, bytes([t["maximum"][n]]))]
            for n, att in enumerate(ATTRS):
                edits += [(base + ATTR + att, bytes([s["current"][n] & 0xFF])), (base + ATTR_MAX + att, bytes([s["maximum"][n] & 0xFF]))]
            if st.calls_since_build + 1 > wc.REBUILD_EVERY:
                st.rebuild()
                st.calls_since_build = 0
                rec.__init__(st)
            rec.calls.clear()
            res = st.call(BARS, [i], ds_edits=edits, saves=False, budget=20_000_000)
            jid = "bar-%05d" % k
            cov.write(json.dumps({"unit": BARS, "state": "%s#%s" % (Path(a.state).stem, jid), **res["coverage"]}) + "\n")
            jobs.append({"id": jid, "member": s, "error": res["error"], "calls": list(rec.calls)})
            if k % 100 == 0:
                print(k, "of", len(members), flush=True)
    with gzip.open(out / "jobs.json.gz", "wt", encoding="utf-8") as f:
        json.dump({"state": Path(a.state).name, "jobs": jobs}, f)
    print("jobs", len(jobs), "errors", sum(1 for j in jobs if j["error"]))


if __name__ == "__main__":
    main()
