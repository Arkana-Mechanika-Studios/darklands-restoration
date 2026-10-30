# Architecture rules

`tests/Darklands.Architecture.Tests` checks the dependency direction of ARCHITECTURE.md on the compiled assemblies: their
references, and inside the code every type, field, property and method each type uses (signatures and method bodies; the uses of
lambdas, iterators and nested types count for the outermost type). Each rule holds for the code as it is; a place that breaks a
rule today is a **known exception** with its reason, so the test is green and fails on any new break. An exception that no longer
breaks the rule fails the test too: take it off the list. The lists only shrink.

| Rule | Checked | Known exceptions |
| --- | --- | --- |
| Each assembly references only the layers below it | a table of the allowed references per product assembly; a new project needs its row | none |
| The game (Core, Persistence, Content, Cards, Simulation) depends on no presentation, device or host; nothing references a host; only the hosts use the console, the launcher and the HD backend | assembly references | none |
| The rules (Core, Cards, Simulation) do no I/O and read no wall clock: no file system, console, `System.Random`, `DateTime.Now`, tick counts, `Stopwatch`, `Thread.Sleep` | member uses | `MscRandom`, `TacticalBattle`: tester traces to the console, off in play |
| Views only show and ask: a screen does not write the game model (no field store or property setter on a `Darklands.Core` type) | member uses in Presentation | 14 types: the game's top loop, the battle screen (last), creation and the roster, camp, alchemy, the map, the hot keys, the shops' and the sheet's leader choice, the save dialog |
| Saved games are used only where the game saves and loads (`DarklandsGame`, `LoadGameScreen`, `SaveGameDialog`) | uses of `Darklands.Persistence` in Presentation | `BattleScreen` (combatant records), `CharacterCreationScreen` (a saved game's characters) |
| Views ask the player through `GameUi` only (keys, mouse, commands: docs/COMMANDS.md) | uses of `IInput` / `IHost` in Presentation | none (`GameUi` and `DarklandsGame` are the door) |

The model-writes rule is a proxy: it sees field stores and setters, not a call of a mutating method or an array element written.
Each exception comes off its list when its rule moves into Core or Simulation, normally with the screen's move onto the command
layer.
