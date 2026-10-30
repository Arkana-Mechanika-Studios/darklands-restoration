"""Original-byte corpus for the game rules card scripts use: location queries, standing
changes, improvement rolls, skill practice, attribute changes.

usage: python -I build_rules_corpus.py <DARKLAND.EXE> <out rules.json>

Each case runs the original function (and everything it calls) from the unchanged EXE with
generated party, location and city data, and records AX and the bytes it changed. Only
results are stored; no game bytes.
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
from run_unit import run  # noqa: E402

DS = 0x321A
HOLDRAND = 0x7B20
MONTH_NOW, LOCATION_NOW, PARTY_RANDOM_SEED, PARTY_MAP_COLUMN, PARTY_MAP_ROW = 0x00E4, 0x907E, 0x9C4A, 0xE3D0, 0xE3D2
CITY_RECORD, LEADER, PARTY_HEADCOUNT, REPORT, PARTY = 0x9785, 0x907B, 0xA67E, 0x26BC, 0x9C69
LOC_TABLE_SEG, LOC_TABLE_OFF = 0x2E38, 0x10BE
LOC_SEG, CITY_SEG = 0x6000, 0x7000
LOC_SIZE = 0x40                       # record spacing in the scratch segment (records are 0x3A)
FN = {
    "place-query": ("0E76", "1A8E", [136, 29]),
    "place-standing-adjust": ("0E76", "19D0", [136, 29]),
    "improvement-roll": ("0E76", "18A8", [136, 29]),
    "raise-attribute-by": ("0E76", "0A72", [136, 29]),
    "exercise-skill": ("1462", "0132", [136, 29, 39]),
    "place-assign": ("0E76", "1DFE", [136, 29]),
    "party-lore-rating": ("1462", "0470", [136, 29, 39]),
}
# party queries (R029 0e76): name -> (offset, number of skill/attribute arguments, attributes?)
QUERIES = {
    "member-top-skill": ("01C0", -1, False), "party-top-ability-low": ("021E", 0, False),
    "party-top-ability-high": ("026A", 0, False), "party-ability-low": ("1396", 1, False),
    "party-ability-low-two": ("13DE", 2, False), "party-ability-low-sum": ("1446", 2, False),
    "party-ability-top": ("14A4", 1, False), "party_max_skill_women": ("14EC", 1, False),
    "party-ability-top-two": ("153A", 2, False), "party-ability-top-sum": ("15A2", 2, False),
    "party-ability-mean": ("1600", 1, False), "party-stat-low": ("164A", 1, True),
    "party_min_attr_sum": ("1696", 2, True), "party-stat-top": ("16FE", 1, True),
    "party_max_attr_women": ("174A", 1, True), "party-stat-top-sum": ("179C", 2, True),
    "party-stat-mean": ("1800", 1, True), "party_max_attr_skill": ("184A", -2, True),
}
for _name, (_off, _n, _a) in QUERIES.items():
    FN[_name] = ("0E76", _off, [136, 29])


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def w(v: int) -> str:
    return struct.pack("<H", v & 0xFFFF).hex()


def ds(offset: int, data: str) -> dict:
    return {"offset": "%04X" % offset, "hex": data}


def location_record(r, rep):
    rec = bytearray(0x3A)
    struct.pack_into("<h", rec, 0x12, rep)
    rec[0x14] = r.choice([0, 1, 2, 4, 0x20, 0x80, 0x81, 0xFF])
    for k in range(3):
        rec[0x15 + k] = r.randint(0, 255)
    struct.pack_into("<hhhhh", rec, 0x18, r.choice([-1, 0, 3]), r.randint(-50, 50), r.randint(0, 99),
                     r.randint(0, 99), r.randint(0, 99))
    return rec


def locations(r, count, location_now):
    """Location records 0..count-1 (and the current one) with their far-pointer table."""
    recs, memory = {}, []
    for i in list(range(count)) + ([location_now] if location_now >= 0 else []):
        recs[i] = location_record(r, r.randint(-99, 99))
    for i, rec in recs.items():
        memory.append({"segment": "%04X" % LOC_TABLE_SEG, "offset": "%04X" % (LOC_TABLE_OFF + 4 * i),
                       "hex": struct.pack("<HH", i * LOC_SIZE, LOC_SEG).hex()})
        memory.append({"segment": "%04X" % LOC_SEG, "offset": "%04X" % (i * LOC_SIZE), "hex": rec.hex()})
    return {str(i): rec.hex() for i, rec in recs.items()}, memory


def party_records(r, size=5):
    """Five member records; slots from `size` on are empty, as party-headcount implies."""
    members, memory = [], []
    for i in range(5):
        rec = bytearray(0x80)
        rec[0] = r.choice([1, 1, 1, 0, 2, 5]) if i < size else 0
        rec[3] = r.randint(0, 1)
        for k in range(7):
            rec[0x50 + k] = r.randint(1, 99)
            rec[0x49 + k] = r.randint(1, 99)
        for k in range(19):
            rec[0x57 + k] = r.choice([0, 1, 50, 98, 99, r.randint(0, 99)])
        members.append(rec.hex())
        memory.append(ds(PARTY + 0x80 * i, rec.hex()))
    return members, memory


def cases(r):
    out = []
    for _ in range(400):
        location_now = r.choice([-1, 2, 150])
        what = r.randint(0, 44)
        if location_now == -1 and what in (39, 40, 41):
            continue
        recs, memory = locations(r, 4, location_now)
        city = bytes(r.randint(0, 255) for _ in range(0x30))
        month, seed, x, y = r.randint(0, 11), r.randint(0, 0x7FFF), r.randint(0, 330), r.randint(0, 930)
        memory += [{"segment": "%04X" % CITY_SEG, "offset": "0040", "hex": city.hex()},
                   ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now)),
                   ds(MONTH_NOW, w(month)), ds(PARTY_RANDOM_SEED, w(seed)), ds(PARTY_MAP_COLUMN, w(x)), ds(PARTY_MAP_ROW, w(y))]
        loc = r.randint(0, 3)
        out.append(("place-query", memory, [h16(loc), h16(what)],
                    {"loc": loc, "what": what, "curLoc": location_now, "locations": recs, "city": city.hex(),
                     "month": month, "seed": seed, "x": x, "y": y}))
    for _ in range(150):
        state = r.getrandbits(32)
        rep = r.randint(-99, 99)
        a, b = r.randint(-40, 40), r.randint(-80, 80)
        recs, memory = locations(r, 1, -1)
        rec = bytearray.fromhex(recs["0"])
        struct.pack_into("<h", rec, 0x12, rep)
        memory[-1]["hex"] = rec.hex()
        memory.append(ds(HOLDRAND, struct.pack("<I", state).hex()))
        out.append(("place-standing-adjust", memory, [h16(0), h16(a), h16(b)], {"state": state, "rep": rep, "a": a, "b": b}))
    for _ in range(150):
        state = r.getrandbits(32)
        v, lo = r.randint(-5, 105), r.randint(0, 6)
        hi = lo + r.randint(-1, 12)
        out.append(("improvement-roll", [ds(HOLDRAND, struct.pack("<I", state).hex())], [h16(v), h16(lo), h16(hi)],
                    {"state": state, "v": v, "lo": lo, "hi": hi}))
    for _ in range(200):
        members, memory = party_records(r)
        who, k, d = r.choice([-2, -1, 0, 1, 2, 3, 4]), r.randint(0, 6), r.randint(-120, 120)
        leader = r.randint(0, 4)
        memory += [ds(LEADER, "%02X" % leader)]
        out.append(("raise-attribute-by", memory, [h16(who), h16(k), h16(d)],
                    {"members": members, "leader": leader, "who": who, "k": k, "d": d}))
    for _ in range(250):
        size = r.randint(1, 5)
        members, memory = party_records(r, size)
        state = r.getrandbits(32)
        who, k = r.choice([-2, -1, 0, 1, 2, 3, 4]), r.randint(0, 18)
        mode, pct, lvl = r.choice([0, 1, 2]), r.choice([1, 3, 5, 10, 20, 30, 40]), r.randint(0, 4)
        leader = r.randint(0, size - 1)
        memory += [ds(LEADER, "%02X" % leader), ds(PARTY_HEADCOUNT, w(size)), ds(REPORT, w(0)),
                   ds(HOLDRAND, struct.pack("<I", state).hex())]
        out.append(("exercise-skill", memory, [h16(who), h16(k), h16(mode), h16(pct), h16(lvl)],
                    {"members": members, "leader": leader, "size": size, "state": state, "who": who, "k": k,
                     "mode": mode, "pct": pct, "lvl": lvl}))
    for _ in range(150):
        location_now = r.choice([2, 150])
        what = r.choice([0, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 32, 37, 37, 37, 9, 1])
        recs, memory = locations(r, 2, location_now)
        rec = bytearray.fromhex(recs["1"])
        struct.pack_into("<hhh", rec, 0x1C, *[r.choice([0, 0, 5]) for _ in range(3)])
        recs["1"] = rec.hex()
        memory = [m for m in memory if not (m.get("segment") == "%04X" % LOC_SEG and m["offset"] == "%04X" % LOC_SIZE)]
        memory.append({"segment": "%04X" % LOC_SEG, "offset": "%04X" % LOC_SIZE, "hex": rec.hex()})
        city = bytes(r.randint(0, 255) for _ in range(0x30))
        memory += [{"segment": "%04X" % CITY_SEG, "offset": "0040", "hex": city.hex()},
                   ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now))]
        value = r.choice([0, 1, 7, 0x40, 99, -5, 300])
        out.append(("place-assign", memory, [h16(1), h16(what), h16(value)],
                    {"loc": 1, "what": what, "value": value, "curLoc": location_now, "locations": recs, "city": city.hex()}))
    for name, (_, n, attributes) in QUERIES.items():
        for _ in range(40):
            members, memory = party_records(r)
            leader = r.randint(0, 4)
            memory += [ds(LEADER, "%02X" % leader)]
            args = ([r.choice([-1, 0, 1, 2, 3, 4])] if n == -1 else
                    [r.randint(0, 6), r.randint(0, 18)] if n == -2 else
                    [r.randint(0, 6 if attributes else 18) for _ in range(n)])
            out.append((name, memory, [h16(a) for a in args], {"members": members, "leader": leader, "args": args}))
    for _ in range(60):
        members, memory = party_records(r)
        out.append(("party-lore-rating", memory, [], {"members": members, "leader": 0}))
    return out + targeted(random.Random(20261017))


def party_with_status(r, statuses):
    """Five member records with the given status bytes (and random values otherwise)."""
    members, memory = party_records(r)
    for i, st in enumerate(statuses):
        rec = bytearray.fromhex(members[i])
        rec[0] = st
        members[i] = rec.hex()
        memory[i] = ds(PARTY + 0x80 * i, members[i])
    return members, memory


def targeted(r):
    """Cases for the branches the random cases above miss (docs/faithfulness/branches-missed.csv):
    every reputation band, location-now at the city/location/wilderness limits, the city-word comparisons
    and the uprising flag of place-query, place-assign's out-of-range selector, queries and the lore level
    with 0..5 present members, exercise-skill for the party with no slot in use."""
    out = []

    def query(loc, what, location_now, rep=None, flags=None, city=None, month=None):
        recs, memory = locations(r, 4, location_now)
        if rep is not None or flags is not None:
            for key in {str(loc), str(location_now)} & set(recs):
                rec = bytearray.fromhex(recs[key])
                if rep is not None:
                    struct.pack_into("<h", rec, 0x12, rep)
                if flags is not None:
                    rec[0x14] = flags
                recs[key] = rec.hex()
                at = int(key)
                memory = [m for m in memory if not (m.get("segment") == "%04X" % LOC_SEG and m["offset"] == "%04X" % (at * LOC_SIZE))]
                memory.append({"segment": "%04X" % LOC_SEG, "offset": "%04X" % (at * LOC_SIZE), "hex": rec.hex()})
        city = city if city is not None else bytes(r.randint(0, 255) for _ in range(0x30))
        month = r.randint(0, 11) if month is None else month
        seed, x, y = r.randint(0, 0x7FFF), r.randint(0, 330), r.randint(0, 930)
        memory += [{"segment": "%04X" % CITY_SEG, "offset": "0040", "hex": city.hex()},
                   ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now)),
                   ds(MONTH_NOW, w(month)), ds(PARTY_RANDOM_SEED, w(seed)), ds(PARTY_MAP_COLUMN, w(x)), ds(PARTY_MAP_ROW, w(y))]
        out.append(("place-query", memory, [h16(loc), h16(what)],
                    {"loc": loc, "what": what, "curLoc": location_now, "locations": recs, "city": city.hex(),
                     "month": month, "seed": seed, "x": x, "y": y}))

    for rep in (-99, -76, -75, -74, -41, -40, -39, -11, -10, -9, 10, 11, 49, 50, 51, 99):
        query(1, 1, 2, rep=rep)
    for cur in (-1, 0, 0x5B, 0x5C, 0x5D, 150):
        query(1, 2, cur)
    for cur in (2, 150):
        for flags in (0x00, 0x80, 0x81):
            for what in (39, 40, 41):
                query(1, what, cur, flags=flags)
    # what = 35: the city's day of the year (+0x6C) within 14 days of the month's first day, both sides
    first_day = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
    for month in (0, 1, 6, 11):
        for delta in (-16, -15, -14, -13, -1, 0, 1, 13, 14, 15, 16):
            city = bytearray(r.randint(0, 255) for _ in range(0x30))
            struct.pack_into("<h", city, 0x6C - 0x40, first_day[month] + delta)
            query(1, 35, 2, city=bytes(city), month=month)
    # place-assign: a selector past the switch's table
    for what in (42, 45, 100, 0x7FFF):
        location_now = 2
        recs, memory = locations(r, 2, location_now)
        city = bytes(r.randint(0, 255) for _ in range(0x30))
        memory += [{"segment": "%04X" % CITY_SEG, "offset": "0040", "hex": city.hex()},
                   ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now))]
        out.append(("place-assign", memory, [h16(1), h16(what), h16(5)],
                    {"loc": 1, "what": what, "value": 5, "curLoc": location_now, "locations": recs, "city": city.hex()}))
    # the party queries and the lore level with 0..5 present members
    patterns = [[0] * 5, [1, 0, 0, 0, 0], [0, 0, 0, 0, 2], [1, 1, 0, 0, 0], [0, 5, 0, 2, 0], [1, 1, 1, 0, 0],
                [1, 2, 0, 5, 1], [1, 1, 1, 1, 1], [5, 5, 5, 5, 5]]
    for statuses in patterns:
        members, memory = party_with_status(r, statuses)
        out.append(("party-lore-rating", memory, [], {"members": members, "leader": 0}))
        for name, (_, n, attributes) in QUERIES.items():
            leader = r.randint(0, 4)
            mem = memory + [ds(LEADER, "%02X" % leader)]
            args = ([r.choice([-1, 0, 1, 2, 3, 4])] if n == -1 else
                    [r.randint(0, 6), r.randint(0, 18)] if n == -2 else
                    [r.randint(0, 6 if attributes else 18) for _ in range(n)])
            out.append((name, mem, [h16(a) for a in args], {"members": members, "leader": leader, "args": args}))
    # exercise-skill for the whole party while no slot is in use (party-headcount 0) and with one slot
    for size in (0, 1):
        members, memory = party_records(r, size)
        state = r.getrandbits(32)
        memory += [ds(LEADER, "00"), ds(PARTY_HEADCOUNT, w(size)), ds(REPORT, w(0)), ds(HOLDRAND, struct.pack("<I", state).hex())]
        out.append(("exercise-skill", memory, [h16(-2), h16(3), h16(1), h16(20), h16(2)],
                    {"members": members, "leader": 0, "size": size, "state": state, "who": -2, "k": 3,
                     "mode": 1, "pct": 20, "lvl": 2}))
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261018)
    result, images = [], None
    for name, memory, args, inputs in cases(r):
        seg, off, sections = FN[name]
        res = run(exe, {"unit": name, "entry": {"segment": seg, "offset": off}, "sections": sections, "ds": DS,
                        "memory": memory, "args": args, "follow": True, "track_all": True})
        images = res["loadedImages"]
        result.append({"function": name, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                       "changed": {c["linear"]: c["after"] for c in res["memChanged"]}})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % PARTY, "holdrand": "%04X" % HOLDRAND,
        "locationSegment": "%04X" % LOC_SEG, "locationSpacing": "%04X" % LOC_SIZE,
        "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
