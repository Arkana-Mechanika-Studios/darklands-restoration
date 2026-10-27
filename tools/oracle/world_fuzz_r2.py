"""world_fuzz_r1.py plus the aimed situations of round R2 (the rules leftovers of R029): members who went away (R029+0x2474), a full event table
(R029+0x2A32, 0x2B36, 0x2E74), long waits across months and years (R029+0x54D8).

usage: python -I -B world_fuzz_r2.py <states folder> <out folder> --functions R029+0x2474,... --states-list banks-loaded,... [world_fuzz.py's options]
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import world_fuzz as wf  # noqa: E402
import world_fuzz_r1 as r1  # noqa: E402

AWAY = {"R029+0x2474"}
FULL = {"R029+0x2A32", "R029+0x2B36", "R029+0x2E74", "R029+0x2D5C"}
CLOCK = {"R029+0x54D8"}


class R2(r1.R1):
    def away(self) -> list:
        rng = self.rng
        n = len(self.sav.chars)
        ops = []
        p = rng.choice([0.3, 0.5, 0.8])
        for k in range(n):
            status = 5 if rng.random() < p else rng.choice([0, 1, 1, 1, 2, 3])
            ops.append({"s": "char", "i": k, "o": 0x14, "hex": "%02x" % status})
        if rng.random() < 0.8:
            ops.append({"s": "ds", "o": r1.bc.HOLDRAND, "hex": rng.getrandbits(32).to_bytes(4, "little").hex()})
        return ops

    def full(self) -> list:
        """The event table filled up to `have` entries with events that match no query (a kind none of the functions look for), the last
        slots optionally of the kind the call asks for; the call then finds no free slot, or scans to the end."""
        rng = self.rng
        y = self.year()
        have = len(self.sav.events)
        target = rng.choice([300, 300, 299, 298])
        ops = []
        for k in range(have, target):
            ops.append({"s": "event+", "hex": r1.event_hex(y, rng.choice([0x3B, 0x3D, 0x47, 0x52, 0x60]), type_=rng.randrange(0, 5), owner=rng.choice([0xFFFE, 0, 1]),
                                                          loc=self.cur_loc(), value=rng.randrange(0, 5), flags=1, handler=0, tag=rng.randrange(0, 4))})
        if rng.random() < 0.5:
            # a matching-looking event in the last slot (the scan's last candidate)
            ops.append({"s": "event+", "hex": r1.event_hex(y, rng.choice(self.ekinds or [0x3B]), type_=rng.choice(self.etypes or [0]), owner=rng.choice([0xFFFE, 0xFFFF, 0]),
                                                          loc=rng.choice([self.cur_loc(), 0]), value=rng.randrange(0, 4), flags=1, handler=0, tag=rng.choice(self.etags or [0]),
                                                          d1=rng.randrange(3))})
        return ops

    def clock(self) -> list:
        rng = self.rng
        return [{"s": "head", "o": 0x6A, "hex": wf.w16(rng.choice([0, 1, 5, 10, 11, 11, 11])).hex()}, {"s": "head", "o": 0x6C, "hex": wf.w16(rng.choice([0, 15, 28, 29, 30])).hex()},
                {"s": "head", "o": 0x6E, "hex": wf.w16(rng.choice([0, 12, 23])).hex()}]

    def scenario(self) -> list:
        if self.coord in AWAY and self.rng.random() < 0.9:
            return self.away()
        if self.coord in FULL and self.rng.random() < 0.6:
            return self.full()
        if self.coord in CLOCK and self.rng.random() < 0.7:
            return self.clock()
        return super().scenario()

    def derive(self, args, ops):
        return super().derive(args, ops)


def main():
    pick = lambda *v: (lambda r, f: r.choice(v))  # noqa: E731
    wf.FUNCTIONS["R029+0x2474"] = []
    wf.FUNCTIONS["R029+0x2A32"] = [wf.kind_, wf.small_, pick(1, 2, 12, 24, 0x270F, 9999, 5000, 300)]
    wf.FUNCTIONS["R029+0x2B36"] = [wf.kind_, pick(1, 2, 12, 24, 0x270F, 9999, 100)]
    wf.FUNCTIONS["R029+0x54D8"] = [pick(1, 5, 24, 100, 500, 720, 1000, 2000, 5000, 9000, 20000)]
    r1.R1 = R2
    r1.main()


if __name__ == "__main__":
    main()
