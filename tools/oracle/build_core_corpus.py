"""Original-byte corpus for the Core value rules: money, calendar, random numbers.

usage: python -I build_core_corpus.py <DARKLAND.EXE> <out corpus.json>

Each case runs one original function (and its callees) from the unchanged EXE with
generated arguments, recording the bytes it changes and AX/DX. Structures passed by
pointer live in a scratch DS area. Only results are stored; no game bytes.
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
A, B = 0xD000, 0xD010            # scratch struct addresses in DGROUP
HOLDRAND = 0x7B20                # MSC rand() state (32-bit)
S029 = "1367"
FN = {
    "money_set": (S029, "00a4"), "money_add_fgp": (S029, "0130"), "money_sub_fgp": (S029, "0180"),
    "money_add": (S029, "01ec"), "money_sub": (S029, "023e"), "money_mul": (S029, "0290"),
    "money_cmp": (S029, "02d4"), "date_set": (S029, "08ae"), "date_add_hours": (S029, "09ea"),
    "days_between": (S029, "0ba4"), "hour_band": (S029, "0744"),
    "rand_mod": ("0000", "0c70"), "random_n": ("0410", "0008"), "rand_1_n": ("0000", "0c8c"),
}


def w(v: int) -> str:
    return struct.pack("<h", ((v + 0x8000) & 0xFFFF) - 0x8000).hex()


def h16(v: int) -> str:
    return "%04X" % (v & 0xFFFF)


def money_hex(m):
    return "".join(w(x) for x in m)


def rnd_money(r):
    return [r.randint(-50, 400), r.randint(-30, 60), r.randint(-30, 60)]


def rnd_date(r):
    return [r.randint(0, 30), r.randint(0, 33), r.randint(0, 13), r.randint(1400, 1500)]  # hour, day, month, year


def cases(r):
    out = []
    for _ in range(60):
        m = rnd_money(r)
        fl, gr, pf = r.randint(-20, 200), r.randint(0, 900), r.randint(0, 900)
        out.append(("money_set", {"memory": [{"offset": "%04X" % A, "hex": money_hex(m)}],
                                  "args": [h16(A), h16(fl), h16(gr), h16(pf)]},
                    {"m": m, "fl": fl, "gr": gr, "pf": pf}))
    for name in ("money_add_fgp", "money_sub_fgp"):
        for _ in range(80):
            m = rnd_money(r)
            fl, gr, pf = r.randint(-20, 100), r.randint(-40, 100), r.randint(-40, 100)
            out.append((name, {"memory": [{"offset": "%04X" % A, "hex": money_hex(m)}],
                               "args": [h16(A), h16(fl), h16(gr), h16(pf)]},
                        {"m": m, "fl": fl, "gr": gr, "pf": pf}))
    for name in ("money_add", "money_sub", "money_cmp"):
        for _ in range(80):
            a, b = rnd_money(r), rnd_money(r)
            out.append((name, {"memory": [{"offset": "%04X" % A, "hex": money_hex(a)},
                                          {"offset": "%04X" % B, "hex": money_hex(b)}],
                               "args": [h16(A), h16(B)]}, {"a": a, "b": b}))
    for _ in range(40):
        unit, n = rnd_money(r), r.randint(-2, 30)
        out.append(("money_mul", {"memory": [{"offset": "%04X" % A, "hex": money_hex([7, 7, 7])},
                                             {"offset": "%04X" % B, "hex": money_hex(unit)}],
                                  "args": [h16(A), h16(B), h16(n)]}, {"unit": unit, "n": n}))
    for _ in range(80):
        y, mo, d, hr = r.randint(1400, 1500), r.randint(0, 30), r.randint(0, 400), r.randint(0, 200)
        out.append(("date_set", {"memory": [{"offset": "%04X" % A, "hex": "00" * 8}],
                                 "args": [h16(A), h16(y), h16(mo), h16(d), h16(hr)]},
                    {"year": y, "month": mo, "day": d, "hour": hr}))
    for _ in range(100):
        dt, hours = rnd_date(r), r.choice([0, 1, 2, 5, 23, 24, 25, 48, 100, 720, 2000, 8760])
        out.append(("date_add_hours", {"memory": [{"offset": "%04X" % A, "hex": "".join(w(x) for x in dt)}],
                                       "args": [h16(A), h16(DS), h16(hours)]}, {"date": dt, "hours": hours}))
    for _ in range(80):
        a, b = rnd_date(r), rnd_date(r)
        a[2], b[2] = a[2] % 12, b[2] % 12   # day numbers index the 12-entry month table
        b[3] = a[3] + r.randint(-3, 3)
        out.append(("days_between", {"memory": [{"offset": "%04X" % A, "hex": "".join(w(x) for x in a)},
                                                {"offset": "%04X" % B, "hex": "".join(w(x) for x in b)}],
                                     "args": [h16(A), h16(B)]}, {"a": a, "b": b}))
    for hr in range(-2, 26):
        out.append(("hour_band", {"memory": [], "args": [h16(hr)]}, {"hour": hr}))
    for name in ("rand_mod", "random_n"):
        for _ in range(80):
            state = r.getrandbits(32)
            n = r.choice([-1, 0, 1, 2, 3, 6, 10, 100, 1000, 32767, r.randint(2, 500)])
            out.append((name, {"memory": [{"offset": "%04X" % HOLDRAND, "hex": struct.pack("<I", state).hex()}],
                               "args": [h16(n)]}, {"state": state, "n": n}))
    return out + targeted(random.Random(20261017))


def targeted(r):
    """Cases for the branches the random cases miss (docs/faithfulness/branches-missed.csv): money_set
    without a carry, money_cmp of equal sums (also written differently), days_between beyond 32000 days."""
    out = []
    for fl, gr, pf in [(3, 0, 0), (3, 19, 11), (3, 20, 11), (3, 19, 12), (0, 0, 0), (-5, 5, 5), (7, 40, 0), (7, 0, 24)]:
        m = rnd_money(r)
        out.append(("money_set", {"memory": [{"offset": "%04X" % A, "hex": money_hex(m)}],
                                  "args": [h16(A), h16(fl), h16(gr), h16(pf)]},
                    {"m": m, "fl": fl, "gr": gr, "pf": pf}))
    for a, b in [([1000, 0, 0], [0, 0, 0]), ([0, 0, 0], [1000, 0, 0]), ([3000, 5, 5], [300, 0, 0]), ([1, 0, 0], [1, 0, 0]), ([1, 0, 0], [0, 20, 0]), ([0, 1, 0], [0, 0, 12]), ([2, 3, 4], [2, 3, 4]),
                 ([-1, 20, 0], [0, 0, 0]), ([0, 0, 0], [0, 0, 0])]:
        out.append(("money_cmp", {"memory": [{"offset": "%04X" % A, "hex": money_hex(a)},
                                             {"offset": "%04X" % B, "hex": money_hex(b)}],
                                  "args": [h16(A), h16(B)]}, {"a": a, "b": b}))
    # 16-bit day numbers: ta/tb wrap, so (int)(tb - ta) >= 0 with a long difference above 32000 needs
    # particular years (a year before b included)
    for ya, yb in [(1400, 1311), (1400, 1312), (1400, 1490), (1400, 1491), (1401, 1312), (1450, 1450), (1400, 1489),
                   (1309, 1400), (1309, 1580), (1310, 1400), (1310, 1401), (1310, 1580)]:
        a, b = [0, 0, 0, ya], [0, 0, 0, yb]
        out.append(("days_between", {"memory": [{"offset": "%04X" % A, "hex": "".join(w(x) for x in a)},
                                                {"offset": "%04X" % B, "hex": "".join(w(x) for x in b)}],
                                     "args": [h16(A), h16(B)]}, {"a": a, "b": b}))
    for n in (-5, -1, 0, 1, 2, 3, 6, 100):
        state = r.getrandbits(32)
        out.append(("rand_1_n", {"memory": [{"offset": "%04X" % HOLDRAND, "hex": struct.pack("<I", state).hex()}],
                                 "args": [h16(n)]}, {"state": state, "n": n}))
    for dy in (-200, -100, -88, -87, 87, 88, 100, 200):
        a = rnd_date(r)
        a[2] %= 12
        b = rnd_date(r)
        b[2] %= 12
        b[3] = a[3] + dy
        out.append(("days_between", {"memory": [{"offset": "%04X" % A, "hex": "".join(w(x) for x in a)},
                                                {"offset": "%04X" % B, "hex": "".join(w(x) for x in b)}],
                                     "args": [h16(A), h16(B)]}, {"a": a, "b": b}))
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    result, images = [], None
    for name, spec, inputs in cases(r):
        seg, off = FN[name]
        res = run(exe, {"unit": name, "entry": {"segment": seg, "offset": off},
                        "sections": [29, 136], "ds": DS, "memory": spec["memory"], "args": spec["args"],
                        "follow": True, "track_all": True})
        images = res["loadedImages"]
        result.append({"function": name, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                       "changed": {c["linear"]: c["after"] for c in res["memChanged"]}})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(),
        "dataSegment": "%04X" % DS, "scratchA": "%04X" % A, "scratchB": "%04X" % B,
        "holdrand": "%04X" % HOLDRAND, "loadedImages": images, "cases": result,
    }
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_text(json.dumps(corpus, indent=0) + "\n")
    print("cases", len(result))


if __name__ == "__main__":
    main()
