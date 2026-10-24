"""Lockstep: one scripted session played by the ORIGINAL DARKLAND.EXE and by the C# rewrite, states compared.

usage: python -I -B lockstep.py <DARKLAND.EXE> <game folder> <script> <out folder> [--save <DKSAVEn.SAV>]
           [--headless <Darklands.Host.Headless.exe>] [--battle-trace] [--no-wall-draws] [--coverage] [--live] [--replay-only]

The sessions are in lockstep-sessions/ (each from the player's SAVES/DKSAVE0.SAV, given with --save).

The script holds the C# headless host's steps (src/Darklands.Host.Headless/Program.cs), one per line, `#` comments:
key:<c>|enter|esc|space|up|...|alt<c>|f<n>, move:<x>,<y>, click:<x>,<y>, option:<n> (the pointer onto line n of
the card menu shown), state:<name>, bwait:<n>; wait/shot steps are for the C# only.
bwait:<n> is for orders in a battle (the idle test below never passes where animated pieces draw random numbers, and a running battle is
never idle): the steps after it are given n + 1 battle passes after the previous one (the original) / after wait:<n - 3> frames (the C#
host, a frame being a pass there). A menu or a sheet (a loop that does not pass combat_input) is idle after IDLE_POLLS polls, as before
(each bwait waits its own polls there).
Keep the pointer off the battle view's edges (x < 68, y < 8 or > 191, x > 311): the original scrolls the view there, which the C# does not. State steps inside a battle do
not compare (the C# writes the party, the original the battle's save). --battle-records adds the combatant records to every pass. With --save both start from that saved game
(the script loads it from the start screen: key:t, key:l).

1. The original runs from its own start (R000+0x0022, command line -Q) in the DOS machine of dosmachine.py,
   extended here with what a whole session needs:
   * EMS through INT 67h (256 pages, the frame at D000; mapping copies pages in and out); the EMS functions'
     far pointer (root 05F8:000E) is the handler at once and the EMS check (05F8:0064) passes;
   * the root image loaded as DOS loads it (PSP 0813, image 0823, the EXE's relocation items applied: dosmachine.py), the vector table
     at 0000:0000 and the BIOS data area at 0040:0000 as on a DOS machine; the timer interrupt is raised at the PIT's programmed rate from the instruction count
     (BYTES_PER_MS bytes of code per millisecond), into the game's own handler (root 047C:01EB); the DOS time,
     the BIOS ticks and so time(0) follow the same count: every run is the same run;
   * no drivers: misc.exe is not loaded (root 047C:07CE, 047C:08C8 return) and no sound driver is chosen (no
     CONFIG.DRK: nsound.dlc); the slot table answers 0, except: keys (047C:073D peeks the key's code, 047C:0744
     takes it), DOS console input (INT 21h 0Bh/07h/08h, the load's question) and the mouse (INT 33h 3/5/6,
     DS:6EEC..6EFB) from the script (fn 5/6 give the pointer at that button's last press/release, as the driver does); the screen-save and picture buffers (047C:04D5, 047C:05BC/05AE) as real DOS
     blocks; fixed font metrics (6 x 8, as card_oracle), so menus are chosen by line (option:<n>);
   * the copy-protection question (R041+0x0000) passes at once, as the C# has it;
   * a step is given when the game is idle: IDLE_POLLS polls (key_pressed, or the menu bar's handler root 0000:0000
     every screen's loop calls) and IDLE_MS of emulated time with no random number drawn and the same card; a click
     holds the button CLICK_MS once the game has read it;
   * the battle: one call of the game's timer callback (047C:000A) at the head of each pass (R014+0x3166), the
     time-based timer kept out of the pass (the C# host then advances one 60 Hz tick a pass); the ticks each battle's
     set-up takes (combat_state_clear to the enemies' turn, then to the first pass: its loads run at the emulator's
     speed) are measured and given to the C# (DARKLANDS_BATTLE_SETUP_TICKS);
   * state:<name> runs the original's own saved-game writer (R037 1462:2ACE) on a copy of the machine;
   * every srand((unsigned)time(0)) (a call that follows `call time`) is recorded: the clock seeds; the other
     seeds (party seed + place, battle seed) are the game's own and the C# computes them.
2. The C# headless host plays the same steps with those clock seeds (clock:<n>,...), its state steps write the
   game being played through OriginalSaves.Write.
3. Per state: savdiff sections, the random generator's state (DS:7B20 / MscRandom.State) and the card; header
   word +0x84 (DS:E898, the card screen's text state) is reported apart (the C# writes the loaded file's). The
   saved games each wrote with Alt-S are compared too (the header's two texts apart), their battle files (.BSV) byte by byte.
   A pack entry the original filled from a heap address (G at the store's row -1: an item code past DARKLAND.LST's count where the C# has 0,
   DIVERGENCE D-75036a) is reported apart ("undefined memory"), not as a difference.
   The C#'s waits follow the original's passes where its tester counts time: the battle passes a click was held (click_holds) and the
   pass the first key of a battle's start pause came at (pause_keys).

Out folder: report.json, original.log (keys, clicks, cards, clock seeds, states with the Save/Load flags),
original-<state>.sav, cs/<state>.sav, original-file-*, cs-saves/, rewrite.log; with --battle-trace a line per
battle pass in original-battle.trace / rewrite-battle.trace (the combat clock DS:07D4 / Battle.Clock and the
random state; compare them by clock value: the start-of-battle pause lasts a different number of passes).
With --coverage the original's executed blocks and branch outcomes over the whole session go to <out>/cov/cov-0.jsonl (the
format of card_oracle's and battle_oracle's; block_coverage.py takes such a folder as a source), only if every state agrees.
The original side is recorded (recording.py): the first run stores what the comparison reads and the coverage in
local/oracle-recordings/, later runs run only the C# against it, if the SHA-256 of the EXE, the game files, the arguments and the
source of the tester modules are the same (else a live run, which replaces the recording). --live forces the live run,
--replay-only refuses one, --recordings DIR moves the folder. rerun_all.py runs everything with N workers. A full --live rerun
is made before every release and periodically.
The C# side has its own debugging aids: DARKLANDS_TRACE_MAP (each step of the map's marker),
DARKLANDS_RANDOM_TRACE (each seed and draw with its callers), DARKLANDS_STEP_TRACE (each combatant's step).
"""

from __future__ import annotations

import argparse
import struct
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
# the original side (the machine, the input, the clock, the hooks, the recorded run) lives in orig_side.py; this module is the C# run, the comparison and the reports
import orig_side  # noqa: E402
from orig_side import BWAIT_ACTUAL, ClockTrace, TraceList, key_code, run_original, stale_destinations  # noqa: E402,F401

BWAIT_FRAMES = 3                    # the C# host's frames a step costs beyond its wait (see run_rewrite)
# the hot keys whose message box (show_message root 0000:24BC, 0x3C ticks) holds the pass: Alt-M, Alt-F, Alt-C, Alt-D (outside the menu bar's
# keyboard mode, where root 0000:0DD2 takes them without acting); the C# host's frames of that box: its draw, the 1000 ms delay at 32 ms a
# frame (the host's pump and the delay's sleep), the restore
BOX_KEYS = ("altm", "altf", "altc", "altd")
BOX_FRAMES = 34
HEADLESS = HERE.parent.parent / "src" / "Darklands.Host.Headless" / "bin" / "Release" / "net10.0" / "Darklands.Host.Headless.exe"
clock_jumps = orig_side.clock_jumps
Session, SRAND, linear = orig_side.Session, orig_side.SRAND, orig_side.linear


def read_script(path: Path) -> list[tuple[str, str]]:
    out = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.split("#")[0].strip()
        if line:
            kind, _, value = line.partition(":")
            out.append((kind, value))
    return out


def trace_difference(original, rewrite) -> dict:
    """The first combat clock (first pass at each clock value) at which the random states of the two traces part, by clock."""
    def first(lines):
        d = {}
        for l in lines:
            w = l.split()
            d.setdefault(int(w[2]), w[4])
        return d
    a, b = first(original), first(rewrite)
    common = [c for c in a if c in b]
    for k, c in enumerate(common):
        if a[c] != b[c]:
            return {"clock": c, "ticks agreeing": k, "original": a[c], "rewrite": b[c]}
    return {"clock": None, "ticks agreeing": len(common), "clocks": {"original": len(a), "rewrite": len(b)}}


def after_dialog(steps, i) -> bool:
    """Step i is a bwait after the save's dialog (key:alts .. key:enter) or the load's (key:altl .. key:l), wait steps between."""
    keys = [s for s in steps[:i] if s[0] not in ("wait", "shot")][-4:]
    last = keys[-1] if keys else None
    if last == ("key", "enter"):
        return ("key", "alts") in keys
    if last == ("key", "l"):
        return ("key", "altl") in keys
    return False


def dialog_gaps(log, steps, logged: int) -> list[int]:
    """The battle passes from the last key read in a save or load dialog (R037+0x2A68, R037+0x8B76) to the next key the battle took, in
    order, when the bwaits after the dialogs are exactly those missing from BWAIT_ACTUAL (else none)."""
    expected = [i for i, s in enumerate(steps) if s[0] == "bwait" and after_dialog(steps, i)]
    if not expected or sum(1 for s in steps if s[0] == "bwait") - logged != len(expected):
        return []
    keys = []
    for l in log:
        if l.startswith("key ") and "(battle pass " in l:
            keys.append((int(l.split("(battle pass ")[1].split(",")[0]), l.rpartition(" in ")[2].split(" < ")[-1]))
    out = []
    for (p, where), (q, nxt) in zip(keys, keys[1:]):
        if where.startswith(("R037+0x2A68", "R037+0x8B76")) and not nxt.startswith(("R037+0x2A68", "R037+0x8B76")):
            out.append(q - p)
    return out if len(out) == len(expected) else []


def input_passes(log, steps) -> tuple[list[int], dict[int, tuple[int, int]]]:
    """(the indices of the script's input steps, {index of a key step: (battle pass, combat clock) of the original's log line}), the log's
    input lines taken in step order ({} when their count is not the steps')."""
    inputs = [i for i, s in enumerate(steps) if s[0] in ("key", "move", "click", "option")]
    lines = [l for l in log if l.split(" ", 1)[0] in ("key", "move", "click", "option") and " at " in l]
    passes = {}
    if len(inputs) == len(lines):
        for i, l in zip(inputs, lines):
            if l.startswith("key ") and "(battle pass " in l:
                p, c = l.split("(battle pass ")[1].split(")")[0].split(", clock ")
                passes[i] = (int(p), int(c))
    return inputs, passes


PAUSE_KEY_FRAMES = 4        # the C# host's battle passes from the key that starts a battle to a key after wait:<w> there: w + 4


def pause_keys(log, steps, trace) -> dict[int, int]:
    """{index of the last wait step before a battle's first key: the frames to add}: the original gives the first key of a battle's start pause
    after BATTLE_PAUSE_PASSES passes at the pause's clock (orig_side.py), so its pass follows the passes the set-up's ticks left before the
    pause (the clock the first pass has); the C# host gives it after its wait in frames. Only for a key the pause rule gave (the log's pass
    is the pause's first pass + BATTLE_PAUSE_PASSES), after only waits (shot / state steps) from the key that began the battle."""
    inputs, passes = input_passes(log, steps)
    clocks = [int(l.split()[2]) for l in trace if l.startswith("pass clock ")]
    out = {}
    for k, i in enumerate(inputs[1:], 1):
        before = inputs[k - 1]
        if i not in passes or before not in passes or passes[i][0] <= passes[before][0]:
            continue
        start, (at, clock) = passes[before][0], passes[i]
        between = steps[before + 1:i]
        if any(s[0] not in ("wait", "shot", "state") for s in between):
            continue
        waits = [before + 1 + j for j, s in enumerate(between) if s[0] == "wait"]
        first = next((j for j in range(start, min(at, len(clocks))) if clocks[j] == clock), None)
        if not waits or first is None or at - first != orig_side.BATTLE_PAUSE_PASSES:
            continue
        delta = at - (start + sum(int(steps[j][1]) for j in waits) + PAUSE_KEY_FRAMES)
        if delta and int(steps[waits[-1]][1]) + delta >= 0:
            out[waits[-1]] = delta
    return out


def click_holds(log, steps, bwait_actual) -> dict[int, int]:
    """{index of a bwait step after a click: the battle passes the original held that click}, for each battle click between two keys the log
    gives the battle pass of (key, bwait:a, click, bwait:b, key, wait / shot steps between, both bwaits waited in full). The original lets a
    click go CLICK_MS of emulated time after the game read the button and counts the bwait after it only then: the passes that hold spans
    follow how long a pass takes (what it draws), not a fixed count; the passes between the two keys are a + 1 + b + 1 + the hold, where the
    C# host gives a + 1 + b + 1."""
    inputs, passes = input_passes(log, steps)
    bwaits = [i for i, s in enumerate(steps) if s[0] == "bwait"]
    actual = dict(zip(bwaits, bwait_actual)) if len(bwaits) == len(bwait_actual) else {}
    out = {}
    for k, i in enumerate(inputs[1:-1], 1):
        if steps[i][0] != "click":
            continue
        before, after = inputs[k - 1], inputs[k + 1]
        if passes.get(before, (0,))[0] <= 0 or passes.get(after, (0,))[0] <= 0:
            continue
        a = [j for j in range(before + 1, i) if steps[j][0] not in ("wait", "shot")]
        b = [j for j in range(i + 1, after) if steps[j][0] not in ("wait", "shot")]
        if len(a) != 1 or len(b) != 1 or steps[a[0]][0] != "bwait" or steps[b[0]][0] != "bwait":
            continue
        n1, n2 = int(steps[a[0]][1]), int(steps[b[0]][1])
        if actual.get(a[0]) != n1 or actual.get(b[0]) != n2:
            continue
        extra = passes[after][0] - passes[before][0] - (n1 + 1) - (n2 + 1)
        if extra:
            out[b[0]] = extra
    return out


def resume_clocks(trace) -> list[int]:
    """The clock of the original's first battle pass after each fall of the combat clock in its trace: each battle after the first (a new
    one starts its clock again; one taken up from a saved game has the clock the restore R003+0x0CE8 read from the battle file, counted on by
    the timer while the restore and the set-up ran at the emulator's speed). Given to the C# for sessions that load in a battle (Alt-L)."""
    clocks = [int(l.split()[2]) for l in trace if l.startswith("pass clock ")]
    return [b for a, b in zip(clocks, clocks[1:]) if b < a]


def loot_ticks(jumps, log):
    """(jumps, [(clock, ticks)]): the clock jumps of the passes followed by a loot screen (a key read in R040 at clock c, the jump of the pass
    whose head had c - 1) without their ticks after the pass, those being given to the C# at the loot screen: the end test R014+0x08FE runs
    combat_recover R014+0x0DEE (each member's next recovery = the clock) before the loot screen, whose emulated time the timer counts."""
    at = set()
    for l in log:
        if l.startswith("key ") and "< R040+0x0000" in l and ", clock " in l:
            at.add(int(l.split(", clock ")[1].split(")")[0]))
    out, loot = [], []
    for j in jumps:
        if j[0] + 1 in at and j[1] > 0:
            loot.append((j[0] + 1, j[1]))
            at.discard(j[0] + 1)
            j = (j[0], 0) + tuple(j[2:])
        out.append(j)
    return out, loot


def sheet_callbacks(log) -> list[tuple[int, int]]:
    """[(during, after)] for each combat sheet of the original's log ("sheet callbacks 5917,53"): the timer callbacks that came while the sheet held
    the clock (R022+0x0066 .. 0x03C6) and after it up to the next battle pass; a recording made before these lines has none."""
    return [tuple(int(v) for v in l.split()[2].split(",")) for l in log if l.startswith("sheet callbacks ")]


def menu_callbacks(log) -> list[tuple[int, int]]:
    """[(during, after)] for each run of the menu bar in a battle, from the original's log ("menu callbacks 1420,54 (battle pass 310)"): root 0000:0000
    with the bar up holds the clock while DS:07D1 counts on; after: up to the next pass (or the next bar run)."""
    return [tuple(int(v) for v in l.split()[2].split(",")) for l in log if l.startswith("menu callbacks ")]


def run_rewrite(headless: Path, game: str, steps, save: Path | None, seeds, out: Path, battle_trace=False, set_up_ticks=(), clock_jumps=(), stale_dest=None,
                sheets=(), resume_clocks=(), loot=(), log=None, pause_shift=None, menus=()):
    """The session in the C# headless host: (states {name: (sav, random, card)}, its output)."""
    import shutil
    import subprocess
    saves = out / "cs-saves"
    shutil.rmtree(saves, ignore_errors=True)
    saves.mkdir(parents=True)
    if save:
        shutil.copy(save, saves / "DKSAVE0.SAV")
        if save.with_suffix(".BSV").exists():
            shutil.copy(save.with_suffix(".BSV"), saves / "DKSAVE0.BSV")
    shots = out / "cs"
    shutil.rmtree(shots, ignore_errors=True)
    args = [str(headless), game, str(shots), "clock:" + ",".join(str(v) for v in (seeds or [1])), "saves:" + str(saves)]
    # bwait:<n>: the original gives the step n + 1 battle passes after the previous one was taken; the C# host takes three frames more
    # than its wait (the key's two idle frames and the step's own), so the same pass is wait:<n - 3>
    seen = iter(BWAIT_ACTUAL)
    # a bwait right after the save's or the load's dialog (Alt-S .. Enter, Alt-L .. L) is taken by the dialog's console reads in the original
    # (no entry in BWAIT_ACTUAL): the next key there comes when the battle is idle; the C# waits the passes the original's log shows
    gaps = iter(dialog_gaps(log or (), steps, len(BWAIT_ACTUAL)))
    holds = click_holds(log or (), steps, BWAIT_ACTUAL)
    # a click costs the host two frames more than a key (the button stays down over two pumps before its two idle frames), a move one frame less
    for i, (k, v) in enumerate(steps):
        if k == "bwait":
            cost = BWAIT_FRAMES + (2 if i and steps[i - 1][0] in ("click", "rclick") else 0) - holds.get(i, 0)
            if i and steps[i - 1][0] == "move":
                # a move has no idle frames in the host (one frame, the step's own); the original takes the bwait at once at the poll after the move,
                # in the same pass (a key: at the poll after the get_key that takes it), so the next step comes n passes after the move: wait:<n - 2>
                cost -= 1
            if i >= 3 and steps[i - 1] == ("key", "esc") and steps[i - 3][0] == "key" and steps[i - 3][1] in ("f1", "f2", "f3", "f4", "f5"):
                cost -= 1                   # the sheet that Esc closes passes no battle pass in the frame it ends in
            if i and steps[i - 1][0] == "key" and steps[i - 1][1].lower() in BOX_KEYS:
                # the message box of the hot key (root 0000:24BC: a spin of 0x3C timer ticks inside the pass, no key read) is no battle pass in
                # the original, whose next step comes n passes after it; the C# host goes on taking steps over the box's frames, so the steps
                # after it would wait in the keyboard buffer and be read together (a key after F10 then taken by the battle, not the menu)
                cost -= BOX_FRAMES
            if after_dialog(steps, i) and (gap := next(gaps, None)) is not None:
                args.append("wait:%d" % max(0, gap - 1 - cost))
                continue
            actual = next(seen, int(v))
            later = 0
            if i and steps[i - 1] == ("key", "t") and actual < int(v):
                # T (the Throw list, R016+0x068A..0x06A1) only sets the bar's flags: the bar comes up at the next pass's root 0000:0000 and unfolds
                # over three of its loops (MenuBar.RunUnfolded), whose idle polls end the original's bwait; the C# host gives the next step one
                # frame later, once the bar has unfolded
                later = 1
            args.append("wait:%d" % (max(0, min(int(v), actual) - cost) + later))
        elif k == "wait" and pause_shift and i in pause_shift:
            args.append("wait:%d" % (int(v) + pause_shift[i]))
        else:
            args.append("%s:%s" % (k, v) if v else k)
    import os
    env = dict(os.environ, DARKLANDS_BATTLE_TICK_PER_PASS="1")   # one 60 Hz tick a battle pass, as the original is run here
    if battle_trace:
        env["DARKLANDS_BATTLE_TRACE"] = "records" if battle_trace == "records" else "1"
    if set_up_ticks:
        # the clock the original's battle set-ups took here (its loads run at the emulator's speed): the C#'s set-up takes none
        env["DARKLANDS_BATTLE_SETUP_TICKS"] = ";".join("%d,%d,%d,%d" % p for p in set_up_ticks)
    if clock_jumps:
        # the clock a long pass of the original took beyond its one tick (the timer's time-based callbacks): the C# has no emulated time
        env["DARKLANDS_BATTLE_CLOCK_JUMPS"] = ";".join(",".join(str(v) for v in p) for p in clock_jumps)
    if stale_dest:
        # the destination bytes a record keeps from the enemy file's stack bytes, by record (DARKLANDS_BATTLE_STALE_DEST)
        env["DARKLANDS_BATTLE_STALE_DEST"] = ";".join("%d:%d,%d" % p for p in stale_dest)
    if loot:
        # the ticks of the original's loot screens, by the clock it was opened at (applied when the C#'s opens)
        env["DARKLANDS_BATTLE_LOOT_TICKS"] = ";".join("%d,%d" % p for p in loot)
    if resume_clocks:
        # the clock of the original's first pass after each battle taken up from a saved game (its restore and set-up at the emulator's speed)
        env["DARKLANDS_BATTLE_RESUME_CLOCKS"] = ";".join(str(v) for v in resume_clocks)
    if sheets:
        # the timer callbacks of the original's combat sheets (DS:07D1 counts on while the sheet holds the clock): the C# host has no time there
        env["DARKLANDS_BATTLE_SHEET_CALLBACKS"] = ";".join("%d,%d" % p for p in sheets)
    if menus:
        # the same for the menu bar's runs in a battle (root 0000:0000 with the bar up): the C# host has no time there either
        env["DARKLANDS_BATTLE_MENU_CALLBACKS"] = ";".join("%d,%d" % p for p in menus)
    run = subprocess.run(args, capture_output=True, text=True, timeout=1800, env=env)
    states = {}
    for line in run.stdout.splitlines():
        if line.startswith("state ") and " card 0x" in line:
            name = line[6:line.index(":")]
            words = line.split()
            card, random = int(words[words.index("card") + 1], 16), words[words.index("random") + 1]
            states[name] = ((shots / (name + ".sav")).read_bytes(), int(random, 16) if random != "?" else None, card)
    return states, run.stdout + run.stderr


# the report keys of a state row that are not differences (reported apart)
APART = {"state", "file", "text state", "texts", "party seed", "bsv kept", "undefined memory"}
PACK_AT, PACK_ENTRY, PACK_ENTRIES = 0xAA, 6, 64     # a character record's pack (docs/SAVES.md): 64 entries {code word, type, quality, count, weight}


def heap_pack_entries(d: dict, osav: bytes, csav: bytes, items: int | None) -> dict:
    """Takes out of the savdiff d (in place) the pack entries the C# filled from the store table's zero entries before the table where the original
    read a heap address (DIVERGENCE D-75036a: G with the store's row at -1, R035+0x114A): an entry whose original item code is past the item list
    (DARKLAND.LST's count) and whose C# code is 0. {record: {offset: original byte}} of what was taken out."""
    import savdiff
    chars = d.get("chars")
    if not items or not isinstance(chars, dict):
        return {}
    a, b = savdiff.sections(osav)["chars"], savdiff.sections(csav)["chars"]
    out = {}
    for rec, offsets in list(chars.items()):
        r = int(rec)
        if r >= len(a) or r >= len(b):
            continue
        for k in range(PACK_ENTRIES):
            at = PACK_AT + PACK_ENTRY * k
            keys = [o for o in offsets if at <= int(o, 16) < at + PACK_ENTRY]
            if keys and struct.unpack_from("<H", a[r], at)[0] >= items and struct.unpack_from("<H", b[r], at)[0] == 0:
                out.setdefault(rec, {}).update({o: offsets.pop(o) for o in keys})
        if not offsets:
            del chars[rec]
    if not chars:
        del d["chars"]
    return out


def compare(steps, original: dict, rewrite: dict, fresh: bool = False, items: int | None = None) -> list[dict]:
    """Per state step in the script's order: the differences (savdiff sections, the random state, the card). items: DARKLAND.LST's count."""
    import savdiff
    rows = []
    for kind, name in steps:
        if kind != "state":
            continue
        row = {"state": name}
        if name not in original or name not in rewrite:
            row["missing"] = [side for side, d in (("original", original), ("rewrite", rewrite)) if name not in d]
        else:
            (osav, orand, ocard), (csav, crand, ccard) = original[name], rewrite[name]
            d = savdiff.diff(csav, osav)
            head = d.get("head", {}).get("0", {})
            # +0x84 (DS:E898, the card screen's text state): the C# keeps the loaded file's (a known divergence, see the report)
            if "20" in head or "21" in head:
                row["text state"] = {k: v for k, v in head.items() if k in ("20", "21")}
                head = {k: v for k, v in head.items() if k not in ("20", "21")}
                if head:
                    d["head"]["0"] = head
                else:
                    del d["head"]
            # +0x64 (party_seed): a game begun from the start screen (no saved game) takes it from the BIOS timer word at 0040:006C
            # (R037+0x9DB2, keys C and Q), a read of the original's time that the C# host (whose clock is the srand seeds) cannot repeat:
            # it is reported apart ("party seed"), not as a difference
            if fresh and "0" in d.get("head", {}) and ("0" in d["head"]["0"] or "1" in d["head"]["0"]):
                row["party seed"] = {k: v for k, v in d["head"]["0"].items() if k in ("0", "1")}
                rest = {k: v for k, v in d["head"]["0"].items() if k not in ("0", "1")}
                if rest:
                    d["head"]["0"] = rest
                else:
                    del d["head"]
            heap = heap_pack_entries(d, osav, csav, items)
            if heap:
                row["undefined memory"] = {"D-75036a": heap}
            if d:
                row["sav"] = d
            if orand != crand:
                row["random"] = {"original": "%08X" % orand, "rewrite": "%08X" % crand if crand is not None else None}
            if ocard != ccard:
                row["card"] = {"original": "%X" % ocard, "rewrite": "%X" % ccard}
        rows.append(row)
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("script")
    ap.add_argument("out")
    ap.add_argument("--save", help="a .SAV given to both as SAVES/DKSAVE0.SAV (with its .BSV)")
    ap.add_argument("--headless", default=str(HEADLESS))
    ap.add_argument("--budget", type=int, default=2_000_000_000)
    ap.add_argument("--battle-trace", action="store_true", help="original-battle.trace / rewrite-battle.trace: each battle pass")
    ap.add_argument("--battle-records", action="store_true", help="as --battle-trace, each pass line with the combatant records, queued damage and attack rates (battle_oracle.py's), compared by combat clock: report.json's battle")
    ap.add_argument("--no-wall-draws", action="store_true", help="experiment: R014+0x827A's random_n(2) leaves the generator as it was")
    ap.add_argument("--coverage", action="store_true", help="<out>/cov/cov-0.jsonl: the original's blocks and branch outcomes (kept only if all states agree)")
    import recording
    recording.add_arguments(ap)
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    steps = read_script(Path(a.script))
    save = Path(a.save) if a.save else None
    import time
    import recording
    if a.battle_records:
        a.battle_trace = True
    parts = {"kind": "session", "steps": steps, "budget": a.budget, "battle_trace": a.battle_trace, "battle_records": a.battle_records,
             "no_wall_draws": a.no_wall_draws, **recording.common_parts(Path(a.exe), a.game, ("orig_side",), save)}
    rec = recording.Recording(a.recordings, "sessions", Path(a.script).stem, parts, a.live, a.replay_only)
    got = rec.load(need_coverage=a.coverage)
    if got is None:
        snapshots = None
        if not a.no_snapshot and not a.no_wall_draws:
            sparts, n = orig_side.session_start(steps)
            # key_pressed looks ahead at the next step that is a key, a move, a click or an option (an Alt key is let through only at some of the map
            # loop's looks), so the state at the start point depends on it: it is part of the key
            ahead = next(((k, v) for k, v in list(steps)[n:] if k in orig_side.INPUT_KINDS), None)
            snapshots = recording.Snapshot(a.recordings, "session", {**{k: parts[k] for k in recording.COMMON_KEYS}, **sparts, "ahead": ahead, "coverage": a.coverage})
        got = orig_side.record_session(Path(a.exe), a.game, steps, save, a.budget, a.battle_records, a.no_wall_draws, a.coverage, snapshots)
        rec.store(got)
        original, seeds, log, end, files, set_up, trace = (got[k] for k in ("states", "seeds", "log", "end", "files", "set_up", "trace"))
        cov = got["cov"]
        replayed = False
    else:
        original, seeds, log, end, files, set_up, trace = (got[k] for k in ("states", "seeds", "log", "end", "files", "set_up", "trace"))
        cov = dict(got["cov"]) if a.coverage else None
        replayed = True
    jumps, loot = loot_ticks(got["jumps"], got["log"])
    BWAIT_ACTUAL[:] = got["bwait"]          # the passes each bwait of the original waited (run_original fills it on a live run)
    t_original = got["seconds"]
    if a.battle_trace:
        (out / "original-battle.trace").write_text(chr(10).join(trace), encoding="utf-8")
    (out / "original.log").write_text(chr(10).join(log + ["end: " + end]), encoding="utf-8")
    for name, (sav, _, _) in original.items():
        (out / ("original-%s.sav" % name)).write_bytes(sav)
    t = time.time()
    rewrite, output = run_rewrite(Path(a.headless), a.game, steps, save, seeds, out, "records" if a.battle_records else a.battle_trace, set_up, jumps,
                                  got["stale"] if a.battle_records else None, sheet_callbacks(log),
                                  resume_clocks(got["trace"]) if ("key", "altl") in steps else (), loot, log, pause_keys(log, steps, got["trace"]),
                                  menu_callbacks(log))
    battle = None
    if a.battle_trace:
        lines = [l for l in output.splitlines() if l.startswith("pass ")]
        (out / "rewrite-battle.trace").write_text(chr(10).join(lines), encoding="utf-8")
        if a.battle_records:
            import battle_oracle
            battle = battle_oracle.first_difference(battle_oracle.parse(trace), battle_oracle.parse(lines))
        else:
            battle = trace_difference(trace, lines)
    t_rewrite = time.time() - t
    (out / "rewrite.log").write_text(output, encoding="utf-8")
    lst = Path(a.game) / "DARKLAND.LST"
    rows = compare(steps, original, rewrite, save is None, lst.read_bytes()[0] if lst.is_file() else None)
    # the saved games each game wrote itself (Alt-S): the same names, the same bytes by savdiff section
    import savdiff
    for name, data in sorted(files.items()):
        (out / ("original-file-" + name)).write_bytes(data)
        theirs = out / "cs-saves" / name
        if not name.endswith((".SAV", ".BSV")):
            continue
        row = {"file": name}
        if not theirs.exists():
            row["missing"] = ["rewrite"]
        elif name.endswith(".BSV"):
            # the battle file (R003+0x00DC): byte by byte, by imploded block
            given = save.with_suffix(".BSV") if save and save.with_suffix(".BSV").exists() else None
            d = savdiff.bsv_diff(theirs.read_bytes(), data, given.read_bytes() if given else None)
            if "kept" in d:
                row["bsv kept"] = d.pop("kept")     # reported apart (savdiff.bsv_diff)
            if d:
                row["bsv"] = d
        else:
            d = savdiff.diff(theirs.read_bytes(), data)
            if d:
                row["sav"] = d
            if data[:0x64] != theirs.read_bytes()[:0x64]:
                row["texts"] = {"original": data[:0x64].hex(), "rewrite": theirs.read_bytes()[:0x64].hex()}
        rows.append(row)
    if cov is not None:
        if all(set(r) <= APART for r in rows):
            (out / "cov").mkdir(exist_ok=True)
            (out / "cov" / "cov-0.jsonl").write_text(json.dumps({"unit": Path(a.script).stem, **cov}, separators=(",", ":")) + "\n", encoding="utf-8")
        else:
            print("coverage not written: the states differ")
    if battle is not None:
        print("battle: " + json.dumps(battle)[:700])
    report = {"battle": battle, "script": a.script, "save": a.save, "clock seeds": seeds, "battle set-up ticks": set_up, "original end": end,
              "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)},
              "original run": "replayed" if replayed else "live", "states": rows}
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    first = next((r for r in rows if set(r) - APART), None)
    for r in rows:
        print("%-12s %s" % (r.get("state") or r["file"], "same" if set(r) <= APART else json.dumps(
            {k: v for k, v in r.items() if k not in ("state", "file")})[:300]))
    print("original: %s (%d s, %s); rewrite %d s; first divergence: %s" % (end, t_original, "replayed" if replayed else "live", t_rewrite,
                                                                       (first.get("state") or first["file"]) if first else "none"))


if __name__ == "__main__":
    main()
