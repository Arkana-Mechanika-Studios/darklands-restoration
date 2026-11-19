# Localisation (translations)

> **Status: being built for version 1.1.** A first form of translation packs works in the development branch: the
> program loads a pack and shows its words on the screens it covers (below). Card texts, the cards' own words and the
> battle screen's messages are not translatable yet, there is no in-game language screen yet, and the file layout may
> still change before 1.1. This guide is updated as each part is built.

This guide is for would-be translators first ([how you can help now](#how-translators-can-help-now)), then for
developers ([the plan in steps](#the-plan-for-developers)).

## What exists today

* **A language pack setting.** `languagePack` in the settings file ([SETTINGS.md](SETTINGS.md)), `--language-pack
  <folder>` on the command line, the environment variable `DARKLANDS_LANGUAGE_PACK`, and the **Language pack** row on
  the launcher's Advanced tab (None, or a pack found in a `packs` folder, or a folder browsed for or dropped on the
  window). None means the original English text, exactly as before.
* **Translation packs** in [Project Fluent](https://projectfluent.org/) (`.ftl` files), read with the Linguini library
  ([below](#how-a-translation-pack-looks)).
* **What a pack translates now:** the screens' own words (menus, labels, lists, prompts, notices and hints: everything a
  screen asks for by key), the entries of the original's word tables the screens show (months, canonical hours, fame
  titles, standing words, directions, town sizes, shop names, job names, skill names, risk words, the character
  sheet's labels), and the program's own notices (a damaged saved game, missing mods ...).
* **Not yet:** card texts (`.MSG` files) and the words the cards put into them (`$MonthName`, shop and patron names
  ...), the battle screen's messages, item, saint and place names, and the program start's messages. These keep the
  original English for now.
* **Fonts.** The game draws its text with the original fonts, which have no letters beyond the original's set (the
  German edition's umlauts and ß are among them). A letter the font lacks is drawn as `?`. Font packs come later.

## The principle: Classic stays the reference

With no translation pack, the game shows the original English text, exactly as it is in the player's own game files and
in `DARKLAND.EXE` version 483.07. Localisation must never change that, and a pack changes words only: never what the
game does, never a saved game. Every step of the plan is checked against the original so that Classic stays exact.

The project does not ship the game's original text. It is read at run time from the files the player already owns, as
the pictures and sounds are today.

## What will be translatable, and what not

There are two kinds of text, and they are handled differently.

**(a) The restoration's own text.** The launcher, the settings screens, the console, and messages the original game
never had. These strings and their translations live in the repository, and anyone may contribute a translation of
them in the normal way (a pull request).

**(b) The game's own text.** Card texts from the `.MSG` files, the strings inside `DARKLAND.EXE`, and the names and
descriptions in the data tables (items, saints, places and so on). A translation of this text is a derivative of the
original game's text, so the project cannot distribute it. These translations are separate, optional
**translation packs**, kept outside the repository and loaded like the other packs. Where a pack is hosted is up to
its author.

**What cannot be translated by text alone:**

* **Text drawn into the original pictures** (title screens, some panels and signs). It can only be changed by replacing
  those pictures with a picture pack ([PACKS.md](PACKS.md)).
* **Characters the original fonts lack.** Languages that need other characters will need **font packs**.
* **Fixed screen space.** In Classic, the screens, buttons and menus have fixed sizes. A translation that is longer than
  the English must be shortened or wrapped. Translators should expect to abbreviate; each key will carry the width of
  the box the original draws it in, and the pack checker will flag a translation that does not fit (planned).

**Not supported as a base:** the original German release. It uses a different `DARKLAND.EXE`; only the English 483.07
is supported. A German translation is a translation pack on the English base like any other.

## How text is identified

The game logic does not produce English sentences. It produces a **text id** plus arguments, and the screens turn that
into words: from the pack when it has the id, else from the player's own files. The ids are:

| Key | Meaning |
|---|---|
| `exe:DS:XXXX` | a string inside `DARKLAND.EXE`, at offset `XXXX` of its data segment (each copy the original keeps has its own key) |
| `exe:290E:XXXX` | a string of the EXE's far string segment |
| `id:rw.…` | a sentence of the restoration's own (the original has no such text): `id:rw.save.damaged.1` |
| `id:card.…`, `id:skill.changed` | a sentence the original builds from pieces: the chance of an option, a skill report |
| `msg:FILE#block` | a card text: block number in a `.MSG` file (not translatable yet) |

In a `.ftl` file a key is written as a Fluent identifier: lower case, `:` `.` `/` `#` `+` turned into `-`, `$` dropped.
So `exe:DS:2FF2` is `exe-ds-2ff2` and `id:rw.save.damaged.1` is `id-rw-save-damaged-1`. Two keys never meet in one
identifier.

**Arguments** become Fluent variables. A named one keeps its name (`{ $count }`, `{ $name }`). Many of the original's
strings are C format strings (`%s holds the purse of %dfl, %dgr, %dpf (%lupf).`); their arguments are, in order,
`{ $arg0 }`, `{ $arg1 }` …: the translation writes the sentence with those variables and needs no `%` codes.

## How a translation pack looks

A pack is a folder:

```
my-translation-pack/
  pack.json            { "language": "de", "name": "Deutsch", "author": "…" }
  game/exe.ftl         the EXE's strings and the word tables (any file names: every .ftl under the folder is read)
  ui.ftl               the restoration's own notices
```

`pack.json` must name the `language` (a language tag: `de`, `fr`, `pt-BR`; `qps` is the pseudo-locale); `name` and
`author` are optional. Every `.ftl` file anywhere under the folder is read, in path order; a message id defined twice
keeps the first. A key the pack does not have shows the original English, so a pack may be partial. What is wrong
with a pack (a Fluent syntax error with its file and line, an unreadable manifest) is reported on the console's error
output and in the launcher's line for the pack; the valid entries are still used.

A small example. **The ids are real; the German is only an illustration.**

```
# game/exe.ftl
exe-ds-2ff2 = Verlassen
exe-ds-2f71 = { $arg0 } verwaltet die Börse: { $arg1 } Gulden, { $arg2 } Groschen, { $arg3 } Pfennige.
# the months (290E:1D43 points at 290E:0000 "January" ...)
exe-290e-0000 = Januar
```

The keys of a screen can be found with the developer's inspector (a development build: hold F9 over a word; see
[SETTINGS.md](SETTINGS.md)) or in the text extract of your own game files (planned for the translators' tools).

## Fonts, layout and mouse areas

A translation changes the length of the text, and a font pack can change the width of the letters. Both move where
words land on the screen, and so where a click must go. The plan handles both with one design, shared with the
replacement fonts planned for 1.1.

**A font has two parts.** Its *metrics* (each character's width, the line height, the spacing) decide where lines
break and where every word sits. Its *appearance* decides how a character is drawn: the original bitmap, a pack's
bitmap, a high-resolution picture, later a TrueType glyph. A font pack says which kind it is:

* **Same metrics:** only the look changes (for example sharp high-resolution letters). Every line break, position and
  mouse area stays the original's, the game's 320x200 picture is unchanged under it, and the pack may be used with the
  original English in Classic.
* **Own metrics:** other widths, or characters the original fonts lack. The layout is computed again, so such a font
  is used with a translation or an enhanced presentation, never with the original English in Classic.

**One layout, read by everything.** Each text the game shows is laid out once: its lines, where each character sits,
and which part belongs to which choice. The drawing, the highlight under the pointer and the mouse test all read that
result, so a choice's mouse area is the area of its lines, never coordinates written into a screen. For the original
English with the original fonts the layout must give exactly the original's line breaks and positions (checked
against the original); with a translation or another font, the mouse areas follow the text by themselves.

**Three kinds of text on the screens:**

* **Flowing text** (card texts and their choices, message boxes, hints): wraps, and its mouse areas come from the
  layout.
* **Fixed slots** (menu items, the lines of a shop's list, labels on the character sheet, buttons): the slot keeps the
  original's place and size, and its mouse area stays the slot. The text must fit in it: the translation's short form
  first (a `.short` attribute), then slightly narrower spacing, then the text cut with "…" and shown in full in a
  hint. The pseudo-locale (step 5) lists every slot that overflows, so translators know where to abbreviate.
* **Text inside pictures:** a picture pack scoped to the language.

**Clicks give choices, not positions.** The game never receives a mouse position: a click becomes "choice 3" or "buy
the fifth item" before the game sees it. Wherever a translation or a font puts the words, the game plays the same, and
saved games and the checks against the original do not depend on the language.

**Finer positions for other fonts.** With a font of its own metrics, positions are counted in quarter pixels, not
whole pixels of the 320x200 picture, so a high-resolution font can fit a longer line in the original box and stay
sharp. The original fonts still land on whole pixels, as before.

## How translators can help now

You do not need the code to be ready to start useful work.

* **Start a glossary of game terms** in your language: items, saints, alchemy and trade terms, skills, places, status
  words. Keep the English term, your proposed term, and a note on the context. Agreeing terms early is the most
  valuable thing a translator can do, and it does not depend on the file format.
* **Note UI space limits.** While playing, record where English text already fills a button, a menu line or a panel.
  These are the places where a longer translation will not fit.
* **Join the Discord**: <https://discord.gg/HjzWvmHhqZ>, to talk with others working on your language.
* **Open a GitHub issue** at <https://github.com/Arkana-Mechanika-Studios/darklands-restoration>, labelled for your
  language (for example "translation: French"), to say you are working on one, share your glossary, or ask a
  question. One issue per language keeps coordination in one place.

Please do not post the game's own text in bulk in public places; keep working notes to the terms and short phrases you
need.

## The plan for developers

Each step is checked against the original game, so that Classic output stays identical. A step is not done until that
check passes.

1. **Message ids out of the game logic.** (Done in 1.1 for the rules, the cards' hints and the screens.) Replace literal English strings in the rules with message ids and
   arguments. The English built from them must equal the old output exactly.
2. **String-table extraction.** Read the strings of `DARKLAND.EXE` 483.07, the `.MSG` card texts and the data-table
   names from the player's files, and index them under the keys above. Record which characters the original fonts
   cover.
3. **Font metrics apart from appearance, and one layout pass.** The split and the shared layout of
   [the section above](#fonts-layout-and-mouse-areas); mouse areas of flowing text read from the layout. Nothing
   visible changes: the original English must give the original's frames exactly. The 1.1 high-resolution fonts
   (same metrics) are built on this step.
4. **Fluent overlay loader.** (First form built: packs, the setting, the launcher row; card texts next.) Load `.ftl` files from translation packs over the original-language text. A missing
   message falls back to the original text; the game never shows an empty string. The Fluent library is chosen
   after a trial, before this step. The wrapping routine for long words belongs here, and so do fonts with their own
   metrics.
5. **Pseudo-locale.** A generated test language with accented and lengthened text, to find strings that were never
   moved to the message layer, strings that are clipped and fixed slots that overflow.
6. **Fixed slots.** The menus, lists, labels and buttons fit their text into their slot (short form, narrower
   spacing, "…" with the full text in a hint); their mouse areas stay the slots.
7. **Guard tests.** Tests that fail when new literal English strings appear in game logic, so step 1 stays done, and
   tests that fail when a screen tests the mouse against coordinates instead of its layout or its slots.

The restoration's own strings (kind a) move onto the same layer, with their translations kept in the repository.

Related guides: [PACKS.md](PACKS.md), [MODS.md](MODS.md), [SETTINGS.md](SETTINGS.md).
