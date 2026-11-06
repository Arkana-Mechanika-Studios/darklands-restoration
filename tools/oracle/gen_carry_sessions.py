"""Generate whole-game lockstep sessions that fight two or more battles in one program run (lockstep-sessions/gen-carry-NN.steps): what
one battle leaves in the program's variables for the next (the per-battle state carried from battle to battle), compared state by state.

usage: python -I -B gen_carry_sessions.py <first seed> <count> [--out DIR]

A session is a seeded walk over a small graph of battles that one saved game reaches (no other saved game can be given to a session: the
original side holds only SAVES\\DKSAVE0, so a second battle comes from the game's own flow or from an Alt-L in the same run):
  * DKSAVE0 (Pressburg): the city gate's fight (city, the card's menu line 4);
  * synth/DKSAVE0-card24: the footpads' street fight (city, won), then card 0x29 and the way to the gate's fight;
  * synth/DKSAVE0-card16 / -card7a / -card7b: the market at night, the city guard, the rebel mob (city, lost by this party: the card goes on
    to the prayer, the jail, the fortress);
  * synth/DKSAVE99-carda0 / -carda5 / -card24: the wolves, the boars, the footpads in the Harz (wild, won), then the travel map, the walk to
    Rammelsburg and its mine (dungeon: the ambush fought, the loot left, single file, the leader walks; the battle goes on);
  * DKSAVE99: the walk to the mine.
Each battle gets a few seeded orders in its start pause (gen_screen_sessions.border_alphabet) and is then fought to its end (or, last, given
up: S, Y). Between battles: the game's flow, or Alt-L of the session's saved game (DKSAVE0: the first battle again, with what the last
battle left), or of a game saved in the mine's battle (Alt-S there writes DKSAVE1; loaded: the battle taken up mid-way). An Alt-S in a
card's battle is refused (real combat), so the mid-way saves are the mine's. State steps outside the battles (after each battle and at the
end); a session that ends inside the mine ends with an Alt-S there (its .SAV and .BSV compared) and a key after it.
The lines for rerun_all.SESSIONS (name -> saved game) are printed; every session runs with battle records.
"""
from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gen_screen_sessions as g  # noqa: E402

HERE = Path(__file__).resolve().parent
SESSIONS_DIR = HERE / "lockstep-sessions"

LOAD = ["key:t", "wait:5", "key:l", "wait:300"]
RELOAD = ["key:altl", "wait:100", "key:y", "wait:300"]                 # then key:down (DKSAVE1) or not (DKSAVE0), key:l, wait:600
# Pressburg's card 0x29 (its first page) to the gate's menu (orders-walk), then line 4: the guards attacked, the battle begins paused
TO_GATE = (["key:enter", "wait:30"] * 4 + ["key:enter", "wait:200", "option:0", "wait:10", "key:enter", "wait:200", "key:enter", "wait:200",
                                           "option:1", "wait:10", "key:enter", "wait:200", "key:enter", "wait:200", "option:6", "wait:10",
                                           "key:enter", "wait:200", "key:enter", "wait:100", "key:enter", "wait:100", "option:9", "wait:5",
                                           "key:enter", "wait:150", "key:enter", "wait:150"]
           + ["option:4", "wait:5", "key:enter", "wait:300"])
# the travel map of DKSAVE99 (the Harz) to the mine of Rammelsburg (dungeon-mine-doors): the battle begins paused
TO_MINE = ["click:190,108", "wait:12000", "click:203,113", "wait:4000", "key:space", "wait:100", "key:space", "wait:100", "key:space", "wait:100",
           "option:1", "wait:5", "key:enter", "wait:300"]


def card(line):
    """A card of a synthetic save (the game saved at the card): its menu line that starts the fight (None: the fight comes at once)."""
    if line is None:
        return ["key:enter", "wait:300"]
    return ["key:enter", "wait:100", "option:%d" % line, "wait:10", "key:enter", "wait:300", "key:enter", "wait:300"]


# node: (kind, the steps from the session's load (or the previous node's end) to the battle's start, the steps after the fight's end to
# the next node's way in, the next node or None (a fight whose end leads nowhere further in this graph))
WON_TEXT = ["key:l", "wait:300", "key:enter", "wait:300"]            # the loot left (or the card's text), the card's text
NODES = {
    "gate": ("city", None, WON_TEXT, None),
    "foot0": ("city", card(None), WON_TEXT, "gate"),
    "market": ("city", card(4), WON_TEXT + ["key:enter", "wait:300"], None),
    "guard": ("city", card(1), WON_TEXT + ["key:enter", "wait:300"], None),
    "mob": ("city", card(0), WON_TEXT + ["key:enter", "wait:300"], None),
    "wolves": ("wild", card(3), WON_TEXT + ["key:enter", "wait:300"], "mine"),
    "boars": ("wild", card(3), WON_TEXT + ["key:enter", "wait:300"], "mine"),
    "foot99": ("wild", card(None), WON_TEXT + ["key:enter", "wait:300"], "mine"),
    "mine": ("mine", None, None, None),
}
WAY_IN = {"gate": TO_GATE, "mine": TO_MINE}
# the saved games and the node each starts at (after its load)
STARTS = [("DKSAVE0.SAV", "gate"), ("synth/DKSAVE0-card24.sav", "foot0"), ("synth/DKSAVE0-card16.sav", "market"),
          ("synth/DKSAVE0-card7a.sav", "guard"), ("synth/DKSAVE0-card7b.sav", "mob"), ("synth/DKSAVE99-carda0.sav", "wolves"),
          ("synth/DKSAVE99-carda5.sav", "boars"), ("synth/DKSAVE99-card24.sav", "foot99"), ("DKSAVE99.SAV", "mine")]
WEIGHTS = [3, 5, 1, 1, 1, 3, 3, 3, 3]
# Seeds whose walk to the mine meets a road card in the original (its clock seeds draw an encounter on the way), with the walk's number
# (0: the session's first walk to the mine): card 0x24 (the footpads; seeds 3, 6, 23) or 0x41 (the Wild Hunt; seed 26), each fighting at
# once. The mine card's keys of TO_MINE would land in that running battle, where the original reads no key until the fight's end and the
# C# host after its frames (round reload: ordercarry's varB). Such a session ends there: the two clicks of the walk and the fight's first
# 800 passes (as carry-reload-footpads).
ROAD_CARDS = {3: 0, 6: 1, 23: 1, 26: 2}
FIGHT_TO_END = ["key:esc", "bwait:10", "key:esc", "bwait:10", "key:space", "wait:20000"]


class Walk:
    def __init__(self, rng: random.Random, start: str, road_card: int | None = None):
        self.rng, self.start = rng, start
        self.out: list[str] = []
        self.battles, self.mid_saved, self.states = 0, False, 0
        self.kinds: list[str] = []
        self.road_card, self.walks = road_card, 0

    def way_in(self, node: str) -> bool:
        """The steps to the node's battle; True when the walk meets the road card of ROAD_CARDS instead (the session ends in its fight)."""
        if node == "mine":
            self.walks += 1
            if self.walks - 1 == self.road_card:
                self.battles += 1
                self.kinds.append("road card")
                self.out += TO_MINE[:4] + ["bwait:800"]
                return True
        self.out += WAY_IN[node]
        return False

    def state(self):
        self.states += 1
        self.out.append("state:b%d" % self.states)

    def orders(self):
        """The start pause: the first key ends it (gen_screen_sessions.BAT_START), then a few orders and short runs."""
        self.out += ["key:2", "bwait:20"]
        for _ in range(self.rng.randrange(0, 6)):
            for s in g.border_alphabet(self.rng):
                self.out.append(s)
                if s.split(":")[0] in ("key", "click", "move"):
                    self.out.append("bwait:20")

    def walk_mine(self, n):
        for _ in range(n):
            self.out += ["key:" + self.rng.choice(["left", "left", "left", "up", "down", "right"]), "bwait:7"]

    def fight(self, node: str, last: bool) -> bool:
        """The node's battle from its start; True if the game goes on outside a battle (False: the session is inside the mine's battle)."""
        kind = NODES[node][0]
        self.battles += 1
        self.kinds.append(node)
        if kind == "mine":
            # the ambush (dungeon-mine-doors): walk west into it, fight it without orders, leave the loot, single file, walk on
            self.out += ["key:1", "bwait:5", "key:space", "bwait:5"]
            self.out += ["key:left", "bwait:7"] * 30 + ["key:left", "wait:20000", "key:l", "bwait:60", "key:q", "bwait:30"]
            self.walk_mine(self.rng.randrange(2, 10))
            if not self.mid_saved and self.rng.random() < 0.6:
                self.out += ["key:alts", "wait:100", "key:enter", "wait:300", "bwait:20"]      # DKSAVE1: the battle saved mid-way
                self.mid_saved = True
                self.walk_mine(self.rng.randrange(1, 6))
            return False
        self.orders()
        if last and self.rng.random() < 0.3:
            self.out += ["key:esc", "bwait:10", "key:s", "bwait:10", "key:y", "bwait:20", "wait:3000", "key:enter", "wait:300", "key:enter",
                         "wait:300"]                       # given up (R022+0x03E4)
            self.state()
            return True
        self.out += FIGHT_TO_END + NODES[node][2]
        self.state()
        return True

    def reload(self, mid: bool) -> str | None:
        """Alt-L: DKSAVE1 (the mine's battle saved mid-way: taken up again, paused) or DKSAVE0 (the session's start). The node to fight next
        (None: the mine's battle goes on)."""
        self.out += RELOAD + (["key:down", "wait:30"] if mid else []) + ["key:l", "wait:600"]
        if mid:
            self.battles += 1
            self.kinds.append("mine(loaded)")
            self.out += ["bwait:20", "key:space", "bwait:30"]
            self.walk_mine(self.rng.randrange(2, 8))
            return None
        self.state()
        return self.start

    def run(self, max_battles: int):
        node = self.start
        if node in WAY_IN:
            if self.way_in(node):
                return
        else:
            self.out += NODES[node][1]
        outside = True
        while True:
            last = self.battles + 1 >= max_battles
            outside = self.fight(node, last)
            nxt = NODES[node][3] if outside else None
            if self.battles >= max_battles:
                break
            if nxt and self.rng.random() < 0.85:
                node = nxt
                if self.way_in(node):
                    return
                continue
            # Alt-L: the mid-way save when there is one (more often from the mine), else the session's saved game
            mid = self.mid_saved and self.rng.random() < 0.6
            node = self.reload(mid)
            if node is None:
                if self.battles >= max_battles:
                    outside = False
                    break
                # the mine's battle taken up again: Alt-L again to the start, or the end
                mid2 = False
                node = self.reload(mid2)
            if node in WAY_IN:
                if self.way_in(node):
                    return
            else:
                self.out += NODES[node][1]
        if outside:
            self.out += ["wait:300"]
            self.state()
        else:
            self.out += ["key:alts", "wait:100", "key:enter", "wait:300", "bwait:20", "key:space", "bwait:20"]


def generate(seed: int):
    rng = random.Random("carry-%d" % seed)
    k = rng.choices(range(len(STARTS)), WEIGHTS)[0]
    save, start = STARTS[k]
    w = Walk(rng, start, ROAD_CARDS.get(seed))
    w.run(rng.choice([2, 2, 3, 3, 4]))
    head = ("# Generated by gen_carry_sessions.py (seed %d): %d battles in one run from %s: %s; the per-battle state carried from one battle to "
            "the next (bwait:<n> = n battle passes in both games)" % (seed, w.battles, save, " > ".join(w.kinds)))
    return save, head + "\n" + "\n".join(LOAD + g.merge_bwaits(w.out)) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("first", type=int)
    ap.add_argument("count", type=int)
    ap.add_argument("--out", default=str(SESSIONS_DIR))
    a = ap.parse_args()
    for seed in range(a.first, a.first + a.count):
        name = "gen-carry-%02d" % seed
        save, text = generate(seed)
        (Path(a.out) / (name + ".steps")).write_text(text, encoding="utf-8", newline="\n")
        print('"%s": "%s",' % (name, save))


if __name__ == "__main__":
    main()
