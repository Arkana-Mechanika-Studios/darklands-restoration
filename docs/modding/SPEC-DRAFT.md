# Darklands Restoration mod specification, version 1 (public draft for review)

Status: **draft for community review**. Nothing here is final and the mod system it describes is not built yet: it ships
with release 1.2, after the engine work of 1.1 (see `docs/ROADMAP.md`). Tell us what you think on Discord or
in a GitHub issue. When the review is over, this text becomes `docs/modding/SPEC.md`, and today's
`docs/MODS.md` and `docs/PACKS.md` become its chapters.

Marks used below: **[now]** works in 1.0 · **[v1]** the spec-1 loader · **[reserved]** name and place fixed now,
capability comes later (so mods written today stay valid). **(Dn)** = a design choice listed in §13, open to review.

Licence: this specification, its JSON schemas and the sample mods are under the MIT licence, so other tools and engines
may implement the format. The program's code stays GPL-3.0-or-later. A mod's own content carries the licence its author
states in `mod.json`.

## 1. Terms

- **Mod**: one folder (or a zip of one folder) with a `mod.json`. Everything a modder makes is a mod; there is one format.
- **Content kinds**: what a mod contains: pictures, sound, fonts, text, cards, records, rules, world, interface (§4).
- **Cosmetic** kinds change only how the game looks, sounds or reads: pictures, sound, fonts, text, interface themes and
  layouts. **Gameplay** kinds change what the game does: cards, records, rules, world, interface views with new behaviour.
- **Pack**: the everyday word for a mod with cosmetic kinds only. Not a separate format **(D1)**.
- **Classic**: the game with no gameplay mod and the Classic rules preset. It is the verified reference: it plays as the
  1992 game, checked against the original program.
- **Base game**: the player's own Darklands files. A mod never contains them and never changes them.

## 2. Guarantees (the contract)

1. **Classic is untouched.** Cosmetic mods never change a decision, a random draw or the game's timing. A game with
   cosmetic mods only is Classic; its saves are not marked. This is proved by the Classic-impact check (§11) in our CI on
   the sample mods, and modders can run the same check.
2. **Gameplay mods are declared and recorded.** A game with any gameplay mod is *modded*: its saves carry the companion
   file (§8), and the load screen marks them (in the presets that draw clean screens).
3. **The save stays an original save.** A `.SAV` written with mods loads in the original game and in this program without
   the mods. Anything new a mod adds carries a *stand-in* from the base game that the `.SAV` holds in its place (§7.3).
4. **Data only.** A mod is read, never run: JSON, Fluent text, PNG pictures, OGG/WAV/MIDI sound. No code, no scripts in a
   general language, no network, no paths outside the mod's folder **(D2)**.
5. **Whole or nothing.** A mod with any error is left out entirely, with a message naming the file, the place and the
   reason. The game never starts with half a mod. **[now]**
6. **Deterministic.** Everything a mod does that involves chance uses the game's own random numbers, so a modded game
   replays exactly from a save and its inputs, as Classic does (bug reports, tests).
7. **Original bugs that damage data stay fixed** (the DataSafe default) whatever mods are loaded.

## 3. The folder and `mod.json`

```text
mods/wayside-shrine/
  mod.json              the manifest (required)
  pictures/  …          picture replacements and new pictures     (pictures)
  sound/     …          sound replacements and new sounds         (sound)
  fonts/     …          font replacements                         (fonts)      [reserved]
  text/<locale>/*.ftl   texts of the mod, and translations        (text)       [v1 for mod text; game text reserved]
  cards/*.json          cards, encounters, quests                 (cards)
  records/*.json        changes to data records                   (records)    [reserved]
  rules.json            rule parameters and switches              (rules)      [reserved]
  world/     …          the world map and places                  (world)      [reserved]
  interface/ …          themes, layouts, views                    (interface)  [reserved]
  tests/*.json          scenario tests; never loaded by the game  (§11)
  README.md, LICENSE    for people; not read by the game
```

Folder names are fixed; inside them a mod organises files as it likes. Files the game does not know are reported by the
checker as a warning, never loaded.

### 3.1 `mod.json`, spec 1 [v1]

JSON with comments and trailing commas allowed (as today).

```jsonc
{
  "spec": 1,                               // this specification's major version
  "id": "wayside-shrine",                  // lowercase letters, digits, '-'; 3–40 chars; unique; = folder name (D3)
  "name": "The wayside shrine",
  "version": "1.0.0",                      // semantic versioning
  "description": "A pilgrim's shrine before city gates.",
  "authors": ["…"],
  "licence": "CC-BY-4.0",                  // SPDX id, or "proprietary"
  "homepage": "https://…",                 // optional
  "engine": ">=1.1.0 <2.0.0",              // program versions it runs on (§10)
  "kinds": ["cards", "sound"],             // what it contains; checked against its folders
  "requires":  { "other-mod": ">=2.0.0" }, // must be loaded too, and before it
  "loadAfter": ["some-pack"],              // if present, load after it (no requirement)
  "conflicts": { "bad-mod": "*" },         // never together
  "languages": ["en", "de"],               // languages its text exists in; first = its source language
  "cards": [ ]                             // optional: cards may also stay inline here, as in spec 0
}
```

Derived, not written: **class** = gameplay if `kinds` holds a gameplay kind, else cosmetic.

### 3.2 Older folders keep working [v1]

- **Spec 0 mods** (today's `mod.json` with `name`, `version`, `description`, `cards`, no `spec`): read as spec 1 with
  `id` = folder name, `kinds: ["cards"]` (+ `"sound"` if a card has `music`), `engine: "*"`. The checker suggests the upgrade.
- **Folders without `mod.json`** that hold PNGs / sounds / `pack.json` / `audio.json` (today's picture and sound packs):
  read as a cosmetic mod with `id` = folder name. `pack.json` and `audio.json` stay valid inside a spec-1 mod, in
  `pictures/` and `sound/`.
- The settings `display.hdPack` and `sound.audioPack` keep working: they are put in front of the mod list (§6).

## 4. Content kinds

| Kind | Class | What it can do | Addressed by | Status |
|---|---|---|---|---|
| pictures | cosmetic | replace any picture, sprite, frame, battlefield sprite, drop capital; add new pictures for the mod's cards | asset name (§5) | replace **[now]**; new pictures [reserved] |
| sound | cosmetic | replace music pieces and effects (cues); music per card; new sounds for the mod's cards | cue name, card number | replace + per card **[now]**; new cues [reserved] |
| fonts | cosmetic | replace the game's fonts; same metrics (Classic-safe) or own metrics (translations) | font name | [reserved] (L-6b) |
| text | cosmetic | translate the game's text; text of the mod's own content in several languages | text key (§5) | mod text [v1]; game text [reserved] (P1–P2) |
| interface: themes, layouts | cosmetic | look and arrangement of the views that are moddable | screen and slot ids | [reserved] (P6) |
| cards | gameplay | add cards, override the game's cards, encounters, quests; effects | card number, page id | cards v0 **[now]**; vocabulary grows [reserved] (P6) |
| records | gameplay | change fields of items, saints, formulae, cities, places, enemies; add new ones | record kind + id | [reserved] (P3) |
| rules | gameplay | rule parameters; fixed-rule switches; presets | registry key | [reserved] (P3) |
| world | gameplay | the world map's terrain, roads, places | tile, place id | [reserved] (needs a new seam) |
| interface: views | gameplay if it adds behaviour | new or alternative views, built only of the game's own commands | view id | [reserved] (P6) |

Interface rule (from the post-1.0 UI plan): a view may only read screen models and send the game's own commands; it can
never show more than the original shows (field of view in battles) and clicks go through the original picking. A view that
only rearranges is cosmetic; one that adds a command the original lacks is gameplay.

## 5. Ids: the public names a mod uses

These names are the modding API. They are listed per release by `darklands-dev ref export` (catalogues), and stay stable
(§10).

| What | Form | Example |
|---|---|---|
| a picture | asset name (`docs/PACKS.md`) | `PICS/STARTSCR.PIC`, `COMMONSP.IMG#12`, `F01C.CAT/F01WKSW.IMC#3.5`, `BC/CAVE.FLC@1A40` |
| a new picture | `mod:<id>/<path>` | `mod:wayside-shrine/pictures/shrine.png` [reserved] |
| a sound | cue name (`docs/PACKS.md` catalogue) | `inn-1`, `battle-death`, `music-0A` |
| a text of the game | text key | `msg:CITYG04#3`, `exe:DS:1A2B`, `lst:item:42:name` [reserved] |
| a text of a mod | `mod-<id>-<name>`, or derived from the card (§7.2) | `mod-wayside-shrine-card-366-shrine` |
| a card | number; game cards 0–365, new 366–32767 | `366` |
| a record | kind + our id | `item:42`, `saint:17`, `formula:5`, `city:3`, `place:211`, `enemy:9` [reserved] |
| a new record | `<id>:<name>` | `wayside-shrine:pilgrim-badge` [reserved] |
| a rule | registry key | `rules.lodging_price`, `fix.B-7fade3` [reserved] |
| a screen, slot or view | screen id + slot | `screen.inn.menu`, `slot.card.text` [reserved] |

## 6. Loading: order, layers, conflicts

### 6.1 Which mods and in which order [v1]

- One ordered list: setting `"mods": [...]`, or `--mod <name>` repeated, or the launcher's Mods row (tick, order).
- A name is found as a folder, then in `mods/` next to the program, in the game folder or beside it **[now]**; also a
  `<id>-<version>.zip` there [v1].
- The order is the player's, then corrected so that `requires` and `loadAfter` are met (a stable sort). A cycle, a missing
  `requires`, a version outside a range, a `conflicts` pair or an `engine` range that does not hold leaves the mod out.

### 6.2 Layers

The base game is the bottom layer; each mod lays over the ones before it. What "over" means per kind:

| Kind | Two mods change the same thing | Result |
|---|---|---|
| pictures, sound, fonts, text | same asset / cue / font / key | the later mod wins; a line in the log; the checker lists it as information |
| cards | same card number | **conflict** (both cards refused, the second mod left out) unless the later mod lists the earlier one in `loadAfter` or `requires`: then the later wins **(D4)** |
| records, rules | same field of the same record / same key | conflict, same rule as cards |
| records, rules | different fields of the same record | both apply |
| new things (cards 366+, new records) | same number or id | always a conflict: two mods cannot add the same new thing |

A card override may hand on to the card below it: `"next": "original"` reaches the layer under this mod (another mod's
card or the game's), not always the game's **(D5)**.

### 6.3 When it happens

Mods are read and checked once, before the game starts (before the opening). Cosmetic mods may also be switched or
reloaded while playing (launcher, the in-game settings screen, dev mode's hot reload); gameplay mods only at the start of
the program.

## 7. Content rules

### 7.1 Pictures and sound **[now]**

As `docs/PACKS.md` says today: a PNG named after its asset (or mapped in `pack.json`) is stretched over the asset's
rectangle and follows the palette; a sound named after its cue (or mapped in `audio.json`), OGG/WAV/MIDI with a SoundFont.
Spec 1 adds only the folders (`pictures/`, `sound/`) and keeps the rules.

### 7.2 Text [v1 for mod text]

- A mod's texts may stay inline in its cards (as today); its first language is `languages[0]` (default `en`).
- Every inline text has a **derived key**: `mod-<id>-card-<number>-<page id>` for a page, `…-option-<n>` for an option. A
  translation of a mod is then a `text/<locale>/*.ftl` file with those keys, in the same mod or in a separate cosmetic mod.
- Translations of the game's own text are cosmetic mods with `text/<locale>/` by the keys of §5 [reserved until P1–P2];
  layout as in `docs/LOCALISATION.md`.
- Characters: until the font work (L-6b), only the game font's characters are allowed; the checker says which are missing.

### 7.3 Cards [now, grows]

The rules of `docs/MODS.md` stay. Spec 1 adds:
- Cards may be in `cards/*.json` (a list of cards per file) or inline in `mod.json`.
- **Stand-ins**: every new card names `savesAs` (a game card the `.SAV` holds in its place) **[now]**. The same rule will
  hold for every new thing: a new item names an original item as stand-in, a new place an original place, etc., so the
  `.SAV` stays an original save; the companion file keeps the real ids **(D6)**.
- The effect vocabulary grows by spec minor versions (1.1, 1.2 …) in this order:
  items, skill tests, reputation, events and flags, battles, quests, encounters, attachments to game cards. A mod using a
  newer effect declares the `engine` range that has it.

### 7.4 Records, rules, world, interface [reserved]

Fixed now: their folders and id forms (§3, §5), their class (gameplay, except themes/layouts), the layer rules (§6.2), the
stand-in rule (§7.3). Their file formats are written as chapters of this spec when their engine phase starts, each with its
JSON Schema, checker rules, a sample mod and a test kind. World mods apply to **new games only** **(D7)**.

### 7.5 Raw file replacement

Not allowed in spec 1: a mod cannot replace a file of the base game. Every change goes through a content kind with ids, so
it can be checked, combined and recorded **(D8)**.

## 8. Saves [now, extended in v1]

- The `.SAV` stays in the original format (§2.3). Beside it, the companion `DKSAVE<n>.MODS.JSON` **[now]**, extended in v1
  with: `spec`, each gameplay mod's id, version and **content hash** (of its files), the rules preset and switches, the
  stand-ins in use and the real ids.
- Cosmetic mods are not recorded and never mark a save.
- Loading a save:
  - all its gameplay mods present, same versions: the game goes on exactly;
  - a mod missing: a message names it; the game goes on from the stand-ins **[now for cards]**;
  - a mod at another version or with another hash: a message; the game goes on with the mod as it is now;
  - a gameplay mod added that the save never had: allowed; from now on the save is modded (cards, records and rules take
    effect from the load) **(D9)**; world mods excepted (new game only).
- A game once modded stays marked **[now]**.

## 9. Limits and safety [v1]

- Paths: relative, inside the mod folder; no `..`, no absolute paths, no links that lead outside.
- Sizes (**D10**): a picture at most 8 times the original's size per side and at most 4096 pixels per side; a
  sound file at most 30 minutes and 200 MB; a JSON or Fluent file at most 4 MB; a whole mod at most 2 GB. Above a limit
  the file (not the mod) is refused with a message, so a too-large picture keeps the game's own.
- Files of unknown types are not read. Text files are UTF-8.
- Nothing in a mod is executed; no network access by the game for mods.

## 10. Versions

- **This spec**: `spec` = major version (1). Minor versions (1.1, 1.2 …) only add kinds, fields, effects and ids; a spec-1
  mod never stops working because of a minor version. A major version (2) may break, with a converter.
- **Program**: `engine` is a range over the program's version (semantic versioning). Each release lists which spec minor
  version it implements.
- **Ids**: an id of §5 is never renamed or reused within a major version. One that must go is marked deprecated (the
  checker warns) for at least one minor version first.

## 11. Checks and tests

- `darklands-dev mod check <folder|zip>` runs exactly the game's own checks without starting it, plus warnings (unknown
  files, overridden assets, text longer than its slot, characters missing from the font, unreachable pages). Output: file,
  line or JSON path, reason, fix hint; `--json` for tools.
- `tests/*.json`: scenario tests (start: a save or a new party; steps: the headless step language or card choices;
  expectations). `darklands-dev mod test` runs them headless.
- **Classic-impact check** `darklands-dev mod impact`: our standard sessions replayed with the mod loaded. A cosmetic mod
  must show no difference (otherwise it is not cosmetic and is refused as such); a gameplay mod gets the list of moments it
  changes.

## 12. Packaging and rights

- A mod is shared as `<id>-<version>.zip` with one top folder `<id>/`. `darklands-dev mod pack` runs the check and the
  tests and writes the zip with `CONTENTS.sha256`. The launcher takes a dropped zip and puts it in `mods/`.
- We host no mods. Where mods are shared is the community's choice.
- A mod contains only its authors' work: new pictures, sounds, texts, data. Never the base game's files, and not pictures,
  sounds or texts taken out of them unchanged. A translation of the game's text is the translator's work over the player's
  own files; it is distributed outside the program **(D11)**.

## 13. Design choices (open to review)

These are the choices this draft makes. Each can still change after the review.

- **D1** One format; "pack" is only the word for a cosmetic mod.
- **D2** Data only, for good. A small safe expression language for card conditions may come with the card vocabulary,
  still not code.
- **D3** `id` must equal the folder name.
- **D4** The same card number from two mods is a conflict unless the later one declares the earlier (`loadAfter` /
  `requires`). **This changes today's behaviour** (today the later wins, with a log line).
- **D5** `"next": "original"` reaches the layer below, so overrides chain. **This changes today's behaviour** (today it
  always reaches the game's card); with one mod the result is the same as today.
- **D6** Stand-ins for every new thing, so the `.SAV` stays an original save.
- **D7** World mods only for new games.
- **D8** No raw file replacement.
- **D9** Gameplay mods may be added to a running save (world mods excepted).
- **D10** The size limits of §9.
- **D11** The rights wording of §12, including that translations of the game's text are distributed outside the program.

Questions we would most like answers to: are the ids of §5 the ones you would want to address; is anything you want to
make impossible under §7 and §9; are the size limits right; does the stand-in rule (D6) work for what you have in mind.
