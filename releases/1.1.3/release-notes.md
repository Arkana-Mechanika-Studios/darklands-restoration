Darklands Restoration 1.1.3 is a small fix to 1.1 for translators.

## Fixed

- **Language packs can translate the descriptions of the data files.** A saint's description on the prayer screen, a
  formula's description in the alchemy screen's book and on the formula page, and a city's description in the card texts
  that show it (arriving at a city, for example) kept the original English whatever a pack said: they had no place in the
  translator's template. They are now in its `tables.ftl` (`snt:saint/<n>.desc`, `alc:<n>.desc`, `dsc:<n>.desc`), each
  with its box (the lines and width the screen gives it), and the screens take them from the pack as they already took the
  names. The pack check reports a description that runs past the foot of its box.
- **Alchemy and camp texts with a translation pack.** The word-wrapped texts of the alchemy and camp screens (a formula's
  name and description, the batch lines, the camp's activities, its cost line and its teachers) drew a pack's Greek,
  Cyrillic, Chinese, Japanese or Korean letters as `?`, even with a font pack that has them. They are now drawn as the rest
  of a pack's text is.

Without a language pack the game is exactly as before: these screens draw the original's bytes.

## For translators

- Make a new template (`DarklandsRP <game folder> --translator-template <folder>`) to get the descriptions, about 300 more
  texts, and copy them into your pack's `tables.ftl`.
- The translator's guide (`docs/TRANSLATING.md`) has a short section on Chinese, Japanese and Korean today: slot 1 at 7 px,
  `lines: 2` where a text needs it, and what is planned.
- The Workshop 1.1.0-beta.1 does not list the descriptions yet: its editor and its check know the texts of 1.1.2 (its
  check calls a description an unknown id). Descriptions written by hand into `tables.ftl` are kept when the Workshop saves
  the pack, and the game shows them.

Everything else is as in 1.1.2. For what 1.1 brought, see
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/v1.1.0

Your saved games and settings carry over: unpack 1.1.3 over your 1.1.x or 1.0 folder.

**You need your own copy of Darklands** (for example the GOG or Steam release, the English version 483.07). Nothing from
the game is included.

## The Darklands Workshop (beta)

The translators' editor is unchanged: version 1.1.0-beta.1, a separate download that works with this release:
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/workshop-v1.1.0-beta.1

## Faithfulness

The testers' exact mode still plays exactly as the original. All 1,042 recorded battles and whole-game sessions were
replayed against this build and agree with the original.

## Reporting a problem

If you find a crash, something that looks or sounds wrong, or anything that plays differently from the Darklands you
remember, report it. In the game, **Ctrl+F12** (or "make a problem report" in the launcher) packs the logs and your last
saved game into a zip and opens the GitHub issue form. Nothing is sent by itself. Ideas and impressions are welcome on the
Discord (https://discord.gg/HjzWvmHhqZ).

## Getting started

1. Download `DarklandsRP-1.1.3-win-x64.zip` (Windows) or `DarklandsRP-1.1.3-linux-x64.tar.gz` (Linux and Steam Deck,
   experimental) and unpack it into a folder of its own, or over your 1.1.x or 1.0 folder. On Linux, unpack with `tar`
   so `DarklandsRP` stays executable.
2. Start `DarklandsRP Launcher.exe` (Windows) or `./DarklandsRP-Launcher` (Linux), choose the game folder and the
   settings, then press Play.
3. For MT-32 music, put your MT-32 or CM-32L ROM files in the `MT32_ROMS` folder.

To translate: download the Workshop (link above) and unzip it into the same folder.

## Files

| File | |
|---|---|
| `DarklandsRP-1.1.3-win-x64.zip` | Windows x64: the game, the launcher, the guides (`docs/`), the HD fonts pack (`packs/fonts`), and empty `mods`, `packs/languages` and `packs/audio` folders |
| `DarklandsRP-1.1.3-linux-x64.tar.gz` | Linux x64 (experimental): the same |
| `*.sha256` | the SHA-256 checksum of each package |

The Workshop's own files (`DarklandsWorkshop-1.1.0-beta.1-win-x64.zip` and `-linux-x64.tar.gz`) are on its pre-release
page.
