# Localisation (translations)

> **Status: being built for version 1.1.** A first form of translation packs works in the development branch: the
> program loads a pack and shows its words on the screens and in the card texts it covers (below). There is no in-game
> language screen yet, and the
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
  shop names, the cards' own words such as a physician's verdict), and the **names of the data files**
  ([below](#names-of-the-data-files)): items, saints, formulae, places, cities and their buildings and people, enemies.
* **The battle's words:** its messages (a split party, travelling as a group, a lock or trap's outcome, the surrender
  question ...), its menus and the enemy panel's weapon, armour and shield types and quality words, under their `exe:`
  keys; the battle's far texts (the type names, the ordinals of "Select …", the "cannot view" notice) as `exe:286B:XXXX`.
  Without a pack the battle is the original's to the byte: the replays prove it.
* **Not yet:** the descriptions in the data files (saints, formulae, places) and the program start's messages. These
  keep the original English for now.
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
| `exe:286B:XXXX` | a string of the battle's far segment (link frame 286B): its weapon, armour and shield type names, ordinals and notices |
| `exe:DS:XXXX` | a string inside `DARKLAND.EXE`, at offset `XXXX` of its data segment (each copy the original keeps has its own key) |
| `exe:290E:XXXX` | a string of the EXE's far string segment |
| `id:rw.…` | a sentence of the restoration's own (the original has no such text): `id:rw.save.damaged.1` |
| `id:card.…`, `id:skill.changed` | a sentence the original builds from pieces: the chance of an option, a skill report |
| `msg:$FILE#block` | a card text: block number in a `.MSG` file (`msg:$ADDCH00#0`, Fluent id `msg-addch00-0`) |
| `lst:item/n.name`, `.short` | an item's name and short name, by its code in `DARKLAND.LST` (`lst-item-42-name`); `lst:saint/n.…`, `lst:formula/n.…` the same for saints and formulae |
| `cty:n.field` | a city's names in `DARKLAND.CTY`: `short`, `full`, `leader`, `ruler`, and its buildings (`church`, `market` … the names cards put in) |
| `loc:n.name` | a place's name, by its index in `DARKLAND.LOC` |
| `enm:type/n.name` | an enemy kind's name in `DARKLAND.ENM` (the battle's enemy panel) |

In a `.ftl` file a key is written as a Fluent identifier: lower case, `:` `.` `/` `#` `+` turned into `-`, `$` dropped.
So `exe:DS:2FF2` is `exe-ds-2ff2` and `id:rw.save.damaged.1` is `id-rw-save-damaged-1`. Two keys never meet in one
identifier.

**Arguments** become Fluent variables. A named one keeps its name (`{ $count }`, `{ $name }`). Many of the original's
strings are C format strings (`%s holds the purse of %dfl, %dgr, %dpf (%lupf).`); their arguments are, in order,
`{ $arg0 }`, `{ $arg1 }` …: the translation writes the sentence with those variables and needs no `%` codes.

## Names of the data files

The names in the game's data files (items, saints, formulae, places, cities with their buildings and people, enemy
kinds) are in the template's `tables.ftl`, each under its record and index (the keys above), the English from your own
files and the field's width in the original's file in the comment. They are translated where the game shows them: the
character sheet, the stores, barter and loot, alchemy, party information, the card option pop-ups, the saint prayer screen, the battle's enemy panel
and its place notes, and the names cards
put into their texts (`$cityChurch`, `$PlaceName`, `$LocName`, `$ItemName` ...). The rules and the saved games keep the
original's names: a saved game written with a pack is the same as one written without. Names a save holds as text (a
place's, a battle's enemies) are translated while they are still the data file's; the party's names, which the player
typed, are never translated.

**A name's other forms.** A language that declines its nouns or makes words agree with them gives a name its forms as
Fluent attributes: `nom`, `gen`, `dat`, `acc`, `ins`, `loc`, `voc`, `abl` (the cases a language needs) and `gender`:

```
lst-item-7-name = Кинжал
    .acc = Кинжал
    .gender = m
```

Where the game puts a name into a sentence (an item in a store's line, on the sheet's pack list, in alchemy), the sentence
gets the name as `{ $arg0 }` and each form as `{ $arg0_acc }`, `{ $arg0_gender }` …, so a translated sentence can write
`{ $arg0_gen }` or `{ $arg0_gender -> [f] … *[m] … }`. A name without a form gives its words for a case and nothing for the
gender (the default variant). The pack check accepts the forms of an argument the sentence has; a pack that needs none
writes plain names.

## How a translation pack looks

A pack is a folder:

```
my-translation-pack/
  pack.json            { "language": "de", "name": "Deutsch", "author": "…", "base": "483.07" }
  text/
    de/
      exe.ftl          the EXE's strings and the word tables
      ui.ftl           the restoration's own notices
      tables.ftl       the names of the data files (items, saints, places ...)
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
| `<lt>`, `<gt>` | a `<` or `>` of the text itself |

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

**Long card texts.** A translation longer than the card's window can hold is shown in pages: each page but the last ends
with a prompt at the foot of the text ("(more ...)", `id:rw.card.more` in `ui.ftl`, translatable like any text), and a key
or a click turns the page, as the press-to-continue of any card text. The option lines are together on the last page,
with their keys and mouse areas as always; a last page of option lines only is ready to choose from at once. A page ends
between lines, or at a space in a paragraph taller than a page, never inside a word; the passages are chosen first, so
none is split. Only a pack's text is ever paged: without a pack the game is the original's.

**A pack's own variables.** A language may need words the English `$Names` do not give, such as a pronoun in each of its
grammatical cases. A pack defines them in its `.ftl` files as `var-<name>` messages, chosen by the chosen character's sex
(`$sex` is `female` or `male`); its card texts may then use `$<name>` beside the English block's `$Names`:

```
# text/de/ui.ftl  (German: the dative pronoun)
var-him1 =
    { $sex ->
        [female] ihr
       *[male] ihm
    }

# text/de/msg/$ARMOR00.ftl
msg-armor00-12 = ... Ihr gebt $ChosenOneName $him1 ...
```

A name is letters and digits, and its case counts (`var-Him1` beside `var-him1`). The check allows in a card text exactly
the English block's `$Names` and the pack's own (a translation need not use them all); any other is an error, and so is a
variable that uses anything but `$sex`. As Fluent requires, each variant of a choice is on a line of its own.

## The Workshop's text editor

`DarklandsWorkshop` (a download of its own beside the game's; put it in the same folder as `DarklandsRP`) is the
translators' editor. It finds your game folder as the launcher does (or the game folder button at the top right), and
starts on its pack list: every translation pack it finds in the program's `packs` folder (the one the launcher lists),
in the game folder's `packs` folder and among the packs you opened, each with its language, name, author, progress and
when it last changed, the last one you opened first (**Enter** continues with it). Under the project's banner are the
version ("Restoration 1.1.0", and the game program's version beside the Workshop when it differs) and the project's
website, GitHub and Discord. **Packs** at the top left leads back to the list from a pack.

* **New pack** (**Ctrl+N**) asks for the language from a list of the usual ones (search by its own name, its English
  name or its tag; **Other language…** takes any well-formed BCP 47 tag and its name), a pack name and your name, and
  makes the pack in the program's `packs\<tag>` folder (**Browse…** for another). When the language has letters the
  game's fonts lack, **Build a matching font** is ticked: the pack gets the font in the game's pixel style at once.
  **Open folder…** (**Ctrl+O**) opens a pack from anywhere.
* The list holds every text a pack can translate, in sections by where the game shows it (cards, screens, names, and
  "not seen": a text no recorded session showed), and in each section by card or screen, under a plain title ("City
  church by day", "Barter") with its share translated (a bar and "1/10"); a click folds a group open or shut. Each text
  is named by its first words. Filter the list (**All**, **To do**, **Review**, **Problems**) and search the English,
  your words or the key. Each row's mark is its state: a green dot translated, an amber dot marked for review, a red
  dot translated with a problem the check found, an empty ring not translated yet. The counter reads "12 of 4,917
  translated · 3 shown".
* **Needs review** marks a translated text to look at again (yours to doubt, or a reviewer's to check). The marks are
  kept in the pack's `workshop.json`, the Workshop's own file (the game does not read it), and saved with the pack.
* For the text chosen: its title and first words above, then its kind, key and the template's comment (its box, what
  each argument is), its state, the English, and your translation, written as the pack's file holds it (the same tags
  and placeables as the template). In the
  English, in your translation as you type and on the chips, each kind of tag has its colour: grey the line ends (`<br>`, `<p>`), blue the option lines
  and indents (`<opt>`, `<indent>`), green the `$Names` and arguments, red the colour tags.
* **Card texts in rows.** A card text opens in its **Rows** view: the introduction, each passage that shows only when
  its condition holds, and each option line (numbered) in a field of its own, the English on the left and yours on
  the right (on a narrower window, yours under each English line, so the card's preview keeps twice the game's pixels). You write only the words:
  a new line is a line break, a blank line a new paragraph, and the option markers, the "…" lead-ins and the indents
  are the English's, written for you. **Raw** shows the text as the pack's file holds it, its tags included (for
  experts, and for a translation whose lines are not the English's: another number of options or passages opens there).
  Both views write the same file, and the check judges it as always.
* **As the game draws it:** the text in the game's font (your pack's font when it has one), by the game's own drawing
  code, in whole multiples of the game's pixels as large as the window allows: a card text's window beside your words, a
  screen text under them; a screen text on one line with its box's edge dotted and a bar (green: no wider than
  the English, amber: wider but inside the box, red: too wide), a card text laid out in its block with every passage shown
  against the foot of the card's window. It follows your typing.
* **Layout** (under a screen text's preview) changes where your pack draws that text, when its words need more room than
  the English. You can set:
  * **Width:** a wider box, but no wider than the room the text has.
  * **Lines:** 1 to 3; the text breaks at spaces.
  * **Font:** another of the pack's font slots, for example a narrower one.
  * **Move:** up to 16 pixels each way, from where the original draws the text.

  **Reset** puts the text back where and as the original draws it.

  **Clicks and highlights follow the layout.** A menu line or list row answers the pointer and lights up where it is
  drawn, and the place it left answers nothing.

  **The file.** The changes go into the pack's `interface/layout.json`, by screen, then text key:
  `{"screens": {"BarterScreen": {"exe:DS:2FF2": {"y": 35, "width": 200, "lines": 2}}}}`. It holds plain places and sizes in game
  pixels: `x`, `y`, `width`, `lines`, and `font` (0, or a slot from 1 to 3). This is the place and form the mod spec reserves
  for interface layouts (docs/modding/SPEC-DRAFT.md).

  **The check.** It holds a translation pack to the language-pack limits: the room the text had, a move of at most 16
  pixels, 1 to 3 lines. It reports a layout that breaks them, runs off the screen, or covers another text of the same
  screen. An interface mod will have looser limits on the same file.

  Texts with no layout, and the game without a pack, are drawn exactly as the original draws them. Screens are added one by
  one: today the item store, barter and loot screens.
* **Hot keys.** A menu's coloured key letter (`<c12> M</c>ore batches`) shows as the game draws it, the letter coloured
  and underlined, in the list, the English and your translation; its tags are kept in the text (the cursor steps over
  them, and deleting the letter shows them again). A screen text with one has a **Text** | **Raw** switch: **Raw**
  shows the tags as the pack's file holds them.
* **Chips:** the English's tags, `$Names` and arguments (and your pack's own variables, for a card text) are buttons under
  your translation (**Insert**); a click, or **Alt+1** to **Alt+9**, puts one where the cursor is. Below it, the editor names the chips you left out and any that
  are not the English's. A translation that is a choice (a plural, a variable's `$sex` choice) shows one field a variant.
* **Glossary** keeps your pack's agreed terms (`glossary.tsv` in the pack's folder; the game does not read it). The
  terms a text's English holds are listed above your translation, marked when your words do not use the agreed one.
* **Variables** edits your pack's own variables (the female and the male words of each), written to
  `variables.ftl`.
* **Play in game** saves, then starts the game with the pack **at the text chosen**, as a translation test. A Quick Start
  party, always the same one, starts at the card of a card text, at the text's own passage, or with the screen of a
  screen text open (sheet, store, barter, loot, alchemy, party info, prayer, roster, camp, map, battle, character
  creation …). The game stays playable from there.
  * **Reaching a later passage.** The Workshop runs the card without a display beforehand, trying its menu's choices
    (up to 6 menus deep, following it into the next card when that card shows the same texts) on a few plausible states
    (the Quick Start's, the option flags set, a rich purse, night, day, another city). It then replays the choices that
    lead there.
  * **A passage that needs story state.** A passage none of these reach (a quest given, being wanted in the city …) opens
    at the card's start with a short notice; the Workshop's preview shows every passage.
  * **Saves** go to the pack's own `.workshop\saves` folder, never your games' (zip the pack without `.workshop` to share
    it).
  * **The window's title** says it is a translation test.
  * The small menu beside the button has **From the start** (the title screen, as usual).
  * **On the command line** it is `DarklandsRP --language-pack <pack> --start-at card:$CITYC00#4` (or
    `screen:BarterScreen`), accepted only with a language pack.
* **Save** writes the pack's `.ftl` files in the template's layout (only the texts you translated; your pack's own
  variables and anything else in its files are kept). **Check** runs the game's pack check; its findings open below the
  editor (the panel folds away), and a double-click on one shows its text. **Play in game** starts
  `DarklandsRP --language-pack` on it (at the text chosen, above).

**Keys.** **Ctrl+Enter** accepts the text (clears its review mark) and goes to the next untranslated one;
**Ctrl+↓** and **Ctrl+↑** go to the next and the previous untranslated text; **F8** and **Shift+F8** to the next and the
previous text with a problem; **Ctrl+F** to the search; **Ctrl+S** saves.

A pack written by the Workshop is a plain pack: the game reads it as it is, and you can still edit its files by hand.

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
  it with), a card text that runs past the foot of the card's window and cannot be shown in pages (its option lines alone
  do not fit, or a word is wider than the column);
* notes: an argument the translation does not use; a card text the game shows in pages ("laid out in 2 pages").

`boxes.json` in the pack's folder (the template writes one when it was given boxes), else from the boxes the program ships
(below).
width is checked).

**The pseudo-locale** shows where a longer language will not fit, before any translation exists:

```
DarklandsRP <game folder> --pseudo-language-pack <folder>   [--text-boxes <file or folder>]
```

writes a `qps` pack of every text a third longer, its vowels marked (a → ä, o → ö, u → ü, so a text from the pack is seen at
once and the game's fonts draw it), its tags, `$Names`, arguments and hot keys kept, and lists with the check where it
does not fit. Play with it (`--language-pack <folder>`) to see the screens in it.

**Another edition's texts.** If you own another edition of the game (the German one), the program can make a pack of
its card texts for your own use, to see a long language on the screens:

```
DarklandsRP <game folder> --edition-pack <folder> --from-edition <the other edition's folder> [--edition-language de]
```

It takes each block by its key (the same `.MSG` file and block), keeps those that keep the English block's structure,
says how many were left out and why, and checks the pack. Such a pack holds that edition's text: it is yours, not one to
share. `--edition-variables <file.ftl>` adds a file of the pack's own variables (above). (The German edition's card texts,
with its fifteen pronoun variables: 3,159 of 3,753 blocks taken; the others use other `$Names` or option lines, or are
blocks the German edition does not have.)

**Collecting the boxes** (developers): a development build of the headless host (`DevTools=true`) run with the
environment variable `DARKLANDS_TEXT_BOXES=<folder>` writes the box of every keyed text it draws to
`<folder>/boxes-<process>.json` when it ends, with the screens each text was drawn on (the translators' editor groups the
texts by them); the recorded sessions replayed with it (`tools/oracle/rerun_all.py
--headless <that build> --fights none --replay-only`) give the boxes of the screens they visit. The files hold keys and
numbers (and the values a session drew), never the game's words. The keys of a screen can also be found with the
developer's inspector (a development build: hold F9 over a word; see [SETTINGS.md](SETTINGS.md)).

**The boxes the program ships.** The program and the Workshop carry the boxes of the last such run without the game's words (each
recorded argument replaced by filler of its length, so a translation is measured about as the screen drew it); a
pack's own `boxes.json` wins over them. A developer regenerates them after the run with
`DarklandsRP --text-boxes <the run's boxes folder> --strip-text-boxes src/Darklands.Presentation/Resources/boxes.json`;
a test checks that the file holds no text of the game's files.

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
the original's are.

A slot can also take a **TrueType or OpenType font**, drawn sharp at the window's resolution:

```
fonts/slot3.json   { "ttf": "MyFont.ttf", "size": 8, "spacing": 0, "lineSpacing": 1 }
```

`size` is the height of a line (ascent to descent) in the game's 320-wide pixels; each letter's advance is rounded to whole
pixels, so the game lays the text out as it does with any font. The game's 320x200 picture gets the letters at that size;
the window, the screenshots and the HD renderer's pictures draw them again at their own resolution, in the colour the
picture shows (option lines, highlights and the fades included). Known limits: no hinting and no shaping of complex
scripts, so Latin, Greek and Cyrillic fonts work, while Arabic or Indic scripts would need shaping later. The card's
option lines, invisible on the picture until it fades, may show faintly in the window during that moment. A font of a pack has its own metrics: with it every text of its slot (the translation's and any
left in English) is measured and laid out again, and the pack check measures in it. A letter neither the pack's font
nor the original's has is drawn as `?`.

**Pixel fonts in the game's own style.** The program makes such a pack from your own `FONTS.FNT`:

```
DarklandsRP <game folder> --make-font-pack <folder> [--scripts latin,greek,cyrillic]
```

It writes `fonts/slot1..3` (the game's three fonts) into the folder, usually your language pack's. Every glyph of the
game's fonts is kept as it is. Accented Latin letters (Latin-1 and Latin Extended-A: French, Spanish, Portuguese,
Italian, Polish, Czech, Hungarian, the Nordic languages ...) are made by putting the accent onto the font's own letter,
in two rows where the letter leaves room above it, else in one; a capital gives up a row for its accent, as the game's
own Ä Ö Ü do. Greek and Cyrillic letters that look like Latin ones (А, Ο ...) are the font's own; the others are the
project's own pixel drawings fitted to each font's rows. `charts/` holds a chart of every glyph of each font and script
(four times the pixels, the letters in order in a .txt) for judging them. Such a font has the game's metrics for the
game's letters, so English text in it draws exactly as the game draws it.

The Workshop builds the same pack for you: **Font pack…** in its toolbar writes it into the pack open (Latin, and Greek or
Cyrillic for the languages written in them), shows the charts, and the preview uses the new letters at once. When a new
pack's language has letters the game's fonts lack (French à, Polish ł, any Greek or Russian letter ...), the Workshop
offers to build it, and until then the preview names the letters it draws as `?`.

**Decorated capitals.** A card text starts with a decorated capital; the game has pictures for A–Z only. A first letter
outside A–Z takes the picture of its Latin letter (É: E, В: B, Α: A); a capital with no such letter (Φ, Ж, Ψ) is drawn
at twice the font's pixels in a square of the pictures' own colours, the text beside it as beside a decorated one.

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
  hint. The pseudo-locale ([above](#the-translators-template-and-the-pack-check)) lists every slot that overflows, so
  translators know where to abbreviate.
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
