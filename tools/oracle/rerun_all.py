"""Run every fight (battle_oracle.FIGHTS) and every session (lockstep-sessions/*.steps) with N workers, each its own process.

usage: python -I -B rerun_all.py <out folder> [--workers N] [--fights a,b|none] [--sessions a,b|none] [--live] [--replay-only]
           [--no-coverage] [--recordings DIR] [--headless <Darklands.Host.Headless.exe>] [--stale-ok] [--exe E] [--game G]

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
from paths import local_dir  # noqa: E402
GAME = Path(os.environ.get("DARKLANDS_ROOT") or REPO.parent) / "game" / "DARKLAND"   # the project root: DARKLANDS_ROOT (needed from worktrees\<round>), else beside the code folder

# DKSAVE40 (written by another tool: party size 6, party slots whose status is 0) as a normalised copy under local/ (divergence D-72509c: the
# original'"'"'s writer R037+0x2ACE recounts the party and skips a status-0 slot): the copy is made by local/overnight/dk40/normalise.py when missing
NORMALISED = "local/oracle-saves/DKSAVE40N.SAV"
NORMALISING = {NORMALISED: ("DKSAVE40.SAV", "local/overnight/dk40/normalise.py")}
LOCAL = Path(local_dir(REPO))     # the git-ignored local/ folder (DARKLANDS_LOCAL, else DARKLANDS_ROOT/code/local, else REPO/local; no junctions)

# the saved game each session starts from (None: the start screen, no saved game)
SESSIONS = {"pressburg-map": "DKSAVE0.SAV", "tyrnau": "DKSAVE0.SAV", "residence": "DKSAVE0.SAV", "save-load": "DKSAVE0.SAV",
            "gate-battle": "DKSAVE0.SAV", "shop": "DKSAVE0.SAV", "cache": "Dksave10.sav", "cache-revisit": "Dksave10.sav", "saints": "Dksave10.sav",
            "stay": "Dksave10.sav", "sheet": "Dksave10.sav", "wildcamp": "DKSAVE17.SAV", "alchemy": "Dksave10.sav",
            "inventory": "Dksave10.sav", "chargen-party": None, "chargen-created": None, "notices": "DKSAVE0.SAV", "university": "DKSAVE0.SAV",
            "smiths": "DKSAVE0.SAV", "healer": "DKSAVE0.SAV", "churches": "DKSAVE0.SAV", "wharves": "DKSAVE0.SAV",
            "university-shop": "DKSAVE0.SAV",
            "stables": "DKSAVE0.SAV", "pawnshop": "DKSAVE0.SAV", "foreigntrader": "DKSAVE0.SAV", "banks": "DKSAVE0.SAV",
            "menubar": "DKSAVE0.SAV", "menubar2": "DKSAVE0.SAV", "menubar-kbright": "DKSAVE0.SAV", "roster": "DKSAVE0.SAV", "roster4": "DKSAVE0.SAV",
            "wien-rathaus": "DKSAVE0.SAV", "mapsave": "DKSAVE17.SAV",
            # the inn's roster screen (local/overnight/roster2): DKSAVE0 (five listed), DKSAVE8 (Hamburg, four rows), Dksave10 (Ulm, nine rows)
            "roster-colours": "DKSAVE0.SAV", "roster-moves": "DKSAVE0.SAV", "roster-wrap": "DKSAVE8.SAV", "roster-mouse": "DKSAVE8.SAV",
            "roster-create2": "DKSAVE8.SAV", "roster-ulm": "Dksave10.sav", "discordq-retire": "DKSAVE0.SAV",
            # the player's orders in a battle (local/overnight/orders): Prague's gate DKSAVE40, Luebeck's Dksave21, the others the gate of DKSAVE0
            "orders-select": "DKSAVE0.SAV", "orders-walk": "DKSAVE0.SAV", "orders-missile": "DKSAVE0.SAV", "orders-attack-a": "DKSAVE0.SAV",
            "orders-attack-b": "DKSAVE0.SAV", "orders-attack-b2": "DKSAVE0.SAV", "orders-attack-v": "DKSAVE0.SAV", "orders-attacks": "DKSAVE0.SAV",
            "orders-sheet": "DKSAVE0.SAV", "orders-sheetclick": "DKSAVE0.SAV", "orders-save": "DKSAVE0.SAV", "orders-potion": "Dksave21.sav", "luebeck-potion": "Dksave21.sav", "orders-cloud": "Dksave21.sav", "orders-potion2": NORMALISED,
            "orders-potion3": NORMALISED, "orders-prague-missile": NORMALISED,
            "sheet-dksave0": "DKSAVE0.SAV",
            # items dragged on the character sheet (ldown .. lup, drag_mouse.py): moved, readied, given, the stack given
            "sheet-drag": "Dksave10.sav",
            # the loot screen after the gate fight of DKSAVE0 (local/overnight/loot): keys, mouse, capital keys and Esc
            "loot-keys": "DKSAVE0.SAV", "loot-mouse": "DKSAVE0.SAV", "loot-esc": "DKSAVE0.SAV",
            # keys held through the keyboard interrupt (kdown / kup, DS:0880..0882) and Ctrl (local/batch/kbd): the map of DKSAVE17, the gate fight of DKSAVE0
            "kbd-map-held": "DKSAVE17.SAV", "kbd-map-enter": "DKSAVE17.SAV", "kbd-map-wait": "DKSAVE17.SAV",
            "tk-rest-first": "DKSAVE17.SAV", "tk-rest-second": "DKSAVE17.SAV", "tk-two-keys-rest": "DKSAVE17.SAV", "tk-two-keys-walk": "DKSAVE17.SAV",
            "kbd-battle-cursor": "DKSAVE0.SAV", "kbd-battle-held": "DKSAVE0.SAV", "kbd-battle-ctrl": "DKSAVE0.SAV",
            # the camp and the pack outside a battle (local/overnight/camp): DKSAVE99 and Dksave22 are parties on the map, DKSAVE0 is at the Pressburg inn
            "camp-wild-brew": "DKSAVE99.SAV", "camp-wild-risk": "Dksave22.sav", "camp-inn-broke": "DKSAVE0.SAV", "camp-rough": "DKSAVE0.SAV", "uj-hourm3": "synth/Dksave22-hourm3.sav", "uj-marchm1": "synth/DKSAVE0-marchm1.sav", "uj-relic1": "synth/DKSAVE0-relics.sav", "uj-formula1": "Dksave10.sav", "camp-mouse": "DKSAVE99.SAV",
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
                  "orders-attacks", "orders-sheet", "orders-sheetclick", "orders-save", "orders-potion", "luebeck-potion", "orders-cloud", "orders-potion2", "orders-potion3", "orders-prague-missile",
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
# b35: the riddle door's battle of the same save started, entered through the door and back, and started again after an Alt-L outside the
# battle, each with an in-battle Alt-S (the party's sizes and place tables, the level files, the chest offsets; local/batch/b35)
for _name in ("b35-door-start", "b35-door-back", "b35-door-reload"):
    SESSIONS[_name] = "local/oracle-saves/dlp-issue2/DKSAVE16.SAV"
    BATTLE_RECORDS.add(_name)
# lugum: the Templar fortress of Lugum (place 188 by Flensburg) from a 483.07 saved game at its graveyard card (kept under
# local/oracle-saves/lugum, not in the repository): the door in the wall, stairs and doors to the fortress's next levels, an in-battle Alt-S;
# split: one member alone down the stairs and back by Use Stairs, from the U key and from the Orders menu, and the keyboard menu there (F10,
# whose Right reads top-scoring-member as card-run's MEM.LOG query left it) (GitHub issue 3)
for _name in ("fortress-lugum-stairs", "fortress-lugum-back", "fortress-lugum-split", "fortress-lugum-split-menu", "fortress-lugum-split-f10",
              # running: U with the battle running after the lone member was selected and Space pressed (the leader goes through his
              # record number read on the level shown, OriginalBehaviour.FixLeaderStairsRecordOfShownLevel off); -leader: the leader alone
              "fortress-lugum-split-running", "fortress-lugum-split-running-leader"):
    SESSIONS[_name] = "local/oracle-saves/lugum/DKSAVE2.SAV"
    BATTLE_RECORDS.add(_name)
# issue3: the reporter's saved game on the travel map by night (kept under local/oracle-saves/issue3, not in the repository; every member's
# Stealth set to 99 so the night climb succeeds): the castle of Rotenburg climbed by night, its guards beaten, one member alone down the
# keep's stairs and back by Use Stairs from the U key and from the Orders menu (GitHub issue 3)
# reselect: the reporter's own keys, Use Stairs (U or the menu) and then another member selected before Space (Resume), his own order
# or the first member selected again: the order still goes, use-exit reading his record past the party level's count
for _name in ("castle-rotenburg-split", "castle-rotenburg-split-menu", "castle-rotenburg-split-reselect", "castle-rotenburg-split-reselect-menu",
              "castle-rotenburg-split-reselect-order", "castle-rotenburg-split-reselect-back", "castle-rotenburg-split-reselect-resume",
              # arrow: Shift and an arrow with the party split (the USE STAIRS notice, paused) before Use Stairs
              "castle-rotenburg-split-arrow", "castle-rotenburg-split-arrow-menu",
              # running: U with the battle running after the lone member was selected and Space pressed (with the right button held
              # between, -rbutton): the leader goes through his record number read on the level shown (FixLeaderStairsRecordOfShownLevel off)
              "castle-rotenburg-split-running", "castle-rotenburg-split-running-rbutton"):
    SESSIONS[_name] = "local/oracle-saves/issue3/DKSAVE6.SAV"
    BATTLE_RECORDS.add(_name)
# prefinal: the party-split audit's rows in lockstep (Faithful agreeing with the original; the DataSafe end states in
# docs/faithfulness/SUMMARY.md): Lugum's split (rows 3, 4, 5) and battles given by the request step (rows 6, 7; the pit, dart trap and
# chest routines of battlefield 0x88), and the request step's own probe
SESSIONS["fortress-lugum-split-door"] = "local/oracle-saves/lugum/DKSAVE2.SAV"
BATTLE_RECORDS.add("fortress-lugum-split-door")
SESSIONS["fortress-lugum-split-alarm"] = "local/oracle-saves/lugum/DKSAVE2.SAV"
BATTLE_RECORDS.add("fortress-lugum-split-alarm")
SESSIONS["fortress-lugum-split-walk"] = "local/oracle-saves/lugum/DKSAVE2.SAV"
BATTLE_RECORDS.add("fortress-lugum-split-walk")
SESSIONS["castle-request31-split-sight"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("castle-request31-split-sight")
SESSIONS["castle-request31-split-archers"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("castle-request31-split-archers")
SESSIONS["dungeon-request136-pit"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("dungeon-request136-pit")
SESSIONS["dungeon-request136-dart"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("dungeon-request136-dart")
SESSIONS["dungeon-request136-chest"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("dungeon-request136-chest")
SESSIONS["gate-request-castle124"] = "DKSAVE0.SAV"
BATTLE_RECORDS.add("gate-request-castle124")
# carry: two or more battles in one program run (gen_carry_sessions.py: wild, city, the mine; won, given up, abandoned by an Alt-L,
# taken up from a save made in the mine's battle), the per-battle state carried from one battle to the next. Only the seeds that agree are
# here (27, 28, 36 of 1..40; since round spawn 2, 4, 8, 15, 22, 24, 38, 40; since round reload 05, by its still:<n> wait for the
# start pause's keys, and 30, by the enemies' turn rotation; since round refight 7, by the map's terrain DS:D843 kept over a load, 6, 23
# and 3, ended by the generator at the road card their walk meets (ROAD_CARDS), with the map's vertical-step toggle DS:290E kept over a
# load, and 9, 16, 21, 31, by rounds spawn and reload together; since round bsvtimes 17, 18, 20, 26, 35, 37, by the tester's loot ticks,
# the clock wrapped by a save, the out-of-battle recovery words and a last load dialog; 10, 19, 39 by refight and bsvtimes together;
# since round timing 01, 11, 12, 13, 14, 25, 29, 32, 33, 34, by the timer callbacks timer_marks.py finds inside the regain, trigger-check,
# fighter-step and the pile filler: TIMER_MARKS below): all 40 seeds; the generator makes them again. carry-reload-footpads: ordercarry's varB without its script fault
CARRY = {
    "gen-carry-05": "synth/DKSAVE0-card24.sav",
    "carry-reload-footpads": "DKSAVE99.SAV",
    "gen-carry-27": "synth/DKSAVE0-card16.sav",
    "gen-carry-28": "DKSAVE99.SAV",
    "gen-carry-30": "DKSAVE0.SAV",
    "gen-carry-36": "synth/DKSAVE0-card24.sav",
    "gen-carry-02": "synth/DKSAVE99-card24.sav",
    "gen-carry-04": "synth/DKSAVE99-carda0.sav",
    "gen-carry-08": "synth/DKSAVE99-carda5.sav",
    "gen-carry-22": "synth/DKSAVE99-carda0.sav",
    "gen-carry-24": "synth/DKSAVE99-card24.sav",
    "gen-carry-38": "DKSAVE0.SAV",
    "gen-carry-40": "synth/DKSAVE0-card16.sav",
    "gen-carry-15": "synth/DKSAVE0-card16.sav",
    "gen-carry-03": "synth/DKSAVE99-carda5.sav",
    "gen-carry-06": "DKSAVE99.SAV",
    "gen-carry-07": "synth/DKSAVE99-carda5.sav",
    "gen-carry-09": "synth/DKSAVE0-card7a.sav",
    "gen-carry-16": "synth/DKSAVE0-card16.sav",
    "gen-carry-21": "DKSAVE0.SAV",
    "gen-carry-23": "DKSAVE99.SAV",
    "gen-carry-31": "synth/DKSAVE0-card7a.sav",
    "gen-carry-17": "DKSAVE99.SAV",
    "gen-carry-18": "synth/DKSAVE99-card24.sav",
    "gen-carry-20": "synth/DKSAVE99-carda5.sav",
    "gen-carry-26": "DKSAVE99.SAV",
    "gen-carry-35": "DKSAVE99.SAV",
    "gen-carry-37": "synth/DKSAVE99-card24.sav",
    "gen-carry-10": "synth/DKSAVE99-card24.sav",
    "gen-carry-19": "synth/DKSAVE99-carda5.sav",
    "gen-carry-39": "synth/DKSAVE99-card24.sav",
}
# the carry seeds whose original runs had a timer callback inside a stretch that reads the clock or the stack more than once (the endurance
# regain: 11, 13, 25, 34; trigger-check after the in-battle save: 12, 14, 29, 32; the mine's pile filler: 01, 33): run with
# lockstep.py --timer-marks (timer_marks.py), which gives the C# host where they landed
TIMER_MARKS = {"gen-carry-11", "gen-carry-13", "gen-carry-25", "gen-carry-34", "gen-carry-12", "gen-carry-14", "gen-carry-29", "gen-carry-32",
               "gen-carry-01", "gen-carry-33"}
CARRY.update({
    "gen-carry-11": "synth/DKSAVE99-carda0.sav",
    "gen-carry-13": "synth/DKSAVE99-carda0.sav",
    "gen-carry-25": "synth/DKSAVE99-carda5.sav",
    "gen-carry-34": "synth/DKSAVE99-carda0.sav",
    "gen-carry-12": "synth/DKSAVE99-carda0.sav",
    "gen-carry-14": "synth/DKSAVE99-card24.sav",
    "gen-carry-29": "synth/DKSAVE99-carda0.sav",
    "gen-carry-32": "synth/DKSAVE99-carda5.sav",
    "gen-carry-01": "synth/DKSAVE99-carda0.sav",
    "gen-carry-33": "synth/DKSAVE99-card24.sav",
})
SESSIONS.update(CARRY)
BATTLE_RECORDS |= set(CARRY)
# carry (round cards2's lead): a prayer on the creation's member sheet, then Begin the Adventure
SESSIONS["carry-create-pray"] = None
# round newgame: a new game saved before any battle and loaded back (the program's start battle variables and the folder's work files)
SESSIONS["tester-newgame-reload"] = None
# creation: three prayers with the most favour on the creation sheet (they succeed), then Begin the Adventure
SESSIONS["carry-create-pray3"] = None
# coverage: the event table full (synth_saves.py ev300): event-create / event-refresh / event-advance find no free slot (R029+0x2C4E / 0x2D5C / 0x2E74 -1)
SESSIONS.update({"ev300-inn": "synth/DKSAVE0-ev300.sav", "ev300-rough": "synth/DKSAVE0-ev300.sav", "ev300-pray": "synth/Dksave10-ev300.sav",
                 "ev300-camp": "synth/DKSAVE99-ev300.sav", "ev300-smiths": "synth/DKSAVE0-ev300.sav", "ev300-healer": "synth/DKSAVE0-ev300.sav"})
# evtable: quest 7's night travel on DKSAVE99 at 22h (synth_saves.py q7night*): event-advance R029+0x2E74 with no fact and a full table (-1),
# and its controls: the fact found in place on the full table (kept), the fact made in a free slot (free)
SESSIONS.update({"ev300-night": "synth/DKSAVE99-q7night.sav", "ev300-night-kept": "synth/DKSAVE99-q7nightkept.sav",
                 "ev300-night-free": "synth/DKSAVE99-q7nightfree.sav"})
# restart (item 4): sessions played in a copy of the game folder without the work files an earlier run of the original left there
# (TERRAIN.FIL, CFILE.DAT, LEVEL0..4.ENM): the battle-state save then packs none (raw size -1) and the restore R003+0x0CE8 takes its
# "no work file" arms (0x182A..0x1838 removes terrain.fil, 0x18E7..0x18F5 the loot file). The copy is made under local/oracle-games/clean when
# missing (clean_game); every other session keeps the default game folder, so their recordings stay valid.
SESSIONS["tester-clean-reload"] = None
CLEAN_GAME = {"tester-clean-reload"}
# restart (item 5): R030+0x0C44's key cases the tester can now give (ctrlf<n>, kp<n>, F9) and its own Alt-Q (the second key of a pass, tkey)
SESSIONS["tk-map-keys"] = "synth/DKSAVE17-out3.sav"
SESSIONS["tk-map-altq"] = "DKSAVE17.SAV"
# restmouse: a left button pressed inside the map's rest (R030+0x0F60 / 0x0F65, the loop's button test), at a timer tick (tclick, tick_mouse.py)
SESSIONS["tk-rest-click"] = "DKSAVE17.SAV"
# ctrlbreak: Ctrl-Break as the BIOS delivers it (kbreak, ctrl_break.py): key 0 into R030+0x0C44 after a walk (12E7 taken), the default
# case's step in place (R030+0x10AB) after a letter, and quit-check (root 0000:1338) in press-to-continue and card-choice-menu
SESSIONS.update({"kbreak-map-walk": "DKSAVE17.SAV", "tk-map-walk-key": "DKSAVE17.SAV", "kbreak-card-quit": "DKSAVE0.SAV",
                 "kbreak-menu-quit": "DKSAVE0.SAV"})
# gaps round (northpage): the map's backward paging with the row buffer's top past 0x41 (R030+0x0C44 11D8..1231), walked north over the sea
SESSIONS["map-page-north"] = "synth/DKSAVE17-sea.sav"
WORK_FILES = {"terrain.fil", "cfile.dat"} | {"level%d.enm" % i for i in range(5)}
NOT_GAME = {"docs", "convert", "logs", "qcap", "file_formats"}   # bulky folders of the project's copy that neither side reads


def clean_game(game: Path) -> Path:
    """The game folder copied without its work files (and the project's non-game folders), made once under local/oracle-games/clean."""
    import shutil
    target = LOCAL / "oracle-games" / "clean"
    if (target / "DARKLAND.EXE").is_file():
        return target
    staging = target.with_name("clean.part")
    if staging.exists():
        shutil.rmtree(staging)

    def ignore(folder, names):
        top = Path(folder) == game
        return [n for n in names if (top and (n.lower() in WORK_FILES or n.lower() in NOT_GAME))]
    shutil.copytree(game, staging, ignore=ignore)
    staging.rename(target)
    return target


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
# b32 (A4a, lead b3o): a refused Alt-S in a running real-combat fight and bwaits two in a row (local/batch/b32/a234)
SESSIONS["b32-refused-hotkey"] = "DKSAVE99.SAV"
BATTLE_RECORDS.add("b32-refused-hotkey")
# b32 (C-fix, U-96d507): F6 and Alt-O inside an F1 sheet, taken by hotkey-table root 0000:0CAA (local/batch/b32/c-fix)
SESSIONS["b32-sheet-hotkeys"] = "Dksave10.sav"
# b32 (A6): Enter on a card's saint line after a Shift hint on option 3 prays to the empty saint slot -1 (local/batch/b32/a6-saint);
# the saves: synth_saves.py card148 / card144 on SAVES/DKSAVE0.SAV
SESSIONS["b32-saint-stale-slot"] = "synth/DKSAVE0-card148.sav"
SESSIONS["b32-saint-stale-slot-fail"] = "synth/DKSAVE0-card144.sav"
SESSIONS["b32-saint-stale-slot-more"] = "synth/DKSAVE0-card144.sav"
# b32 (A7): the dwarf king's riddle door (wall alarm, card D4 -> E5) and the ladder it shows, the opened state saved, the stone box's door
# (furniture alarm, card E4) for contrast (local/batch/b32/a7-king); the saves: synth_saves.py king / stonebox on the original's DKSAVE2 of
# savemid-mine (kept as local/oracle-saves/synth/savemid-DKSAVE2.SAV/.BSV)
SESSIONS["dungeon-king-door"] = "local/oracle-saves/synth/DKSAVE2-king.SAV"
# b32 (A7-L5): the opened state loaded in a program just started, the leader down the ladder (the first tick ends the zeroed modifiers)
SESSIONS["dungeon-king-ladder"] = "local/oracle-saves/synth/DKSAVE1-kingopen.SAV"
SESSIONS["dungeon-king-opened"] = "local/oracle-saves/synth/DKSAVE2-king.SAV"
SESSIONS["dungeon-stonebox-door"] = "local/oracle-saves/synth/DKSAVE2-stonebox.SAV"


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


# scr2: pointer and key sweeps of the prayer page and the character generator (gen_scr2.py); kept: agree live and add open outcomes
SESSIONS.update({
    "scr2-create-1": "DKSAVE8.SAV",
    "scr2-create-2": "DKSAVE8.SAV",
    "scr2-create-3": "DKSAVE8.SAV",
    "scr2-create-4": "DKSAVE8.SAV",
    "scr2-create-5": "DKSAVE8.SAV",
    "scr2-create-6": "DKSAVE8.SAV",
    "scr2-pray-1": "Dksave10.sav",
    "scr2-pray-2": "Dksave10.sav",
    "scr2-pray-3": "Dksave10.sav",
    "scr2-pray-5": "Dksave10.sav",
    "scr2-pray-6": "Dksave10.sav",
    "scr2-pray-7": "Dksave10.sav",
    "scr2-pray-8": "Dksave10.sav",
})


# prefinal-cov: the alchemy list scrolled past its 17 rows (R033+0x1116), on a save whose members know all 66 formulas (synth_saves allformulas)
SESSIONS["alchemy-scroll"] = "synth/Dksave10-allformulas.sav"
SESSIONS["tk-map-walk-fkey"] = "DKSAVE17.SAV"   # prefinal-cov: F1..F5 / Ctrl-F1..F5 after a walked pass on the map (the ctrlbreak lead): agrees


# c3battle: the early B1 battle sessions left open, rerun on the current original side with the round's compare rules, and Space in a running
# battle (local/batch/c3battle)
SESSIONS.update({
    "c3-space-pause": "synth/DKSAVE0-away7b.sav",
    "gen-bedge-02": "DKSAVE0.SAV",
    "gen-bedge-03": "DKSAVE0.SAV",
    "gen-bheld-22": "DKSAVE0.SAV",
    "gen-bheld-40": "DKSAVE0.SAV",
    "gen-bhurta-03": "synth/DKSAVE0-hurtattr.sav",
    "gen-bhurtk-01": "synth/DKSAVE0-hurt.sav",
    "gen-bhurtk-02": "synth/DKSAVE0-hurt.sav",
    "gen-bhurtk-05": "synth/DKSAVE0-hurt.sav",
    "gen-bhurts-01": "synth/DKSAVE0-hurtstatus.sav",
    "gen-bhurts-02": "synth/DKSAVE0-hurtstatus.sav",
    "gen-bkeys-21": "DKSAVE0.SAV",
    "gen-bkeys-25": "DKSAVE0.SAV",
    "gen-bkeys-37": "DKSAVE0.SAV",
    "gen-bkeys-44": "DKSAVE0.SAV",
    "gen-bkeys-51": "DKSAVE0.SAV",
    "gen-blue-02": "Dksave21.sav",
    "gen-blue-04": "Dksave21.sav",
    "gen-blue-05": "Dksave21.sav",
    "gen-bluer-01": "Dksave21.sav",
    "gen-bsafe-27": "DKSAVE0.SAV",
    "gen-bsafe-33": "DKSAVE0.SAV",
    "gen-bsafe-35": "DKSAVE0.SAV",
    "gen-bsafe-50": "DKSAVE0.SAV",
})
BATTLE_RECORDS |= {"c3-space-pause", "gen-bedge-02", "gen-bedge-03", "gen-bheld-22", "gen-bheld-40", "gen-bhurta-03", "gen-bhurtk-01", "gen-bhurtk-02", "gen-bhurtk-05", "gen-bhurts-01", "gen-bhurts-02", "gen-bkeys-21", "gen-bkeys-25", "gen-bkeys-37", "gen-bkeys-44", "gen-bkeys-51", "gen-blue-02", "gen-blue-04", "gen-blue-05", "gen-bluer-01", "gen-bsafe-27", "gen-bsafe-33", "gen-bsafe-35", "gen-bsafe-50"}


# The travel-map sessions (86): the generated walks of the map (gen-trav*, round R2), the clamp walks (rc-m1, rc-m2: round batch 33), the view-edge and
# paging walks (b34-m4, m5, m6), the Ctrl-F-key and keypad walks (b35-d) and the three small probes L1, L2, L2b (rebuilt from the logged inputs of the
# lost originals, round pre-RC2). The party's cell of a b34 / rc / b35 start is DKSAVE17's with the header words +0x7E / +0x80 moved
# (synth_saves.py: pos<column>x<row>). TRAVEL_BUDGET: the walks that run longer than the default instruction budget of lockstep.py.
TRAVEL_BUDGET = 9_000_000_000
TRAVEL = {
    "gen-trav-01": "DKSAVE17.SAV", "gen-trav-02": "DKSAVE17.SAV", "gen-trav-03": "DKSAVE17.SAV", "gen-trav-04": "DKSAVE17.SAV",
    "gen-trav-05": "DKSAVE17.SAV", "gen-trav-06": "DKSAVE17.SAV", "gen-trav-07": "DKSAVE17.SAV", "gen-trav-08": "DKSAVE17.SAV",
    "gen-trav-09": "DKSAVE17.SAV", "gen-trav-10": "DKSAVE17.SAV", "gen-trav-11": "DKSAVE17.SAV", "gen-trav-12": "DKSAVE17.SAV",
    "gen-trav-13": "DKSAVE17.SAV", "gen-trav-14": "DKSAVE17.SAV", "gen-trav-15": "DKSAVE17.SAV", "gen-trav-16": "DKSAVE17.SAV",
    "gen-trav-17": "DKSAVE17.SAV", "gen-trav0-01": "DKSAVE0.SAV", "gen-trav0-02": "DKSAVE0.SAV", "gen-trav0-03": "DKSAVE0.SAV",
    "gen-trav0-04": "DKSAVE0.SAV", "gen-trav0-05": "DKSAVE0.SAV", "gen-trav0-06": "DKSAVE0.SAV", "gen-trav0-m5": "DKSAVE0.SAV",
    "gen-travn-01": "synth/DKSAVE17-month10d1.sav", "gen-travn-02": "synth/DKSAVE17-month10d1.sav", "gen-travn-03": "synth/DKSAVE17-month10d1.sav",
    "gen-travn-04": "synth/DKSAVE17-month10d1.sav", "gen-travs-01": "synth/DKSAVE17-month6.sav", "gen-travs-02": "synth/DKSAVE17-month6.sav",
    "gen-travs-03": "synth/DKSAVE17-month6.sav", "gen-travs-04": "synth/DKSAVE17-month6.sav", "gen-travw-01": "synth/DKSAVE17-month0d3.sav",
    "gen-travw-02": "synth/DKSAVE17-month0d3.sav", "gen-travw-03": "synth/DKSAVE17-month0d3.sav", "gen-travw-04": "synth/DKSAVE17-month0d3.sav",
    "gen-travw-05": "synth/DKSAVE17-month0d3.sav", "gen-travw-06": "synth/DKSAVE17-month0d3.sav", "rc-m2-down": "synth/DKSAVE17-pos216x490.sav",
    "rc-m2-up": "synth/DKSAVE17-pos216x490.sav", "rc-m2-sup": "synth/DKSAVE17-pos162x928.sav", "rc-m2-right": "synth/DKSAVE17-pos216x490.sav",
    "rc-m2-left": "synth/DKSAVE17-pos216x490.sav", "rc-m1-e-01": "synth/DKSAVE17-pos325x500.sav", "rc-m1-n-01": "synth/DKSAVE17-pos126x1.sav",
    "rc-m1-s-01": "synth/DKSAVE17-pos162x928.sav", "rc-m1-sw-01": "synth/DKSAVE17-pos0x925.sav", "rc-m1-se-01": "synth/DKSAVE17-pos325x925.sav",
    "rc-m1-ne-01": "synth/DKSAVE17-pos326x79.sav", "b34-m4-n-a-01": "synth/DKSAVE17-pos95x40.sav", "b34-m4-n-b-01": "synth/DKSAVE17-pos91x10.sav",
    "b34-m4-n-c-01": "synth/DKSAVE17-pos99x30.sav", "b34-m4-w-a-01": "synth/DKSAVE17-pos3x500.sav", "b34-m4-w-b-01": "synth/DKSAVE17-pos5x700.sav",
    "b34-m4-e-a-01": "synth/DKSAVE17-pos322x500.sav", "b34-m4-e-b-01": "synth/DKSAVE17-pos324x700.sav",
    "b34-m4-s-a-01": "synth/DKSAVE17-pos100x925.sav", "b34-m4-s-b-01": "synth/DKSAVE17-pos30x927.sav",
    "b34-m4-f1-a-01": "synth/DKSAVE17-pos100x199.sav", "b34-m4-f1-b-01": "synth/DKSAVE17-pos150x199.sav",
    "b34-m4-f2-a-01": "synth/DKSAVE17-pos100x830.sav", "b34-m4-f2-b-01": "synth/DKSAVE17-pos60x830.sav",
    "b34-m4-b1-a-01": "synth/DKSAVE17-pos290x150.sav", "b34-m4-b1-b-01": "synth/DKSAVE17-pos305x152.sav",
    "b34-m4-b2-a-01": "synth/DKSAVE17-pos280x130.sav", "b34-m4-b2-b-01": "synth/DKSAVE17-pos295x131.sav",
    "b34-m4-h-a-01": "synth/DKSAVE17-pos100x500.sav", "b34-m4-h-b-01": "synth/DKSAVE17-pos100x470.sav",
    "b34-m4-rest-01": "synth/DKSAVE17-pos100x500.sav", "b34-m5-w-430": "synth/DKSAVE17-pos3x430.sav", "b34-m5-w-546": "synth/DKSAVE17-pos3x546.sav",
    "b34-m5-w-610": "synth/DKSAVE17-pos3x610.sav", "b34-m5-w-777": "synth/DKSAVE17-pos3x777.sav", "b34-m5-n-90": "synth/DKSAVE17-pos90x30.sav",
    "b34-m5-n-92": "synth/DKSAVE17-pos92x12.sav", "b34-m5-n-93": "synth/DKSAVE17-pos93x20.sav", "b34-m6-pb-100": "synth/DKSAVE17-pos100x199.sav",
    "b34-m6-pb-150": "synth/DKSAVE17-pos150x199.sav", "b34-m6-pb-145": "synth/DKSAVE17-pos145x199.sav",
    "b35-d-ctrlf-01": "synth/DKSAVE17-pos95x40.sav", "b35-d-keypad-01": "synth/DKSAVE17-pos95x40.sav", "b35-d-mix-01": "synth/DKSAVE17-pos3x500.sav",
    "b35-d-ctrlf-02": "synth/DKSAVE17-pos3x500.sav", "L1-key-l-then-bottom-click": "DKSAVE17.SAV", "L2-bottom-click-pressburg": "DKSAVE0.SAV",
    "L2b-trav0-03-3clicks": "DKSAVE0.SAV",
}
TRAVEL_LONG = {
    "rc-m2-down", "rc-m2-up", "rc-m2-sup", "rc-m2-right", "rc-m2-left", "b34-m4-n-a-01", "b34-m4-n-b-01", "b34-m4-n-c-01", "b34-m4-w-a-01",
    "b34-m4-w-b-01", "b34-m4-e-a-01", "b34-m4-e-b-01", "b34-m4-s-a-01", "b34-m4-s-b-01", "b34-m4-f1-a-01", "b34-m4-f1-b-01", "b34-m4-f2-a-01",
    "b34-m4-f2-b-01", "b34-m4-b1-a-01", "b34-m4-b1-b-01", "b34-m4-b2-a-01", "b34-m4-b2-b-01", "b34-m4-h-a-01", "b34-m4-h-b-01", "b34-m4-rest-01",
    "b34-m5-w-430", "b34-m5-w-546", "b34-m5-w-610", "b34-m5-w-777", "b34-m5-n-90", "b34-m5-n-92", "b34-m5-n-93", "b34-m6-pb-100", "b34-m6-pb-150",
    "b34-m6-pb-145",
}
SESSIONS.update(TRAVEL)


def check_headless(given: Path | None, stale_ok: bool) -> None:
    """Prints which headless build the rewrite side runs, and stops when the build is older than this repository's sources.

    Without --headless the jobs run src/Darklands.Host.Headless/bin/Release (lockstep.HEADLESS), which no tool rebuilds: on 2026-11-06 a
    full replay of master ran a build of 2026-10-30 that way and 21 of 854 jobs differed, all of which agree with a fresh build. A build
    whose dlls are older than the newest source file under src/ is refused unless --stale-ok; a build outside this repository (a copy
    given with --headless) is only reported with its time, as its sources are not known here."""
    import lockstep
    exe = given or lockstep.HEADLESS
    if not exe.exists():
        raise SystemExit("no headless build at %s (dotnet build -c Release src/Darklands.Host.Headless)" % exe)
    dlls = list(exe.parent.glob("Darklands.*.dll"))
    built = min((p.stat().st_mtime for p in dlls), default=exe.stat().st_mtime)
    print("headless: %s (built %s)" % (exe, time.strftime("%Y-%m-%d %H:%M", time.localtime(built))), flush=True)
    src = REPO / "src"
    try:
        inside = exe.resolve().is_relative_to(src.resolve())
    except OSError:
        inside = False
    if not inside:
        return
    newest = max((p for ext in ("*.cs", "*.csproj") for p in src.rglob(ext) if "bin" not in p.parts and "obj" not in p.parts),
                 key=lambda p: p.stat().st_mtime, default=None)
    if newest is not None and newest.stat().st_mtime > built and not stale_ok:
        raise SystemExit("the headless build is older than %s (changed %s): rebuild it (dotnet build -c Release src/Darklands.Host.Headless) "
                         "or pass --stale-ok" % (newest.relative_to(REPO), time.strftime("%Y-%m-%d %H:%M", time.localtime(newest.stat().st_mtime))))


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
    ap.add_argument("--stale-ok", action="store_true", help="run with a headless build older than the sources beside it")
    import recording
    recording.add_arguments(ap)
    a = ap.parse_args()
    import battle_oracle
    fights = list(battle_oracle.FIGHTS) if a.fights == "all" else [] if a.fights == "none" else a.fights.split(",")
    sessions = sorted(SESSIONS) if a.sessions == "all" else [] if a.sessions == "none" else a.sessions.split(",")
    unknown = {p.stem for p in (HERE / "lockstep-sessions").glob("*.steps")} - set(SESSIONS)
    if unknown and a.sessions == "all":
        raise SystemExit("sessions without a saved game in SESSIONS: %s" % ", ".join(sorted(unknown)))
    check_headless(Path(a.headless) if a.headless else None, a.stale_ok)
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
        game = str(clean_game(Path(a.game))) if s in CLEAN_GAME else a.game
        jobs.append(("session", s, py + [str(HERE / "lockstep.py"), a.exe, game, str(HERE / "lockstep-sessions" / (s + ".steps")),
                                         str(out / "sessions" / s)] + save + (["--battle-records"] if s in BATTLE_RECORDS else []) + (["--timer-marks"] if s in TIMER_MARKS else [])
                                         + (["--budget", str(TRAVEL_BUDGET)] if s in TRAVEL_LONG else []) + common))
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
