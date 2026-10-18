"""Original-byte corpus for the battle's attack rules (R014 18BF): attack_rate, to_hit_roll
(with the hand-to-hand chance 0592 and the inline missile chance), hit_damage + damage_queue,
damage_apply and weapon_ready.

usage: python -I build_battle_corpus.py <DARKLAND.EXE> <out battle.json>

Each case runs the original function from the unchanged EXE (sections 136 data + 14 code) with
generated combatant records, executing every call for real except the cloud test, the cloud
membership test and the party panel redraw, which are stubbed. Only inputs and results are
stored; no game bytes.
"""
from __future__ import annotations

import hashlib
import json
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import CoverageSet, commit_coverage, run  # noqa: E402

DS = 0x321A
HOLDRAND = 0x7B20
COMBATANTS = 0x9C55
ATTACK_RATE, QUEUED, SHOWN = 0xD493, 0xD70C, 0xD4C7
REAL_COMBAT, DEMO, CLOUDS = 0xA6C1, 0xC21A, 0xA33D
REACH_SEG, REACH_OFF = 0x20A5, 0x7900
ANIM_FILE_TABLE = 0x1174
SCRATCH = 0xFD00
CRIT = 0xFE00
SEG = "18BF"
FN = {"attack_rate": "0002", "to_hit_roll": "02A8", "hit_damage": "0778", "damage_queue": "08CA",
      "damage_apply": "098E", "weapon_ready": "0A40", "skill_try_improve": "0000"}
CALLSITES = {"19045": "cloud_blocks_line", "1930f": "inside_cloud", "19624": "party_panel_rows", "1f158": "skill_message_box"}
DIFFICULTY = 0x906A


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def ds(offset: int, data: bytes) -> dict:
    return {"offset": "%04X" % offset, "hex": data.hex()}


def sb(r) -> int:
    return r.choice([0, 1, 5, 10, 20, 35, 50, 70, 99, r.randint(-20, 120)]) & 0xFF


ACTIONS = [0, 1, 0x12, 0x06, 0x0A, 0x22, 0x80, 0x100, 0x102, 0x92, 0x26]


def combatant(r) -> bytearray:
    rec = bytearray(0x80)
    struct.pack_into("<H", rec, 0x10, r.choice(ACTIONS))
    rec[0x14] = r.choice([1, 1, 1, 1, 2, 3, 0, 5])
    rec[0x16] = r.randint(0, 6)
    rec[0x0D] = sb(r)
    rec[0x1D] = r.randint(0, 80)
    rec[0x20] = r.randint(0, 255)
    rec[0x21] = r.randint(0, 255)
    rec[0x22] = r.choice([0xFF, r.randint(0, 33)])
    rec[0x23] = sb(r)
    rec[0x24] = sb(r)
    rec[0x48] = r.choice([0, 0x10, 0x23, 0xF1, r.randint(0, 255)])
    rec[0x49] = r.randint(0, 127)
    rec[0x4A] = r.choice([0xFF, r.randint(0, 12)])
    rec[0x4B] = r.choice([0xFF, r.randint(67, 84)])
    rec[0x4C] = r.choice([0xFF, r.randint(67, 84)])
    rec[0x4D] = r.randint(0, 6)
    rec[0x4E] = r.randint(0, 6)
    rec[0x4F] = sb(r)
    rec[0x50] = sb(r)
    rec[0x51] = r.randint(0, 33)
    rec[0x52] = r.randint(0, 200)
    rec[0x53] = r.choice([0x13, 0x24, 0x15, 0x36, r.randint(0, 255)])
    rec[0x54] = sb(r)
    rec[0x55] = r.choice([0, 0x80, 3, 0x85, r.randint(0, 255)])
    rec[0x56] = sb(r)
    rec[0x57] = r.choice([0x1D, 0x9D, sb(r)])
    rec[0x58] = sb(r)
    rec[0x59] = sb(r)
    rec[0x5A] = sb(r)
    rec[0x5B] = sb(r)
    rec[0x5C] = r.choice([0x5F, 0x60, 0x61, 0xFF, 0x10])
    for k in range(7):
        rec[0x5D + k] = r.randint(-3, 99) & 0xFF
        rec[0x64 + k] = r.randint(1, 99)
    for k in range(19):
        rec[0x6B + k] = sb(r)
    return rec


def battle(r):
    recs = [combatant(r) for _ in range(13)]
    reach = bytes(r.choice([0, 0, 1]) for _ in range(13 * 14))
    rates = bytes(r.choice([0xFF, r.randint(0, 120)]) for _ in range(13))
    queued = bytes(r.choice([0, r.randint(0, 50)]) for _ in range(26))
    shown = bytes(r.randint(0, 20) for _ in range(13))
    state = r.getrandbits(32)
    real = r.choice([0, 1, 1])
    memory = [ds(COMBATANTS + 0x80 * i, rec) for i, rec in enumerate(recs)]
    memory += [{"segment": "%04X" % REACH_SEG, "offset": "%04X" % REACH_OFF, "hex": reach.hex()},
               ds(ATTACK_RATE, rates), ds(QUEUED, queued), ds(SHOWN, shown), ds(REAL_COMBAT, bytes([real])),
               ds(DEMO, b"\x00"), ds(CLOUDS, b"\x00"), ds(HOLDRAND, struct.pack("<I", state)),
               ds(COMBATANTS + 13 * 0x80 + 0x14, b"\x00")]  # the 14th "status" the enemies' loop reads
    inputs = {"combatants": [x.hex() for x in recs], "reach": reach.hex(), "rates": rates.hex(),
              "queued": queued.hex(), "shown": shown.hex(), "state": state, "real": real}
    return memory, inputs


COUNTS = (("attack_rate", 100), ("to_hit_roll", 250), ("hit_damage", 150), ("damage_queue", 60),
          ("damage_apply", 80), ("weapon_ready", 100), ("skill_try_improve", 120))
# more of the same generated battles (docs/faithfulness/branches-missed.csv: the rarer combinations of the
# allies next to a target, the attackers of one combatant, shields and loads); a generator of its own so
# the cases above stay as they were
MORE = (("attack_rate", 600), ("to_hit_roll", 4000), ("hit_damage", 1500), ("damage_queue", 600),
        ("damage_apply", 600), ("weapon_ready", 1000), ("skill_try_improve", 600))


def cases(r, counts=COUNTS):
    out = []
    for name, count in counts:
        for _ in range(count):
            memory, inputs = battle(r)
            results = {}
            if name == "attack_rate":
                i = r.randint(0, 12)
                args = [i]
            elif name == "to_hit_roll":
                a, t = r.randint(0, 12), r.randint(0, 12)
                cloud = r.choice([0, 0, 0, 1])
                inputs["cloud"] = cloud
                results = {"cloud_blocks_line": [{"ax": cloud}]}
                args = [a, t, CRIT]
            elif name == "hit_damage":
                a, t = r.randint(0, 12), r.randint(0, 12)
                args = [a, t, r.choice([2, 3]), r.randint(0, 1)]
            elif name == "damage_queue":
                args = [r.randint(-5, 45), r.randint(0, 12), r.randint(-2, 12), r.randint(-2, 12)]
            elif name == "damage_apply":
                args = [r.randint(0, 12)]
            elif name == "skill_try_improve":
                difficulty = r.randint(0, 2)
                memory.append(ds(DIFFICULTY, bytes([difficulty])))
                inputs["difficulty"] = difficulty
                args = [r.randint(0, 4), r.randint(0, 18), r.choice([0, 1])]
            else:
                i = r.randint(0, 12)
                w = r.choice([-1, r.randint(0, 33), r.randint(0, 33)])
                alt = r.choice([0, 0, 0x80])
                memory.append(ds(ANIM_FILE_TABLE, b"".join(struct.pack("<H", SCRATCH + 4 * k) for k in range(7))))
                args = [i, w, alt]
            inputs["args"] = args
            out.append((name, memory, [h16(a) for a in args], inputs, results))
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    result, images = [], None
    seen = CoverageSet()
    base = cases(r)
    for n, (name, memory, args, inputs, results) in enumerate(base + cases(random.Random(20261019), MORE)):
        seg, sections = ("1EF8", [136, 14, 21]) if name == "skill_try_improve" else (SEG, [136, 14])
        res = run(exe, {"unit": name, "entry": {"segment": seg, "offset": FN[name]}, "sections": sections,
                        "ds": DS, "memory": memory, "args": args, "follow": True, "callsites": CALLSITES,
                        "stub": list(CALLSITES.values()), "results": results, "coverage": True,
                        "coverage_defer": True})
        images = res["loadedImages"]
        if not seen.adds(res) and n >= len(base):
            continue   # the extra cases are kept when they exercise something new
        commit_coverage(res)
        result.append({"function": name, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                       "changed": {"%05X" % (DS * 16 + int(c["offset"], 16)): c["after"] for c in res["dsChanged"]}})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % COMBATANTS, "holdrand": "%04X" % HOLDRAND,
        "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
