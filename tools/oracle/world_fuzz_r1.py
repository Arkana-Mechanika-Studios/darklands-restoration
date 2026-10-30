"""world_fuzz.py with aimed scenarios for the world events and rules (round R1): the same driver, states and output, a Fuzzer subclass whose edits
also build the situations the never-run outcomes of R039 (city politics, uprisings, quests) and R029 (the daily politics, the expiry handlers)
need, and a random-generator edit for every function.

usage: python -I -B world_fuzz_r1.py <states folder> <out folder> --functions R039+0x0BF6,... --states-list banks-loaded,... [world_fuzz.py's options]

Aimed edits (all of them saved-game bytes the original loads, or the generator's state DS:7B20, so every input is a state the game can write):
  random  the generator's state (the draws of a function that picks by chance: quests, saints, items)
  politics  the facts of the city's two sides (group 2, types 1 and 2: which of the 15 bits belong to which side), the leaning (type 0),
            the market weekday, the flags facts 6 and 7, the pacts (types 0x24 / 0x25), the date's day (the seventh-day test)
  handler  an expiry event of the handler the function is (alchemy, daily politics, quests here, boosts, travel blessing ...) of fitting data,
           whose slot is the function's argument
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import world_fuzz as wf  # noqa: E402
import battle_calls as bc  # noqa: E402

ALL_BITS = [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x80, 0x40, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000]
STRENGTH_BITS = [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x80, 0x40, 0x200, 0x400, 0x1000]
POLITICS = {"R039+0x0BF6", "R039+0x0D46", "R039+0x0EBE", "R039+0x0A34", "R039+0x0A7E", "R029+0x4180"}
# expiry handlers of the event table (event-expiry-handlers): coordinate -> handler number
HANDLERS = {"R029+0x3FB2": 1, "R029+0x4180": 2, "R029+0x4378": 3, "R029+0x4436": 4, "R029+0x4442": 5, "R029+0x448A": 6, "R029+0x450A": 7,
            "R029+0x4534": 8, "R029+0x4626": 9}
GROUP = 2
KIND_FACT, KIND_QUEST = 0x1C, 0x08


def w(v: int) -> bytes:
    return struct.pack("<H", v & 0xFFFF)


def event_hex(year: int, kind: int, type_: int = 0, owner: int = 0xFFFE, loc: int = 0, value: int = 0, extra: int = 0, flags: int = 0, handler: int = 0,
              tag: int = 0, d1: int = 0, d2: int = 0, d3: int = 0) -> str:
    rec = bytearray(wf.EVENT)
    for o, v in ((0x00, type_), (0x22, kind), (0x1A, owner), (0x1C, loc), (0x1E, value), (0x20, extra), (0x24, flags), (0x26, handler), (0x28, tag),
                 (0x2A, d1), (0x2C, d2), (0x2E, d3), (0x18, year + 1), (0x16, 0), (0x14, 1), (0x12, 0), (0x08, year - 1), (0x10, year - 1)):
        struct.pack_into("<H", rec, o, v & 0xFFFF)
    return rec.hex()


class R1(wf.Fuzzer):
    """Fuzzer plus the aimed edits above."""

    def year(self) -> int:
        return struct.unpack_from("<H", self.sav.head, 0x68)[0]

    # ---- helpers
    def fact_op(self, type_: int, value: int, loc: int | None = None, kind: int = KIND_FACT, tag: int = GROUP):
        """An edit that gives the fact (kind, type, loc, tag) the value: the state's own event when it has one, else a new one."""
        loc = self.location_now() if loc is None else loc
        for i, e in enumerate(self.sav.events):
            if (struct.unpack_from("<H", e, 0x22)[0], struct.unpack_from("<H", e, 0x00)[0], struct.unpack_from("<H", e, 0x1C)[0],
                    struct.unpack_from("<H", e, 0x28)[0]) == (kind, type_, loc, tag):
                return {"s": "event", "i": i, "o": 0x1E, "hex": w(value).hex()}
        return {"s": "event+", "hex": event_hex(self.year(), kind, type_, 0xFFFE, loc, value, 0, 0, 0, tag)}

    def bits(self, rng) -> int:
        density = rng.choice([0.1, 0.3, 0.5, 0.7, 0.9])
        m = 0
        for b in ALL_BITS:
            if rng.random() < density:
                m |= b
        return m

    # ---- aimed scenarios
    def politics(self) -> list:
        rng = self.rng
        a = self.bits(rng)
        mode = rng.random()
        if mode < 0.6:
            b = self.bits(rng) & ~a                              # disjoint, as the game keeps them
        elif mode < 0.8:
            b = a                                                # both: the minus side wins the test
        else:
            b = self.bits(rng)
        if rng.random() < 0.15:                                  # a single bit: each bit's own test
            a = rng.choice(STRENGTH_BITS)
            b = rng.choice([0, 0, rng.choice(STRENGTH_BITS)])
        if rng.random() < 0.1:
            a, b = 0, 0
        ops = [self.fact_op(1, a), self.fact_op(2, b)]
        for type_, dom in ((0, [0xFFFF, 1, 0]), (6, [0, 1]), (7, [0, 1]), (0x24, [0, 1]), (0x25, [0, 1])):
            if rng.random() < 0.7:
                ops.append(self.fact_op(type_, rng.choice(dom)))
        if rng.random() < 0.6:
            ops.append(self.fact_op(0x17, rng.choice([0, 1 << rng.randrange(16), 1 << rng.randrange(16)])))
        if rng.random() < 0.5:
            ops.append({"s": "head", "o": 0x6C, "hex": w(rng.randrange(31)).hex()})
        if self.coord == "R029+0x4180" or rng.random() < 0.2:
            ops.append({"s": "event+", "hex": event_hex(self.year(), KIND_FACT, 0xC, 0xFFFE, self.location_now(), 0x63, 0, 1, 2, GROUP)})
        return ops

    def handler(self) -> list:
        rng = self.rng
        n = HANDLERS[self.coord]
        y = self.year()
        loc = self.location_now()
        member = rng.randrange(max(1, len(self.sav.chars)))
        if n == 1:       # a brew is done: owner, formula, chance, potions, the mishap's sort, the formula's difficulty
            rec = event_hex(y, 0x1D, type_=rng.randrange(0, 33), owner=member, loc=loc, value=rng.choice([0, 1, 10, 50, 90, 99, 100, 120]),
                            extra=rng.choice([1, 2, 3, 5]), flags=1, handler=n, d1=rng.randrange(4), d2=rng.randrange(3))
            return [{"s": "event+", "hex": rec}] + ([{"s": "head", "o": 0x7C, "hex": w(0xFFFF).hex()}] if rng.random() < 0.2 else [])
        if n == 3:       # the quests at the event's location are resolved: quest events (tag = the quest's group) at that place
            ops = [{"s": "event+", "hex": event_hex(y, KIND_FACT, type_=0xC, loc=loc, value=0x63, flags=1, handler=n, tag=GROUP)}]
            for _ in range(rng.choice([0, 1, 2, 3])):
                ops.append({"s": "event+", "hex": event_hex(y, KIND_QUEST, type_=rng.randrange(0, 12), owner=rng.choice([0xFFFE, loc, 0]), loc=loc,
                                                            value=rng.randrange(3), extra=rng.randrange(3), tag=rng.randrange(1, 14), d1=rng.randrange(5), d2=rng.randrange(5))})
            return ops
        if n == 6:       # a boost wears off
            kind = rng.choice([0x48, 0x49, 0x4A, 0x4B])
            return [{"s": "event+", "hex": event_hex(y, kind, type_=rng.randrange(0, 19) if kind != 0x48 else rng.randrange(0, 7),
                                                      owner=rng.choice([member, member, 0xFFFE, 0xFFFF, 0]), loc=loc,
                                                      value=rng.choice([1, 2, 5, 0xFFFB, 0xFFF6, 20]), flags=1, handler=n)}]
        if n == 9:       # the travel blessing: DS:D843 (the terrain)
            return [{"s": "ds", "o": 0xD843, "hex": w(rng.choice([0, 1, 2, 3, 5])).hex()}, {"s": "event+", "hex": event_hex(y, 0x4B, flags=1, handler=n)}]
        return [{"s": "event+", "hex": event_hex(y, 0x3B if n == 4 else 0x43, type_=rng.randrange(0, 5), owner=member, loc=loc, flags=1, handler=n)}]

    # ---- hooks of the driver
    def scenario(self) -> list:
        if self.coord in POLITICS and self.rng.random() < 0.8:
            return self.politics()
        if self.coord in HANDLERS and self.rng.random() < 0.8:
            return self.handler()
        return super().scenario()

    def gen_op(self) -> dict:
        r = self.rng.random()
        if r < 0.3:
            return {"s": "ds", "o": bc.HOLDRAND, "hex": self.rng.getrandbits(32).to_bytes(4, "little").hex()}
        if r < 0.5 and self.coord in POLITICS:
            return self.rng.choice(self.politics())
        return super().gen_op()

    def derive(self, args, ops):
        n = HANDLERS.get(self.coord)
        if n is None:
            return super().derive(args, ops)
        events = wf.Save(self.edited(ops)).events
        slots = [i for i, e in enumerate(events) if struct.unpack_from("<H", e, 0x26)[0] == n and struct.unpack_from("<H", e, 0x24)[0] & 1]
        return [slots[-1] if slots else (args[0] % max(1, len(events)))]


def main():
    bit = lambda r, f: r.choice([1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000, r.randrange(0x10000)])  # noqa: E731
    side = lambda r, f: r.choice([0xFFFF, 0, 1])  # noqa: E731
    wf.FUNCTIONS.update({"R039+0x0A34": [bit], "R039+0x0A7E": [bit, side], "R039+0x0EBE": [side, side, side]})
    who = lambda r, f: r.choice([0xFFFE, 0xFFFF, 0, 1, 2, 3, 4])  # noqa: E731
    wf.FUNCTIONS["R029+0x4D24"] = [who, lambda r, f: r.randrange(7), lambda r, f: r.choice([1, 2, 5, 10, 0xFFF6]), lambda r, f: r.choice([1, 6, 12, 24, 48])]
    pick = lambda *v: (lambda r, f: r.choice(v))  # noqa: E731
    whom = lambda r, f: r.choice([0xFFFE, 0xFFFF, 0, 1, 2, 3, 4, 5, 6, 7, 8])  # noqa: E731    5.. : a stored list
    for coord in ("R029+0x0F54", "R029+0x0DD8"):
        wf.FUNCTIONS[coord] = [whom] + wf.FUNCTIONS[coord][1:]
    wf.FUNCTIONS["R029+0x0C76"] = [whom, wf.FUNCTIONS["R029+0x0C76"][1]]
    wf.FUNCTIONS.update({
        "R039+0x00BA": [], "R039+0x0102": [], "R039+0x15C2": [], "R039+0x11FC": [], "R039+0x1B0C": [],
        # (value, <unused>, low): the function reads its first and third words; every caller passes value > low (limit-to-range(low, value, roll-below(value - low)))
        "R039+0x0000": [lambda r, f: r.choice([20, 50, 100, 500, 1000, 0x23, 0x0E, 8, 0x0F]), lambda r, f: 0, lambda r, f: r.choice([0, 1, 2, 4, 5, 10, 0x14, 6])],
        "R039+0x1D2C": [pick(-80, -11, -10, -9, 0, 9, 10, 49, 50, 100, 0xFFB0), pick(0, 1, 5, 50, 200, 1000)],
        "R039+0x1E06": [pick(0, 1, 2, 3, 4, 5, 0xFFFF)],
        "R039+0x10B6": [who, lambda r, f: r.randrange(f.places), lambda r, f: r.randrange(0, 8), pick(0, 1, 5, 50, 100), pick(0, 1, 2, 3, 4, 5, 6), pick(0, 1, 5)],       # 6 words; the fact events' location is DS:E89B (R039+0x118D..0x11EA), set as a ds edit below and recorded in the job
        "R039+0x026A": [who, pick(0, 1, 5, 10, 20), pick(0, 1, 5, 10, 20), pick(0, 1, 5, 10, 40, 100)],
    })
    wf.IN_A_CITY.update({"R039+0x00BA", "R039+0x0102", "R039+0x0000", "R039+0x1D2C"})
    for coord in HANDLERS:
        wf.FUNCTIONS[coord] = [lambda r, f: 0]
    wf.IN_A_CITY.update({"R029+0x4180", "R039+0x0A34", "R039+0x0A7E", "R039+0x0BF6", "R039+0x0D46"})
    wf.CTX["R029+0x4626"] = [0xD843]
    wf.CTX["R039+0x10B6"] = [0xE89B]
    wf.DS_EDITS["R039+0x10B6"] = [(0xE89B, 2, [0, 1, 5, 50, 88, 120, 300])]
    wf.Fuzzer = R1
    wf.main()


if __name__ == "__main__":
    main()
