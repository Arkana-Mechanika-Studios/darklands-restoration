# Faithfulness: where it stands

All checks run against the original DARKLAND.EXE (483.07), executed in an emulator or read from
its bytes, never against another reimplementation.

## The claim for 1.0

The rules are identical to the original's in the faithful mode (`OriginalBehaviour.Faithful`, which the
oracles and the headless host use). By default the player's game differs from it only in the 3 listed,
switchable data-safety fixes below. No known divergence, strong evidence, published coverage:

* every original function the C# cites is compared with it statically, and no lead is open;
* every oracle agrees with the original wherever it runs, or differs for a listed reason;
* whole scripted game sessions, played in the original and in the C# side by side, give the same
  game state at every step, a battle included, and fights started from the same situation agree at
  every combat tick; at present all of them do, and any that does not is listed as an open lead;
* the branch coverage of the original reached by the oracles is published below with its gaps;
* a divergence found later is fixed with the same tools, and registered with its reason.

It is not a proof that every branch of the original has run: counted over every function the C#
implements, about 63% of the branch outcomes have run in a comparison with the original, and the
rest is listed (`branches-missed.csv` for the functions the oracles enter). The best-covered areas
are the battlefield generators, the rules and the battles; the least covered are the card scripts and the screens (see below). In the card scripts, the open branches lie behind particular quest
stages, items and location facts; reaching them with start states made for each card continues
after 1.0.

## Rule: quirks stay, data-damaging bugs are switchable

* A gameplay quirk of the original (a wrong price formula, a skipped check, a card that misbehaves) is
  reproduced exactly and stays so by default; where a fix flag exists it is off.
* A bug that damages the player's data (saved games, the item stores in CACHE.TMP) is reproduced exactly
  behind a switch in `OriginalBehaviour` (marked `BUG:` at the reproduction, with the original's
  coordinates) and fixed by default in the player's game, which uses `OriginalBehaviour.DataSafe`. What
  the fixed game writes is still read by the original: saves are shared both ways. Future data-safety
  fixes go into `DataSafe`, and only those.
* The three fixes now in `DataSafe` (registered as deliberate fixes, `divergences.csv`; the bugs in
  `original_bugs.csv`):
  * `FixInnCacheRewrite`: the rewrite of a known store in CACHE.TMP (cache_save R035+0x1468, the inn
    cache bug) writes a 101-byte header over a 198-byte header's offsets and copies the other stores from
    memory it should not read; fixed, the file is laid out compactly (see the next item).
  * `FixCacheStoreLimit`: the original's CACHE.TMP cannot grow past its 198-byte table: the 99th store's
    offset is written over store 1's count byte and first item (the append at R035+0x1532), and its
    rewrite finds store 1 by the signed low byte of its offset (R035+0x15A2, `cbw` R035+0x1615), so a
    table of 128 bytes or more is garbled by the original's own next rewrite (emulated runs, kept under
    `local/datasafe/cache-limit`). Fixed (this and `FixInnCacheRewrite` lay the file out the same way):
    byte 0 = 124, byte 1 = the live store count, offsets at 2h, store 1 at 126, the stores packed in
    handle order; at most 62 stores and 127 entries a store, under 64 KiB. Stores no place refers to are
    dropped and the places' handles (location +0x18) renumbered when the file is laid out. The original
    reads, appends and rewrites such a file with no loss (runs `compact60-*`, their output is a test
    fixture). A Put (cache_key, R035 keys 0x1950/0x1970) that would need a 63rd live store or a 128th
    entry is refused when it is made, with the message "The innkeeper has no room for more." (own text,
    registered with the fix); the item stays in the member's pack, so closing the screen never loses
    items. The faithful mode keeps the overwrite byte for byte. The original's own fault that remains
    (it reopens the file without truncating it, leaving bytes after a shrunk store) is registered as an
    original bug.
  * `FixSaveWriterSkipsEmptySlots`: the save writer (R037+0x2ACE) leaves out the record of a party slot of
    status 0 but writes the full count, a short file the original can crash loading (the Wild Hunt causes
    it); fixed, every roster record is written.
* The switches are not exposed in the settings; the player's host sets the profile.

## Coverage of the original functions

See `COVERAGE.md`. Every original function reachable from the game logic is implemented with a
trace, excluded for a reviewed reason (`exclusions.csv`), or listed in `missing-allowlist.txt`;
`FaithfulnessCoverageTests` keeps the list shrinking.

## Structure of the C# against the original (static)

`STRUCTURE.md` / `structure.csv` (`tools/faithfulness/structure.py`): without running anything, every
original function the C# cites is compared with the C# that implements it: the calls with their constant
arguments, the random draws, the call order, the constants tested and written, and the number of
branches, read from the EXE bytes and from the C# (Roslyn). It covers every cited function at once,
including paths no oracle case reaches. Its findings are leads to review; a reviewed lead goes to
`structure-reviewed.csv` with the reason.

| | |
|---|---:|
| original functions compared | 5,563 |
| comparisons without a finding | 3,161 of 3,445 |
| leads reviewed (each with its reason) | 345 |
| leads open | 0 |

The first review of its leads fixed about twenty divergences in the card scripts and the rules (the
order of the party queries a card makes, menus that ran on the wrong layout, a member who did not
really join) and ported behaviours the C# lacked: the dragon fights (DS:C21A) and five battle
behaviours.

## Branch coverage of the original code

`BRANCHES.md` / `branches.csv`: blocks and branch outcomes of the original code exercised by the
oracle runs (where the C# agrees), per function; never-exercised branches are either reached by
new cases or listed in `unreachable.csv` with a justification.

| scope | blocks | branch outcomes |
|---|---|---|
| the 479 functions of the unit corpora | 88.6% | 81.2% |
| the 6,228 functions the oracles enter | 75.5% (41,220 / 54,595) | 64.9% (27,606 / 42,506) |
| **every function the C# implements (9,281)** | | **62.7% (26,820 / 42,782)** |

The last line is the whole-program figure: the functions no oracle enters count with all their
branch outcomes, decoded from the EXE (`tools/faithfulness/structure_exe.py`). The two decoders
differ slightly on the resident code, hence 26,820 against 27,606 exercised. By area:

| area | functions run / implemented | branch outcomes exercised / total | |
|---|---:|---:|---:|
| battlefield generators | 222 / 226 | 6,787 / 8,524 | 80% |
| rules | 318 / 358 | 1,796 / 2,584 | 70% |
| battles (tactical combat) | 252 / 314 | 4,970 / 7,516 | 66% |
| travel (overland map) | 23 / 24 | 603 / 982 | 61% |
| screens (inventory, barter, creation ...) | 177 / 202 | 3,485 / 6,070 | 57% |
| resident code | 131 / 160 | 904 / 1,644 | 55% |
| card engine | 34 / 36 | 377 / 686 | 55% |
| card scripts | 4,787 / 7,949 | 7,835 / 14,614 | 54% |

The whole-game sessions and the battle oracle record the original's coverage where they agree with
the C#; the screens are checked mostly by the structure comparison and the C#'s own tests.

28,275 items of the functions the oracles enter were never exercised: 1,241 are justified in
`unreachable.csv` (dormant branches — tautologies, repeated tests, unused thresholds —, compiled
forms, the C run-time library, drivers, memory and file helpers, the copy protection, code nothing
reaches), 27,034 are open. The battle oracle and the whole-game sessions entered about 400
functions no run had entered, whose unreached branches are now listed too. By area, the open items
lie in the card scripts (about 12,800), the screens (about 4,400), the battles (about 3,600), the
battlefield generators (about 3,000), the rules (about 800) and the overland map (about 700).

## Card oracle

`CARDS.md`: every one of the 365 original card functions runs in the emulator with a scripted
choice policy and is compared with the C# card (texts, option states, hints, random draws, state
changes, next card).

* The standing corpus, 14,600 runs from 40 start states: 13,542 agree (92.8%); 1,058 differ for
  one of the listed reasons (`card-divergences.json`) — cards run without their quest, where the
  original reads index −1 of a table; stack leftovers returned as hint results; option flags left
  by an earlier card; missing message blocks or files; the endless loop of card 0xAB; crashes of
  the original; 0 differ without a reason.
* Richer start states (`CardOracleStates`, exported with `DARKLANDS_CARD_ORACLE_VARIANTS`): a poor
  party, a rich one, a skilled one, one carrying every item, location facts and quests, and
  daytime. Run on all cards (36,500 and 43,800 runs), they reached about 1,800 branches no run had
  reached, and found divergences that are now fixed: three in the C# (the sally port's pop-up
  handlers, a merchant's price bound, a joining member's weapon quality and record bytes), two
  original bugs the C# had not reproduced (the university's formulas line, the altar boy's hour),
  and faults of the oracle itself. Every run now agrees or differs for a listed reason.
* Start states aimed at the branches no run reaches (`tools/oracle/branch_flip.py`, applied with
  `DARKLANDS_CARD_ORACLE_MUTATIONS`): where a missed branch tests a value the saved game holds (a
  word of the save, a started event), the state takes the other side. On all cards they reached
  614 more branches; the C# agreed on every run.
* Option lines never offered by any earlier state (883 lines of 156 cards): start states aimed at
  the condition that offers a line (a quest or event started, a saint known, a location fact, the
  hour) turned 81 lines on; the other 802 are run by forcing the line's choice in both games (the
  original's action and hint handlers run for real; 794 of 802 forced runs agree, the 8 others are
  the picture redrawn after a prayer from a line never offered, listed). Of the forced lines, 521
  are never offered on any path (marked `DORMANT` with the reason; their handlers listed in
  `unreachable.csv`), 12 are reachable under a condition no state builds yet, 269 are unsettled.
  The 1,278 aimed and forced runs reached about 800 branch outcomes no run had reached (card
  scripts 49% -> 54%); their coverage counts as the original's code compared with the C#, the forced
  part from a choice the player cannot make. They found one divergence, now fixed: the cloth
  makers' street (card 0x25) kept the side a faction takes in its own variable instead of the
  shared one the text reads (R053+0x120D, R053+0x17D6).

## Whole game sessions (lockstep)

`tools/oracle/lockstep.py` plays a scripted session of the whole original game in the emulator,
from its own start through its own load screen, with keys and mouse from the script and the clock
driven by the instruction count, and the same session in the C# (the headless host). After each
step both write the saved game with their own writers, and the sections, the random generator's
state and the current card are compared. The sessions (`tools/oracle/lockstep-sessions/`) start
from a saved game in Pressburg:

| session | | |
|---|---|---|
| pressburg-map | the inn, eight hours' rest, the streets, the gate at night, the map, a walk | 10 states identical |
| tyrnau | as above, then the walk to Tyrnau, its chapel and its priest | 15 states identical |
| residence | taking up residence, days passing | 8 states identical |
| save-load | saving with Alt-S, resting, loading it back with Alt-L | 6 states identical; the saved file is identical |
| gate-battle | fighting the gate's guards | identical in every state, every battle pass and both saves after it |
| shop | a merchant: buying, selling, dropping, barter for the next member | identical |
| cache | the innkeeper's cache: putting and getting items | identical |
| cache-revisit | the innkeeper's cache visited again: put, leave, back in, put, leave | identical (it found the rewrite of a known store's CACHE.TMP: a 101-byte header, the old bytes left behind, cache_save R035+0x1468, the community's inn cache bug; the C# now keeps the file as bytes and does the same) |
| saints | the character sheet's saints, praying to two saints | identical |
| stay | the residence: each member's work over five days | identical |
| wildcamp | camping on the map, work choices, three days | identical |
| sheet | the character sheets: readying items, the formulae | identical |
| alchemy | the alchemist brews, then days pass | identical |
| inventory | giving and dropping items, then the camp | identical |
| chargen-party | creating characters, beginning the adventure | identical, but for the party's seed, which the original takes from the BIOS timer (R037+0x9DB2; a registered divergence) |
| notices | the town square's notices, the town hall's clerks | identical |
| university | searching for saints and formulae, the materials seller, the teacher | identical |
| university-shop | buying from the university's materials seller | identical |
| smiths | the swordsmith and the armourer: selling, buying, barter | identical |
| healer | the physician's house | identical |
| churches | a donation into the night, the altar boy, Mass | identical |
| wharves | the boat to Olmütz, the wharf toll, the gate and main street there | identical |
| stables, pawnshop, foreigntrader | barter at the stablemaster, the pawnbroker and the foreign traders | identical |
| roster, roster4 | the inn's party screen: finding somebody, retiring members | identical |
| wien-rathaus | Vienna's town hall: the audience, the clerk | identical |
| mapsave | saving on the map with Alt-S, loading it back | identical |
| banks | the Fugger and Medici offices, the Hanse hall | identical (it found the hour of the Hanse hall passing before its options instead of after, R061+0x2278, now fixed) |
| menubar, menubar2 | the menu bar's keys and the keyboard menu | identical |
| orders-select, -walk, -missile, -attack-a, -attack-b, -attack-b2, -attack-v, -attacks, -sheet, -save | the player's orders in the gate battle: selecting, pausing, walking, missiles, the attack orders, berserk, the combat sheet, saving in battle | identical, and the 13 combatant records at every battle pass |
| luebeck-potion | the Lübeck gate: potions thrown in battle | identical, records at every pass included |
| orders-potion | as luebeck-potion, with the combat sheet opened for long | differs after the sheet: the original's timer ticks while the sheet is open, which the C# does not model |
| orders-potion2, orders-potion3, orders-prague-missile | the Prague gate: potions and missiles | identical, from a copy of DKSAVE40 made consistent (its true party size, no slot of status 0; the file as given comes from another tool, and the original's writer R037+0x2ACE recounts the size and skips the status-0 slots, which the C# now does too) |
| roster-colours, roster-moves, roster-wrap, roster-mouse, roster-create2, roster-ulm | the inn's roster: hiring, retiring, the portrait colours, creating a character, by keys and by mouse | identical |
| loot-keys, loot-mouse, loot-esc | the loot after a fight: taking, sharing, leaving, by keys and by mouse | identical |
| camp-wild-brew, camp-wild-risk, camp-inn-broke, camp-rough, camp-mouse | camping in the wild, at the inn without money, in the slum: each member's work over days | identical |
| pack-potions, pack-stacks | potions and the pack outside battle: drinking, splitting and joining stacks, giving one or the stack | identical |
| dungeon-mine-doors, dungeon-mine-orders, dungeon-mine-group | a fight at the Rammelsburg mine: doors, single file, the attack orders, moving the party as a group | identical, records at every pass included |

In one more session (the character sheet from DKSAVE0) the original itself stops with its "Buffer
dealloc error" (R031+0x0B22) where the C# goes on. Only the sessions that agree throughout count as
coverage.

The oracles record the original's side (`tools/oracle/recording.py`) and replay it against the C# for
day-to-day checks; a recording is used only when the hashes of the EXE, the game files, the arguments,
the saved games and the tester's sources are unchanged. A full live rerun of every fight and session
(`tools/oracle/rerun_all.py <out> --live`) is made before every release and periodically (at least
weekly during active work); the published verdicts and coverage come from such a live run (this one:
104 runs, 102 agreeing; the other two for the reasons listed above. The last open lead, a group move at
the mine acted on one pass later in the original, was the tester's: the original takes the keyboard
click in one pass (R016+0x0734 sets DS:D73B) and acts on it in the mouse handler of the next
(R014+0xB838), as the C# does; the tester gave the C# the key one pass early after a pointer move).

The emulator loads the original as DOS does: the PSP at 0813 and the root image at 0823 (where DOSBox-X
puts DARKLAND.EXE), the EXE's relocation items applied, above a real vector table and BIOS data area, so
the game's own install of its keyboard handler writes into the table; every fight and session runs
with this one setting.

The roster, loot, camp, pack and dungeon sessions found six more divergences in the C#, now fixed: a
character created at the inn reads the clock once for each random name, and its record keeps the bytes
R037+0x675A resets and the old name's tail; after the loot an item taken off clears the member's
equipped slot; a member's change of work calls off brews only from event slot 66 on (R036+0x1EC2, an
original bug, reproduced); a single-file follower's end-of-path byte 0xFF ends its step (R014+0x162D);
and the monthly update runs with the clock already moved on (R029+0x54D8, R029+0x5590), so a quest it
starts is dated as in the original.


They found six divergences the per-function checks could not see, now fixed: leaving the map
(the previous card and the hour pass after it), entering it (the party's square), the click to
walk (kept in screen coordinates), the map's scrolling (with an original quirk at its bottom edge),
arriving at a place (the party keeps its square), and Save and Load switched off in the camp,
the character sheet and barter. The battle needed more: the opening frame drawn before the first
tick with its random draws, the view's centre and following, the combat clock running while the
battle sets up, and after it the dead leaving the party, the unconscious waking and the combatant
records written back as the original writes them.

## Battle oracle

`tools/oracle/battle_oracle.py` starts a fight in both games from the same situation (the
battlefield, the seed and three enemy groups, put in place at the battle's entry R002+0x0000), lets
both fight without the player's orders, and compares the 13 combatant records, the queued damage,
the attack rates and the random generator's state at every combat tick. 37 fights so far, over
city battlefields, wildernesses, mines, castles, a keep, a fortress, tombs, a cave and hell, twelve of
them with enemies who cast spells or carry potions (alchemists, witches, friars, a kobold king,
vulcans, a demon lord, preceptors, leaders):

all 37 agree at every compared tick for their whole run. The seven that differed before (city13,
wild40, wild50, wild6f, mine10, tombs5f, cave60) were settled by two C# fixes and by the tester:
the minimum searches over the combatants take the last of equal minima, as root 0410:02A8 and 0410:01DC
do; an enemy's path is cut after 20 steps (R018+0x113A); the destination bytes a record keeps from
the enemy file until it first moves, and the clock read for the next activations check
(R014+0x0332), are now given to the C# by opt-in hooks that leave normal play unchanged. The casters'
fights found two more divergences in the C#, now fixed: a caster tests its lines of sight to the
members from the previous member, not from itself, because the area test it calls writes the line's
start (R018+0x093C, R014+0x469E; an original bug, reproduced); and on a keep's first level, where the
party starts at (0, 0) and members stand outside the level, the collision test reads the cells past
the edge as empty ones, with the party's sizes still 0 until R014+0x0258 sets them (R014+0x5ECA).

The fights brought the battle code run against the original from 32 to 252 of 314 functions (the battle sessions with the player's orders included). The
first five found one divergence in the C# (the first frame also turns whoever just got an action
toward its target, so enemies face their targets from the start); the other differences they showed
were the tester's own (fields the original has not set yet, the pause, the set-up and the clock's
jumps during long passes), passed to the C# by opt-in hooks that leave normal play unchanged. The
next round put the end test and the endurance recovery of a battle pass before the player's input,
as the original's R014+0x0000 runs them (a reordering within the pass, in normal play too). The player's orders are compared in the whole-game sessions above (orders-*, luebeck-potion), with the
13 combatant records at every pass. They found six more divergences in the C#, now fixed: the combat
flags of a loaded battle (the original never clears DS:A696, so a record's no-target bit from the battle
file keeps it out of the enemies' first turn, R018+0x1069); the sheet's end draws once more (R022+0x03B0);
freeing an enemy slot keeps its header tables (R025+0x0C02); a member's destination and +0x1D come
from the loaded record; the armour qualities of record 0 are not rewritten after the combat sheet; and
the loot screen shows the fight's wounds.

## Saint oracle

4,352 runs covering all 136 saints: all agree.

## Other oracles

About 12,000 further cases: money, calendar and random numbers, events, party and location
rules, prices and stock, character creation, names, the saints' help, attack and damage, enemy
setup, collisions and line of sight, path finding, every battlefield generator, spell effects,
memory-layout effects (writes outside arrays the original performs).

## Divergences and original bugs

`DIVERGENCES.md`, `ORIGINAL_BUGS.md`, `UNCERTAIN.md`, `DIVERGENCE_REVIEW.md`: generated from the
markers in the code, each with its reason and original coordinate: 171 original bugs the game
reproduces (each can be fixed by its own `OriginalBehaviour` flag), 316 registered divergences,
907 dormant branches kept as the original has them, 58 uncertain readings (of 301: 234 were
naming only, 8 settled by an agreeing oracle or the code, now closed).

## Presentation

The graphics driver's behaviour (start-up colours, fonts, XOR option lines, the fade,
see-through restores) was reconstructed from the original driver; the Quickstart sequence
matches the original DOSBox-X captures pixel for pixel (`tools/visuals/compare.py`). Drawing and
timing are otherwise checked by playing.
