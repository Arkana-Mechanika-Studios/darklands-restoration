# Mods: adding a card

A mod adds to what the game does: new cards (scenes with a picture, a text and choices), or a different card in place
of one of the game's. Where a [pack](PACKS.md) only changes what the game looks or sounds like, a mod changes the game
itself, so a game played with a mod is a *modded* game: its saved games are marked (below) and it no longer plays as
the 1992 game would.

Without a mod nothing of this is used: the game runs exactly its own 366 cards (Classic), which stay the permanent
reference the restoration is checked against.

A mod is data: one `mod.json` file in a folder of its own, with texts, choices and a few effects. Reading a mod never
runs anything from its folder, so a mod cannot harm your computer or your saved games.

## Playing with a mod

Name the mod when the game starts:

* command line: `DarklandsRP --mod wayside-shrine` (one `--mod` per mod; they load in that order);
* settings file ([SETTINGS.md](SETTINGS.md)): `"mods": ["wayside-shrine"]`;
* headless host (for testing): the step `mod:<folder or name>`.

A name is looked up as a folder first, then as a folder of that name in a `mods` folder: next to the program, in the
game folder, or next to the game folder. The release comes with the example of this guide in `mods/wayside-shrine`;
it is not played unless named.

At the start the program writes a line for every card a mod adds or replaces, and for every card it refuses, with the
reason, to the console (the error output):

```text
card 2 (0x2): mod "The wayside shrine" 1.0 (wayside-shrine) overrides the game's card $CityG01
card 366 (0x16E): added by mod "The wayside shrine" 1.0 (wayside-shrine) (saved games name card 2 (0x2) in its place)
```

## How cards are found

The game moves from card to card: each card returns the number of the next one. The game's cards are numbered 0 to
365 (`src/Darklands.Cards/CardTable.cs`); 12 is the travel map, 98 the end of the game.

* **New cards** take numbers from 366 up (to 32767).
* A mod card with one of the game's numbers **overrides** that card, and only when it says so (`"overrides": true`);
  otherwise it is refused. The game's own card stays available to the override (`"next": "original"`).
* The game's table itself is never changed: a registry holds it apart and looks a number up among the mods' cards first,
  then among the game's.
* When two mods use the same number, the mod loaded **later wins**, and a line says which card replaced which.
* 12 (the map), 98 (the end of the game) and negative numbers (battles, quitting) are not cards and cannot be added or
  overridden. A card may still lead to them.

## mod.json

Comments (`//`, `/* */`) and trailing commas are allowed.

```json
{
  "name": "The wayside shrine",
  "version": "1.0",
  "description": "What the mod does, in a sentence.",
  "cards": [ ... ]
}
```

`name` and `version` are shown in the log and recorded with modded saves; the mod's id is its folder's name.

### A card

| key | |
|---|---|
| `number` | the card's number: 366 and up for a new card, 0..365 with `overrides` |
| `overrides` | `true`: replaces the game's card of that number |
| `savesAs` | **new cards only, required**: the game's card a saved game names in this card's place (below) |
| `name` | the card's text name, 1 to 12 characters without spaces (`"$Shrin00"`), not one of the game's; default `$Mod<number>` |
| `picture` | a card picture: its number (the list in `src/Darklands.Cards/PictureNames.cs`, e.g. 4 is `aproch1`, a city gate) or its name; default: none (an override takes the picture of the card it replaces) |
| `pages` | the card's pages, the first one first |

### Pages

A card runs its pages one after another until one leads to another card. A page is one of four kinds:

```json
{ "id": "start", "text": "Some text.", "next": "more" }
{ "id": "ask", "text": "A question?", "options": [ ... ] }
{ "id": "roll", "chance": 25, "next": 366, "otherwise": "original" }
{ "id": "back", "ifPreviousCard": [2, 366], "next": "original", "otherwise": "roll" }
```

* **Text**: shown over the picture; the player reads it and continues to `next`.
* **Options**: the text with option lines under it; each option leads somewhere (below).
* **Chance**: no text; `next` with that chance in 100, else `otherwise`. It uses the game's random numbers.
* **ifPreviousCard**: no text; `next` when the card shown before this one is one of the numbers, else `otherwise`.

`id` names a page for `next` (default `page1`, `page2` ...). A text page may also set its place on the picture:
`x`, `y` (pixels from the card window's corner, default 10, 10) and `width` (default 232).

### Options

```json
{ "text": "...drop a groschen into the bowl.", "if": { "groschen": 1 }, "do": { "groschen": -1 }, "next": "gift" }
```

* `text`: the option line.
* `if` (optional): money the party must have (`florins`, `groschen`, `pfennigs`); without it the line is shown but
  cannot be chosen. At least one option of a page has no `if`.
* `do` (optional): what choosing it does. A text page can have a `do` too.
* `next`: where it leads.

### Effects (`do`)

| key | |
|---|---|
| `florins`, `groschen`, `pfennigs` | money the party gets (negative: pays; never below nothing). 1 florin = 20 groschen, 1 groschen = 12 pfennigs |
| `hours` | hours that pass (up to 168), with everything the game does as time passes |

### Where a page leads (`next`, `otherwise`)

| value | |
|---|---|
| `"gift"` | the page of that id in this card |
| `366`, `2`, `12` ... | that card (a new card, one of the game's, the map) |
| `"original"` | the game's own card this card overrides, as if the mod were not there (overrides only) |
| `"previous"` | the card shown before this one |

### Texts

Printable ASCII and line breaks (`\n` in JSON). The game's text names work as in its own cards: `$PlaceName` (the town
or place), `$Money1`, `$ChosenOneName`, `$he` / `$his` and the others of `src/Darklands.Cards/TextVariables.cs`; a `$`
always starts a name, so a plain `$` cannot be shown. The first letter of a text gets the game's drop capital. Keep
texts short enough to fit the card window: a few lines as in the game's own cards.

## When a mod is not played

A mod is played whole or not at all. Any problem leaves the whole mod out, with a line naming the mod and the field, and
the game starts without it:

* a value of the wrong type (`"number": "400"` instead of `400`, `"hours": "1"`), JSON that cannot be read;
* a page that leads nowhere, a missing `next`, a character the game's font lacks;
* a card the rules above refuse (a new card without `savesAs`, a game number without `"overrides": true` ...);
* a card `name` that is one of the game's message files (also the game's spare texts that no card uses) or another mod's
  card name;
* a link to a card number that no card of the game and no played mod defines (a mod that leads to another mod's card is
  played only with that mod, and only when that mod is played).

So a game with mods never reaches a card number that nothing defines.

## Example: the wayside shrine

`docs/mods/wayside-shrine/mod.json` (in a release: `mods/wayside-shrine`) adds one small encounter, written for this
guide. Sometimes, before a city's gate by day, an old pilgrim keeps a shrine by the road.

**1. A new card.** Card 366 shows the gate's picture (4), a text and three options: give a groschen (only with a
groschen in the purse), pray an hour, or walk on. Each leads to a short text, then to card 2, the gate. A game saved
on this card names card 2 in its saved game (`"savesAs": 2`).

```json
{
  "number": 366,
  "name": "$Shrin00",
  "savesAs": 2,
  "picture": 4,
  "pages": [
    {
      "id": "shrine",
      "text": "A stone's throw from the gate of $PlaceName stands a weathered wooden shrine ... \nWhat will you do?",
      "options": [
        { "text": "...drop a groschen into the pilgrim's bowl.", "if": { "groschen": 1 }, "do": { "groschen": -1 }, "next": "gift" },
        { "text": "...kneel at the shrine and pray a while.", "do": { "hours": 1 }, "next": "prayer" },
        { "text": "...nod back and walk on to the gate.", "next": 2 }
      ]
    },
    { "id": "gift", "text": "The coin rings in the bowl ...", "next": 2 },
    { "id": "prayer", "text": "You kneel on the worn stones ...", "next": 2 }
  ]
}
```

**2. A way in.** Nothing leads to a new number until a card returns it. The mod overrides card 2 (the gate by day)
with a card of two branch pages and no text: coming back from the shrine, or from the gate itself, it runs the game's
own gate card at once; otherwise, one time in four, it goes to 366 first.

```json
{
  "number": 2,
  "overrides": true,
  "name": "$ShrGt00",
  "pages": [
    { "id": "start", "ifPreviousCard": [2, 366], "next": "original", "otherwise": "roll" },
    { "id": "roll", "chance": 25, "next": 366, "otherwise": "original" }
  ]
}
```

**3. Try it.** Start with `--mod wayside-shrine` and come to a city by day (a Quickstart party starting in a city
leaves it and comes back). To see the card at once with the headless host:

```text
Darklands.Host.Headless <game folder> <output folder> mod:docs/mods/wayside-shrine card:16E key:q wait:300 shot:shrine
```

(`card:16E` starts a Quickstart game on card 366.) Save there with Alt-S: the saves folder gets `DKSAVE<n>.SAV` and
`DKSAVE<n>.MODS.JSON`.

## Saved games of a modded game

A saved game stays in the original format, so the original game (and this program without the mod) loads every one:

* No card number above 365 goes into a `.SAV`. Where the game stands on a new card (or remembers one), the `.SAV` holds
  the card's `savesAs` instead (any other number above 365, which a played mod cannot reach, as the map, 12).
* Beside it, `DKSAVE<n>.MODS.JSON` (the companion file) records the mods loaded when it was saved, every mod the game was
  ever played with, and the real card numbers. It carries the SHA-256 of its `.SAV`: if the `.SAV` was replaced since
  (deleted and saved again by the original game), the file is ignored. Deleting a save in the load screen deletes it too.
* Loaded **with** its mods, the game goes on where it was, on the mod's card.
* Loaded **without** them, a message names the missing mods and the game goes on from the cards the `.SAV` names (for the
  example, the gate). Nothing breaks.
* A game once played with a mod stays marked: its later saves get a companion file even after the mod is gone.

Without mods, and for a game never played with one, no companion file is written: the saves are the same as before.

## Cards in C#

The game's own cards are C# classes (`src/Darklands.Cards/Scripts`, one per card, subclasses of `Card`); a data card
runs on the same card screen through the same calls. To add a card in code in your own build, write a `Card` subclass
and register it before the game starts:

```csharp
var mods = ModSet.None();
mods.Cards.Register(new MyCard(), new CardSource("my-mod", "My mod", "1"), savesAs: 2);
game.Mods = mods;
```

`CardRegistry` (`src/Darklands.Simulation/CardRegistry.cs`) applies the same rules as for `mod.json`. A `Card` gets the
whole `CardContext`: the party, the events, the battles, the screens. Such a card's texts are a `MessageFile` added with
`GameAssets.AddMessages`. The program does not load compiled code from mod folders.

## Not yet

Data cards cannot yet start battles, give or take items, test skills, change reputation or set events; a mod checker
and a launcher row for mods are planned after 1.0.
