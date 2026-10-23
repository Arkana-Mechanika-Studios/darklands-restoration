"""Generate short lockstep sessions (lockstep-sessions/gen-*.steps) that sweep one screen's controls with seeded key / mouse walks.

usage: python -I -B gen_screen_sessions.py <screen> <first seed> <count> [--steps N] [--out DIR]
screens: see SCREENS. Each session = the screen's prefix (the way into the screen, cut from an existing session) + N input steps drawn
from the screen's alphabet by a seeded generator + a state step every STATE_EVERY inputs and at the end. The lines for
rerun_all.SESSIONS (name -> saved game) are printed.
"""
from __future__ import annotations

import argparse
import random
from pathlib import Path

HERE = Path(__file__).resolve().parent
SESSIONS_DIR = HERE / "lockstep-sessions"
STATE_EVERY = 8
NO_STATES = {"party"}       # the party menu of the start screen has no game to write until Begin the Adventure


def cut(session: str, until_state: str) -> str:
    """The steps of an existing session up to (and with) its `state:<until_state>` step: the way into a screen."""
    out = []
    for l in (SESSIONS_DIR / (session + ".steps")).read_text(encoding="utf-8").splitlines():
        l = l.strip()
        if not l or l.startswith("#"):
            continue
        out.append(l)
        if l == "state:" + until_state:
            return "\n".join(out)
    raise SystemExit("%s has no state %s" % (session, until_state))


ARROWS = ["up", "down", "left", "right"]
SHEET_BOX_CLICKS = [(160, 6), (290, 6), (127, 189), (226, 47), (290, 45), (290, 108), (226, 115), (290, 168), (226, 205), (226, 155), (226, 171), (226, 187),
                    (30, 20), (30, 60), (30, 100), (30, 140), (30, 180)]


# the sheet's boxes: (flags, up, down, left, right) of the table at R136+0xBBE7, centre of each
BOXES = [(12, 0, 3, 12, 1), (12, 1, 4, 0, 1), (12, 0, 2, 16, 10), (13, 0, 6, 13, 4), (13, 1, 5, 3, 4), (13, 4, 7, 6, 5), (13, 3, 9, 14, 5), (13, 5, 7, 9, 7),
         (12, 11, 10, 2, 7), (0x5F, 6, 10, 14, 7), (0x8F, 9, 11, 15, 7), (0x8F, 10, 10, 15, 7), (0, 12, 13, 12, 0), (0, 12, 14, 13, 17), (0, 13, 15, 14, 6),
         (0, 14, 16, 15, 9), (0, 15, 16, 16, 10)]


PANELS = {9: (62, 30, 137, 134), 10: (62, 46, 137, 134), 11: (62, 62, 137, 134)}


class SheetWalk:
    """Follows the sheet's cursor, its open box and the member shown, so that the walk does not leave the sheet by accident
    (the own member's F key, portrait row or Enter on its box close it; the walk then lands on the city menu)."""

    def __init__(self, members=4):
        self.cursor, self.open, self.who = 0, False, 0
        self.members = members              # the members whose sheets the walk may show (F1..Fn, party rows)

    def step(self, rng: random.Random) -> list[str]:
        c, flags = self.cursor, BOXES[self.cursor][0]
        opens, is_list = flags & 1, flags & 2
        r = rng.random()
        if self.open and is_list and rng.random() < 0.18:
            # the open list's panel: its up / down scroll arrows (the button held) and a line, by click
            px, py, pw, ph = PANELS[c]
            x = px + rng.randrange(8, pw - 8)
            y = rng.choice([py + 4, py + ph - 8, py + 9 + 8 * rng.randrange(0, 14), py + 9 + 8 * rng.randrange(0, 14)])
            return ["click:%d,%d" % (x, y)]
        if r < 0.28:
            k = rng.choice(ARROWS)
            if self.open and is_list and k in ("up", "down"):
                return ["key:" + k]
            target = BOXES[c][{"up": 1, "down": 2, "left": 3, "right": 4}[k]]
            if target >= len(BOXES):            # box 13's right neighbour is the table's dormant entry 17
                k, target = "left", BOXES[c][3]
            self.open = False
            self.cursor = target
            return ["key:" + k]
        if r < 0.42:
            if 12 <= c <= 16:
                if c - 12 == self.who or c - 12 >= self.members:
                    return ["key:" + rng.choice(["up", "down"])] if False else ["key:right"]
                self.who = c - 12
                return ["key:enter"]
            if opens:
                self.open = not self.open
            return ["key:enter"]
        if r < 0.50:
            return ["key:" + rng.choice(["pgup", "pgdn", "home", "end"])]
        if r < 0.58:
            return ["key:" + rng.choice(["a", "u", "d", "p"])]
        if r < 0.66:
            return ["key:" + rng.choice("12345")]
        if r < 0.70:
            return ["shift:on", "key:" + rng.choice(["d", "1", "2", "3", "4"]), "shift:off"]
        if r < 0.78:
            m = rng.randrange(0, self.members)
            if m == self.who:
                m = (m + 1) % self.members
            self.who = m
            return ["key:f%d" % (m + 1)]
        if r < 0.82:
            return ["key:" + rng.choice(["x", "space", "m", "z", "tab"])]
        if r < 0.95:
            i = rng.randrange(0, 12)
            x, y = SHEET_BOX_CLICKS[i]
            if self.open and self.cursor == i:
                self.open = False
            else:
                self.cursor, self.open = i, bool(BOXES[i][0] & 1)
            return ["click:%d,%d" % (x, y)]
        m = rng.randrange(0, self.members)
        if m == self.who:
            m = (m + 1) % self.members
        self.who = m
        return ["click:30,%d" % (m * 40 + 20)]


def sheet_alphabet(rng: random.Random) -> list[str]:
    if not hasattr(rng, "sheet"):
        rng.sheet = SheetWalk()
    return rng.sheet.step(rng)


def sheet3_alphabet(rng: random.Random) -> list[str]:
    """The sheets of the first three members only (the fourth's picture is missing from the game folder: the original ends there)."""
    if not hasattr(rng, "sheet"):
        rng.sheet = SheetWalk(3)
    return rng.sheet.step(rng)


class PackSweep:
    """The pack box opened (three Downs and Enter from the name box), then down the lines applying the hot keys; a member's pack after the
    other's: P (drink), A (ready), U (unready), D (drop, Shift: the stack), 1..5 (give, Shift: the stack)."""

    def __init__(self, members):
        self.members, self.who, self.line, self.ready = members, 0, 0, False

    def step(self, rng: random.Random) -> list[str]:
        if not self.ready:
            self.ready = True
            return ["key:down", "key:down", "key:down", "key:enter"]
        r = rng.random()
        if r < 0.14:
            self.line += 1
            return ["key:down"]
        if r < 0.20:
            self.line = max(0, self.line - 1)
            return ["key:up"]
        if r < 0.50:
            return ["key:p"]
        if r < 0.62:
            return ["key:" + rng.choice("au")]
        if r < 0.72:
            return ["key:d"] if rng.random() < 0.6 else ["shift:on", "key:d", "shift:off"]
        if r < 0.86:
            k = rng.choice("12345")
            return ["key:" + k] if rng.random() < 0.65 else ["shift:on", "key:" + k, "shift:off"]
        if r < 0.90:
            return ["key:" + rng.choice(["pgdn", "pgup", "home", "end"])]
        if r < 0.97 and self.members > 1:
            # another member's pack: its F key leaves this sheet for that member's (the own member's key would close the sheet)
            m = rng.randrange(0, self.members)
            if m == self.who:
                m = (m + 1) % self.members
            self.who, self.line, self.ready = m, 0, False
            return ["key:f%d" % (m + 1)]
        return ["key:down"]


def packsweep_alphabet(members):
    def alphabet(rng):
        if not hasattr(rng, "pack"):
            rng.pack = PackSweep(members)
        return rng.pack.step(rng)
    return alphabet


def saints_alphabet(rng: random.Random) -> list[str]:
    """The saints box of the sheet and the prayer page (R034): lines by click, the offer more / less (M, L), P prays."""
    r = rng.random()
    if r < 0.28:
        return ["key:" + rng.choice("mlp")]
    if r < 0.40:
        return ["key:" + rng.choice(["up", "down"])]
    if r < 0.60:
        return ["click:%d,%d" % (rng.randrange(70, 190), 75 + 8 * rng.randrange(0, 8))]
    if r < 0.70:
        return ["key:" + rng.choice(["enter", "pgdn", "pgup", "home", "end", "x"])]
    if r < 0.80:
        return ["key:p", "key:p"]
    if r < 0.88:
        return ["key:esc"]
    if r < 0.94:
        return ["key:down", "key:enter"]
    return ["key:m", "key:m", "key:m", "key:p"]


def bench_alphabet(rng: random.Random) -> list[str]:
    """The alchemist's bench (R033): formula page, material and batch lines, brewing; no way out (l, Esc) but at the end."""
    r = rng.random()
    if r < 0.30:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.62:
        return ["key:" + rng.choice("umfsjgad")]
    if r < 0.70:
        return ["key:" + rng.choice("123456789")]
    if r < 0.76:
        return ["key:" + rng.choice(["enter", "space", "pgup", "pgdn", "home", "end", "x"])]
    if r < 0.80:
        return ["shift:on", "key:" + rng.choice("umfsj"), "shift:off"]
    return ["click:%d,%d" % (rng.randrange(0, 320), rng.randrange(20, 195))]


def barter_alphabet(rng: random.Random) -> list[str]:
    """The page code of barter (R032), cache (R035) and loot (R040): menu lines, two item lists with arrows, the party column."""
    r = rng.random()
    if r < 0.30:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.62:
        return ["key:" + rng.choice("psdbgc")]
    if r < 0.68:
        return ["key:" + rng.choice("12345")]
    if r < 0.74:
        return ["shift:on", "key:" + rng.choice("psdbg1234"), "shift:off"]
    if r < 0.78:
        return ["key:" + rng.choice(["x", "space", "enter", "l", "m", "pgup", "pgdn", "home", "end", "tab"])]
    if r < 0.84:
        return ["key:" + rng.choice("psgd").upper()]
    if r < 0.86:
        return ["move:%d,%d" % (rng.randrange(0x52, 0xE6), rng.randrange(0x1C, 0x43))]
    where = rng.random()
    if where < 0.30:
        return ["click:%d,%d" % (rng.randrange(0x53, 0x10B), rng.randrange(0x56, 0x75))]
    if where < 0.60:
        return ["click:%d,%d" % (rng.randrange(0x73, 0x11C), rng.randrange(0x94, 0xB3))]
    if where < 0.75:
        return ["click:%d,%d" % (rng.randrange(0x52, 0x10B), rng.choice([0x50, 0x78]))]
    if where < 0.87:
        return ["click:%d,%d" % (rng.randrange(0x72, 0x12B), rng.choice([0x8E, 0xB8]))]
    if where < 0.95:
        return ["click:%d,%d" % (rng.randrange(0x52, 0xE6), rng.randrange(0x1C, 0x43))]
    return ["click:%d,%d" % (rng.randrange(2, 0x3A), rng.randrange(0, 200))]


def camp_alphabet(rng: random.Random) -> list[str]:
    """The camp / stay screen (R036) and the alchemy bench (R033) it opens."""
    r = rng.random()
    if r < 0.22:
        return ["key:" + rng.choice("12345")]
    if r < 0.50:
        return ["key:" + rng.choice("eprtgax")]
    if r < 0.60:
        return ["key:" + rng.choice(["s", "s", "s", "enter"])]
    if r < 0.78:
        return ["key:" + rng.choice(ARROWS + ["u", "m", "f", "j", "g", "enter", "pgup", "pgdn", "home", "end"])]
    if r < 0.84:
        return ["key:" + rng.choice(["f1", "f2", "f3"]), "key:esc"]      # a sheet and back (Esc alone would leave the camp)
    if r < 0.88:
        return ["shift:on", "key:" + rng.choice("12345mu"), "shift:off"]
    return ["click:%d,%d" % (rng.randrange(0, 320), rng.randrange(20, 195))]


def roster_alphabet(rng: random.Random) -> list[str]:
    """The inn roster screen (R038)."""
    r = rng.random()
    if r < 0.40:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.70:
        return ["key:" + rng.choice("ahis123dx")]
    if r < 0.76:
        return ["key:" + rng.choice(["enter", "space", "tab", "pgup", "pgdn"])]
    return ["click:%d,%d" % (rng.randrange(60, 300), rng.randrange(20, 160))]


def party_alphabet(rng: random.Random) -> list[str]:
    """The create-a-party menu from the start screen (R037+0x0444)."""
    r = rng.random()
    if r < 0.30:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.88:
        return ["key:" + rng.choice("ahsd123")]
    if r < 0.92:
        return ["key:" + rng.choice(["enter", "space", "tab", "pgup", "pgdn", "home", "end"])]
    return ["click:%d,%d" % (rng.randrange(0, 320), rng.randrange(0, 200))]


def create_alphabet(rng: random.Random) -> list[str]:
    """The character generator (R037+0x4BEE): letters, list rows by click, typed names."""
    r = rng.random()
    if r < 0.26:
        return ["key:" + rng.choice("bgmsr")]   # e and c open a text entry: only as the macros below, which end it
    if r < 0.34:
        return ["key:" + rng.choice(["enter", "space", "backspace", "up", "down", "left", "right"])]
    if r < 0.38:
        return ["key:e"] + ["key:" + rng.choice("abcdefghijklmnopqrstuvwxyz") for _ in range(rng.randrange(0, 5))] + ["key:" + rng.choice(["enter", "enter", "esc"])]
    if r < 0.42:
        return ["key:c"] + ["key:" + rng.choice("abcdefghijklmnopqrstuvwxyz") for _ in range(rng.randrange(0, 4))] + ["key:" + rng.choice(["enter", "enter", "esc"])]
    if r < 0.46:
        return ["key:" + rng.choice("123456789")]
    x = rng.choice([100, 120, 140, 200, 230])
    return ["click:%d,%d" % (x, int(28 + 11.5 * rng.randrange(0, 16)))]


def create2_alphabet(rng: random.Random) -> list[str]:
    """The character generator again, with only what the screen's own hit rectangles and keys offer (no digits: 2 and 3 open modal boxes)."""
    r = rng.random()
    if r < 0.30:
        return ["click:%d,%d" % (rng.randrange(0x42, 0xAD), 0x2D + 0x0B * rng.randrange(0, 14) + 3)]      # the list rows: commands 1..14
    if r < 0.42:
        return ["click:%d,%d" % (rng.randrange(0xC6, 0xEA), 0x12 + 8 * rng.randrange(0, 6) + 3)]          # childhood attribute rows
    if r < 0.52:
        return ["click:%d,%d" % (rng.randrange(0xF7, 0x13A), 0x12 + 8 * rng.randrange(0, 22) + 3)]         # skill rows
    if r < 0.56:
        return ["click:%d,%d" % (rng.randrange(0xC5, 0xEA), rng.randrange(0x60, 0xA4))]                    # the minus area
    if r < 0.60:
        return ["click:%d,%d" % (rng.randrange(0xAF, 0xE9), rng.choice([0xAC, 0xBC]))]                     # the two list buttons
    if r < 0.74:
        return ["key:" + rng.choice(["up", "down", "left", "right"])]
    if r < 0.88:
        return ["key:" + rng.choice("gmsr")]
    if r < 0.92:
        return ["key:e"] + ["key:" + rng.choice("abcdefghijklmnopqrstuvwxyz") for _ in range(rng.randrange(0, 5))] + ["key:" + rng.choice(["enter", "enter", "esc"])]
    if r < 0.95:
        return ["key:c"] + ["key:" + rng.choice("abcdefghijklmnopqrstuvwxyz") for _ in range(rng.randrange(0, 4))] + ["key:" + rng.choice(["enter", "enter", "esc"])]
    return ["key:" + rng.choice("23"), "key:esc"]


# name -> (saved game, prefix, alphabet, closing steps)
SCREENS = {
    "sheet": ("Dksave10.sav", lambda: cut("sheet", "sheet"), sheet_alphabet, ["key:esc", "wait:200"]),
    "shop": ("DKSAVE0.SAV", lambda: cut("shop", "shop"), barter_alphabet, ["key:esc", "wait:200"]),
    "pawn": ("DKSAVE0.SAV", lambda: cut("pawnshop", "shop"), barter_alphabet, ["key:esc", "wait:200"]),
    "stable": ("DKSAVE0.SAV", lambda: cut("stables", "stables"), barter_alphabet, ["key:esc", "wait:200"]),
    "cache": ("Dksave10.sav", lambda: cut("cache", "cache"), barter_alphabet, ["key:esc", "wait:200"]),
    "sheet99": ("DKSAVE99.SAV", lambda: cut("pack-potions", "sheet"), sheet3_alphabet, ["key:esc", "wait:200"]),
    "pack99": ("DKSAVE99.SAV", lambda: cut("pack-potions", "sheet"), packsweep_alphabet(3), ["key:esc", "wait:200"]),
    "pack10": ("Dksave10.sav", lambda: cut("sheet", "sheet"), packsweep_alphabet(4), ["key:esc", "wait:200"]),
    "saints": ("Dksave10.sav", lambda: cut("saints", "saints"), saints_alphabet, ["key:esc", "key:esc", "wait:200"]),
    "bench": ("Dksave10.sav", lambda: cut("alchemy", "alchemy"), bench_alphabet, ["key:l", "wait:200"]),
    "bench99": ("DKSAVE99.SAV", lambda: cut("camp-wild-brew", "alchemy"), bench_alphabet, ["key:l", "wait:200"]),
    "stay": ("Dksave10.sav", lambda: cut("alchemy", "stay"), camp_alphabet, ["key:l", "wait:200"]),
    "wild": ("DKSAVE99.SAV", lambda: cut("camp-wild-brew", "camp"), camp_alphabet, ["key:l", "wait:200"]),
    "roster": ("DKSAVE0.SAV", lambda: cut("roster-moves", "roster"), roster_alphabet, ["key:esc", "wait:200"]),
    "party": (None, lambda: "key:c\nwait:5", party_alphabet, ["key:a", "wait:30", "key:b", "wait:30"]),
    "create2": ("DKSAVE8.SAV", lambda: cut("roster-create2", "generator"), create2_alphabet, ["key:esc", "wait:200"]),
    "create": ("DKSAVE8.SAV", lambda: cut("roster-create2", "generator"), create_alphabet, ["key:esc", "wait:200"]),
}


CAMP_SCREENS = ("stay", "wild")
HOURGLASS_WAIT = 300        # frames: the C# host's hourglass of a camp day (4 steps x 400 ms + 1000 ms, R043+0x3494) runs on the host's frames


def input_wait(screen: str, step: str) -> str:
    """The C#'s wait after an input: a camp day (S, or a click on the bottom menu's first row, R036+0x0734) passes time with the
    hourglass, whose delays the headless host plays out frame by frame; the original's next step waits for it to be idle anyway."""
    if screen in CAMP_SCREENS:
        kind, _, value = step.partition(":")
        if kind == "key" and value in ("s", "S"):
            return "wait:%d" % HOURGLASS_WAIT
        if kind == "click":
            x, y = (int(v) for v in value.split(","))
            if 0x46 < x < 0x134 and 0xAA < y < 0xBC and (y - 0xAA) // 9 == 0:
                return "wait:%d" % HOURGLASS_WAIT
    return "wait:30"


def fix_waits(screen: str, text: str) -> str:
    """An existing generated session with the waits of input_wait (the inputs and states unchanged)."""
    lines = text.splitlines()
    for i in range(1, len(lines)):
        if lines[i] == "wait:30" and lines[i - 1].split(":")[0] in ("key", "click"):
            lines[i] = input_wait(screen, lines[i - 1])
    return "\n".join(lines) + "\n"


def generate(screen: str, seed: int, steps: int) -> str:
    save, prefix, alphabet, closing = SCREENS[screen]
    rng = random.Random("%s-%d" % (screen, seed))
    out = ["# Generated by gen_screen_sessions.py (%s, seed %d, %d inputs): a seeded walk over the %s screen's keys and mouse; " % (screen, seed, steps, screen)
           + "a state every %d inputs" % STATE_EVERY]
    out += prefix().splitlines()
    for k in range(steps):
        for s in alphabet(rng):
            out.append(s)
            if s.split(":")[0] in ("key", "click"):
                out.append(input_wait(screen, s))
        if (k + 1) % STATE_EVERY == 0 and screen not in NO_STATES:
            out.append("state:s%d" % (k + 1))
    out += closing
    out.append("state:end")
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("screen")
    ap.add_argument("first", type=int)
    ap.add_argument("count", type=int)
    ap.add_argument("--steps", type=int, default=64)
    ap.add_argument("--out", default=str(SESSIONS_DIR))
    ap.add_argument("--fix-waits", nargs="*", help="rewrite the waits of these generated .steps files (input_wait) instead of generating")
    a = ap.parse_args()
    if a.fix_waits:
        for f in a.fix_waits:
            Path(f).write_text(fix_waits(a.screen, Path(f).read_text(encoding="utf-8")), encoding="utf-8", newline="\n")
        return
    for seed in range(a.first, a.first + a.count):
        name = "gen-%s-%02d" % (a.screen, seed)
        (Path(a.out) / (name + ".steps")).write_text(generate(a.screen, seed, a.steps), encoding="utf-8")
        save = SCREENS[a.screen][0]
        print('"%s": %s,' % (name, '"%s"' % save if save else "None"))


if __name__ == "__main__":
    main()
