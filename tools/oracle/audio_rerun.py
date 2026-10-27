"""Sound sidecar of a card corpus: the ORIGINAL rerun over every run of an older corpus (card_oracle.py recorded no sound lines before the
audio round), only the sound lines of each run kept.

usage: python -I -B audio_rerun.py <DARKLAND.EXE> <game folder> <corpus.json.gz> <out sidecar.json.gz> <states folder>[;<states folder>...]
           [--workers 3] [--direct jobs-direct.json] [--cards 2,6] [--full] [--only-runs FILE]

The corpus gives the runs ("plain KEY", "probe KEY", "hand KEY M1L7[H][S2]", "direct KEY D"); the start state of KEY is the first of the states
folders that holds state-KEY.sav (the C# exports: DARKLANDS_CARD_ORACLE_EXPORT / DARKLANDS_CARD_ORACLE_SAVES). The sidecar is
{"clock", "cards": {card: {"runs": {run name: ["sound 1", "music 6", ...]}}}}, which tests/Darklands.Cards.Tests/Oracle/CardSoundOracleTests
replays against the C# (DARKLANDS_CARD_ORACLE_SOUNDS). Results are kept as they arrive in <out>.partial.jsonl and resumed.

--full: the whole result of each run (trace, end, random, saved game: what card_oracle.py writes) instead of the sound lines, as a corpus with
"sound": true that replaces the old one (the committed corpora; their traces then hold the sound lines among the others).
--only-runs FILE: a JSON list of [card hex, run name] pairs; only those runs are rerun (the sidecar then has just them).
"""
from __future__ import annotations

import argparse
import gzip
import json
import re
import sys
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import card_oracle as co  # noqa: E402

_W = {}


def sound_lines(trace):
    return [l for l in trace if l.startswith(("sound ", "music "))]


def index_of(folders):
    """state file name -> the first folder that has it (a folder given twice, or a key in two folders, is taken from the first)."""
    found = {}
    for f in folders:
        for entry in f.iterdir():
            if entry.name.endswith(".sav"):
                found.setdefault(entry.name, f)
    return found


def resolve(index, key):
    base = co.split_key(key)[0]
    try:
        return index[f"state-{base}.sav"]
    except KeyError:
        raise FileNotFoundError(base) from None


def parse_hand(spec: str) -> dict:
    m = re.fullmatch(r"M(\d+)L(\d+)(H?)(?:S(\d+))?", spec)
    return {"menu": int(m[1]), "line": int(m[2]), "hints": bool(m[3]), "slot": int(m[4]) if m[4] else None}


def init(exe, game, folders, direct, full):
    _W["full"] = full
    _W["args"] = (exe, game, folders)
    _W["oracle"] = co.Oracle(Path(exe).read_bytes(), game, folders[0])
    _W["index"] = index_of(folders)
    _W["direct"] = direct


def one(o, card: int, name: str):
    kind, key, *rest = name.split(" ")
    o.states = resolve(_W["index"], key)
    option = int(key[key.rindex("-O") + 2:])
    if kind == "hand":
        r = o.run_card(card, key, option, True, parse_hand(rest[0]))
    elif kind == "direct":
        steps = _W["direct"].get((card, key, rest[0]))
        r = o.run_direct(card, key, steps) if steps else {"trace": ["error no steps"]}
    else:
        r = o.run_card(card, key, option, kind == "probe")
    return r if _W["full"] else sound_lines(r["trace"])


def work(item):
    card, names = item
    runs = {}
    for name in names:
        try:
            runs[name] = one(_W["oracle"], card, name)
        except Exception as e:  # noqa: BLE001
            exe, game, folders = _W["args"]
            _W["oracle"] = co.Oracle(Path(exe).read_bytes(), game, folders[0])
            msg = "error %s: %s" % (type(e).__name__, str(e)[:120])
            runs[name] = {"trace": [msg], "end": "error", "random": "0", "draws": 0} if _W["full"] else [msg]
    return card, runs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("corpus")
    ap.add_argument("out")
    ap.add_argument("states")
    ap.add_argument("--workers", type=int, default=3)
    ap.add_argument("--direct")
    ap.add_argument("--cards")
    ap.add_argument("--chunk", type=int, default=400)
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--only-runs")
    a = ap.parse_args()
    folders = [Path(p) for p in a.states.split(";")]
    direct = {}
    if a.direct:
        for j in json.load(open(a.direct)):
            direct[(j["card"], j["key"], j["group"])] = j["steps"]
    corpus = json.load(gzip.open(a.corpus, "rt", encoding="utf-8"))
    only = {int(x, 0) for x in a.cards.split(",")} if a.cards else None
    wanted = None
    if a.only_runs:
        wanted = {}
        for hexcard, name in json.load(open(a.only_runs)):
            wanted.setdefault(hexcard, set()).add(name)
    partial = Path(a.out + ".partial.jsonl")
    done = {}
    if partial.exists():
        for line in partial.read_text(encoding="utf-8").splitlines():
            k, v = json.loads(line)
            done[k] = v
    todo = []
    for hexcard, e in corpus["cards"].items():
        card = int(hexcard, 16)
        if only and card not in only:
            continue
        names = [n for n in e["runs"] if wanted is None or n in wanted.get(hexcard, ())]
        if not names:
            continue
        for i in range(0, len(names), a.chunk):
            todo.append(("%s.%d" % (hexcard, i), card, names[i:i + a.chunk]))
    todo = [t for t in todo if t[0] not in done]
    with Pool(a.workers, initializer=init, initargs=(a.exe, a.game, folders, direct, a.full)) as pool, partial.open("a", encoding="utf-8") as f:
        for (cid, _, _), (card, runs) in zip(todo, pool.imap(work, [(c, n) for _, c, n in todo])):
            f.write(json.dumps([cid, runs], separators=(",", ":")) + "\n")
            f.flush()
            done[cid] = runs
    cards: dict = {}
    for cid, runs in done.items():
        cards.setdefault(cid.split(".")[0], {"runs": {}})["runs"].update(runs)
    out = {"authority": "test_only_original_byte_execution", "source": "tools/oracle/audio_rerun.py", "clock": corpus["clock"], "cards": cards}
    if a.full:
        # the corpus shape of card_oracle.py: per card the function, the record and the runs with their texts shared
        for hexcard, e in cards.items():
            old = corpus["cards"][hexcard]
            cards[hexcard] = {"function": old["function"], "record": old["record"], "seconds": old.get("seconds", 0), **co.share_texts(e["runs"])}
        out["sound"] = True
    with gzip.open(a.out, "wt", encoding="utf-8") as f:
        json.dump(out, f, separators=(",", ":"), sort_keys=True)
    print("cards", len(cards), "runs", sum(len(c["runs"]) for c in cards.values()))


if __name__ == "__main__":
    main()
