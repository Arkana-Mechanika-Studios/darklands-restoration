"""Original-byte corpus for the special battlefield generators: the Templar fortress and the tombs
(overlay R011), the dragon cave (R012) and Baphomet's citadel (R013).

usage: python -I build_special_corpus.py <DARKLAND.EXE> <game folder> <out special.json> [workers] [--full]

Each generator runs from the unchanged EXE (sections 136 data, 4 loader, 11/12/13 generator) on the
enemy buffer prepared as battlefield_build prepares it (darkland.enm/.lst, monsters_clear,
groups_clear, the tile-description defaults). Its I/O is stubbed by hooks: the level buffer
allocation, the reads of ifortmon.*/imisctom.* (decoded here from the player's IMAPS.CAT) and of
the lc_* members of LCASTLE, the level saves (captured) and party_lore_level (a scripted lore
level). The static floor image the cave and tombs work in is zeroed before the call (the C#
starts from zero too; the original keeps the previous battle's bytes there).
Stored per scenario: the request, the saved levels (zlib + base64), the tile description, the kind
and group records of every level, counts, the random state, the combat flags and the cleared-level
memory afterwards. --full also stores the raw hex of the levels (large).
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

LEVEL_SEG = 0x5000
FLOOR_SEG = 0x20A5   # the static floor image (the data image segment 20A5, R136)
CUR_LOC, COMBAT_FLAGS, CLEARED, LAST_LOC = 0x907E, 0x9080, 0xDA34, 0xDB27
FORCE_MAP = 0xC100   # DS:C100: always -1 in the shipped game (the fortress start level override)
LOAD_BUF = 0xC103    # far pointer to the load buffer (the citadel loads its levels there)
COURT_PROGRESS = 0x160C   # DS:160C, R013's own data (the court's progress; 0 in the shipped game)
RES = 0xF000   # outside the emulated stack (writes inside it are not reported)
TILE_DESC = 0xF504

# The call instructions to replace by hooks, per generator (linear addresses 0x1432:offset, from the disassembly).
FORTRESS_SITES = {"1432d": "alloc", "14589": "free", "147e3": "read", "162c7": "save"}
TOMB_SITES = {"16311": "read", "163af": "save"}
CAVE_SITES = {"14373": "save", "14c23": "lore", "14c59": "lore"}
CITADEL_SITES = {"14326": "dsel"}
for _a in "14441 145ce 145f4 14681 1471c 1472f 147c3 1485e 14871 14905 149a0 149b3 14a47 14ae2 14af5 14b91 14c2c 14c3f 14cd3 14d6e " \
          "14d90 14e25 14ec0 14ed3 14f62 15001 15014".split():
    CITADEL_SITES[_a] = "read"
for _a in "145bb 14709 1484b 1498d 14acf 14c19 14d5b 14ead 14fee".split():
    CITADEL_SITES[_a] = "save"

KINDS_OF = {
    "fortress": dict(unit="templar_build", sections=[136, 4, 11], entry="0000", sites=FORTRESS_SITES, res=True),
    "tombs": dict(unit="tombs_build", sections=[136, 4, 11], entry="1FB2", sites=TOMB_SITES, res=False),
    "dragon": dict(unit="cave_build", sections=[136, 4, 12], entry="0000", sites=CAVE_SITES, res=False),
    "citadel": dict(unit="citadel_build", sections=[136, 4, 13], entry="0000", sites=CITADEL_SITES, res=True),
}
CITADEL_TYPES = [0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x6E]
CAVE_TYPES = [0x60, 0x64, 0x65]

_G: dict = {}


def init_worker(exe_path: str, game_path: str, full: bool = False) -> None:
    global FULL
    FULL = full
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
              lcastle=cat_members(game / "LCASTLE"), snapshot=(bytes(base.enemy), dict(base.ds)))


def spec_row(r: random.Random, special=None):
    """One enemy spec (type, quality, count); type -1 means none."""
    t = r.choice([r.randint(0, 0x51), r.randint(0, 0x51), 0x12, 0x03, 0x22, 0x28, 0x3F, 0x43])
    if special is not None:
        t = special
    return [t, r.randint(0, 6), r.randint(0, 8)]


def make_scenarios() -> list[dict]:
    r = random.Random(20261022)
    out = []

    def base(kind, bf, **extra):
        sc = {"kind": kind, "bf": bf, "seed": r.randint(0, 0xFFFF), "specs": [spec_row(r) for _ in range(3)],
              "cleared": 0, "lastLocation": 5, "location": 5, "forceMap": -1, "lore": r.randint(1, 10), "courtProgress": 0}
        sc.update(extra)
        out.append(sc)
        return sc

    # The Templar fortress: the cleared-room memory (same place) in many shapes, the two quality switches, the debugging override.
    for n in range(14):
        sc = base("fortress", 0x5E, cleared=[0, 0xFFFF, 0x0001, 0x8000, 0x5555, 0xAAAA][n] if n < 6 else r.randint(0, 0xFFFF))
        sc["specs"][1][1] = 1 if n % 2 == 0 else r.choice([0, 2, 3])
        sc["specs"][2][1] = 1 if n % 3 == 0 else r.choice([0, 2, 5])
        if n == 7:
            sc["lastLocation"] = -1   # a new place: the cleared rooms are forgotten
        if n in (9, 10, 11):
            sc["forceMap"] = [0, 15, 16][n - 9]
        if n == 12:
            sc["forceMap"] = -2
    # The tombs: the number of groups asked for from 0 to 8, some beyond the markers the maps have.
    for n in range(10):
        sc = base("tombs", 0x5F)
        sc["specs"][0][2] = n if n < 9 else 30
    # The dragon cave: every type, the dragon's enemy type in each position, all lore levels.
    for n in range(15):
        bf = CAVE_TYPES[n % 3]
        sc = base("dragon", bf, lore=n % 10 + 1)
        if n in (3, 4, 5, 6, 7, 8):
            sc["specs"][(n - 3) % 3][0] = [0x3F, 0x43][(n - 3) // 3 % 2]
        for s in sc["specs"]:
            if n < 3 or n > 8:
                s[0] = r.choice([0x00, 0x12, 0x2A])
    # Baphomet's citadel: every room (the stairs room both ways), the court's progress.
    for n in range(24):
        bf = CITADEL_TYPES[n % len(CITADEL_TYPES)]
        sc = base("citadel", bf)
        if bf == 0x5B:
            sc["specs"][0][0] = 0 if (n // len(CITADEL_TYPES)) % 2 == 0 else r.randint(1, 0x51)
        elif n % 4 == 1:
            sc["specs"][0][0] = r.choice([-1, 0, 5])
        if bf == 0x57 and n >= len(CITADEL_TYPES):
            sc["courtProgress"] = r.choice([0x0001, 0x0004, 0x0080, 0x0100, 0x0300, 0x0140, 0x2A15, 0x7F7F, 0x0100 | 0x40, 0x80])
    # More caves (the rubble thresholds and the lore thresholds are picked by single dice) and the stairs room both ways.
    for n in range(60):
        base("dragon", CAVE_TYPES[n % 3], lore=n // 3 % 10 + 1)
    for n, first in enumerate([0, 1, -1, 5, 6, 0x22, 0, -1]):
        sc = base("citadel", 0x5B)
        sc["specs"][0][0] = first
    for progress in (0x0001, 0x0002, 0x0010, 0x0040, 0x0080, 0x00C0, 0x0100, 0x0200, 0x0400, 0x1000, 0x4000, 0x8000, 0x0103, 0xFFFF):
        sc = base("citadel", 0x57)
        sc["courtProgress"] = progress
    return out


def run_scenario(sc: dict) -> dict:
    exe, lst = _G["exe"], _G["lst"]
    spec = KINDS_OF[sc["kind"]]
    memory_enemy = bytearray(_G["snapshot"][0])
    ds = dict(_G["snapshot"][1])
    words = [(HOLDRAND, struct.pack("<I", sc["seed"])), (CUR_LOC, struct.pack("<h", sc["location"])),
             (COMBAT_FLAGS, b"\x00\x00"), (CLEARED, struct.pack("<H", sc["cleared"])),
             (LAST_LOC, struct.pack("<h", sc["lastLocation"])), (FORCE_MAP, struct.pack("<h", sc["forceMap"])),
             (LOAD_BUF, struct.pack("<HH", 0, LEVEL_SEG)), (COURT_PROGRESS, struct.pack("<H", sc["courtProgress"])),
             (RES, b"\x00\x00")]
    for off, data in words:
        for k, b in enumerate(data):
            ds[off + k] = b
    archive = _G["lcastle"] if sc["kind"] == "citadel" else _G["imaps"]
    saved, reads = {}, []

    def read_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode().upper()
        reads.append(name)
        data = cat_decode(archive[name])
        if sc["kind"] != "citadel":
            data = data[:0x33FC]
        uc.mem_write(stack[2] * 16 + stack[1], data)
        return 0, 0

    def save_hook(uc, stack):
        name = bytes(uc.mem_read(DS * 16 + stack[0], 16)).split(b"\0")[0].decode()
        saved[name] = bytes(uc.mem_read(stack[2] * 16 + stack[1], stack[3]))
        return 0, 0

    memory = [{"segment": "%04X" % ENEMY_SEG, "offset": "0000", "hex": bytes(memory_enemy).hex()},
              {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": lst[:0x4000].hex()},
              {"segment": "%04X" % FLOOR_SEG, "offset": "0000", "hex": bytes(0x33FC).hex()}]
    memory += [{"offset": "%04X" % off, "hex": "%02X" % b} for off, b in sorted(ds.items())]
    flat = [v for s in sc["specs"] for v in s]
    args = [sc["bf"]] + ([RES] if spec["res"] else []) + flat
    res = run(exe, {"unit": spec["unit"], "entry": {"segment": "1432", "offset": spec["entry"]}, "sections": spec["sections"],
                    "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args], "follow": True,
                    "callsites": spec["sites"], "stub": list(set(spec["sites"].values())), "track_all": True,
                    "max_instructions": 120000000, "default_result": {"ax": 0, "dx": 0}},
              hooks={"alloc": lambda uc, st: (LEVEL_SEG, 0), "free": lambda uc, st: (0, 0), "read": read_hook,
                     "save": save_hook, "lore": lambda uc, st: (sc["lore"], 0), "dsel": lambda uc, st: (0, 0)})
    assert int(res["stack"]["min_sp"], 16) - 0x20 > RES + 2, "the stack reached the result word"
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
              for k in range(ds_word(MON_COUNT + 2 * lv))] for lv in range(levels)]
    groups = [[bytes(enemy[GROUPS + (lv * 10 + g) * 0x1E:GROUPS + (lv * 10 + g + 1) * 0x1E]).hex()
               for g in range(ds_word(GROUP_COUNT + 2 * lv))] for lv in range(levels)]
    holdrand = struct.unpack("<I", bytes(after_ds.get(HOLDRAND + k, ds[HOLDRAND + k]) for k in range(4)))[0]
    out_levels = {}
    for name, data in saved.items():
        entry = {"sha256": hashlib.sha256(data).hexdigest(), "zdata": base64.b64encode(zlib.compress(data, 9)).decode()}
        if FULL:
            entry["data"] = data.hex()
        out_levels[name] = entry
    return {"function": sc["kind"], "ax": "%04X" % levels, "changed": {}, "inputs": {
        **sc, "levels": levels, "start": ds_word(RES) if spec["res"] else 0, "reads": reads, "saved": out_levels,
        "terrain": bytes(enemy[TILE_DESC:TILE_DESC + 0xC4]).hex(), "kinds": kinds, "groups": groups,
        "holdrand": holdrand, "combatFlags": ds_word(COMBAT_FLAGS), "clearedAfter": ds_word(CLEARED),
        "lastLocationAfter": ds_word(LAST_LOC)}}


FULL = False


def main() -> None:
    global FULL
    argv = [a for a in sys.argv[1:] if a != "--full"]
    FULL = len(argv) != len(sys.argv) - 1
    exe_path, game_path, out = argv[0], argv[1], argv[2]
    workers = int(argv[3]) if len(argv) > 3 else 8
    scenarios = make_scenarios()
    with Pool(workers, initializer=init_worker, initargs=(exe_path, game_path, FULL)) as pool:
        cases = pool.map(run_scenario, scenarios, chunksize=1)
    exe = Path(exe_path).read_bytes()
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND, "cases": cases}
    Path(out).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(cases))


if __name__ == "__main__":
    main()
