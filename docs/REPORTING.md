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
| `report.txt` | version, commit, system, .NET, screen and sound settings, packs and mods, the game program's version and SHA-256, why the report was made (crash or you), the error with its stack, your words, the list of files and what was left out |
| `logs\` | the log of this run and of the one before (everything the program printed, and the console's lines) |
| `settings\darklands.settings.json` | your settings file |
| `screenshot.png` | the last frame, when there is one |
| `saves\last-used\` | the saved game you last loaded or saved (`DKSAVEn.SAV`, `.BSV`, `.MODS.JSON`), as it is now |
| `saves\at-load\` | a copy of that saved game as it was when you loaded it: the state before the problem |
| `saves\newest\` | the newest saved game, if it is another |
| `game\` | `CACHE.TMP` and `CHARACTR.TMP`, if present |

Your user name and your user-profile folder are replaced by `<user>` in every text file. Saved games and the picture are copied as they
are; a saved game holds your characters' names. A file over 20 MB is left out (and the report says so).

## Where things are

Beside the settings file (`darklands.settings.json`; when that folder cannot be written, in `%APPDATA%\DarklandsRestoration`):

* `logs\darklands-<date>-<time>.log`: one per run, the newest 5 are kept;
* `reports\`: the zips, the newest 10 are kept;
* `logs\session\`: helper files (the last frame, the saved game last loaded).

You can read any of them, and delete them whenever you like.

## For developers

The build stamps the commit it was made from (`AssemblyMetadata("Commit")`: `GITHUB_SHA` in CI, else the short commit from git,
else `unknown`). The issue form is `.github/ISSUE_TEMPLATE/bug_report.yml`; the address the program opens fills its fields `kind`,
`version`, `system` and, for a crash, `what_happened` with the error's first lines (and the title); a player's own report leaves
`what_happened` to the player, or fills it only with what they wrote in the program. The address is kept under 6,000 characters.