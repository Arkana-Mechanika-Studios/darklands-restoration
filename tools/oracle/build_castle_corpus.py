"""Original-byte corpus for the castle and keep battlefield generators (R005 gen_battlefield with R006 and R007).

usage: python -B -I build_castle_corpus.py <DARKLAND.EXE> <game folder> <out castle.json> [scenarios] [workers]

gen_battlefield (section 5, 1432:0000) runs from the unchanged EXE (sections 136 data, 4 loader, 5 driver,
6 level planner) on the enemy buffer prepared as battlefield_build prepares it (darkland.enm/.lst,
monsters_clear, groups_clear, the tile-description defaults). When the driver calls castle_build
(R007, which shares its load address with R006) the harness swaps section 7 in over section 6, as the
overlay manager would. The overlay vector thunks in the root (thunk 09C0:1BAD ... thunk 09C0:1C11) are patched to jump
straight to their targets. I/O is stubbed by hooks: the level saves (captured) and party_lore_level
(a scripted lore level).

The generator's work area (segment DS:C105, 64 KB, cleared by gen_clear) is placed at WORK_SEG; the level
buffer floor_img (20A5:0000) is zeroed before the run. Stored per scenario: the request, the saved
level images, the tile description, the kind and group records, counts, the random state, the combat
flags and the memory of cleared levels. The level images are stored zlib-compressed (base64). Scenarios in
which the original never returns (an endless draw in the party-room choice of type 0x7C) are left out.

Environment: CASTLE_SEED (scenario generator seed), CASTLE_MAX (instruction limit per scenario), and
CASTLE_WORK_DUMP=<dir>, which writes the work area before castle_build (*_plan.bin) and after the run
(*_final.bin) there for debugging (not part of the corpus).
"""
from __future__ import annotations

import base64
import hashlib
import json
import os
import random
import struct
import sys
import zlib
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import run, Rtlink  # noqa: E402
from build_enemy_corpus import Machine, MON_COUNT, GROUP_COUNT, KINDS, GROUPS, ENEMY_SEG, ITEM_SEG, DS, HOLDRAND  # noqa: E402

WORK_SEG = 0x8000
FLOOR_SEG = 0x20A5
FLOOR_SIZE = 0x33FC
CUR_LOC, COMBAT_FLAGS, CLEARED, LAST_LOC, WORK_PTR = 0x907E, 0x9080, 0xDA34, 0xDB27, 0xC105
RES = 0xF000   # outside the emulated stack (writes inside it are not reported)
TILE_DESC = 0xF504
CASTLE_TYPES = list(range(0, 9)) + [0x27, 0x28] + list(range(0x7A, 0x7F))
CALLSITES = {"15c62": "save", "17707": "lore"}
# the overlay vector thunks of R006 (thunk 09C0:1BAD..thunk 09C0:1C07) and R007 (thunk 09C0:1C11): linear addresses in the root (E8 rel16, EA far target)
THUNKS_S006 = [0x0B7AD, 0x0B7B7, 0x0B7C1, 0x0B7CB, 0x0B7D5, 0x0B7DF, 0x0B7E9, 0x0B7F3, 0x0B7FD, 0x0B807]
THUNK_S007 = 0x0B811

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
    rt = Rtlink(exe)
    images = {i: exe[rt.sections[i]["file_offset"]:rt.sections[i]["file_offset"] + rt.sections[i]["length"]] for i in (6, 7)}
    _G.update(exe=exe, lst=(game / "DARKLAND.LST").read_bytes(), snapshot=(bytes(base.enemy), dict(base.ds)),
              images=images, load_seg=rt.sections[7]["load_seg"])


def make_scenarios(count: int) -> list[dict]:
    r = random.Random(int(os.environ.get("CASTLE_SEED", 20261022)))
    out = []
    for n in range(count):
        bf = CASTLE_TYPES[n % len(CASTLE_TYPES)] if n < len(CASTLE_TYPES) * 2 else r.choice(CASTLE_TYPES)
        specs = [(r.choice([r.randint(0, 0x51), 0x12, 0x03, 0x22, 0x24, 0x26, 0x23, 0x16, 0x50, 0x4F]), r.randint(0, 6), r.randint(0, 9))
                 for _ in range(3)]
        if r.random() < 0.15:
            specs[2] = (-1, 0, 0)
        out.append({"bf": bf, "seed": r.randint(0, 0xFFFF), "specs": specs, "cleared": r.choice([0, 0, 0, 1, 2, 3, 6, 31, 0x12]),
                    "lastLocation": r.choice([-1, 5]), "location": r.choice([5, 7]), "lore": r.randint(1, 10)})
    return out


def run_scenario(sc: dict) -> dict:
    exe, lst = _G["exe"], _G["lst"]
    memory_enemy = bytearray(_G["snapshot"][0])
    ds = dict(_G["snapshot"][1])
    for off, data in ((HOLDRAND, struct.pack("<I", sc["seed"])), (CUR_LOC, struct.pack("<h", sc["location"])),
                      (COMBAT_FLAGS, b"\x00\x00"), (CLEARED, struct.pack("<H", sc["cleared"])),
                      (LAST_LOC, struct.pack("<h", sc["lastLocation"])), (RES, b"\x00\x00"),
                      (WORK_PTR, struct.pack("<H", WORK_SEG))):
        for k, b in enumerate(data):
            ds[off + k] = b
    saved = {}
    dumps = {}

    def save_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode()
        saved[name] = bytes(uc.mem_read(stack[2] * 16 + stack[1], stack[3]))
        return 0, 0

    def swap_in_s007(uc):
        # the work area as the planner left it, then the overlay manager loads section 7 over section 6
        dumps["plan"] = bytes(uc.mem_read(WORK_SEG * 16, 0x10000))
        uc.mem_write(_G["load_seg"] * 16, bytes(len(_G["images"][6])))
        uc.mem_write(_G["load_seg"] * 16, _G["images"][7])
        # Unicorn keeps translated blocks of the code that was there before
        uc.ctl_remove_cache(_G["load_seg"] * 16, _G["load_seg"] * 16 + len(_G["images"][6]))

    memory = [{"segment": "%04X" % ENEMY_SEG, "offset": "0000", "hex": bytes(memory_enemy).hex()},
              {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": lst[:0x4000].hex()},
              {"segment": "%04X" % FLOOR_SEG, "offset": "0000", "hex": bytes(FLOOR_SIZE).hex()},
              {"segment": "%04X" % WORK_SEG, "offset": "0000", "hex": bytes(0x10000).hex()}]
    for at in THUNKS_S006 + [THUNK_S007]:
        memory.append({"segment": "0000", "offset": "%04X" % at, "hex": "909090"})
    memory += [{"offset": "%04X" % off, "hex": "%02X" % b} for off, b in sorted(ds.items())]
    args = [sc["bf"], RES] + [v for s in sc["specs"] for v in s]
    try:
        res = run(exe, {"unit": "gen_battlefield", "entry": {"segment": "1432", "offset": "0000"}, "sections": [136, 4, 5, 6],
                        "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args], "follow": True,
                        "callsites": CALLSITES, "stub": list(set(CALLSITES.values())), "track_all": True,
                        "max_instructions": int(os.environ.get("CASTLE_MAX", 20000000)), "default_result": {"ax": 0, "dx": 0}},
                  hooks={"save": save_hook, "lore": lambda uc, st: (sc["lore"], 0)},
                  at_hooks={THUNK_S007: swap_in_s007})
    except SystemExit as e:
        if "instruction limit" in str(e):
            # the original never returns: the party-room draw of battlefield 0x7C repeats forever when only the gate room can be drawn
            return {"function": "gen_battlefield_freeze", "ax": "0000", "changed": {}, "inputs": sc}
        raise
    assert int(res["stack"]["min_sp"], 16) - 0x20 > RES + 2, "the stack reached the result word"
    enemy = bytearray(memory_enemy)
    after_ds = {}
    work = bytearray(0x10000)
    for c in res["memChanged"]:
        lin, val = int(c["linear"], 16), int(c["after"], 16)
        if ENEMY_SEG * 16 <= lin < ENEMY_SEG * 16 + 0x10000:
            enemy[lin - ENEMY_SEG * 16] = val
        elif DS * 16 <= lin < DS * 16 + 0x10000:
            after_ds[lin - DS * 16] = val
        elif WORK_SEG * 16 <= lin < WORK_SEG * 16 + 0x10000:
            work[lin - WORK_SEG * 16] = val

    def ds_word(off):
        lo = after_ds.get(off, ds.get(off, 0))
        hi = after_ds.get(off + 1, ds.get(off + 1, 0))
        return struct.unpack("<h", bytes([lo, hi]))[0]

    levels = int(res["final"]["ax"], 16)
    counts = [ds_word(MON_COUNT + 2 * lv) for lv in range(levels)]
    gcounts = [ds_word(GROUP_COUNT + 2 * lv) for lv in range(levels)]
    kinds = [[bytes(enemy[KINDS + (lv * 12 + k) * 0xD6:KINDS + (lv * 12 + k + 1) * 0xD6]).hex() for k in range(counts[lv])]
             for lv in range(levels)]
    groups = [[bytes(enemy[GROUPS + (lv * 10 + g) * 0x1E:GROUPS + (lv * 10 + g + 1) * 0x1E]).hex() for g in range(gcounts[lv])]
              for lv in range(levels)]
    holdrand = struct.unpack("<I", bytes(after_ds.get(HOLDRAND + k, ds[HOLDRAND + k]) for k in range(4)))[0]
    # the level images are stored zlib-compressed (base64) to keep the corpus small
    out_levels = {name: {"sha256": hashlib.sha256(d).hexdigest(), "zlib": base64.b64encode(zlib.compress(d, 9)).decode()}
                  for name, d in saved.items()}
    dump_dir = os.environ.get("CASTLE_WORK_DUMP")
    if dump_dir:
        tag = "%d_%02X_%d" % (sc.get("index", 0), sc["bf"], sc["seed"])
        Path(dump_dir).mkdir(parents=True, exist_ok=True)
        (Path(dump_dir) / (tag + "_plan.bin")).write_bytes(dumps.get("plan", b""))
        (Path(dump_dir) / (tag + "_final.bin")).write_bytes(bytes(work))
    return {"function": "gen_battlefield", "ax": "%04X" % levels, "changed": {}, "inputs": {
        **sc, "levels": levels, "start": ds_word(RES), "saved": out_levels,
        "terrain": bytes(enemy[TILE_DESC:TILE_DESC + 0xC4]).hex(), "kinds": kinds, "groups": groups,
        "holdrand": holdrand, "combatFlags": ds_word(COMBAT_FLAGS), "clearedAfter": ds_word(CLEARED),
        "lastLocationAfter": ds_word(LAST_LOC)}}


def main() -> None:
    exe_path, game_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 40
    workers = int(sys.argv[5]) if len(sys.argv) > 5 else 8
    scenarios = make_scenarios(count)
    for i, sc in enumerate(scenarios):
        sc["index"] = i
    with Pool(workers, initializer=init_worker, initargs=(exe_path, game_path)) as pool:
        cases = pool.map(run_scenario, scenarios, chunksize=1)
    froze = [c for c in cases if c["function"] == "gen_battlefield_freeze"]
    cases = [c for c in cases if c["function"] == "gen_battlefield"]
    print("scenarios the original never finishes (left out):", [(c["inputs"]["bf"], c["inputs"]["seed"]) for c in froze])
    exe = Path(exe_path).read_bytes()
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND, "cases": cases}
    Path(out).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(cases))


if __name__ == "__main__":
    main()
