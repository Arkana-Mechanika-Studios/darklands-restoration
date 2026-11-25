# The switch register

[switches.csv](switches.csv) lists every switch of the game: each place where the code can take the original's path or a
changed one, and the setting or flag that chooses. Nothing in the register changes the game; it records what the code has
today, and the few fixes already decided for a later step (`planned` rows). (Plan: the post-1.0 keyed flag table builds on it, so a
row's `id` is the key it will keep.)

The rules it serves: every departure from the original sits behind a switch with an id, and the original path stays beside it;
the faithful game stays the reference the oracles check; two kinds of fix are on in every player mode, each kept
exact behind its switch for the oracle (data-damaging bugs and plain visual glitches).

## Columns

| Column | Meaning |
|---|---|
| `id` | The stable key, lower case, dotted: `<family>.<area>.<name>`, words joined by hyphens. Never renamed once released. Families: `fix` (a fixed original bug), `restore` (content the original holds and never offers, [RESTORATION.md](RESTORATION.md)), `view` (what is drawn), `comfort` (how orders are given), `content` (mods and packs). |
| `kind` | `flag`: a property of `OriginalBehaviour` (`src/Darklands.Core/OriginalBehaviour.cs`), set per game. `setting`: a key of the settings file ([../SETTINGS.md](../SETTINGS.md)), set per player. `planned`: a fix decided for a later step of the plan and not in the code yet; its key is the flag it will be, and the row becomes a `flag` row when the flag is added. |
| `key` | The name in the code: the property (`FixFameCost`) or the settings key (`input.mouseWheel`). |
| `class` | The change class of the plan's table: `data-safety` (a bug that damages saved games or stores or ends the program: on in every player mode), `visual` (an evident drawing glitch: on in every player mode), `gameplay` (an original bug that changes what the game decides: in the Fixed rule set), `restoration` (content the original has and never reaches, restored: in the Restored rule set only, its `register` the row of [restoration.csv](restoration.csv)), `interface-comfort`, `presentation`, `content-mod`. (`new-capability` is allowed for later rows; none exist yet.) |
| `axis` | `Rules` (what the game decides), `Presentation` (what is seen and heard), `Interface` (how the same orders are given). |
| `presets` | The rules presets the switch is on in: `original`, `fixed`, `restored` joined by `;`, or `any` for a choice independent of the rules preset. Restored always includes Fixed. For a flag the column is the code: `tools/faithfulness/rule_table.py` turns it into `src/Darklands.Core/RuleTable.Generated.cs`, the presets' table (`OriginalBehaviour.Of`). |
| `default` | The state in the player's game of this release: for a flag, whether a new game at the default rules preset (Fixed, `RulePresets.Default`) has it on; for a setting, the default of the settings file. A game with the Original preset (`OriginalBehaviour.DataSafe`) has the data-safety and visual flags on; the faithful game (`OriginalBehaviour.Faithful`, which the oracles and the headless host use) has every flag off. |
| `register` | The ids of the entries of [original_bugs.csv](original_bugs.csv) the flag fixes (`;` joined; their `fix_flag` column names the flag). For a `planned` row the bugs it will fix (kept as the original today: no fix flag, decision `later`, the note naming the planned flag). For a `restoration` flag the `R-` ids of [restoration.csv](restoration.csv) it restores (rows of status `built`). Empty for settings. |
| `code` | The files where the code chooses between the two paths (up to three, `;` joined). A flag's name is in at least one of them. |
| `description` | One line: the original's behaviour and what the switch does. The long account is the flag's comment in the source and the register entries. |

## Counts (at the time of writing)

73 flags, 12 settings and 1 planned fix, 86 rows:

| Class | Rows | Of which on in the player's game now |
|---|---|---|
| data-safety | 28 | 28 |
| visual | 3 flags + 1 setting | the 3 flags (the setting `view.clean-drawing` is off by default) |
| gameplay | 40 flags + 1 planned + 1 setting | the 40 flags in a new game (the Fixed rule set, the default of `rules.preset`; none with Original); the player's list: [../FIXED.md](../FIXED.md) |
| interface-comfort | 3 | 3 (the Interface switch is Comfort by default) |
| presentation | 6 | choices, none changes a rule |
| content-mod | 1 | no mods by default |
| restoration | 2 | none (the Restored rule set only, not offered to players yet) |

## How it is kept true

`tests/Darklands.Core.Tests/SwitchRegisterTests.cs` fails when:

- a boolean `Fix…` or `Restore…` property of `OriginalBehaviour` has no row, or a row names a flag that no longer exists;
- a flag's `default` differs from its state in a new game at the default preset (Fixed), or its `presets` disagree with its class
  (only `data-safety` and `visual` fixes are on in `original`; every fix is in `fixed` and `restored`; a `restoration` is in `restored` only);
- the generated rule table (`RuleTable.Generated.cs`) differs from the flag rows (run `tools/faithfulness/rule_table.py`);
- a flag's `register` ids differ from the entries of `original_bugs.csv` that name it, or (a `restoration`) from the `built` rows of
  `restoration.csv` whose note names it;
- a code location does not exist, or (for a flag) none of its files mentions the flag;
- a key of the table in `docs/SETTINGS.md` is neither a `setting` row nor on the test's short list of keys that are not
  switches (paths, window, sound hardware, start-up, developer console), so a new setting has to be decided;
- an id is malformed or repeated, or a class, axis or preset is not one of the allowed words;
- a `planned` row names a flag that already exists, is not off, or names bugs that are not kept as the original with a decision
  `later` and a note naming the planned flag.

To add a switch: add the flag (or settings key), give it a row here, keep the original path beside the changed one, and
run the test. A removed switch loses its row in the same change.

## Not switches (and not yet switchable)

Some departures from the original have no switch of their own. They are listed so the later flag table can decide each:

| Where | What it does | Note |
|---|---|---|
| `SdlHost.cs` (pointer outside the window) | The game's drawn pointer is hidden while the mouse is outside the window | host only, no game value depends on it |
| `CharacterGeneratorScreen.cs` | A right click on an attribute or skill row lowers it (the key `-`) | interface comfort, always on |
| `GameUi.cs` (pressed button) | A pressed button's picture is held a minimum time on fast machines | host timing, always on |
| `MessageBox.cs` | The restoration's own notices (a saved game whose mods are not loaded; a refused load or Put) | own text, shown only where a switch's fix refuses something or a mod is involved |
| `CardScreen.Menu.cs` | On a host that cannot move the pointer, the menu acts as if the pointer were on the chosen line | host only |
| `OriginalSaves.cs`, `SaveFolder.cs`, `DarklandsGame.cs` (mods) | Companion files for modded saves; card numbers above the original's are never written | part of `content.mods` |

The rules presets: Faithful (every flag off: the oracles and the headless host), Original (the data-safety and visual fixes:
the player's reference, `OriginalBehaviour.DataSafe`), Fixed (Original and every gameplay fix) and Restored (Fixed and the
restoration items: the `restoration` rows, not offered to players yet). The flags are one table keyed by the `id`s above (`OriginalBehaviour.IsOn`). The player chooses
the preset with the settings key `rules.preset` ([../SETTINGS.md](../SETTINGS.md)); a saved game keeps the rules it was
started with.
