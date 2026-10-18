"""Original-byte corpus for the wilderness battlefield generator (R008 wild_main).

usage: python -I build_wild_corpus.py <DARKLAND.EXE> <game folder> <out wild.json> [scenarios] [workers]

wild_main runs from the unchanged EXE (sections 136 data, 4 loader, 8 generator) on the enemy
buffer prepared as battlefield_build prepares it (darkland.enm/.lst, monsters_clear, groups_clear,
the tile-description defaults). Its I/O is stubbed by hooks: the level buffer allocation, the read
of iwild* (decoded here from the player's IMAPS.CAT), the level save (captured) and
party_lore_level (a scripted lore level). The party's able members, the terrain under the party,
the month and the two debugging globals forceMap (DS:C100) and forceSide (DS:C0FE) are written into
the data segment before the call.
Stored: the request, and per scenario the saved level image, the tile description, the kind and
group records, counts and the random state.
"""
from __future__ import annotations

import hashlib
import json
import random
import struct
import sys
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import run  # noqa: E402
from build_enemy_corpus import Machine, MON_COUNT, GROUP_COUNT, KINDS, GROUPS, ENEMY_SEG, ITEM_SEG, DS, HOLDRAND  # noqa: E402
from build_city_corpus import cat_members, cat_decode  # noqa: E402

LEVEL_SEG = 0x5000
CUR_LOC, COMBAT_FLAGS = 0x907E, 0x9080
WORLD_TERRAIN, MONTH, FORCE_MAP, FORCE_SIDE = 0xD843, 0x00E4, 0xC100, 0xC0FE
PARTY, PARTY_STRIDE = 0x9C69, 0x80
TILE_DESC = 0xF504
WILD_TYPES = list(range(0x2E, 0x56)) + [0x61, 0x62, 0x63] + list(range(0x6F, 0x7A))
CALLSITES = {"1432f": "alloc", "143ad": "free", "147e3": "read", "1439e": "save", "15ad0": "lore", "16d94": "lore"}

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


def make_scenarios(count: int) -> list[dict]:
    r = random.Random(20261021)
    out = []
    for n in range(count):
        bf = WILD_TYPES[n % len(WILD_TYPES)] if n < len(WILD_TYPES) else r.choice(WILD_TYPES)
        specs = [(r.choice([r.randint(0, 0x51), 0x12, 0x03, 0x22, 0x24, 0x26, 0x2A]), r.randint(0, 6), r.randint(0, 8))
                 for _ in range(3)]
        if r.random() < 0.2:
            specs[2] = (-1, 0, 0)
        able = r.randint(0, 5)
        status = [1] * able + [r.choice([0, 2, 3]) for _ in range(5 - able)]
        r.shuffle(status)
        sc = {"bf": bf, "seed": r.randint(0, 0xFFFF), "specs": specs, "terrain": r.randint(0, 29),
              "month": r.randint(0, 11), "lore": r.randint(1, 10), "party": status, "ableMembers": able,
              "forceMap": -1, "forceSide": -1}
        if n >= 80:
            # the debugging globals: dormant in the shipped game, exercised here against the asm
            if r.random() < 0.6:
                sc["forceMap"] = r.randint(1, 19)
            if r.random() < 0.6:
                sc["forceSide"] = r.randint(1, 4)
        out.append(sc)
    return out


def run_scenario(sc: dict) -> dict:
    exe, lst, imaps = _G["exe"], _G["lst"], _G["imaps"]
    memory_enemy = bytearray(_G["snapshot"][0])
    ds = dict(_G["snapshot"][1])
    words = ((HOLDRAND, struct.pack("<I", sc["seed"])), (CUR_LOC, struct.pack("<h", -1)),
             (COMBAT_FLAGS, b"\x00\x00"), (WORLD_TERRAIN, struct.pack("<h", sc["terrain"])),
             (MONTH, struct.pack("<h", sc["month"])), (FORCE_MAP, struct.pack("<h", sc["forceMap"])),
             (FORCE_SIDE, struct.pack("<h", sc["forceSide"])))
    for off, data in words:
        for k, b in enumerate(data):
            ds[off + k] = b
    for i, status in enumerate(sc["party"]):
        ds[PARTY + i * PARTY_STRIDE] = status
    saved, reads = {}, []

    def read_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode().upper()
        reads.append(name)
        data = cat_decode(imaps[name])[:0x33FC]
        uc.mem_write(stack[2] * 16 + stack[1], data)
        return 0, 0

    def save_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode()
        saved[name] = bytes(uc.mem_read(stack[2] * 16 + stack[1], stack[3]))
        return 0, 0

    memory = [{"segment": "%04X" % ENEMY_SEG, "offset": "0000", "hex": bytes(memory_enemy).hex()},
              {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": lst[:0x4000].hex()}]
    memory += [{"offset": "%04X" % off, "hex": "%02X" % b} for off, b in sorted(ds.items())]
    args = [sc["bf"]] + [v for s in sc["specs"] for v in s]
    res = run(exe, {"unit": "wild_main", "entry": {"segment": "1432", "offset": "0000"}, "sections": [136, 4, 8],
                    "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args], "follow": True,
                    "callsites": CALLSITES, "stub": list(set(CALLSITES.values())), "track_all": True,
                    "max_instructions": 30000000, "default_result": {"ax": 0, "dx": 0}},
              hooks={"alloc": lambda uc, st: (LEVEL_SEG, 0), "free": lambda uc, st: (0, 0), "read": read_hook,
                     "save": save_hook, "lore": lambda uc, st: (sc["lore"], 0)})
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
              for k in range(ds_word(MON_COUNT + 2 * lv))] for lv in range(2)]
    groups = [[bytes(enemy[GROUPS + (lv * 10 + g) * 0x1E:GROUPS + (lv * 10 + g + 1) * 0x1E]).hex()
               for g in range(ds_word(GROUP_COUNT + 2 * lv))] for lv in range(2)]
    holdrand = struct.unpack("<I", bytes(after_ds.get(HOLDRAND + k, ds[HOLDRAND + k]) for k in range(4)))[0]
    out_levels = {name: {"sha256": hashlib.sha256(d).hexdigest(), "data": d.hex()} for name, d in saved.items()}
    return {"function": "wild_main", "ax": "%04X" % levels, "changed": {}, "inputs": {
        **sc, "levels": levels, "reads": reads, "saved": out_levels,
        "terrainDescription": bytes(enemy[TILE_DESC:TILE_DESC + 0xC4]).hex(), "kinds": kinds, "groups": groups,
        "holdrand": holdrand, "combatFlags": ds_word(COMBAT_FLAGS)}}


def main() -> None:
    exe_path, game_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 80
    workers = int(sys.argv[5]) if len(sys.argv) > 5 else 8
    scenarios = make_scenarios(count)
    with Pool(workers, initializer=init_worker, initargs=(exe_path, game_path)) as pool:
        cases = pool.map(run_scenario, scenarios, chunksize=1)
    exe = Path(exe_path).read_bytes()
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND, "cases": cases}
    Path(out).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(cases))


if __name__ == "__main__":
    main()
