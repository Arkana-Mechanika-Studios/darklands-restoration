# Battles

How the battle system is put together, how the game opens a battle, and what is not done yet.
The trace of each operation to the original is in `docs/source-map/map.csv` (see
`TRACEABILITY.md`); the oracle corpora that check it are under `tests/Darklands.Oracle.Tests/Corpus`.

## Layers

| Layer | Where | What |
|---|---|---|
| Rules | `Darklands.Core/Battles` | combatants (`Combatant`, the 0x80-byte record plus the battle's parallel tables), attack and damage (`Attacks`, R014 18BF), the end of a battle (`BattleEnd`), enemy kinds and groups (`BattlefieldForces`, R004) |
| Battlefields | `Darklands.Core/Battles/Generation` | `BattlefieldBuilder` (battlefield_build, R004) and every generator: castles and keeps (R005..R007), mines (R009), cities (R010), wilderness (R008), Templar fortress and tombs (R011), dragon cave (R012), Baphomet's citadel (R013), empty (R008 2D62), map turning (R004 1091) |
| Tactics | `Darklands.Core/Battles/Tactics` | `TacticalBattle` (combat_main and its children: the loop, the action state machine, engagement, AI, enemy turn, missiles, placement, the player's orders), `BattleMap` (collisions, line of sight), `PathFinder` |
| Files | `Darklands.Content/Formats/Battles`, `Formats/Exe` | IMAPS.CAT levels, BC tile sets, IMC animations, BATTLEGR/COMMONSP images, HITSEQ, ENEMYPAL, DARKLAND.ENM, the EXE's combat tables |
| Screen | `Darklands.Presentation/Battles`, `BattleScreens.cs` | `BattleScreen` (real-time loop, isometric view, sprites, cursors, panel, keys and mouse), `BattlefieldView`, `AnimationLibrary`, `BattleAssets` |

The rules never look at the screen: `TacticalBattle` talks to the screen through
`ITacticalView` (sounds, messages, redraws, the loot screen) and to the animation files through
`IAnimationLibrary`.

## Time

`Battle.Clock` is the original's `combat_clock`: 60 ticks a second, stopped while the game is
paused (order mode). `TacticalBattle.Iterate()` is one pass of combat_main's loop; a combat tick
(`Tick()`) runs every 8 clock ticks (7.5 a second). `BattleScreen` advances the clock from the
host's `IClock` and calls `Iterate()` once per frame, so the battle runs at the original speed
whatever the frame rate.

## Opening a battle from the game

`BattleScreens` (Presentation) is the battle the game opens:

```csharp
var battles = new BattleScreens(ui, () => state, loot: (items, money) => screens.Loot(items, money));
int result = battles.Battle(new BattleRequest(battlefield, seed, groups, mode, persistent));
int nextCard = battles.LastResultCard;   // battle_args: the level's card for that result
```

`result` is battle_result: 0 won, 2 fled (8 "left before the fight" is reported as 2), 3 lost,
4 surrendered, 5 all down, 6 fell into a pit, 7 timed exit.

The two ways the original starts a battle map onto `BattleRequest` like this (DarklandsGame is
not changed here; this is the wiring it needs):

* **A card script's `run_battle(side, seed, t1, q1, n1, t2, q2, n2, t3, q3, n3)`** — already
  `CardContext.RunBattle`, which picks a random side for −1, computes the seed with
  `PartyRules.BattleSeed` (party seed added, the original's double addition for −2) and calls the
  `CardContext.Battle` hook. Set the hook to

  ```csharp
  context.Battle = (side, seed, p) => battles.Battle(new BattleRequest(side, seed,
      [(p[0], p[1], p[2]), (p[3], p[4], p[5]), (p[6], p[7], p[8])]));
  ```

  (`side` is the battlefield type; `battle_run` is called with mode 0; RunBattle then applies
  `PartyRules.AfterBattle` and the time passage.)
* **The main loop's battle cards** — the card leaves `battle_mode` (DS:A766), `battle_enemy`
  (DS:A768, the battlefield type, or the level for mode 3), `battle_seed` (DS:A76A) and the three
  enemy types DS:A76C, DS:A76E, DS:A770 (quality −1, count 0 in the original call), and the main loop
  runs `battle_run(mode, enemy, seed, (−1, DS:A76C, 0), (−1, DS:A76E, 0), (−1, DS:A770, 0))` with
  combat_flags bit 0 set. The equivalent is
  `battles.Battle(new BattleRequest(enemy, seed, [(-1, a76c, 0), (-1, a76e, 0), (-1, a770, 0)], mode, Persistent: true))`
  and the next card is `battles.LastResultCard`.

`BattleScreens.WorldTerrain` can supply the terrain under the party (DS:D843) when the overland
map knows it better than `GameState.PartyTerrain`. `BattleScreens.Memory` keeps the
cleared-level memory of a place between battles (DS:DA34, DS:DB27).

The headless host runs a battle directly for tests and screenshots:
`Darklands.Host.Headless <game> <out> battle:<battlefield>,<seed>[,<type>,<quality>,<count>...] wait:60 shot:name`.

## Effects, the dungeon and the menus

* **Effects (R019)** — `EffectField` (`TacticalBattle.Effects.cs`, `Combatant.Effects.cs`): a spell
  or thrown potion that lands (`Lands`, effect_spell) lays out a cloud (fire wall, poison, smoke …),
  a slowing area, or a flash/blast marker; each combat tick the clouds and markers act on who
  stands in them (`CloudActs`, `MarkerActs`) with the pending-damage caps and byte wraps of the
  original, the worn potions and modifiers (DS:A532) wear off, and clouds block lines of fire with
  the original's off-by-one slot and stack residue (bug 0029; `OriginalBehaviour.FixCloudLineOneTooMany`
  fixes it). The renderer's random draws (puff jitter, flashes) are taken by the rules in the
  original order through `IBattleViewport`, so the game's random sequence stays the original's.
* **Doors, locks, traps, chests, stairs (R023, R014 19CD, R024)** — `TacticalBattle.Dungeon.cs`
  and `TacticalBattle.Doors.cs` work on the level image as the original lays it out (doors
  0x1F52, triggers 0x2184, wall lists 0x2488, stairs 0x2FB6, chest regions 0x300C, zones 0x3326,
  removal lists 0x3350/0x3382, door flags 0x33D3): Use Door, Pick Lock, Dissolve Lock (Eater-Water),
  Open Chest, Disarm Trap, secret doors found by perception, hidden/spell/missile traps, pits,
  alarms, walls that open, rooms behind shut doors hidden until opened, and the three-area model
  of stairs (the party split over two levels, U takes the stairs back, the level shown follows).
* **Menus and order mode** — `TacticalBattle.OrderMode.cs` (R014 19CD: order mode, the cursor,
  orders, place actions, cancel, pause), `TacticalBattle.Menus.cs`/`BattleMenus.cs` (the enable
  bytes of every Orders and Attack entry, DS:9607 and DS:97DB), `BattleMenuBar` (the menu bar of
  the right button; T opens the Throw list), `TacticalBattle.Party.cs` (surrender R022 03E4, the
  enemy information kinds R021 02CE, the marching order, the character sheet's return). The screen
  shows the Enemy Info panel (EINFO.CAT portraits, armour and weapon words), asks before
  surrendering, opens the character sheet with F1..F5 or a portrait click, and shows the
  "BattleResults - …" box after the battle.

* **The mouse and the keys** — clicks are the mouse driver's releases (INT 33h function 6), taken at the
  place of the release: press on a member, drag, let go elsewhere; while the left button is held the
  member under the pointer is selected (outside order mode) or the order cursor follows it. A release over
  the party panel opens the sheet or says "You can't view …" (a box the battle waits out). Ctrl-click
  activates a member; Shift with the pointer at an edge scrolls. F10 drives the menu bar from the keyboard
  (the game's bar over the battle's menus, the battle standing still while it is up, as while the right
  button holds it); T unfolds the Attack menu to the Throw list. In order mode (and travelling as a group)
  a held direction key moves the pointer once a combat tick, faster the longer it is held, and letting it
  go is a click at the pointer (Enter clicks when travelling as a group); hosts report the keys' make and
  break codes for this (`kdown:<hex>` / `kup:<hex>` in the headless host).
* **The pointer** — the ground point under the pointer is the exact inverse of the projection
  (R014 19CD: `h = y − 0x69`, `x = h − 0x78 + (x − 60) − 16 + origin x`, 0x69 / 0x78 being slot
  75 of the slot tables), so the ghost of a move order stands on the clicked pixel. Enemies are
  hit-tested on the hot spots the renderer builds (R014 14F7:13AE: a strip at the foot point by the
  animation's size class), front-most first, and only for combatants in the view. A click on an
  enemy targets it only once an attack is chosen (A, V, B, P, M, Throw) or while the member already
  fights or shoots: a member idle or walking with no order chosen takes the click as a spot on the
  floor, as the original does. The scroll arrows are drawn by their own hot spots.
* **The end of a battle** — the loot screen of a victory saves and restores the battle palette and
  the battle is redrawn after it; the "BattleResults - …" box comes up over the battle view (pointer
  hidden) and nothing puts back the screen of before the battle: the caller draws its next screen.

The headless host has steps for them: `rdown:<x>,<y>` / `move` / `rup` (the menu bar),
`ldown`/`lup`, `give:<code>,<quality>,<count>` (potions to throw), and the keys. For example
`battle:40,1234,34,4,3 give:95,30,3 wait:3 key:space wait:5 key:1 click:234,180 key:space wait:150 key:1 key:t wait:5 key:enter ldown:200,150 lup key:space wait:60 shot:boom key:s key:y wait:400`
walks into the enemies, throws a potion and surrenders. `click:cbt:<i>` (also `move`, `ldown`,
`rdown`) points at the centre of combatant i's hot spot, `dump` prints the combatants (position,
action, target, hot spot) and the order cursor, `picture:<name>` shows a picture in its colours
before the battle (as a card's screen would be), `tail:<n>` saves the last n frames of the battle
(`tail00001.png` …) and `rec:<n>` the next n frames; a won battle opens the game's loot screen (Esc
leaves it). For example `battle:40,1234,34,4,3 wait:3 key:space wait:5 key:1 click:234,180 key:space wait:150 key:1 key:a click:cbt:5 dump`
targets the first enemy, and
`picture:APROCH1 battle:17,60256,18,0,1 tail:1500 wait:5 key:space` followed by `wait:600 key:esc`
repeated shows the end of a won fight. `DUNGEON_SHOTS=<folder>` makes
`DungeonTests` save the door, secret door, chest and stairs screens.

* **Loot** — `LootStore` is the battle's loot file: each pile and chest keeps a block of item
  records; a body falling next to a pile merges into a new block both piles share (equal items add
  their byte counts); after a fight the recovered missiles and the new piles' blocks go to the
  leader's pile; what the party leaves on the loot screen goes back into the file.
* **Program-wide state** — `GenerationProcessMemory` (kept by `BattleScreens`) holds the
  original's globals that outlive a battle: the Templar fortress's smaller-groups byte the tombs
  read, the last floor image the empty battlefield starts from, the alarm trigger the next fight
  applies (combat flag 4). Combat flags 8 and 0x80 from the card re-enable an activation.

## What the oracles check

Each corpus runs the original code from DARKLAND.EXE in an emulator (tools/oracle) on generated
inputs and stores only inputs and results:

| Corpus | What | Cases |
|---|---|---|
| battle.json | attack rate, to-hit, damage, damage queue/apply, weapon readying, skill gains (R014 18BF, R021) | 860 |
| enemies.json | enemy kinds and groups (R004) | 120 |
| city.json, wild.json, castle.json, mine.json, special.json | the battlefield generators, byte for byte | 40, 96, 100, 400, 145 |
| tactics.json | cbt_collisions, line_blocked, distance3 | 1200 |
| path.json | path_find (trackers, pruning, shortcuts; path_trackers alone with `--trackers`) | 60 |
| effects.json | effect clouds, slowing areas, markers, modifiers, to-hit through clouds (R019) | 1020 |
| implode.json | the PKWARE DCL 1.02 implode and explode of record 3 (frame 1016) on generated data, byte for byte with return codes | 98 |

The tactical loop itself (combat_main, the action state machine, AI, enemy turn, placement) is
written from the asm-checked spec and exercised end to end by `Darklands.Battles.Tests` and the
headless host, not by an oracle.

## Not done yet

* The keyboard menu mode (F10) beyond the Throw list; the joystick; Ctrl-click (Ctrl-F1..F5 do it).
* The battle has no joystick (the original reads one only on the map).
* The renderer's draw list (157 entries) is counted without clipping each entry's box to the view
  (an entry the view clips to nothing gives its place back in the original); the animated
  decorations of the view (the tile set's animation limits) show the frame drawn by lot each tick.
* Revisiting a battlefield keeps its levels, piles and chests in memory (no .flr/.atv files), which
  is equivalent except that the original reloads a level from its last save (changes made without
  setting the level's changed flag DS:D4E4 are lost there).
* The cloud edges are drawn over the combatants instead of depth-sorted; the schemes' palette
  cycles are not run.
* Uninitialised reads of the original are replaced by fixed values (a door record without a side
  flag: north; lock-pick quality without picks: 0; the encounter's typ[4]: −1; a ninth cloud slot:
  empty) — marked DIVERGENCE where they are.
* Message boxes use colour 0xFF, which the battle palette leaves as the previous screen set it
  (the headless host, starting straight in a battle, shows them dark).
