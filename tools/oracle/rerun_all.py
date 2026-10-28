"""Run every fight (battle_oracle.FIGHTS) and every session (lockstep-sessions/*.steps) with N workers, each its own process.

usage: python -I -B rerun_all.py <out folder> [--workers N] [--fights a,b|none] [--sessions a,b|none] [--live] [--replay-only]
           [--no-coverage] [--recordings DIR] [--headless <Darklands.Host.Headless.exe>] [--exe E] [--game G]

Outputs (as the two tools write them): <out>/fights/<fight>/report.json, <out>/fights/<fight>/<fight>/cov/cov-0.jsonl (a fight
that agrees), <out>/sessions/<session>/report.json and cov/cov-0.jsonl (a session whose states all agree), <out>/<job>.log, and
<out>/summary.json with one row per job: verdict, first-difference point, whether the original was replayed or run live, seconds.

The original side is replayed from recordings (recording.py) when they are valid, else run live and recorded; --live forces live (from the
snapshot of the run's start point when there is one, else it makes it; --no-snapshot starts every live run from the beginning). The jobs are
started slowest first (the last live durations, <recordings>/durations.json).
RULE: a full --live rerun before every release, and periodically (the recordings only say what the original did; the live run is
the proof that they still do).

Workers: default the core count, bounded by the free memory (a live original takes up to ~400 MB; a job is started only while
that much plus a reserve is free).
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
REPO = HERE.parent.parent
GAME = REPO.parent / "game" / "DARKLAND"

# DKSAVE40 (written by another tool: party size 6, party slots whose status is 0) as a normalised copy under local/ (divergence D-72509c: the
# original'"'"'s writer R037+0x2ACE recounts the party and skips a status-0 slot): the copy is made by local/overnight/dk40/normalise.py when missing
NORMALISED = "local/oracle-saves/DKSAVE40N.SAV"
NORMALISING = {NORMALISED: ("DKSAVE40.SAV", "local/overnight/dk40/normalise.py")}
LOCAL = Path(os.environ.get("DARKLANDS_LOCAL", REPO / "local"))     # the git-ignored local/ folder (shared by worktrees through the variable)

# the saved game each session starts from (None: the start screen, no saved game)
SESSIONS = {"pressburg-map": "DKSAVE0.SAV", "tyrnau": "DKSAVE0.SAV", "residence": "DKSAVE0.SAV", "save-load": "DKSAVE0.SAV",
            "gate-battle": "DKSAVE0.SAV", "shop": "DKSAVE0.SAV", "cache": "Dksave10.sav", "cache-revisit": "Dksave10.sav", "saints": "Dksave10.sav",
            "stay": "Dksave10.sav", "sheet": "Dksave10.sav", "wildcamp": "DKSAVE17.SAV", "alchemy": "Dksave10.sav",
            "inventory": "Dksave10.sav", "chargen-party": None, "notices": "DKSAVE0.SAV", "university": "DKSAVE0.SAV",
            "smiths": "DKSAVE0.SAV", "healer": "DKSAVE0.SAV", "churches": "DKSAVE0.SAV", "wharves": "DKSAVE0.SAV",
            "university-shop": "DKSAVE0.SAV",
            "stables": "DKSAVE0.SAV", "pawnshop": "DKSAVE0.SAV", "foreigntrader": "DKSAVE0.SAV", "banks": "DKSAVE0.SAV",
            "menubar": "DKSAVE0.SAV", "menubar2": "DKSAVE0.SAV", "menubar-kbright": "DKSAVE0.SAV", "roster": "DKSAVE0.SAV", "roster4": "DKSAVE0.SAV",
            "wien-rathaus": "DKSAVE0.SAV", "mapsave": "DKSAVE17.SAV",
            # the inn's roster screen (local/overnight/roster2): DKSAVE0 (five listed), DKSAVE8 (Hamburg, four rows), Dksave10 (Ulm, nine rows)
            "roster-colours": "DKSAVE0.SAV", "roster-moves": "DKSAVE0.SAV", "roster-wrap": "DKSAVE8.SAV", "roster-mouse": "DKSAVE8.SAV",
            "roster-create2": "DKSAVE8.SAV", "roster-ulm": "Dksave10.sav",
            # the player's orders in a battle (local/overnight/orders): Prague's gate DKSAVE40, Luebeck's Dksave21, the others the gate of DKSAVE0
            "orders-select": "DKSAVE0.SAV", "orders-walk": "DKSAVE0.SAV", "orders-missile": "DKSAVE0.SAV", "orders-attack-a": "DKSAVE0.SAV",
            "orders-attack-b": "DKSAVE0.SAV", "orders-attack-b2": "DKSAVE0.SAV", "orders-attack-v": "DKSAVE0.SAV", "orders-attacks": "DKSAVE0.SAV",
            "orders-sheet": "DKSAVE0.SAV", "orders-sheetclick": "DKSAVE0.SAV", "orders-save": "DKSAVE0.SAV", "orders-potion": "Dksave21.sav", "luebeck-potion": "Dksave21.sav", "orders-potion2": NORMALISED,
            "orders-potion3": NORMALISED, "orders-prague-missile": NORMALISED,
            "sheet-dksave0": "DKSAVE0.SAV",
            # the loot screen after the gate fight of DKSAVE0 (local/overnight/loot): keys, mouse, capital keys and Esc
            "loot-keys": "DKSAVE0.SAV", "loot-mouse": "DKSAVE0.SAV", "loot-esc": "DKSAVE0.SAV",
            # keys held through the keyboard interrupt (kdown / kup, DS:0880..0882) and Ctrl (local/batch/kbd): the map of DKSAVE17, the gate fight of DKSAVE0
            "kbd-map-held": "DKSAVE17.SAV", "kbd-map-enter": "DKSAVE17.SAV", "kbd-map-wait": "DKSAVE17.SAV",
            "kbd-battle-cursor": "DKSAVE0.SAV", "kbd-battle-held": "DKSAVE0.SAV", "kbd-battle-ctrl": "DKSAVE0.SAV",
            # the camp and the pack outside a battle (local/overnight/camp): DKSAVE99 and Dksave22 are parties on the map, DKSAVE0 is at the Pressburg inn
            "camp-wild-brew": "DKSAVE99.SAV", "camp-wild-risk": "Dksave22.sav", "camp-inn-broke": "DKSAVE0.SAV", "camp-rough": "DKSAVE0.SAV", "camp-mouse": "DKSAVE99.SAV",
            "pack-potions": "DKSAVE99.SAV", "pack-stacks": "DKSAVE99.SAV",
            # the mine of Rammelsburg (local/overnight/dungeon): DKSAVE99 on the map, the walk to the town, the mine's battle
            "dungeon-mine-doors": "DKSAVE99.SAV", "savemid-mine": "DKSAVE99.SAV", "dungeon-mine-orders": "DKSAVE99.SAV", "dungeon-mine-group": "DKSAVE99.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-sheet-01": "Dksave10.sav",
            "gen-sheet-02": "Dksave10.sav",
            "gen-sheet-03": "Dksave10.sav",
            "gen-party-01": None,
            "gen-party-02": None,
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-sheet-10": "Dksave10.sav",
            "gen-sheet-12": "Dksave10.sav",
            "gen-sheet-13": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-sheet-14": "Dksave10.sav",
            "gen-sheet-15": "Dksave10.sav",
            "gen-sheet-16": "Dksave10.sav",
            "gen-sheet-17": "Dksave10.sav",
            "gen-sheet-18": "Dksave10.sav",
            "gen-sheet-19": "Dksave10.sav",
            "gen-sheet-20": "Dksave10.sav",
            "gen-sheet-21": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-party-04": None,
            "gen-party-06": None,
            "gen-party-10": None,
            "gen-party-11": None,
            "gen-party-12": None,
            "gen-party-13": None,
            "gen-party-14": None,
            "gen-party-15": None,
            "gen-party-17": None,
            "gen-party-19": None,
            "gen-party-20": None,
            "gen-party-21": None,
            "gen-party-22": None,
            "gen-party-23": None,
            "gen-party-24": None,
            "gen-party-25": None,
            "gen-party-26": None,
            "gen-party-27": None,
            "gen-party-28": None,
            "gen-party-29": None,
            "gen-shop-01": "DKSAVE0.SAV",
            "gen-stable-01": "DKSAVE0.SAV",
            "gen-cache-01": "Dksave10.sav",
            "gen-stay-01": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-shop-02": "DKSAVE0.SAV",
            "gen-shop-03": "DKSAVE0.SAV",
            "gen-shop-05": "DKSAVE0.SAV",
            "gen-shop-07": "DKSAVE0.SAV",
            "gen-pawn-02": "DKSAVE0.SAV",
            "gen-pawn-03": "DKSAVE0.SAV",
            "gen-pawn-04": "DKSAVE0.SAV",
            "gen-stable-02": "DKSAVE0.SAV",
            "gen-stable-03": "DKSAVE0.SAV",
            "gen-stable-04": "DKSAVE0.SAV",
            "gen-cache-02": "Dksave10.sav",
            "gen-cache-03": "Dksave10.sav",
            "gen-cache-04": "Dksave10.sav",
            "gen-cache-06": "Dksave10.sav",
            "gen-cache-07": "Dksave10.sav",
            "gen-cache-08": "Dksave10.sav",
            "gen-cache-09": "Dksave10.sav",
            "gen-stay-03": "Dksave10.sav",
            "gen-wild-03": "DKSAVE99.SAV",
            "gen-wild-05": "DKSAVE99.SAV",
            "gen-roster-05": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-bench-01": "Dksave10.sav",
            "gen-bench-02": "Dksave10.sav",
            "gen-bench-04": "Dksave10.sav",
            "gen-bench-05": "Dksave10.sav",
            "gen-bench-06": "Dksave10.sav",
            "gen-party-33": None,
            "gen-party-35": None,
            "gen-party-36": None,
            "gen-party-37": None,
            "gen-party-39": None,
            "gen-party-40": None,
            "gen-party-42": None,
            "gen-party-45": None,
            "gen-party-47": None,
            "gen-party-48": None,
            "gen-party-49": None,
            "gen-party-50": None,
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-bench99-01": "DKSAVE99.SAV",
            "gen-bench99-02": "DKSAVE99.SAV",
            "gen-bench99-04": "DKSAVE99.SAV",
            "gen-sheet99-03": "DKSAVE99.SAV",
            "gen-sheet99-05": "DKSAVE99.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-pack99-01": "DKSAVE99.SAV",
            "gen-pack99-02": "DKSAVE99.SAV",
            "gen-pack99-03": "DKSAVE99.SAV",
            "gen-pack99-04": "DKSAVE99.SAV",
            "gen-pack99-05": "DKSAVE99.SAV",
            "gen-pack99-06": "DKSAVE99.SAV",
            "gen-pack10-01": "Dksave10.sav",
            "gen-pack10-02": "Dksave10.sav",
            "gen-pack10-03": "Dksave10.sav",
            "gen-pack10-04": "Dksave10.sav",
            "gen-pack10-05": "Dksave10.sav",
            "gen-pack10-06": "Dksave10.sav",
            "gen-create2-01": "DKSAVE8.SAV",
            "gen-create2-02": "DKSAVE8.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-create2-12": "DKSAVE8.SAV",
            "gen-create2-14": "DKSAVE8.SAV",
            "gen-create2-16": "DKSAVE8.SAV",
            "gen-create2-26": "DKSAVE8.SAV",
            "gen-wild-07": "DKSAVE99.SAV",
            "gen-roster-11": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-sheet-04": "Dksave10.sav",
            "gen-sheet-11": "Dksave10.sav",
            "gen-sheet99-01": "DKSAVE99.SAV",
            "gen-sheet99-02": "DKSAVE99.SAV",
            "gen-sheet99-04": "DKSAVE99.SAV",
            "gen-sheet99-06": "DKSAVE99.SAV",
            "gen-saints-01": "Dksave10.sav",
            "gen-saints-02": "Dksave10.sav",
            "gen-saints-03": "Dksave10.sav",
            "gen-saints-04": "Dksave10.sav",
            "gen-saints-05": "Dksave10.sav",
            "gen-saints-06": "Dksave10.sav",
            "gen-cache-05": "Dksave10.sav",
            "gen-pawn-01": "DKSAVE0.SAV",
            "gen-pawn-05": "DKSAVE0.SAV",
            "gen-shop-04": "DKSAVE0.SAV",
            "gen-shop-06": "DKSAVE0.SAV",
            "gen-shop-08": "DKSAVE0.SAV",
            "gen-shop-09": "DKSAVE0.SAV",
            "gen-stay-02": "Dksave10.sav",
            "gen-stay-04": "Dksave10.sav",
            "gen-stay-05": "Dksave10.sav",
            "gen-stay-06": "Dksave10.sav",
            "gen-stay-07": "Dksave10.sav",
            "gen-stay-08": "Dksave10.sav",
            "gen-stay-09": "Dksave10.sav",
            "gen-wild-02": "DKSAVE99.SAV",
            "gen-stay-08-full": "Dksave10.sav",
            "gen-pawn-05-full": "DKSAVE0.SAV",
            "gen-wild-02-full": "DKSAVE99.SAV",
            "map-enter-below": "DKSAVE0.SAV",
            "map-enter-above": "DKSAVE0.SAV",
            "gen-wild-04": "DKSAVE99.SAV",
            "gen-wild-06": "DKSAVE99.SAV",
            "gen-wild-08": "DKSAVE99.SAV",
            "gen-wild-09": "DKSAVE99.SAV",
            "gen-party2-01": None,
            "gen-party2-02": None,
            "gen-party2-03": None,
            "gen-party2-04": None,
            "gen-party2-05": None,
            "gen-party2-06": None,
            "gen-party2-07": None,
            "gen-party2-08": None,
            "gen-party2-09": None,
            "gen-party2-10": None,
            "gen-party2-11": None,
            "gen-party2-12": None,
            "gen-party2-13": None,
            "gen-party2-14": None,
            "gen-party2-15": None,
            "gen-party2-16": None,
            "gen-roster2-01": "DKSAVE0.SAV",
            "gen-roster2-02": "DKSAVE0.SAV",
            "gen-roster2-03": "DKSAVE0.SAV",
            "gen-roster2-04": "DKSAVE0.SAV",
            "gen-roster2-05": "DKSAVE0.SAV",
            "gen-roster2-06": "DKSAVE0.SAV",
            "gen-roster2-07": "DKSAVE0.SAV",
            "gen-roster2-08": "DKSAVE0.SAV",
            "gen-roster2-09": "DKSAVE0.SAV",
            "gen-roster2-10": "DKSAVE0.SAV",
            "gen-create3-01": "DKSAVE8.SAV",
            "gen-create3-02": "DKSAVE8.SAV",
            "gen-create3-03": "DKSAVE8.SAV",
            "gen-create3-04": "DKSAVE8.SAV",
            "gen-create3-05": "DKSAVE8.SAV",
            "gen-create3-06": "DKSAVE8.SAV",
            "gen-create3-07": "DKSAVE8.SAV",
            "gen-create3-08": "DKSAVE8.SAV",
            "gen-create3-09": "DKSAVE8.SAV",
            "gen-create3-10": "DKSAVE8.SAV",
            "gen-party-07": None,
            "gen-create-02": "DKSAVE8.SAV",
            "gen-create-03": "DKSAVE8.SAV",
            "gen-create-04": "DKSAVE8.SAV",
            "gen-create-05": "DKSAVE8.SAV",
            "gen-create-06": "DKSAVE8.SAV",
            "gen-create-08": "DKSAVE8.SAV",
            "gen-create-10": "DKSAVE8.SAV",
            "gen-create2-03": "DKSAVE8.SAV",
            "gen-create2-06": "DKSAVE8.SAV",
            "gen-create2-07": "DKSAVE8.SAV",
            "gen-create2-08": "DKSAVE8.SAV",
            "gen-create2-17": "DKSAVE8.SAV",
            "gen-create2-20": "DKSAVE8.SAV",
            "gen-create2-23": "DKSAVE8.SAV",
            "gen-create2-25": "DKSAVE8.SAV",
            "gen-roster-02": "DKSAVE0.SAV",
            "gen-roster-03": "DKSAVE0.SAV",
            "gen-roster-04": "DKSAVE0.SAV",
            "gen-roster-06": "DKSAVE0.SAV",
            "gen-roster-07": "DKSAVE0.SAV",
            "gen-roster-08": "DKSAVE0.SAV",
            "gen-roster-10": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-mbar-01": "DKSAVE0.SAV",
            "gen-mbar-02": "DKSAVE0.SAV",
            "gen-mbar-03": "DKSAVE0.SAV",
            "gen-mbar-04": "DKSAVE0.SAV",
            "gen-mbar-05": "DKSAVE0.SAV",
            "gen-mbar-06": "DKSAVE0.SAV",
            "gen-mbar-07": "DKSAVE0.SAV",
            "gen-mbar-08": "DKSAVE0.SAV",
            "gen-mbar-09": "DKSAVE0.SAV",
            "gen-mbar-10": "DKSAVE0.SAV",
            # lead 1 of screens2 (leads-menu): Alt-M in the keyboard mode, with moves so that the F10 reaches the hot keys' look
            "gen-mbar-altm": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-mquit-01": "DKSAVE0.SAV",
            "gen-mquit-02": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-mquit-03": "DKSAVE0.SAV",
            "gen-mquit-04": "DKSAVE17.SAV",
            "gen-mquit-05": None,
            "gen-mquit-06": None,
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-mbarm-01": "DKSAVE17.SAV",
            "gen-mbarm-02": "DKSAVE17.SAV",
            "gen-mbarm-03": "DKSAVE17.SAV",
            "gen-mbarm-04": "DKSAVE17.SAV",
            "gen-mbarm-05": "DKSAVE17.SAV",
            "gen-mbarm-06": "DKSAVE17.SAV",
            "gen-mbarm-07": "DKSAVE17.SAV",
            "gen-mbarm-08": "DKSAVE17.SAV",
            "gen-mbarm-09": "DKSAVE17.SAV",
            "gen-mbarm-10": "DKSAVE17.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-gate-05": "DKSAVE0.SAV",
            "gen-gate-06": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-pots99-01": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-02": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-03": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-04": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-05": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-06": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-07": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-08": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-09": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-10": "synth/DKSAVE99-pots3.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-pots99-11": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-12": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-13": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-14": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-15": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-16": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-17": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-18": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-19": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-20": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-21": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-22": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-23": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-24": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-25": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-26": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-27": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-28": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-29": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-30": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-31": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-32": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-33": "synth/DKSAVE99-pots3.sav",
            "gen-pots99-34": "synth/DKSAVE99-pots3.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-kit-01": "synth/Dksave10-kitchen.sav",
            "gen-kit-02": "synth/Dksave10-kitchen.sav",
            "gen-kit-03": "synth/Dksave10-kitchen.sav",
            "gen-kit-04": "synth/Dksave10-kitchen.sav",
            "gen-kit-05": "synth/Dksave10-kitchen.sav",
            "gen-kit-06": "synth/Dksave10-kitchen.sav",
            "gen-kit-07": "synth/Dksave10-kitchen.sav",
            "gen-kit-08": "synth/Dksave10-kitchen.sav",
            "gen-kit-09": "synth/Dksave10-kitchen.sav",
            "gen-kit-10": "synth/Dksave10-kitchen.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-hurt-01": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-02": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-03": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-04": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-05": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-06": "synth/DKSAVE0-hurt.sav",
            "gen-hurt-08": "synth/DKSAVE0-hurtstatus.sav",
            "gen-hurt-10": "synth/DKSAVE0-away.sav",
            "gen-hurt-07": "synth/DKSAVE0-hurtattr.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen),
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-gatem-03": "DKSAVE0.SAV",
            "gen-gatem-04": "DKSAVE0.SAV",
            "gen-gatem-05": "DKSAVE0.SAV",
            # leads of screens2 (leads-menu): the Alt keys' message boxes in the paused gate battle (lockstep.BOX_FRAMES)
            "gen-gatem-01": "DKSAVE0.SAV",
            "gen-gatem-02": "DKSAVE0.SAV",
            "gen-gatem-06": "DKSAVE0.SAV",
            "gen-gate-13": "DKSAVE0.SAV",
            "gen-gate-14": "DKSAVE0.SAV",
            "gen-gate-16": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-party2-21": None,
            "gen-party2-22": None,
            "gen-party2-23": None,
            "gen-party2-24": None,
            "gen-party2-25": None,
            "gen-party2-26": None,
            "gen-party2-27": None,
            "gen-party2-28": None,
            "gen-party2-29": None,
            "gen-party2-30": None,
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-create3-22": "DKSAVE8.SAV",
            "gen-create3-23": "DKSAVE8.SAV",
            "gen-create3-24": "DKSAVE8.SAV",
            "gen-create3-25": "DKSAVE8.SAV",
            "gen-create3-26": "DKSAVE8.SAV",
            "gen-create3-27": "DKSAVE8.SAV",
            "gen-create3-28": "DKSAVE8.SAV",
            "gen-create3-29": "DKSAVE8.SAV",
            "gen-create3-30": "DKSAVE8.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-roster2-21": "DKSAVE0.SAV",
            "gen-roster2-22": "DKSAVE0.SAV",
            "gen-roster2-23": "DKSAVE0.SAV",
            "gen-roster2-24": "DKSAVE0.SAV",
            "gen-roster2-25": "DKSAVE0.SAV",
            "gen-roster2-26": "DKSAVE0.SAV",
            "gen-roster2-27": "DKSAVE0.SAV",
            "gen-roster2-28": "DKSAVE0.SAV",
            "gen-roster2-29": "DKSAVE0.SAV",
            "gen-roster2-30": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-cache-21": "Dksave10.sav",
            "gen-cache-22": "Dksave10.sav",
            "gen-cache-23": "Dksave10.sav",
            "gen-cache-24": "Dksave10.sav",
            "gen-cache-25": "Dksave10.sav",
            "gen-cache-26": "Dksave10.sav",
            "gen-cache-27": "Dksave10.sav",
            "gen-cache-28": "Dksave10.sav",
            "gen-cache-29": "Dksave10.sav",
            "gen-cache-30": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-shop-21": "DKSAVE0.SAV",
            "gen-shop-22": "DKSAVE0.SAV",
            "gen-shop-23": "DKSAVE0.SAV",
            "gen-shop-24": "DKSAVE0.SAV",
            "gen-shop-25": "DKSAVE0.SAV",
            "gen-shop-26": "DKSAVE0.SAV",
            "gen-shop-27": "DKSAVE0.SAV",
            "gen-shop-28": "DKSAVE0.SAV",
            "gen-shop-29": "DKSAVE0.SAV",
            "gen-shop-30": "DKSAVE0.SAV",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-bench-21": "Dksave10.sav",
            "gen-bench-22": "Dksave10.sav",
            "gen-bench-23": "Dksave10.sav",
            "gen-bench-25": "Dksave10.sav",
            "gen-bench-26": "Dksave10.sav",
            "gen-bench-27": "Dksave10.sav",
            "gen-bench-28": "Dksave10.sav",
            "gen-bench-29": "Dksave10.sav",
            "gen-bench-30": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-stay-21": "Dksave10.sav",
            "gen-stay-22": "Dksave10.sav",
            "gen-stay-23": "Dksave10.sav",
            "gen-stay-24": "Dksave10.sav",
            "gen-stay-25": "Dksave10.sav",
            "gen-stay-26": "Dksave10.sav",
            "gen-stay-27": "Dksave10.sav",
            "gen-stay-28": "Dksave10.sav",
            "gen-stay-29": "Dksave10.sav",
            "gen-stay-30": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-sheet-31": "Dksave10.sav",
            "gen-sheet-32": "Dksave10.sav",
            "gen-sheet-33": "Dksave10.sav",
            "gen-sheet-34": "Dksave10.sav",
            "gen-sheet-35": "Dksave10.sav",
            "gen-sheet-36": "Dksave10.sav",
            "gen-sheet-37": "Dksave10.sav",
            "gen-sheet-38": "Dksave10.sav",
            "gen-sheet-39": "Dksave10.sav",
            "gen-sheet-40": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-hurt-09": "synth/DKSAVE0-dead.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-bench-31": "Dksave10.sav",
            "gen-bench-32": "Dksave10.sav",
            "gen-bench-33": "Dksave10.sav",
            "gen-bench-34": "Dksave10.sav",
            "gen-bench-35": "Dksave10.sav",
            "gen-bench-36": "Dksave10.sav",
            "gen-bench-37": "Dksave10.sav",
            "gen-bench-38": "Dksave10.sav",
            "gen-bench-39": "Dksave10.sav",
            "gen-bench-40": "Dksave10.sav",
            "gen-bench-41": "Dksave10.sav",
            "gen-bench-42": "Dksave10.sav",
            "gen-bench-43": "Dksave10.sav",
            "gen-bench-44": "Dksave10.sav",
            "gen-bench-45": "Dksave10.sav",
            "gen-bench-46": "Dksave10.sav",
            "gen-bench-47": "Dksave10.sav",
            "gen-bench-48": "Dksave10.sav",
            "gen-bench-49": "Dksave10.sav",
            "gen-bench-50": "Dksave10.sav",
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-party2-31": None,
            "gen-party2-32": None,
            "gen-party2-33": None,
            "gen-party2-34": None,
            "gen-party2-35": None,
            "gen-party2-36": None,
            "gen-party2-37": None,
            "gen-party2-38": None,
            "gen-party2-39": None,
            "gen-party2-40": None,
            "gen-party2-41": None,
            "gen-party2-42": None,
            "gen-party2-43": None,
            "gen-party2-44": None,
            "gen-party2-45": None,
            "gen-party2-46": None,
            "gen-party2-47": None,
            "gen-party2-48": None,
            "gen-party2-49": None,
            "gen-party2-50": None,
            "gen-party2-51": None,
            "gen-party2-52": None,
            "gen-party2-53": None,
            "gen-party2-54": None,
            "gen-party2-55": None,
            "gen-party2-56": None,
            "gen-party2-57": None,
            "gen-party2-58": None,
            "gen-party2-59": None,
            "gen-party2-60": None,
            # generated by gen_screen_sessions.py (seeded key / mouse walks over one screen)
            "gen-cache-31": "Dksave10.sav",
            "gen-cache-32": "Dksave10.sav",
            "gen-cache-33": "Dksave10.sav",
            "gen-cache-34": "Dksave10.sav",
            "gen-cache-35": "Dksave10.sav",
            "gen-cache-36": "Dksave10.sav",
            "gen-cache-37": "Dksave10.sav",
            "gen-cache-38": "Dksave10.sav",
            "gen-cache-39": "Dksave10.sav",
            "gen-cache-40": "Dksave10.sav",
            "gen-cache-41": "Dksave10.sav",
            "gen-cache-42": "Dksave10.sav",
            "gen-cache-43": "Dksave10.sav",
            "gen-cache-44": "Dksave10.sav",
            "gen-cache-45": "Dksave10.sav",
            "gen-cache-46": "Dksave10.sav",
            "gen-cache-47": "Dksave10.sav",
            "gen-cache-48": "Dksave10.sav",
            "gen-cache-49": "Dksave10.sav",
            "gen-cache-50": "Dksave10.sav"}

# the right button (gen_screen_sessions.py rm*: the menu bar held or clicked with it; r*: a screen's walk with it mixed in; local/batch/rightclick)
SESSIONS.update({
    "gen-rmbar-01": "DKSAVE0.SAV",
    "gen-rmbar-02": "DKSAVE0.SAV",
    "gen-rmbar-03": "DKSAVE0.SAV",
    "gen-rmbarm-01": "DKSAVE17.SAV",
    "gen-rmbarm-02": "DKSAVE17.SAV",
    "gen-rmbarm-03": "DKSAVE17.SAV",
    "gen-rmgate-01": "DKSAVE0.SAV",
    "gen-rmgate-02": "DKSAVE0.SAV",
    "gen-rsheet-01": "Dksave10.sav",
    "gen-rsheet-02": "Dksave10.sav",
    "gen-rshop-01": "DKSAVE0.SAV",
    "gen-rshop-02": "DKSAVE0.SAV",
    "gen-rpawn-01": "DKSAVE0.SAV",
    "gen-rpawn-02": "DKSAVE0.SAV",
    "gen-rstable-01": "DKSAVE0.SAV",
    "gen-rstable-02": "DKSAVE0.SAV",
    "gen-rcache-01": "Dksave10.sav",
    "gen-rcache-02": "Dksave10.sav",
    "gen-rroster-01": "DKSAVE0.SAV",
    "gen-rroster-02": "DKSAVE0.SAV",
    "gen-rroster2-01": "DKSAVE0.SAV",
    "gen-rroster2-02": "DKSAVE0.SAV",
    "gen-rcreate-01": "DKSAVE8.SAV",
    "gen-rcreate-02": "DKSAVE8.SAV",
    "gen-rstay-01": "Dksave10.sav",
    "gen-rstay-02": "Dksave10.sav",
    "gen-rwild-01": "DKSAVE99.SAV",
    "gen-rwild-02": "DKSAVE99.SAV",
    "gen-rsaints-01": "Dksave10.sav",
    "gen-rsaints-02": "Dksave10.sav",
    "gen-rbench-01": "Dksave10.sav",
    "gen-rbench-02": "Dksave10.sav",
    "gen-rgate-01": "DKSAVE0.SAV",
    "gen-rgate-02": "DKSAVE0.SAV",
    # res5: a card fight lost with everybody on the floor dead while a member left Away keeps the party alive (battle result 5 reaches the card)
    "res5-footpads": "synth/DKSAVE0-away24.sav",
    "res5-ChasedByTheCityGuard": "synth/DKSAVE0-away7a.sav",
    "res5-MarketAtNight": "synth/DKSAVE0-away16.sav",
    # res5b: the same at RebelMobInTheStreets, WolfPackOnTheTrail and WildBoarsCharge (local/batch/res5b)
    "res5-rebelmob": "synth/DKSAVE0-away7b.sav",
    "res5-WolfPackOnTheTrail": "synth/DKSAVE0-awaya0.sav",
    "res5-WildBoarsCharge": "synth/DKSAVE0-awaya5.sav",
    # b1-begin
    "gen-bdead-01": "synth/DKSAVE0-dead.sav",
    "gen-bdead-03": "synth/DKSAVE0-dead.sav",
    "gen-bdead-04": "synth/DKSAVE0-dead.sav",
    "gen-bdead-05": "synth/DKSAVE0-dead.sav",
    "gen-bgater-02": "DKSAVE0.SAV",
    "gen-bgater-03": "DKSAVE0.SAV",
    "gen-bgater-05": "DKSAVE0.SAV",
    "gen-bheld-21": "DKSAVE0.SAV",
    "gen-bheld-23": "DKSAVE0.SAV",
    "gen-bheld-24": "DKSAVE0.SAV",
    "gen-bheld-25": "DKSAVE0.SAV",
    "gen-bheld-31": "DKSAVE0.SAV",
    "gen-bheld-33": "DKSAVE0.SAV",
    "gen-bheld-37": "DKSAVE0.SAV",
    "gen-bheld-38": "DKSAVE0.SAV",
    "gen-bheld-39": "DKSAVE0.SAV",
    "gen-bheld-41": "DKSAVE0.SAV",
    "gen-bheld-42": "DKSAVE0.SAV",
    "gen-bheld-43": "DKSAVE0.SAV",
    "gen-bheld-44": "DKSAVE0.SAV",
    "gen-bheld-46": "DKSAVE0.SAV",
    "gen-bheld-47": "DKSAVE0.SAV",
    "gen-bheld-48": "DKSAVE0.SAV",
    "gen-bheld-50": "DKSAVE0.SAV",
    "gen-bhurt-02": "synth/DKSAVE0-hurt.sav",
    "gen-bhurt-03": "synth/DKSAVE0-hurt.sav",
    "gen-bhurt-05": "synth/DKSAVE0-hurt.sav",
    "gen-bhurta-01": "synth/DKSAVE0-hurtattr.sav",
    "gen-bhurta-05": "synth/DKSAVE0-hurtattr.sav",
    "gen-bhurtk-03": "synth/DKSAVE0-hurt.sav",
    "gen-bhurto-01": "synth/DKSAVE0-hurt.sav",
    "gen-bhurto-02": "synth/DKSAVE0-hurt.sav",
    "gen-bhurto-03": "synth/DKSAVE0-hurt.sav",
    "gen-bhurto-04": "synth/DKSAVE0-hurt.sav",
    "gen-bhurto-05": "synth/DKSAVE0-hurt.sav",
    "gen-bhurts-03": "synth/DKSAVE0-hurtstatus.sav",
    "gen-bhurts-05": "synth/DKSAVE0-hurtstatus.sav",
    "gen-bkeys-01": "DKSAVE0.SAV",
    "gen-bkeys-14": "DKSAVE0.SAV",
    "gen-bkeys-22": "DKSAVE0.SAV",
    "gen-bkeys-23": "DKSAVE0.SAV",
    "gen-bkeys-24": "DKSAVE0.SAV",
    "gen-bkeys-26": "DKSAVE0.SAV",
    "gen-bkeys-28": "DKSAVE0.SAV",
    "gen-bkeys-31": "DKSAVE0.SAV",
    "gen-bkeys-35": "DKSAVE0.SAV",
    "gen-bkeys-36": "DKSAVE0.SAV",
    "gen-bkeys-38": "DKSAVE0.SAV",
    "gen-bkeys-41": "DKSAVE0.SAV",
    "gen-bkeys-42": "DKSAVE0.SAV",
    "gen-bkeys-43": "DKSAVE0.SAV",
    "gen-bkeys-46": "DKSAVE0.SAV",
    "gen-bkeys-48": "DKSAVE0.SAV",
    "gen-bkeys-49": "DKSAVE0.SAV",
    "gen-bkeys-52": "DKSAVE0.SAV",
    "gen-bluer-03": "Dksave21.sav",
    "gen-bluer-04": "Dksave21.sav",
    "gen-bluer-05": "Dksave21.sav",
    "gen-bpra-01": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bpra-02": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bpra-03": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bpra-04": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bpra-05": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bprar-01": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bprar-02": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bprar-03": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bprar-04": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-bprar-05": "local/oracle-saves/DKSAVE40N.SAV",
    "gen-brun-01": "DKSAVE0.SAV",
    "gen-brun-03": "DKSAVE0.SAV",
    "gen-brun-06": "DKSAVE0.SAV",
    "gen-brun-08": "DKSAVE0.SAV",
    "gen-brun-09": "DKSAVE0.SAV",
    "gen-bsafe-01": "DKSAVE0.SAV",
    "gen-bsafe-04": "DKSAVE0.SAV",
    "gen-bsafe-22": "DKSAVE0.SAV",
    "gen-bsafe-24": "DKSAVE0.SAV",
    "gen-bsafe-26": "DKSAVE0.SAV",
    "gen-bsafe-28": "DKSAVE0.SAV",
    "gen-bsafe-31": "DKSAVE0.SAV",
    "gen-bsafe-34": "DKSAVE0.SAV",
    "gen-bsafe-36": "DKSAVE0.SAV",
    "gen-bsafe-37": "DKSAVE0.SAV",
    "gen-bsafe-41": "DKSAVE0.SAV",
    "gen-bsafe-42": "DKSAVE0.SAV",
    "gen-bsafe-43": "DKSAVE0.SAV",
    "gen-bsafe-44": "DKSAVE0.SAV",
    "gen-bsafe-45": "DKSAVE0.SAV",
    "gen-bsafe-48": "DKSAVE0.SAV",
    "gen-bsafe-49": "DKSAVE0.SAV",
    # b1-end
    # b1def-begin: the leader, Game-menu tick and Alt-D difficulty leads (battle leader = party leader, ticks in the battle's Game menu)
    "gen-bheld-45": "DKSAVE0.SAV",
    "gen-bheld-49": "DKSAVE0.SAV",
    "gen-bhurta-04": "synth/DKSAVE0-hurtattr.sav",
    "gen-bluer-02": "Dksave21.sav",
    "gen-brun-07": "DKSAVE0.SAV",
    "gen-bsafe-46": "DKSAVE0.SAV",
    "gen-bsafe-47": "DKSAVE0.SAV",
    "gen-bkeys-32": "DKSAVE0.SAV",
    "gen-bkeys-33": "DKSAVE0.SAV",
    "gen-bkeys-05": "DKSAVE0.SAV",
    # b1def-end
    # b1-hand-begin
    "battle-edge-1": "DKSAVE0.SAV",
    "battle-edge-2": "DKSAVE0.SAV",
    "battle-edge-3": "DKSAVE0.SAV",
    "battle-edge-4": "DKSAVE0.SAV",
    "battle-edge-5": "DKSAVE0.SAV",
    "battle-edge-6": "DKSAVE0.SAV",
    "battle-edge-7": "DKSAVE0.SAV",
    "battle-cursor-1": "DKSAVE0.SAV",
    "battle-cursor-2": "DKSAVE0.SAV",
    "battle-cursor-3": "DKSAVE0.SAV",
    "battle-cursor-4": "DKSAVE0.SAV",
    # b1-hand-end
    # b1t (tester follow-up of b1: the sessions of clusters A, B, C and J that agree after the script and compare-side fixes)
    "gen-bedge-01": "DKSAVE0.SAV",
    "gen-bedge-04": "DKSAVE0.SAV",
    "gen-bheld-11": "DKSAVE0.SAV",
    "gen-bkeys-02": "DKSAVE0.SAV",
    "gen-bkeys-11": "DKSAVE0.SAV",
    "gen-bkeys-15": "DKSAVE0.SAV",
    "gen-bkeys-27": "DKSAVE0.SAV",
    "gen-bkeys-39": "DKSAVE0.SAV",
    "gen-bkeys-47": "DKSAVE0.SAV",
    "gen-bkeys-50": "DKSAVE0.SAV",
    "gen-blue-03": "Dksave21.sav",
    "gen-bheld-01": "DKSAVE0.SAV",
    "gen-bheld-04": "DKSAVE0.SAV",
    "gen-bheld-12": "DKSAVE0.SAV",
    "gen-bheld-13": "DKSAVE0.SAV",
    "gen-bkeys-16": "DKSAVE0.SAV",
    "gen-bsafe-02": "DKSAVE0.SAV",
    "gen-bsafe-08": "DKSAVE0.SAV",
    "gen-bheld-32": "DKSAVE0.SAV",
    "gen-bsafe-03": "DKSAVE0.SAV",
    "gen-bsafe-05": "DKSAVE0.SAV",
    "gen-bsafe-32": "DKSAVE0.SAV",
    "gen-bsafe-38": "DKSAVE0.SAV",
    "gen-bheld-34": "DKSAVE0.SAV",
})
# the sessions run with --battle-records (the combatant records of every battle pass compared: report.json's battle)
BATTLE_RECORDS = {"gen-bheld-01", "gen-bheld-04", "gen-bheld-12", "gen-bheld-13", "gen-bkeys-16", "gen-bsafe-02", "gen-bsafe-08", "gen-bheld-32", "gen-bsafe-03", "gen-bsafe-05", "gen-bsafe-32", "gen-bsafe-38", "gen-bheld-34", "gen-bedge-01", "gen-bedge-04", "gen-bheld-11", "gen-bkeys-02", "gen-bkeys-11", "gen-bkeys-15", "gen-bkeys-27", "gen-bkeys-39", "gen-bkeys-47", "gen-bkeys-50", "gen-blue-03", "orders-select", "orders-walk", "orders-missile", "orders-attack-a", "orders-attack-b", "orders-attack-b2", "orders-attack-v",
                  "orders-attacks", "orders-sheet", "orders-sheetclick", "orders-save", "orders-potion", "luebeck-potion", "orders-potion2", "orders-potion3", "orders-prague-missile",
                  "dungeon-mine-doors", "dungeon-mine-orders", "dungeon-mine-group",
                  "res5-footpads", "res5-ChasedByTheCityGuard", "res5-MarketAtNight", "res5-rebelmob", "res5-WolfPackOnTheTrail", "res5-WildBoarsCharge"}      # the minimal case of the sheet's allocation error (R031+0x0B22): ends, does not agree
SESSIONS["gen-bhurt-01"] = "synth/DKSAVE0-hurt.sav"
SESSIONS["gen-brun-02"] = "DKSAVE0.SAV"
BATTLE_RECORDS |= {"gen-bhurt-01", "gen-brun-02"}      # B1 follow-up b1h: agree with battle records on (local/batch/b1h)
SESSIONS.update({"gen-bheld-03": "DKSAVE0.SAV", "gen-bheld-26": "DKSAVE0.SAV", "gen-bheld-35": "DKSAVE0.SAV", "gen-bkeys-34": "DKSAVE0.SAV", "gen-bsafe-23": "DKSAVE0.SAV", "gen-bsafe-39": "DKSAVE0.SAV", "gen-bsafe-40": "DKSAVE0.SAV"})  # B1r: agree live with battle records on (local/batch/b1r)
BATTLE_RECORDS |= {"gen-bheld-03", "gen-bheld-26", "gen-bheld-35", "gen-bkeys-34", "gen-bsafe-23", "gen-bsafe-39", "gen-bsafe-40"}
SESSIONS.update({"gen-bdead-02": "synth/DKSAVE0-dead.sav", "gen-bgater-01": "DKSAVE0.SAV", "gen-bgater-04": "DKSAVE0.SAV", "gen-bheld-02": "DKSAVE0.SAV", "gen-bheld-36": "DKSAVE0.SAV", "gen-bhurt-04": "synth/DKSAVE0-hurt.sav", "gen-bhurta-02": "synth/DKSAVE0-hurtattr.sav", "gen-bhurtk-04": "synth/DKSAVE0-hurt.sav", "gen-bhurts-04": "synth/DKSAVE0-hurtstatus.sav", "gen-bkeys-03": "DKSAVE0.SAV", "gen-bkeys-04": "DKSAVE0.SAV", "gen-bkeys-06": "DKSAVE0.SAV", "gen-bkeys-40": "DKSAVE0.SAV", "gen-bkeys-45": "DKSAVE0.SAV", "gen-blue-01": "Dksave21.sav", "gen-brun-04": "DKSAVE0.SAV", "gen-brun-05": "DKSAVE0.SAV", "gen-brun-10": "DKSAVE0.SAV", "gen-bsafe-06": "DKSAVE0.SAV", "gen-bsafe-07": "DKSAVE0.SAV"})  # B1o2: agree with battle records on (local/batch/b1o2)
BATTLE_RECORDS |= {"gen-bdead-02", "gen-bgater-01", "gen-bgater-04", "gen-bheld-02", "gen-bheld-36", "gen-bhurt-04", "gen-bhurta-02", "gen-bhurtk-04", "gen-bhurts-04", "gen-bkeys-03", "gen-bkeys-04", "gen-bkeys-06", "gen-bkeys-40", "gen-bkeys-45", "gen-blue-01", "gen-brun-04", "gen-brun-05", "gen-brun-10", "gen-bsafe-06", "gen-bsafe-07"}
# the place notes of the party panel ("near a door") on the way to a mine's riddle door (local/batch/doormsg): a player's saved game from a
# bug report, kept under local/oracle-saves/dlp-issue2 and not in the repository (no licence to redistribute it)
SESSIONS["dungeon-mine-doormsg"] = "local/oracle-saves/dlp-issue2/DKSAVE16.SAV"
BATTLE_RECORDS |= {"dungeon-mine-doormsg"}
IGNORED = {"state", "file", "text state", "texts", "party seed", "bsv kept", "undefined memory", "in battle"}   # lockstep.APART
RESERVE_MB, JOB_MB = int(os.environ.get("DARKLANDS_RESERVE_MB", 1500)), int(os.environ.get("DARKLANDS_JOB_MB", 450))   # the 2026-10-27 memory rule: reserve 6000 and 1900 per oracle worker


def free_mb() -> int:
    if sys.platform == "win32":
        import ctypes

        class Status(ctypes.Structure):
            _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong), ("total", ctypes.c_ulonglong), ("avail", ctypes.c_ulonglong),
                        ("tpage", ctypes.c_ulonglong), ("apage", ctypes.c_ulonglong), ("tvirt", ctypes.c_ulonglong), ("avirt", ctypes.c_ulonglong),
                        ("ext", ctypes.c_ulonglong)]
        st = Status(length=ctypes.sizeof(Status))
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(st))
        return int(st.avail >> 20)
    for line in open("/proc/meminfo"):
        if line.startswith("MemAvailable"):
            return int(line.split()[1]) // 1024
    return 1 << 20


def resolve_save(name: str, saves: Path) -> Path:
    """A saved game of the game folder, or a normalised copy under local/ (made when missing)."""
    if name.startswith("synth/"):
        # a synthetic copy (synth_saves.py): synth/<source>-<variant>.sav is made from SAVES/<source> with the variant's edits
        target = LOCAL / "oracle-saves" / name
        if not target.exists():
            source, _, variant = name[len("synth/"):-len(".sav")].rpartition("-")
            subprocess.run([sys.executable, "-I", "-B", str(HERE / "synth_saves.py"), variant, str(saves / (source + ".sav")), str(target)], check=True)
        return target
    if not name.startswith("local/"):
        return saves / name
    target = LOCAL / name[len("local/"):]
    if not target.exists():
        if name not in NORMALISING:
            raise SystemExit("%s is missing: a saved game kept only under local/ (not in the repository); put it there or leave the session out" % target)
        source, script = NORMALISING[name]
        subprocess.run([sys.executable, "-I", "-B", str(LOCAL / script[len("local/"):]), str(saves / source), str(target)], check=False)
        if not target.exists():
            raise SystemExit("%s is missing and %s could not make it from SAVES/%s" % (target, script, source))
    return target


def verdict_fight(folder: Path, name: str):
    report = json.loads((folder / "report.json").read_text(encoding="utf-8"))[name]
    ok = report["clock"] is None
    where = "agrees over %d ticks" % report["ticks agreeing"] if ok else "clock %s after %d ticks: %s" % (
        report["clock"], report["ticks agreeing"], json.dumps(report["differences"][:1]))
    return ok, where, report["original run"], report["seconds"]


def verdict_session(folder: Path, name: str):
    report = json.loads((folder / "report.json").read_text(encoding="utf-8"))
    first = next((r for r in report["states"] if set(r) - IGNORED), None)
    where = "agrees" if first is None else "first divergence: %s" % (first.get("state") or first["file"])
    battle = report.get("battle")
    if battle and battle.get("clock") is not None:
        first, where = battle, "battle parts at clock %s%s" % (battle["clock"], "" if first is None else "; " + where)
    return first is None, where, report["original run"], report["seconds"]


# b2-session
SESSIONS["b2-einfo-a5"] = "synth/DKSAVE0-carda5.sav"
BATTLE_RECORDS.add("b2-einfo-a5")


# b2-session
SESSIONS["b2-march-keys"] = "synth/DKSAVE0-carda5.sav"
BATTLE_RECORDS.add("b2-march-keys")


# b2-session
SESSIONS["b2-march-mouse"] = "synth/DKSAVE0-carda5.sav"
BATTLE_RECORDS.add("b2-march-mouse")


# b2-session
SESSIONS["b2-einfo-a0"] = "synth/DKSAVE0-carda0.sav"
BATTLE_RECORDS.add("b2-einfo-a0")


# b2-session


# b2-session
SESSIONS["b2-einfo-7a"] = "synth/DKSAVE0-card7a.sav"
BATTLE_RECORDS.add("b2-einfo-7a")


# b2-session
SESSIONS["b2-einfo-16"] = "synth/DKSAVE0-card16.sav"
BATTLE_RECORDS.add("b2-einfo-16")


# b2-session
SESSIONS["b2-einfo-7b"] = "synth/DKSAVE0-card7b.sav"
BATTLE_RECORDS.add("b2-einfo-7b")


# b3-sessions: saves in a running battle (the dialogs' passes, the cursor keys' bytes), Alt-S refused in real combat, the probes of round B3 (local/batch/b3)
for _name in ("b3-save-fight", "b3-save-fight2", "b3-save-fight3", "b3-probe1", "b3-probe2", "b3-probe3"):
    SESSIONS[_name] = "DKSAVE99.SAV"
    BATTLE_RECORDS.add(_name)

















# scr1: scripted sweeps of the screens' open input paths (gen_scr1.py; local/batch/scr1)
SESSIONS.update({
    "scr1-camp-1": "Dksave10.sav",
    "scr1-camp-2": "DKSAVE99.SAV",
    "scr1-camp-3": "Dksave10.sav",
    "scr1-card-1": "DKSAVE0.SAV",
    "scr1-card-2": "DKSAVE0.SAV",
    "scr1-create-1": "DKSAVE8.SAV",
    "scr1-create-2": "DKSAVE8.SAV",
    "scr1-create-3": "DKSAVE8.SAV",
    "scr1-f6-1": "DKSAVE0.SAV",
    "scr1-f6-2": "DKSAVE0.SAV",
    "scr1-load-1": "DKSAVE0.SAV",
    "scr1-load-2": "DKSAVE0.SAV",
    "scr1-load-3": "DKSAVE0.SAV",
    "scr1-party-1": None,
    "scr1-party-2": None,
    "scr1-party-3": None,
    "scr1-party-4": None,
    "scr1-pray-1": "Dksave10.sav",
    "scr1-pray-2": "Dksave10.sav",
    "scr1-pray-3": "Dksave10.sav",
    "scr1-pray-4": "Dksave10.sav",
})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=os.cpu_count() or 4)
    ap.add_argument("--fights", default="all")
    ap.add_argument("--sessions", default="all")
    ap.add_argument("--no-coverage", action="store_true")
    ap.add_argument("--exe", default=str(GAME / "DARKLAND.EXE"))
    ap.add_argument("--game", default=str(GAME))
    ap.add_argument("--headless")
    ap.add_argument("--timeout", type=int, default=7200)
    import recording
    recording.add_arguments(ap)
    a = ap.parse_args()
    import battle_oracle
    fights = list(battle_oracle.FIGHTS) if a.fights == "all" else [] if a.fights == "none" else a.fights.split(",")
    sessions = sorted(SESSIONS) if a.sessions == "all" else [] if a.sessions == "none" else a.sessions.split(",")
    unknown = {p.stem for p in (HERE / "lockstep-sessions").glob("*.steps")} - set(SESSIONS)
    if unknown and a.sessions == "all":
        raise SystemExit("sessions without a saved game in SESSIONS: %s" % ", ".join(sorted(unknown)))
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    py = [sys.executable, "-I", "-B", "-u"]
    common = ["--recordings", a.recordings] + (["--live"] if a.live else []) + (["--replay-only"] if a.replay_only else []) + (["--no-snapshot"] if a.no_snapshot else []) \
        + (["--coverage"] if not a.no_coverage else []) + (["--headless", a.headless] if a.headless else [])
    saves = Path(a.game) / "SAVES"
    jobs = []       # (kind, name, command)
    for f in fights:
        jobs.append(("fight", f, py + [str(HERE / "battle_oracle.py"), a.exe, a.game, str(out / "fights" / f), "--save", str(saves / "DKSAVE0.SAV"),
                                       "--fights", f] + common))
    for s in sessions:
        save = ["--save", str(resolve_save(SESSIONS[s], saves))] if SESSIONS[s] else []
        jobs.append(("session", s, py + [str(HERE / "lockstep.py"), a.exe, a.game, str(HERE / "lockstep-sessions" / (s + ".steps")),
                                         str(out / "sessions" / s)] + save + (["--battle-records"] if s in BATTLE_RECORDS else []) + common))
    # the slowest jobs first (the last live durations kept in <recordings>/durations.json; a job never timed goes first, as it may be the slowest)
    durations_file = Path(a.recordings) / "durations.json"
    try:
        known = json.loads(durations_file.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        known = {}
    jobs.sort(key=lambda j: -known.get("%s/%s" % (j[0], j[1]), float("inf")))
    workers = max(1, min(a.workers, (free_mb() - RESERVE_MB) // JOB_MB))
    print("%d jobs, %d workers (%d MB free)" % (len(jobs), workers, free_mb()), flush=True)
    running, done, t0 = {}, [], time.time()
    pending = list(jobs)
    while pending or running:
        while pending and len(running) < workers and (not running or free_mb() - RESERVE_MB >= JOB_MB):
            kind, name, cmd = pending.pop(0)
            log = open(out / ("%s-%s.log" % (kind, name)), "w", encoding="utf-8")
            running[name] = (kind, subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT), log, time.time())
        time.sleep(1)
        for name, (kind, proc, log, started) in list(running.items()):
            late = time.time() - started > a.timeout
            if proc.poll() is None and not late:
                continue
            if late:
                proc.kill()
            log.close()
            del running[name]
            row = {"kind": kind, "name": name, "exit": proc.returncode, "wall": round(time.time() - started)}
            try:
                folder = out / ("fights" if kind == "fight" else "sessions") / name
                ok, where, how, seconds = (verdict_fight if kind == "fight" else verdict_session)(folder, name)
                row.update(agrees=ok, point=where, original=how, seconds=seconds)
            except Exception as e:
                row.update(agrees=False, point="no report (%s)" % type(e).__name__, original="?")
            done.append(row)
            print("%-8s %-16s %-9s %s [%s, %ds]" % (kind, name, "agrees" if row["agrees"] else "DIFFERS", row["point"][:140], row["original"], row["wall"]), flush=True)
    for row in done:
        if row["original"] == "live":
            known["%s/%s" % (row["kind"], row["name"])] = row["wall"]
    durations_file.parent.mkdir(parents=True, exist_ok=True)
    durations_file.write_text(json.dumps(dict(sorted(known.items())), indent=1), encoding="utf-8")
    summary = {"seconds": round(time.time() - t0), "workers": workers, "jobs": sorted(done, key=lambda r: (r["kind"], r["name"]))}
    (out / "summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print("%d jobs in %d s; %d agree, %d differ" % (len(done), summary["seconds"], sum(r["agrees"] for r in done), sum(not r["agrees"] for r in done)))


if __name__ == "__main__":
    main()
