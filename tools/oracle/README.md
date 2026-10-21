# tools/oracle: recorded originals

`battle_oracle.py` (fights) and `lockstep.py` (sessions) play the original DARKLAND.EXE in the DOS machine (dosmachine.py)
and the C# rewrite and compare them. The original is deterministic and takes ~98% of the time, so its side is recorded
(recording.py): the first run stores what the comparison reads (the pass lines and clock marks of a fight; the saved games,
random state, card, written files, clock seeds, set-up ticks, log of a session) with the coverage under
`local/oracle-recordings/{fights,sessions}/` (git-ignored); later runs run only the C# and compare against it.

A recording is used only when the SHA-256 of all of these is unchanged, else the original is run live and the recording replaced:
the EXE; every game-folder file the original can read (not `*.tmp`, `*.log`, `SAVES/`); `docs/source-map/original-functions.csv`;
the steps (or the FIGHTS request words, passes, key pause) and the saved game bytes (.SAV and .BSV); the source of every
tools/oracle module the entry module imports (lockstep.py, dosmachine.py, run_unit.py, savdiff.py, and battle_oracle.py for
fights); the Python, unicorn and capstone versions.

- `--live`: run the original live and refresh the recording. `--replay-only`: never run the original. `--recordings DIR`.
- `rerun_all.py <out> [--workers N]`: every fight of FIGHTS and every session of lockstep-sessions, one process each, N workers (default the core count,
  bounded by free memory); writes `<out>/summary.json`.

Rule: replay is for the daily loop (a C# change is checked in seconds). A full `--live` rerun of every fight and session
(`rerun_all.py <out> --live`) is made before every release and periodically: the recording only says what the original did.
