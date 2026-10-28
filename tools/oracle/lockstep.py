"""Lockstep: one scripted session played by the ORIGINAL DARKLAND.EXE and by the C# rewrite, states compared.

usage: python -I -B lockstep.py <DARKLAND.EXE> <game folder> <script> <out folder> [--save <DKSAVEn.SAV>]
           [--headless <Darklands.Host.Headless.exe>] [--battle-trace] [--no-wall-draws] [--coverage] [--live] [--replay-only]

The sessions are in lockstep-sessions/ (each from the player's SAVES/DKSAVE0.SAV, given with --save).

The script holds the C# headless host's steps (src/Darklands.Host.Headless/Program.cs), one per line, `#` comments:
key:<c>|enter|esc|space|up|...|alt<c>|f<n>, move:<x>,<y>, click:<x>,<y>, option:<n> (the pointer onto line n of
the card menu shown), state:<name>, bwait:<n>, shift:on|off, ctrl:on|off, kdown:<hex> / kup:<hex> (a key's make / break code through the
game's keyboard interrupt, root 044A:020E, which keeps DS:0880..0889: held direction keys and Enter; no buffered key, give a key step for that;
in a battle give them after a bwait: they reach the original at the head of the next pass); wait/shot steps are for the C# only.
bwait:<n> is for orders in a battle (the idle test below never passes where animated pieces draw random numbers, and a running battle is
never idle): the steps after it are given n + 1 battle passes after the previous one (the original) / after wait:<n - 3> frames (the C#
host, a frame being a pass there). A menu or a sheet (a loop that does not pass combat_input) is idle after IDLE_POLLS polls, as before
(each bwait waits its own polls there).
Keep the pointer off the battle view's edges (x < 68, y < 8 or > 191, x > 311): the original scrolls the view there, which the C# does not. State steps inside a battle do
not compare (the C# writes the party, the original the battle's save): a state the original wrote in a battle (the difficulty byte's bit 7) while
the C# host reports one running ("in-battle") compares the card only, and the session then writes no coverage. --battle-records adds the combatant records to every pass. With --save both start from that saved game
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
   * the copy-protection question (R041+0x0000) passes at once, as the C# has it, after the five draws it makes first (rand_mod 4, 20, 7, 7, 6,
     the original's own rand_mod run by the stub);
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
# frame (the host's pump and the delay's sleep), the restore; 33 (round B1 tier 2: with 34 the next step came one battle pass after the original's at
# 18 of 22 box keys in 14 sessions, the other four with another lag between)
BOX_KEYS = ("altm", "altf", "altc", "altd")
BOX_FRAMES = 33
RUP_FRAMES = 34     # the C# host's frames of the battle's menu bar after the right button held up is let go (round B1 tier 2: gen-bhurtk-04 aligned)
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
    # a clock only one side reached (its battle ended or stalled earlier) is a difference too: the shorter trace does not pass
    lone = sorted(set(a) ^ set(b))
    for k, c in enumerate(common):
        if lone and c > lone[0]:
            break
        if a[c] != b[c]:
            return {"clock": c, "ticks agreeing": k, "original": a[c], "rewrite": b[c]}
    if lone:
        return {"clock": lone[0], "ticks agreeing": sum(c < lone[0] for c in common), "original": a.get(lone[0], "not reached"),
                "rewrite": b.get(lone[0], "not reached"), "clocks": {"original": len(a), "rewrite": len(b)}}
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


def unwaited_bwaits(steps) -> tuple[list[tuple[str, str]], set[int]]:
    """The steps without the bwaits the original side never waits: orig_side's _next_key takes a bwait only when it heads the script at a
    poll; one that comes after a non-input step (shift / ctrl / state / wait / shot, taken together with the next input) is passed over with
    them and not logged (BWAIT_ACTUAL has no entry), so the next input comes at once. The C# host is given the same: such a bwait is left out
    (B22-L05: gen-bkeys-15's bwait:7 after shift:off shifted the next three, the hold of kdown:49 was given 2 passes instead of 45). A bwait
    after a save's or load's dialog is the same case and stays: dialog_gaps gives it the original's passes.
    Also {index in the steps kept: the bwait before a left-out one}: there the original gives the next input in the same poll as the modifier
    steps, at the bwait's end, which a short bwait (bwait:2 after a kup, then ctrl:off) cannot give the host its frames for (run_rewrite carries
    the overrun to the next bwait)."""
    left = {i for i, s in enumerate(steps)
            if s[0] == "bwait" and i and steps[i - 1][0] not in orig_side.INPUT_KINDS + ("bwait",) and not after_dialog(steps, i)}
    kept, carry = [], set()
    for i, s in enumerate(steps):
        if i in left:
            j = len(kept) - 1
            while j >= 0 and kept[j][0] != "bwait":
                j -= 1
            if j >= 0:
                carry.add(j)
            continue
        kept.append(s)
    return kept, carry


def waits_since_key(steps, i) -> int:
    """The frames of the wait steps between step i and the input step before it."""
    total = 0
    j = i - 1
    while j >= 0 and steps[j][0] in ("wait", "shot"):
        if steps[j][0] == "wait":
            total += int(steps[j][1])
        j -= 1
    return total


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


def after_bwait(steps, i) -> bool:
    """Step i follows a bwait (shift / ctrl steps between): a kdown / kup there reaches the original at the head of the next battle pass."""
    j = i - 1
    while j >= 0 and steps[j][0] in ("shift", "ctrl"):
        j -= 1
    return j >= 0 and steps[j][0] == "bwait"


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


def click_holds(log, steps, bwait_actual, lists=None) -> dict[int, int]:
    """{index of a bwait step after a click: the battle passes the original held that click}, for each battle click between two keys the log
    gives the battle pass of (key, bwait:a, click, bwait:b, key, wait / shot steps between, both bwaits waited in full). The original lets a
    click go CLICK_MS of emulated time after the game read the button and counts the bwait after it only then: the passes that hold spans
    follow how long a pass takes (what it draws), not a fixed count; the passes between the two keys are a + 1 + b + 1 + the hold, where the
    C# host gives a + 1 + b + 1. Pointer moves between the two keys (round B1 tester follow-up: the log gives a move no battle pass, so the
    neighbours of a click are the nearest keys) each count their bwait as n passes (the original takes it in the pass of the move; so after a kdown / kup, which also anchor the
    span: the log gives them a pass), a key's or a click's as n + 1; the hold is the rest, given to the bwait after the click (with several clicks between the keys, their holds together at the first one's)."""
    # the anchors are the keys and the make / break codes (kdown / kup) the log gives a battle pass; the pointer steps have none
    kinds = ("key", "kdown", "kup", "move", "click", "option")
    inputs = [i for i, s in enumerate(steps) if s[0] in kinds]
    lines = [l for l in log if l.split(" ", 1)[0] in kinds and " at " in l]
    passes = {}
    if len(inputs) == len(lines):
        for i, l in zip(inputs, lines):
            if l.split(" ", 1)[0] in ("key", "kdown", "kup") and "(battle pass " in l:
                passes[i] = (int(l.split("(battle pass ")[1].split(")")[0].split(",")[0]),)
    bwaits = [i for i, s in enumerate(steps) if s[0] == "bwait"]
    actual = dict(zip(bwaits, bwait_actual)) if len(bwaits) == len(bwait_actual) else {}
    out = {}
    # every input's battle pass where the log gives one (newer logs give the clicks and the pointer moves theirs too)
    every = {}
    if len(inputs) == len(lines):
        for i, l in zip(inputs, lines):
            if "(battle pass " in l:
                every[i] = int(l.split("(battle pass ")[1].split(")")[0].split(",")[0])
    keyed = [k for k, i in enumerate(inputs) if steps[i][0] in ("key", "kdown", "kup") and passes.get(i, (0,))[0] > 0]
    for k0, k1 in zip(keyed, keyed[1:]):
        before, after = inputs[k0], inputs[k1]
        middle = inputs[k0 + 1:k1]
        if any(steps[i][0] not in ("click", "move") for i in middle) or not any(steps[i][0] == "click" for i in middle):
            continue
        if all(i in every for i in middle):
            # the log gives each click and move its pass: the per-click rule below gives each click its own hold (counting the span's
            # holds here as well gave the clicks after the first one theirs twice: gen-bsafe-40, B22-L02)
            continue
        # the steps between (shift / ctrl / wait / shot steps take no pass): bwait, input, bwait, input ... bwait
        seq = [j for j in range(before + 1, after) if steps[j][0] not in ("wait", "shot", "shift", "ctrl")]
        if len(seq) != 2 * len(middle) + 1 or any(steps[j][0] != "bwait" for j in seq[0::2]) or seq[1::2] != middle:
            continue
        # (a key read in one of LIST_FRAMES' boxes: the C#'s wait after it already takes its frames, list_keys)
        expected, ok, held = -(lists or {}).get(before, 0), True, None
        for w, owner in zip(seq[0::2], [before] + middle):       # the bwait that follows each input
            n = int(steps[w][1])
            if actual.get(w) != n:
                ok = False
                break
            expected += n + (1 if steps[owner][0] in ("key", "click") else 0)
            if steps[owner][0] == "click" and held is None:
                held = w                # (the holds of several clicks between the keys are given together to the first one's bwait)
        if not ok or held is None:
            continue
        extra = passes[after][0] - passes[before][0] - expected
        if extra:
            out[held] = extra
    # a click that is not between two keys: the original's log gives the click and the pointer moves their own battle pass ("click x,y at .. ms
    # (battle pass N) in ..."; absent from older logs, then nothing is added). The hold is what the passes up to the next input that has a pass
    # take more than its bwait: bwait n after a click or key counts n + 1, after a move n.
    for k, i in enumerate(inputs[:-1]):
        if steps[i][0] != "click" or i not in every or k + 1 >= len(inputs):
            continue
        nxt = inputs[k + 1]
        if nxt not in every:
            continue
        seq = [j for j in range(i + 1, nxt) if steps[j][0] not in ("wait", "shot", "shift", "ctrl")]
        if len(seq) != 1 or steps[seq[0]][0] != "bwait" or actual.get(seq[0]) != int(steps[seq[0]][1]) or seq[0] in out:
            continue
        extra = every[nxt] - every[i] - (int(steps[seq[0]][1]) + 1)
        if extra:
            out[seq[0]] = extra
    return out


SHEET_OPEN_FRAMES = 2       # the C# host's battle passes from a click on the party panel to the pass its character sheet runs in (the button's two pumps)


def sheet_holds(log, steps, bwait_actual) -> dict[int, int]:
    """{index of the bwait step before a click: the passes to add} for each battle click that opens the character sheet (the party panel:
    the next key the log gives is read inside the sheet, R022, with the battle pass the sheet ran in), right after a key or a make / break
    code the log gives a battle pass (one bwait between, waited in full). The battle opens the sheet on the button's release
    (R014+0x031E's release count): the original's click is let go CLICK_MS after the game read it, so the sheet runs a varying number of
    passes after the click (as long as the passes of that hold take); the C# host's button is let go after its two pumps. The C# gives
    the click itself that many passes later, so that its sheet runs in the original's pass (round B1 tier 2: gen-bhurts-01, gen-bedge-02 ...)."""
    kinds = ("key", "kdown", "kup", "move", "click", "option")
    inputs = [i for i, s in enumerate(steps) if s[0] in kinds]
    lines = [l for l in log if l.split(" ", 1)[0] in kinds and " at " in l]
    if len(inputs) != len(lines):
        return {}
    bwaits = [i for i, s in enumerate(steps) if s[0] == "bwait"]
    actual = dict(zip(bwaits, bwait_actual)) if len(bwaits) == len(bwait_actual) else {}
    passes, in_sheet = {}, {}
    for i, l in zip(inputs, lines):
        if l.split(" ", 1)[0] in ("key", "kdown", "kup") and "(battle pass " in l:
            passes[i] = int(l.split("(battle pass ")[1].split(")")[0].split(",")[0])
            in_sheet[i] = "< R022+" in l
    out = {}
    for k in range(1, len(inputs) - 1):
        click, before = inputs[k], inputs[k - 1]
        if steps[click][0] != "click" or before not in passes or in_sheet[before]:
            continue
        # the sheet's first key: the next key the log gives a pass, read in R022, with only moves and bwaits between (the sheet takes them)
        nxt = next((j for j in inputs[k + 1:] if j in passes or steps[j][0] != "move"), None)
        if nxt is None or not in_sheet.get(nxt):
            continue
        seq = [j for j in range(before + 1, click) if steps[j][0] not in ("wait", "shot", "shift", "ctrl")]
        if len(seq) != 1 or steps[seq[0]][0] != "bwait" or actual.get(seq[0]) != int(steps[seq[0]][1]):
            continue
        at = passes[before] + int(steps[seq[0]][1]) + (1 if steps[before][0] == "key" else 0)
        extra = passes[nxt] - at - SHEET_OPEN_FRAMES
        if extra:
            out[seq[0]] = extra
    return out


LIST_FRAMES = {"R021+0x02CE": 1, "R022+0x03E4": -1}   # by the function that read the key: the frames the C# host's next step costs more


def list_keys(log, steps) -> dict[int, int]:
    """{index of a key step the original read in one of LIST_FRAMES' boxes over the battle (the key that closes it): its frames}, from its
    log (empty when the log's input lines are not the steps')."""
    inputs = [i for i, s in enumerate(steps) if s[0] in ("key", "move", "click", "option")]
    lines = [l for l in log if l.split(" ", 1)[0] in ("key", "move", "click", "option") and " at " in l]
    if len(inputs) != len(lines):
        return {}
    return {i: LIST_FRAMES[l.rstrip().split(" < ")[-1]] for i, l in zip(inputs, lines) if steps[i][0] == "key" and l.rstrip().split(" < ")[-1] in LIST_FRAMES}


def map_holds(log, steps) -> dict[int, int]:
    """{index of a click step: the map passes the original saw its left button down}, from the log's "maphold n" lines (written at the click's
    release when overland_loop R030+0x02DC found the button down in n passes; empty when the log's input lines are not the steps'). The
    original lets a click go CLICK_MS after the game read the button, which spans as many map passes as the emulator takes for them (a pass
    that steps and draws takes longer than one that stands); the target is aimed again at every pass that sees the button down, so after a
    blocked step (the target dropped, overland_loop's sel == 9999) a pass with the button still down walks on (round R2: L1, L2). The C#
    host holds the click for as many passes of its map loop (mapclick:x,y,n). The log's input lines are matched to the input steps in order
    (a run that ended early logs fewer); the matching stops at the first line whose kind is not its step's."""
    kinds = orig_side.INPUT_KINDS          # the steps the original logs as input lines ("<kind> <value> at ...")
    inputs = [i for i, s in enumerate(steps) if s[0] in kinds]
    out, k = {}, -1
    for l in log:
        if l.startswith("maphold "):
            if k >= 0 and steps[inputs[k]][0] == "click":
                out[inputs[k]] = int(l.split()[1])
        elif l.split(" ", 1)[0] in kinds and " at " in l:
            k += 1
            if k >= len(inputs) or steps[inputs[k]][0] != l.split(" ", 1)[0]:
                break
    return out


def rup_boxes(log) -> list[bool]:
    """For each release of the held right button (rup) in the original's log, in order: whether the menu bar's run it ended showed a message
    box (the next "menu callbacks" line counts show_message's 0x3C ticks)."""
    out, waiting = [], False
    for l in log:
        if l.startswith("rup "):
            if waiting:
                out.append(False)
            waiting = True
        elif l.startswith("menu callbacks ") and waiting:
            out.append(int(l.split()[2].split(",")[0]) == 0x3C)
            waiting = False
    if waiting:
        out.append(False)
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


def box_callbacks(log) -> list[tuple[int, int]]:
    """[(during, after)] for each hot key's message box in a battle, from the original's log ("box callbacks 13,16 (battle pass 746)" after the
    key line of Alt-D / F / M / C): the timer callbacks of show_message's wait and of the rest of its pass."""
    out, last = [], None
    for l in log:
        if l.startswith("key ") and "(battle pass " in l:
            last = l.split()[1]
        elif l.startswith("box callbacks ") and last in ("2000", "2100", "3200", "2E00"):
            out.append(tuple(int(v) for v in l.split()[2].split(",")))
            last = None
    return out


def menu_callbacks(log) -> list[tuple[int, int]]:
    """[(during, after)] for each run of the menu bar in a battle, from the original's log ("menu callbacks 1420,54 (battle pass 310)"): root 0000:0000
    with the bar up holds the clock while DS:07D1 counts on; after: up to the next pass (or the next bar run)."""
    return [tuple(int(v) for v in l.split()[2].split(",")) for l in log if l.startswith("menu callbacks ")]


def run_rewrite(headless: Path, game: str, steps, save: Path | None, seeds, out: Path, battle_trace=False, set_up_ticks=(), clock_jumps=(), stale_dest=None,
                sheets=(), resume_clocks=(), loot=(), log=None, pause_shift=None, menus=(), boxes=(), carry=(), ticks=None):
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
    if ticks is not None:
        args.append("ticks:%d" % ticks)
    # bwait:<n>: the original gives the step n + 1 battle passes after the previous one was taken; the C# host takes three frames more
    # than its wait (the key's two idle frames and the step's own), so the same pass is wait:<n - 3>
    seen = iter(BWAIT_ACTUAL)
    debt = 0
    # a bwait right after the save's or the load's dialog (Alt-S .. Enter, Alt-L .. L) is taken by the dialog's console reads in the original
    # (no entry in BWAIT_ACTUAL): the next key there comes when the battle is idle; the C# waits the passes the original's log shows
    gaps = iter(dialog_gaps(log or (), steps, len(BWAIT_ACTUAL)))
    in_list = list_keys(log or (), steps)
    holds = click_holds(log or (), steps, BWAIT_ACTUAL, in_list)
    if holds:
        print("click holds (bwait step: passes): %s" % holds)
    sheet_clicks = sheet_holds(log or (), steps, BWAIT_ACTUAL)
    boxed = iter(rup_boxes(log or ()))
    mapheld = map_holds(log or (), steps)
    if sheet_clicks:
        print("sheet click holds (bwait step: passes): %s" % sheet_clicks)
    # a click costs the host two frames more than a key (the button stays down over two pumps before its two idle frames), a move one frame less;
    # a battle right click does not (round B1 tier 2: with the two frames the next step came two battle passes before the original's at 17 of
    # 18 lone right clicks in 12 sessions; its menu bar's own time is DARKLANDS_BATTLE_MENU_CALLBACKS)
    for i, (k, v) in enumerate(steps):
        if k == "bwait":
            cost = BWAIT_FRAMES + (2 if i and steps[i - 1][0] == "click" else 0) - holds.get(i, 0) - sheet_clicks.get(i, 0)
            if i and steps[i - 1][0] in ("move", "kdown", "kup"):
                # a move has no idle frames in the host (one frame, the step's own); the original takes the bwait at once at the poll after the move,
                # in the same pass (a key: at the poll after the get_key that takes it), so the next step comes n passes after the move: wait:<n - 2>
                # (a key's make or break code, kdown / kup, likewise: the host feeds it to its keyboard interrupt model in the step's frame)
                cost -= 1
            if i and steps[i - 1][0] == "rup" and next(boxed, False):
                # the right button let go in a battle on an item whose message box shows (rdown .. rup; the original's bar run counted show_message's
                # 0x3C ticks, its "menu callbacks 60,n"): the C# host's box goes on over its frames before the battle's next pass, where the
                # original's next pass comes at once (the box's time is DARKLANDS_BATTLE_MENU_CALLBACKS)
                cost -= RUP_FRAMES
            if i and steps[i - 1][0] in ("kdown", "kup") and after_bwait(steps, i - 1):
                cost += 1                   # its frame came one later than the pass the original counts this bwait from (below)
            if i and i - 1 in in_list:
                # a key read in a box over the battle that it closes (the list of E, R021+0x02CE; the question of S, R022+0x03E4): the C# host's
                # next step comes a frame later after the list, a frame earlier after the question, than the original's (round B1 tier 2)
                cost += in_list[i - 1]
            if i >= 3 and steps[i - 1] == ("key", "esc") and steps[i - 3][0] == "key" and steps[i - 3][1] in ("f1", "f2", "f3", "f4", "f5"):
                cost -= 1                   # the sheet that Esc closes passes no battle pass in the frame it ends in
            if i and steps[i - 1][0] == "key" and steps[i - 1][1].lower() in BOX_KEYS and i - 1 not in in_list:
                # the message box of the hot key (root 0000:24BC: a spin of 0x3C timer ticks inside the pass, no key read) is no battle pass in
                # the original, whose next step comes n passes after it; the C# host goes on taking steps over the box's frames, so the steps
                # after it would wait in the keyboard buffer and be read together (a key after F10 then taken by the battle, not the menu)
                cost -= BOX_FRAMES          # (not for a key a list over the battle read and closed with: no box there, gen-bkeys-15)
            # the modifier steps right after the bwait (shift / ctrl, set before the next input) take no pass in the original, which takes them
            # with that input; the C# host spends a frame on each
            j = i + 1
            while j < len(steps) and steps[j][0] in ("shift", "ctrl"):
                cost += 1
                j += 1
            if j < len(steps) and steps[j][0] in ("kdown", "kup"):
                # a key's make or break code after a bwait reaches the original's keyboard interrupt at the head of the next battle pass
                # (orig_side.py, before that pass's cursor keys): the C# host feeds it at the start of its frame, one frame later
                cost -= 1
            if after_dialog(steps, i) and (gap := next(gaps, None)) is not None:
                # the gap counts every pass from the dialog's last key on. The wait steps between that key and this bwait (the script's
                # wait:300 after the save's Enter, wait:600 after the load's L) are frames the C# host runs as battle passes, where the
                # original spent them inside the dialog's file work; the key's own frames (BWAIT_FRAMES) are the dialog's, not battle passes
                # (round B3, b3-probe1: the next key came 297 passes late in a running battle, 3 early without the key's frames)
                args.append("wait:%d" % max(0, gap - 1 - cost + BWAIT_FRAMES - waits_since_key(steps, i)))
                continue
            actual = next(seen, int(v))
            later = 0
            if i and steps[i - 1] == ("key", "t") and actual < int(v):
                # T (the Throw list, R016+0x068A..0x06A1) only sets the bar's flags: the bar comes up at the next pass's root 0000:0000 and unfolds
                # over three of its loops (MenuBar.RunUnfolded), whose idle polls end the original's bwait; the C# host gives the next step one
                # frame later, once the bar has unfolded
                later = 1
            w = min(int(v), actual) - cost - debt
            # the frames a bwait before a left-out one overran (unwaited_bwaits) come off the next bwait (gen-bsafe-04: its steps came
            # one pass late per such bwait); elsewhere a bwait shorter than its cost is waited as 0 (the cost rules were set so)
            debt = max(0, -w) if i in carry else 0
            args.append("wait:%d" % (max(0, w) + later))
        elif k == "click" and i in mapheld:
            args.append("mapclick:%s,%d" % (v, mapheld[i]))
        elif k == "wait" and pause_shift and i in pause_shift:
            args.append("wait:%d" % (int(v) + pause_shift[i]))
        else:
            args.append("%s:%s" % (k, v) if v else k)
    import os
    env = dict(os.environ, DARKLANDS_BATTLE_TICK_PER_PASS="1")   # one 60 Hz tick a battle pass, as the original is run here
    env["DARKLANDS_SOUND_LOG"] = "1"                              # the host writes its music requests and driver commands (sound_compare)
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
    if boxes:
        # and for the hot keys' message boxes in a battle (show_message's wait of the timer's tick byte)
        env["DARKLANDS_BATTLE_BOX_CALLBACKS"] = ";".join("%d,%d" % p for p in boxes)
    run = subprocess.run(args, capture_output=True, text=True, timeout=1800, env=env)
    states = {}
    for line in run.stdout.splitlines():
        if line.startswith("state ") and " card 0x" in line:
            name = line[6:line.index(":")]
            words = line.split()
            card, random = int(words[words.index("card") + 1], 16), words[words.index("random") + 1]
            states[name] = ((shots / (name + ".sav")).read_bytes(), int(random, 16) if random != "?" else None, card, line.rstrip().endswith(" in-battle"))
    return states, run.stdout + run.stderr


# the report keys of a state row that are not differences (reported apart)
APART = {"state", "file", "text state", "texts", "party seed", "bsv kept", "undefined memory", "in battle"}
IN_BATTLE_BIT = 0x96, 0x80          # the saved game's difficulty byte, bit 7: written in a battle (R037+0x2B57, OriginalSaves InBattleBit)
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


def sound_lines(lines, rewrite: bool) -> list[tuple[str, str]]:
    """[(segment, "sound n" | "music x")] in order: the segment is the last "state <name>" step before the line ("start" before the first). The
    original's log (orig_side.py, patch 06) has "sound 6 at 1234 ms in f < g" / "music 6 at ..." and "state <name> at ..."; the C# host's output
    (DARKLANDS_SOUND_LOG) has "sound 6" / "music 6" and "state <name>: ..." in the same stream."""
    out, segment = [], "start"
    for l in lines:
        if l.startswith("state "):
            segment = l[6:].split(":" if rewrite else " ")[0]
        elif l.startswith(("sound ", "music ")) and not l.startswith("sound log"):
            words = l.split()
            out.append((segment, words[0] + " " + words[1]))
    return out


def sound_compare(log, output: str, original_cut: bool = False) -> dict:
    """The sound requests of the original (sound_play root 047C:078A, music_play R043+0x09AE) against the C# host's, per segment between the
    script's state steps. {"original": n, "rewrite": n, "agree": bool, "first": {...} | None, "segments": {name: {"original": [...], "rewrite": [...]}}
    for the segments that differ}; original_cut: the original stopped at its budget while the C# ran on (a fight): the C#'s requests after the
    original's last one are not compared; "recorded": False when the original's log has no "sound log on" line (a recording made before patch 06)."""
    theirs, mine = sound_lines(log, False), sound_lines(output.splitlines(), True)
    if theirs[:1] == [("start", "sound 7")]:
        # orig: R000+0x052A: sound_play(7) right after the driver is loaded at the program's start (the "ready" tune). The C# host plays it when its
        # SynthAudio is made (the driver start, SynthAudio ctor); the silent headless host has no driver, so there is nothing to compare
        theirs = theirs[1:]
    if original_cut and theirs:
        last = theirs[-1][0]
        kept, seen = [], 0
        for m in mine:
            if m[0] == last:
                if seen >= sum(1 for t in theirs if t[0] == last):
                    continue
                seen += 1
            kept.append(m)
        mine = kept
    names = list(dict.fromkeys([s for s, _ in theirs] + [s for s, _ in mine]))
    differ = {}
    for name in names:
        a, b = [l for s, l in theirs if s == name], [l for s, l in mine if s == name]
        if a != b:
            at = next((i for i in range(min(len(a), len(b))) if a[i] != b[i]), min(len(a), len(b)))
            differ[name] = {"at": at, "original": a, "rewrite": b}
    first = next(iter(differ), None)
    return {"recorded": "sound log on" in log, "original": len(theirs), "rewrite": len(mine), "agree": not differ,
            "first": None if first is None else {"segment": first, **{k: differ[first][k] for k in ("at",)},
                                                  "original": differ[first]["original"][max(0, differ[first]["at"] - 1):differ[first]["at"] + 3],
                                                  "rewrite": differ[first]["rewrite"][max(0, differ[first]["at"] - 1):differ[first]["at"] + 3]},
            "segments": differ}


def party_ticks(steps, original: dict, save) -> int | None:
    """The BIOS tick word the original took as the party seed (R037+0x9DB2, keys C and Q: DS:9C4A = the word at 0040:006C), which the C# host cannot
    read from the original's clock: the party seed (header +0x64) of the first saved game the original wrote, for a session that begins at the start
    screen (no --save). One word serves the session (the host repeats the last): a session that takes the party seed twice before its first state is
    given the second. None: no state, or a session that starts from a saved game."""
    if save is not None:
        return None
    name = next((n for k, n in steps if k == "state" and n in original), None)
    return None if name is None else struct.unpack_from("<H", original[name][0], 0x64)[0]


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
            (osav, orand, ocard), (csav, crand, ccard, *cfight) = original[name], rewrite[name]
            if osav[IN_BATTLE_BIT[0]] & IN_BATTLE_BIT[1] and cfight and cfight[0]:
                # taken inside a battle on both sides: the original's writer saves the battle (the combatant records, the events read back,
                # bit 7), the C# host's state step the party as it stands, and a running battle has no idle point where the two sides'
                # passes meet (its random state follows the passes): only the card is compared (round R2: gen-trav-16 and five more)
                row["in battle"] = "sav and random not compared"
                if ocard != ccard:
                    row["card"] = {"original": "%X" % ocard, "rewrite": "%X" % ccard}
                rows.append(row)
                continue
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
            # the host is given the word the first saved game holds (party_ticks, the ticks: step), and then the party seed is compared like
            # any field; only a session with no state to take the word from keeps it apart ("party seed"), not as a difference
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
    cs_steps, carry = unwaited_bwaits(steps)
    ticks = party_ticks(steps, original, save)
    rewrite, output = run_rewrite(Path(a.headless), a.game, cs_steps, save, seeds, out, "records" if a.battle_records else a.battle_trace, set_up, jumps,
                                  got["stale"] if a.battle_records else None, sheet_callbacks(log),
                                  resume_clocks(got["trace"]) if ("key", "altl") in steps else (), loot, log, pause_keys(log, cs_steps, got["trace"]),
                                  menu_callbacks(log), box_callbacks(log), carry, ticks)
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
    sound = sound_compare(log, output)
    lst = Path(a.game) / "DARKLAND.LST"
    rows = compare(steps, original, rewrite, save is None and ticks is None, lst.read_bytes()[0] if lst.is_file() else None)
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
        # a state taken inside a battle (not compared, "in battle") leaves the battle's passes unchecked: no coverage from the session
        if all(set(r) <= APART and "in battle" not in r for r in rows):
            (out / "cov").mkdir(exist_ok=True)
            (out / "cov" / "cov-0.jsonl").write_text(json.dumps({"unit": Path(a.script).stem, **cov}, separators=(",", ":")) + "\n", encoding="utf-8")
        else:
            print("coverage not written: the states differ")
    if battle is not None:
        print("battle: " + json.dumps(battle)[:700])
    report = {"battle": battle, "script": a.script, "save": a.save, "clock seeds": seeds, "battle set-up ticks": set_up, "original end": end,
              "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)},
              "original run": "replayed" if replayed else "live", "sound": sound, "states": rows}
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    first = next((r for r in rows if set(r) - APART), None)
    for r in rows:
        print("%-12s %s" % (r.get("state") or r["file"], "same" if set(r) <= APART else json.dumps(
            {k: v for k, v in r.items() if k not in ("state", "file")})[:300]))
    # the sound requests are a lead apart from the states (the verdict above is the states'); a recording made before patch 06 has none
    print("sound: %s" % ("not recorded by the original (a recording made before patch 06)" if not sound["recorded"] else
                         "%d requests of the original, %d of the rewrite, %s" % (sound["original"], sound["rewrite"], "agree" if sound["agree"] else
                                                                                 "first difference " + json.dumps(sound["first"])[:300])))
    print("original: %s (%d s, %s); rewrite %d s; first divergence: %s" % (end, t_original, "replayed" if replayed else "live", t_rewrite,
                                                                       (first.get("state") or first["file"]) if first else "none"))


if __name__ == "__main__":
    main()
