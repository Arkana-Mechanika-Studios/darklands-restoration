# Saved games

The game saves and loads the original's own files: `SAVES\DKSAVEn.SAV` with its battle file
`DKSAVEn.BSV`. A save of the original DOS game loads here, and a save made here loads in the
original. `Darklands.Persistence` is the only code that knows the layout:

| Type | Role |
|---|---|
| `SaveFile` | the sections of a .SAV as bytes (parse / write back byte for byte) |
| `OriginalSaves` | .SAV (+ .BSV) ↔ `GameState`; `Read`, `Write`, `Saved`, `FileNameOf` |
| `CharacterRecords`, `LocationRecords`, `EventRecords`, `CacheFile` | the records ↔ the domain |
| `BattleSaveFile`, `Implode`, `Explode` | the .BSV container and its PKWARE DCL 1.02 blocks (implode and explode ported from the library's assembly in record 3) |
| `SaveFolder` | the SAVES folder: list, summaries, next free name, save, load, delete |

The screens are `LoadGameScreen` (R037 1c7d: `LOADSCRN.PIC`, nine rows, arrows/Home/End/click,
`L` load, `D` delete, `R` and Esc back) and `SaveGameDialog` (R037 2a68/2ace: "Save Game
Comment:", up to 23 letters, "Darklands" offered, then "Saving Game" while it writes; Esc saves
nothing). The start screen's third button and Alt-L open the load screen; Alt-L first asks
"Loading is irreversable. Proceed?".

The folder is the game folder's `SAVES`, as the original uses it, so saved games are shared with the
original (`DarklandsGame.SaveDirectory`). The settings file's `savesFolder`, `DARKLANDS_SAVES` or
`--saves <folder>` put them elsewhere. When none of them is set and the game folder's `SAVES` cannot be
written (a game under Program Files, such as GOG Galaxy's `C:\Program Files (x86)\GOG Galaxy\Games\Darklands`:
Windows gives a .NET program no VirtualStore there), the SDL host uses `%APPDATA%\DarklandsRestoration\SAVES`
instead (beside the per-user settings file), creates it and says so in one line on the error output
(`Darklands.Platform.SaveLocation`: a probe file is created and deleted to test the folder). The load screen
then lists only that folder; to go on with saves of the original, copy its `DKSAVEn.SAV` and `DKSAVEn.BSV`
files there (and back, for the original to load saves made here). The headless host writes beside itself unless given `saves:<folder>`,
so scripted runs never touch the game folder.

## What is kept, and how

* Every field the model names is read into the state and written from it.
* Bytes the model does not name stay with the state and are written back as they were:
  `Character.OriginalRecord` (0x22A), `Location.OriginalRecord` (0x3A; for a new game the bytes
  of DARKLAND.LOC) and `GameState.OriginalSave` (the header, the slot images and colours, the
  cache file, the battle file).
* Values the original recomputes when it saves are written as read while what they are computed
  from is unchanged: `party_size` (from the slots), `DS:A67C` (hirelings, from the 0x43 events),
  the header's place name (from `cur_loc`), CACHE.TMP (from the item stores). DKSAVE40 (party
  size 6 for four members, year 1447) and DKSAVE0/1 (place names that are not `cur_loc`'s)
  show that the player's folder holds files the formula would not reproduce.
* Texts (names, comment, place) are kept byte for byte while they read the same; a new text is
  followed by zeros (the original leaves stack bytes after it).

Result: every save of the player's folder (19 .SAV files and SAVES\DEFAULT) is read and written
back byte for byte, and each of the 18 .BSV files with its .SAV.

## The .SAV file

| Offset | Size | Original | Model | Status |
|---|---|---|---|---|
| 0x000 | 0x15 | place name | `Locations[CurrentLocation].Name` or "Wilderness" | derived, kept as read while the place is unchanged |
| 0x015 | 0x4F | comment | `LoadedGame.Comment`; the save dialog's text | named |
| 0x064 | 2 | `party_seed` | `Party.Seed` | named |
| 0x066 | 2 | 7 | — | constant (the loader skips it) |
| 0x068 | 8 | year, month, day, hour | `Date` | named |
| 0x070 | 6 | purse fl/gr/pf | `Party.Purse` | named |
| 0x076 | 4 | zero long | — | constant |
| 0x07A | 2 | `party_fame` | `Party.Fame` | named |
| 0x07C | 2 | `cur_loc` | `CurrentLocation` | named |
| 0x07E | 4 | `party_x`, `party_y` | `MapPosition` | named |
| 0x082 | 2 | `DS:A772` (card) | `CurrentCard` | named |
| 0x084 | 2 | `DS:E898` (card text state) | — | raw |
| 0x086 | 2 | `saved_card` | `SavedCard` | named |
| 0x088 | 2 | `saved_card2` | `SavedCard2` | named |
| 0x08A | 2 | `prev_screen` | `PreviousCard` | named |
| 0x08C | 6 | `DS:9072..9076` bank notes | `Party.BankNotes` | named |
| 0x092 | 2 | `DS:9078` philosopher's stone | `Party.PhilosophersStone` | named |
| 0x094 | 2 | `DS:A67C` hirelings | count of 0x43 events | derived, kept as read while the count is unchanged |
| 0x096 | 1 | `game_difficulty` (bit 7: saved in a battle) | `Difficulty`, `ResumeBattle` | named; bit 7 set by a save in a battle, read as DS:9081 bit 0 |
| 0x097 | 2 | `DS:DB27` last battle place | `Battlefield.LastLocation` | named |
| 0x099 | 2 | `DS:DA34` cleared levels | `Battlefield.ClearedLevels` | named (the .BSV's copy wins on load, as in the original) |
| 0x09B | 6 | marching order (DS:C624) | `BattleFile` variables +0x1573 | written from the battle file's copy (one global) |
| 0x0A1 | 1 | `leader` | `Party.LeaderIndex` | named |
| 0x0A2 | 2 | `opt_music`, `opt_sound` | `LoadedGame.Music/SoundEffects` → `GameOptions` | named |
| 0x0A4 | 2 | `DS:A891` | `MapLeaveMode` | named |
| 0x0A6 | 0x49 | zero byte, 36 zero words | — | constant |
| 0x0EF | 2 | `party_size` | — | derived, kept as read while the slots are unchanged |
| 0x0F1 | 2 | character count | `Roster.Characters.Count` | named |
| 0x0F3 | 10 | party slots | `Roster.PartySlots` | named |
| 0x0FD | 20 | `member_shield` (figure image per slot) | `Character.Image` of the slot | named; empty slots raw |
| 0x111 | 0x78 | `member_palette` per slot | `Character.ImagePalette` of the slot | named; empty slots raw |
| 0x189 | n × 0x22A | the roster's records | `Roster.Characters` (party slots: the same objects as `Party.Members`) | see below |
| … | 2 + m × 0x30 | the events in slot order | `Events` (loaded into slots 0..m−1) | all 24 words named |
| … | 2 + 414 × 0x3A | the places | `Locations` | see below |
| … | rest | CACHE.TMP | `ItemStores` | named (layout kept as read while the stores are unchanged) |

Not in the file (as in the original): the random generator's state, the bankers' standings
(`DS:4BB6..4BBA`), the saintly-changes option, `city_rec` and `city_desc` (reread from
DARKLAND.CTY / DARKLAND.DSC at `cur_loc`). A load from inside a game carries them over from the
running game, as the original keeps its memory.

### Character record (0x22A)

Named: roster location +0x00 (the place the character was left), age +0x12, status +0x14, shield
letter +0x15, sex +0x17, missile type +0x22, Firewall strength (`CharmStrength`) +0x24, name
+0x25 (25), nickname +0x3E (to the weapon-potion byte), weapon potions +0x48, encumbrance +0x49,
armour types/qualities +0x4B/+0x4C/+0x4F/+0x50, weapon +0x51/+0x58, armour potions
(`BattleCharms`) +0x55, missile quality +0x5A, shield +0x5B/+0x5C, attributes +0x5D, maxima
+0x64, skills +0x6B, pack count +0x7E, saints +0x80 (bit `0x80 >> (s & 7)`), formulas +0x94
(bits 0..2 of the potion's byte), pack +0xAA (64 × {code, type, quality, count, weight}).

Raw (kept as read; zeros for characters made here): +0x02..+0x11 (battle position, destination,
range, orders), +0x16, +0x18..+0x21, +0x23, +0x4A, +0x4D/+0x4E, +0x52..+0x54, +0x56/+0x57,
+0x59, the top five bits of each formula byte, the pack entries past the count.

### Place record (0x3A)

Named: icon +0x00, x/y +0x04/+0x06, notice range +0x08, danger +0x0A, arrival and encounter cards
+0x0C/+0x0E, size +0x11, reputation +0x12, flags +0x14, local values +0x15..+0x17, cache handle
+0x18, mark +0x1A, recorded words +0x1C..+0x21, name +0x26 (20). Raw: +0x02, +0x10,
+0x22..+0x25 and the name's tail.

### CACHE.TMP

Byte 0 = 0x63, byte 1 = number of stores, the store h's offset at 2h, stores of a count byte and
{item word, quality, count}. Written as `cache_save(-1)` builds it: the 198-byte header of the
empty file, then the stores in handle order (at most 0x62).

## The .BSV file

A 0x2A-byte header of block sizes, then PKWARE DCL imploded blocks: the battle variables
(0x1978 bytes, in `battle_save`'s order: `DS:907A`, `cbt_paths`, … `marching_order` at +0x1573,
`DS:D661` level count at +0x16E4, `DS:DA34` at +0x16F3 …), per level the floor image (0x33FC,
object functions as numbers 0..8), the .atv and the .enm data, then the work files `DS:1164` and
`DS:1162`. `BattleSaveFile` explodes and splits it; the levels' floors are `BattlefieldLevel`s.
The program holds it as `GameState.BattleFile` (Core, `BattleFile` / `BattleVariables`): a load
replaces it (a saved game without one keeps the running game's, as the restore returns at once), a
battle fought here writes it at its end (`TacticalBattle.WriteBattleFile`: variables, levels with
their object functions as numbers, activations, enemy kinds, terrain.fil, the loot file), and every
save writes it as the .BSV through the ported implode (the compressor's work area carries over from
save to save, as the original's buffer; the header words of blocks not written keep their values).
A program that has had no battle and no battle file writes zero variables.

Each of the 18 .BSV files of the player's folder is written back byte for byte by the reader and
writer (`EveryBattleFileIsWrittenBackByteForByte`); read into a game and saved again, a work file
that was saved empty comes back as missing (raw size -1), because the restore removes it.

### Saving and resuming in a battle

Save Game (Alt-S, the Game menu) is offered while DS:9920 is 1: a persistent battlefield (the main
loop's battles), not the keep's tile set, outside real combat. The save ends the members' running
modifiers, writes the battle file from the battle (DS:907A = 1), the .SAV with bit 7 of the
difficulty and the members' combatant records (position, destination, animation, attributes,
pack), then switches to the shown level again. A game saved in a battle is taken up inside it: the
main loop skips its card, the battle starts from the battle file (levels, activations, kinds, loot,
doors, the variables) with the party where it stood and the timers running; the orders are dropped,
the weapons readied again and the enemies of the fight saved are gone (records 5..12 are not in the
file, as in the original). Load Saved Game works in a battle and leaves it.

## Divergences

* Bytes the original's battle file holds from earlier activity of the session (the level
  loader's buffer in .enm records, per-combatant table bytes of earlier battles, the pointer shape,
  the cursor keys' state) are zeros for a battle fought here; the files of the player's folder keep
  theirs. The compressor's work area starts as zeros (the original's holds what other loads left);
  it only matters when a repetition search runs past the end of a block.
* A damaged battle file fails the load with the original's message ("EOF-..", "Corrupt file-..");
  the original returns to DOS.
* The "Saving Game" box of a save in a battle is drawn in the box's own colour (the original: 9).
* A party slot whose record has status 0 (DKSAVE40) is written; the original skips it, copies the
  following roster records one place late and leaves the file a record short.
* New texts are padded with zeros; the original writes stack bytes after them.
* The folder lists in name order (DOS: directory order); the pointer stays visible on an empty
  load screen (the original leaves it hidden); after "no" to Alt-Q on the load screen the button
  picture still draws (the original has freed it).
* From the start screen there is no city record before the load; a load outside a city past the
  cities' range uses the lazy city 0 where the original keeps the start-up city's record.

Reproduced on purpose: Alt-L empties the party slots and the events before the load screen, and
Esc there goes on with that game (`OriginalBehaviour.FixLoadScreenEscape` fixes it); a game
saved in this session is named in lower case (`DS:A680`) and can be deleted from the list (the
original compares the names case-sensitively); the wilderness `city_rec` after a load is the
first 0x26E bytes of DARKLAND.CTY; the delete label is redrawn a pixel lower; stale row names
serve a delete on an empty list.

## The JSON snapshot

Removed. The original's format holds every part of the game the original keeps; the snapshot's
extra content (random generator state, bankers' standings, the working city record) is exactly
what the original does not save, and keeping a second format would mean keeping two mappings
in step. Saved games are inspected with `SaveFile` / `OriginalSaves` in tests.

## Tests

* `OriginalSaveTests` (Persistence.Tests): every .SAV of the player's folder (copied to a
  temporary folder) through `SaveFile` and through `OriginalSaves.Read`/`Write` byte for byte,
  with its .BSV; every .BSV explodes and agrees with its header; header fields of DKSAVE12; a
  record's fields and its kept bytes; event words; the cache layout; a new game in the original's
  layout and back; a character left at an inn; a saved game without a battle file keeps the program's; every battle file written back byte for byte;
  the wilderness and village city records.
* `LoadScreenTests` (Battles.Tests, on the game files): the EXE's button texts; down, delete and
  load pick the right files; the game being played cannot be deleted; the save dialog's next free
  name, comment and battle file; Esc saves nothing.
* `OriginalSaveGameTests` (Cards.Tests): DKSAVE12, DKSAVE18 and Dksave21 load, their card runs
  through the card dispatcher, and the game saves and loads again.
* `BattleSaveTests` (Battles.Tests): battles on a city, a mine and the Templar fortress are saved
  inside (Alt-S's steps), read back and taken up: the battle stands as saved and, saved again, writes
  the same battle file but for the orders and the two timers the start sets; the castles' keep tiles
  never offer the save; every level of the player's battle files goes through the .enm and object
  conversions unchanged. `ImplodeOracleTests` (Oracle.Tests): the compressor and decompressor
  against the original's code (Corpus/implode.json, tools/oracle/build_implode_corpus.py).

Tests that need the player's files pass vacuously without them (DARKLANDS_DIR, or the "game"
key of the git-ignored local/paths.json).
