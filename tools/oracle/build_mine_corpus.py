"""Original-byte corpus for the mine battlefield generator (R009 mine_build).

usage: python -I build_mine_corpus.py <DARKLAND.EXE> <game folder> <out mine.json> [scenarios] [workers]

mine_build runs from the unchanged EXE (sections 136 data, 4 loader, 9 generator) on the enemy
buffer prepared as battlefield_build prepares it (darkland.enm/.lst, monsters_clear, groups_clear,
the tile-description defaults). Its I/O is stubbed by hooks: the two buffer allocations (the four
level slots and the flag maps), the reads of iminegen.* / iminspec.* (decoded here from the
player's IMAPS.CAT), the level saves (captured) and party_lore_level (a scripted lore level). The
cleared-level memory (DS:DA34), the last battle location (DS:DB27), the location (cur_loc) and the
two debugging globals DS:C100 and DS:C0FE are written into the data segment before the call.
Stored: the request, and per scenario every saved level image (deflated), the tile description,
the kind and group records of every level, counts, the random state and the memory words.
"""
from __future__ import annotations

import base64
import hashlib
import json
import random
import struct
import sys
import zlib
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import run  # noqa: E402
from build_enemy_corpus import Machine, MON_COUNT, GROUP_COUNT, KINDS, GROUPS, ENEMY_SEG, ITEM_SEG, DS, HOLDRAND  # noqa: E402
from build_city_corpus import cat_members, cat_decode  # noqa: E402

LEVEL_SEG, FLAG_SEG = 0x5000, 0x8000
CUR_LOC, COMBAT_FLAGS, CLEARED, LAST_LOC = 0x907E, 0x9080, 0xDA34, 0xDB27
FORCE_MAP, FORCE_SIDE, DEBUG_SWITCH = 0xC100, 0xC0FE, 0x11DD
TILE_DESC = 0xF504
MINE_TYPES = list(range(0x0A, 0x11)) + [0x66, 0x67, 0x68, 0x69, 0x6C, 0x6D] + list(range(0x7F, 0x8A))
# linear addresses of the calls the oracle stubs (R009 frame 1432 / 182c; `call` instruction addresses)
CALLSITES = {"14335": "alloc", "14355": "alloc", "144a1": "free", "144b0": "free",
             "14553": "lore", "146c1": "lore", "1490a": "lore", "14b0d": "lore", "14e20": "lore",
             "15340": "read", "18241": "save"}

_G: dict = {}


def init_worker(exe_path: str, game_path: str) -> None:
    game = Path(game_path)
    exe = Path(exe_path).read_bytes()
    base = Machine(exe, (game / "DARKLAND.ENM").read_bytes(), (game / "DARKLAND.LST").read_bytes())
    base.call("monsters_clear", [])
    base.call("groups_clear", [])
    base.enemy[TILE_DESC:TILE_DESC + 0xC4] = bytes(0xC4)
    for at in (0x0A, 0x24, 0x3D, 0x57, 0x70, 0x8A, 0xA4):
        base.enemy[TILE_DESC + at:TILE_DESC + at + 16] = b"\xff" * 16
    base.enemy[TILE_DESC + 0x23] = 2
    for at in (0x56, 0x89, 0xA3):
        base.enemy[TILE_DESC + at] = 8
    _G.update(exe=exe, lst=(game / "DARKLAND.LST").read_bytes(), imaps=cat_members(game / "IMAPS.CAT"),
              snapshot=(bytes(base.enemy), dict(base.ds)))


def random_specs(r: random.Random) -> list:
    specs = [(r.choice([r.randint(0, 0x51), 0x12, 0x03, 0x22, 0x24, 0x26, 0x2A]), r.randint(0, 6), r.randint(0, 20))
             for _ in range(3)]
    if r.random() < 0.15:
        specs[1] = (-1, 0, 0)
    return specs


# fixed-layout sections that carve a wall gap (2c98), by battlefield type: swept over all eight turns
GAP_SECTIONS = {0x82: (3, 8, 10), 0x83: (1, 2, 7, 8, 11, 12), 0x84: (1, 2, 3, 4, 5), 0x85: (1, 3), 0x86: (5,)}


def make_scenarios(count: int) -> list[dict]:
    r = random.Random(20261022)
    # (bf, section named by the third spec or None, turn named by its quality or None, debug switch, forceMap, forceSide)
    plan = [(t, None, None, 0, -1, -1) for t in MINE_TYPES]
    plan += [(0x82, lv, None, 0, -1, -1) for lv in list(range(0, 16)) + [20, -1]]
    plan += [(0x83, lv, None, 0, -1, -1) for lv in range(0, 15)]
    for t in (0x84, 0x85, 0x86):
        plan += [(t, lv, None, 0, -1, -1) for lv in range(0, 15)]
    # every turn of every section with a gap; the odd turns also with the generator's debugging switch on
    for t, sections in GAP_SECTIONS.items():
        plan += [(t, lv, turn, turn & 1, -1, -1) for lv in sections for turn in range(8)]
    # the debugging overrides of the exit used (DS:C0FE) and of the general map (DS:C100) on the fixed-layout mines
    for t in (0x82, 0x83, 0x84, 0x85, 0x86):
        plan += [(t, lv, None, 0, -1, side) for lv in (1, 3, 5, 13) for side in (0, 1, 2, 5)]
    for t in (0x0A, 0x82, 0x83, 0x84, 0x85, 0x86):
        plan += [(t, 2, None, 0, fm, -1) for fm in (0, 3, 8, 21)]
    out = []
    n = 0
    while len(out) < max(count, len(plan)):
        bf, lv, turn, debug, force_map, force_side = plan[n] if n < len(plan) else (r.choice(MINE_TYPES), None, None, 0, -1, -1)
        n += 1
        specs = random_specs(r)
        if bf in (0x82, 0x83, 0x84, 0x85, 0x86):
            if lv is None:
                lv = r.randint(1, 14)
            specs[2] = (lv, specs[2][1] if turn is None else turn, specs[2][2])
        cleared = r.choice([0, 0, 0x7FFF, r.randint(0, 0x7FFF), r.randint(0, 0x7FFF)])
        location = r.choice([5, 7, -1])
        last = r.choice([location, location, -1, 9])
        sc = {"index": len(out), "bf": bf, "seed": r.randint(0, 0xFFFF), "specs": specs, "lore": r.randint(1, 10), "cleared": cleared,
              "lastLocation": last, "location": location, "forceMap": force_map, "forceSide": force_side}
        if n > len(plan) and r.random() < 0.35:
            # the debugging globals: dormant in the shipped game, exercised here against the asm
            sc["forceMap"] = r.choice([0, r.randint(1, 21)])
            sc["forceSide"] = r.choice([-1, r.randint(0, 6)])
        if debug or (n > len(plan) and r.random() < 0.15):
            sc["debugSwitch"] = 1   # DS:11DD, the generator's other debugging switch
        out.append(sc)
    return out


def run_scenario(sc: dict) -> dict:
    exe, lst, imaps = _G["exe"], _G["lst"], _G["imaps"]
    memory_enemy = bytearray(_G["snapshot"][0])
    ds = dict(_G["snapshot"][1])
    words = ((HOLDRAND, struct.pack("<I", sc["seed"])), (CUR_LOC, struct.pack("<h", sc["location"])),
             (COMBAT_FLAGS, b"\x00\x00"), (CLEARED, struct.pack("<H", sc["cleared"])),
             (LAST_LOC, struct.pack("<h", sc["lastLocation"])), (FORCE_MAP, struct.pack("<h", sc["forceMap"])),
             (FORCE_SIDE, struct.pack("<h", sc["forceSide"])), (DEBUG_SWITCH, bytes([sc.get("debugSwitch", 0)])))
    for off, data in words:
        for k, b in enumerate(data):
            ds[off + k] = b
    saved, reads = {}, []

    def alloc_hook(uc, stack):
        size = stack[0] | stack[1] << 16
        return (LEVEL_SEG if size == 0xCFF0 else FLAG_SEG), 0

    def read_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode().upper()
        reads.append(name)
        data = cat_decode(imaps[name])[:0x33FC]
        uc.mem_write(stack[2] * 16 + stack[1], data)
        return 0, 0

    def save_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode()
        assert name not in saved, "level saved twice " + name
        saved[name] = bytes(uc.mem_read(stack[2] * 16 + stack[1], stack[3]))
        return 0, 0

    memory = [{"segment": "%04X" % ENEMY_SEG, "offset": "0000", "hex": bytes(memory_enemy).hex()},
              {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": lst[:0x4000].hex()}]
    memory += [{"offset": "%04X" % off, "hex": "%02X" % b} for off, b in sorted(ds.items())]
    s = sc["specs"]
    args = [sc["bf"], s[0][0], s[0][1], s[0][2], s[1][0], s[1][1], s[1][2], s[2][0], s[2][1]]
    try:
        res = run(exe, {"unit": "mine_build", "entry": {"segment": "1432", "offset": "0000"}, "sections": [136, 4, 9],
                        "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args], "follow": True,
                        "callsites": CALLSITES, "stub": list(set(CALLSITES.values())), "track_all": True,
                        "max_instructions": 200000000, "default_result": {"ax": 0, "dx": 0}},
                  hooks={"alloc": alloc_hook, "free": lambda uc, st: (0, 0), "read": read_hook, "save": save_hook,
                         "lore": lambda uc, st: (sc["lore"], 0)})
    except SystemExit as stop:
        # the original itself stopped (an instruction limit, an INT, exit_): the scenario is not usable
        return {"error": str(stop), "inputs": sc}
    enemy = bytearray(memory_enemy)
    after_ds = {}
    for c in res["memChanged"]:
        lin, val = int(c["linear"], 16), int(c["after"], 16)
        if ENEMY_SEG * 16 <= lin < ENEMY_SEG * 16 + 0x10000:
            enemy[lin - ENEMY_SEG * 16] = val
        elif DS * 16 <= lin < DS * 16 + 0x10000:
            after_ds[lin - DS * 16] = val

    def ds_word(off):
        lo = after_ds.get(off, ds.get(off, 0))
        hi = after_ds.get(off + 1, ds.get(off + 1, 0))
        return struct.unpack("<h", bytes([lo, hi]))[0]

    levels = int(res["final"]["ax"], 16)
    kinds = [[bytes(enemy[KINDS + (lv * 12 + k) * 0xD6:KINDS + (lv * 12 + k + 1) * 0xD6]).hex()
              for k in range(ds_word(MON_COUNT + 2 * lv))] for lv in range(16)]
    groups = [[bytes(enemy[GROUPS + (lv * 10 + g) * 0x1E:GROUPS + (lv * 10 + g + 1) * 0x1E]).hex()
               for g in range(ds_word(GROUP_COUNT + 2 * lv))] for lv in range(16)]
    holdrand = struct.unpack("<I", bytes(after_ds.get(HOLDRAND + k, ds[HOLDRAND + k]) for k in range(4)))[0]
    out_levels = {name: {"sha256": hashlib.sha256(d).hexdigest(),
                         "deflated": base64.b64encode(zlib.compress(d, 9)).decode()} for name, d in saved.items()}
    return {"function": "mine_build", "ax": "%04X" % levels, "changed": {}, "inputs": {
        **sc, "levels": levels, "reads": reads, "saved": out_levels, "instructions": res["instructions"],
        "terrain": bytes(enemy[TILE_DESC:TILE_DESC + 0xC4]).hex(), "kinds": kinds, "groups": groups,
        "holdrand": holdrand, "combatFlags": ds_word(COMBAT_FLAGS), "clearedAfter": ds_word(CLEARED),
        "lastLocationAfter": ds_word(LAST_LOC)}}


def main() -> None:
    exe_path, game_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 150
    workers = int(sys.argv[5]) if len(sys.argv) > 5 else 8
    scenarios = make_scenarios(count)
    cases, failed = [], []
    # results are kept as they arrive (<out>.partial.jsonl) so that a long run can be inspected before it ends
    with Pool(workers, initializer=init_worker, initargs=(exe_path, game_path)) as pool, open(out + ".partial.jsonl", "w") as partial:
        for case in pool.imap_unordered(run_scenario, scenarios, chunksize=1):
            partial.write(json.dumps(case) + "\n")
            partial.flush()
            (failed if "error" in case else cases).append(case)
    for case in failed:
        print("unusable scenario:", case["error"], case["inputs"]["bf"], case["inputs"]["specs"])
    cases.sort(key=lambda c: c["inputs"]["index"])
    exe = Path(exe_path).read_bytes()
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND, "cases": cases}
    Path(out).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(cases))


if __name__ == "__main__":
    main()
