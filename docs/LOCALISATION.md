# Localisation (translations)

> **Status: designed, not built.** Nothing in this guide can be used yet. The game has no language setting, no
> translation packs and no translation tools today. Localisation is planned for **version 1.2**, after the
> release candidates and the 1.0 and 1.1 work. Everything below describes the plan, so that translators can prepare
> and developers can see what is coming. Names, file layouts and examples are drafts and may change. This guide
> will be rewritten as each part is built.

This guide is for would-be translators first ([how you can help now](#how-translators-can-help-now)), then for
developers ([the plan in steps](#the-plan-for-developers)).

## What exists today

* **No language or locale setting.** The game, the launcher and the console are in English. [SETTINGS.md](SETTINGS.md)
  has no such key.
* **No translation mechanism.** [PACKS.md](PACKS.md) describes picture packs and sound packs only; [MODS.md](MODS.md)
  describes mods that add content. Neither replaces text.
* **Fonts.** The game draws its text with the original fonts, which cover a limited set of characters. There is a
  `textFont` setting (an 8x16 VGA font dump, see [SETTINGS.md](SETTINGS.md)) and a `console.font` setting for the
  console, but they are not a language feature, and no font pack exists.

## The principle: Classic stays the reference

With no translation pack, the game shows the original English text, exactly as it is in the player's own game files and
in `DARKLAND.EXE` version 483.07. Localisation must never change that. Every step of the plan is checked against the
original so that Classic stays exact.

The project does not ship the game's original text. It is read at run time from the files the player already owns, as
the pictures and sounds are today.

## What will be translatable, and what not

There are two kinds of text, and they are handled differently.

**(a) The restoration's own text.** The launcher, the settings screens, the console, and messages the original game
never had. These strings and their translations live in the repository, and anyone may contribute a translation of
them in the normal way (a pull request).

**(b) The game's own text.** Card texts from the `.MSG` files, the strings inside `DARKLAND.EXE`, and the names and
descriptions in the data tables (items, saints, places and so on). A translation of this text is a derivative of the
original game's text, so the project cannot distribute it. These translations will be separate, optional
**translation packs**, kept outside the repository and loaded like the other packs. Where a pack is hosted is up to
its author.

**What cannot be translated by text alone:**

* **Text drawn into the original pictures** (title screens, some panels and signs). It can only be changed by replacing
  those pictures with a picture pack ([PACKS.md](PACKS.md)).
* **Characters the original fonts lack.** Languages that need other characters will need **font packs**. Which
  characters the original fonts cover is part of the groundwork (see the plan).
* **Fixed screen space.** In Classic, the screens, buttons and menus have fixed sizes. A translation that is longer than
  the English must be shortened or wrapped, and a wrapping routine for long translated words is part of the planned
  work. Translators should expect to abbreviate.

**Not supported as a base:** the original German release. It uses a different `DARKLAND.EXE`; only the English 483.07
is supported. A German translation would be a translation pack on the English base like any other.

## How text will be identified

The game logic will stop producing English sentences. It will produce a **message id** plus arguments, and a separate
layer turns that into text. In the original language, that text is built at run time from the player's files. Ids
will be keys such as:

| Key form | Meaning |
|---|---|
| `exe:DS:offset` | a string inside `DARKLAND.EXE`, at an offset in its data segment |
| `msg:FILE#block` | a card text: block number in a `.MSG` file |
| record ids | entries of data tables (an item, a saint, a place) |

The exact key spelling is a draft.

## How a translation pack will look (draft)

Translations use [Project Fluent](https://projectfluent.org/) (`.ftl` files). Which Fluent library for .NET to use
has not been decided; it will be chosen after a trial.

Card texts get one `.ftl` file per `.MSG` file. A card's options are attributes named `.option-N`; a menu hotkey is a
`.key` attribute. Placeholders in braces take the arguments the game passes.

A possible folder layout:

```
my-translation-pack/
  pack.json            name, language code, author, base game version (483.07)
  game/                translation of the game's own text (kind b)
    exe.ftl            strings from DARKLAND.EXE
    msg/
      EXAMPLE.ftl      one file per .MSG file
    tables.ftl         names and descriptions of data records
  fonts/               optional: font pack for extra characters
  pictures/            optional: replacements for pictures that contain text
```

A small illustration. **This is a draft: the ids and fields are invented to show the shape, not a real file.**

```
# game/msg/EXAMPLE.ftl  (DRAFT, illustrative only)

card-12 = You come to a crossroads. { $party } hesitates.
    .option-1 = Take the road north.
    .option-2 = Take the road east.
    .key-1 = N
    .key-2 = E
```

Packs will be chosen the way the other packs are (a setting, a command-line option, a launcher row); none of that
exists yet.

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

1. **Message ids out of the game logic.** Replace literal English strings in the rules with message ids and
   arguments. The English built from them must equal the old output exactly.
2. **String-table extraction.** Read the strings of `DARKLAND.EXE` 483.07, the `.MSG` card texts and the data-table
   names from the player's files, and index them under the keys above. Record which characters the original fonts
   cover.
3. **Fluent overlay loader.** Load `.ftl` files from translation packs over the original-language text. A missing
   message falls back to the original text; the game never shows an empty string. The Fluent library is chosen
   after a trial, before this step. The wrapping routine for long words belongs here.
4. **Pseudo-locale.** A generated test language with accented and lengthened text, to find strings that were never
   moved to the message layer and strings that are clipped.
5. **Guard tests.** Tests that fail when new literal English strings appear in game logic, so step 1 stays done.

The restoration's own strings (kind a) move onto the same layer, with their translations kept in the repository.

Related guides: [PACKS.md](PACKS.md), [MODS.md](MODS.md), [SETTINGS.md](SETTINGS.md).
