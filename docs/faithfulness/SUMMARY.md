# Faithfulness: where it stands

All checks run against the original DARKLAND.EXE (483.07), executed in an emulator or read from
its bytes, never against another reimplementation.

## The claim for 1.0

Wherever an oracle runs, the rules agree with the original's in the faithful mode (`OriginalBehaviour.Faithful`,
which the oracles and the headless host use); the exceptions are registered, each with its reason (undefined
memory, the host, deliberate fixes, a few things not modelled: see "Divergences and original bugs"). By default the
player's game differs from the faithful mode only in the 4 listed, switchable data-safety fixes below. The aim is no
known divergence, strong evidence, published coverage. The differences found and not yet settled are listed as open
items there. Where it stands:

* every original function the C# cites is compared with it statically (2,695 implemented functions are cited only
  in statements, types or the map, and are not in that comparison), and no lead is open;
* every oracle agrees with the original wherever it runs, or differs for a listed reason;
* whole scripted game sessions, played in the original and in the C# side by side, give the same
  game state at every step, a battle included, and fights started from the same situation agree at
  every combat tick; at present all of them do, and any that does not is listed as an open lead;
* the branch coverage of the original reached by the oracles is published below with its gaps;
* a divergence found later is fixed with the same tools, and registered with its reason.

It is not a proof that every branch of the original has run: counted over every function the C#
implements, about 79.0% of the branch outcomes have run in a comparison with the original, and the
rest is listed (`branches-missed.csv` for the functions the oracles enter). The best-covered areas
are the rules, the battles and the battlefield generators (82-87%); the least covered are the resident
code (64%), the card engine (65%), the overland map (72%) and the card scripts (75%) (see below). In the card scripts, the open branches lie behind particular quest
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
* The four fixes now in `DataSafe` (registered as deliberate fixes, `divergences.csv`; the bugs in
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
  * `FixLeavingMemberRecordTail`: a member who leaves the party (party_remove R037+0x3B58) is written back to
    CHARACTR.TMP as the 0x80-byte member record only (combatants + p x 0x80): the saints (+0x80), formulas (+0x94)
    and the 64 pack entries (+0xAA) of the file's record stay, and only the item count (+0x7E) is the member's, so
    a member who rejoins finds the file's old saints, formulas and items under the new count (found by the card
    oracle with saved games that have an away member, batch 19; registered B-c48d94, test
    `LeavingMemberRecordTests`). Fixed: the whole record is written. The faithful mode keeps the mix byte for byte.
* The switches are not exposed in the settings; the player's host sets the profile.

## Coverage of the original functions

See `COVERAGE.md`. Every original function reachable from the game logic is implemented with a
trace, excluded for a reviewed reason (`exclusions.csv`), or listed in `missing-allowlist.txt`;
`FaithfulnessCoverageTests` keeps the list shrinking.

## Structure of the C# against the original (static)

`STRUCTURE.md` / `structure.csv` (`tools/faithfulness/structure.py`): without running anything, every
original function the C# cites in a member's `orig:` comment (all but 2,695 implemented functions, cited only in
statements, types or the map) is compared with the C# that implements it: the calls with their constant
arguments, the random draws, the call order, the constants tested and written, and the number of
branches, read from the EXE bytes and from the C# (Roslyn). It covers every compared function at once,
including paths no oracle case reaches. Its findings are leads to review; a reviewed lead goes to
`structure-reviewed.csv` with the reason.

| | |
|---|---:|
| original functions compared | 5,574 |
| comparisons without a finding | 3,180 of 3,439 |
| findings reviewed (each with its reason) | 304 |
| findings open | 0 |

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
| the 479 functions of the unit corpora, before their gaps were worked on (`branches-summary-before.json`) | 88.6% | 81.2% |
| the 8,557 functions the oracles enter | 88.0% (51,100 / 58,040) | 79.5% (34,613 / 43,540) |
| **every function the C# implements (9,281)** | | **79.0% (33,785 / 42,790)** |

The last line is the whole-program figure: the functions no oracle enters count with all their
branch outcomes, decoded from the EXE (`tools/faithfulness/structure_exe.py`). The two decoders
differ slightly on the resident code, hence 33,785 against 34,613 exercised (1,461 more branch outcomes than the published batch 17: the card scripts +1,244, rules +26 and screens +22 from the card oracle's saved-game start states and controlled battle and prayer results of round cards1, battles +166 from round B2's direct calls, both below, and 1 each in travel, the card engine and the resident code). By area:

| area | functions run / implemented | branch outcomes exercised / total | |
|---|---:|---:|---:|
| battlefield generators | 222 / 226 | 7,014 / 8,524 | 82% |
| rules | 335 / 358 | 2,253 / 2,590 | 87% |
| battles (tactical combat) | 284 / 314 | 6,497 / 7,516 | 86% |
| travel (overland map) | 23 / 24 | 705 / 982 | 72% |
| screens (inventory, barter, creation ...) | 191 / 202 | 4,760 / 6,070 | 78% |
| resident code | 137 / 160 | 1,044 / 1,644 | 64% |
| card engine | 35 / 36 | 444 / 686 | 65% |
| card scripts | 6,874 / 7,949 | 10,993 / 14,616 | 75% |
| other | 10 / 12 | 75 / 162 | 46% |

A second figure, kept apart from the first and never counted as exercised: the outcomes exercised
or justified as unreachable in play (a dormant arm, a compiled form, read from the original's code
and listed in `unreachable.csv`; 366 outcomes, 37 of them added in batch 14 from the code's dormant markers, each read against the original's branch): 79.8% (34,151 / 42,790). Most of what is open is reachable in
play and not yet driven by an oracle (in the resident code: the party panel, the intro).

The whole-game sessions and the battle oracle record the original's coverage where they agree with
the C#; the screens are checked mostly by the structure comparison and the C#'s own tests.

15,867 items of the functions the oracles enter were never exercised: 2,363 are justified in
`unreachable.csv` (dormant branches — tautologies, repeated tests, unused thresholds —, compiled
forms, the C run-time library, drivers, memory and file helpers, code nothing reaches), 13,504 are
open. The battle oracle and the whole-game sessions entered about 400
functions no run had entered, whose unreached branches are now listed too. By area, the open items
lie in the card scripts (about 6,430), the screens (about 2,010), the battlefield generators (about 2,070), the
battles (about 1,340), the resident code (about 410), the card engine (about 370), the overland map (about 470), the
rules (about 250) and the other code (about 160). A sweep
of the generators' seeds and forced maps added about 230 outcomes; the rest of their open branches test
tile contents and random draws, not inputs a sweep sets.

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
* The community's saved games as start states (17 saves from the players' archives, every card from
  each, 25,702 runs): about 1,600 outcomes no run had reached, from states real play produced. They
  found five divergences, now fixed: card 0x164 empties only the florins of the purse and the bank
  notes (R133+0x04D3, an original bug reproduced); the city-wall cards 0x73 and 0x115 draw the last
  picture when a negative query result matches no picture case; cards 0xC1/0xC2 name the goods
  merchant and the foreign trader from the titles at 290E:2107 / 2117 (R092+0x07B1, +0x10A4); card
  0xE6's prayer leaves the chosen name to the saint pop-up (R101+0x0E00, +0x1103). The other
  differences come from the oracle's stubs or from a normalised save, each listed.
* Handlers run directly (every option line and pop-up slot of each card's first menu chosen in both
  games, and the option tables of the menu-less cards called): 6,804 runs entering about 2,850
  card functions no run had entered. Most of it is code the player cannot reach through that menu
  (lines the block never offers, empty pop-up slots, dormant tables): the original's code compared
  with the C#, not play. Six small C# corrections came from it, and the C# no longer fails on an
  empty saint slot (0x37); the rest is listed.
* Further start states: other random seeds (about 200 outcomes) and states at 38 places of every
  kind (about 130, nearly all in cities). Neither is worth more runs.
* The handlers run again from the skilled party and three community saves (about 730 more outcomes)
  found three divergences in the party's records, now fixed: a hireling who joins keeps the slot's
  colours (R029+0x4650 writes no palette; the figure name is the one the card passes, below); a slot loaded without a record is
  zero memory (its weapon and missile qualities 0); a slot without a roster entry that a card makes
  unconscious stays off the roster, as the save writer (R037+0x2ACE) writes roster records only.
  Further reading of the joining code: a hireling added past the party keeps the bytes the departed
  member left in that slot (the original clears only the status byte) and takes the figure name the
  card passes (R029+0x48B1 copies it to DS:A668), keeping the slot's colours as said; a member rejoining from
  the roster keeps the slot's figure (R037+0x3D91 .. +0x3E16).
* Saved games of the players' archives with aimed changes, and controlled results of battles and prayers (round cards1,
  batch 18; the start states `S<id>-V<atoms>-O<n>` and the result keys `B<n>` / `Y<c|f|a>` of `CardOracleStates`): 37 saved games
  (three more, saved inside a battle, differ in a head byte the C# loader does not carry and are left out) were run on the 200 cards
  with the most open outcomes; every card that fights or prays was then run again from the states that reached it, with the battle's
  result forced to 1..5 and the prayer cancelled, failed or answered, so the result-5 arms (S047, S065, S067, S070, S072) run in both
  games; and random aimed states (hour, month, facts, quests, events, skills, attributes, saints, items, reputation, previous card,
  seed) for the cards of the sheet's 40 records. 29,310 runs count (675 more are left out because they differ or errored); on the
  batch 19 tree the replay of all of them against the C# differs in 42 runs only (card 0xFE, below), where it differed in 1,185 rows before the leads were judged. They added 1,292 branch
  outcomes and 1,529 blocks to the count of the functions the oracles enter (card scripts +1,243 in the whole-program count). The differences formed 16 clusters: 14 were judged in batch 19 (below) and the two tester ones closed
  (card 0xFE, the saves made inside a battle). The committed corpus `Oracle/cards-controlled.json.gz` holds 2,004 runs of the controlled
  results, 1,940 agreeing and 64 differing for a listed reason (test `ControlledResultsAgreeWithTheOriginal`). A community-reported lead on the alchemist's trade formula agrees: the
  traveling alchemist (card 0xA6) changes only the packs, and the formula seller (card 0x53) teaches the chosen member, in both.

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
| orders-potion | as luebeck-potion, with the combat sheet opened for long | identical (it found that the timer goes on counting while the sheet holds the battle clock, so a paused battle ticks as soon as the sheet closes: root 047C:000A, R022+0x0066 / +0x03C6, R014+0x0F7B; the C# now does the same) |
| orders-potion2, orders-potion3, orders-prague-missile | the Prague gate: potions and missiles | identical, from a copy of DKSAVE40 made consistent (its true party size, no slot of status 0; the file as given comes from another tool, and the original's writer R037+0x2ACE recounts the size and skips the status-0 slots, which the C# now does too) |
| roster-colours, roster-moves, roster-wrap, roster-mouse, roster-create2, roster-ulm | the inn's roster: hiring, retiring, the portrait colours, creating a character, by keys and by mouse | identical |
| loot-keys, loot-mouse, loot-esc | the loot after a fight: taking, sharing, leaving, by keys and by mouse | identical |
| camp-wild-brew, camp-wild-risk, camp-inn-broke, camp-rough, camp-mouse | camping in the wild, at the inn without money, in the slum: each member's work over days | identical |
| pack-potions, pack-stacks | potions and the pack outside battle: drinking, splitting and joining stacks, giving one or the stack | identical |
| dungeon-mine-doors, dungeon-mine-orders, dungeon-mine-group | a fight at the Rammelsburg mine: doors, single file, the attack orders, moving the party as a group | identical, records at every pass included |
| sheet-dksave0 | the character sheet of a member who wears no armour | identical (the sheet frees the same picture block twice, R031+0x0B22 .. +0x0C8C, which DOS allows; the emulator now frees as DOS does, where it had stopped the original with its "Buffer dealloc error") |

Generated sessions (`tools/oracle/gen_screen_sessions.py`: seeded walks of keys and pointer clicks over
the sheet, the pack, the saints, barter and the stores, the camp, the roster, the party menu and the
generator, from the start screen or a saved game; `gen-*.steps`) add 103 more sessions that agree
throughout: about 1,800 outcomes, the screens from 58% to 73%. Judging the ones that differed found two
divergences on the character sheet, now fixed: its box table has 19 entries, the last two empty, and
the cursor can stand on them (R136+0xBBE7; the C# failed there); D, A, U and P with the pack closed
leave an open box open (R031+0x1D40; only the default case R031+0x1C66 closes). Twelve more sessions
agree. In the shops and stores: Down in an empty store puts its row at -1, where the original reads
the bytes before the table (a heap address there; here they start as zero) and takes from them with
an unsigned compare (R035+0x114A; the C# failed there); a click on the party column in barter, the
stores and the loot opens the member's sheet; F1..F5 and Ctrl-F inside a screen are that screen's
keys, the hot keys being taken only by the card engine (R043+0x33C6) and the map (R030+0x0C44).
Twenty-one more sessions agree. In the start screen's party menu a click on a member's figure opens
that member's sheet (R037+0x01C0, R031+0x0124), and a press that closes one of the generator's list
boxes also acts as a click when the button is still down after the redraw (R037+0x7F06, +0x802A):
both now as the original. 59 more sessions agree (the party menu, the roster, the generator).

Only the sessions that agree throughout count as coverage.

The oracles record the original's side (`tools/oracle/recording.py`) and replay it against the C# for
day-to-day checks; a recording is used only when the hashes of the EXE, the game files, the arguments,
the saved games and the tester's sources are unchanged. A full live rerun of every fight and session
(`tools/oracle/rerun_all.py <out> --live`) is made before every release and periodically (at least
weekly during active work); the published verdicts and coverage come from such a live run (latest pass: a replay, 762 fights and sessions (37 fights, 725 whole-game sessions), all agreeing: the replay of batch 19, 586 s on 6 workers, every run replayed (none needed a live run) against the recordings of the last full live pass (batch 16: 590 fights and sessions, all agreeing, 8,135 s on 8 workers, after the tester's original side was changed); the 54 sessions that wait in a battle and press G or Q, whose timing the C# changed in batch 18, were run live once then: all agree (1,170 s on 6 workers; not repeated in batch 19, which changed no timing; the recordings are the original's); the 28 card corpora give the same differing runs as batch 18 (none at all; four lines of the per-card reports moved, cards 0x169 and 0x16A, whose battle-result runs now end at the death film), and the controlled-results corpus (2,004 runs: 1,940 agree, 64 differ for a listed reason) and the lead corpus of batch 19 (156 runs, all agree) give no other difference; the 30 battle call job files of batch 17 give the same summaries but for R016+0x0000's keyed jobs (batch 18: 651 moved from agree to not judged by a rule of the tester, below; batch 19: 1 more, a leader outside the party slots): 23,943 jobs, 14,481 agree, 0 differ, 9,462 not judged; round B2's 17 job files: 5,015 jobs, 3,978 agree, 83 differ, 9 raise an exception, 945 not judged). The last open lead before batch 14, a group move at
the mine acted on one pass later in the original, was the tester's: the original takes the keyboard
click in one pass (R016+0x0734 sets DS:D73B) and acts on it in the mouse handler of the next
(R014+0xB838), as the C# does; the tester gave the C# the key one pass early after a pointer move).

The tester now drives the right mouse button (press, hold, release; the driver's button state and counters
are set as the original's mouse driver sets them) and delivers F6 and F10 to the hot keys' look. The
sessions it added: 34 with the right button, 9 of hurt, away and cached members, 4 of the menu and 2 of
the map with F10. They found three divergences in the C#, now fixed and checked against the original:
card menus take Up and Down only while the pointer is over a line and ignore the digit keys (R043+0x1BD2,
DS:ED6C; the old keyboard behaviour stays as an option, off by default); the character generator's loop
(R037+0x4664) and its waits run neither the menu bar nor the hot keys; party members not placed in a
battle (unconscious or away) keep their record's place and facings (R002+0x1830).

Batch 14 changed the tester's original side again, each change proven exact before use. Two speed-ups: the
state capture's compressor (R003+0x2264) is run block for block outside the emulator, with the same register
frames at every timer interrupt, the same clock, memory and coverage (checked on 86 captures from live
sessions, 265,929 interrupts taken inside the function), and the per-block hook is one function. Faults of the
tester fixed: a battle sheet's seven pictures are placed in one DOS block, as the real driver keeps them (the
emulated DOS gave each its own block, six leaked each time a sheet opened, and the out-of-memory exit that
followed trimmed two gate sessions; gen-rgate-01 and -02 now run their full steps); INT 33h functions 5 and 6
report where a button was pressed and released; the wait counter restarts at each wait; the timer is counted
while the menu bar is up (an opt-in hook on the compare side, as the held battle clock needs: the bar holds
the clock while the timer's callbacks keep counting, root 0000:0000, root 047C:000A). The card menu's chosen
menu is kept per thread (a race in the tests, no change to the game). Held keys repeat at the PC/AT typematic
default in the host (the original never sets it; no effect on the rules).
Two real differences in the C# came out, fixed and checked against the original: in a battle, T opens the
Throw list at the next pass, not at once (R016+0x068A .. +0x06A1 only set the request, root 0000:0000 raises
the bar; BattleUiTests); and the overland step, the camp watch and the party's drawing read the map's cell
buffer as the original does, so in a view whose first column (row) is 0 the terrain under the party is the
cell one east (south) of it (R030+0x20D2 fills the buffer with 0 taken as 1, R030+0x1300 reads it around
+0x137B, R030+0x25BE is the camp watch): it decides deep water, the minutes of a step, whether the arrival
loop runs, the encounter roll and the weight. This is common in the west of the map. 144 direct calls of the
step after the original's own fill of each view agree.

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
start (the loop R018+0x0851 .. +0x09DC, R014+0x469E; an original bug, reproduced); and on a keep's first level, where the
party starts at (0, 0) and members stand outside the level, the collision test reads the cells past
the edge as empty ones, with the party's sizes still 0 until R014+0x0258 sets them (R014+0x5ECA).

Single battle functions are also called directly, in the original and the C#, from machine states
taken inside ten fights, with combatant records and battle tables varied within what the game
writes (`tools/oracle/battle_calls.py`, `battle_fuzz.py`; about 70 functions): about 800 more
outcomes. They found four divergences, now fixed: the recovery after a fight sets each member's
recovery clock and clears the hit shown (R014+0x0DEE .. +0x0E19); a door wakes an encounter by its
X (R023+0x0362 .. +0x0368); an enemy's path search that fails at its goal puts the doors back as the
last opening search left them (R014+0x48CA, +0x4B5A, an original bug reproduced); a member's pass
over the party's places always clears the place-changed bit (R014+0xDD17). A third round (the order mode,
the battle keys R016+0x0000, the mouse R014+0xB576, the order menu, loot, the compression library R003)
added about 800 outcomes (battles 72% -> 80%) and found more, now fixed: selecting a member stores the
flee bit (R016+0x0A7D); the place keys D, P and U do not test the selection (R016+0x0F76, +0x10CC,
+0x014E); leaving the order mode with nobody selected still resets the menu and the cursor (R014+0xBCA8),
and a shot order sets flag 0x200 (R014+0xBE85); a group click's timeout, formation direction, retries and
line of sight (R014+0xB8E8, R014+0xC75C); the keypad digits under NumLock are arrows; a dying member
uses the caller's animation set (R014+0x2324, +0x2363 / +0x2415); an arrived missile is removed in the draw (R014+0x6D39).

The fights brought the battle code run against the original from 32 to 252 of 314 functions (an earlier milestone, the battle sessions with the player's orders included; 284 of 314 now, in the table above). The
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

1,792 cases covering all 136 saints (966 value cases, 544 chance cases, 282 help cases; `saints-flip.json` adds 17): all agree.

## Other oracles

13,661 further cases in the committed corpora (the saint corpora apart): money, calendar and random numbers, events, party and location
rules, prices and stock, character creation, names, the saints' help, attack and damage, enemy
setup, collisions and line of sight, path finding, every battlefield generator, spell effects,
memory-layout effects (writes outside arrays the original performs).

## Divergences and original bugs

`DIVERGENCES.md`, `ORIGINAL_BUGS.md`, `UNCERTAIN.md`, `DIVERGENCE_REVIEW.md`: generated from the
markers in the code, each with its reason and original coordinate: 200 original bugs the game
reproduces (40 with their own `OriginalBehaviour` flag), 314 registered divergences,
870 dormant branches kept as the original has them, 36 uncertain readings (the others were
naming only, or settled by an agreeing oracle or the code, now closed; each of the 36 is in
`UNCERTAIN.md` with the assumption it rests on). Every core-logic divergence has a
review row (`DIVERGENCE_REVIEW.md`: the not-modelled, deliberate-fix and undefined-memory classes in game logic; the host and
presentation entries are not reviewed there) with its action; in batch 16 six stale notes were removed and
the justifications given evidence (a search of the whole EXE for readers, a table, a test), and the
rows of game logic were read again against the original's code (100 rows: 84 right, 8 with a slip in
the text, corrected, 7 wrong, of which 3 are now implemented and 4 are leads below, 1 unsure).

Open items (2026-10-26, after batch 19), listed honestly:
* Leads of batch 16: the barter's buy on a lit row past the list's end takes a leftover of an earlier, longer listing
  (D-dd4c2e); the sneaking card shows the previous card's text until a redraw, as it loads no message file of its own
  (D-ecc6f5); the loot screen's click on an empty pile row followed by G gives the entry past the pile's end, from a stale
  tail of the pile (D-b00418); Ctrl-F1 in the open pack reads the status byte before the party, whose value at run time is
  unknown (D-03c80e, unsure; classed undefined memory).
* The mouse pointer is drawn over a battle message box, where the original hides it (presentation; `DrawMessage` is followed
  by `DrawPointer`). It has no entry in the register yet.
* Twelve battle input sessions of round B1 still differ and are not counted: gen-bhurtk-02 (the right button let go after
  crossing two menus: the original's bar run counted a message box, the C#'s bar closed at once), gen-bhurts-01 (keys read in
  one pass inside the I sheet, R022+0x051C, plus or minus 1), gen-bkeys-21 (Alt-F then a view click), gen-bkeys-37 (right
  click then a click on the bar), gen-bkeys-51 and gen-bkeys-25 (Esc after an F-key sheet or F6: the C#'s next step one pass
  late), gen-bsafe-25 (the pass where a held-key view scroll stops is long in the original), gen-bedge-02 and gen-bedge-03
  (pointer moves into the view's edge zones and the bar's row), gen-bhurtk-01, gen-bhurtk-05, gen-bluer-01 (one pass of the
  original takes clock 911 to 1047). Ten more agree only with queued changes of the tester's original side (01
  schedule-reads, 04 box-callbacks; private runs), not applied, and, for gen-bsafe-27 and gen-bheld-40, also with measured click
  holds (gen-bsafe-50 with patch 01 alone). gen-bheld-22 is fixed (the C#) and its battle records agree, but its end state, taken inside
  the battle, still differs and was not proven again. gen-bkeys-44's step timing has no derivation.
* Round B2 leads still open, not triaged (local/batch/b2/leads.tsv): the battle end R014+0x08FE (the random state after N draws differs in 3 of 110
  jobs, with split floors and a replacement leader), an exception when the leader byte DS:907B is 0xFF (not a state the game has; the original
  reads record -1), the chest actions of R014+0x2170 (pack counts 02/FF against 03/00 in about 8 jobs, one C# exception), missiles in flight
  carried across a level switch (2 jobs) (leads L05 to L08; the single-file path byte after G or Q, L13, is fixed in batch 19 and the U key's
  level switch, L14, was the tester's, both below; no job of those two differs now). The per-battle state carried from one battle to the next (a lead from the community's bug list) needs a run of two
  battles in the original; no tool for it exists yet. The first run of R014+0x2170 (e2: 66 differing jobs, timer-callback counts) predates the
  tick wait fix and is replaced by the later runs of the same function; its coverage is not counted.
* Round cards1 leads (14 judged in batch 19, below): what is left of them is registered, not reproduced: the new game's slot 4 holds the figure
  "0" of SAVES\DEFAULT in the original, which a C# new game does not model for a slot nobody held (D-dff113); a loaded saved game with two slots
  naming one record is read as two copies, so a later save would give the second a roster entry of its own (not seen in any oracle run, not
  changed); the stack or session leftovers D-9654eb (a text variable past msg_file), D-9903b8 (the abbot's score in the verdict of card 0x8D),
  the 'time 16' of card 0x41 and the hint value of card 0x11 depend on menu and text code the cards do not model. Open for the audit: the
  ResumeBattle clause of the save writer (D-f631fd, below).
* Evidence round (batch 18, local/batch/evid1 and evid1o): 36 uncertain readings remain (one, U-b280c3, the abbot's score in the verdict of card 0x8D, became the registered divergence D-9903b8 in batch 19), each in `UNCERTAIN.md`; those needing a session
  or a captured state are the stale-local cards (U-aff1d9, U-ecbac4, U-4c93ee, U-c18edd, U-4ffac2), the tester's interrupt table
  against the reference machine's (U-9860ee, U-6d067b, U-16b0a7, U-69b939), generator and battle states (U-2a08c3, U-8f4107, U-5bdb14,
  U-8df8a9, U-91948c, U-cb31b9, U-5d20fe) and screens and timing for the user's checks. Further: the other cards' stores of the shared options
  block DS:EE53 are not modelled (356 write sites; D-4d048d); whether an event created by a prayer on the creation sheet survives into the begun game;
  the creation session chargen-created still differs in the new name's stale tail bytes and the party seed; the copy-protection direct call
  returns after the five draws in the tester (not investigated); of the community's bug list, the inn saving, the neon palettes, the warehouse name,
  the merchant selection, the two menus, the strikes and the stale step after a re-path are not proven.
* Registered in batch 19 for the audit of game-logic rows (reading taken from earlier audits, not re-derived against the EXE in the batch): door_set R023+0x04AC
  leaves its direction local unset for a door record with none of the four side flags and the C# takes north (D-e92f84; whether a generated level can hold
  such a record is not established), and the wall search R006+0x1D48 reads its two 40-cell local arrays up to two cells outside, which the C# reads as 0
  (D-0bf8d6; matters only for a room within two cells of the map's edge, not proven absent). They were inline notes in comments, now markers with review
  rows; the text-variable guards of TextVariables (the third inline note of audit 18) were not a divergence of their own and the comment is reworded. The save writer's
  `|| state.ResumeBattle` has no counterpart in the original (bit 7 of the saved game's byte depends on DS:907A alone): kept and registered (D-f631fd)
  because the headless host's state writer needs it; it changes nothing in play. A tester gap, low: headless states taken inside a battle that was not
  loaded from a save lack the in-battle bit.
* Kept as documented: the original exits (code 99) on the roster's arrow keys when a member stands on row 4 or later of a
  long roster (a missing picture; the C# documents a deliberate difference); the undefined-memory reads D-6300b5 (Down inside the menu bar's
  item -1 sub-menu reads memory of the data segment that is not modelled), D-ff5e3e,
  D-5c358f and D-8bb04c (below); the camp watch's flat read of its settlement block (a gameplay quirk, reproduced).
* Unverified: whether Space pauses the C# battle where
  the original does not (res5b); the map-rest key case (D-3721e3), proved by the original's code and a unit test only, as the
  tester cannot feed it; the tester's script reader drops a bwait that follows a non-input step (the generator avoids it) and the
  BIOS tick 0040:006C runs at about twice the rate of INT 1Ah and stands still in a battle (no reader known to care).
* Registered as not modelled (12; most are justified as unreachable or without effect): the enemy item lists' counting
  (D-3859aa), the prayer without its attached presentation (D-e60a70), the
  deleted member's shield picture (D-34f49d), two omitted pop-up flags (D-920e47, D-40b596), the omitted previous-card global
  (D-58560f), the other cards' stores of the shared options block (D-4d048d), the fortress room bookkeeping (D-ca7108), a card number outside the table (D-5dc345), the rejection of an
  oversized message (D-3854a2), the copy-protection question (D-9c9eab) and the joystick with the overland loop's reload
  (D-959078).

Closed items, kept for the record. The three reachable original bugs listed before are now
reproduced behind fix flags (faithful by default; none damages a saved game, so none is in `DataSafe`):
with nobody selected the stairs key copies entry -1 of two member tables (R016+0x014E; DS:C77D = DS:C8B1)
and Esc writes the flee bit into the pause byte DS:A695, which can become 0x11, neither paused nor running
(R016+0x13D4 .. +0x13F9); an exit's 0xFF wall mark is lost when the level switch does not save the level
(R023+0x1657, R024+0x05B4 saving only when DS:D4E4 is set). The leads of the direct calls are judged: a
prayer for the whole party boosts no weapon skill when no menu bar has been opened, since R029+0x0792 passes member -1 to R031+0x5D06, which
reads the menu bar's words before the member records (reproduced; the session-dependent case is D-8bb04c, below); a group move whose leader's path is
shorter than two steps reads the bytes before that path (R014+0xC833 .. +0xC891, reproduced); the AI's path
hook runs before the search's shortcuts (R014+0x4AE4); a random placement makes 501 + 501 tries
(R024+0x0B2E .. +0x0C17); an enemy's pack keeps the entries past its stacks (R025+0x0314 .. +0x035D); the
battle keys are matched on whole key words as R016's switches do (the keypad digits scroll the view, a
split party refuses every arrow); the rest were the tester's (the monthly update called with a month its
callers never pass, R029+0x5537 / +0x55CD; the uprising outcome outside a city, which no caller reaches).
After a menu-bar sub-menu has been opened, a party prayer in the original scans the last battle's leftover
records (DS:C0A0 + 6i, never saved) for a quality equal to DS:9C2D, a DOS segment: not deterministic game
state, kept as a divergence (D-8bb04c).
Resolved in batch 14: the tester's memory leak at the battle sheet (above) and the marching-order click lead
(a tester fault: the mouse driver's release query now reports where the button was released, INT 33h fn 6).
Resolved in batch 15: the map's drawing at the view's column 0 or row 0, shifted by one cell as R030+0x20D2
does, is compared pixel for pixel at the border (2,824 frames, 0 pixels differ; the divergence once listed
for it is closed). Kept as documented (listed above): the original exits (code 99) on
the roster's arrow keys when a member stands on row 4 or later of a long roster (a missing picture); the C#
documents this as a deliberate difference. The camp watch (R030+0x25BE) reads its 10 x 10 settlement block
flat through the cell buffer's 19-wide rows, so a row wraps into the next and the read runs into the 70
deterministic bytes after the buffer (the icons the fill writes one row or column too far); this is a
gameplay quirk, reproduced and faithful by default. Past those bytes it reads the marker picture handles,
which are undefined memory (a registered divergence, D-ff5e3e; rare). The map drawing decodes its cells from the original's row buffer as the map screen reads and scrolls it (0x83 slots of 0xD2 bytes): at the map's east border the last column is decoded past each row's end (the next row's first cells), a scroll up leaves the rows of the old top in the buffer's lower slots and the scroll block's bottom jump reads no rows at all, and the map drawn, the step's terrain and the camp watch then show those rows, as in the original (pixel jobs at the east border and after those reads: 10,000 frames and 2,684 cell buffers, 0 differ; the camp watch from blocks reaching the last column: 120 direct calls agree; the step is not affected at the east edge, and reads the screen's buffer after the bottom jump). Where the decoder runs past the buffer's block (the map's bottom view) it reads the DOS memory after it (D-d000fa, undefined memory); the block's first contents are zeros as in every captured state (D-53cd7b). The battle's palette cycles (the battlefield's water and fire colours and the 14 cycling enemy schemes, a rotation of a DAC range every few frames by the timer's frame work) now run as the original's routines do, compared step by step with direct calls of them (3,000 frames, every DAC byte equal); when within a frame a step comes is host timing (D-38f2ca). 40 battle-result
markers said DORMANT for results the card engine never gets; a result 5 (every member down) reaches a card when
a member was left behind by an earlier flight, so they now read COVERAGE GAP, and three more notes were split;
six sessions now reach it (S049, S053, S074 twice, S084, S085: 39 more outcomes).
Batch 15, the rest: the comparison now reports a clock reached by one side only (a shorter C# battle trace passed
before; replaying everything found no hidden difference), and the scheduler's clock rise after a re-path search
is applied where the original has it (an oracle hook only). Runs: 584 fights and sessions agree (replayed; the 6
new sessions run live), the 28 card corpora and the 30 battle call jobs are unchanged.

Batch 16 (2026-10-25). The tester now feeds the keyboard interrupt: a `kdown` / `kup` step puts each scan byte on port 60h
and runs the game's own INT 9 handler (root 044A:020E), which keeps the held-key bytes DS:0880..0889 itself (the handler is
covered; `ctrl` sets the BIOS flag byte); the copy-protection stub draws through the original's random routine. Six sessions
with held keys on the map and in a battle agree (186 never-run items reached: R014+0x35FC, R014+0x3166, R016+0x0000,
R030+0x02C0). Real differences in the C#, found by the reading, the direct calls and these sessions, fixed and checked
against the original:
* the overland loop's held-key reading (R030+0x0540 .. +0x066A): Enter and the directions held replace the key that was
  read, then the held bytes are cleared; a read with a direction held and nothing waiting blocks (047C:0744 is a blocking
  INT 16h read);
* the copy-protection question's five random draws are made on the first map visit (R041+0x0000; the question itself is
  left out); after a card battle the city record is read again from the game's city file (R043+0x054E, +0x078E), losing
  that visit's changes (city size, shop qualities: per-visit fields, never saved, so faithful by default);
* every battle message blocks for its ticks as the original's (root 0000:24BC, delay root 0410:0302): no pass runs, the
  timer's callbacks count, the combat clock too unless held; the sheet's "can't view" message the same (R022+0x04E4, before
  the clock is held);
* the map decoder at the east edge and two quirks of the row buffer (a scroll up keeps old rows, the jump to the bottom
  reads none), in the drawing, the step and the camp watch (above); the bytes after the cell buffer;
* the enemy palette cycles and the battlefield's water and fire cycles, run as the original's timer frame work does (3,000
  frames from direct calls of the original's routines, every DAC byte equal);
* hot keys in a battle: F6 / Party Info is refused, and the Orders, Attack and Party titles are greyed around the sheet and the
  loot (the screen flags DS:987F, DS:9893); F1..F5 act only in the card engine's and the map's loops; battle loot to a member who left
  the field is the original's (R040+0x0DB3 reads a byte table as words: an original bug, reproduced behind a switch);
* leaving the order mode with nobody selected writes entry -1 of the tables (R014+0xBCA8; behind a switch, the rest of
  what it reads is undefined memory, D-5c358f); the sheet's "can't view" check for a member who left (R031+0x064B, +0x07AB,
  +0x083E); the rest key tests the key after the step (R030+0x0C44); the saint message comes before the help
  (R034+0x0166); the menu bar's Right after F10 reads the word before the item table, whose high byte is best_member
  (DS:991D); barter and the sheet draw again after F6 (R032+0x06BF, R031+0x06D2) and the creation screens clear the left
  release count on leaving (R037+0x0348, R037+0x4ADC); the sheet's A, U and P act on a line past the pack's end
  (R031+0x192E, +0x19E2, +0x1B2C); 8 presentation reproductions (the south border's rows, redraws, clamps).

Batch 17 (2026-10-26), two coverage rounds. Round B1 (battle input): 164 more whole-game sessions of generated and hand-made
walks in a battle (keys, held keys, function keys, modifiers, the right button, pointer clicks and edge zones, orders; the gate
fight of DKSAVE0, hurt and dead starts, Lübeck, Prague) agree throughout (53 of them with the combatant records compared at every pass,
the other 111 on the states, the end state after the battle); the 100 first sessions of b1 and 9 of b1t, the only ones measured, added 245 open items (blocks and
branch outcomes; battles +143 branch outcomes; R014+0x35FC, R014+0x3166, R016+0x0000 and others); the 64 sessions of b1def, b1h, b1r and b1o2
were never measured. The 164 sessions found six real differences in the C#, fixed and
checked against the original: the battle's path buffers (DS:D6FB) start as zeros and keep the last battle's or load's bytes, as
the original never clears them, so a walk with no path given follows them; the battle's leader is the party's leader byte
(DS:907B; member_activate R016+0x09BC, combat_over R014+0x08FE and the sheet's Ctrl-F1..F5 write it, so a save after the battle
holds it); the battle's Game menu keeps the tick bytes of its items (R000+0x083E: the dropdown is 6 px wider, root 0000:07D4);
Alt-D changes the one difficulty byte DS:906A that the battle's skill gains read (hot_keys root 0000:0CAA); order mode keeps the
destination's y (DS:C67D) for the life of the program and clears only its x (R014+0xBC6A, R016+0x13DA; the walk order of a
combatant with no click uses the last spot's y); and the clip test (R014+0xF284, an original bug: its second point is never
initialised) reads the stack place of each caller, so a line test starts from a clean point unless its caller's run keeps one
(enemy_sees' tests, the missile test R018+0x0BE7, an enemy's candidate loop, the spot search, the cloud tests; D-fb3254, probed in the original
under each caller). The other differences of the 164 sessions were the tester's: click holds (the original keeps a button down some
passes), a script reader that drops a wait after a modifier, box, menu and sheet callbacks; they are compare-side rules and step
fixes, none changes the game. Round R1 (world events and rules): aimed direct calls of the city politics (R039+0x0BF6 .. +0x0EBE,
R029+0x4180), the quests (R039+0x1712 and neighbours), items and members, prices and the expiry handlers added 222 open items
(blocks and outcomes, measured on the first 1,635 agreeing runs; the rules from 81% to 86%, +134 branch outcomes); two harness faults were
fixed (the event table is cleared before a saved game loads, as the game's load does; one function's argument order). The audit of
batch 17 found that the world-call comparison never looked at the bytes of a record a call appends; that is fixed (the bytes of a new
record are compared) and every world-call jobs file was rerun: 57 runs stopped agreeing and left the coverage. 38 were the old jobs of
R039+0x10B6, whose adapter gave the C# a place that the original does not read (it reads DS:E89B); the adapter and the generator were
fixed, 42 jobs redone, all agree. One was an old two-argument job of R039+0x0000. 18 runs of R039+0x1712 (quest 10, the new event's
location) differ and are open leads, not triaged (probably the documented uninitialised sort and item of region kinds 1, 2, 9 and 11);
there is no change in the C#. The agreeing world-call runs now: 1,626 of R1 (the other corpora unchanged: 630 rules, 1,673 travel,
2,448 map entry); the contents of the events a call creates are now compared in all of them.

Batch 19 (2026-10-26), three branches. Card leads (cards1o, 0ad0d6c): the 14 clusters of the card oracle's differences were judged against the
original's code and runs. The death film (cards 0x8F, 0x123, 0x8D: nobody is left, the original plays it and stops at the keyboard test) was the
tester's: the harness now records `film death` and ends the run (44 + 1 + 21 runs agree). Three were real and are fixed, each with a unit test
(`CardLeadsTests`, `LeavingMemberRecordTests`): card 0x6C's text variable reads the loaded message file through party slots 25 and up
(var_expand R043+0x1316; `AChosenIndexPastThePartyReadsTheLoadedMessageFile`); a hireling added to a slot nobody held takes no figure, and a character
already in the party is read into the slot as its own copy (R037+0x3CD6; `AMemberJoiningASlotNobodyHeldTakesNoFigure`; the rest, the new game's
slot 4, is D-dff113); the knight's renown wraps at 16 bits (R092+0x16F2; `TheKnightsRenownWrapsAtSixteenBits`). One was a data-damaging original
bug, the member record written back when a member leaves (R037+0x3B58; cards 0x0D and 0x8D with an away member): reproduced exactly in the
faithful mode and fixed in the player's game behind `FixLeavingMemberRecordTail` (B-c48d94; the fourth `DataSafe` switch, above). Four were
original bugs reproduced or registered: the abbot's score in the verdict of card 0x8D (R079+0x0CF6, stack leftover; D-9903b8), the text
block 7 of $CityE00.msg that the file does not have (D-b58c48), the 'time 16' of card 0x41 (R060+0x0FF4, existing D in WildHunt.OutrunChance)
and the hint of card 0x11 (R048+0x124E). The committed corpus `Oracle/cards-leads.json.gz` holds the 156 runs of these clusters (test
`LeadRunsAgreeWithTheOriginal`, all agree). Card tester (b2t, 1f28f8a): card 0xFE's endless fights stop at the 202nd battle on both sides (the 50
distinct runs agree) and the saves taken inside a battle clear ResumeBattle as the main loop does (1,032 runs differed, 0 now). Battles (b2t
e4ba58f, b2l13 facf790): the U key lead B2-L14 was the tester's (a fuzzed leader outside the five party slots; such jobs are not judged); B2-L13
was real and is fixed: the marching order keeps its five bytes DS:C624..C628 and the length apart, the rebuild with nobody able leaves the bytes
as they were, and single file acts on the first byte (R016+0x0E2E, +0x0E7E; `SingleFileWithNobodyAbleTakesTheStaleFirstByte`; the byte past the
13 records of a foreign save is D-ec9372); jobs e14 and e17 now agree throughout (134 of 193 and 171 of 240, 0 differ, the rest not judged).
Replay of everything on the merged tree: 762 of 762 agree, the card corpora and the world calls unchanged, the battle call jobs as above; the
coverage moved by 2 outcomes and 1 block only (the reinstated card runs and the now agreeing jobs of e14 and e17), so the figures above are batch 18's plus that.

Batch 18 (2026-10-26), three rounds. Round B2 (battle internals): direct calls of the battle code, after the harness gave the game's tick wait
(root 0410:0302, which every battle message uses) the counters of one timer callback (the calls that reached it never ended and were
dropped before), with new adapters for the surrender question (R022+0x03E4), leaving a level (R023+0x17D2), placing around (R024+0x08F2) and the
cloud line test (R014+0x10672), scenarios with doors, chests and stairs, and 7 sessions (the enemy information in five card fights, the
marching order by keys and by the pointer). 3,973 jobs of 17 runs and the 7 sessions agree and added 167 branch outcomes and 155 blocks.
Three real differences came out, fixed and checked against the original (tests in `JudgeBattleLeadsTests`): leaving a level drops the
weakest enemy by the whole 16-bit percent, 100 x endurance / maximum computed as a word (R023+0x19BD .. +0x19C2;
`LeavingALevelDropsTheWeakestByTheWholePercent`); a surrender with no able enemy keeps the last enemy level DS:C651, which is set only from
the first able enemy (R022+0x0427 .. +0x0440; `SurrenderWithNoAbleEnemyKeepsTheLastEnemyLevel`); and G and Q show the marching-order message
286B:0231 for 60 ticks when they rebuild the order (R016+0x0E54 .. +0x0E73; `SingleFileOverABrokenMarchingOrderWaitsForTwoMessages`; it had been
registered as the screen's, and the 54 sessions with G or Q in a battle still agree, run live). The recorded jobs rerun on the fixed C# agree
where they differed (R023+0x17D2: 206 of 215 agree, 0 differ, 9 not judged, against 184 and 22 differ before; R014+0x116E: 120 of 120 and 123 of 123), but for 6 jobs of R023+0x17D2's earlier runs (e11, e12) whose fuzzed state puts an enemy in animation slot 1..3, which the game never does (there the original's count word overlays a kind word). The
tester's changes: the R014+0x116E adapter takes the state's view origin, and a keyed call of R016+0x0000 is not judged when the clock hold
DS:07D3 disagrees with the pause DS:A695 (the game sets and clears them together; this moved 651 earlier jobs from agree to not judged and
their coverage left the count). Round cards1 (above): saved games of the players' archives and controlled battle and prayer results in the card
oracle, +1,292 branch outcomes; no game code changed. Round evidence (track C): 23 uncertain readings were settled or
re-classified (the register went from 60 to 37) and the community's bug list was read against the original. Real differences fixed, each with a
unit test: the creation menu's "Add to the Party" recomputes the member's encumbrance (R037+0x0D67 equip_weight; `CreationRosterTests`);
the camp offers alchemy for any bit of a formula byte, as R036+0x1E9B tests the whole byte (`CampAndSheetRulesTests`); the jewelers'
guild at night uses the lowest Stealth value as the member, so a party whose lowest value is 20 can raise Place[11] of the last battle's
table (12% on a failed break-in), the one record past the party that reaches a saved file (R067+0x1A0A, practice_skill R039+0x0132; not data-damaging,
a new battle resets that table, so faithful in both modes; `RecordOverrunTests`; the other records past the party are registered as
undefined memory, D-472715); and the member
sheet of the creation menu uses the program's start-up game, the start-up city at 9:00 on 26/8/1400, captured in the original at the menu
(`StartUpDateTests`). Registered, not changed: the Hussite band's potion pop-up calls an earlier card's stale handler (undefined memory,
D-bd2879); the dragon's blighted land shows the options block the battle keeps, which the C# now takes from the shared block on the dragon's three
ways in (`DragonRetainedBlockTests`; the other cards' stores of that block are not modelled, D-4d048d); the enemy loader's buffer leftovers are
undefined memory, not a gap in the model (D-990fbb), and the copy-protection question is justified (D-9c9eab). One decision of the project
owner: when the stranger's mob offers the ointment with no line enabled (R128+0x03B8), the original waits for ever (a direct call of card_menu
took 602 inputs, every key and 552 clicks, and did not return); the C# takes line 0, the escape line, so the game does not hang, kept as a deliberate
divergence (D-1b04e8; `EmptyOfferTests`). The quest-start lead of batch 17 (R039+0x1712, 18 runs) is closed: all 18 start in the wilderness
(location -1, the far pointer to the interrupt table's first words in the tester), where the documented divergence applies, and the function is only
called from cities. A card deleting an empty party slot (U-ee7ab6) is settled as dormant: the callers pass a member. The items of that list confirmed in the
original's code and reproduced by the C# now carry a `BUG:` note: the wrong mount mask (R084+0x054F), Edward's payment (R039+0x4384), four saints
(R039+0x8E02, +0x706C, +0x4AD0, +0x58D4), the politics quest answering fact 0 (R029+0x3279 .. +0x3281), the missing fact 8, the conflict outcome clearing
the group's facts (R029+0x3E06 at R039+0x0F3E), the city news (R069+0x151C, +0x15A8) and the handbills (R057+0x21DC .. +0x2230). The agreeing
world-call runs and the battle call jobs of earlier rounds are unchanged but for the 651 jobs above; no run of the other corpora stopped agreeing.

Earlier round (2026-10-23): a saved game made in a battle (the mines of DKSAVE99: save, reload, save
again) agrees with the original's, the .BSV block by block (`savemid-mine`); it found five divergences,
now fixed: a load leaves the program's battle seed word DS:D6CE alone; a battle's reset sets every
combatant's exchange to 7 (0E76:19D0); the battle view's centre (DS:C866 / DS:C886) and the seen positions
(20A5:79A9) are the program's, kept through a load; a load inside a battle reseeds (R043+0x078E). None
damages saved data. On the overland map, entering a location needed the party a little above the city
(reported for 0.9.1): the entry test (R030+0x1300) agrees with the original in 2,448 direct calls around
Pressburg from all sides, the difference was the drawing: the original copies its map buffer from (8, 8)
to the screen at (60, 16) (R030+0x2599 .. +0x25AD) and draws the party's corner at the marker + (0x36, 9)
(R030+0x1B96 .. +0x1C3C); the C# drew the map 8 pixels right and down and the party one lower. Fixed.
The party's drawing (R030+0x16C6) is now reproduced too: the lift by three 4 x 16 tables indexed by the pixel
inside the cell (terrain 16..19 and 20..23 raise the sprite, the minor river, tidal marsh and ford lower it), the rows
drawn over the sprite by terrain (R030+0x1B3E .. +0x1B80), the map's cell buffer as 20d2 fills it (a place icon 18
columns right of the view's first column also lands in the next row's first cell and changes the masks at the left
edge), and the screen kept between passes (the map drawn again only when the view moves, the party's 17 x 21 rectangle
every pass). The original's drawing calls, run directly in the emulator from two map states (1,784 frames: 584 single
frames over every terrain group and 20 walks of 60 passes) and replayed through a model of the graphics driver
(MGRAPHIC.EXE), give the same pixels as the C# (tools/oracle/map_draw.py, OverlandDrawingTests). The cell buffer's overflow (R030+0x16C6) is
reproduced. The drawing at the view's column 0 or row 0 (shifted by one cell) is compared too (batch 15, above). The party rectangle's rows below the screen at the south border (screen lines 200..202 land on the top line through the
driver's row table) are reproduced and compared (map_draw.py --south, 90 frames). The 70 bytes after the cell buffer (zeros, and the icons the fill writes one row too far, kept for the program's
life) are the original's too, in the drawing and the camp watch (840 cell buffers equal, 3,290 frames with the south jobs, 0
pixels differ). Left as a divergence: the bytes just before the cell buffer (the tail of the place icons' handle table, a driver
allocation: host, D-2439b3).
A click on a card menu sets the highlight from the pointer before it is judged (R043+0x21D4); a key
pressed during the map's pause waits for the next pass (R030+0x3096); the battle screen tests the pause
byte as the original at each place (the order pointer when it is 0, the clock held while it is not 0).
A second round of generated sessions (225 more that agree: the menu bar in its keyboard mode in a city,
on the map and in a battle, the quit paths, the gate battle's orders, potions and packs on saved games
edited for the purpose (`tools/oracle/synth_saves.py`), the party menu, the alchemist's bench, the cache,
the camp, the sheet, the generator) took the resident code from 57% to 62% and the screens from 75% to
78%. Its differing sessions (Alt-M in the menu bar's keyboard mode, an unconscious member in the gate battle, an away member's record bytes after a battle, the gate battle with Alt toggles, the cache's G after g, Left, B, two map sessions with F10) were settled in batch 13: the unconscious and the away member were a real difference (fixed, R002+0x1830); Alt-M, the Alt toggles and the two F10 map sessions were tester artifacts (the scripts and the F6/F10 delivery fixed); the cache's G agrees, its heap pointer (R035+0x114A) being reported as undefined memory. All of them agree.

The battle direct calls' old differences were the harness's (15 now agree, 13 unreachable states); every judged job of
those files agrees (batch 19: 14,481 of 23,943 agree, 0 differ, the rest not judged).

Travel functions called directly from map states (the step R030+0x1300, the camp watch, the minutes per
step, the encounter weight, the season palette and three R029 helpers; 25 map states): about 80 more
outcomes, and three divergences, now fixed: on a walk the step does not write the party's position, the
key handler sets it from the marker at the start of its next pass, so a save made from the menu bar in
between holds the cell the party stepped from; a leader index left past the party (the leader in the last
slot removed while nobody else is able) reads the slot table, as R029+0x255A does; a slot of status 0
past the party keeps its id, so the faithful writer leaves its record out as the original's does
(B-0c7843). The road encounter's draw is the same draw in the same order (checked on the generator).

## Presentation

The graphics driver's behaviour (start-up colours, fonts, XOR option lines, the fade,
see-through restores) was reconstructed from the original driver; the Quickstart sequence
matches the original DOSBox-X captures pixel for pixel (`tools/visuals/compare.py`). The map's
drawing, the battle's palette cycles and the timing of the battle clock are compared automatically with direct calls
and whole sessions (above); the rest of the drawing is checked by playing.
