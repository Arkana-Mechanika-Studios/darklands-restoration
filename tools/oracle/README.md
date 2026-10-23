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
The Shift/Ctrl/Alt flags (0040:0017) are 0 (nothing held) unless the script's `shift:on` is in force.

## Original side and compare side

The testers are split so that editing the comparison never expires a recording:

| Side | Files | In the recording key |
|---|---|---|
| original side: drives, hooks, stubs or feeds the emulated original, and decides what is recorded from it | `orig_side.py` (the Session: EMS, clock, keys and mouse from the script, the hooks and stubs, the recorded session run, `clock_jumps`, `stale_destinations`, the traces, the snapshots), `battle_orig.py` (the fight's run up to and through the battle; fights only), `dosmachine.py`, `run_unit.py`, whatever they import | yes |
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
