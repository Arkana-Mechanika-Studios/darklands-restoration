# Localisation (translations)

> **Status: being built for version 1.1.** A first form of translation packs works in the development branch: the
> program loads a pack and shows its words on the screens and in the card texts it covers (below). The battle screen's
> messages and the names in the data tables are not translatable yet, there is no in-game language screen yet, and the
> file layout may still change before 1.1. This guide is updated as each part is built.

This guide is for would-be translators first ([how you can help now](#how-translators-can-help-now)), then for
developers ([the plan in steps](#the-plan-for-developers)).

## What exists today

* **A language pack setting.** `text.languagePack` in the settings file ([SETTINGS.md](SETTINGS.md)), `--language-pack
  <folder>` on the command line, the environment variable `DARKLANDS_LANGUAGE_PACK`, and the **Language pack** row on
  the launcher's Advanced tab (None, or a pack found in a `packs` folder, or a folder browsed for or dropped on the
  window). None means the original English text, exactly as before.
* **Translation packs** in [Project Fluent](https://projectfluent.org/) (`.ftl` files), read with the Linguini library
  ([below](#how-a-translation-pack-looks)).
* **What a pack translates now:** the screens' own words (menus, labels, lists, prompts, notices and hints: everything a
  screen asks for by key), the entries of the original's word tables the screens show (months, canonical hours, fame
  titles, standing words, directions, town sizes, shop names, job names, skill names, risk words, the character
  sheet's labels), the program's own notices (a damaged saved game, missing mods ...), the **card texts** (the `.MSG`
  blocks, [written as below](#card-texts)) and the words the cards put into them (months, hours, lord titles, patron and
  shop names, the cards' own words such as a physician's verdict).
* **Not yet:** the battle screen's messages, item, saint and place names, and the program start's messages. These keep
  the original English for now.
* **Fonts.** Without a font pack the game draws a translation with the original fonts, which have no letters beyond the
  original's set (the German edition's umlauts and ß are among them); a letter they lack is drawn as `?`. A pack can bring
  fonts of its own with any letters ([font packs](#font-packs)).

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
  the English must be shortened or wrapped. Translators should expect to abbreviate; the template gives each text the
  width of the box the original draws it in, and the pack check flags a translation that does not fit
  ([below](#the-translators-template-and-the-pack-check)).

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
| `msg:$FILE#block` | a card text: block number in a `.MSG` file (`msg:$ADDCH00#0`, Fluent id `msg-addch00-0`) |

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
  pack.json            { "language": "de", "name": "Deutsch", "author": "…", "base": "483.07" }
  text/
    de/
      exe.ftl          the EXE's strings and the word tables
      ui.ftl           the restoration's own notices
      msg/
        $CITYG04.ftl   card texts, one file per .MSG file (the file names are free; this one is the convention)
```

`pack.json` must name the `language` (a language tag: `de`, `fr`, `pt-BR`; `qps` is the pseudo-locale); `name`,
`author` and `base` (the game version translated: `483.07`, the only one supported) are optional. The Fluent files are
under `text/<language>/`, the layout of the mods' text, so a language pack will also load unchanged as a cosmetic mod
in 1.2. Every `.ftl` file there is read, in path order (the file names are free); a message id defined twice keeps the
first. A key the pack does not have shows the original English, so a pack may be partial. What is wrong
with a pack (a Fluent syntax error with its file and line, an unreadable manifest) is reported on the console's error
output and in the launcher's line for the pack; the valid entries are still used.

A small example. **The ids are real; the German is only an illustration.**

```
# text/de/exe.ftl
exe-ds-2ff2 = Verlassen
exe-ds-2f71 = { $arg0 } verwaltet die Börse: { $arg1 } Gulden, { $arg2 } Groschen, { $arg3 } Pfennige.
# the months (290E:1D43 points at 290E:0000 "January" ...)
exe-290e-0000 = Januar
```

## Card texts

A card text is one Fluent message per block. Its control codes are written as tags:

| Tag | What it is |
|---|---|
| `<br>` | the end of a line |
| `<p>` | a paragraph (an empty line) |
| `<opt>` | the start of an option line the player chooses |
| `<opt-items>`, `<opt-saints>`, `<opt-battle>` | an option line that opens the item list, the saints' list, or starts a battle |
| `<indent>` | the rest of the line, and its following lines, start here (the hanging indent after "...") |
| `<if-1>` … `<endif>` | a passage the card shows or leaves out by its condition 1 (`<if-2>` the block's second passage, condition 2 …) |
| `<x19>` | any other code byte, by its number |

`$Names` (`$ChosenOneName`, `$MonthName`) stay as written: the game puts the words in. A line break in the `.ftl` file and the
spaces round it count as one space, or as nothing next to a tag, so a long text may be wrapped freely. As Fluent requires,
a line of the file may not start with `.`, `[` or `*`: start it with a tag or end the line before such a character.

```
# text/de/msg/$ADDCH00.ftl  (the German is only an illustration)
msg-addch00-0 =
    Ihr verbringt einige Zeit damit, mehr zu erfahren über...<br><p>
    <opt>...<indent>einen alten Jugendfreund.<br>
    <opt>...<indent>einen Söldner.<br>
```

**What must stay as the original's**, or the block is not used (the original text is shown, and the problem is reported
on the console's error output):

* the option lines: the same number, in the same order, of the same kinds (the menu chooses by line);
* the passages: the same `<if-N>`, in the same order (the game counts a block's passages to know which condition each
  tests, so they cannot be reordered);
* the same `$Names` and the same `<xNN>` bytes (in any order, any number of times).

`{` and `}` are Fluent's own: in a card text write them as `{"{"}` and `{"}"}` (the English texts hold none).

## The translator's template and the pack check

The program writes the template of your own game files, and checks a pack against them:

```
DarklandsRP <game folder> --translator-template <folder>   [--text-boxes <file or folder>]
DarklandsRP <game folder> --check-language-pack <pack folder>   [--text-boxes <file or folder>]
```

**The template** is a pack of the original English (`pack.json` and `text/en/`: `exe.ftl`, `ui.ftl`, `msg/<FILE>.ftl`),
every text a pack can translate today. Copy its files into your pack's `text/<language>/` folder and write your words
after each `=`. Above each message a comment gives its key, its box and what each argument is:

```
# exe:DS:2F71 · box 148 px (English 131 px); $arg0: the leader's short name, $arg1: florins, $arg2: groschen, $arg3: pfennigs, $arg4: the whole purse in pfennigs
exe-ds-2f71 = { $arg0 } ...
```

* **The box** is the room the screen gives the text: up to the edge of its window ("box"), or up to the screen's right
  edge where the screen sets no window ("room ... to the screen's edge"); "box ?" is a text not drawn in the sessions the
  boxes were collected from. A card text's box ("box: 12 lines of 230 px") is its block: the lines it has down to the
  foot of the card's window and the width they wrap in.
* **The arguments.** A format of the original (`%s`, `%d`) has one `$argN` per value it prints, in the original's order;
  each is the value as the original prints it (a number padded to the width the English gives it stays padded), so a
  translation may put them in any order or leave one out.
* **Odd characters** are written as Fluent string literals: `{"{"}` and `{"}"}` for braces, `{" "}` for a space at a
  text's start or end (Fluent drops those), `{"<"}` and `{">"}` for angle brackets.
* **Colours and hot keys.** A colour code of the original is a tag: `<c12>` draws what follows in colour 12, `</c>` goes
  back to the line's colour. The menus mark a hot key's letter this way (`<c12> M</c>ore batches`). Every menu of the
  template tests its keys in the program, whatever the text says, so the comment says "fixed in the code": keep the same
  letter coloured (`<c12> M</c>ehr Chargen`), or the coloured letter would point at a key that does nothing. The check
  requires it. The same tags may stand in card texts.
* Texts are only written from your game files: the template is yours and is not to be shared as part of a pack unless
  it is your translation.

**The check** loads a pack and lists, with each file and line:

* errors (the pack's text is not used, or not as meant): a Fluent syntax error, an id no text of the game has, a
  variable the screen does not give, a card text whose option lines, passages, `$Names` or code bytes are not the
  original's, a character the game's font cannot draw (the fonts have the game's ä ö ü Ä Ö Ü ß; other letters need a font
  pack);
* overflows: a text wider than its box (measured in the font the screen draws it in, with the arguments a session drew
  it with), a card text that runs past the foot of the card's window (laid out with every passage shown and the `$Names`
  as written);
* notes: an argument the translation does not use.

It ends with a count; the exit code is 1 when there are errors. The boxes come from `--text-boxes`, else from a
`boxes.json` in the pack's folder (the template writes one when it was given boxes), else there are none (then no
width is checked).

**Collecting the boxes** (developers): a development build of the headless host (`DevTools=true`) run with the
environment variable `DARKLANDS_TEXT_BOXES=<folder>` writes the box of every keyed text it draws to
`<folder>/boxes-<process>.json` when it ends; the recorded sessions replayed with it (`tools/oracle/rerun_all.py
--headless <that build> --fights none --replay-only`) give the boxes of the screens they visit. The files hold keys and
numbers (and the values a session drew), never the game's words. The keys of a screen can also be found with the
developer's inspector (a development build: hold F9 over a word; see [SETTINGS.md](SETTINGS.md)).

## Font packs

A translation pack can bring its own fonts, for letters the original fonts lack or for another look. They are in its
`fonts/` folder, one pair of files for each of the game's three font slots it replaces (slot 1: the side window, the
screen pages and the menus; slot 2; slot 3: the card window):

```
fonts/slot3.json   { "image": "slot3.png", "height": 7, "lineSpacing": 1, "spacing": 1,
                     "glyphs": { "A": [0, 0, 5], "é": [6, 0, 4], "ł": [11, 0, 3] } }
fonts/slot3.png    the glyphs; a pixel is drawn where its alpha is 128 or more
```

Each glyph is `[x, y, width]` in the picture; its height is the font's. The glyphs are drawn in the screen's colour, as
the original's are. A font of a pack has its own metrics: with it every text of its slot (the translation's and any
left in English) is measured and laid out again, and the pack check measures in it. A letter neither the pack's font
nor the original's has is drawn as `?`.

**The text path.** Without a pack, and for every text that the original's bytes can hold, the game keeps the original's
drawing exactly (Classic). A translation with other letters, or any text in a pack's font, takes the Unicode path: the
same layout (the card texts' word wrap, passages, option lines and indents are the same loop), with the letters as
Unicode and the control codes as tokens.

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
4. **Fluent overlay loader.** (Built in 1.1: packs, the setting, the launcher row, the screens' words and the card texts.) Load `.ftl` files from translation packs over the original-language text. A missing
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
