# Release-candidate play-through checklist

A person plays the C# build and the original (DOSBox, the same `DARKLAND` folder, same CONFIG.DRK) side by side and
ticks each item. Load the same saved game in both whenever an item says so; the saves are shared both ways.

How to read an item:

* **Do** what to play. **Compare** what must look and behave the same.
* **Proven by**: the automated session or fight that already agrees with the original (names under
  `tools/oracle/lockstep-sessions/`, `FIGHTS` in `tools/oracle/battle_oracle.py`, the card oracle in
  [faithfulness/CARDS.md](faithfulness/CARDS.md)). Those compare the game state, the random numbers and the saved files, not
  every pixel or sound. `gen-*` means a family of generated sessions. **UNPROVEN** = no automated session covers it: look
  hardest here.
* Sessions and fights compare the rules; your eyes are for pictures, colours, sound, timing and feel.

## Expected differences (do not report these)

Seen as expected, not as a failure. The full lists are [faithfulness/DIVERGENCES.md](faithfulness/DIVERGENCES.md) (350
entries, mostly invisible: undefined memory, host timing) and [faithfulness/SUMMARY.md](faithfulness/SUMMARY.md) (open items).

* The eight data-safety fixes (on by default, C# only): the inn cache file rewrite and its limit (the innkeeper may say
  "The innkeeper has no room for more." with 63 stores or 128 entries), the save writer keeping empty party slots, a leaving
  member's record, the Dissolve Lock / Dissolve key using the right Eater-Water, and no ghost party member after a won argument at the
  Devil's Bridge, and a refused load of a saved game cut short ("This saved game is damaged and cannot be loaded."). The original may
  garble, crash or play on from garbage there.
* Window, scale, fullscreen, pointer and key timing are the host's. Animation speed and exact frame phase can differ.
* The battle clouds (potion gas): the depth order and clipping now follow the original (D-09fbc1 is closed); only the colour
  cycling phase and the puffs' random jitter frame by frame can differ, and a paused battle may show one more jitter frame
  than the original (open lead, an UNCERTAIN row).
* The battle pointer, the party panel in battle, the mine battle's post and the loot picture's "-" were fixed in batch 27;
  report any of them that still differs.
* Shift hints on two cards whose hint handlers leave the member unset (the village graveyard, the heir lines after the dragon): the
  original draws a row background over the card from what the mouse handler left on the stack; the restoration shows "Success is
  assured." for the party (D-39eb3b, D-8ef30d). A chance hint can differ when a timer tick lands just before it (D-252ba1).
* The city gate at night: four colours one palette step apart; the night gate fade length is fixed (D-dff56b).
* The prison cell card (D-99119d) shows its text and waits; the original flashes it. Returning to the last battlefield
  uses the same place or builds a new one (D-24e8b0).
* The load screen lists saves in name order (DOS: directory order); the pointer stays visible on an empty list.
* The party's random seed at character creation comes from the clock in both; do not compare dice there.
* Sound is compared as the requests to the sound driver; how a request sounds can differ. Event music heard in DOSBox may not be recorded.
* Alt-Q reading the key at a different moment (host timing).
* F11 (developer console), F12 (screenshot) and Ctrl-F12 (problem report) exist only in the C# build; the original ignores them.
* Quirks and registered original bugs are reproduced on purpose, e.g. horses do not shorten travel.

## How to report a difference

Make the report with the program ([REPORTING.md](REPORTING.md): `report` in the console, Ctrl-F12, or the launcher's link): it
packs the logs, the settings, the saved game used (also as it was when loaded), `CACHE.TMP` and `CHARACTR.TMP` and the last
frame, and opens the issue form (choose "Difference from the original game", fill in the checklist item). One report per
difference. Give: the item number; build (release, commit or file date) and system; the save file(s) used,
copied from the `SAVES` folder (`DKSAVEn.SAV`, `.BSV`, `CACHE.TMP`, `CHARACTR.TMP`, and `.MODS.JSON` if modded); the
exact keys and clicks from that save; a screenshot of each side at the same moment (F12 in the C# build, DOSBox's capture
key in the original); what is different and which side shows what. If you cannot repeat it, say how it started. Label it
"sound", "timing", "picture" or "rules" if you can. Do not edit the save after the difference appears.

## A. Start and install

* [ ] **A1 First run, Windows.** Do: unpack the release in an empty folder with no settings file, start `DarklandsRP`.
  Compare: the launcher comes up, finds the game (GOG, Steam or a typed folder), Play starts the game and writes
  `darklands.settings.json`. Proven by: UNPROVEN.
* [ ] **A2 First run, Linux and Steam Deck.** Do: untar the Linux build, start it (desktop mode; once as a non-Steam game).
  Compare: SDL2 found, the game folder found in Steam/Heroic/Wine, keyboard and mouse work, music plays. Proven by:
  UNPROVEN (tried once on a desktop Linux; not yet on a Steam Deck).
* [ ] **A3 Launcher rows.** Do: window size, fullscreen, sound card, Roland ROM folder with the sound test, Shift at start,
  the SoundFont row, `--setup`, `--no-launcher`. Compare: each choice is kept in the settings file and takes effect at the next start. Proven by: `LauncherTests`, `LauncherPackTests`, `SettingsTests` (each choice is kept and read back); how it looks: UNPROVEN.
* [ ] **A4 Start screen and opening.** Do: let the intro run once, then skip it with a key. Compare: pictures, music, the
  three start buttons (new, quickstart, load). Proven by: `scr1-load-1` (start screen to load screen), `pressburg-map`.
* [ ] **A5 Saves folder.** Do: install under a read-only folder. Compare: the start-up line names the fallback
  `%APPDATA%\DarklandsRestoration\SAVES` and saving works. Proven by: `SaveLocationTests` (an unwritable folder falls back to the user folder); the start-up line itself: UNPROVEN.
* [ ] **A6 Another game program.** Do: point the launcher at a German or an older `DARKLAND.EXE`. Compare: the launcher
  stays open and says why the game cannot start. Proven by: `ExeVersionTests`.

## B. Character creation and the party

* [ ] **B1 Create a character.** Do: new game; try several origins and professions, change attributes and skills with keys
  and with the mouse, accept. Compare: figures, points, portrait, texts. Proven by: `chargen-created`, `scr1-create-1`,
  `scr1-create-2`, `scr1-create-3`, `gen-create*`, `gen-rcreate*`.
* [ ] **B2 Make the party and begin.** Do: four characters, names, begin the adventure. Compare: start city, money, items,
  first card. Proven by: `chargen-party`, `scr1-party-1` to `scr1-party-4`, `gen-party*` (the seed differs, see above).
* [ ] **B3 Inn roster.** Do: hire, retire, create a character in the inn, move members, portrait colours, by key and by
  mouse. Compare: the list, wrap-around, the party size limit. Proven by: `roster`, `roster4`, `roster-*`, `gen-roster*`,
  `discordq-retire`.
* [ ] **B4 Quickstart party.** Do: start with the quickstart button. Compare: same party, money and city. Proven by:
  `RcPlayTests` (Q on the start screen gives the Quickstart city, members and items); against the original: UNPROVEN (the sessions load a save).

## C. Cities and services (visit each in a few cities, with a rich and a poor party)

* [ ] **C1 Gate, streets, main street.** Do: arrive; enter by day and at night; walk the street menu. Compare: pictures,
  music change, the night-gate fade. Proven by: `pressburg-map`, `gate-battle`, `luebeck-potion`.
* [ ] **C2 Inn: rest, stay, residence.** Do: sleep, take up residence, rest several days, each member's work. Compare:
  hours, costs, health, texts. Proven by: `pressburg-map`, `residence`, `stay`, `gen-stay*`, `camp-inn-broke`, `camp-rough`.
* [ ] **C3 Inn cache (the innkeeper's store).** Do: put and get items, leave, come back, repeat in two cities. Compare: the
  lists and items. Expect the data-safety fix, never a loss. Proven by: `cache`, `cache-revisit`, `gen-cache*`.
* [ ] **C4 Merchant, swordsmith, armourer.** Do: buy, sell, drop, barter for the next member. Compare: prices, haggling,
  item quality. Proven by: `shop`, `smiths`, `gen-shop*`, `gen-rshop*`.
* [ ] **C5 Alchemist.** Do: have a potion brewed, then let days pass. Compare: cost, delay, the finished potion. Proven by:
  `alchemy`, `pack-potions`.
* [ ] **C6 University.** Do: search for saints and formulae, the materials seller, a teacher. Compare: fees, what is
  learned. Proven by: `university`, `university-shop`.
* [ ] **C7 Healer, physician.** Do: heal a wounded member. Compare: price, result. Proven by: `healer`.
* [ ] **C8 Church.** Do: a donation (also by night), the altar boy, a Mass, praying to a saint. Compare: texts, saint
  points. Proven by: `churches`, `saints`, `scr1-pray-1` to `scr1-pray-4`.
* [ ] **C9 Banks: Fugger, Medici, Hanse hall.** Do: deposit, withdraw, letters of credit, loans. Compare: sums, hours.
  Proven by: `banks`.
* [ ] **C10 Town hall and fortress.** Do: ask for an audience, the clerk, the Rathaus in Vienna; a fortress or castle visit.
  Compare: texts and results. Proven by: `wien-rathaus`, `notices`; the fortress: UNPROVEN.
* [ ] **C11 Pawnbroker, stablemaster, foreign trader.** Do: sell, buy, barter. Compare: prices. Proven by: `pawnshop`,
  `stables`, `foreigntrader`, `gen-pawn*`, `gen-stable*`.
* [ ] **C12 Wharves and boat.** Do: the toll, the boat to Olmuetz, the gate on the other side. Compare: cost, time.
  Proven by: `wharves`.
* [ ] **C13 Notices and guild halls.** Do: read the town square's notices, visit each guild and hall. Compare: texts and
  options. Proven by: `notices`, the card oracle; the guilds' menus played in a session: UNPROVEN.
* [ ] **C14 Slum and rumours.** Do: camp in the slum, hear rumours, meet brawls. Compare: texts, losses. Proven by:
  `camp-rough`, the card oracle.
* [ ] **C15 Every city once.** Do: enter every city's gate and street. Compare: pictures and menu lines. Proven by: the card
  oracle (all 365 cards: texts and options); as played sessions only `pressburg-map`, `tyrnau`, `wharves`, `wien-rathaus`.

## D. Travel and encounters

* [ ] **D1 The map.** Do: leave a city, walk with mouse and keys, enter a place from above and from below. Compare:
  terrain, time, positions. Proven by: `pressburg-map`, `tyrnau`, `kbd-map-enter`, `kbd-map-wait`, `map-enter-above`,
  `map-enter-below`.
* [ ] **D2 Camp in the wilds.** Do: camp, work choices, brewing, a risky camp, three days. Compare: hours, food, events.
  Proven by: `wildcamp`, `camp-wild-brew`, `camp-wild-risk`, `camp-mouse`, `gen-wild*`.
* [ ] **D3 Road encounters.** Do: walk until three different encounters come (an animal pack, bandits, a stranger). Compare:
  the card text, options, outcome. Proven by: `res5-WolfPackOnTheTrail`, `res5-WildBoarsCharge`, `res5-footpads`, the card
  oracle; which encounter the map picks over a long walk: UNPROVEN.
* [ ] **D4 City events.** Do: wait in streets for night events (market at night, a mob, the city guard). Compare: texts,
  results. Proven by: `res5-MarketAtNight`, `res5-rebelmob`, `res5-ChasedByTheCityGuard`.
* [ ] **D5 Dungeon and mine.** Do: a mine and a cave: doors, single file, moving as a group, orders. Compare: doors,
  messages, fights. Proven by: `dungeon-mine-doors`, `dungeon-mine-doormsg`, `dungeon-mine-group`, `dungeon-mine-orders`, `savemid-mine`.
* [ ] **D6 Date, weather, seasons.** Do: spend many weeks, watch the calendar and the light. Compare: dates, day and night.
  Proven by: `RcPlayTests` long generated play (the date stays valid); weeks against the original and the light: UNPROVEN (sessions run a few days).

## E. Cards and events

* [ ] **E1 Option menus.** Do: in menus use the mouse, Up/Down, Enter, digits, Esc. Compare: highlight, fade, result.
  Proven by: `scr1-card-1`, `scr1-card-2`, the card oracle (the digits are a C# convenience, not in the original).
* [ ] **E2 Quests and letters.** Do: take a quest, carry it out or fail it. Compare: texts, rewards, reputation. Proven by:
  the card oracle (start states with quests); a whole quest played through: UNPROVEN.
* [ ] **E3 Items and saints on cards.** Do: use an item or a saint on a card line. Compare: the box, the result. Proven by:
  the card oracle, `saints`.
* [ ] **E4 Death of the party, heir, game over.** Do: let the last member die; the heir card; the end films. Compare:
  texts and pictures. Proven by: the card oracle (heir menu), `gen-bdead*`; the final films: UNPROVEN.
* [ ] **E5 Events that change the party.** Do: a member joins, leaves, becomes Away. Compare: the roster. Proven by:
  `res5-footpads` (an Away member), the card oracle.

## F. Battles

* [ ] **F1 Melee.** Do: fight the gate guards and a street fight; select, walk, attack orders, berserk, defend, pause.
  Compare: hits, damage, order of events, the log. Proven by: `gate-battle`, `orders-select`, `orders-walk`,
  `orders-attacks`, `orders-attack-*`, fights `gate`, `street`, `wild`, `mine`, `castle`, `city*`, `wild*`, `mine*`,
  `castle*`, `fort5e`, `tombs5f`, `cave60`.
* [ ] **F2 Missiles.** Do: shoot and throw; face enemy archers. Compare: aim, hits. Proven by: `orders-missile`,
  `orders-prague-missile`, fights with missile users (`street`, `wild40`, `castle7c`).
* [ ] **F3 Potions.** Do: drink and throw potions. Compare: effect and the cloud picture. Proven by:
  `luebeck-potion`, `orders-potion`, `orders-potion2`, `orders-potion3`, `gen-pots99*`, `orders-cloud` (a thrown potion lands in a cloud); the cloud picture: the vis10 shots D-09fbc1
  (ordered and paused shots: 0 differing pixels; a later paused shot differs, see above).
* [ ] **F4 Spells and saints in battle.** Do: cast a formula and call a saint; fight casters (witches, alchemists, friars,
  demons). Compare: points used, effect, range. Proven by: fights `f2*` (casters), `saints`; spell rounds played in a
  session: UNPROVEN.
* [ ] **F5 Retreat.** Do: run away, the party leaves the field. Compare: who escapes, the card that follows. Proven by:
  `gen-brun*`.
* [ ] **F6 Loot.** Do: take, share, leave, by key and by mouse. Compare: the lists. Proven by: `loot-keys`, `loot-mouse`,
  `loot-esc`.
* [ ] **F7 Doors, traps, locks.** Do: Open Chest, Disarm Trap, Dissolve Lock (the D key). Compare: messages, items used up.
  Proven by: `dungeon-mine-doors`, `dungeon-mine-doormsg`.
* [ ] **F8 Defeat.** Do: lose a fight with everyone down. Compare: who dies, the next card. Proven by: `res5-footpads`,
  `gen-bdead*`.
* [ ] **F9 Special foes.** Do: the dragon, a demon lord, a high witch, bears. Compare: behaviours. Proven by: fights
  `cave60`, `f2demon61`, `f2hwitch41`, `wild30`.
* [ ] **F10 Battle pointer, scrolling, sheets in battle.** Do: scroll with the pointer at an edge; F1..F5 in battle; the
  menu bar. Compare: scroll, cursor shapes. Proven by: `battle-cursor-1` to `battle-cursor-4`, `battle-edge-1` to
  `battle-edge-7`, `kbd-battle-cursor`, `orders-sheet`, `orders-sheetclick`, `gen-bkeys*`, `gen-bedge*`.
* [ ] **F11 Battlefields.** Do: reach a city, wilderness, mine, castle and tomb fight. Compare: the map drawn, the start
  positions. Proven by: the fights above (the generators agree).

## G. Saving and loading

* [ ] **G1 Save in a city and on the map.** Do: Alt-S with a new name, then overwrite; reload with Alt-L. Compare: the
  state and the screen. Proven by: `save-load`, `mapsave`.
* [ ] **G2 Save in a battle and in a mine.** Do: Alt-S in a fight, load it in the same build and in the original. Compare:
  positions, timers, the battle goes on. Proven by: `orders-save`, `b3-save-fight`, `b3-save-fight2`, `b3-save-fight3`, `savemid-mine`.
* [ ] **G3 Load an original save.** Do: copy saves made in the original into the C# `SAVES` and load them (`DKSAVEn.SAV`,
  and a battle `.BSV`). Compare: everything. Proven by: `scr1-load-1` to `scr1-load-3`, `sheet-dksave0` and every
  session that starts from a `DKSAVE` file; saves from the community archive: UNPROVEN.
* [ ] **G4 Load a C# save in the original.** Do: save in the C#, load in DOSBox. Compare: it loads and the game is the same.
  Proven by: the sessions compare the saved files with the original's writer.
* [ ] **G5 Load screen.** Do: delete a save; cancel with Esc and with Alt-Q (answer no); open an empty list. Compare: the
  buttons and the list (order differs, see above). Proven by: `scr1-load-1` to `scr1-load-3`.
* [ ] **G6 A damaged or missing save.** Do: load a truncated `.SAV` and a missing battle file. Compare: the message.
  Proven by: `RcPlayTests` (a save without its battle file loads and the game goes on; a missing file is named). A `.SAV` cut inside its header, records or location table is refused with "This saved game is damaged and cannot be loaded." (a data-safety fix; the original ends in DOS with a file error or plays on from garbage, with no message of its own to compare; tests `RcPlayTests`, `DamagedSaveTests`); so is a saved game whose `.BSV` is there but empty, short or corrupt (the original ends in DOS with "EOF-..." or "Corrupt file-...", also outside a battle); a cut only in its cache, or a missing `.BSV`, loads as before.
* [ ] **G7 Many saves.** Do: save in four different places in a row and reload each. Compare: names, place. Proven by:
  `save-load`, `mapsave` (one at a time); the series: UNPROVEN.

## H. Hot keys and menus

* [ ] **H1 Menu bar.** Do: right button, F10, arrows, Enter, Esc through every menu. Compare: every entry and its state.
  Proven by: `menubar`, `menubar2`, `menubar-kbright`, `gen-mbar*`, `gen-rmbar*`.
* [ ] **H2 Alt-Q quit.** Do: Alt-Q, answer no and yes (also on the load screen). Compare: the question, the exit. Proven by:
  `gen-mquit*` (the timing differs, see above).
* [ ] **H3 Alt-O marching order.** Do: reorder with keys and mouse. Compare: the order on the map and in battle. Proven by:
  `b2-march-keys`, `b2-march-mouse`.
* [ ] **H4 Alt-P pause.** Do: pause on the map and in a battle, resume. Compare: nothing moves, the clock stands. Proven by:
  `c3-space-pause`.
* [ ] **H5 Alt-D, Alt-F, Alt-C, Alt-M.** Do: each once (difficulty, sound effects, Alt-C, music). Compare: the box and the
  setting kept. Proven by: `gen-mbar*` (with `gen-mbar-altm`); the audible result: UNPROVEN.
* [ ] **H6 F6 party sheet and Ctrl-F1..F5.** Do: open F6; choose the leader with Ctrl-F1..F5 on the map and in a battle.
  Compare: the panel, who leads. Proven by: `scr1-f6-1`, `scr1-f6-2`, `kbd-map-held`, `kbd-battle-ctrl`.
* [ ] **H7 F1..F5 character sheets.** Do: open each sheet; ready weapons and armour, give and drop items, pack stacks,
  saints and formulae; close with Esc. Compare: every figure and box. Proven by: `sheet`, `sheet-dksave0`, `inventory`,
  `pack-stacks`, `orders-sheet`, `gen-sheet*`.
* [ ] **H8 Held keys.** Do: hold keys on the map and in a battle. Compare: repeat rate and moves. Proven by: `kbd-map-held`,
  `kbd-battle-held`, `gen-bheld*`.
* [ ] **H9 Developer console and screenshot (C# only).** Do: F11 then `help`; F12. Compare: no game key is lost (the
  original has neither). Proven by: `GameConsoleTests` (the console); that no game key is lost: UNPROVEN, not compared.

## I. Sound and music

* [ ] **I1 Music by place.** Do: visit the districts, the inn, the outskirts, the main street (fade-out), load a game.
  Compare: which piece plays and when. Proven by: UNPROVEN as heard (the calls to the drivers are recorded in sessions).
* [ ] **I2 Sound cards.** Do: repeat with `config` (MT-32 with ROMs), Sound Blaster Pro 2, AdLib, none. Compare: the same
  piece, not stuck, no silence. Proven by: UNPROVEN.
* [ ] **I3 Battle effects.** Do: hits, deaths, spells, the end of a fight. Compare: effects match events. Proven by:
  UNPROVEN as heard (order inside a tick may differ).
* [ ] **I4 Music and effects switches.** Do: Alt-M and Alt-F during play. Compare: stop and start. Proven by: UNPROVEN.
* [ ] **I5 Speech and samples.** Do: the cards with a sample, on Sound Blaster. Compare: heard in both. Proven by: UNPROVEN.

## J. Packs ([PACKS.md](PACKS.md))

* [ ] **J1 No pack.** Do: nothing set. Compare: identical to the original in pictures and sound. Proven by: all sessions.
* [ ] **J2 Picture pack.** Do: set a pack with a few PNGs, start. Compare: the replaced pictures show only where the
  original shows them, text and menus stay on top, fades follow the palette; the game plays the same. Proven by: `HdPackTests`, `HdRendererTests`
  (headless shots); the look: UNPROVEN.
* [ ] **J3 Sound pack.** Do: use `packs/audio-pack-example`. Compare: tones replace the pieces; Alt-P pauses them; a battle
  ends them; timing does not change. Proven by: `PackAudioTests` (the game's calls reach the drivers unchanged; pause and mute), `LauncherPackTests` (the example pack reads clean); how it sounds: UNPROVEN.
* [ ] **J4 Broken pack.** Do: a missing folder, a bad PNG, an unknown name. Compare: a console line and the game's own
  picture or sound. Proven by: `PackAudioTests`, `HdPackTests`, `LauncherPackTests` (the problem is reported and the rest still loads).
* [ ] **J5 Launcher rows.** Do: choose a pack with the arrows, by browsing, by dropping its folder. Compare: the row shows
  the name, marked (!) when unreadable. Proven by: `LauncherPackTests`.
* [ ] **J6 MIDI music and music per card.** Do: a pack with `.mid` pieces, once with a SoundFont and once without; a pack
  or mod that gives a card its own music. Compare: the MIDI pieces play through the SoundFont (without one, the game's own
  music); a card's own music starts with the card and stops at a battle; the ten cards listed in PACKS.md that silence the
  music stay silent. Proven by: `MidiPackTests`, `CardMusicTests`.

## K. Mods ([MODS.md](MODS.md))

* [ ] **K1 No mod.** Do: play with no `--mod`. Compare: the 366 original cards, saves with no `.MODS.JSON`. Proven by: all sessions.
* [ ] **K2 The example mod.** Do: `--mod wayside-shrine`, reach the added card by day in a city. Compare: text, choices,
  effects; the console lists what it adds. Proven by: `ModCardTests` and `RcPlayTests` (the shrine card runs in the game loop; the console lists what the mod adds).
* [ ] **K3 Modded save.** Do: save on the mod's card with Alt-S. Compare: `DKSAVE<n>.SAV` and `DKSAVE<n>.MODS.JSON` appear;
  the original loads the `.SAV` and goes on from the card the save names. Proven by: `ModCardTests`, `RcPlayTests` (Alt-S on the mod's card writes both files; the `.SAV` names only game cards).
* [ ] **K4 Load with and without the mod.** Do: load the modded save with the mod (it goes on at the mod's card), then
  without (a message names the missing mod; the game goes on). Compare: no crash. Proven by: `ModCardTests`, `RcPlayTests` (with the mod the card goes on; without it the console names the mod).
* [ ] **K5 No modded card into an original save.** Do: load an original save (no companion file) with the mod on, play past
  the mod's card, save, load. Compare: the saved `.SAV` never holds a card number above 365 and the original loads it.
  Proven by: `ModCardTests`, `RcPlayTests` (an original save played with the mod, saved several times: no card above 365).
* [ ] **K6 Deleted or replaced save.** Do: delete a modded save in the load screen; replace its `.SAV` by another. Compare:
  the companion file is removed or ignored. Proven by: `ModCardTests`.
* [ ] **K7 A refused mod card.** Do: a mod card with a game's number and no `overrides`. Compare: a console line with the
  reason, the card not used. Proven by: `ModCardTests` (the reason is a console line; the mod is not played).

## L. Options ([SETTINGS.md](SETTINGS.md))

* [ ] **L1 Original look.** Do: default `cleanDrawing` false. Compare: screens drawn as the original, oddities included.
  Proven by: the sessions compare state, not pictures; pictures: UNPROVEN apart from the visual passes' shots.
* [ ] **L2 CleanDrawing.** Do: `--clean-drawing`. Compare: the cleaner drawing, and the game plays the same (a save made
  with either setting loads in both). Proven by: `RcPlayTests` (the same keys give the same game state and files with it off and on; a save of either loads in both); the drawing: UNPROVEN.
* [ ] **L3 Scale, resolution, fullscreen.** Do: scale 1 to 4, a custom size, fullscreen and back. Compare: aspect,
  sharpness, pointer position over the picture. Proven by: `MouseMappingTests` (every window position gives the game pixel shown there), `DisplayLayoutTests`; sharpness and fullscreen: UNPROVEN.
* [ ] **L4 Settings file.** Do: edit a key, put in a wrong value, add a command-line option. Compare: the console names the
  bad key; the command line wins. Proven by: `SettingsTests` (a bad key is named and ignored; the command line wins).
* [ ] **L5 Faithful and data-safe behaviour.** Do: a long inn session with many stores; a leaving member rejoins. Compare:
  nothing is lost in the C#; the original may lose it. Proven by: unit tests; the faithful mode by `cache`, `cache-revisit`.

## M. Long play

* [ ] **M1 One whole evening.** Do: play two hours in each build from the same save, with fights and trade. Compare:
  money, time, health at the end; any crash. Proven by: `RcPlayTests` (about 40,000 frames of generated play, three seeds: no crash, invariants hold, the end saves and reloads equal); two hours against the original: UNPROVEN (sessions are minutes long).
* [ ] **M2 Final stretch.** Do: reach a late-game scene (the Wild Hunt, an end film). Compare: texts; saves still load.
  Proven by: UNPROVEN.
