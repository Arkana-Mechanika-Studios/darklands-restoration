"""R2: shop reputation modifier (R032+0x1A94..0x1AD1 buy, +0x1C86..0x1CC3 sell) over reputations incl. extremes: original-byte corpus in build_trading_corpus format.

usage: python -I -B build_trading_rep_corpus.py <out json> <DARKLAND.EXE>"""
import hashlib, json, random, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_trading_corpus as b
from run_unit import run
exe = Path(sys.argv[2]).read_bytes()
seg = b.item_table_segment(exe)
REPS = [-32768, -32767, -20000, -10000, -1000, -656, -655, -654, -400, -200, -100, -99, -50, -20, -19, -18, -17, -16, -5, -4, -3, -2, -1, 0, 1, 2, 3, 20, 98, 99, 100, 101, 150, 200, 654, 655, 656, 1000, 16384, 20000, 32767]
r = random.Random(20261027)
out, images = [], None
for fn in ("buy_price", "sell_price"):
    for rep in REPS:
        for k in range(10):
            force = {"rep": rep, "curLoc": r.choice([5, 5, 5, 150]), "flags": 0 if k % 2 == 0 else r.choice([0, 8]),
                     "item": {"type": 3, "categories": 0x1, "tradeFlags": 0, "rarity": r.randint(0, 5), "quality": r.randint(10, 80),
                              "value": r.choice([100, 1000, 3000, 9000, 32000, r.randint(1, 32000)])}}
            if k < 2:
                force["item"]["value"] = 32000
            mem, args, inputs = b.case(r, seg, fn, force)
            res = run(exe, b.plain_spec(fn, mem, args))
            images = res["loadedImages"]
            out.append(b.plain_record(fn, inputs, res))
corpus = {"authority": "test_only_original_byte_execution", "originalExeSha256": hashlib.sha256(exe).hexdigest().upper(), "dataSegment": "%04X" % b.DS,
          "scratchA": "%04X" % b.PARTY, "holdrand": "%04X" % b.HOLDRAND, "loadedImages": images, "cases": out}
Path(sys.argv[1]).write_text(json.dumps(corpus))
print(len(out))
