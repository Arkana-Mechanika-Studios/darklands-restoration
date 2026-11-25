# Reporting a problem

The program can pack what a bug report needs into one zip file. **Nothing leaves your computer until you attach that zip to a
GitHub issue yourself**: the program uploads nothing, sends nothing, and has no account, address or key for it.

## How to report

1. Make the report, any of these ways:
   * after a crash, the program shows a notice with the report's place and three buttons: **Open report form**, **Show the file**, **Close**;
   * in the game, press **Ctrl-F12** (the original game does not use it);
   * in the developer console (F11), type `report` (and, if you like, a few words: `report the guard walks through the wall`);
   * on the launcher (`--setup`, or Shift at start), the link "make a problem report" (it uses the logs of your last games).
2. The program opens the folder with the zip (`reports\darklands-report-<date>-<time>.zip`) and your browser at the issue form,
   with the version, the system and the first lines of an error filled in.
3. Sign in to GitHub, choose the kind (crash, difference from the original game, other bug), say what you did and what happened,
   and **drag the zip into the form**. For a difference from the original, add a screenshot of each side at the same moment
   (F12 here, DOSBox's capture key in the original) if you can.

If the browser or the folder does not open, the program prints the path and the address; open
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/issues/new/choose yourself.

## What the zip holds

| File | What |
|---|---|
| `report.txt` | version, commit, system, .NET, screen and sound settings, packs and mods (and the Workshop version that wrote the language pack), the game program's version and SHA-256, why the report was made (crash or you), the error with its stack, your words, the list of files and what was left out |
| `logs\` | the log of this run and of the one before (everything the program printed, and the console's lines) |
| `settings\darklands.settings.json` | your settings file |
| `screenshot.png` | the last frame, when there is one |
| `recording\recording.dlrec` | the input recording of this run (below): what you did since the program started, so that it can be played again |
| `saves\last-used\` | the saved game you last loaded or saved (`DKSAVEn.SAV`, `.BSV`, `.MODS.JSON`), as it is now |
| `saves\at-load\` | a copy of that saved game as it was when you loaded it: the state before the problem |
| `saves\newest\` | the newest saved game, if it is another |
| `game\` | `CACHE.TMP` and `CHARACTR.TMP`, if present |

### The input recording

From the moment the program starts, it notes what the game is given: each key you press, where the mouse is and its buttons, the
game's clock and timer, the sound driver's answers, the random seeds the game takes from the clock, and a setting you change on
the settings screen (Alt-T) while you play. It also keeps the saved games of your saves folder as they were at the start (the
game reads them all: the load screen lists each one's place and date, newest first; past 6 MB the oldest are left out), and the
checksums of the games you save. Nothing else: no other file of yours, no folder name or user name (packs and mods are named by
their own folders' names; a saved game holds your characters' names, as the saved games in the report do), and nothing is sent.

It stays in memory, packed small, until you make a report, which puts it in the zip as `recording\recording.dlrec` (and
`report.txt` says how long it is). Measured: about 0.1 MB for an hour of ordinary play, about 0.8 MB for an hour with the mouse
moving all the time. It stops growing at 12 MB (more than ten hours even then); a recording cut there, or one whose oldest saved
games were left out, says so in `report.txt`. In the zip the recording comes before the logs: when the package is too large, an
older run's log is left out first.

With it, a developer plays your run again on their own copy of the game, step by step, to the same saved game: the problem shows up
as it did for you. If the run differs (another version of the game files, a pack they do not have), the replay says where. A
recording is played only with the build that made it (the same version and commit); another build refuses it and names the build
to use.

The developer console's commands (F11) are recorded too, and the replay runs the game's own ones. Pausing, the settings screen,
moving or resizing the window and the picture filters do not change the replay.

Your user name and your user-profile folder are replaced by `<user>` in every text file. Saved games and the picture are copied as they
are; a saved game holds your characters' names. A file over 20 MB is left out (and the report says so).

## Where things are

Beside the settings file (`darklands.settings.json`; when that folder cannot be written, in `%APPDATA%\DarklandsRestoration`):

* `logs\darklands-<date>-<time>.log`: one per run, the newest 5 are kept;
* `reports\`: the zips, the newest 10 are kept;
* `logs\session\`: helper files (the last frame, the saved game last loaded).

You can read any of them, and delete them whenever you like.

## For developers

**Playing a recording again.** The headless host replays one:
`Darklands.Host.Headless <game folder> <output folder> replay:<recording.dlrec> [packs:<folder>]`. It restores the recording's saves
into `<output folder>\saves`, sets the game up as the recording says (rules, interface, packs and mods by name, found in
`<folder>\languages`, `<folder>\fonts` and the program's `packs`), hands the game the recorded values in order, and prints whether the
tape played to its end, each game saved during the recording came out with the same bytes, and the game at the end is the same
(`replay: same`; exit code 3 when not). Where the game asks for something else than the tape holds, it stops and names the entry
(`DARKLANDS_TAPE_STACK=1` adds where the game asked from; `tapedump:<file>:<from>:<count>` lists the tape). `record:<file>` (or
`DARKLANDS_RECORD=<file>`, `1` for `recording.dlrec` in the output folder) records a scripted run the same way. The console's
`recording` writes the recording without a report. Another build's recording is refused (`replay refused:`, exit code 3);
`anybuild` plays it anyway, a developer's try that may part.

The build stamps the commit it was made from (`AssemblyMetadata("Commit")`: `GITHUB_SHA` in CI, else the short commit from git,
else `unknown`). The issue form is `.github/ISSUE_TEMPLATE/bug_report.yml`; the address the program opens fills its fields `kind`,
`version`, `system` and, for a crash, `what_happened` with the error's first lines (and the title); a player's own report leaves
`what_happened` to the player, or fills it only with what they wrote in the program. The address is kept under 6,000 characters.