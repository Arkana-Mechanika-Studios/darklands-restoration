# Roadmap after 1.0

Version 1.0 was the first release of the faithful game: Darklands 483.07 rebuilt in C#, every rule checked against the
original program. This page says what comes after it and in what order. It gives no dates: releases ship when they are ready. How work is
proposed, decided and reviewed is in `docs/GOVERNANCE.md`; how to take part is in `CONTRIBUTING.md`.

## The promise that does not change

- **The original stays the reference.** The original program is the reference. The Original rule set is the faithful game,
  checked against the original in every release; tag v1.0 records its first release. Where our game is found to differ
  from the original (our mistake), the fix goes into Original, and so into every rule set, and the release's notes list it.
  The original's own bugs stay in Original and are fixed in Fixed. Nothing below may make Original less faithful.
- **Everything new is a switch.** Every departure from the original has an id, and the original path stays beside it.
- **Presets** gather the switches into the games players choose between:
  - **Original**: the original game, its bugs included (only bugs that damage saved games, and plain display glitches, are
    fixed in every preset). It is the reference for checking and for purists. (Not the Interface switch's Classic, which is
    about how orders are given.)
  - **Fixed**: the original content with the original game's bugs fixed. It is meant to become the default.
  - **Restored**: Fixed, plus content the original game holds but never uses and what its designers meant to do.
  - **Custom**: any choice of switches.
- **Saved games stay compatible with the original** wherever the rule set allows it, and each save records the rules it
  was played with.

## Tracks

The work is organised in ten tracks. Each has its own goal, its own standard of evidence and its own entry points;
`CONTRIBUTING.md` (section 3) describes each one for contributors, and the issue labels use the same names.

| Track | Goal |
|---|---|
| Faithfulness | Keep the original game exact: the checks against the original, the registers of differences, bugs and unused code. |
| Engine structure | Separate the game's rules from its screens, text, data and rules settings, so the tracks below can build on it. |
| Original bug fixing | The Fixed rule set: each bug of the original reproduced, registered with an id, then fixed behind its own switch. |
| Restoration and research | The Restored preset: content made or meant by the designers, each item with its sources; the history of the game. |
| Localisation | The game, its card texts included, in other languages, with fonts that fit. |
| Interface and features | An in-game settings screen, quality-of-life options and new screens, never changing what the game decides. |
| Graphics, sound and fonts | Optional new art, sound, music and sharper fonts; the original pixels stay the reference picture. |
| Modding | A mod format, the Workshop, and tools to make mods: pictures, sounds, texts, items, cards, rules, maps and screens. |
| Platforms | Linux, macOS, handheld PCs; packaging; finding the player's game files. |
| Testing and documentation | Playtesting and problem reports; the manual of the restoration's own features and switches, the FAQ, the website. |

## Releases

### 1.0.x: fixes to the faithful game only
Anything found in 1.0 that differs from the original. No new switches.

### 1.1: fixed, translatable and modular
One development line that reworks each part of the engine once. In order:

1. Code-health work held back for 1.0 (file layout; settings read only by the hosts).
2. Screens that only read the game and send commands, together with every text of the game given an id instead of
   English text in the code. Each step checked against the original.
3. One table of rule switches and rule values, the Original and Fixed presets, and the first fixed rules: as many of the
   original's gameplay bugs as can be done, each one a switch tied to its register id.
4. An in-game settings screen (the game and its clock stand still while it is open), and input recording in problem
   reports, so a report can be replayed exactly.
5. Fonts split into their metrics and their look, sharper fonts, and the translation loader: the **first translatable
   build**, with a test language that shows every text that is not yet translated. Translators are invited here.
6. Camp, the travel map and the game loop restructured, checked against the original at each step.
7. The battle engine restructured, checked against the original after every change, and then the battle fixes (among
   them the party-split bugs).
8. Game data as data: items, saints, formulae, places and enemies addressed by stable ids; new pictures and sounds;
   cards described as data; the world map's structure.
9. A registry of screens, and layouts for languages whose words are longer than English.
10. The **Restored** preset, with its first items.

If this line takes long, a release may be cut at a step boundary where what is done is complete in itself (for example
after step 5).

### 1.2: mods
The mod system and its tools, built once the engine work above is finished, so the mod format is fixed only once:

- the mod specification, after a community review of the draft (`docs/modding/SPEC-DRAFT.md`);
- the mod loader, a checker that explains every problem, a packager, and mods in the launcher;
- a developer mode in the game (an inspector that names what is under the cursor, live reload);
- the **Darklands Workshop**: one window with an editor for each kind of mod (pictures, sounds, fonts, texts, cards and
  quests, items and other data, rules, battles, saved games, the world map, screens), each showing its change in the
  running game;
- public notes on the original game's file formats.

### Later
What the mods show is missing; more platforms as they become ready.

## What you can do now

- **Play 1.0 and report** anything that differs from the original (Ctrl+F12 in the game packs a report).
- **Read the mod specification draft** and tell us what you think.
- **Research**: what the designers meant, what the manual and the hint book say that the game does not do, the history
  behind places, saints and alchemy.
- **Translators**: say on Discord which language you would like to work on; the test builds of 1.1 will come to you first.
- **Artists and musicians**: picture and sound packs already work (`docs/PACKS.md`).
