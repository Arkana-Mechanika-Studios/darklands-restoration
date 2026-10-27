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
import battle_orig  # noqa: E402
import lockstep  # noqa: E402
from battle_orig import BATTLE_ENTRY, COMBATANTS, COUNT, PAUSED, QUEUED, RATES, RECORD, stale_destinations  # noqa: E402,F401

# the fields the weapon-readying step sets: an enemy whose weapon is not readied before the fight keeps the
# enemy file buffer's stack bytes there (R025+0x0267 copies the buffer whole), which the C# leaves 0
STALE_READY = {0x0C, 0x0D, 0x20, 0x21, 0x23, 0x4D, 0x4E, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x59}
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


class Recorded(list):
    """A recorded run's pass lines with the clock marks the original made (the attributes of run_original's Trace)."""


def original_side(rec_parts, folder, name, exe, game, steps, save, request, passes, coverage, paused, live, replay_only, snapshots=None):
    """battle_orig.record_fight, or its recording when one with the same key exists: (the run_original tuple, seconds, replayed)."""
    import recording
    rec = recording.Recording(folder, "fights", name, {"kind": "fight", "name": name, "request": request, "passes": passes, "paused": paused,
                                                      "steps": steps, **rec_parts}, live, replay_only, "p%d" % paused)
    got = rec.load(need_coverage=coverage)
    replayed = got is not None
    if got is None:
        got = battle_orig.record_fight(exe, game, steps, save, request, passes, coverage, paused, snapshots)
        rec.store(got)
    trace = Recorded(got["lines"])
    trace.draw_clocks, trace.draws_at = got["draw_clocks"], got["draws_at"]
    trace.phase_clocks, trace.phases_at = got["phase_clocks"], got["phases_at"]
    cov = got["cov"] if coverage else None
    return (trace, got["given"], got["seeds"], got["log"], got["end"], got["set_up"], cov), got["seconds"], replayed


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


clock_jumps = lockstep.clock_jumps            # orig_side.py (its sessions with a battle pass the jumps too)


def lone_clocks(original, rewrite, original_cut: bool = False) -> list:
    """The combat clocks one trace reached and the other did not (a battle that ends or stalls earlier on one side): every clock
    only the original reached, and every clock only the rewrite reached; with original_cut (a fight: the original's run stops at its
    instruction budget or at the script's end, the C#'s runs on) only the rewrite's clocks up to the original's last count."""
    last = max(original, default=None)
    return sorted([c for c in original if c not in rewrite]
                  + [c for c in rewrite if c not in original and not (original_cut and (last is None or c > last))])


def lone_difference(original, rewrite, clock, agreeing) -> dict:
    span = lambda d: [min(d), max(d)] if d else []
    return {"clock": clock, "ticks agreeing": agreeing,
            "differences": [{"field": "battle clock", "original": "reached" if clock in original else "not reached",
                             "rewrite": "reached" if clock in rewrite else "not reached"}],
            "clocks": {"original": span(original), "rewrite": span(rewrite)}}


def first_difference(original: dict, rewrite: dict, original_cut: bool = False):
    """The first combat clock at which the two traces differ: a field of the records, or a clock only one side reached (the shorter
    trace no longer passes: a C# battle that ends earlier than the original's is a difference)."""
    common = sorted(set(original) & set(rewrite))
    lone = lone_clocks(original, rewrite, original_cut)
    start = {}      # slot -> (original, rewrite) +0x1D at its first tick
    ready = {}      # (slot, offset) -> (original, rewrite) at the slot's first tick
    dest = {}       # slot -> (original, rewrite) +0x08..0x0B at its first tick
    for clock in common:
        if lone and clock > lone[0]:
            break
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
    if lone:
        return lone_difference(original, rewrite, lone[0], sum(c < lone[0] for c in common))
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
    rec_parts = recording.common_parts(exe, a.game, ("battle_orig",), save)

    # the snapshot at the battle's entry is the run up to the steps before the wait, the battle's key and the wait after it (the fight's request
    # and passes come after the entry)
    snapshots = None
    if not a.no_snapshot and len(steps) > 3 and steps[-3][0] == "wait" and steps[-2] == ("key", "space") and steps[-1][0] == "wait":
        snapshots = recording.Snapshot(a.recordings, "fight", {**{k: rec_parts[k] for k in recording.COMMON_KEYS}, "prefix": steps[:-3], "ahead": steps[-2], "coverage": a.coverage})

    def original(name, request, paused):
        return original_side(rec_parts, a.recordings, name, exe, a.game, steps, save, request, a.passes, a.coverage, paused, a.live, a.replay_only, snapshots)
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
        result = first_difference(parse(trace), parse(lines), original_cut=True)     # the original stops at its budget, the C# runs on
        report[name] = {"request": request, "card's arguments": given, "set-up ticks": set_up, "original end": end,
                        "passes": {"original": len(trace), "rewrite": len(lines)},
                        "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)},
                        "original run": "replayed" if replayed else "live", **result}
        # the sound requests of the whole run (the program's start, the load, the battle's set-up, its effects) against the C# host's; a lead apart from
        # the passes' verdict above (the original's log has them since patch 06 of local/batch/orig-side-queue, battle_orig.py)
        sound = lockstep.sound_compare(log, output, original_cut=True)
        report[name]["sound"] = {k: v for k, v in sound.items() if k != "segments"}
        print(name, json.dumps(report[name])[:600], flush=True)
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
