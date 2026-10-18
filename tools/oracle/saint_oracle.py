"""Saint oracle: the help of every saint (R039 165C, what = 8) of the ORIGINAL DARKLAND.EXE on whole games.

usage: python -I -B saint_oracle.py <DARKLAND.EXE> <game folder> <states folder> <out saint-help.json.gz>
           [--workers 8] [--coverage DIR]

build_saints_corpus.py runs the help of 48 saints on generated members with the item list, the
events and the map left out. Here every saint's help runs in the card oracle's DOS machine
(card_oracle.py: the start states the C# exports, booted with the original's own world loader and
saved-game loader), for the leader (-1), each party member and the whole party (-2), with the
"saintly changes" messages on and off. Recorded per run: the messages the help shows (beside a
portrait or in a box), the random generator, and the bytes of the saved game the original's writer
makes afterwards that differ from the start. tests/Darklands.Cards.Tests/Oracle/SaintHelpOracleTests
runs SaintBook.Help on the same states.
"""
from __future__ import annotations

import argparse
import gzip
import json
import os
import struct
import sys
import time
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from card_oracle import DS, HOLDRAND, Oracle, PARTY_SIZE, CardRun  # noqa: E402
from dosmachine import Stop, linear  # noqa: E402

SAINT_FNS_SEG_SLOT, SAINT_FNS = 0x7E2C, 0x2937      # far pointer table of the 136 saint functions
SAINT_MSGS = 0x01BD                                  # "show saintly changes" (Alt-C)
KEYS = ["L3-O0", "L3-O4", "L120-O1", "L120-O7"]

_W = {}


def saint_entries(o: Oracle):
    m = o.m
    seg = m.word(0x321A, SAINT_FNS_SEG_SLOT)
    out = []
    for i in range(136):
        off, sg = struct.unpack("<HH", m.read(seg, SAINT_FNS + 4 * i, 4))
        out.append((sg, off))
    return out


def run_help(o: Oracle, key: str, saint: int, who: int, messages: bool):
    o.boot(key)
    m = o.m
    m.write(DS, SAINT_MSGS, bytes([1 if messages else 0]))
    o.run = CardRun(0, False, o.messages)
    seg, off = o.saints[saint]
    try:
        ax, _ = m.call(seg, off, [who, 0, 8])
        end = "returns %d" % (ax - 0x10000 if ax & 0x8000 else ax)
        ok = True
    except Stop as e:
        end = "stops %s" % str(e)[:300]
        ok = False
    run, o.run = o.run, None
    result = {"trace": run.lines, "end": end, "random": "%08X" % struct.unpack("<I", m.read(DS, HOLDRAND, 4))[0]}
    if ok:
        result["sav"] = o.final_sav()
    return result


def init_worker(exe_path, game, states, coverage_dir):
    o = Oracle(Path(exe_path).read_bytes(), game, Path(states), coverage=bool(coverage_dir))
    o.saints = saint_entries(o)
    _W["oracle"], _W["coverage"] = o, coverage_dir


def run_saint(saint: int):
    o = _W["oracle"]
    t = time.time()
    runs = {}
    for key in KEYS:
        o.boot(key)
        size = o.m.word(DS, PARTY_SIZE)
        for who in range(size):   # the prayer screen helps the member who prays (R034)
            for messages in (True, False):
                runs["%s who %d %s" % (key, who, "messages" if messages else "quiet")] = run_help(o, key, saint, who, messages)
    if _W["coverage"]:
        cov = o.m.coverage_result()
        o.m.cov_insn_rec, o.m.cov_edge_rec = {}, {}
        with open(os.path.join(_W["coverage"], "cov-%d.jsonl" % os.getpid()), "a") as f:
            f.write(json.dumps({"unit": "saint %d" % saint, **cov}, separators=(",", ":")) + "\n")
    return saint, {"seconds": round(time.time() - t, 1), "runs": runs}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("states")
    ap.add_argument("out")
    ap.add_argument("--saints")
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--coverage")
    a = ap.parse_args()
    if a.coverage:
        os.makedirs(a.coverage, exist_ok=True)
    saints = list(range(136)) if not a.saints else [int(x, 0) for x in a.saints.split(",")]
    probe = Oracle(Path(a.exe).read_bytes(), a.game, Path(a.states))
    clock = probe.clock()
    del probe
    with Pool(a.workers, initializer=init_worker, initargs=(a.exe, a.game, a.states, a.coverage)) as pool:
        results = dict(pool.imap_unordered(run_saint, saints))
    corpus = {"authority": "test_only_original_byte_execution", "source": "tools/oracle/saint_oracle.py",
              "clock": "%04X" % clock, "saints": {str(k): results[k] for k in sorted(results)}}
    with gzip.open(a.out, "wt", encoding="utf-8") as f:
        json.dump(corpus, f, separators=(",", ":"), sort_keys=True)
    print("saints", len(results))


if __name__ == "__main__":
    main()
