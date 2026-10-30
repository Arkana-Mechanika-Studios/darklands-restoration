"""Original-byte corpus for the barter rules: the goods' quality, buy and sell prices and the
stock test (overlay R032 = RTLink record 32, linked at 1462).

usage: python -I build_trading_corpus.py <DARKLAND.EXE> <out trading.json>

Each case runs the original function (and everything it calls) from the unchanged EXE with a
generated item, location, city record and party, and records AX, the random state and, for
the stock test, the stack word it reads uninitialised. Only results are stored; no game bytes.
"""
from __future__ import annotations

import hashlib
import json
import random
import struct
import sys
from pathlib import Path

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import Rtlink, run  # noqa: E402

DS = 0x321A
HOLDRAND = 0x7B20
LOCATION_NOW, PARTY_RANDOM_SEED, CITY_RECORD, LEADER, PARTY = 0x907E, 0x9C4A, 0x9785, 0x907B, 0x9C69
FAME = 0x9082                      # party-read(4) (R029 0E76:1326)
SHOP_QUALITY, SHOP_CATEGORIES = 0x8834, 0x8868
ITEM_TABLE_SEGMENT_SLOT, ITEM_TABLE = 0x7E6A, 0x0890
LOC_TABLE_SEG, LOC_TABLE_OFF = 0x2E38, 0x10BE
LOC_SEG, CITY_SEG, ITEM_SEG = 0x6000, 0x7000, 0x6800
STALE_SLOT = 0xFFE6                # [bp-2] of shop-stocks-item under run_unit's stack (SS = DS)
SECTIONS = [136, 29, 32]
FN = {"item-grade": "0EFA", "price-to-buy": "192E", "price-to-sell": "1B26", "shop-stocks-item": "1D18"}
CODE = 7                           # the item code the cases use


def w(v: int) -> str:
    return struct.pack("<H", v & 0xFFFF).hex()


def ds(offset: int, data: str) -> dict:
    return {"offset": "%04X" % offset, "hex": data}


def item_table_segment(exe: bytes) -> int:
    rt = Rtlink(exe)
    s = rt.sections[136]
    img = exe[s["file_offset"]:s["file_offset"] + s["length"]]
    at = DS * 16 + ITEM_TABLE_SEGMENT_SLOT - s["load_seg"] * 16
    return struct.unpack_from("<H", img, at)[0]


def case(r: random.Random, table_seg: int, function: str, force: dict | None = None) -> tuple[list, list, dict]:
    location_now = r.choice([-1, 5, 5, 150])
    flags = r.choice([0, 0, 1, 2, 8, 8, 9, 0x40, 0x41])
    rep = r.choice([r.randint(-99, 99), -20, -1, 0, 50])
    item = {
        "type": r.choice([0x17, 0x19, 0x40, 0x42] + [3, 0x26, 0x60, 9, 0x11] * 4),
        "categories": r.choice([0x400, 0x10000, 0x401] + [0x1, 0x6, 0x2, 0x404] * 3),
        "tradeFlags": r.choice([0] * 7 + [0x80]),
        "weight": r.randint(0, 30),
        "quality": r.randint(-20, 99),
        "rarity": r.randint(-2, 14),
        "value": r.choice([r.randint(-300, 0), r.randint(1, 3000), r.randint(1, 3000), r.randint(1, 3000), r.randint(0, 32000), 1, 0]),
    }
    inputs = {
        "curLoc": location_now, "flags": flags, "rep": rep, "item": item,
        "citySize": r.randint(0, 10), "charisma": r.randint(1, 60), "leader": r.randint(0, 4),
        "fame": r.choice([0, 10, 200, 327, 328, 400, 700]), "shopQuality": r.randint(-5, 60),
        "categories": r.choice([0xFFFFFFFF, 0xFFFFFFFF, 0x400, 0x1, 0x6, 0x2, 0]), "state": r.getrandbits(32),
        "quality": r.randint(0, 120), "slot": r.choice([0x2000, 1, 5, 9, -1, 0x5B, 0x5C, -5, 0]),
    }
    if force:
        item.update(force.get("item", {}))
        inputs.update({k: v for k, v in force.items() if k != "item"})
        location_now, flags, rep = inputs["curLoc"], inputs["flags"], inputs["rep"]
    rec = bytearray(0x2E)
    struct.pack_into("<HIBbbbxxxxh", rec, 0x1E, item["type"], item["categories"], item["tradeFlags"],
                     item["weight"], item["quality"], item["rarity"], item["value"])
    loc_rec = bytearray(0x3A)
    struct.pack_into("<h", loc_rec, 0x12, rep)
    loc_rec[0x14] = flags
    city = bytearray(0x60)
    struct.pack_into("<h", city, 0x40, inputs["citySize"])
    party = bytearray(0x80 * 5)
    for m in range(5):
        party[0x80 * m] = 1
        party[0x80 * m + 0x49 + 5] = inputs["charisma"] if m == inputs["leader"] else r.randint(1, 60)
    loc = max(location_now, 0)
    memory = [
        {"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": rec.hex()},
        {"segment": "%04X" % table_seg, "offset": "%04X" % (ITEM_TABLE + 4 * CODE), "hex": struct.pack("<HH", 0, ITEM_SEG).hex()},
        {"segment": "%04X" % LOC_TABLE_SEG, "offset": "%04X" % (LOC_TABLE_OFF + 4 * loc), "hex": struct.pack("<HH", 0, LOC_SEG).hex()},
        {"segment": "%04X" % LOC_SEG, "offset": "0000", "hex": loc_rec.hex()},
        {"segment": "%04X" % CITY_SEG, "offset": "0000", "hex": city.hex()},
        ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now)), ds(LEADER, "%02X" % inputs["leader"]),
        ds(PARTY, party.hex()), ds(FAME, w(inputs["fame"])), ds(SHOP_QUALITY, w(inputs["shopQuality"])),
        ds(SHOP_CATEGORIES, struct.pack("<I", inputs["categories"]).hex()),
        ds(HOLDRAND, struct.pack("<I", inputs["state"]).hex()), ds(STALE_SLOT, w(inputs["slot"])),
    ]
    args = ["%04X" % CODE] + (["%04X" % inputs["quality"]] if function == "price-to-sell" else [])
    return memory, args, inputs


STOCK_COUNT, STOCK_CODES, BARTERER, OVERLAY_SEG = 0x8850, 0x86A2, 0x884E, 0x1462


def stub_sites(exe: bytes) -> dict:
    """The calls of trade-screen-paint (1462:078E..0B30) that leave the rules: graphics driver
    thunks (047C), the picture loader (0617) and time() (06A1:23A8). They are skipped, but write
    their return address below SP as the real calls do (the stock test reads that stack word)."""
    import capstone
    rt = Rtlink(exe)
    s = rt.sections[32]
    img = exe[s["file_offset"]:s["file_offset"] + s["length"]]
    sites = {}
    for ins in capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16).disasm(img[0x078E:0x0B30], 0x078E):
        if ins.bytes[0] == 0x9A:
            off, seg = struct.unpack_from("<HH", ins.bytes, 1)
            if seg in (0x047C, 0x0617) or (seg, off) == (0x06A1, 0x23A8):
                sites["%05x" % (OVERLAY_SEG * 16 + ins.address)] = "outside"
    return sites


def stock_case(r: random.Random, table_seg: int) -> tuple[list, dict]:
    """A whole stock listing: 200 generated items, a place, a city record and a party."""
    location_now = r.choice([-1, 5, 30, 150])
    flags = r.choice([0, 0, 2, 8, 0x41])
    inputs = {"curLoc": location_now, "flags": flags, "rep": r.randint(-99, 99), "citySize": r.randint(1, 9),
              "citySeed": r.randint(0, 91), "partySeed": r.randint(0, 0x7FFF), "charisma": r.randint(1, 40),
              "fame": r.randint(0, 300), "shopQuality": r.randint(5, 50),
              "categories": r.choice([0xFFFFFFFF, 0x6, 0x1, 0x400]), "state": r.getrandbits(32), "items": []}
    for k in range(200):
        item = {"type": r.choice([0x40, 0x17] + [3, 0x26, 9] * 6), "categories": r.choice([0x1, 0x2, 0x4, 0x400, 0x10]),
                "tradeFlags": r.choice([0] * 9 + [0x80]), "weight": r.randint(0, 20), "quality": r.randint(5, 45),
                "rarity": r.randint(0, 12), "value": r.randint(1, 900)}
        inputs["items"].append(item)
    return stock_memory(table_seg, inputs), inputs


def stock_memory(table_seg: int, inputs: dict) -> list:
    """The seeded state of a stock case from its inputs."""
    location_now, flags = inputs["curLoc"], inputs["flags"]
    memory = []
    records = bytearray()
    for item in inputs["items"]:
        rec = bytearray(0x2E)
        struct.pack_into("<HIBbbbxxxxh", rec, 0x1E, item["type"], item["categories"], item["tradeFlags"],
                         item["weight"], item["quality"], item["rarity"], item["value"])
        records += rec
    memory.append({"segment": "%04X" % ITEM_SEG, "offset": "0000", "hex": records.hex()})
    memory.append({"segment": "%04X" % table_seg, "offset": "%04X" % ITEM_TABLE,
                   "hex": b"".join(struct.pack("<HH", 0x2E * k, ITEM_SEG) for k in range(200)).hex()})
    loc_rec = bytearray(0x3A)
    struct.pack_into("<h", loc_rec, 0x12, inputs["rep"])
    loc_rec[0x14] = flags
    city = bytearray(0x60)
    struct.pack_into("<h", city, 0x40, inputs["citySize"])
    struct.pack_into("<h", city, 0x56, inputs["citySeed"])
    party = bytearray(0x80 * 5)
    for m in range(5):
        party[0x80 * m] = 1
        party[0x80 * m + 0x2A] = ord("A")        # a one-letter name; no items (nitems +0x6A = 0)
        party[0x80 * m + 0x49 + 5] = inputs["charisma"]
    memory += [
        {"segment": "%04X" % LOC_TABLE_SEG, "offset": "%04X" % (LOC_TABLE_OFF + 4 * max(location_now, 0)), "hex": struct.pack("<HH", 0, LOC_SEG).hex()},
        {"segment": "%04X" % LOC_SEG, "offset": "0000", "hex": loc_rec.hex()},
        {"segment": "%04X" % CITY_SEG, "offset": "0000", "hex": city.hex()},
        ds(CITY_RECORD, struct.pack("<HH", 0, CITY_SEG).hex()), ds(LOCATION_NOW, w(location_now)), ds(LEADER, "00"), ds(BARTERER, w(0)),
        ds(PARTY, party.hex()), ds(FAME, w(inputs["fame"])), ds(SHOP_QUALITY, w(inputs["shopQuality"])),
        ds(PARTY_RANDOM_SEED, w(inputs["partySeed"])), ds(SHOP_CATEGORIES, struct.pack("<I", inputs["categories"]).hex()),
        ds(HOLDRAND, struct.pack("<I", inputs["state"]).hex()),
    ]
    return memory


READ_PLAIN = [{"segment": "%04X" % DS, "offset": "%04X" % HOLDRAND, "length": 4},
              {"segment": "%04X" % DS, "offset": "%04X" % STALE_SLOT, "length": 2}]
READ_STOCK = [{"segment": "%04X" % DS, "offset": "%04X" % HOLDRAND, "length": 4},
              {"segment": "%04X" % DS, "offset": "%04X" % STOCK_COUNT, "length": 2},
              {"segment": "%04X" % DS, "offset": "%04X" % STOCK_CODES, "length": 400}]


def plain_spec(function: str, memory: list, args: list) -> dict:
    return {"unit": function, "entry": {"segment": "1462", "offset": FN[function]}, "sections": SECTIONS,
            "ds": DS, "memory": memory, "args": args, "follow": True, "read": READ_PLAIN}


def stock_spec(memory: list, sites: dict) -> dict:
    return {"unit": "trade-screen-paint", "entry": {"segment": "1462", "offset": "078E"}, "sections": SECTIONS,
            "ds": DS, "memory": memory + [{"segment": "6F00", "offset": "0000", "hex": "00"}],
            "args": ["0000", "0000", "6F00", "FFFF", "FFFF"],   # who 0, the shop name (an empty far string at 6F00:0000), the category long (set again below)
            "follow": True, "callsites": sites, "stub": ["outside"], "stub_return_words": True,
            "max_instructions": 3000000, "read": READ_STOCK}


def plain_record(function: str, inputs: dict, res: dict) -> dict:
    holdrand, slot = res["reads"]
    return {"function": function, "inputs": inputs, "ax": res["final"]["ax"],
            "holdrand": struct.unpack("<I", bytes.fromhex(holdrand))[0],
            "slot": struct.unpack("<h", bytes.fromhex(slot))[0], "changed": {}}


def stock_record(inputs: dict, res: dict) -> dict:
    holdrand, count, codes = res["reads"]
    n = struct.unpack("<h", bytes.fromhex(count))[0]
    return {"function": "stock", "inputs": inputs, "ax": res["final"]["ax"],
            "holdrand": struct.unpack("<I", bytes.fromhex(holdrand))[0],
            "codes": list(struct.unpack("<%dh" % n, bytes.fromhex(codes)[:2 * n])), "changed": {}}


def forced_cases() -> list:
    """The targeted cases (docs/faithfulness/branches-missed.csv): the item types item-grade fixes at 25,
    rare goods (rarity above 5) in a place, the goods a shop always has, a city size past 9."""
    forced = []
    for t in (0x40, 0x41, 0x42, 0x17, 0x18, 0x19, 3):
        forced += [("item-grade", {"item": {"type": t, "categories": 0x1}}), ("item-grade", {"item": {"type": 3, "categories": 0x400}})]
    for fn in ("price-to-buy", "price-to-sell"):
        for rarity in (5, 6, 9, 14):
            for location_now in (5, 150):
                forced.append((fn, {"curLoc": location_now, "flags": 0, "item": {"rarity": rarity, "type": 3, "categories": 0x1}}))
    for t in (0x40, 0x41, 0x42, 3):
        for size in (5, 9, 10, 12):
            forced.append(("shop-stocks-item", {"categories": 0xFFFFFFFF, "citySize": size, "curLoc": 5, "slot": 5,
                                             "item": {"type": t, "categories": 0x1, "tradeFlags": 0}}))
    return forced


def all_cases(table_seg: int):
    """Every case of the corpus in generation order: ("plain", function, memory, args, inputs) or ("stock", None, memory, None, inputs)."""
    r = random.Random(20261017)
    for function in FN:
        for _ in range(250):
            memory, args, inputs = case(r, table_seg, function)
            yield "plain", function, memory, args, inputs
    tr = random.Random(20261018)
    for function, force in forced_cases():
        memory, args, inputs = case(tr, table_seg, function, force)
        yield "plain", function, memory, args, inputs
    for _ in range(40):
        memory, inputs = stock_case(r, table_seg)
        yield "stock", None, memory, None, inputs


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    table_seg = item_table_segment(exe)
    result, images = [], None
    sites = stub_sites(exe)
    for kind, function, memory, args, inputs in all_cases(table_seg):
        if kind == "plain":
            res = run(exe, plain_spec(function, memory, args))
            images = res["loadedImages"]
            result.append(plain_record(function, inputs, res))
        else:
            res = run(exe, stock_spec(memory, sites))
            result.append(stock_record(inputs, res))
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % PARTY, "holdrand": "%04X" % HOLDRAND,
        "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
