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
NO_STATES = {"bkeys", "bheld", "bedge", "bsafe", "party", "party2", "gate", "gatem", "rgate", "rmgate"}       # the party menu of the start screen has no game to write until Begin the Adventure


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


class PotionSweep(PackSweep):
    """PackSweep for a pack full of potions (synth_saves.py pots): mostly P (drink one), then the next line; the stacks drunk up shift the lines."""

    def step(self, rng: random.Random) -> list[str]:
        if not self.ready:
            return super().step(rng)
        r = rng.random()
        if r < 0.50:
            return ["key:p"]
        if r < 0.78:
            self.line += 1
            return ["key:down"]
        if r < 0.84:
            return ["key:up"]
        if r < 0.88:
            return ["key:" + rng.choice(["pgdn", "pgup", "home", "end"])]
        if r < 0.92:
            return ["key:" + rng.choice("au")]
        if self.members > 1:
            m = rng.randrange(0, self.members)
            if m == self.who:
                m = (m + 1) % self.members
            self.who, self.line, self.ready = m, 0, False
            return ["key:f%d" % (m + 1)]
        return ["key:down"]


def potions_alphabet(members):
    def alphabet(rng):
        if not hasattr(rng, "pack"):
            rng.pack = PotionSweep(members)
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


def party2_alphabet(rng: random.Random) -> list[str]:
    """The party menu again, its clicks kept on the menu: the command rows 1..8 (Add .. Kill; row 0 opens the generator, 9 begins the game, 10
    returns to the start screen) and the character list (R037+0x0000's two click areas); no click on the party column (x < 0x3C opens the
    member's sheet, R037+0x01C0: the sheet families walk it)."""
    r = rng.random()
    if r < 0.30:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.84:
        return ["key:" + rng.choice("ahsd123")]
    if r < 0.88:
        return ["key:" + rng.choice(["enter", "space", "tab", "pgup", "pgdn", "home", "end"])]
    if r < 0.95:
        return ["click:%d,%d" % (rng.randrange(0x47, 0xC8), 0x1A + 15 * rng.randrange(1, 9) + rng.randrange(0, 14))]
    return ["click:%d,%d" % (rng.randrange(0xC6, 0x138), rng.randrange(0x15, 0xB4))]


def roster2_alphabet(rng: random.Random) -> list[str]:
    """The inn roster again, its clicks kept on the screen: the command rows 1..6 (row 0 opens the generator, row 7 returns to the inn) and the
    character list (R038+0x0000's two click areas). No arrows: with the highlight on a party member, R038+0x02F8 loads the figure picture named by
    the figure list's entry of the ROW, an empty name past the list's end, and the original stops on the missing file (PICS/SMALL.PIC, exit 99:
    PartyRosterScreen's divergence); the list clicks move the highlight without a picture."""
    r = rng.random()
    if r < 0.40:
        return ["click:%d,%d" % (rng.randrange(0xC6, 0x138), rng.randrange(0x15, 0xB4))]
    if r < 0.70:
        return ["key:" + rng.choice("ahis123dx")]
    if r < 0.76:
        return ["key:" + rng.choice(["enter", "space", "tab", "pgup", "pgdn"])]
    if r < 0.88:
        return ["click:%d,%d" % (rng.randrange(0x47, 0xC8), 0x2E + 15 * rng.randrange(1, 7) + rng.randrange(0, 14))]
    return ["click:%d,%d" % (rng.randrange(0xC6, 0x138), rng.randrange(0x15, 0xB4))]


def create3_alphabet(rng: random.Random) -> list[str]:
    """create2 without the inputs that stall the original-side tester: R037+0x43E6 (the name boxes that list commands 4 and 5 and the keys e and
    c open in the first step) reads keys only, so a click given while one is up is never read and the tester waits for it: every list click
    is followed by Enter (no case of R037+0x4BEE; it closes a box that opened), and the typed letters have no command of their own (so they do
    nothing when no box opened). R is left out (it leaves the generator from the first step)."""
    r = rng.random()
    if r < 0.30:
        return ["click:%d,%d" % (rng.randrange(0x42, 0xAD), 0x2D + 0x0B * rng.randrange(0, 14) + 3), "key:enter"]
    if r < 0.42:
        return ["click:%d,%d" % (rng.randrange(0xC6, 0xEA), 0x12 + 8 * rng.randrange(0, 6) + 3)]
    if r < 0.52:
        return ["click:%d,%d" % (rng.randrange(0xF7, 0x13A), 0x12 + 8 * rng.randrange(0, 22) + 3)]
    if r < 0.56:
        return ["click:%d,%d" % (rng.randrange(0xC5, 0xEA), rng.randrange(0x60, 0xA4))]
    if r < 0.60:
        return ["click:%d,%d" % (rng.randrange(0xAF, 0xE9), rng.choice([0xAC, 0xBC]))]
    if r < 0.74:
        return ["key:" + rng.choice(["up", "down", "left", "right"])]
    if r < 0.88:
        return ["key:" + rng.choice("gms")]
    if r < 0.95:
        return ["key:" + rng.choice("ec")] + ["key:" + rng.choice("adfhijlopqtuvwxyz") for _ in range(rng.randrange(0, 5))] + ["key:enter"]
    return ["key:" + rng.choice("23"), "key:esc"]


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


class MenuWalk:
    """The menu bar's keyboard mode (root 0000:0DD2, hot keys root 0000:0CAA) over a card (city, map): F10 starts it, then the arrows, Enter, Esc,
    F10 again and the other keys it ignores; out of the mode the Alt hot keys (O, P, D, M, F, C), F6 and Alt-Q (the question answered N).
    Follows the cursor like the game's own code so that Enter is given only on items whose action is harmless to the walk (not the save,
    the load, the quit): menus = [(opens, [(on, subs, after), ...])], `after` the keys that dismiss what the item opens."""

    def __init__(self, menus, nav_only=False):
        self.menus = menus
        self.nav_only = nav_only            # a battle's bar: which items can be chosen is the battle's (no Enter: the walk only moves the cursor)
        self.mode, self.m, self.i, self.s = False, 0, -1, -1

    def _down(self):
        items = self.menus[self.m][1]
        if self.s != -1:
            if self.s + 1 < items[self.i][1]:
                self.s += 1
            return
        n = self.i
        while n + 1 < len(items) and items[n + 1][0] == 2:
            n += 1
        if n + 1 < len(items) and items[n + 1][0] == 1:
            self.i = n + 1

    def _up(self):
        items = self.menus[self.m][1]
        if self.s != -1:
            if self.s > 0:
                self.s -= 1
            return
        while self.i != -1:
            self.i -= 1
            if self.i >= 0 and items[self.i][0] == 1:
                break

    def _left(self):
        if self.s != -1:
            self.s = -1
            return
        if self.m > 0:
            self.i = self.s = -1
            while self.m != 0:
                self.m -= 1
                if self.menus[self.m][0] == 1:
                    return

    def _right(self):
        items = self.menus[self.m][1]
        if 0 <= self.i < len(items) and items[self.i][1] > 0:
            self.s = 0
            return
        nxt = self.m + 1
        while nxt < len(self.menus) and self.menus[nxt][0] == 2:
            nxt += 1
        if nxt < len(self.menus) and self.menus[nxt][0] == 1:
            self.m, self.i, self.s = nxt, -1, -1

    def safe_enter(self):
        """Enter chooses the item under the cursor (or its sub-item): the keys that dismiss what it opens, or None when it must not be chosen."""
        items = self.menus[self.m][1]
        if self.i < 0 or self.i >= len(items):
            return []
        on, subs, after = items[self.i]
        if self.s != -1:
            return []                     # a difficulty sub-item: set at once
        return after

    def step(self, rng: random.Random) -> list[str]:
        r = rng.random()
        if not self.mode:
            if r < 0.40:
                self.mode, self.m, self.i, self.s = True, 0, -1, -1
                return ["key:f10"]
            if r < 0.62:
                return ["key:" + rng.choice(["altm", "altf", "altc", "altm", "altf", "altc"])]
            if r < 0.70:
                return ["key:altp", "key:enter"]
            if r < 0.78:
                return ["key:alto", "key:esc"]
            if r < 0.86:
                return ["key:altd"]                   # steps to the next difficulty at once (no arrows / Enter after: they go to the card's own menu)
            if r < 0.93:
                return ["key:f6", "key:esc"]
            a = rng.choice(["x", "n", "N"])
            return ["key:altq", "key:" + a] + (["key:n"] if a == "x" else [])
        if r < 0.24:
            self._down()
            return ["key:down"]
        if r < 0.38:
            self._up()
            return ["key:up"]
        if r < 0.52:
            self._left()
            return ["key:left"]
        if r < 0.70:
            self._right()
            return ["key:right"]
        if r < 0.84:
            after = None if self.nav_only else self.safe_enter()
            if after is None:
                self._down()
                return ["key:down"]
            self.mode = False
            return ["key:enter"] + after
        if r < 0.90:
            self.mode = False
            return ["key:esc"]
        if r < 0.93:
            self.mode = False
            return ["key:f10"]
        return ["key:" + rng.choice(["space", "tab", "x"])]       # keys the mode ignores (an Alt key inside the mode toggles in the C#, not in the original: lead)


def gate_alphabet(rng: random.Random) -> list[str]:
    """The player's orders in the gate battle (the battle starts paused): members by digit, order letters, clicks on the ground / the enemy,
    the party panel's rows (the pointer is put back at the view's middle: the original scrolls the view at its edges), Esc / Enter, the arrows
    that step the active member, a member's sheet (F1..F3, Esc (the fourth picture is missing from the game folder))."""
    r = rng.random()
    if r < 0.18:
        return ["key:" + rng.choice("1234")]
    if r < 0.40:
        return ["key:" + rng.choice("wavbpmfh")]
    if r < 0.58:
        return ["click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))]
    if r < 0.66:
        return ["key:" + rng.choice(ARROWS)]
    if r < 0.72:
        return ["key:esc"]
    if r < 0.75:
        return ["key:esc"]                  # (Enter would end the order mode and start the battle: the closing's Space then pauses it)
    if r < 0.88:
        return ["click:%d,%d" % (rng.randrange(4, 56), 40 * rng.randrange(0, 4) + rng.randrange(3, 37)), "move:170,100"]
    if r < 0.93:
        return ["key:" + rng.choice(["f1", "f2", "f3"]), "key:esc"]
    return ["move:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))]


# Game: Save, Load, Difficulty (3 sub-items), Show Changes, Music, Sound FX, Pause, Quit; Orders and Attack greyed (opens 2); Party: Party Info, Marching Order
CITY_MENUS = [(1, [(1, 0, None), (1, 0, None), (1, 3, []), (1, 0, []), (1, 0, []), (1, 0, []), (1, 0, ["key:enter"]), (1, 0, None)]),
              (2, []), (2, []), (1, [(1, 0, ["key:esc"]), (1, 0, ["key:esc"])])]


BATTLE_MENUS = [(1, [(1, 0, None)] * 8), (1, [(1, 0, None)] * 4), (1, [(1, 6, None)] * 6), (1, [(1, 0, None)] * 2)]


def mbat_alphabet(rng: random.Random) -> list[str]:
    """The menu bar in the gate battle (Game, Orders, Attack, Party): cursor moves only, and the Alt hot keys outside the mode."""
    if not hasattr(rng, "mw"):
        rng.mw = MenuWalk(BATTLE_MENUS, nav_only=True)
    if not rng.mw.mode:
        # out of the mode: F10, or an Alt toggle that leaves the battle's pause alone (Alt-P, Enter and the quit question are left out)
        if rng.random() < 0.35:
            return ["key:" + rng.choice(["altm", "altf", "altc", "altd"])]
        rng.mw.mode, rng.mw.m, rng.mw.i, rng.mw.s = True, 0, -1, -1
        return ["key:f10"]
    out = rng.mw.step(rng)
    if not rng.mw.mode:
        # TESTER: after a key that leaves the mode the next key is given at once (4 ms later in the original: F10, Esc, F10, Esc leaves the
        # original in a key wait); a pointer move needs the idle time first
        out += ["move:170,100"]
    return out


def mbar_alphabet(rng: random.Random) -> list[str]:
    if not hasattr(rng, "mw"):
        rng.mw = MenuWalk(CITY_MENUS)
    return rng.mw.step(rng)


def right_steps(rng: random.Random, screen: str = "") -> list[str]:
    """The right button: a click anywhere (the menu bar's root 0000:0059 test of DS:6EF0 bit 1 runs its menu mode once, and the screens that read the
    button themselves, R030+0x038B the map, R038+0x26E3 / +0x2CAD the roster), or the bar held: down on a title (y 4), a move into the dropdown
    (items 8 rows of 8 below the bar's 12), up on the item. Each hold ends with a wait and Esc, which answers a question or closes a panel the item opened."""
    wait = "bwait:5" if screen in ("gate", "rgate", "rmgate") else "wait:40"
    r = rng.random()
    if r < 0.35:
        return ["rclick:%d,%d" % (rng.randrange(0, 320), rng.randrange(0, 200))]
    if r < 0.50:
        return ["rclick:%d,%d" % (rng.randrange(60, 320), rng.randrange(0, 12))]
    # the bar held: its titles (Game x 66, Orders 96, Attack 130, Party 177 in the city's font), the items below (rows of 8 from y 12)
    # Save, Load, Pause and Quit (rows 0, 1, 6, 7 of Game) are left out: they open dialogs the walk does not answer
    k = rng.random()
    if k < 0.40:
        x, row, esc = rng.randrange(66, 82), rng.choice([3, 4, 5]), False
        moves = [(x + rng.randrange(0, 40), 12 + 8 * row + 4)]
    elif k < 0.60:
        x, esc = rng.randrange(66, 82), False
        s_row = rng.randrange(0, 3)
        moves = [(x + 20, 12 + 8 * 2 + 4), (x + 110, 12 + 8 * (2 + s_row) + 4)]
    elif k < 0.85:
        x, esc = rng.randrange(177, 195), True
        moves = [(x + rng.randrange(0, 30), 12 + 8 * rng.randrange(0, 2) + 4)]
    else:
        x, esc = rng.randrange(96, 165), False
        moves = [(x + rng.randrange(0, 30), 12 + 8 * rng.randrange(0, 3) + 4)]
    out = ["rdown:%d,4" % x, wait]
    for mx, my in moves:
        out += ["move:%d,%d" % (mx, my), wait]
    out += ["rup", wait]
    if esc:
        out += ["key:esc"]
    return out


def right_wrapped(alphabet, screen: str, p: float = 0.15):
    """The screen's alphabet with the right button mixed in with probability p (the cancel paths of screens that read it)."""
    def walk(rng: random.Random) -> list[str]:
        if rng.random() < p:
            return right_steps(rng, screen)
        return alphabet(rng)
    return walk


def rmenu_alphabet(screen: str):
    return lambda rng: right_steps(rng, screen) if rng.random() < 0.8 else ["move:%d,%d" % (rng.randrange(0, 320), rng.randrange(20, 190))]


GATE_START = "\noption:4\nwait:5\nkey:enter\nwait:300"       # the gate guards are attacked: the battle begins paused

# ---- round R2: the travel map (overland_loop R030+0x02C0, the key handler R030+0x0C44) ----
def trav_alphabet(rng: random.Random) -> list[str]:
    """The map from DKSAVE17 (a party by night on the road): clicks that steer the marker, held directions (the keyboard interrupt), buffered arrows,
    Shift / Ctrl F-keys (the leader), the sheets, the right button with Shift (the coordinates), the portraits, the camp and the cards an encounter shows."""
    r = rng.random()
    if r < 0.28:
        return ["click:%d,%d" % (rng.randrange(66, 318), rng.randrange(24, 192))]
    if r < 0.40:
        d = rng.choice(sorted(HELD))
        return ["kdown:" + HELD[d], "wait:5", "key:x", "wait:60", "kup:" + HELD[d], "wait:5"]
    if r < 0.48:
        return ["key:" + rng.choice(ARROWS + ["home", "end", "pgup", "pgdn"])]
    if r < 0.56:
        return ["shift:on", "key:f%d" % rng.randrange(1, 6), "shift:off"]
    if r < 0.60:
        return ["ctrl:on", "key:f%d" % rng.randrange(1, 6), "ctrl:off"]
    if r < 0.66:
        return ["key:f%d" % rng.randrange(1, 6), "wait:30", "key:esc"]
    if r < 0.72:
        x, y = rng.randrange(66, 318), rng.randrange(24, 192)
        # (no key inside the hold: the menu bar's tracking loop, which the held right button starts, takes no key)
        shift = rng.random() < 0.7
        return (["shift:on"] if shift else []) + ["rdown:%d,%d" % (x, y), "wait:5", "move:%d,%d" % (x + rng.randrange(-8, 9), y + rng.randrange(-8, 9)), "wait:5", "rup", "wait:5"]             + (["shift:off"] if shift else [])
    if r < 0.78:
        return ["click:%d,%d" % (rng.randrange(0, 60), rng.randrange(20, 200))]
    if r < 0.82:
        return ["key:c", "wait:60", "key:l", "wait:200"]
    if r < 0.92:
        return ["key:enter"]
    if r < 0.96:
        return ["key:enter", "wait:60", "key:enter"]
    return ["move:%d,%d" % (rng.randrange(0, 320), rng.randrange(20, 190))]


# name -> (saved game, prefix, alphabet, closing steps)
SCREENS = {
    "mbar": ("DKSAVE0.SAV", lambda: cut("menubar", "menu"), mbar_alphabet, []),
    "mbarm": ("DKSAVE17.SAV", lambda: cut("mapsave", "map"), mbar_alphabet, []),
    "trav": ("DKSAVE17.SAV", lambda: cut("mapsave", "map"), trav_alphabet, []),
    "trav0": ("DKSAVE0.SAV", lambda: cut("pressburg-map", "map"), trav_alphabet, []),
    "travw": ("synth/DKSAVE17-month0d3.sav", lambda: cut("mapsave", "map"), trav_alphabet, []),
    "travs": ("synth/DKSAVE17-month6.sav", lambda: cut("mapsave", "map"), trav_alphabet, []),
    "travn": ("synth/DKSAVE17-month10d1.sav", lambda: cut("mapsave", "map"), trav_alphabet, []),
    "gate": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + GATE_START, gate_alphabet,
             ["key:space", "wait:20000", "key:l", "wait:300", "key:enter", "wait:300"]),
    "pots": ("synth/Dksave10-pots.sav", lambda: cut("sheet", "sheet"), potions_alphabet(4), ["key:esc", "wait:200"]),
    "kit": ("synth/Dksave10-kitchen.sav", lambda: cut("sheet", "sheet"), packsweep_alphabet(4), ["key:esc", "wait:200"]),
    "pots99": ("synth/DKSAVE99-pots3.sav", lambda: cut("pack-potions", "sheet"), potions_alphabet(3), ["key:esc", "wait:200"]),
    "gatem": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + GATE_START, mbat_alphabet, ["key:space", "wait:20000", "key:l", "wait:300", "key:enter", "wait:300"]),
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
    "party2": (None, lambda: "key:c\nwait:5", party2_alphabet, ["key:a", "wait:30", "key:b", "wait:30"]),
    "roster2": ("DKSAVE0.SAV", lambda: cut("roster-moves", "roster"), roster2_alphabet, ["key:esc", "wait:200"]),
    "create3": ("DKSAVE8.SAV", lambda: cut("roster-create2", "generator"), create3_alphabet, ["key:esc", "wait:200"]),
    "create2": ("DKSAVE8.SAV", lambda: cut("roster-create2", "generator"), create2_alphabet, ["key:esc", "wait:200"]),
    "create": ("DKSAVE8.SAV", lambda: cut("roster-create2", "generator"), create_alphabet, ["key:esc", "wait:200"]),
}


# the right button on screens (rm*: the menu bar with it from the city card, the map and the gate battle; r*: the screen's walk with it mixed in)
SCREENS.update({
    "rmbar": ("DKSAVE0.SAV", lambda: cut("menubar", "menu"), rmenu_alphabet("rmbar"), []),
    "rmbarm": ("DKSAVE17.SAV", lambda: cut("mapsave", "map"), rmenu_alphabet("rmbarm"), []),
    "rmgate": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + GATE_START, rmenu_alphabet("rmgate"),
               ["key:space", "wait:20000", "key:l", "wait:300", "key:enter", "wait:300"]),
})
for _name in ("sheet", "shop", "pawn", "stable", "cache", "roster", "roster2", "create", "stay", "wild", "saints", "bench", "gate"):
    _save, _prefix, _alpha, _closing = SCREENS[_name]
    SCREENS["r" + _name] = (_save, _prefix, right_wrapped(_alpha, "r" + _name), _closing)


# ---- round B1: the battle's input (keys, held keys, modifiers, the mouse at the view's edges, the party panel, the right button) ----
HELD = {"up": "48", "down": "50", "left": "4B", "right": "4D", "home": "47", "end": "4F", "pgup": "49", "pgdn": "51"}
HELD_PAIRS = [("left", "up"), ("left", "down"), ("right", "up"), ("right", "down"), ("up", "down"), ("left", "right"), ("home", "pgdn"), ("end", "pgup")]
BAT_NAMED = ["enter", "esc", "up", "down", "left", "right", "home", "end", "pgup", "pgdn", "backspace", "tab", "altm", "altf", "altc", "altd"]
BAT_FKEYS = ["f%d" % k for k in range(1, 11)]
BAT_LETTERS = "abcdefghijklmnoprtuvwxyz".replace("s", "")            # (no q: Alt-Q is the quit; plain q is harmless but kept out as the Alt hot keys are)
BAT_ORDERS = "wavbpmfhgcdeiknortulxyz"


def pause_run(rng: random.Random, lo: int = 20, hi: int = 120) -> list[str]:
    """The battle runs for a while (unpaused) and is paused again: members get hurt, missiles fly, orders are carried out."""
    return ["key:space", "bwait:%d" % rng.randrange(lo, hi), "key:space"]


def surrender_steps(rng: random.Random, final: bool = False) -> list[str]:
    """S asks "Do you really want to surrender?" (R022+0x03E4): other keys / clicks first (the question waits), then N / Esc (the fight goes on) or, last, Y."""
    out = ["key:s", "bwait:10"]
    for _ in range(rng.randrange(0, 3)):
        out += [rng.choice(["key:h", "key:a", "key:1", "key:space", "key:tab", "click:150,100", "move:200,90", "key:up", "key:enter"]), "bwait:6"]
    out += [("key:y" if final else "key:n"), "bwait:20"]
    return out


def held_steps(rng: random.Random) -> list[str]:
    """One or two held direction keys (kdown / kup through the game's own keyboard interrupt), with Shift / Ctrl sometimes, for some passes."""
    mod = rng.random()
    on = ["shift:on"] if mod < 0.15 else ["ctrl:on"] if mod < 0.27 else []
    off = ["shift:off"] if mod < 0.15 else ["ctrl:off"] if mod < 0.27 else []
    if rng.random() < 0.45:
        a, b = rng.choice(HELD_PAIRS)
        ks = [HELD[a], HELD[b]]
    else:
        ks = [HELD[rng.choice(sorted(HELD))]]
    out = ["bwait:2"] + on
    for k in ks:
        out += ["kdown:" + k, "bwait:2"]
    out += ["bwait:%d" % rng.randrange(6, 60)]
    for k in (ks if rng.random() < 0.5 else ks[::-1]):
        out += ["kup:" + k, "bwait:2"]
    return out + off + ["bwait:5"]


def bat_click(rng: random.Random) -> list[str]:
    r = rng.random()
    mod = rng.random()
    pre = ["ctrl:on"] if mod < 0.10 else ["shift:on"] if mod < 0.18 else []
    post = ["ctrl:off"] if mod < 0.10 else ["shift:off"] if mod < 0.18 else []
    if r < 0.55:
        return ["key:esc"] + pre + ["click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))] + post + ["key:esc", "move:170,100"]
    # the party panel's rows and its edges (the pointer is put back at the view's middle)
    return ["key:esc"] + pre + ["click:%d,%d" % (rng.randrange(4, 56), 40 * rng.randrange(0, 4) + rng.randrange(3, 37))] + post + ["key:esc", "move:170,100"]       # (a click on a portrait may open the combat sheet)


def bkeys_alphabet(rng: random.Random) -> list[str]:
    r = rng.random()
    if r < 0.22:
        k = rng.choice(BAT_ORDERS)
        return ["key:" + k] + (["key:esc"] if k == "e" else [])             # (E: the enemy info)
    if r < 0.30:
        return ["key:" + rng.choice("1234567890")]
    if r < 0.42:
        return ["key:" + rng.choice(BAT_NAMED)]
    if r < 0.47:
        return ["key:" + rng.choice(BAT_FKEYS), "key:esc"]
    if r < 0.56:
        m = rng.choice(["shift", "ctrl"])
        return ["%s:on" % m, "key:" + rng.choice(BAT_LETTERS + "1234"), "%s:off" % m]
    if r < 0.60:
        m = rng.choice(["shift", "ctrl"])
        return ["%s:on" % m, "key:" + rng.choice(["up", "down", "left", "right", "home", "end", "pgup", "pgdn", "enter", "tab", "space"]), "%s:off" % m]
    if r < 0.75:
        return held_steps(rng)
    if r < 0.88:
        return bat_click(rng)
    if r < 0.92:
        return right_steps(rng, "gate")
    if r < 0.95:
        return pause_run(rng)
    if r < 0.97:
        return surrender_steps(rng)
    return ["move:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))]


def bheld_alphabet(rng: random.Random) -> list[str]:
    """Held keys mostly, around orders and member selections and runs of the battle."""
    r = rng.random()
    if r < 0.5:
        return held_steps(rng)
    if r < 0.62:
        return ["key:" + rng.choice("1234")]
    if r < 0.74:
        return ["key:" + rng.choice(BAT_ORDERS[:8])]
    if r < 0.82:
        return pause_run(rng)
    if r < 0.90:
        return ["key:esc"]
    if r < 0.95:
        return ["key:enter"]
    return bat_click(rng)


def bedge_alphabet(rng: random.Random) -> list[str]:
    """The pointer at the view's edges and the screen's edges (the original scrolls the view there), clicks there, then back to the middle."""
    r = rng.random()
    side = rng.randrange(8)
    x = [0, 319, rng.randrange(0, 320), rng.randrange(0, 320), 1, 318, rng.randrange(60, 80), rng.randrange(300, 320)][side]
    y = [rng.randrange(0, 200), rng.randrange(0, 200), 0, 199, rng.randrange(0, 12), rng.randrange(188, 200), rng.randrange(0, 200), rng.randrange(0, 200)][side]
    if r < 0.55:
        return ["move:%d,%d" % (x, y), "bwait:%d" % rng.randrange(4, 40), "move:170,100", "bwait:3"]
    if r < 0.75:
        return ["key:esc", "click:%d,%d" % (x, y), "key:esc", "move:170,100"]
    if r < 0.85:
        return ["move:%d,%d" % (x, y), "bwait:%d" % rng.randrange(4, 30), "key:" + rng.choice("1234wavbpm"), "move:170,100"]
    if r < 0.93:
        return held_steps(rng)
    return pause_run(rng, 10, 60) if rng.random() < 0.5 else ["key:" + rng.choice("1234")]


SAFE_NAMED = ["up", "down", "left", "right", "home", "end", "pgup", "pgdn", "backspace", "tab"]


def bsafe_alphabet(rng: random.Random) -> list[str]:
    """Keys that stay in the battle's order mode (no Enter / Space / F-keys / S / E / panel clicks / mouse at the edges): letters, digits, the
    named cursor keys, held keys with Shift / Ctrl, view clicks."""
    r = rng.random()
    if r < 0.30:
        return ["key:" + rng.choice(BAT_ORDERS.replace("e", ""))]
    if r < 0.40:
        return ["key:" + rng.choice("1234567890")]
    if r < 0.50:
        return ["key:" + rng.choice(SAFE_NAMED)]
    if r < 0.60:
        m = rng.choice(["shift", "ctrl"])
        return ["%s:on" % m, "key:" + rng.choice(BAT_LETTERS.replace("e", "") + "1234" + "x"), "%s:off" % m]
    if r < 0.66:
        m = rng.choice(["shift", "ctrl"])
        return ["%s:on" % m, "key:" + rng.choice(SAFE_NAMED), "%s:off" % m]
    if r < 0.88:
        return held_steps(rng)
    r2 = rng.random()
    mod = rng.random()
    pre = ["ctrl:on"] if mod < 0.15 else ["shift:on"] if mod < 0.25 else []
    post = ["ctrl:off"] if mod < 0.15 else ["shift:off"] if mod < 0.25 else []
    if r2 < 0.8:
        return ["key:esc"] + pre + ["click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))] + post + ["key:esc", "move:170,100"]
    return ["move:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))]


BAT_START = GATE_START + "\nkey:2\nbwait:20"            # the first key ends the battle start pause (a held key or a mouse move alone does not)
GATE_CLOSING = ["key:esc", "bwait:10", "key:esc", "bwait:10", "key:space", "wait:20000", "key:l", "wait:300", "key:enter", "wait:300"]
SCREENS.update({
    "bkeys": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + BAT_START, bkeys_alphabet, GATE_CLOSING),
    "bheld": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + BAT_START, bheld_alphabet, GATE_CLOSING),
    "bedge": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + BAT_START, bedge_alphabet, GATE_CLOSING),
    "bsafe": ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + BAT_START, bsafe_alphabet, GATE_CLOSING),
})


def brun_alphabet(rng: random.Random) -> list[str]:
    """Keys, held keys and clicks while the battle RUNS (unpaused: DS:9081 bit 0, the active member changing under DS:C623): short waits."""
    r = rng.random()
    if r < 0.18:
        return ["key:" + rng.choice("1234")]
    if r < 0.40:
        k = rng.choice(BAT_ORDERS)
        return ["key:" + k] + (["key:esc"] if k == "e" else [])
    if r < 0.55:
        mod = rng.random()
        pre = ["ctrl:on"] if mod < 0.15 else ["shift:on"] if mod < 0.25 else []
        post = ["ctrl:off"] if mod < 0.15 else ["shift:off"] if mod < 0.25 else []
        return ["key:esc"] + pre + ["click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))] + post + ["key:esc", "move:170,100"]
    if r < 0.70:
        return held_steps(rng)
    if r < 0.80:
        m = rng.choice(["shift", "ctrl"])
        return ["%s:on" % m, "key:" + rng.choice(BAT_LETTERS + "1234"), "%s:off" % m]
    if r < 0.92:
        return ["key:" + rng.choice(SAFE_NAMED)]
    if r < 0.96:
        return ["key:" + rng.choice("1234"), "key:" + rng.choice("wavbpm")]
    return ["move:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180))]


RUN_START = BAT_START + "\nkey:space\nbwait:30"                       # the first Space starts the fight
RUN_CLOSING = ["key:esc", "bwait:5", "key:space", "bwait:5"] + GATE_CLOSING                  # paused again, then the closing Space as the other walks
SCREENS["brun"] = ("DKSAVE0.SAV", lambda: cut("orders-walk", "gate") + RUN_START, brun_alphabet, RUN_CLOSING)
NO_STATES.add("brun")

# the battle walks on synthetic saved games (synth_saves.py): a hurt / ill / dead party in the gate battle (the battle's party panel and sheets)
for _name, _save, _alpha, _start in (("bhurt", "synth/DKSAVE0-hurt.sav", brun_alphabet, RUN_START), ("bhurta", "synth/DKSAVE0-hurtattr.sav", brun_alphabet, RUN_START),
                                     ("bhurts", "synth/DKSAVE0-hurtstatus.sav", bkeys_alphabet, BAT_START), ("bdead", "synth/DKSAVE0-dead.sav", brun_alphabet, RUN_START),
                                     ("bhurtk", "synth/DKSAVE0-hurt.sav", bkeys_alphabet, BAT_START)):
    SCREENS[_name] = (_save, (lambda s=_start: cut("orders-walk", "gate") + s), _alpha, RUN_CLOSING if _start == RUN_START else GATE_CLOSING)
    NO_STATES.add(_name)

def border_alphabet(rng: random.Random) -> list[str]:
    """Orders as the player gives them in the pause (member, order letter, target click), then the fight runs for a while (Space .. Space)."""
    r = rng.random()
    if r < 0.55:
        order = rng.choice("wavbpmfh")
        steps = ["key:" + rng.choice("1234"), "key:" + order]
        steps += ["key:7", "click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180)), "key:esc", "move:170,100"] if order in "wavbm" else [ ]
        if order == "p" or order == "f":
            steps += ["key:7", "click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180)), "key:esc", "move:170,100"]
        return steps
    if r < 0.70:
        # a thrown potion: T opens the Throw list (the Attack menu unfolded); Down k times, Enter, a click on a target
        return ["key:" + rng.choice("1234"), "key:t"] + ["key:down"] * rng.randrange(0, 5) + ["key:enter", "key:7", "click:%d,%d" % (rng.randrange(80, 300), rng.randrange(20, 180)), "key:esc", "move:170,100"]
    if r < 0.85:
        return pause_run(rng, 15, 90)
    if r < 0.93:
        return held_steps(rng)
    return ["key:" + rng.choice(["g", "q", "h", "u", "o", "d"])]


for _name, _save, _from, _alpha, _start in (("blue", "Dksave21.sav", "orders-potion", bkeys_alphabet, BAT_START), ("bluer", "Dksave21.sav", "orders-potion", border_alphabet, BAT_START),
                                           ("bpra", "local/oracle-saves/DKSAVE40N.SAV", "orders-prague-missile", bkeys_alphabet, BAT_START),
                                           ("bprar", "local/oracle-saves/DKSAVE40N.SAV", "orders-prague-missile", border_alphabet, BAT_START),
                                           ("bgater", "DKSAVE0.SAV", "orders-walk", border_alphabet, BAT_START),
                                           ("bhurto", "synth/DKSAVE0-hurt.sav", "orders-walk", border_alphabet, BAT_START)):
    SCREENS[_name] = (_save, (lambda f=_from, s=_start: cut(f, "gate") + s), _alpha, GATE_CLOSING)
    NO_STATES.add(_name)

CAMP_SCREENS = ("stay", "wild")
BATTLE_SCREENS = ("gate", "gatem", "rgate", "rmgate", "bkeys", "bheld", "bedge", "bsafe", "brun", "bhurt", "bhurta", "bhurts", "bdead", "bhurtk", "blue", "bluer", "bpra", "bprar", "bgater", "bhurto")        # inputs of a battle are followed by bwait:<passes> (lockstep.py); no state steps inside a battle
HOURGLASS_WAIT = 300        # frames: the C# host's hourglass of a camp day (4 steps x 400 ms + 1000 ms, R043+0x3494) runs on the host's frames


def input_wait(screen: str, step: str) -> str:
    """The C#'s wait after an input: a camp day (S, or a click on the bottom menu's first row, R036+0x0734) passes time with the
    hourglass, whose delays the headless host plays out frame by frame; the original's next step waits for it to be idle anyway."""
    if screen.startswith("mbar") or screen.startswith("rmbar"):
        return "wait:100"
    if screen.startswith("trav"):
        kind, _, value = step.partition(":")
        return "wait:600" if kind == "click" else "wait:60"
    if screen in ("brun", "bhurt", "bhurta", "bdead"):
        return "bwait:8"
    if screen in BATTLE_SCREENS:
        return "bwait:20"
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


def merge_bwaits(lines: list[str]) -> list[str]:
    """Consecutive bwait steps as one (their sum): the original's script reader keeps only the last of a run, the C# host takes each one (round B1)."""
    # a bwait right after a shift / ctrl step is dropped by the original's script reader (BWAIT_ACTUAL is then one short and the C# side's waits
    # shift): move it before the modifier steps (they take no time)
    lines = list(lines)
    moved = True
    while moved:
        moved = False
        for i in range(1, len(lines)):
            if lines[i].startswith("bwait:") and lines[i - 1] in ("shift:on", "shift:off", "ctrl:on", "ctrl:off"):
                lines[i - 1], lines[i] = lines[i], lines[i - 1]
                moved = True
    out = []
    for l in lines:
        if l.startswith("bwait:") and out and out[-1].startswith("bwait:"):
            out[-1] = "bwait:%d" % (int(out[-1][6:]) + int(l[6:]))
        else:
            out.append(l)
    return out


def generate(screen: str, seed: int, steps: int) -> str:
    save, prefix, alphabet, closing = SCREENS[screen]
    rng = random.Random("%s-%d" % (screen, seed))
    out = ["# Generated by gen_screen_sessions.py (%s, seed %d, %d inputs): a seeded walk over the %s screen's keys and mouse; " % (screen, seed, steps, screen)
           + "a state every %d inputs" % STATE_EVERY]
    out += prefix().splitlines()
    due = 0
    for k in range(steps):
        for s in alphabet(rng):
            out.append(s)
            if s.split(":")[0] in ("key", "click", "rclick") or (screen in BATTLE_SCREENS and s.split(":")[0] == "move"):
                out.append(input_wait(screen, s))
        if (k + 1) % STATE_EVERY == 0 and screen not in NO_STATES:
            if getattr(rng, "mw", None) is not None and rng.mw.mode:
                due = k + 1                          # no state inside the keyboard menu mode: at the next input that leaves it
            else:
                out.append("state:s%d" % (k + 1))
                due = 0
        elif due and not (getattr(rng, "mw", None) is not None and rng.mw.mode):
            out.append("state:s%d" % (k + 1))
            due = 0
    if getattr(rng, "mw", None) is not None and rng.mw.mode:
        out += ["key:esc", "wait:100"]              # the keyboard menu mode still on: leave it
    out += closing
    out.append("state:end")
    return "\n".join(merge_bwaits(out) if screen in BATTLE_SCREENS else out) + "\n"


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
