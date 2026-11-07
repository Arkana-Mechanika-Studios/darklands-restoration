"""Opt-in forced draws and read-only watches for the battlefield-generator corpus builders (build_castle_corpus.py, build_mine_corpus.py,
build_wild_corpus.py; tools/oracle/mutate_battle.py gen-* steps). Nothing changes unless a scenario carries "force" or the environment
names a scenario file or watch addresses: the committed corpora are built as before.

- DL_SCENARIOS=<file.json>: the builder runs the scenarios of the file (a list, or {"scenarios": [...]}) in place of its own random plan.
- a scenario's "force": {"k": raw}: the k-th call of the generator rand() (root 06A1:29B6, from 0, counted from the generator's entry)
  returns raw (0..32767): before that draw the state DS:7B20 is set to the one whose successor is raw << 16 (battle_calls.draw_state),
  as the rewrite's corpus test does (Darklands.Oracle.Tests ScenarioDraws).
- DL_WATCH=<linear,linear,...>: at each of these addresses (before the instruction runs) the draws made so far and the return address of
  the latest draw are noted (the first few times); the case gets a "watch" field: {address: [[draws, caller linear], ...]}.
"""
from __future__ import annotations

import json
import os
import struct

from unicorn import x86_const as ux

RAND_LINEAR = 0x06A1 * 16 + 0x29B6          # the generator rand() in the root as run_unit loads it (linear = link-time address)
MSC_MUL, MSC_ADD = 214013, 2531011
MSC_MUL_INVERSE = pow(MSC_MUL, -1, 1 << 32)
WATCH_HITS = 3


def draw_state(raw: int) -> int:
    return ((((raw & 0x7FFF) << 16) - MSC_ADD) * MSC_MUL_INVERSE) & 0xFFFFFFFF


def scenarios(make, *args):
    """The scenarios of DL_SCENARIOS when set, else the builder's own plan make(*args)."""
    path = os.environ.get("DL_SCENARIOS")
    if not path:
        return make(*args)
    d = json.load(open(path, encoding="utf-8"))
    return d["scenarios"] if isinstance(d, dict) else d


class Draws:
    """The at_hooks of one scenario run (empty when nothing is forced or watched) and what the watch saw."""

    def __init__(self, sc: dict, ds_seg: int, holdrand: int):
        self.force = {int(k): int(v) & 0x7FFF for k, v in (sc.get("force") or {}).items()}
        self.watch_at = [int(x, 0) for x in os.environ.get("DL_WATCH", "").split(",") if x]
        self.ds, self.holdrand = ds_seg, holdrand
        self.count = 0
        self.caller = None
        self.seen: dict = {}

    def at_hooks(self, others: dict | None = None) -> dict:
        out = dict(others or {})
        if not self.force and not self.watch_at:
            return out
        prev = out.get(RAND_LINEAR)

        def on_rand(uc):
            if prev is not None:
                prev(uc)
            if self.count in self.force:
                uc.mem_write(self.ds * 16 + self.holdrand, struct.pack("<I", draw_state(self.force[self.count])))
            ss = uc.reg_read(ux.UC_X86_REG_SS)
            sp = uc.reg_read(ux.UC_X86_REG_SP)
            off, seg = struct.unpack("<HH", bytes(uc.mem_read(ss * 16 + sp, 4)))       # rand() is far: the caller's return address
            self.caller = seg * 16 + off
            self.count += 1
        out[RAND_LINEAR] = on_rand
        for a in self.watch_at:
            prev_a = out.get(a)

            def on_at(uc, _a=a, _prev=prev_a):
                if _prev is not None:
                    _prev(uc)
                lst = self.seen.setdefault("%X" % _a, [])
                if len(lst) < WATCH_HITS:
                    lst.append([self.count, self.caller])
            out[a] = on_at
        return out

    def annotate(self, case: dict) -> dict:
        if self.watch_at:
            case["watch"] = self.seen
            case["draws"] = self.count
        return case
