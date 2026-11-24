Darklands Restoration 1.1.2 is a small fix to 1.1.

## Fixed

- **HD fonts: no thin bits of the original letters at the edges of the HD letters.** On the saved-games list, the party
  panel, card texts and elsewhere, thin dark or light ticks of the original low-resolution letters showed beside the HD
  letters, and white ones in the pale shadow under the saved-games list's lines. It happened when the game's picture was not a whole multiple of its original size in the window (most full
  screens, 1920x1080 for example, and most window sizes), and with every picture filter other than Sharp (Even, Soft, HQ and
  the CRT filters). That is why it came and went as the window changed size. The original fonts were not affected.
- **HD fonts: a lighter text shadow.** With the HD fonts, the shadow under the saved-games list's lines (and the party
  screen's) is now drawn smaller and softer, as a light emboss, instead of a blurry second copy of the text. The original
  fonts draw it exactly as in 1992.

Everything else is as in 1.1.0, with 1.1.1's fix included (no faint outlines of a card's hidden answers with the HD fonts).
For what 1.1 brought, see
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/v1.1.0

Your saved games and settings carry over: unpack 1.1.2 over your 1.1.x or 1.0 folder.

**You need your own copy of Darklands** (for example the GOG or Steam release, the English version 483.07). Nothing from
the game is included.

## The Darklands Workshop (beta)

The translators' editor is unchanged: version 1.1.0-beta.1, a separate download that works with this release:
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/workshop-v1.1.0-beta.1

## Faithfulness

The testers' exact mode still plays exactly as the original. All 1,040 recorded battles and whole-game sessions were
replayed against this build and agree with the original.

## Reporting a problem

If you find a crash, something that looks or sounds wrong, or anything that plays differently from the Darklands you
remember, report it. In the game, **Ctrl+F12** (or "make a problem report" in the launcher) packs the logs and your last
saved game into a zip and opens the GitHub issue form. Nothing is sent by itself. Ideas and impressions are welcome on the
Discord (https://discord.gg/HjzWvmHhqZ).

## Getting started

1. Download `DarklandsRP-1.1.2-win-x64.zip` (Windows) or `DarklandsRP-1.1.2-linux-x64.tar.gz` (Linux and Steam Deck,
   experimental) and unpack it into a folder of its own, or over your 1.1.x or 1.0 folder. On Linux, unpack with `tar`
   so `DarklandsRP` stays executable.
2. Start `DarklandsRP Launcher.exe` (Windows) or `./DarklandsRP-Launcher` (Linux), choose the game folder and the
   settings, then press Play.
3. For MT-32 music, put your MT-32 or CM-32L ROM files in the `MT32_ROMS` folder.

To translate: download the Workshop (link above) and unzip it into the same folder.

## Files

| File | |
|---|---|
| `DarklandsRP-1.1.2-win-x64.zip` | Windows x64: the game, the launcher, the guides (`docs/`), the HD fonts pack (`packs/fonts`), and empty `mods`, `packs/languages` and `packs/audio` folders |
| `DarklandsRP-1.1.2-linux-x64.tar.gz` | Linux x64 (experimental): the same |
| `*.sha256` | the SHA-256 checksum of each package |

The Workshop's own files (`DarklandsWorkshop-1.1.0-beta.1-win-x64.zip` and `-linux-x64.tar.gz`) are on its pre-release
page.
