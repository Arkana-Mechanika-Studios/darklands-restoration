"""Round scr1: scripted (not random) lockstep sessions that drive the open input paths of the screens.

usage: python -I -B gen_scr1.py [recipe ...] [--out DIR]     (no recipe: all)
Each recipe = the way into a screen (cut from an existing session) + a fixed sweep: every hover cell of a pointer area, every key of a screen's
command set once, boundary clicks. The lines for rerun_all.SESSIONS are printed.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from gen_screen_sessions import SESSIONS_DIR, cut

W = "wait:30"


def keys(seq, wait=W):
    out = []
    for k in seq:
        out += ["key:" + k, wait]
    return out


def hover(xs, ys, wait="wait:10"):
    """Pointer onto every cell of the grid (rows left to right, then the next row)."""
    out = []
    for y in ys:
        for x in xs:
            out += ["move:%d,%d" % (x, y), wait]
    return out


def shifted(k, wait=W):
    return ["shift:on", "key:" + k, wait, "shift:off"]


def r_f6(variant):
    """F6 party information (R034+0x0EE6): the small map's hover cells, Tab / Shift-Tab through the towns, a click inside, the borders."""
    out = cut("menubar", "menu").splitlines() + ["key:f6", "wait:300", "state:f6"]
    xs = list(range(0xC4, 0x143, 10))
    ys = list(range(0x48, 0xC6, 9))
    if variant == 1:
        out += hover(xs, ys)
        out += ["state:hover"]
        out += keys(["tab"] * 6)
        for _ in range(4):
            out += shifted("tab")
        out += ["state:tab"]
        out += keys(["tab"]) + ["move:230,120", "wait:10"] + keys(["tab", "tab"]) + ["state:tab2"]
    elif variant == 2:
        # the borders of the map window and the town box, the pointer leaving the map and coming back; Shift-Tab first
        for x, y in [(0xC6, 0x4A), (0xC7, 0x4B), (0x13F, 0xC2), (0x140, 0xC3), (0xC5, 0x80), (0x13F, 0x80), (0x100, 0x4A), (0x100, 0x4B), (0x100, 0xC2),
                     (0x100, 0xC3), (0x100, 0x20), (0x100, 0x80), (0x40, 0x80), (0x100, 0x80), (0xFF, 0x7F), (0x101, 0x81)]:
            out += ["move:%d,%d" % (x, y), "wait:10"]
        out += ["state:edges"] + shifted("tab") + shifted("tab") + keys(["tab", "tab"]) + ["move:200,100", "wait:10", "move:300,180", "wait:10", "state:s2"]
    elif variant == 3:
        out += keys(["tab"] * 40) + ["state:many"] + ["move:%d,%d" % (0x100, 0x90), "wait:10"] + keys(["tab"]) + ["move:%d,%d" % (0x120, 0x60), "wait:10"]
    out += ["key:esc", "wait:300", "state:end"]
    return "DKSAVE0.SAV", out


def r_pray(variant, member=3):
    """The prayer page (R034+0x022C): hover over the menu lines and the member box, clicks on the lines and the box rows, M / L at the limits."""
    out = cut("saints", "saints").replace("key:f3", "key:f%d" % member).splitlines()
    saint_y = 75 + 8 * (variant - 1)
    out += ["click:100,%d" % saint_y, "wait:300", "state:saint"]
    # the menu lines (x 149..317, y 158 + 8 row) and the box (x 251..309, y 143..191)
    for y in range(156, 194, 3):
        out += ["move:200,%d" % y, "wait:10"]
    out += ["state:menu"]
    for y in range(140, 196, 4):
        out += ["move:280,%d" % y, "wait:10"]
    out += ["move:170,175", "wait:10", "move:280,150", "wait:10", "move:280,160", "wait:10", "move:280,170", "wait:10", "move:280,185", "wait:10",
            "move:150,160", "wait:10", "move:317,190", "wait:10", "move:318,190", "wait:10", "move:148,158", "wait:10", "move:149,159", "wait:10", "state:hover"]
    out += keys(["m", "m", "m", "m"]) + ["state:more"]
    # the Pray line lights (and with a chance above 0 opens the member box); then through the box's rows, out to the menu and back in
    out += ["move:200,175", "wait:10"]
    for y in (143, 151, 160, 169, 178, 187, 191):
        out += ["move:280,%d" % y, "wait:10"]
    out += ["move:200,166", "wait:10", "move:200,175", "wait:10", "move:280,150", "wait:10", "state:box"]
    out += keys(["l", "l", "l", "l", "l", "l", "l"]) + ["state:less"]
    out += keys(["m", "m"]) + ["move:200,175", "wait:10", "move:280,%d" % (146 + 9 * (variant - 1)), "wait:10"]
    out += ["click:280,%d" % (146 + 9 * (variant - 1)), "wait:300", "state:pray"]
    out += ["click:200,185", "wait:300", "state:end"]
    return "Dksave10.sav", out


def r_load(variant):
    """The title and the load screen (R037+0x9C48 / 0x9DB2 / 0x81B2 / 0x84BA / 0x8896): the button rows' hover cells and clicks, the list rows by click and by
    arrows / Home / End, the third (return) row, D with its question answered no, Alt-Q answered no; then T and L load the game.
    No state step before the game is loaded (the start screen has none to write)."""
    out = ["key:t", "wait:5"]
    if variant == 1:
        # the load screen: hover over the buttons (x 0x45..0xC5, y 0x2E..0x59) and past their borders, then the list's rows and the borders
        for y in (0x2D, 0x2E, 0x3A, 0x3B, 0x3C, 0x48, 0x49, 0x4A, 0x58, 0x59, 0x5A, 0x5B, 0x70):
            for x in (0x44, 0x45, 0x80, 0xC5, 0xC6):
                out += ["move:%d,%d" % (x, y), "wait:10"]
        for y in (0x14, 0x15, 0x24, 0x25, 0x34, 0x44, 0x64, 0x94, 0xA4, 0xA5):
            out += ["click:%d,%d" % (0xC4 if y & 1 else 0xF0, y), W]
        out += ["click:%d,%d" % (0x13F, 0x30), W, "click:%d,%d" % (0x140, 0x30), W, "click:%d,%d" % (0xC3, 0x30), W]
        out += keys(["down", "down", "up", "up", "up", "end", "end", "down", "home", "right", "left", "left", "pgdn", "x"])
        out += ["click:100,%d" % 0x2F, "wait:300", "state:loaded"]
    elif variant == 2:
        # D answered no (n, then Esc), Alt-Q answered no, a click on the second row (delete) answered no; the third row returns to the title,
        # where T loads again
        out += keys(["d", "n", "D", "esc"])
        out += keys(["altq", "n", "altq", "esc"])
        out += ["click:100,%d" % 0x3F, "wait:100", "key:n", "wait:100", "click:100,%d" % 0x4F, "wait:200"]
        out += ["key:x", W, "key:enter", W, "key:t", "wait:50", "key:r", "wait:200", "key:T", "wait:100", "key:L", "wait:300", "state:loaded"]
    else:
        # the title's clicks: the bands of y (0x33 .. 0x95), margins and the bottom strip that pick nothing; then a click on the third band loads
        out = []
        for y in (0, 0x10, 0x31, 0x32, 0x92, 0x93, 0x95, 0x96, 0x97, 0xA0, 0xC7):
            out += ["click:%d,%d" % (0x60, y), "wait:30"]
        for x in (0, 0x20, 0x50, 0x51, 0xF0, 0x13F):
            out += ["click:%d,%d" % (x, 0x92), "wait:30"]
        out += keys(["x", "space", "enter", "esc", "R"])
        out += ["click:100,%d" % 0x7A, "wait:100", "key:l", "wait:300", "state:loaded"]
    return "DKSAVE0.SAV", out


def r_party(variant):
    """The create-a-party menu (R037+0x0444 / 0x0000, from the start screen): add, delete, kill, heraldry, figure and the three colour keys with their arrows,
    the command rows by click; Begin at the end (no state before: there is no game).
    Variant 4: every character killed, then A and B: Begin is refused as the original refuses it (R037+0x087A: the roster count DS:E7DC, which
    R037+0x2760 takes from charactr.tmp after each kill, must be above 0, then the party size from the slot ids; neither holds) and the menu stays;
    a state there would compare the original's saved-game writer run with no game against the C# host's "no game", so R leaves for the start
    screen, C opens the menu again (R037+0x0000 reloads the ready-made roster from the saved game "default"), and A, B begin (a game begun at the first
    B would take R and C as a card's keys). No --save: the tester's machine has that saved game only without one."""
    out = ["key:c", "wait:5"]
    if variant == 4:
        out += keys(["k"] * 6 + ["a"], "wait:50")
        out += ["key:b", "wait:300", "key:r", "wait:100", "key:c", "wait:200", "key:a", "wait:50", "key:b", "wait:300", "state:begun"]
        return None, out
    if variant == 1:
        out += keys(["a"], "wait:50")
        for c in "123":
            out += keys([c], "wait:20")
            for k in ("up",) * 6 + ("down",) * 6 + ("left",) * 6 + ("right",) * 6:
                out += keys([k], "wait:10")
        out += keys(["h"] * 17 + ["s"] * 7, "wait:20")
    elif variant == 2:
        for _ in range(5):
            out += keys(["a", "down"], "wait:50")
        out += keys(["up", "h", "h", "s", "s", "1", "2", "3", "up", "d", "down", "k", "up", "up", "a", "h", "left", "right", "d", "d", "k", "k", "a"], "wait:30")
    else:
        out += keys(["a", "down", "a", "down", "a"], "wait:50")
        for row in (1, 2, 3, 4, 5, 6, 7, 8, 1, 4, 5, 2):
            out += ["click:%d,%d" % (0x60, 0x1A + 15 * row + 5), "wait:40"]
        for r in range(0, 7):
            out += ["click:%d,%d" % (0xD0, 0x2E + 15 * r + 5), "wait:30"]
        out += ["click:%d,%d" % (0x60, 0x1A + 15 * 8 + 5), "wait:30", "click:%d,%d" % (0x60, 0x1A + 15 * 8 + 5), "wait:30"]
    out += ["key:a", "wait:30", "key:b", "wait:300", "state:begun"]
    return None, out


def r_card(variant):
    """The city card's menu (R043+0x1BD2 / 0x33C6 / 0x32B6): Up / Down wrapping from a hovered line, the F keys (sheets, party information; an empty party slot),
    Esc with the quit question answered no, Enter on a line; the keys again at the Expert difficulty (Alt-D) and with the party order changed."""
    out = cut("menubar", "menu").splitlines()
    if variant == 2:
        out += ["key:altd", "wait:300", "key:enter", "wait:300", "state:expert"]
    out += ["option:0", "wait:10"]
    out += keys(["up", "up", "down"] + ["down"] * 9, "wait:20")
    out += ["state:wrap"]
    for f in ("f1", "f2", "f3", "f4", "f5", "f6"):
        out += ["key:" + f, "wait:150", "key:esc", "wait:150"]
        if variant == 2 and f == "f3":
            out += ["state:f3"]
    out += ["state:fkeys", "option:1", "wait:10", "key:up", "wait:20", "key:esc", "wait:100", "key:n", "wait:100", "state:esc"]
    out += ["key:f6", "wait:150", "key:tab", "wait:100", "key:esc", "wait:150", "option:1", "wait:10", "key:down", "wait:20", "key:down", "wait:20", "key:enter", "wait:300", "state:end"]
    return "DKSAVE0.SAV", out


def r_create(variant):
    """The character generator (R037+0x4664 / 0x4BEE / 0x5D98) from the inn roster: name, nickname and sex, a family background, the childhood attributes
    (rows by click: the first click selects, the next raises; the minus area), then `variant` * 2 occupations of five years each with skill clicks,
    and Begin Adventuring. The choices of each stage come from a seeded generator (the structure is fixed)."""
    import random
    rng = random.Random("scr1-create-%d" % variant)
    out = cut("roster-create2", "generator").splitlines()
    out += ["key:e", "wait:100"] + keys(rng.choice(["ab", "lunsmann", "x"]), "wait:30") + ["key:enter", "wait:100"]
    if variant % 2:
        out += ["key:c", "wait:100", "key:q", W, "key:enter", "wait:100"]
    out += ["key:m", "wait:50", "state:named"]
    out += ["key:b", "wait:300", "state:family"]
    out += ["click:120,%d" % (0x2D + 11 * rng.randrange(0, 8) + 3), "wait:100", "state:childhood"]
    for _ in range(6):
        r = rng.randrange(0, 6)
        out += ["click:200,%d" % (0x12 + 8 * r + 3), "wait:60"] + ["click:200,%d" % (0x12 + 8 * r + 3), "wait:60"] * rng.randrange(1, 4)
    out += ["click:%d,%d" % (0xD0, 0x70), "wait:60", "key:up", W, "key:down", W, "state:spent"]
    out += ["click:120,52", "wait:100", "state:occupations"]
    for term in range(variant * 2):
        out += ["click:120,%d" % (0x2D + 11 * rng.randrange(0, 14) + 3), "wait:100"]
        for _ in range(rng.randrange(2, 6)):
            r = rng.randrange(0, 14)
            out += ["click:300,%d" % (0x12 + 8 * r + 3), "wait:60"] + ["click:300,%d" % (0x12 + 8 * r + 3), "wait:60"] * rng.randrange(1, 3)
        if term % 2:
            out += ["click:%d,%d" % (0xD0, 0x70), "wait:60"]
        out += ["state:term%d" % term, "click:120,52", "wait:100"]
    out += ["click:120,%d" % (0x2D + 11 * rng.randrange(0, 6) + 3), "wait:100", "click:120,63", "wait:1500", "state:begun", "key:esc", "wait:2500", "state:back"]
    return "DKSAVE8.SAV", out


def r_camp(variant):
    """The camp / stay screen (R036+0x0082 / 0x08EC): every member's sheet by F1..F5 (and back), the member keys 1..5 (an empty slot too), the arrows round the
    party, each activity key for each member, A (the bench; L leaves it), T, a day spent between; variant 3 ends with Alt-Q (the game is quit)."""
    if variant == 2:
        save, out = "DKSAVE99.SAV", cut("camp-wild-brew", "camp").splitlines()
    else:
        save, out = "Dksave10.sav", cut("stay", "stay").splitlines()
    for f in ("f1", "f2", "f3", "f4", "f5"):
        out += ["key:" + f, "wait:150", "key:esc", "wait:150"]
    out += ["state:sheets"]
    out += keys(["5", "4", "3", "2", "1", "up", "up", "down", "down", "down", "down", "down", "left", "right", "x"], "wait:30") + ["state:moves"]
    for m in "1234":
        out += keys([m, "j", "r", "p", "e", "g"], "wait:30")
    out += ["state:acts"]
    # camp-2: the Esc after a sheet has left the wild camp (both sides), so T, A and L are the map's keys: A rests (R030+0x0C44 case 0x1E41) until a
    # key; the original side gives the next key only when the game is idle, and the rest draws random numbers every step, so the original
    # rests until the encounter (card 0xA5, 53 hours on) and takes L at the card: the C#'s wait must outlast those steps (200 frames gave 11)
    out += keys(["t"], "wait:100") + keys(["a"], "wait:%d" % (2400 if variant == 2 else 200)) + ["key:l", "wait:200"] + ["state:alch"]
    out += ["key:s", "wait:%d" % 300, "state:day", "key:5", W, "key:j", W, "key:s", "wait:300", "state:day2"]
    if variant == 3:
        out += ["key:altq", "wait:300"]
    else:
        out += ["key:l", "wait:300"]
    out += ["state:end"]
    return save, out


RECIPES = {}
for _v in (1, 2, 3):
    RECIPES["load-%d" % _v] = (lambda v=_v: r_load(v))
for _v in (1, 2, 3, 4):
    RECIPES["party-%d" % _v] = (lambda v=_v: r_party(v))
for _v in (1, 2, 3):
    RECIPES["camp-%d" % _v] = (lambda v=_v: r_camp(v))
for _v in (1, 2):
    RECIPES["card-%d" % _v] = (lambda v=_v: r_card(v))
for _v in (1, 2, 3):
    RECIPES["create-%d" % _v] = (lambda v=_v: r_create(v))
for _v in (1, 2):
    RECIPES["f6-%d" % _v] = (lambda v=_v: r_f6(v))
for _v in (1, 2, 3, 4):
    RECIPES["pray-%d" % _v] = (lambda v=_v: r_pray(v))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("recipes", nargs="*")
    ap.add_argument("--out", default=str(SESSIONS_DIR))
    a = ap.parse_args()
    for r in a.recipes or sorted(RECIPES):
        save, lines = RECIPES[r]()
        name = "scr1-" + r
        (Path(a.out) / (name + ".steps")).write_text("# Generated by gen_scr1.py (%s): a scripted sweep of a screen's open input paths\n" % r + "\n".join(lines) + "\n",
                                                      encoding="utf-8", newline="\n")
        print('"%s": %s,' % (name, '"%s"' % save if save else "None"))


if __name__ == "__main__":
    main()
