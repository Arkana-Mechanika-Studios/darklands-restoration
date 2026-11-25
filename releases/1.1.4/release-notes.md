Darklands Restoration 1.1.4 is a small fix to 1.1 for players who use the HD fonts, and for translators.

## Fixed

- **The pointer over HD text.** With the HD fonts (or any font pack with TrueType fonts), the gauntlet moved over a card's
  text or a store's lines showed under the letters, and the letters right next to it went missing while it was there. The
  pointer is now drawn over the text, and the letters it covers in part are drawn beside it. Wrong since 1.1.0. Thanks to
  the player on Discord who reported it, with screenshots.
- **Text of another screen over a card.** Coming back to a card from a store (the market's merchants, for example) or
  another screen, faint pieces of that screen's lines showed over the card's picture, and some letters of the card's own
  text had gaps. The card's window now takes back its picture whole. Wrong since 1.1.0. Reported in the same message.

## For translators

A check of every text the game draws found texts that stayed English with a language pack loaded, and pack texts that lost
their letters. With a pack:

- **Now in the pack's words:** a formula's short name in the alchemy screen's batch list; the character generator's
  families, occupations, formulas and saints; the town size and standing on the party information screen; the camp's
  details (potions, jobs, fees); the place and month of a saved game on the load screen; the battle's potions, its "(Ldr)"
  and "(All)" and the members' messages; the store's message when it is full; the shop names and the guild master's words
  the cards put in; the names the cards copy into their texts (items, saints, formulas, and the dragons', dwarfs' and
  titles' names); and the map's messages.
- **No more `?` for a pack's letters** in a card's hint and tell line, the battle's place notes and members' messages, and
  the store's pages: a Chinese, Japanese, Korean, Greek or Cyrillic letter is drawn as the rest of the pack's text is.
- **Pronouns:** a pack's own variables `var-he`, `var-his`, `var-him`, `var-himself`, `var-He`, `var-His` now answer the
  game's pronouns in every card text, so a language can say them in its own words and forms.
- **New keys** for the template: `map:<n>.text` (the map's messages, with their box), `id:rw.store.no-room`, and the EXE's
  word tables of those names. A formula's short name now has its room in the template and the check (61 px).

Make a new template (`DarklandsRP <game folder> --translator-template <folder>`) or use the Workshop 1.1.0-beta.3 to see
them. The translator's guide (`docs/LOCALISATION.md`) lists them.

Without a font pack or a language pack the game is exactly as before: no screen changes.

Everything else is as in 1.1.3. For what 1.1 brought, see
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/v1.1.0

Your saved games and settings carry over: unpack 1.1.4 over your 1.1.x or 1.0 folder.

**You need your own copy of Darklands** (for example the GOG or Steam release, the English version 483.07). Nothing from
the game is included.

## The Darklands Workshop (beta)

A new beta of the translators' editor comes with this release: version 1.1.0-beta.3, a separate download. It lists the new
texts where they are shown, with their room:
https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/workshop-v1.1.0-beta.3

## Faithfulness

The testers' exact mode still plays exactly as the original. All 1,040 recorded battles and whole-game sessions were
replayed against this build and agree with the original.

## Reporting a problem

If you find a crash, something that looks or sounds wrong, or anything that plays differently from the Darklands you
remember, report it. In the game, **Ctrl+F12** (or "make a problem report" in the launcher) packs the logs and your last
saved game into a zip and opens the GitHub issue form. Nothing is sent by itself. Ideas and impressions are welcome on the
Discord (https://discord.gg/HjzWvmHhqZ).

## Getting started

1. Download `DarklandsRP-1.1.4-win-x64.zip` (Windows) or `DarklandsRP-1.1.4-linux-x64.tar.gz` (Linux and Steam Deck,
   experimental) and unpack it into a folder of its own, or over your 1.1.x or 1.0 folder. On Linux, unpack with `tar`
   so `DarklandsRP` stays executable.
2. Start `DarklandsRP Launcher.exe` (Windows) or `./DarklandsRP-Launcher` (Linux), choose the game folder and the
   settings, then press Play.
3. For MT-32 music, put your MT-32 or CM-32L ROM files in the `MT32_ROMS` folder.

To translate: download the Workshop (link above) and unzip it into the same folder.

## Files

| File | |
|---|---|
| `DarklandsRP-1.1.4-win-x64.zip` | Windows x64: the game, the launcher, the guides (`docs/`), the HD fonts pack (`packs/fonts`), and empty `mods`, `packs/languages` and `packs/audio` folders |
| `DarklandsRP-1.1.4-linux-x64.tar.gz` | Linux x64 (experimental): the same |
| `*.sha256` | the SHA-256 checksum of each package |

The Workshop's own files (`DarklandsWorkshop-1.1.0-beta.3-win-x64.zip` and `-linux-x64.tar.gz`) are on its pre-release
page.
