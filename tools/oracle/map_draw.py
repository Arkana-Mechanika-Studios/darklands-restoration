"""map_draw: the travel map's drawing calls of the ORIGINAL (R030+0x20D2, the map; R030+0x16C6, the party) run directly from map states
captured inside lockstep sessions (world_calls.py capture), with the graphics driver's entries recorded instead of drawn (the emulator loads
no MGRAPHIC.EXE: the pictures in its memory are empty blocks, so the calls are kept with the picture each segment stands for and the rewrite's
test, tests/Darklands.Cards.Tests/OverlandDrawingTests.cs, replays them with a model of the driver's primitives).

usage: python -I -B map_draw.py [--border | --south | --east | --after] <out.json.gz> <state> [<state> ...]

--border: only the views at the map's west / north border (DS:286A = 0 and / or DS:286C = 0, where 20d2 takes the 0 as 1, R030+0x20D8..0x20E9)
and two controls next to them, instead of the sweep of the whole map.

--south: only marker positions with y 175..177 (the rectangle reaches screen lines 200..202, which the driver's row table maps to the
screen's top line) in the state's own view and two others; the views whose cell rows below the cell buffer matter (a picture that is
not a tile: the bytes after the buffer, which the rewrite reads as empty) are left out.

--east: only the views at the map's east border (DS:286A = width - 0x11: 20d2's decoder reads the cell past each row's end from the row
buffer's next bytes), the south-east corner, controls west of it, and views after a sequence of row buffer reads (the R030+0x1DD4 calls
listed in the job's "loads": the stale slots of a scroll up, the bottom jump of the scroll block that reads no rows, slot tails left by
other rows). A job whose decoder runs past the row buffer's block (into the memory after it) is marked "past_block".

--after: views with a place icon in the row past the cell buffer (row tope + 50, which 20d2's icon loop writes into the bytes after
the buffer, G[50][..]; icons with a road in the map row above first, whose masks read it) and the marker in the view's last rows near
it (DS:2868 0xAD..0xB1: 16c6 draws rows up to 50 and reads row 51 for the masks), so that the party's rectangle shows those bytes.

Each job also keeps the 70 bytes after the cell buffer ("after", R136+0x10082.., as 20d2 left them) for a direct comparison, and the
state's own DS:286E ("state_top": the rows its buffer held before the job's reads, whose slot tails a read of shorter rows leaves).

Driver entries (MGRAPHIC.EXE, the slot table of root 047C:04C7): 5 (047C:04EA, fill page), 6 (047C:04EF, transparent picture clipped to
the window), 20 (047C:0553, rectangle copy), 32 (047C:05A5, transparent picture, not clipped); root 047C:0952 (filled rectangle) is
recorded at its entry. A window is (page, x, y, last x, last y) at its DS offset. Pictures: the tiles R136 table at [DS:7E3E]:1FFC
(index (code - 1) * 16 + mask, the place icons at code + 382), the party sprites [DS:7E3E]:2798 ([direction][frame]), the header DS:E3FC
and the clock pictures DS:E400[hour / 3].

Each job is one state, one view (DS:286A column, DS:286C first row in the row buffer; DS:286E is the state's) and either one marker
position (a 20d2 call then a 16c6 call) or a walk (one 20d2 call, then 16c6 for every position of a path, as 0c44 calls it each pass
while the view does not move)."""
from __future__ import annotations

import base64
import gzip
import json
import random
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import world_calls as wc  # noqa: E402
from dosmachine import DS, P, Stop, linear  # noqa: E402

DRIVER = P(0x047C)
MAP, PARTY = "R030+0x20D2", "R030+0x16C6"
G = 0x239C                       # the cell buffer [50][19] in the segment DS:7E42 names (20d2 fills it, 16c6 and 1300 read it)
HILLS, MOUNTAINS, WATER = (16, 17, 18, 19), (20, 21, 22, 23), (3, 4, 25)


class Recorder:
    def __init__(self, st: wc.WState):
        self.st, self.m = st, st.m
        self.calls: list = []
        m = self.m
        ic = m.word(DS, 0x7E3E)
        self.pictures = {}
        for i in range(32 * 16 + 48):
            seg = m.word(ic, 0x1FFC + 2 * i)
            if seg:
                self.pictures.setdefault(seg, ["tile", i // 16 + 1, i % 16])
        for d in range(10):
            for f in range(8):
                self.pictures.setdefault(m.word(ic, 0x2798 + 2 * (d * 8 + f)), ["party", d, f])
        self.pictures.setdefault(m.word(DS, 0xE3FC), ["header"])
        for k in range(8):
            self.pictures.setdefault(m.word(DS, 0xE400 + 2 * k), ["clock", k])
        for off, h in ((0x04EA, self.fill_page), (0x04EF, self.sprite), (0x0553, self.copy), (0x05A5, self.picture)):
            lin = linear(DRIVER, off)
            m.stubs.setdefault(lin, []).insert(0, (m.owner(DRIVER, off, "root"), h))
        depth, m.depth = m.depth, 0          # the snapshot was taken inside a call: the entry's code hook is added between two calls
        try:
            m.stub(DRIVER, 0x0952, self.fill, "fill_rect", record="root")
        finally:
            m.depth = depth

    def window(self, off):
        return list(struct.unpack("<5h", bytes(self.m.read(DS, off, 10))))

    def a(self, k):
        return self.m.sarg(k)

    def fill_page(self, m):
        self.calls.append(["fill_page", m.arg(0), m.arg(1) & 0xFF])
        return (0, 0)

    def fill(self, m):
        self.calls.append(["fill", self.window(m.arg(0)), self.a(1), self.a(2), self.a(3), self.a(4), m.arg(5) & 0xFF])
        return (0, 0)

    def pic(self, seg):
        return self.pictures.get(seg, ["segment", seg])

    def sprite(self, m):
        self.calls.append(["sprite", self.window(m.arg(0)), self.a(1), self.a(2), self.pic(m.arg(3))])
        return (0, 0)

    def picture(self, m):
        self.calls.append(["picture", self.window(m.arg(0)), self.a(1), self.a(2), self.pic(m.arg(3))])
        return (0, 0)

    def copy(self, m):
        self.calls.append(["copy", self.window(m.arg(0)), self.a(1), self.a(2), self.a(3), self.a(4), self.window(m.arg(5)), self.a(6), self.a(7)])
        return (0, 0)


def run(st: wc.WState, rec: Recorder, coord: str, args):
    i = st.cat.info(coord)
    m = st.m
    if i["record"] not in m.resident:
        m.ensure(i["record"])
    m.count, m.budget = 0, 20_000_000
    return st.invoke(i["seg"], i["off"], args, i["near"])


def cells(st: wc.WState) -> bytes:
    return bytes(st.m.read(st.m.word(DS, 0x7E42), G, 50 * 19))


def party_cell(st, g, x, y):
    """16c6's reading of the marker's cell: (terrain as 16c6 takes it, row's parity, pixel row and column inside the cell)."""
    w = lambda o: st.m.word(DS, o)
    fy, row = (y + 11) % 4, (y + 11) // 4
    odd = (row + w(0x286E) + w(0x286C) - 1) % 2
    xa = x + (16 if odd else 24)
    c = g[row * 19 + xa // 16]
    return (28 if c > 50 else c), odd, fy, xa % 16


def view_regs(st):
    w = lambda o: st.m.word(DS, o)
    return {"column": w(0x286A), "rows": w(0x286C), "top": w(0x286E), "background": w(0x2860) & 0xFF, "hour": w(0x00DE),
            "width": w(0xE3F6), "height": w(0xE3F8)}


def setup(st, rec, column, rows, top, loads=None):
    """A fresh machine with the view at (column, rows) of the row buffer starting at map row `top` (R030+0x1DD4 reads the whole
    buffer afresh, as on the map's first draw), then the map drawn (20d2). With `loads`, the row buffer is read by those 1dd4 calls
    (new, old; old -1 = all rows) instead and DS:286E is set to `top` afterwards (the registers can name other rows than the buffer holds)."""
    st.restore()
    old = st.m.word(DS, 0x286E)
    if loads:
        for new, was in loads:
            run(st, rec, "R030+0x1DD4", [st.m.word(DS, 0xE3F4), new, was & 0xFFFF])
        st.m.put_word(DS, 0x286E, top)
    elif top != old:
        run(st, rec, "R030+0x1DD4", [st.m.word(DS, 0xE3F4), top, 0xFFFF])     # old = -1: the whole buffer read afresh
        st.m.put_word(DS, 0x286E, top)
    st.m.put_word(DS, 0x286A, column)
    st.m.put_word(DS, 0x286C, rows)
    rec.calls = []
    run(st, rec, MAP, [column, rows])


_MAP: dict = {}


def terrain_at(st, column, row) -> int:
    """The map's terrain at (column, row), decoded from DARKLAND.MAP (for choosing views only)."""
    if not _MAP:
        data = next(p for p in Path(wc.GAME).iterdir() if p.name.upper() == "DARKLAND.MAP").read_bytes()
        _MAP["data"], _MAP["width"] = data, struct.unpack(">H", data[:2])[0]
    data, width = _MAP["data"], _MAP["width"]
    at, n = struct.unpack_from("<I", data, 4 + 4 * row)[0], 0
    while True:
        b = data[at]
        n += b >> 5
        if n > column:
            return b & 0x1F
        at += 1


def after(st) -> list:
    return list(st.m.read(st.m.word(DS, 0x7E42), G + 50 * 19, 70))


def past_block(st, column, rows) -> bool:
    """Whether 20d2's decoder (R030+0x2104..0x2182) runs past the row buffer's block (0x7530 bytes) for some buffer row of the view."""
    m = st.m
    buf = bytes(m.read(m.word(DS, 0x2870), 0, 0x7530))
    column, rows = max(column, 1), max(rows, 1)
    for y in range(50):
        at, read, stored = (rows + y - 1) * 0xD2, 0, 0
        while column + 0x12 > read and stored < 19:
            if at >= len(buf):
                return True
            for _ in range(buf[at] >> 5):
                if stored < 19:
                    if column - 1 <= read:
                        stored += 1
                    read += 1
            at += 1
    return False


def kinds_of(st):
    g = cells(st)
    kinds: dict = {}
    for y in range(0x14, 0xAB):
        for x in range(0x14, 0xE3):
            tl, odd, fy, fx = party_cell(st, g, x, y)
            kind = "hills" if tl in HILLS else "mountains" if tl in MOUNTAINS else "water" if tl in WATER else "rows%d" % (
                0 if tl <= 8 else 1 if tl == 9 else 2 if tl <= 14 else 3 if tl <= 19 else 4)
            kinds.setdefault((kind, odd, fy), []).append((x, y))
    return kinds


DIRS = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)]


def main():
    args = [a for a in sys.argv[1:] if a not in ("--border", "--south", "--east", "--after")]
    border = "--border" in sys.argv[1:]
    south = "--south" in sys.argv[1:]
    east = "--east" in sys.argv[1:]
    past = "--after" in sys.argv[1:]
    out = Path(args[0])
    rnd = random.Random(16)
    jobs, saves = [], {}
    t0 = time.time()
    for path in args[1:]:
        st = wc.WState(Path(path), coverage=False, near=[])
        rec = Recorder(st)
        base = view_regs(st)
        name = Path(path).name[:-len(".state")]
        saves[name] = base64.b64encode(st.sav).decode()       # the saved game of the state: its places give the icons (R030+0x1C76)
        views = [(base["column"], base["rows"], base["top"])]
        loads_of = {}
        if east:
            e, bottom = base["width"] - 0x11, base["height"] - 0x83
            # the east border at several heights, the south-east corner (rows 0x3C and 0x52 of the last 0x83; 0x53 reads slot 0x83, past
            # the block), controls one and ten columns west
            views = [(e, 72, 748), (e, 20, 748), (e, 40, 600), (e, 5, 400), (e, 60, 300), (e, 30, 100), (e, 1, 0), (e, 0x3C, bottom),
                     (e, 0x52, bottom), (e, 0x53, bottom), (e - 1, 40, 600), (e - 10, 72, 748)]
            # (view, loads): a scroll up from a top of 24 to 0 (slots 48 .. 0x82 keep rows 24 further south), the bottom jump from a top of
            # 796 (DS:286E = height - 0x83 with the rows of 796 in the buffer), a scroll up by 0x41 (slot 0x82 stale), slot tails left by
            # the rows of other tops (down then up again) read at the east border
            history = [((150, 20, 0), [(24, -1), (0, 24)]), ((e, 20, 0), [(24, -1), (0, 24)]), ((200, 0x3C, bottom), [(796, -1)]),
                       ((e, 0x3C, bottom), [(796, -1)]), ((150, 0x52, 300), [(365, -1), (300, 365)]),
                       ((e, 40, 300), [(300, -1), (365, 300), (300, 365)]), ((e, 60, 200), [(200, -1), (135, 200), (200, 135)])]
            for view, loads in history:
                key = view + (len(loads_of),)
                loads_of[key] = loads
                views.append(key)
        icon_at = {}
        if past:
            # the icon list R136+0x10168 ([DS:7E4C]:2838: y, x, code, ..., flag at +8, 10 bytes each, until flag -1)
            m, views = st.m, []
            seg = m.word(DS, 0x7E4C)
            icons = []
            for k in range(600):
                if m.word(seg, 0x2840 + 10 * k) == 0xFFFF:
                    break
                icons.append((m.word(seg, 0x283A + 10 * k), m.word(seg, 0x2838 + 10 * k)))
            top = base["top"]
            # icons with a road (24, which continues into any code from 24 up) in the map row above, next to them: the masks of the
            # buffer's row 49 there read the icon in G[50]
            road = [(x, y) for x, y in icons if y >= 51 and any(terrain_at(st, c, y - 1) == 24 for c in (x - 1, x, x + 1))]
            # icons three map rows below a minor river, a marsh or a ford (3, 4, 25): a marker there in buffer row 47 is lowered (lift -1..-3)
            # and the rectangle then shows the top of row 50, the icon itself
            river = [(x, y) for x, y in icons if y >= 53 and any(terrain_at(st, c, y - 3) in (3, 4, 25) for c in (x - 1, x, x + 1))]
            for x, y in river + road + rnd.sample(icons, len(icons)):
                rows = y - 50 - top + 1            # tope = top + rows - 1 = y - 50
                column = x - 8                     # the icon in buffer column 9
                if 1 <= rows <= 0x52 and 1 <= column <= base["width"] - 0x11 and (column, rows, top) not in views:
                    views.append((column, rows, top))
                    icon_at[(column, rows, top)] = 9
                if len(views) >= 10:
                    break
        if border:
            # (column, rows, top): column 0 at several heights (the Luxemburg / Nancy strip), row 0 (leaving Vordingborg: top 0), both,
            # and the controls column 1 / row 1 (no shift)
            views = [(0, 5, 400), (0, 20, 500), (0, 40, 600), (0, 20, 700), (150, 0, 0), (60, 0, 0), (0, 0, 0), (1, 20, 500), (150, 1, 0)]
        if south:
            views += [(base["column"] - 30, 20, base["top"]), (base["column"], 40, base["top"] + 100)]
        # the views with the most mountains and the most hills or water among a sweep of the whole map
        best = {}
        for top in (() if border or south or east or past else range(0, base["height"] - 0x83, 0x41)):
            for column in range(1, base["width"] - 0x11, 24):
                try:
                    setup(st, rec, column, 0x29, top)
                except Stop as e:
                    print("sweep", column, top, "stopped:", str(e)[:120], flush=True)
                    continue
                k = kinds_of(st)
                for want in ("mountains", "hills", "water"):
                    n = sum(len(v) for key, v in k.items() if key[0] == want)
                    if n > best.get(want, (0,))[0]:
                        best[want] = (n, column, 0x29, top)
        views += [v[1:] for v in best.values() if v[1:] not in views]
        if not border and not south and not east and not past:
            views.append((rnd.randint(1, base["width"] - 0x11), rnd.randint(1, 0x50), base["top"]))
        print(name, "views", views, "%.0f s" % (time.time() - t0), flush=True)
        for view in views:
            column, rows, top = view[:3]
            loads = loads_of.get(view)
            extra = {"loads": [[n, o] for n, o in loads]} if loads else {}
            try:
                setup(st, rec, column, rows, top, loads)
            except Stop as e:
                print(name, "view", column, rows, top, "stopped:", str(e)[:120], flush=True)
                continue
            if south:
                for y, x in [(y, rnd.randint(0x14, 0xE2)) for y in (175, 176, 177) for _ in range(6)]:
                    setup(st, rec, column, rows, top)
                    d, f = rnd.randint(0, 9), rnd.randint(0, 7)
                    run(st, rec, PARTY, [x, y, f, d])
                    if any(c[0] in ("sprite", "picture") and c[-1][0] == "segment" for c in rec.calls):
                        continue
                    tl, odd, fy, fx = party_cell(st, cells(st), x, y)
                    jobs.append({"state": name, **view_regs(st), "kind": "frame", "terrain": tl, "path": [[x, y, f, d]], "marks": [len(rec.calls)],
                                 "cells": list(cells(st)), "calls": rec.calls})
                continue
            kinds = kinds_of(st)
            picks = []
            if view in icon_at:
                # the marker in the view's last cell rows, its column at the icon's (buffer column 9) or next to it
                picks = [(rnd.randint(0x68, 0x88), y) for y in (0xAD, 0xAE, 0xAF, 0xB0, 0xB1) for _ in range(4)]
                picks += [(x, 0xB1) for x in range(0x68, 0x90, 2)]
            for key in (() if view in icon_at else sorted(kinds)):
                picks += rnd.sample(kinds[key], min(2 if key[0] in ("hills", "mountains", "water") else 1, len(kinds[key])))
            extra["past_block"] = past_block(st, column, rows)
            for x, y in picks:
                setup(st, rec, column, rows, top, loads)
                d, f = rnd.randint(0, 9), rnd.randint(0, 7)
                run(st, rec, PARTY, [x, y, f, d])
                tl, odd, fy, fx = party_cell(st, cells(st), x, y)
                jobs.append({"state": name, "state_top": base["top"], **view_regs(st), **extra, "kind": "frame", "terrain": tl, "path": [[x, y, f, d]],
                             "marks": [len(rec.calls)], "cells": list(cells(st)), "after": after(st), "calls": rec.calls})
            # walks: from a hill, mountain or water point, 60 steps changing direction every 12 (the marker kept 20 pixels off the view's
            # edges, so 0c44 would not scroll); every pass calls 16c6 once
            for _ in range(2):
                setup(st, rec, column, rows, top, loads)
                rough = [p for k, v in kinds.items() if k[0] in ("hills", "mountains", "water") for p in v] or [p for v in kinds.values() for p in v]
                x, y = rnd.choice(rough)
                steps, marks = [], []
                dx, dy = rnd.choice(DIRS)
                for step in range(60):
                    if step % 12 == 0:
                        dx, dy = rnd.choice(DIRS)
                    x = min(max(x + dx * rnd.randint(1, 2), 0x14), 0xE2)
                    y = min(max(y + dy, 0x14), 0xAA)
                    f, d = step % 8, rnd.randint(0, 9)
                    run(st, rec, PARTY, [x, y, f, d])
                    steps.append([x, y, f, d])
                    marks.append(len(rec.calls))
                jobs.append({"state": name, "state_top": base["top"], **view_regs(st), **extra, "kind": "walk", "terrain": -1, "path": steps, "marks": marks,
                             "cells": list(cells(st)), "after": after(st), "calls": rec.calls})
            print(name, "view", column, rows, top, extra, "frames", len(picks), "%.0f s" % (time.time() - t0), flush=True)
        st.m = None
        with gzip.open(out, "wt", encoding="utf-8") as f:
            json.dump({"saves": saves, "jobs": jobs}, f)
    print("jobs", len(jobs), "unknown pictures", sum(1 for j in jobs for c in j["calls"] if c[0] in ("sprite", "picture") and c[-1][0] == "segment"))


if __name__ == "__main__":
    main()
