# The command layer

New code, not the game's. It makes the player's intentions explicit, so that a later interface convenience or a script plays
the game only as a player could: the same keys and clicks, in a valid order, with no time skipped and no random draw added or
left out. Classic play is unchanged: with no command waiting, every screen reads the host exactly as before (the replay of the
816 whole-game sessions agrees as before).

## Two levels

1. **Player commands** (`Darklands.Presentation.Commands.PlayerCommand`): what a hand can do. `PressKey(key)`,
   `ClickAt(x, y)`, `PointAt(x, y)`, and `ChooseLine(n)`: line n of the list the screen is choosing from, which the screen
   turns into the pointer moved onto that line in one pass and a click there in the next (`GameUi.CommandTargets`). These are
   the only things a script or a convenience may issue: `GameUi.Commands.Enqueue(...)`.
2. **Screen intents** (`CardIntent` for the card screens): what an action means on that screen. The input side reads the
   player's action (`GameUi.Poll`) and names the intent (`CardIntent.Of`: `PressOnPortraits`, `PressAt`, `Confirm`,
   `MoveHighlight`, `ChooseByNumber`); the screen carries it out and judges whether it does anything (a highlighted line, the
   pointer over the list, an open box), exactly where the original's loop tests the key and the button.

## How a command is carried out

Every loop that reads the player through `GameUi.Poll` takes the next waiting command instead of the host's input, one per pass,
through the same code as a typed key or a click: a commanded key goes through F12, F10 and the hot keys; a click moves the pointer
with the mouse driver's function 4 (as the game's own cursor keys do) and is a left press there. So a command is a key or a click
made at the screen's next look at the input: one a player could always make. A command never reaches the screen through another
door (no direct call of a menu's choice, no state written by the caller).

`ChooseLine` on a card's item or saint line opens the box and chooses nothing, as the player's click does; the box's entry is the
next `ChooseLine`. While a box is up, `ChooseLine` names the box's lines.

## Screens on the layer (1.0)

| Screen | Intents | Notes |
| --- | --- | --- |
| A card's option menu (`CardScreen.Select`: every city, shop and event menu) | all five | `CommandTargets` = the main list, or the box while one is up |
| A card's wait after a text (`CardScreen.WaitToContinue`) | `Confirm`, `PressOnPortraits`, `PressAt` | |

Every other screen that reads through `GameUi.Poll` takes player commands already (they are a key or a click there too), but reads
its actions without named intents. Loops that read the keyboard or the button themselves (`PollWithoutBar`, `PollKeyOnly`,
`ReadKey`, the battle screen's own mouse tests, the driver's release count) do not take commands yet; a command waits until a
`Poll` loop comes. The battle screen comes last (PLAN-1.0).

## Known gaps

- The keyboard convenience of the card menus (`GameUi.KeyboardCardMenus`, off unless a host turns it on): Up / Down from no
  highlight and the digits 1..9 choose without the pointer, and a digit on an item or saint line chooses the line without an entry
  of its box. It predates the layer and is not player-equivalent; when it is rebuilt on `ChooseLine` it will be.
- A host that cannot move its pointer (`IInput.MovePointer` ignored) keeps its own pointer after a commanded click; the SDL and
  headless hosts move it.

The architecture test (docs/ARCHITECTURE-RULES.md) keeps the views on the path: they read the host only through `GameUi`.
