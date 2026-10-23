"""Original-byte corpus for the city battlefield generator (R010 city_build).

usage: python -I build_city_corpus.py <DARKLAND.EXE> <game folder> <out city.json> [scenarios]

city_build runs from the unchanged EXE (sections 136 data, 4 loader, 10 generator) on the enemy
buffer prepared as battlefield_build prepares it (darkland.enm/.lst, monsters_clear, groups_clear,
the tile-description defaults). Its I/O is stubbed by hooks: the level buffer allocation, the
reads of icity.* (decoded here from the player's IMAPS.CAT) and the level saves (captured).
Stored: the request and, per scenario, the saved levels' hashes and changed bytes against the
source maps, the tile description, the kind and group records, counts and the random state.
"""
from __future__ import annotations

import hashlib
import json
import os
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import run  # noqa: E402
from build_enemy_corpus import Machine, MON_COUNT, GROUP_COUNT, KINDS, GROUPS, ENEMY_SEG, ITEM_SEG, DS, HOLDRAND  # noqa: E402

LEVEL_SEG = 0x5000
CUR_LOC, COMBAT_FLAGS, CLEARED, LAST_LOC = 0x907E, 0x9080, 0xDA34, 0xDB27
RES = 0xF000   # outside the emulated stack (writes inside it are not reported)
TILE_DESC = 0xF504
CITY_TYPES = [t for t in range(0x11, 0x27)] + [0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x56, 0x6A, 0x6B]
CALLSITES = {"1432f": "alloc", "14414": "free", "14c37": "read", "1585e": "read", "15ed3": "read", "16857": "read",
             "1714b": "save"}


def cat_members(path: Path) -> dict:
    d = path.read_bytes()
    n = struct.unpack_from("<H", d, 0)[0]
    out = {}
    for i in range(n):
        name = d[2 + 24 * i:14 + 24 * i].split(b"\0")[0].decode().upper()
        _, length, offset = struct.unpack_from("<III", d, 14 + 24 * i)
        out[name] = d[offset:offset + length]
    return out


def cat_decode(s: bytes) -> bytes:
    """The CAT member decompressor (the same algorithm as Darklands.Content CatMemberDecoder)."""
    out = bytearray()
    pos = [0]

    def word():
        v = struct.unpack_from("<H", s, pos[0])[0]
        pos[0] += 2
        return v

    def byte():
        v = s[pos[0]]
        pos[0] += 1
        return v

    st = {"c": word(), "b": 16}

    def bit():
        v = st["c"] & 1
        st["b"] -= 1
        if st["b"] == 0:
            st["c"] = word()
            st["b"] = 16
        else:
            st["c"] >>= 1
        return v

    while True:
        if bit():
            out.append(byte())
            continue
        if bit():
            w = word()
            dist = -struct.unpack("<h", struct.pack("<H", ((((w >> 11) | 0xE0) << 8) | (w & 0xFF)) & 0xFFFF))[0]
            length = (w >> 8) & 7
            if length == 0:
                e = byte()
                if e == 0:
                    return bytes(out)
                length = e + 1
            else:
                length += 2
        else:
            length = (2 if bit() else 0) + (1 if bit() else 0) + 2
            dist = 256 - byte()
        for _ in range(length):
            out.append(out[-dist])


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    game = Path(sys.argv[2])
    enm = (game / "DARKLAND.ENM").read_bytes()
    lst = (game / "DARKLAND.LST").read_bytes()
    imaps = cat_members(game / "IMAPS.CAT")
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 80
    r = random.Random(20261020 + int(os.environ.get("DL_SWEEP_SEED", 0)))
    base = Machine(exe, enm, lst)
    base.call("monsters_clear", [])
    base.call("groups_clear", [])
    base.enemy[TILE_DESC:TILE_DESC + 0xC4] = bytes(0xC4)
    for at in (0x0A, 0x24, 0x3D, 0x57, 0x70, 0x8A, 0xA4):
        base.enemy[TILE_DESC + at:TILE_DESC + at + 16] = b"\xff" * 16
    base.enemy[TILE_DESC + 0x23] = 2
    for at in (0x56, 0x89, 0xA3):
        base.enemy[TILE_DESC + at] = 8
    snapshot = (bytes(base.enemy), dict(base.ds))
    scenarios = []
    for n in range(count):
        bf = CITY_TYPES[n % len(CITY_TYPES)] if n < len(CITY_TYPES) * 2 else r.choice(CITY_TYPES)
        seed = r.randint(0, 0xFFFF)
        specs = [(r.choice([r.randint(0, 0x51), 0x12, 0x03, 0x22]), r.randint(0, 6), r.randint(0, 8)) for _ in range(3)]
        if r.random() < 0.2:
            specs[2] = (-1, 0, 0)
        cleared, last_loc, cur_loc = r.choice([0, 0, 1, 2, 3]), r.choice([-1, 5]), r.choice([5, 7])
        memory_enemy = bytearray(snapshot[0])
        ds = dict(snapshot[1])
        for off, data in ((HOLDRAND, struct.pack("<I", seed)), (CUR_LOC, struct.pack("<h", cur_loc)),
                          (COMBAT_FLAGS, b"\x00\x00"), (CLEARED, struct.pack("<H", cleared)),
                          (LAST_LOC, struct.pack("<h", last_loc)), (RES, b"\x00\x00")):
            for k, b in enumerate(data):
                ds[off + k] = b
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
        args = [bf, RES] + [v for s in specs for v in s]
        res = run(exe, {"unit": "city_build", "entry": {"segment": "1432", "offset": "0000"}, "sections": [136, 4, 10],
                        "ds": DS, "memory": memory, "args": ["%04X" % (a & 0xFFFF) for a in args], "follow": True,
                        "callsites": CALLSITES, "stub": list(set(CALLSITES.values())), "track_all": True,
                        "max_instructions": 20000000, "default_result": {"ax": 0, "dx": 0}},
                  hooks={"alloc": lambda uc, st: (LEVEL_SEG, 0), "free": lambda uc, st: (0, 0), "read": read_hook,
                         "save": save_hook})
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
                  for k in range(ds_word(MON_COUNT + 2 * lv))] for lv in range(2)]
        groups = [[bytes(enemy[GROUPS + (lv * 10 + g) * 0x1E:GROUPS + (lv * 10 + g + 1) * 0x1E]).hex()
                   for g in range(ds_word(GROUP_COUNT + 2 * lv))] for lv in range(2)]
        holdrand = struct.unpack("<I", bytes(after_ds.get(HOLDRAND + k, ds[HOLDRAND + k]) for k in range(4)))[0]
        out_levels = {}
        for name, data in saved.items():
            out_levels[name] = {"sha256": hashlib.sha256(data).hexdigest(), "data": data.hex()}
        scenarios.append({"function": "city_build", "ax": "%04X" % levels, "changed": {}, "inputs": {
            "bf": bf, "seed": seed, "specs": specs, "cleared": cleared, "lastLocation": last_loc, "location": cur_loc,
            "levels": levels, "start": ds_word(RES), "reads": reads, "saved": out_levels,
            "terrain": bytes(enemy[TILE_DESC:TILE_DESC + 0xC4]).hex(), "kinds": kinds, "groups": groups,
            "holdrand": holdrand, "combatFlags": ds_word(COMBAT_FLAGS), "clearedAfter": ds_word(CLEARED),
            "lastLocationAfter": ds_word(LAST_LOC)}})
    corpus = {"authority": "test_only_original_byte_execution",
              "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
              "dataSegment": "%04X" % DS, "scratchA": "%04X" % KINDS, "holdrand": "%04X" % HOLDRAND, "cases": scenarios}
    Path(sys.argv[3]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("scenarios", len(scenarios))


if __name__ == "__main__":
    main()
