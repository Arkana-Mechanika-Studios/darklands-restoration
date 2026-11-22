Darklands Restoration 1.1 makes Darklands translatable. Language packs can now change the words the game writes: the card
texts, the screens, the names of items, places and saints, and the battle messages. They work in Latin, Greek and
Cyrillic letters, and in Chinese, Japanese and Korean. Right-to-left languages come later. Words drawn into the original
pictures need a picture pack. Pixel fonts are drawn in the game's own style, and sharp TrueType fonts are an option.
Translators get their own editor, the **Darklands Workshop**, published beside this release as a separate beta download.

**The game's rules are those of 1.0:** the same orders give the same rolls, battles and events. What's new is in how
you give orders (the Comfort interface below) and in what you read. Your saved games and settings carry over. No full
translation ships with 1.1 yet: the first ones will come from the translators using the Workshop.

**You need your own copy of Darklands** (for example the GOG or Steam release, the English version 483.07). Nothing from
the game is included.

## What's new

- **Language packs.** Put a pack in `packs/languages`, then choose it on the launcher's Advanced tab (the **Language** row).
  A pack can bring its own fonts, wider or two-line labels where a language needs more room, and its own decorated
  capitals for letters the 1992 game has no picture of. When a language needs letters the original fonts don't have, the
  launcher turns the HD fonts on and says so. Without a pack, the game is in English exactly as before.
- **HD fonts for English.** The new **Fonts** row on the launcher's Advanced tab draws the game's English in sharp,
  high-resolution fonts. The original fonts stay the default.
- **The Interface switch: Comfort or Classic.** It has one row on the launcher's Advanced tab, with Comfort as the default.
  **Classic** keeps the 1992 map and saved-games screens. **Comfort** changes how you give orders, never what the game
  decides:
  - **Walking on the map:** tap a direction twice, or hold Shift with it, and the party walks on until something happens
    (an event, a place, water or the map's edge) or you press a key.
  - **The saved-games list:** one line per save, newest first, the dates in a column, and the chosen save's details under
    the buttons.
  - The mouse wheel in menus and the card-menu keys of 1.0 keep their own rows on the Advanced tab, under either
    interface.
- **Fixed: the character sheet's skill values** from Healing down now line up with the others. This is a drawing glitch of
  the original (GitHub issue 4), fixed in every mode except the testers' exact mode.
- **The game's folders:** `mods`, `packs/languages` and `packs/audio` are there from the start, each with a short README.
  The example audio pack and the example mod now come with the Workshop.

## The Darklands Workshop (beta)

A separate download, for people who want to translate the game:
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/workshop-v1.1.0-beta.1

Unzip it into the game's folder; it makes a `Workshop` folder there. It shows every text as the game draws it, in the
pack's fonts, checks the pack with the game's own check, and opens the game at the text you are translating. It also
exports and imports texts (JSON, CSV, XLIFF) and helps you work with an AI translator. It comes with short sample packs
(the opening of the game in French, Spanish, Polish, Russian, Greek, Japanese and Korean) to play and to start from, the
translator's guide, and a video tutorial: https://youtu.be/tPzPl-0qWg4

It is a beta: please report what goes wrong with its **Report a problem…** menu.

## Faithfulness

The testers' exact mode still plays exactly as the original. All 1,040 recorded battles and whole-game sessions were
replayed against this build and agree with the original.

## Reporting a problem

If you find a crash, something that looks or sounds wrong, or anything that plays differently from the Darklands you
remember, report it. In the game, **Ctrl+F12** (or "make a problem report" in the launcher) packs the logs and your last
saved game into a zip and opens the GitHub issue form. Nothing is sent by itself. Ideas and impressions are welcome on the
Discord (https://discord.gg/HjzWvmHhqZ).

## Getting started

1. Download `DarklandsRP-1.1.0-win-x64.zip` (Windows) or `DarklandsRP-1.1.0-linux-x64.tar.gz` (Linux and Steam Deck,
   experimental) and unpack it into a folder of its own, or over your 1.0 folder. On Linux, unpack with `tar` so
   `DarklandsRP` stays executable.
2. Start `DarklandsRP Launcher.exe` (Windows) or `./DarklandsRP-Launcher` (Linux), choose the game folder and the
   settings, then press Play.
3. For MT-32 music, put your MT-32 or CM-32L ROM files in the `MT32_ROMS` folder.

Unpacked over 1.0, the old example audio pack (`packs/audio-pack-example`) and example mod (`mods/wayside-shrine`) stay
where 1.0 put them. They do no harm; delete them if you don't use them.

To translate: download the Workshop (link above) and unzip it into the same folder.

## Files

| File | |
|---|---|
| `DarklandsRP-1.1.0-win-x64.zip` | Windows x64: the game, the launcher, the guides (`docs/`), the HD fonts pack (`packs/fonts`), and empty `mods`, `packs/languages` and `packs/audio` folders |
| `DarklandsRP-1.1.0-linux-x64.tar.gz` | Linux x64 (experimental): the same |
| `*.sha256` | the SHA-256 checksum of each package |

The Workshop's own files (`DarklandsWorkshop-1.1.0-beta.1-win-x64.zip` and `-linux-x64.tar.gz`) are on its pre-release
page.
