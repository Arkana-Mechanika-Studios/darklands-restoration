"""Battle oracle (pilot): one battle fought by the ORIGINAL DARKLAND.EXE and by the C# rewrite, compared at every combat tick.

usage: python -I -B battle_oracle.py <DARKLAND.EXE> <game folder> <out folder> --save <DKSAVEn.SAV>
           [--fights name,name] [--passes N] [--coverage] [--headless <Darklands.Host.Headless.exe>] [--live] [--replay-only]

Both start as lockstep.py's gate-battle session does (the player's saved game, the inn, the street, the city gate at night,
"attack the guards"); that path agrees in both up to the battle. At the battle's entry (the main loop's call of R002+0x0000,
12 words: mode, battlefield, seed, three enemy groups) the fight's arguments replace the card's: in the original on the
stack at R002+0x0000's entry, in the C# by DARKLANDS_BATTLE_REQUEST (the same words). Neither side gives orders: the party
fights as the original's party without orders does, the enemies by their AI. The battle runs for --passes passes.

At the head of every battle pass (R014+0x3166, the combat clock DS:07D4) both write a line: the random generator's state
(DS:7B20), the 13 combatant records (DS:9C55, 0x80 bytes each; the C#'s through CombatantRecords.Write), the queued damage
(DS:D70C, endurance and strength per combatant) and the attack rates (DS:D493). A pass of the original that computes for long lets the time-based timer callbacks come (the clock
rises by more than one to the next pass); the C# is given those extra callbacks by clock. They are compared by combat clock (the first
pass at each clock value), over the record bytes the C# models; the first difference is reported per fight with its field.

With --coverage the original's executed blocks and branch outcomes go to <out>/<fight>/cov/cov-0.jsonl (as card_oracle's;
block_coverage.py takes such a folder as a further source).

The original side is recorded (recording.py): the first run stores what the comparison reads and the coverage in
local/oracle-recordings/, later runs run only the C# against it, if the SHA-256 of the EXE, the game files, the arguments and the
source of the tester modules are the same (else a live run, which replaces the recording). --live forces the live run,
--replay-only refuses one, --recordings DIR moves the folder. rerun_all.py runs everything with N workers. A full --live rerun
is made before every release and periodically.
Out folder: report.json, per fight original.log, original-battle.trace, rewrite-battle.trace, rewrite.log.
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import lockstep  # noqa: E402
from dosmachine import DS, Stop  # noqa: E402

BATTLE_ENTRY = (0x0E76, 0x0000)     # R002+0x0000, called by the main loop root 03A1:000A with 12 words
COMBATANTS, RECORD, COUNT = 0x9C55, 0x80, 13
QUEUED, RATES = 0xD70C, 0xD493     # 2 bytes a combatant (endurance, strength); 1 byte a combatant
# the fields the weapon-readying step sets: an enemy whose weapon is not readied before the fight keeps the
# enemy file buffer's stack bytes there (R025+0x0267 copies the buffer whole), which the C# leaves 0
STALE_READY = {0x0C, 0x0D, 0x20, 0x21, 0x23, 0x4D, 0x4E, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x59}
PAUSED = 297                        # passes the battle stays paused before its key: the C# host's pause under the script's wait:300 (298 passes at one clock value)
SESSION = HERE / "lockstep-sessions" / "gate-battle.steps"

# mode, battlefield, seed, then (a, b, c) of the three enemy groups as the battle's arguments take them (the C#'s BattleRequest
# (Type, Quality, Count)); None: the gate battle as the card orders it
FIGHTS = {
    "gate": None,
    "street": (0, 0x11, 1234, 12, 2, 3, 2, 2, 2, -1, 0, 0),      # a city street: thugs (DARKLAND.ENM type 12), archers (2: missiles)
    "wild": (0, 0x2E, 777, 42, 2, 4, 1, 2, 2, -1, 0, 0),         # wilderness: wolves (42), huntsmen (1: missiles)
    "mine": (0, 0x0A, 99, 48, 2, 4, 46, 2, 2, -1, 0, 0),         # a mine: kobolds (48), gnomes (46)
    "castle": (0, 0x01, 4321, 33, 2, 2, 0, 2, 2, -1, 0, 0),      # a castle: knights (33), sergeants (0: missiles)
    # the batch: more battlefields (city battle types 1..9, other wilderness, mines, castles, fortress, tombs, dragon cave), the
    # enemy templates of DARKLAND.ENM (missile users: the sergeants 0..4), larger and mixed groups, qualities 1..3, other seeds
    "city13": (0, 0x13, 4001, 11, 2, 5, 1, 3, 2, 6, 1, 3),      # city: bandits, sergeants, guards
    "city1c": (0, 0x1C, 5002, 15, 2, 6, 3, 2, 3, -1, 0, 0),     # city battle type 9: mercenaries, sergeants
    "city1f": (0, 0x1F, 6003, 40, 2, 4, 7, 2, 4, -1, 0, 0),     # city battle type 8: cultists, guards
    "city24": (0, 0x24, 7004, 10, 1, 8, -1, 0, 0, -1, 0, 0),    # city battle type 5: eight bandits
    "city2b": (0, 0x2B, 8005, 28, 2, 3, 31, 2, 2, -1, 0, 0),    # city battle type 6: monks, knights
    "city15": (0, 0x15, 9006, 21, 2, 3, 5, 2, 4, -1, 0, 0),     # city battle type 7: evil alchemists, guards
    "wild30": (0, 0x30, 1111, 63, 2, 2, 43, 1, 3, -1, 0, 0),    # wilderness: bears, boars
    "wild40": (0, 0x40, 2222, 12, 3, 6, 2, 2, 3, -1, 0, 0),     # wilderness: bandits, sergeants (missiles)
    "wild50": (0, 0x50, 3333, 62, 2, 5, 46, 2, 4, -1, 0, 0),    # wilderness: spiders, skeletons
    "wild6f": (0, 0x6F, 4444, 36, 2, 4, 4, 3, 2, -1, 0, 0),     # wilderness: templars, sergeants
    "wild75": (0, 0x75, 5555, 66, 2, 3, 67, 2, 2, 42, 2, 6),    # wilderness: hell hounds, schrats, wolves
    "mine0b": (0, 0x0B, 6666, 46, 2, 4, 50, 2, 6, -1, 0, 0),    # mine: skeletons, kobolds
    "mine10": (0, 0x10, 7777, 49, 2, 3, 44, 2, 4, -1, 0, 0),    # mine: gargoyles, dwarves
    "mine80": (0, 0x80, 8888, 48, 2, 5, 54, 2, 1, 0, 2, 3),     # mine: gnomes, giant gnome, sergeants
    "castle04": (0, 0x04, 1212, 30, 2, 4, 33, 2, 2, 3, 2, 3),   # castle: knights, tin knights, sergeants
    "castle07": (0, 0x07, 2323, 37, 2, 5, 29, 2, 3, -1, 0, 0),  # castle: templars, monks
    "castle7c": (0, 0x7C, 3434, 8, 3, 8, 2, 2, 4, -1, 0, 0),    # castle: eight guards, sergeants
    "fort5e": (0, 0x5E, 4545, 37, 2, 4, 1, 2, 4, -1, 0, 0),     # templar fortress: templars, sergeants
    "tombs5f": (0, 0x5F, 5656, 47, 2, 5, 52, 2, 2, -1, 0, 0),   # tombs: skeletons, undead
    "cave60": (0, 0x60, 6767, 69, 2, 1, -1, 0, 0, -1, 0, 0),    # dragon cave: the cave dragon
    # fights2: casters (enemy types with the spell flag, enemy file type +0x16 bit 0x80), potion carriers (template +0x32), new battlefields
    "f2alch16": (0, 0x16, 1101, 24, 2, 3, 3, 2, 2, -1, 0, 0),    # city: alchemists (casters, potions), guards
    "f2witch2f": (0, 0x2F, 1202, 39, 2, 2, 42, 2, 3, -1, 0, 0),  # wilderness: witches, wolves (a caster tests its lines of sight from the previous member, R018+0x093C)
    "f2hwitch41": (0, 0x41, 1303, 28, 3, 1, 39, 2, 2, 60, 2, 1), # wilderness: high witch, witches, wild hunter
    "f2friar19": (0, 0x19, 1404, 32, 2, 3, 30, 2, 2, 31, 2, 2),  # city: friars, evil monks, bad friars (casters, potions)
    "f2kking0c": (0, 0x0C, 1505, 25, 3, 1, 48, 2, 4, 51, 2, 1),  # mine: kobold king (caster, potions), kobolds, overseer (potions)
    "f2vulcan0e": (0, 0x0E, 1606, 49, 2, 2, 44, 2, 3, -1, 0, 0), # mine: vulcans, dwarves (casters)
    "f2demon61": (0, 0x61, 1707, 55, 3, 1, 53, 2, 3, 54, 2, 2),  # demon lord (caster), demons, demon knights
    "f2precept05": (0, 0x05, 1808, 71, 3, 2, 40, 2, 3, 81, 2, 2), # castle: preceptors, templars, acolytes (casters)
    "f2leader14": (0, 0x14, 1909, 77, 3, 1, 65, 3, 1, 29, 2, 2),  # city: leaders (casters), monks
    "f2magic58": (0, 0x58, 2010, 24, 2, 2, 39, 2, 2, 40, 2, 2),  # mixed casters: alchemists, witches, templars
    "f2hell62": (0, 0x62, 2111, 56, 2, 4, 61, 2, 3, 64, 3, 1),   # hell locusts, hellhounds, Baphomet
    # keep (two levels, 0x2C/0x2D): the party starts at (0, 0) of level 0 (R010 keep build leaves it unset), where cell reads go off the level
    "f2archwitch2c": (0, 0x2C, 2212, 2, 2, 3, 39, 2, 2, 34, 2, 2),  # archers, witches, gallants
}

# the record bytes CombatantRecords.Write fills, by name
# (+0x00/+0x02, the sprite's place where the last frame drew it, are left out: the C#'s screen writes them at another moment)
FIELDS = {0x04: "x", 0x05: "x", 0x06: "y", 0x07: "y",
          0x08: "dest x", 0x09: "dest x", 0x0A: "dest y", 0x0B: "dest y", 0x0C: "+0C range", 0x0D: "+0D strike quality",
          0x10: "action", 0x11: "action", 0x14: "status", 0x16: "animation slot", 0x19: "facing", 0x1A: "sprite facing",
          0x1D: "aim delay", 0x20: "+20 fighting value", 0x21: "+21 aiming value", 0x22: "readied weapon", 0x23: "+23 min skill",
          0x24: "+24", 0x48: "+48 bonus", 0x49: "+49 load", 0x4A: "target", 0x4B: "armour type", 0x4C: "armour type",
          0x4D: "armour value", 0x4E: "armour value", 0x4F: "armour quality", 0x50: "armour quality", 0x51: "default weapon",
          0x52: "weapon speed", 0x53: "weapon class", 0x54: "weapon damage", 0x55: "protection", 0x56: "min strength",
          0x57: "full strength", 0x58: "weapon quality", 0x59: "readied skill", 0x5A: "missile quality", 0x5B: "shield quality",
          0x5C: "shield type",
          **{0x5D + k: "attribute %d" % k for k in range(7)}, **{0x64 + k: "maximum %d" % k for k in range(7)},
          **{0x6B + k: "skill %d" % k for k in range(19)}}


def run_original(exe: Path, game: str, steps, save: Path, request, passes: int, coverage: bool, paused: int = PAUSED):
    """(trace lines, the battle's arguments as the card gave them, log, how it ended, set-up ticks, coverage)."""
    s = lockstep.Session(exe.read_bytes(), game, max_instructions=4_000_000_000, coverage=coverage)
    # the root-image guard of the vector table writes (lockstep.py) is left off here: it makes the original run the keyboard handler's install as the
    # game does and so moves the original's timer phases; the clock rises of f2precept05's long passes then fall where the C# does not place them (see
    # local/overnight/precept/README.md); the earlier passes of this oracle were all made with the table's bytes left in the code
    s.vector_guard = False
    s.vfs.put("saves/dksave0.sav", save.read_bytes())
    if save.with_suffix(".BSV").exists():
        s.vfs.put("saves/dksave0.bsv", save.with_suffix(".BSV").read_bytes())
    s.script = list(steps)
    given = []

    def entry(m):
        given.append([m.sarg(k) for k in range(12)])
        if request is not None:
            for k, v in enumerate(request):
                m.put_word(m.reg("SS"), m.reg("SP") + 4 + 2 * k, v & 0xFFFF)
        m.log.append("battle %s -> %s at %d ms" % (given[-1], request, m.ms))
    s.hook(*BATTLE_ENTRY, entry, record="R002")

    # The one-section mine arm (R009+0x01E0, 1432:01E0) reads its rotation from a stack word it never sets: the word R009+0x3FA2
    # (182C:0002) left in the frame (the second wall-tile draw, 1..3). An interrupt that lands between that return and the arm (the
    # emulator's timing, about one battle in 10^5 on a real machine) pushes a register over the word and turns the mine another way.
    # The word is kept at the return and put back at the arm's entry, so the original runs as if no interrupt hit the window.
    stale = []
    s.hook(0x1432, 0x0087, lambda m: stale.append(m.word(m.reg("SS"), (m.reg("SP") - 8) & 0xFFFF)), record="R009")
    s.hook(0x1432, 0x01E5, lambda m: stale and m.put_word(m.reg("SS"), (m.reg("BP") - 2) & 0xFFFF, stale[-1]), record="R009")

    class Trace(list):
        draw_clocks: list = []      # the combat clock at each random draw (rand, root 06A1:29B6)
        draws_at: list = []         # the draws made before each pass head
        phase_clocks: list = []     # (-1 or -2, combat clock) where a tick's scheduler (1DB7:0D36) or melee exchanges (0E76:2A4A) begin
        phases_at: list = []        # the phase marks made before each pass head

        def append(self, line):
            self.draws_at.append(len(self.draw_clocks))
            self.phases_at.append(len(self.phase_clocks))
            records = s.read(DS, COMBATANTS, COUNT * RECORD)
            super().append("%s cbt %s q %s ar %s" % (line, records.hex().upper(), s.read(DS, QUEUED, 2 * COUNT).hex().upper(),
                                                    s.read(DS, RATES, COUNT).hex().upper()))
            if len(self) >= passes:
                s.budget = 0            # the next block stops the run (Stop: instruction budget)
    s.battle_trace = Trace()
    s.battle_trace.draw_clocks, s.battle_trace.draws_at = [], []
    s.battle_trace.phase_clocks, s.battle_trace.phases_at = [], []
    s.hook(0x1DB7, 0x0D36, lambda m: s.battle_trace.phase_clocks.append((-1, m.word(DS, 0x07D4))), record="R014")
    s.hook(0x0E76, 0x2A4A, lambda m: s.battle_trace.phase_clocks.append((-2, m.word(DS, 0x07D4))), record="R014")
    s.hook(0x06A1, 0x29B6, lambda m: s.battle_trace.draw_clocks.append(m.word(DS, 0x07D4)))
    # the battle's pause (after its first ticks) waits for Space; lockstep's idle test (no random draw for IDLE_MS) never passes on a
    # battlefield whose animated pieces draw at every frame: in a battle the key is given once the clock has stood still PAUSED passes
    idle_test = s._next_key
    given_keys = []

    def next_key():
        t = s.battle_trace
        while len(t) > paused and s.script and s.script[0][0] in ("wait", "shot"):
            s.script.pop(0)             # the C#'s steps
        if (len(t) > paused and s.script and s.script[0][0] == "key"
                and len({line.split()[2] for line in t[-paused:]}) == 1):
            # peeked as key_pressed does; get_key takes it from the script
            if not given_keys or given_keys[-1] != len(t):
                given_keys.append(len(t))
                s.log.append("%s due in the battle's pause at pass %d" % (s.script[0][1], len(t)))
            return lockstep.key_code(s.script[0][1])
        return idle_test()
    s._next_key = next_key
    seeds = []

    def srand(m):
        ret = lockstep.linear(m.word(m.reg("SS"), m.reg("SP") + 2), m.word(m.reg("SS"), m.reg("SP")))
        if bytes(m.uc.mem_read(ret - 14, 5)) == bytes([0x9A, 0xA8, 0x23, 0xA1, 0x06]):
            seeds.append(m.arg(0))
    s.hook(*lockstep.SRAND, srand)
    try:
        s.start()
        end = "the program ended"
    except Stop as e:
        end = str(e)[:300]
    cov = s.coverage_result() if coverage else None
    return s.battle_trace, given, seeds, [l for l in s.log if not l.startswith("INT")], end, s.set_up_ticks, cov


class Recorded(list):
    """A recorded run's pass lines with the clock marks the original made (the attributes of run_original's Trace)."""


def original_side(rec_parts, folder, name, exe, game, steps, save, request, passes, coverage, paused, live, replay_only):
    """run_original, or its recording when one with the same key exists: (the same tuple, seconds, replayed)."""
    import recording
    rec = recording.Recording(folder, "fights", name, {"kind": "fight", "name": name, "request": request, "passes": passes, "paused": paused,
                                                      "steps": steps, **rec_parts}, live, replay_only, "p%d" % paused)
    got = rec.load(need_coverage=coverage)
    if got is not None:
        trace = Recorded(got["lines"])
        trace.draw_clocks, trace.draws_at = got["draw_clocks"], got["draws_at"]
        trace.phase_clocks, trace.phases_at = got["phase_clocks"], got["phases_at"]
        cov = got["cov"] if coverage else None
        return (trace, got["given"], got["seeds"], got["log"], got["end"], got["set_up"], cov), got["seconds"], True
    t = time.time()
    trace, given, seeds, log, end, set_up, cov = run_original(exe, game, steps, save, request, passes, coverage, paused)
    seconds = time.time() - t
    rec.store({"lines": list(trace), "draw_clocks": trace.draw_clocks, "draws_at": trace.draws_at, "phase_clocks": trace.phase_clocks,
               "phases_at": trace.phases_at, "given": given, "seeds": seeds, "log": log, "end": end, "set_up": set_up, "cov": cov,
               "seconds": seconds})
    return (trace, given, seeds, log, end, set_up, cov), seconds, False


def parse(lines):
    """{clock: (rand, records, queued, rates)} of the first pass at each clock."""
    out = {}
    for line in lines:
        w = line.split()
        if len(w) >= 4 and w[0] == "pass" and w[1] == "clock":
            clock = int(w[2])
            if clock not in out:
                get = lambda key: bytes.fromhex(w[w.index(key) + 1]) if key in w else b""
                out[clock] = (w[4], get("cbt"), get("q"), get("ar"))
    return out


clock_jumps = lockstep.clock_jumps            # moved to lockstep.py (its sessions with a battle pass the jumps too)


def stale_destinations(lines):
    """[(record, x, y)] of the enemy records (5..12): the destination (+0x08..+0x0B) at the first pass where the record is in use. The original
    copies the enemy file's record whole into the combatant (R025+0x0267) and the destination keeps the stack bytes it held until the enemy
    first moves; the C# leaves it 0, and a dragon's breath at a spot (target -1, facing_to_target R014+0x306A) turns toward it."""
    out = {}
    for line in lines:
        w = line.split()
        if len(w) < 6 or "cbt" not in w:
            continue
        records = bytes.fromhex(w[w.index("cbt") + 1])
        for i in range(5, COUNT):
            r = records[i * RECORD:(i + 1) * RECORD]
            if i not in out and r[0x14] != 0:
                out[i] = tuple(v - 65536 if v >= 32768 else v for v in struct.unpack_from("<2H", r, 8))
    return [(i,) + out[i] for i in sorted(out)]


def first_difference(original: dict, rewrite: dict):
    common = sorted(set(original) & set(rewrite))
    start = {}      # slot -> (original, rewrite) +0x1D at its first tick
    ready = {}      # (slot, offset) -> (original, rewrite) at the slot's first tick
    dest = {}       # slot -> (original, rewrite) +0x08..0x0B at its first tick
    for clock in common:
        (orand, orec, oq, oar), (crand, crec, cq, car) = original[clock], rewrite[clock]
        diffs = []
        for i in range(COUNT):
            o, c = orec[i * RECORD:(i + 1) * RECORD], crec[i * RECORD:(i + 1) * RECORD]
            if o[0x14] == 0 and c[0x14] == 0:
                continue                # an empty slot: the original keeps old bytes there
            first = start.setdefault(i, (o[0x1D], c[0x1D]))
            kept = dest.setdefault(i, (o[8:12], c[8:12]))
            for off in STALE_READY:
                ready.setdefault((i, off), (o[off], c[off]))
            for off, name in FIELDS.items():
                if off in STALE_READY:
                    # compared once either side has changed it (weapon_ready sets it outright)
                    if (o[off] != ready[(i, off)][0] or c[off] != ready[(i, off)][1]) and o[off] != c[off]:
                        diffs.append({"combatant": i, "field": name, "offset": "+%02X" % off, "original": o[off], "rewrite": c[off]})
                elif off == 0x1D:
                    # +0x1D starts as whatever the stack held when level_enemies_load (R020+0x0000) wrote the enemy file, which
                    # enemies_place (R025+0x0885) reads into DS:B899 and copies whole (R025+0x0267); it is compared once either side
                    # has changed it (the aim sets it outright, from the readied skill +0x59 (R014+0xA492): -(agility >> 1) - ((skill >> 1) - 150),
                    # so an enemy whose weapon was never readied (its +0x59 a stale byte in the original, 0 in the C#) aims 1 apart per 2 of skill)
                    mine = c[off]
                    if (o[0x59], c[0x59]) == ready[(i, 0x59)]:
                        sar = lambda v: (v - 256 if v > 127 else v) >> 1
                        mine = (mine - (sar(o[0x59]) - sar(c[0x59]))) & 0xFF
                    if (o[off] != first[0] or c[off] != first[1]) and o[off] != mine:
                        diffs.append({"combatant": i, "field": name, "offset": "+1D", "original": o[off], "rewrite": c[off]})
                elif 8 <= off < 12 and o[8:12] == kept[0] and c[8:12] == kept[1]:
                    # the destination is copied whole with the enemy file's record (R025+0x0267) and set by the enemy's first move; a
                    # missile user that never moves keeps the stack bytes there (0x0D70, 0xF272), so it is compared once either side moved
                    continue
                elif o[off] != c[off]:
                    diffs.append({"combatant": i, "field": name, "offset": "+%02X" % off, "original": o[off], "rewrite": c[off]})
            for k, what in ((0, "queued endurance"), (1, "queued strength")):
                if oq[2 * i + k] != cq[2 * i + k]:
                    diffs.append({"combatant": i, "field": what, "original": oq[2 * i + k], "rewrite": cq[2 * i + k]})
            if oar[i] != car[i]:
                diffs.append({"combatant": i, "field": "attack rate", "original": oar[i], "rewrite": car[i]})
        if orand != crand:
            diffs.append({"field": "random", "original": orand, "rewrite": crand})
        if diffs:
            return {"clock": clock, "ticks agreeing": common.index(clock), "differences": diffs[:60]}
    return {"clock": None, "ticks agreeing": len(common), "clocks": [common[0], common[-1]] if common else []}


def pause_length(lines):
    """The passes of the longest stretch at one clock value: the battle's pause."""
    best = run = 0
    last = None
    for line in lines:
        clock = line.split()[2]
        run = run + 1 if clock == last else 1
        last = clock
        best = max(best, run)
    return best


def battle_steps(passes: int):
    """The gate-battle session up to the battle, then the battle for as many frames as passes; no state steps."""
    steps = []
    for kind, value in lockstep.read_script(SESSION):
        if kind == "state":
            continue
        if kind == "wait" and steps and steps[-1] == ("key", "space"):
            steps.append(("wait", str(passes + 400)))
            break
        steps.append((kind, value))
    return steps


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("out")
    ap.add_argument("--save", required=True)
    ap.add_argument("--fights", default=",".join(FIGHTS))
    ap.add_argument("--passes", type=int, default=1500)
    ap.add_argument("--coverage", action="store_true")
    ap.add_argument("--headless", default=str(lockstep.HEADLESS))
    import recording
    recording.add_arguments(ap)
    a = ap.parse_args()
    out = Path(a.out)
    steps = battle_steps(a.passes)
    exe, save = Path(a.exe), Path(a.save)
    rec_parts = recording.common_parts(exe, a.game, "battle_oracle", save)

    def original(name, request, paused):
        return original_side(rec_parts, a.recordings, name, exe, a.game, steps, save, request, a.passes, a.coverage, paused, a.live, a.replay_only)
    report = {}
    for name in a.fights.split(","):
        request = FIGHTS[name]
        folder = out / name
        folder.mkdir(parents=True, exist_ok=True)
        (trace, given, seeds, log, end, set_up, cov), t_original, replayed = original(name, request, PAUSED)
        (folder / "original-battle.trace").write_text("\n".join(trace), encoding="utf-8")
        (folder / "original.log").write_text("\n".join(log + ["end: " + end]), encoding="utf-8")
        if cov is not None:
            (folder / "cov").mkdir(exist_ok=True)
            (folder / "cov" / "cov-0.jsonl").write_text(json.dumps(cov) + "\n", encoding="utf-8")
        jumps = clock_jumps(trace)
        os.environ["DARKLANDS_BATTLE_TRACE"] = "records"
        os.environ["DARKLANDS_BATTLE_REQUEST"] = ",".join(str(v) for v in request) if request else ""
        t = time.time()
        _, output = lockstep.run_rewrite(Path(a.headless), a.game, steps, Path(a.save), seeds, folder, False, set_up, jumps, stale_destinations(trace))
        t_rewrite = time.time() - t
        lines = [l for l in output.splitlines() if l.startswith("pass ")]
        (folder / "rewrite-battle.trace").write_text("\n".join(lines), encoding="utf-8")
        (folder / "rewrite.log").write_text("\n".join(l for l in output.splitlines() if not l.startswith("pass ")), encoding="utf-8")
        # the C#'s pause lasts 297 or 298 passes by the fight (its timer callbacks against the set-up's ticks); the original's ends with
        # the key, so it is run again with the key moved by the difference: the animated pieces draw every 8 passes, and a pause that
        # ends one pass apart shifts every later draw against the passes
        shift = pause_length(lines) - pause_length(trace)
        if shift:
            (trace, given, seeds, log, end, set_up, cov), seconds, again = original(name, request, PAUSED + shift)
            t_original += seconds
            replayed = replayed and again
            (folder / "original-battle.trace").write_text("\n".join(trace), encoding="utf-8")
            (folder / "original.log").write_text("\n".join(log + ["end: " + end]), encoding="utf-8")
            if clock_jumps(trace) != jumps:
                # the long passes (and so the clock jumps) of the original moved with its key: the C# is run again with this run's
                t = time.time()
                _, output = lockstep.run_rewrite(Path(a.headless), a.game, steps, Path(a.save), seeds, folder, False, set_up, clock_jumps(trace), stale_destinations(trace))
                t_rewrite += time.time() - t
                lines = [l for l in output.splitlines() if l.startswith("pass ")]
                (folder / "rewrite-battle.trace").write_text("\n".join(lines), encoding="utf-8")
                (folder / "rewrite.log").write_text("\n".join(l for l in output.splitlines() if not l.startswith("pass ")), encoding="utf-8")
        result = first_difference(parse(trace), parse(lines))
        report[name] = {"request": request, "card's arguments": given, "set-up ticks": set_up, "original end": end,
                        "passes": {"original": len(trace), "rewrite": len(lines)},
                        "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)},
                        "original run": "replayed" if replayed else "live", **result}
        print(name, json.dumps(report[name])[:600], flush=True)
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
