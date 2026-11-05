# Data mods: writing a card

For writers. A data mod adds to what the game does: a new **card** (a scene with a picture, a text and choices), or a
different card in place of one of the game's. Unlike a picture or sound pack, a data mod changes the game itself, so a game
played with one is *modded* and no longer plays as the 1992 game would. This page is a short, friendly introduction.
The full reference, with every key and rule, is [MODS.md](../MODS.md).

## Safe by design

A mod is data: one `mod.json` file in a folder of its own. Reading it never runs anything, so a mod cannot harm your computer
or your saved games. It is played whole or not at all: if anything in it is wrong, the game starts without it and the console says
which mod and which field.

## The folder and the file

```text
mods/
  my-first-mod/
    mod.json
    sound/            (optional: music for your cards)
```

Put the folder in a `mods` folder next to the program. `mod.json` allows comments (`//`, `/* */`) and trailing commas:

```json
{
  "name": "My first mod",
  "version": "1.0",
  "description": "What the mod does, in a sentence.",
  "cards": [ ]
}
```

The mod's id is its folder name. Play it by naming it, either way:

* command line: `DarklandsRP --mod my-first-mod` (once per mod; they load in that order);
* settings file: `"mods": ["my-first-mod"]` ([SETTINGS.md](../SETTINGS.md)).

There is no launcher row for mods yet. When the game starts, the console says what each mod added, replaced or refused.

## What a card is made of

The game moves from card to card, and each card says which comes next. The game's own cards are numbered 0 to 365. Your
cards:

* a **new card** takes a number from 366 up (to 32767);
* a card with one of the game's numbers **replaces** that card, and only if it says `"overrides": true`. The game's own
  card stays available to it as `"next": "original"`.

A card has `pages`, run one after another until one leads to another card. A page is one of four kinds:

```json
{ "id": "start", "text": "Some text.", "next": "more" }
{ "id": "ask", "text": "A question?", "options": [ ... ] }
{ "id": "roll", "chance": 25, "next": 366, "otherwise": "original" }
{ "id": "back", "ifPreviousCard": [2, 366], "next": "original", "otherwise": "roll" }
```

* a **text** page shows text over the picture, and goes on to `next`;
* an **options** page shows text with choices, each leading somewhere;
* a **chance** page has no text: `next` with that chance in 100, else `otherwise`;
* an **ifPreviousCard** page has no text: `next` if the card shown before was one of the numbers, else `otherwise`.

An option is `{ "text": "...", "if": { "groschen": 1 }, "do": { "groschen": -1 }, "next": "gift" }`: a line the player picks, an
optional money condition (`if`), what choosing it does (`do`: money, or `hours` that pass), and where it leads.

A new card needs two more things: `savesAs`, the game's own card that a saved game names in your card's place (so
the original game can still load the save), and usually a `picture` (a number or name from `src/Darklands.Cards/PictureNames.cs`;
4 is a city gate).

## A first mod: a card of your own

Here is a small card that nothing leads to yet, and then the card that leads to it. Save it as `mods/my-first-mod/mod.json`:

```jsonc
{
  "name": "My first mod",
  "version": "1.0",
  "description": "A signpost before a city's gate.",
  "cards": [
    {
      // new card: a signpost by the road
      "number": 366,
      "name": "$Signpo0",
      "savesAs": 2,
      "picture": 4,
      "pages": [
        {
          "id": "post",
          "text": "By the road to $PlaceName stands a leaning signpost, its arms carved with the names of towns you have never heard of.\nWhat will you do?",
          "options": [
            { "text": "...rest here an hour.", "do": { "hours": 1 }, "next": 2 },
            { "text": "...walk on to the gate.", "next": 2 }
          ]
        }
      ]
    },
    {
      // the game's card 2 (the city gate by day): one time in four, the signpost comes first
      "number": 2,
      "overrides": true,
      "name": "$SigGt00",
      "pages": [
        { "id": "start", "ifPreviousCard": [2, 366], "next": "original", "otherwise": "roll" },
        { "id": "roll", "chance": 25, "next": 366, "otherwise": "original" }
      ]
    }
  ]
}
```

Card 366 shows picture 4, a text and two choices, and leads back to card 2. Card 2 is the way in: coming from the signpost
or from the gate itself it runs the game's own gate card at once; otherwise it sometimes goes to 366 first. Without the
override, nothing would ever lead to a new card.

Start the game with `--mod my-first-mod` and come to a city by day. Look at the console for lines such as
`card 366 (0x16E): added by mod ...`.

## Texts

Printable ASCII and line breaks (`\n` in JSON). Names such as `$PlaceName`, `$Money1`, `$ChosenOneName`, `$he` and `$his` work as in
the game's own cards (the list is `src/Darklands.Cards/TextVariables.cs`); a `$` always starts a name, so a plain `$` cannot be
shown. Keep texts short enough to fit the card window, as the game's own cards are. A card's `name` is 1 to 12 characters,
without spaces, and not one of the game's.

## Music for a card

A card may name a sound file of your own (`.ogg`, `.wav` or `.mid`, inside the mod's folder):
`"music": "sound/signpost.ogg"`. It plays while the card is shown, in place of the game's piece. The rules for music per card
(and the ten cards that silence music) are in [AUDIO.md](AUDIO.md#music-per-card). A `.mid` needs a SoundFont, which you supply.

## Saved games

A saved game stays in the original format. A new card is never written into the `.SAV`; its `savesAs` card is. Beside it,
`DKSAVE<n>.MODS.JSON` records the mods and the real card numbers. Loaded with its mods, the game goes on where it was;
loaded without them, a message names the missing mods and the game carries on from the card the `.SAV` names. Nothing breaks.
A game once played with a mod stays marked, even after the mod is gone.

## What a data mod cannot do yet

Data cards cannot yet start battles, give or take items, test skills, change reputation or set events. A checker for mods
and a launcher row for mods are planned after 1.0. If you build the program yourself you can write cards in C#:
[MODS.md](../MODS.md#cards-in-c) and [CARD_SCRIPTS.md](../CARD_SCRIPTS.md). The program does not load compiled code from mod folders.

## Where to go next

* Every key, rule and refusal: [MODS.md](../MODS.md).
* A complete small mod with comments: [`docs/mods/wayside-shrine`](../mods/wayside-shrine/mod.json) (in a release: `mods/wayside-shrine`).
* Packs for the same game, which change only how it looks and sounds: [GRAPHICS.md](GRAPHICS.md), [AUDIO.md](AUDIO.md).

## Rights

Write your own scenes. Do not copy the game's own texts into a mod you share. The original's texts and pictures are not free.
