"""Battle oracle (pilot): one battle fought by the ORIGINAL DARKLAND.EXE and by the C# rewrite, compared at every combat tick.

usage: python -I -B battle_oracle.py <DARKLAND.EXE> <game folder> <out folder> --save <DKSAVEn.SAV>
           [--fights name,name] [--passes N] [--coverage] [--headless <Darklands.Host.Headless.exe>]

Both start as lockstep.py's gate-battle session does (the player's saved game, the inn, the street, the city gate at night,
"attack the guards"); that path agrees in both up to the battle. At the battle's entry (the main loop's call of R002+0x0000,
12 words: mode, battlefield, seed, three enemy groups) the fight's arguments replace the card's: in the original on the
stack at R002+0x0000's entry, in the C# by DARKLANDS_BATTLE_REQUEST (the same words). Neither side gives orders: the party
fights as the original's party without orders does, the enemies by their AI. The battle runs for --passes passes.

At the head of every battle pass (R014+0x3166, the combat clock DS:07D4) both write a line: the random generator's state
(DS:7B20), the 13 combatant records (DS:9C55, 0x80 bytes each; the C#'s through CombatantRecords.Write), the queued damage
(DS:D70C, endurance and strength per combatant) and the attack rates (DS:D493). They are compared by combat clock (the first
pass at each clock value), over the record bytes the C# models; the first difference is reported per fight with its field.

With --coverage the original's executed blocks and branch outcomes go to <out>/<fight>/cov/cov-0.jsonl (as card_oracle's;
block_coverage.py takes such a folder as a further source).

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
PAUSED = 296                        # passes the battle stays paused before its key: the C# host's pause under the script's wait:300 (297 passes)
SESSION = HERE / "lockstep-sessions" / "gate-battle.steps"

# mode, battlefield, seed, then (a, b, c) of the three enemy groups as the battle's arguments take them (the C#'s BattleRequest
# (Type, Quality, Count)); None: the gate battle as the card orders it
FIGHTS = {
    "gate": None,
    "street": (0, 0x11, 1234, 12, 2, 3, 2, 2, 2, -1, 0, 0),      # a city street: thugs (DARKLAND.ENM type 12), archers (2: missiles)
    "wild": (0, 0x2E, 777, 42, 2, 4, 1, 2, 2, -1, 0, 0),         # wilderness: wolves (42), huntsmen (1: missiles)
    "mine": (0, 0x0A, 99, 48, 2, 4, 46, 2, 2, -1, 0, 0),         # a mine: kobolds (48), gnomes (46)
    "castle": (0, 0x01, 4321, 33, 2, 2, 0, 2, 2, -1, 0, 0),      # a castle: knights (33), sergeants (0: missiles)
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


def run_original(exe: Path, game: str, steps, save: Path, request, passes: int, coverage: bool):
    """(trace lines, the battle's arguments as the card gave them, log, how it ended, set-up ticks, coverage)."""
    s = lockstep.Session(exe.read_bytes(), game, max_instructions=4_000_000_000, coverage=coverage)
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

    class Trace(list):
        def append(self, line):
            records = s.read(DS, COMBATANTS, COUNT * RECORD)
            super().append("%s cbt %s q %s ar %s" % (line, records.hex().upper(), s.read(DS, QUEUED, 2 * COUNT).hex().upper(),
                                                    s.read(DS, RATES, COUNT).hex().upper()))
            if len(self) >= passes:
                s.budget = 0            # the next block stops the run (Stop: instruction budget)
    s.battle_trace = Trace()
    # the battle's pause (after its first ticks) waits for Space; lockstep's idle test (no random draw for IDLE_MS) never passes on a
    # battlefield whose animated pieces draw at every frame: in a battle the key is given once the clock has stood still PAUSED passes
    idle_test = s._next_key
    given_keys = []

    def next_key():
        t = s.battle_trace
        while len(t) > PAUSED and s.script and s.script[0][0] in ("wait", "shot"):
            s.script.pop(0)             # the C#'s steps
        if (len(t) > PAUSED and s.script and s.script[0][0] == "key"
                and len({line.split()[2] for line in t[-PAUSED:]}) == 1):
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
    return list(s.battle_trace), given, seeds, [l for l in s.log if not l.startswith("INT")], end, s.set_up_ticks, cov


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


def first_difference(original: dict, rewrite: dict):
    common = sorted(set(original) & set(rewrite))
    for clock in common:
        (orand, orec, oq, oar), (crand, crec, cq, car) = original[clock], rewrite[clock]
        diffs = []
        for i in range(COUNT):
            o, c = orec[i * RECORD:(i + 1) * RECORD], crec[i * RECORD:(i + 1) * RECORD]
            if o[0x14] == 0 and c[0x14] == 0:
                continue                # an empty slot: the original keeps old bytes there
            for off, name in FIELDS.items():
                if o[off] != c[off]:
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
    a = ap.parse_args()
    out = Path(a.out)
    steps = battle_steps(a.passes)
    report = {}
    for name in a.fights.split(","):
        request = FIGHTS[name]
        folder = out / name
        folder.mkdir(parents=True, exist_ok=True)
        t = time.time()
        trace, given, seeds, log, end, set_up, cov = run_original(Path(a.exe), a.game, steps, Path(a.save), request, a.passes, a.coverage)
        t_original = time.time() - t
        (folder / "original-battle.trace").write_text("\n".join(trace), encoding="utf-8")
        (folder / "original.log").write_text("\n".join(log + ["end: " + end]), encoding="utf-8")
        if cov is not None:
            (folder / "cov").mkdir(exist_ok=True)
            (folder / "cov" / "cov-0.jsonl").write_text(json.dumps(cov) + "\n", encoding="utf-8")
        os.environ["DARKLANDS_BATTLE_TRACE"] = "records"
        os.environ["DARKLANDS_BATTLE_REQUEST"] = ",".join(str(v) for v in request) if request else ""
        t = time.time()
        _, output = lockstep.run_rewrite(Path(a.headless), a.game, steps, Path(a.save), seeds, folder, False, set_up)
        t_rewrite = time.time() - t
        lines = [l for l in output.splitlines() if l.startswith("pass ")]
        (folder / "rewrite-battle.trace").write_text("\n".join(lines), encoding="utf-8")
        (folder / "rewrite.log").write_text("\n".join(l for l in output.splitlines() if not l.startswith("pass ")), encoding="utf-8")
        result = first_difference(parse(trace), parse(lines))
        report[name] = {"request": request, "card's arguments": given, "set-up ticks": set_up, "original end": end,
                        "passes": {"original": len(trace), "rewrite": len(lines)},
                        "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)}, **result}
        print(name, json.dumps(report[name])[:600], flush=True)
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
