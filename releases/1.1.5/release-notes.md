Darklands Restoration 1.1.5 is a small fix to 1.1 for everyone who loads a game saved outside a city.

## Faithfulness fixes

A place where our game differed from the original program. It is fixed in every rule set, Original included: the Original
rules are the faithful game, checked against the original in every release.

- **Groningen after a game loaded in the wilderness.** A game saved on the world map and loaded again, then taken straight
  to Groningen, showed the city wrong: a stray `Ö` before its name, no Stadtplatz and no Markt on the main street, the
  inn's name missing or garbled, a city lord's title left as `CityLordTitle`, and the city's own numbers (the toll at the
  gate, for one) read from the wrong place. The game now reads Groningen's own record on arrival, as the original does.
  Any other city, and Groningen reached after another city, were always right. Wrong since 0.9.0 (GitHub issue 7). Proven
  against the original by a new recorded session (`wild-load-groningen`: a game saved on the map next to Groningen, loaded,
  then the city, its gate and its main street), which differed before the fix and agrees now. Thanks to the player who
  reported it with the game's bug report, which made it easy to find.

Your saved games are fine: the city is read from the game's files on arrival and on loading, never from the saved game,
so a game saved in Groningen while it showed wrong is right once loaded in 1.1.5.

Without a font pack or a language pack the game is exactly as before everywhere else: no other screen changes. A new
check now reads every card's texts in several cities, by day and by night, with and without the HD fonts, and compares
them with 1.1.3's, and with an arrival after a load in the wilderness.

Everything else is as in 1.1.4. For what 1.1 brought, see
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/v1.1.0

Your saved games and settings carry over: unpack 1.1.5 over your 1.1.x or 1.0 folder.

**You need your own copy of Darklands** (for example the GOG or Steam release, the English version 483.07). Nothing from
the game is included.

## The Darklands Workshop (beta)

The translators' editor is unchanged: version 1.1.0-beta.3, a separate download:
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/workshop-v1.1.0-beta.3

## Faithfulness

The testers' exact mode still plays exactly as the original. All 1,041 recorded battles and whole-game sessions were
replayed against this build and agree with the original.

## Reporting a problem

If you find a crash, something that looks or sounds wrong, or anything that plays differently from the Darklands you
remember, report it. In the game, **Ctrl+F12** (or "make a problem report" in the launcher) packs the logs and your last
saved game into a zip and opens the GitHub issue form. Nothing is sent by itself. Ideas and impressions are welcome on the
Discord (https://discord.gg/HjzWvmHhqZ).

## Getting started

1. Download `DarklandsRP-1.1.5-win-x64.zip` (Windows) or `DarklandsRP-1.1.5-linux-x64.tar.gz` (Linux and Steam Deck,
   experimental) and unpack it into a folder of its own, or over your 1.1.x or 1.0 folder. On Linux, unpack with `tar`
   so `DarklandsRP` stays executable.
2. Start `DarklandsRP Launcher.exe` (Windows) or `./DarklandsRP-Launcher` (Linux), choose the game folder and the
   settings, then press Play.
3. For MT-32 music, put your MT-32 or CM-32L ROM files in the `MT32_ROMS` folder.

To translate: download the Workshop (link above) and unzip it into the same folder.

## Files

| File | |
|---|---|
| `DarklandsRP-1.1.5-win-x64.zip` | Windows x64: the game, the launcher, the guides (`docs/`), the HD fonts pack (`packs/fonts`), and empty `mods`, `packs/languages` and `packs/audio` folders |
| `DarklandsRP-1.1.5-linux-x64.tar.gz` | Linux x64 (experimental): the same |
| `*.sha256` | the SHA-256 checksum of each package |

The Workshop's own files (`DarklandsWorkshop-1.1.0-beta.3-win-x64.zip` and `-linux-x64.tar.gz`) are on its pre-release
page.
