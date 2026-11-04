"""Direct calls of the ORIGINAL's card text layout from a captured card-screen state: text-paint-wrapped root 0265:0134 and
wrapped-text-size root 0265:0654, with generated texts and settings, the driver's drawing entries recorded instead of drawn (a tools
addition: it changes none of the original-side modules, so no recording expires).

usage: python -I -B text_calls.py <state> <out folder> [--seed 1] [--synthetic 400] [--msg all|<n>] [--cov]

The emulator loads no graphics driver: its slot table answers 0, the font metrics (glyph-width 047C:04E3, font-line-height 047C:051B) follow
FONTS.FNT (orig_side). Here the text-draw entry gfx-text-draw 047C:055A(window, x, y, string) and the picture entry 047C:05A5(window, x, y,
picture) are recorded with the window's words (colour +0x0C, font +0x10) as they are at the call. The rewrite's test
(tests/Darklands.Rendering.Tests/TextLayoutCallsTests.cs) draws those recorded calls with its own glyph painter on one screen and the same
input through CardTextLayout on another, and compares them pixel by pixel, the hot-spot tables and the measured height.

Inputs (one job each): the text (written at DS:9085, the card engine's text buffer, at most 1,500 bytes, followed by 64 zero bytes: a text
that ends right after a conditional marker is read past its terminating zero, see CardTextLayout's DIVERGENCE on the buffer's leftovers), the column (width, x, y), the line
height `spacing` and `leading`, `stop` (stop at the first option line), `icon` (the drop cap), the option colour switch (argument 12: 1 =
the window's colour becomes 0x380 at the first option marker), the alignment byte DS:01BC, the condition flags DS:9C30[20] (one per
conditional passage, by order of appearance), the option states of the hot-spot table DS:E96E+0x2E[20] (0 hidden, 1 choosable, 2 shown),
and the window's colour and font. Texts: every text block of every card's .MSG file (with settings drawn per job) and synthetic edge cases.
wrapped-text-size is called with its callers' constant arguments (line height of the window's font, no stop, leading or drop cap).
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
from card_oracle import MessageFiles  # noqa: E402
from dosmachine import DS, P, linear  # noqa: E402

DRIVER = P(0x047C)
WINDOW = 0x016A          # the card screen's window (the one the card engine passes)
TEXT = 0x9085            # the card engine's text buffer
CONDITIONS = 0x9C30      # condition flags, one byte per conditional passage
HOTSPOTS = 0xE96E        # struct hotspots: count, width, x, y[20], height[20], enabled[20], index[20], type[20] (words)
ALIGN = 0x01BC
DROP_CAPS = 0xA65E       # picture handles of the drop caps, by letter (DS:A65E + 2 * ch)
MAX_TEXT = 1500
PAINT, SIZE = "root 0265:0134", "root 0265:0654"


class Recorder:
    """The driver's text and picture entries recorded (they draw nothing in the emulator)."""

    def __init__(self, st: wc.WState):
        self.st, self.m = st, st.m
        self.calls: list = []
        m = self.m
        self.caps = {m.word(DS, DROP_CAPS + 2 * ch): ch for ch in range(0x41, 0x5B)}
        for off, h in ((0x055A, self.text), (0x05A5, self.picture)):
            lin = linear(DRIVER, off)
            m.stubs.setdefault(lin, []).insert(0, (m.owner(DRIVER, off, "root"), h))

    def window(self, off):
        return list(struct.unpack("<9h", bytes(self.m.read(DS, off, 18))))

    def text(self, m):
        ss = m.reg("SS")
        s = bytes(m.read(ss, m.arg(3), 400))
        s = s[:s.index(0)] if 0 in s else s
        self.calls.append(["text", m.sarg(1), m.sarg(2), s.hex(), self.window(m.arg(0))])
        return (0, 0)

    def picture(self, m):
        seg = m.arg(3)
        self.calls.append(["picture", m.sarg(1), m.sarg(2), self.caps.get(seg, -1), self.window(m.arg(0))])
        return (0, 0)


def synthetic(r: random.Random, n: int) -> list[bytes]:
    """Edge cases of the layout: long words, hyphens, indents, paragraphs, conditional passages, option lines, empty segments."""
    words = [b"a", b"the", b"Darklands", b"Holy-Roman", b"well-kept", b"Wolfenbuettel", b"x" * 40, b"ABCDEFGHIJKLMNOPQRSTUVWXYZ" * 2, b"--", b"-",
             b"\"Yes,\"", b"don't", b"Nuernberg", b"\x81red\xff", b"1,000", b"...", b"  "]
    out = [b"", b" ", b"\n", b"\x14", b"\x15", b"A", b"Z\n", b"-" * 60, b"x" * 300, b" " * 50 + b"end", b"\n\n\n", b"\x15\x15\x15",
           b"\x01\x13", b"\x01hidden\x13shown", b"Text\x1dindented line that goes on and on and on past the column\nnext",
           b"\x15Option one\n\x10Items\n\x16Saints\n\x06Fight\n\x15Last", b"Lead\n\x14\x14\x15Leave"]
    for _ in range(n):
        parts = []
        for _ in range(r.randint(1, 40)):
            k = r.random()
            if k < 0.62:
                parts.append(r.choice(words) + (b" " if r.random() < 0.8 else b""))
            elif k < 0.70:
                parts.append(b"\n")
            elif k < 0.74:
                parts.append(b"\x14")
            elif k < 0.78:
                parts.append(b"\x1d")
            elif k < 0.86:
                parts.append(b"\x01" + r.choice(words) + b" " + r.choice(words) + b"\x13")
            elif k < 0.97:
                parts.append(b"\n" + bytes([r.choice((0x15, 0x15, 0x10, 0x16, 0x06))]) + r.choice(words) + b" " + r.choice(words))
            else:
                parts.append(b"\r")
        t = b"".join(parts)
        if r.random() < 0.3:
            t = bytes([r.randint(0x41, 0x5A)]) + t
        out.append(t[:MAX_TEXT])
    return out


def settings(r: random.Random, text: bytes, kind: str) -> dict:
    card = kind == "msg" and r.random() < 0.5
    return {
        "width": 230 if card else r.choice((0x1C, 40, 60, 100, 150, 182, 222, 230, 250)),
        "x": 15 if card else r.choice((0, 5, 15, 71)),
        "y": 15 if card else r.choice((0, 11, 15, 27)),
        "spacing": 8 if card else r.choice((6, 7, 8, 9)),
        "leading": 1 if card else r.choice((0, 1, 1, 2)),
        "stop": 0 if card else int(r.random() < 0.25),
        "icon": 1 if card else int(r.random() < 0.6),
        "option_colour": 1 if card else int(r.random() < 0.6),
        "align": 0 if card else r.choice((0, 0, 1, 2)),
        "conditions": [int(r.random() < 0.6) for _ in range(20)],
        "options": [r.choice((1, 1, 1, 0, 2)) for _ in range(20)],
        "colour": r.choice((0, 15, 0x0E, 7)),
        "font": 3 if card else r.choice((1, 2, 3)),
        "size_init": int(r.random() < 0.5),
    }


def run_job(st: wc.WState, rec: Recorder, text: bytes, s: dict, budget: int = 8_000_000) -> dict:
    win = st.m.read(DS, WINDOW, 18)
    win = bytearray(win)
    struct.pack_into("<h", win, 0x0C, s["colour"])
    struct.pack_into("<h", win, 0x10, s["font"])
    # the size call reads the option states of its own table too (DS:EA64 with init 1; with the table 0 it reads DS:002E.., left as it is)
    edits = [(TEXT, text + bytes(64)), (CONDITIONS, bytes(s["conditions"])), (ALIGN, bytes([s["align"]])), (HOTSPOTS + 0x2E, bytes(s["options"])),
             (0xEA64 + 0x2E, bytes(s["options"])), (WINDOW, bytes(win))]
    args = [WINDOW, TEXT, s["width"], s["x"], s["y"], s["spacing"], HOTSPOTS, 1, s["stop"], s["leading"], s["icon"], s["option_colour"]]
    # wrapped-text-size as its four callers make it (root 0000:1AFE / 0000:1C68, R043+0x2972, R043+0x2B05): the window font's line height,
    # no stop, no leading, no drop cap; the hot-spot table 0 with init 0, or DS:EA64 with init 1
    f = st.m._font_of(s["font"])
    size_args = [WINDOW, TEXT, s["width"], s["x"], s["y"], f[3] + f[5]] + ([0xEA64, 1] if s["size_init"] else [0, 0]) + [0, 0, 0]
    out = {}
    for coord in (PAINT, SIZE):
        if st.calls_since_build + 1 > wc.REBUILD_EVERY:
            st.rebuild()                       # a fresh machine (world_calls does it every REBUILD_EVERY calls): the recorder's stubs again
            st.calls_since_build = 0
            rec.__init__(st)
        rec.calls.clear()
        res = st.call(coord, args if coord == PAINT else size_args, ds_edits=edits, keep_state=True, saves=False, budget=budget)
        after = res.get("after") or b""
        hs = None
        if coord == PAINT and after:
            h = after[HOTSPOTS:HOTSPOTS + 0xF6]
            n = h[0]
            hs = {"count": n, "width": struct.unpack_from("<h", h, 2)[0], "x": struct.unpack_from("<h", h, 4)[0],
                  "y": list(h[6:6 + n]), "height": list(h[0x1A:0x1A + n]), "enabled": list(h[0x2E:0x2E + 20]), "index": list(h[0x42:0x42 + n]),
                  "type": [struct.unpack_from("<h", h, 0x56 + 2 * k)[0] for k in range(n)]}
        out[coord] = {"ax": res["ax"], "dx": res["dx"], "error": res["error"], "calls": list(rec.calls) if coord == PAINT else None, "hotspots": hs,
                      "coverage": res["coverage"], "instructions": res["instructions"]}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("state")
    ap.add_argument("out")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--synthetic", type=int, default=400)
    ap.add_argument("--msg", default="all", help="all, or the number of .MSG texts to take")
    ap.add_argument("--game", default=str(wc.GAME))
    ap.add_argument("--part", default="0/1", help="k/n: this run takes every n-th input from the k-th (runs side by side)")
    ap.add_argument("--budget", type=int, default=8_000_000, help="instructions a call may run (a few synthetic texts loop for ever)")
    a = ap.parse_args()
    r = random.Random(a.seed)
    st = wc.WState(Path(a.state), a.game, coverage=True)
    rec = Recorder(st)
    texts = []
    mf = MessageFiles(Path(a.game) / "MSGFILES")
    for name in sorted(mf.entries):
        if name.endswith(".msg"):
            for k, b in enumerate(mf.blocks(name[:-4])):
                texts.append(("msg", "%s#%d" % (name[:-4], k), b[:MAX_TEXT]))
    if a.msg != "all":
        r.shuffle(texts)
        texts = texts[:int(a.msg)]
    texts += [("syn", "syn%04d" % k, t) for k, t in enumerate(synthetic(r, a.synthetic))]
    k0, n0 = map(int, a.part.split("/"))
    texts = list(enumerate(texts))[k0::n0]
    out = Path(a.out)
    (out / "cov").mkdir(parents=True, exist_ok=True)
    jobs = []
    with open(out / "cov" / ("cov-text-calls-%d.jsonl" % k0), "w", encoding="utf-8") as cov:
        for n, (k, (kind, name, text)) in enumerate(texts):
            s = settings(random.Random(a.seed * 100003 + k), text, kind)
            got = run_job(st, rec, text, s, a.budget)
            jid = "%s-%05d" % (kind, k)
            for coord, g in got.items():
                cov.write(json.dumps({"unit": coord, "state": "%s#%s" % (Path(a.state).stem, jid), **g.pop("coverage")}) + "\n")
            jobs.append({"id": jid, "name": name, "text": text.hex(), "settings": s, "paint": got[PAINT], "size": got[SIZE]})
            if n % 200 == 0:
                print(n, "of", len(texts), flush=True)
    with gzip.open(out / ("jobs-%d.json.gz" % k0 if n0 > 1 else "jobs.json.gz"), "wt", encoding="utf-8") as f:
        json.dump({"state": Path(a.state).name, "jobs": jobs}, f)
    print("jobs", len(jobs), "errors", sum(1 for j in jobs if j["paint"]["error"] or j["size"]["error"]))


if __name__ == "__main__":
    main()
