# tools/oracle: recorded originals

`battle_oracle.py` (fights) and `lockstep.py` (sessions) play the original DARKLAND.EXE in the DOS machine (dosmachine.py)
and the C# rewrite and compare them. The original is deterministic and takes ~98% of the time, so its side is recorded
(recording.py): the first run stores what the comparison reads (the pass lines and clock marks of a fight; the saved games,
random state, card, written files, clock seeds, set-up ticks, log of a session) with the coverage under
`local/oracle-recordings/{fights,sessions}/` (git-ignored); later runs run only the C# and compare against it.

A recording is used only when the SHA-256 of all of these is unchanged, else the original is run live and the recording replaced:
the EXE; every game-folder file the original can read (not `*.tmp`, `*.log`, `SAVES/`); `docs/source-map/original-functions.csv`;
the steps (or the FIGHTS request words, passes, key pause) and the saved game bytes (.SAV and .BSV); the source of the
**original-side modules** (below); the Python, unicorn and capstone versions.

## The machine's memory layout

The original is loaded as DOS loads it (dosmachine.py): the PSP at 0813 and the root image at 0823 (`LOAD_SEG`; where DOSBox-X puts
DARKLAND.EXE: main MCB 0812, environment block 07C9), above a real vector table (0000:0000), BIOS data area (0040:0000), PSP and environment;
the EXE's relocation items are applied (the MZ table to the root, each overlay's and the data record's own items to its image, as the overlay
manager does). The notation's coordinates (root 0000:0CAA, R014+0x3166 = 0E76:3166, DS:7B20) are link-time segments: in the tools write
`P(0x0E76)` for the segment in memory (`DS` is already `P(0x321A)`), never the sum by hand; payload offsets (R014+0x....) do not move.
A hook or stub is given the segment in memory; the machine's own segments (EMS F800, its frame D000, the call sentinels F000/F100) and the
BIOS data area (0040) are absolute. The game's own writes to the vector table (INT 9 at 0000:0024, the divide-error vector) are ordinary.
The Shift/Ctrl/Alt flags (0040:0017) are 0 (nothing held) unless the script's `shift:on` (bits 0-1) or `ctrl:on` (bit 2) is in force.
The keys of `key:` steps go straight to the stubbed BIOS reads (047C:073D / 047C:0744 = misc.exe's INT 16h AH=1 / AH=0); `kdown:<hex>` /
`kup:<hex>` give a key's make / break bytes (set 1, `E0` prefix allowed) to the game's own INT 9 handler (root 044A:020E, through vector 9,
port 60h giving the byte), which keeps DS:0880..0889 (the held direction key, Enter): the overland map, the battle's keys and its order-mode
cursor read them. In a battle (after a bwait) they are delivered at the head of the next pass, where the C# host's frame starts.

## Original side and compare side

The testers are split so that editing the comparison never expires a recording:

| Side | Files | In the recording key |
|---|---|---|
| original side: drives, hooks, stubs or feeds the emulated original, and decides what is recorded from it | `orig_side.py` (the Session: EMS, clock, keys and mouse from the script, the hooks and stubs, the recorded session run, `clock_jumps`, `stale_destinations`, the traces, the snapshots), `fast_capture.py` (the state capture's match search, see below), `battle_orig.py` (the fight's run up to and through the battle; fights only), `dosmachine.py`, `run_unit.py`, whatever they import | yes |
| compare / C# side: runs the rewrite, compares, writes reports and verdicts | `lockstep.py` (`run_rewrite`, `compare`, the report), `battle_oracle.py` (`FIGHTS`, `first_difference`, the report), `savdiff.py`, `rerun_all.py` (the verdicts, the schedule), `diff_runs.py` | no |
| storage | `recording.py`, `rerun_all.py` | no (`FORMAT` is raised when what is stored changes) |

Rule: a compare-side module must never be imported from an original-side one (`recording.source_hashes` stops with an error if it is). New
code that touches the emulated original goes into the original side; code that reads a recording or the C# output goes into the compare
side. The inputs a recording already covers by value (the steps, a fight's request words, passes and key pause, the saved game) may be
edited freely on the compare side: they are hashed as data, not as code. A change to an original-side file expires every recording; a change
to a compare-side file expires none.

## Which runs to make

* A change to the C# only: `rerun_all.py <out>` in its default mode (the original replayed; live only for a recording whose key
  expired).
* A change to the tester's original side (`orig_side.py`, `battle_orig.py`, `dosmachine.py`, `run_unit.py`) or to a session's
  inputs (steps, saved game, a fight's request): the same default mode; the tool runs live exactly the runs whose keys expired.
* A full `--live` pass: only before a release, and as a periodic safety check (weekly, or after many original-side changes).
* Iterating on one lead: only the affected session or fight (`--sessions` / `--fights`).

**A full live pass is required before every release** (and periodically): `rerun_all.py <out> --live --workers 12`, then
`rerun_all.py <out2> --replay-only` and `diff_runs.py <out> <out2>` (no differences). The recording only says what the original did; the
live pass is the proof that it still does. `--live --no-snapshot` is the strictest form (every run from the beginning).

## Snapshots of the start point

A live run does not boot the original again: `recording.Snapshot` keeps the machine's state (memory, registers, the Session's own state, the
virtual file system) at a start point under a key of the same original-side sources and inputs plus the steps up to the point, and the next
live run puts it back and carries on from the same place of the same call loop (`Session.snapshot_state` / `restore_state` / `resume_state`,
`Machine._drive`). The points: for a session the first idle moment after the saved game is loaded (steps key t, key l; a script that starts at
the start screen: its first idle moment); for a fight the battle's entry R002+0x0000 (the same for every fight: the request and the passes
come after it). A from-snapshot run is byte-identical to a from-scratch one (verdicts, traces, coverage, reports). `--no-snapshot` starts
every live run from the beginning, and neither reads nor writes a snapshot. Files: `<recordings>/snapshots/{session,fight}-<key>.snap`.

- `--live`: run the original live and refresh the recording. `--replay-only`: never run the original. `--recordings DIR`. `--no-snapshot`.
- `rerun_all.py <out> [--workers N]`: every fight of FIGHTS and every session of lockstep-sessions, one process each, N workers (default the core count,
  bounded by free memory), the slowest first (the last live durations in `<recordings>/durations.json`); writes `<out>/summary.json`.
- `diff_runs.py <A> <B>`: two such output folders compared, ignoring timings and live-or-replayed.

## The state capture's match search (`fast_capture.py`)

A session's `state:` step runs the original's saved-game writer (R037+0x2ACE) on a copy of the machine that is thrown away afterwards
(`Session.capture`). Most of that time was the compressor's longest-match search R003+0x2264 (a hash-chain walk with `cmp`, `cmpsb` and
`repe cmpsb`, one translated block per compared byte). Inside a capture its blocks R003+0x2284 .. 0x24AF run in Python, block for block, with
what the emulation does at each of them: the same registers (EFLAGS as Unicorn computes it from the last flag-setting instruction) and memory
bytes, the same blocks in the same order (each `repe cmpsb` iteration a block), `count` (+ each block's bytes), `recent`, and the coverage's
instructions, conditional branches and (branch, next block) edges through the same `_decoded` entries and epoch. It gives the machine back
to the emulation at the block where a timer interrupt is due (the emulation's own block hook then takes it there, as before), where the
progress line or the budget falls, and at the blocks it does not model (the entry and exit around the stack switch R003+0x2264..0x2281 and
0x24BC..0x24CF, whose `mov ss` / `sti` decide the next block's shape, and the `int3` at R003+0x2450). Nothing the original does or when changes:
the emulated clock, the interrupts and their register frames, the saved games and the coverage are identical. For this the Session's
megabyte is mapped from a host buffer of its own (`Machine.direct_ram`, `self.ram`: the same memory to the emulation, read and written in
place by the model). `Session.fast_search = False` runs the search emulated as before (used to compare the two).

The Session's block hook (`Session._on_block`, called by Unicorn for every translated block) is one function: the Session's steps (timer
interrupt, the stock loop's stack, the click release, the progress line) and then Machine._on_block's and Session.valid's (`_block`,
`recent`, the loaded-code test, `count`, the budget, the coverage) in the same order, registered without the binding's two Python layers
(`Session._add_block_hook`: the same exception guard, the first exception kept and re-raised by `emu_start`).

## Direct calls of single battle functions

`battle_calls.py`, `battle_fuzz.py`, `battle_calls_run.py` and `battle_calls_measure.py` call one original battle function (an action, a path
search, an AI step, a door, the end test ...) straight from a machine state taken inside a whole fight, change the state's memory, and ask the
rewrite to make the same call from the same bytes. They are new, additional testers: no original-side module above is touched, so no recording
expires (they are not in any recording's key either).

* `battle_calls.py capture` plays fights of `battle_oracle.FIGHTS` (from the battle-entry snapshot of the recordings) with a Session subclass
  that copies the machine at the head of the asked passes. `State.call(coordinate, args, memory writes)` restores the state, calls the function
  by its link-time far address (a near function with a return to its own entry that a hook ends; a function of an overlay that is not resident
  through its thunk) and reports AX, the DS and far-segment bytes it wrote, the random generator's state and every draw, the coverage.
* `battle_fuzz.py` makes the inputs: coverage-guided (a run is kept when it reaches a block or branch outcome the counted coverage folders and
  the earlier runs did not), with changes to the combatant records' modelled fields, the battle's per-combatant tables and scalars, the level
  image, the paths, the missiles, whole scenarios (statuses, floors, results, times, doors ...) and `branch_flip.py`'s aimed values.
* `tests/Darklands.Battles.Tests/BattleCalls` (xUnit; `DARKLANDS_BATTLE_CALLS=<jobs.json.gz from battle_calls.py export>`): the rewrite's
  battle is started from the saved game and the fight's request, then set from the state's bytes (`ImageOverlay`, `TacticsOverlay`: each entry
  pairs a place of the original's memory with the rewrite's property), the call is made, and the result, the modelled memory and the random
  draws are compared. Differences are leads; jobs the rewrite cannot be asked about (state it does not carry, states the game never has) are
  reported as GAP / SKIP and are not credited.
* `battle_calls_run.py` runs the three steps; `battle_calls_measure.py` counts the open outcomes of `branches-missed.csv` the agreeing runs reach.

## What new coverage gains: `cov_gain.py`

`cov_gain.py --baseline <dir | branches-work> [--baseline ...] --new <cov dir> [--new ...] [--exclude <file of 'unit|state' lines>] [--open branches-missed.csv] [--out gained.json]`
lists the open items of `docs/faithfulness/branches-missed.csv` that the new cov folders reach and the baseline does not, per record and function.
Use it instead of an ad-hoc script: it streams every `cov-*.jsonl` (recursively) line by line into one merged bytearray/edge set per record
(about 60 MB for the whole 3.2 GB of `branches-work`, about 2 minutes), skips excluded runs, refuses to start under 2 GB free memory
(`--min-free-gb`) and aborts past 3 GB of its own memory (`--max-rss-gb`). The item mapping is `battle_calls_measure.Items.reached` (cfg.py, as block_coverage.py).
It is the only gain tool; there is no separate per-folder script and no helper folder to pass. For one folder's own hits give it as `--new`
with no `--baseline`; for the items only that folder reaches give the other folders as `--baseline`. (It replaces `measure_new_outcomes.py`,
which needed a git-ignored `cov.py`; on five folders the old and the new numbers were identical.)

## Sound requests

The game starts music and plays effects through one command, `sound-start(n)` (root 047C:078A: the driver's play slot; 1 stops the music, 2 / 3
pause and resume, 5 asks for the number last played, any other number is a tune or an effect), and `music_play(x)` (R043+0x09AE) in front of it
(`if (DS:01BF) sound-start(x); else sound-start(1)`). Both sides write them as trace lines, `music x` for a music_play request and `sound n` for each
driver command, so a tune an event starts (or fails to start) is a difference like any other:

* `card_oracle.py`: the stub of sound-start and a hook on music_play write the lines into the run's trace (cards-teardown, which ends with
  sound-start(1), is a stub that writes its line); the C# test side (`OracleAudio`, the `Music`, `SoundPlayer` and `MusicPlaying` hooks of
  `CardOracleTests.Run`, `GameSounds`) writes the same words. A corpus whose root has `"sound": true` holds them in its traces and is compared with
  them; an older corpus is compared without (`CardOracleTests.Compare`). `audio_rerun.py` reruns the original over the runs of an older corpus and
  keeps the sound lines (a sidecar, replayed by `CardSoundOracleTests`: `DARKLANDS_CARD_ORACLE_SOUNDS`, `DARKLANDS_CARD_ORACLE_SOUND_DIFFS`), or, with
  `--full`, the whole result of each run as a new corpus with the sound lines.
  Memory: a worker's emulated machine grows by about 35 MB of commit per card it has run (measured: 35 MB at start, 419 MB after 10 cards,
  so about 12 GB over all 365 cards in one long-lived worker, which ended the 2026-10-28 golden run with access violations and MemoryError).
  `--cards-per-worker N` (default 8; 0 = never) replaces a worker after N cards (`Pool(maxtasksperchild=N)`; with N=8 a worker peaks at
  about 350 MB). Results do not change (checked byte for byte on cards 1..10). `DARKLANDS_CARD_ORACLE_MEMLOG=<file>` appends `pid card commit-MB`
  after each card, for measuring.
* `lockstep.py` / `battle_oracle.py`: the original side (patch 06 of the queue; `orig_side.py`, `battle_orig.py`) logs `sound n at T ms in <callers>` and
  `music x at ...` after a `sound log on` line; the C# headless host writes `sound n` / `music x` with `DARKLANDS_SOUND_LOG=1` (set by `run_rewrite`).
  `sound_compare` compares both per segment between the script's state steps (`report.json` "sound", a `sound:` line); a recording made before the
  patch has no `sound log on` line and is reported as not recorded. The original's start-up `sound-start(7)` (R000+0x052A, after the driver is
  loaded) is left out: the C# host plays it when its sound device is made.

## Pictures of the original: `vis10.py`

The lockstep machine stubs every graphics-driver slot, so it draws nothing. `vis10.py` runs a session with a subclass of `orig_side.Session`
(its own file, so no recording's key changes and no recording is replaced: it runs live into its own recordings folder) that loads the game's
own MGRAPHIC.EXE through the game's loader (root 047C:07CE, with DOS function 4B03h implemented here) and installer (root 047C:08C8), leaves
those slots un-stubbed (misc.exe's keys and mouse, the sound driver and the key reads stay stubbed), tracks the VGA colour registers (ports
3C7/3C8/3C9, INT 10h AH=10h, read-back included, since the driver's fades read the colours they start from) and reads the 320x200 mode-13h
screen at A000:0000 at a script step. A script is an ordinary session script with `shot:<name>` (both sides save the screen at that step),
`series:<name>,<n>,<ms>` or `<name>,<n>,p<k>` (before a key / click: n frames every ms milliseconds or k battle passes) and, for the original
only, `shotsave:<name>` (the screen at the next file the game creates: a box shown while a save is written). `state:` steps must not be used:
the state capture's copy and restore breaks the machine that has the driver loaded. Files are written as 64000 palette indices followed by
the 256 colours (the C# host's `.idx` format); `vis10.py compose` makes the side-by-side PNG / GIF (3x, labelled), `vis10.py jobs` runs
`vis10-sessions/jobs.txt`, `vis10.py index notes.json` writes an index page. The original runs far slower than a real PC, so its times in
a GIF are not real times, and the C# host's frames are paired with it by pump or by battle pass, to about two of them.
