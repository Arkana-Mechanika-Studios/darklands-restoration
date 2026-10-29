# Faithfulness: where it stands

All checks run against the original DARKLAND.EXE (483.07), executed in an emulator or read from
its bytes, never against another reimplementation.

## The claim for 1.0

Wherever an oracle runs, the rules agree with the original's in the faithful mode (`OriginalBehaviour.Faithful`,
which the oracles and the headless host use); the exceptions are registered, each with its reason (undefined
memory, the host, deliberate fixes, a few things not modelled: see "Divergences and original bugs"). By default the
player's game differs from the faithful mode only in the 6 listed, switchable data-safety fixes below. The aim is no
known divergence, strong evidence, published coverage. The differences found and not yet settled are listed as open
items there. Where it stands:

* every original function the C# cites is compared with it statically (2,687 implemented functions are cited only
  in statements, types or the map, and are not in that comparison), and no lead is open;
* every oracle agrees with the original wherever it runs, or differs for a listed reason;
* whole scripted game sessions, played in the original and in the C# side by side, give the same
  game state at every step, a battle included, and fights started from the same situation agree at
  every combat tick; at present all of them do but one (gen-bhurtk-02, a tester lead listed below), and any that does not is listed as an open lead;
* the branch coverage of the original reached by the oracles is published below with its gaps;
* a divergence found later is fixed with the same tools, and registered with its reason.

It is not a proof that every branch of the original has run: counted over every function the C#
implements, about 82.3% of the branch outcomes have run in a comparison with the original, and the
rest is listed (`branches-missed.csv` for the functions the oracles enter). The best-covered areas
are the rules, the battles, the battlefield generators and the card scripts (82-88%); the least covered are the resident
code (65%), the card engine (69%), the overland map (79%) and the screens (81%) (see below). In the card scripts, the open branches lie behind particular quest
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
* The six fixes now in `DataSafe` (registered as deliberate fixes, `divergences.csv`; the bugs in
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
  * `FixStaleDissolveSlot`: the Dissolve Lock action at a locked door (R014+0x2170 case 7, 0x2256..0x2263) uses up the
    pack entry `consume_slot` (18BF:0FA4, R014+0xB434) is given by the slot byte DS:C7CD[member], which the menu builders
    store when order mode opens (R014+0xDF28) or, for the leader, at each running tick (R014+0xD04E, 0xD402). The
    character sheet runs inside order mode without rebuilding the menu and can give and drop items, so when the member
    arrives at the door the slot can name another stack or one past the last (the last stack is lost, an empty pack's
    count becomes -1), and with the Eater-Water gone the leader's slot is 0xFF, entry 255 (member m + 3's entry 63 for
    members 0 and 1; past the packs for members 2 to 4: D-5471a9, not modelled). Found in batch 20 by the B2 direct calls
    (mine-p300#370, wild-p300#346; registered B-dce22a, B-bf6904, the fix D-a25000; test
    `ADissolveOrderUsesUpTheSlotFoundWhenTheOrderWasGiven`). Fixed: only an Eater-Water is used up (the slot's stack if
    it still is one, else the weakest; none: nothing, and the lock opens as before). The faithful mode keeps the
    original's use of the slot byte, without the count guard.
  * `FixDissolveKeyMember`: the Dissolve key (D) outside order mode, where the leader dissolves the lock, calls
    `consume_slot` (R016+0x0F9A..0x0FBA) with the selected member DS:A6B2 (outside order mode the party member under the
    pointer, R014+0xB785, else -1, R014+0xB7F5) and member 0's slot byte DS:C7CD (absolute, not indexed), with no test: the
    pointer on another member uses up that member's stack at member 0's slot (another item, or the stale entry past his
    last: his last stack lost); nobody selected writes before the packs (D-5471a9, D-c055d0, not modelled; with slot 0xFF
    member 2's entry 63, reproduced). Found by reading in the batch 20 audit (lead B2-L17; registered B-d71b85, the fix
    D-cbae26; test `TheDissolveKeyUsesMemberZerosSlotOfTheSelectedMember`, both modes). Fixed: the leader's own Eater-Water
    is used up, as by his Dissolve Lock order.
* The switches are not exposed in the settings; the player's host sets the profile.

## Coverage of the original functions

See `COVERAGE.md`. Every original function reachable from the game logic is implemented with a
trace, excluded for a reviewed reason (`exclusions.csv`), or listed in `missing-allowlist.txt`;
`FaithfulnessCoverageTests` keeps the list shrinking.

## Structure of the C# against the original (static)

`STRUCTURE.md` / `structure.csv` (`tools/faithfulness/structure.py`): without running anything, every
original function the C# cites in a member's `orig:` comment (all but 2,687 implemented functions, cited only in
statements, types or the map) is compared with the C# that implements it: the calls with their constant
arguments, the random draws, the call order, the constants tested and written, and the number of
branches, read from the EXE bytes and from the C# (Roslyn). It covers every compared function at once,
including paths no oracle case reaches. Its findings are leads to review; a reviewed lead goes to
`structure-reviewed.csv` with the reason.

| | |
|---|---:|
| original functions compared | 5,583 |
| comparisons without a finding | 3,181 of 3,436 |
| findings reviewed (each with its reason) | 297 |
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
| the 8,646 functions the oracles enter | 90.2% (52,647 / 58,347) | 82.4% (36,043 / 43,740) |
| **every function the C# implements (9,281)** | | **82.3% (35,213 / 42,794)** |

The last line is the whole-program figure: the functions no oracle enters count with all their
branch outcomes, decoded from the EXE (`tools/faithfulness/structure_exe.py`). The two decoders
differ slightly on the resident code, hence 35,213 against 36,043 exercised (2,889 more branch outcomes than the published batch 17 (batch 24: +3, the screens +2 and the battles +1, from the replay of 816 runs on the merged tree and the regenerated card corpora; batch 23: +315, the screens +151 and the card scripts +159 from the sessions of round scr1 and the card set-ups of round cards3, the rules +2, battles +2, card engine +1): the card scripts +2,088, of them +831 in batch 21 from the per-card set-ups of round cards2, and the rules +48, screens +42 and card engine +29 (batch 21: +20, +17 and +28), from the card oracle's saved-game start states and controlled battle and prayer results of round cards1 and the set-ups of cards2; battles +274 (batch 21: +70, from round B3's direct calls and the compression library; batch 22: +35, from the live pass's new recordings) and travel +69 (batch 21: +65, from the travel-map sessions, the map-drawing and camp-watch jobs of round R2; batch 22: +3), both below; and 21 in the resident code (batch 22: +16, the battle's party panel rows and notes)). By area:

| area | functions run / implemented | branch outcomes exercised / total | |
|---|---:|---:|---:|
| battlefield generators | 222 / 226 | 7,014 / 8,524 | 82% |
| rules | 335 / 358 | 2,277 / 2,590 | 88% |
| battles (tactical combat) | 288 / 314 | 6,608 / 7,516 | 88% |
| travel (overland map) | 23 / 24 | 773 / 982 | 79% |
| screens (inventory, barter, creation ...) | 197 / 202 | 4,933 / 6,070 | 81% |
| resident code | 138 / 160 | 1,064 / 1,648 | 65% |
| card engine | 35 / 36 | 473 / 686 | 69% |
| card scripts | 6,949 / 7,949 | 11,996 / 14,616 | 82% |
| other | 10 / 12 | 75 / 162 | 46% |

A second figure, kept apart from the first and never counted as exercised: the outcomes exercised
or justified as unreachable in play (a dormant arm, a compiled form, read from the original's code
and listed in `unreachable.csv`; 1,113 outcomes: 366 before batch 23, 37 of them added in batch 14 from the code's dormant markers, each read against the original's branch, and 747 added in batch 23 from 1,367 rows (below)): 84.9% (36,326 / 42,794). Most of what is open is reachable in
play and not yet driven by an oracle (in the resident code: the party panel, the intro).

The whole-game sessions and the battle oracle record the original's coverage where they agree with
the C#; the screens are checked mostly by the structure comparison and the C#'s own tests.

13,397 items of the functions the oracles enter were never exercised: 3,735 are justified in
`unreachable.csv` (dormant branches — tautologies, repeated tests, unused thresholds —, compiled
forms, the C run-time library, drivers, memory and file helpers, code nothing reaches), 9,662 are
open. The battle oracle and the whole-game sessions entered about 400
functions no run had entered, whose unreached branches are now listed too. By area, the open items
lie in the card scripts (about 3,620), the battlefield generators (about 1,810), the screens (about 1,670), the
battles (about 1,210), the resident code (about 380), the overland map (about 330), the card engine (about 320), the
rules (about 180) and the other code (about 155). A sweep
of the generators' seeds and forced maps added about 230 outcomes; the rest of their open branches test
tile contents and random draws, not inputs a sweep sets.

The 1,367 rows added in batch 23 (`local/batch/ujust`, machine-local) went through three readings before they counted: a
Codex pass wrote a justification for each of 1,430 open rows that it could argue from the original's code (callers, data, switch
defaults, dec chains, bytes inside an instruction), an independent Codex pass tried to refute each one (1,367 confirmed, 60
unsure, 3 refuted; only the confirmed rows entered), and an Opus spot check of 44 stratified rows found 43 sound and one weak
(R039+0x1712/190D, whose second predecessor 0x1971 was not named; corrected). A mechanical pass over the disassembly then listed
every predecessor of the payload for the 111 rows that argue a switch default, a dec chain or code inside an instruction:
58 had every entry inside the argued region, 30 argue from the callers' values alone, 12 argue a byte inside an instruction
(no transfer to it in the aligned code, no unresolved indirect jump) and 11 had an entry from outside: five are the loop-back that
re-enters a menu's set-up (R091+0x0A82, R092+0x109A, R123+0x0256; the choice is read again, now stated in the row), four are the
exits to the return blocks of R005+0x0606, one is the jump table of R010+0x02CC, and one was a real gap, R039+0x1712 (fixed for its two rows).
No row was dropped. The rows use the verdict `dormant`; a reason that starts with `callers:` rests on the values every
caller passes (228 rows) and one that starts with `data:` on the game data (44 rows). The 598 rows that argue about the next-card
word rely on one fact (docs/TRACEABILITY.md): DS:E48A is written only by the card records and the card dispatcher (R030 1462:0000). The 60
unsure rows stay open, and so do the three refuted ones (R016+0x0DA6 at 0x0DCC and 0x0E01, reached by a saved game whose header
byte +0xA0 is 0xFF; R043+0x37CA at 0x37D3, reached by a saved game whose cur_hour is -3): leads for a later run with an edited save.

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
  seed) for the cards of the sheet's 40 records. 29,733 runs count; 252 are left out: 42 that differ, 108 the oracle failed on and 102 left out earlier (saves made inside a
  battle and others); on the
  batch 19 tree the replay of all of them against the C# differs in 42 runs only (card 0xFE, below), where it differed in 1,185 rows before the leads were judged. They added 1,292 branch
  outcomes and 1,529 blocks to the count of the functions the oracles enter (card scripts +1,243 in the whole-program count). The differences formed 16 clusters: 14 were judged in batch 19 (below) and the two tester ones closed
  (card 0xFE, the saves made inside a battle). The committed corpus `Oracle/cards-controlled.json.gz` holds 2,004 runs of the controlled
  results, 1,940 agreeing and 64 differing for a listed reason (test `ControlledResultsAgreeWithTheOriginal`). A community-reported lead on the alchemist's trade formula agrees: the
  traveling alchemist (card 0xA6) changes only the packs, and the formula seller (card 0x53) teaches the chosen member, in both.
* Per-card set-ups (round cards2, batch 21; local/batch/cards2): new aimed atoms of `CardOracleStates` (the party seed, the map position,
  the data words of the event created last and its removal, a stack's quality and count, a current attribute only, the collector card's saved card)
  and generators of random combinations of aimed atoms for the cards of the sheet: the ten with the most open outcomes (0E7, 096, 055, 025, 04B, 04F, 051,
  057, 060, 097) on 100 to 150 states each, then the day and night guilds, the cities' cards on the 92 cities and 30 further cards; every state run plain, probed,
  and with each enabled line of the first menu (each run its own coverage unit). They reached 1,660 open items of the functions the oracles enter
  (853 blocks, 807 branch outcomes) in about 10 CPU-hours of the original's side (families that gave under 20 items per CPU-hour were stopped). 50,754 runs
  count (51,235 coverage lines: 481 runs of r1 ran twice); 530 are left out (456 that differ, 74 the oracle failed on). Of the runs counted, 722 differ for a
  listed reason (card 0x96's heir hints and card 0x0D's prisoner past the roster, below); they reach no open item the agreeing runs do not (`cov_gain.py`,
  batch 21 audit), so every figure here comes from agreeing runs. The differences formed five clusters, judged in `cards2o`: card 0x76's text lines 1 and 3
  put an item box and a saint box over handlers 1 and 3, and a choice in a pop-up box calls the box's handler table whichever line it stands on (R073+0x2238:
  `$ChosenOneName` = the menu member), where the C# took the line's own pop-up: real, fixed (those two lines get the item and saint pop-ups; the lead corpus's
  076 run, `LeadRunsAgreeWithTheOriginal`, and two state digests of the golden traces); card 0x96's hints 1 to 3 of the heir menu return a member word the code never
  writes (R081+0x16EA, +0x1728, +0x1766, a stack leftover of the caller: undefined memory, uncertain, registered with a card-divergences entry; 435 runs); card 0x96
  with a sole-member heir ends at the death film in both and the harness compared a saved game of an empty party (tester, 14 runs agree); card 0x0D's menu with 20
  lines came from a start state no play reaches (`party_x` 4 or 100 where the card before it stores 0..3: tester; its 1,226 reachable re-runs agree), its fellow
  prisoner past a four-record roster reads past the file's records in the original (tester, and the registered `AddFromRoster` divergence), and the record byte
  +0x86 differed through a harness state (tester). On the batch 21 tree the replay of all passes differs only in the 456 unreachable-state runs of card 0x0D
  (not counted); the 28 other corpora, the controlled corpus and cards1's own corpora give the same differing runs as batch 20, and the lead corpus gained
  33 runs of cards2 (its report: 59 agree, 14 differ for a listed reason, was 36 and 4).

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

Sound and music (batch 22): the original side logs every call of the sound driver's one command (sound_play, root 047C:078A) and every music request (music_play, R043+0x09AE) in order (queued change 06 of the tester's original side); the C# headless host writes the same requests (DARKLANDS_SOUND_LOG, set by the sessions' lockstep), and a session compares them per segment between its state steps, a fight over the whole run up to the original's last request. A run whose states agree but whose requests differ is reported as a sound lead and its coverage is left out; a recording made before the change reports that the sound was not recorded. The card oracle compares the requests of a card's run in the same way (three corpora were regenerated with the sound lines). Only the requests are compared: the driver, the synthesis and the playing are not.

The oracles record the original's side (`tools/oracle/recording.py`) and replay it against the C# for
day-to-day checks; a recording is used only when the hashes of the EXE, the game files, the arguments,
the saved games and the tester's sources are unchanged. A full live rerun of every fight and session
(`tools/oracle/rerun_all.py <out> --live`) is made before every release and periodically (at least
weekly during active work); the published verdicts and coverage come from such a live run (latest pass: the full live pass of batch 22, with the queued changes 01 to 06 of the tester's original side applied (01 the battle's clock reads, 02 the click's battle pass, 03 the BIOS tick carry, 04 the hot keys' box callbacks, 05 the held map click, 06 the sound log), fresh recordings, 769 fights and sessions (37 fights, 732 whole-game sessions) in 13,304 s on 5 workers: 767 agree, 2 differ (gen-bsafe-36 and gen-bsafe-40, gate battles, from their end state; they agreed on every earlier replay and also differ with the C# of batch 21, so the difference comes with the new original side: leads B22-L01 and L02, a fault of the tester's click holds, fixed: both now agree, see batch 22 below); the 67 travel-map and patch-waiting sessions were run live in the same pass on 6 workers (4 h 19 min for the whole pass): the ten battle sessions that waited for the changes 01, 02 and 04 (gen-bsafe-27, gen-bheld-40, gen-bsafe-50, gen-bhurts-02, gen-blue-02, gen-blue-04, gen-blue-05, gen-bhurta-03, gen-bsafe-33, gen-bsafe-35) now agree in every state, and of the six travel sessions that waited for the change 05 five agree (gen-trav-11, gen-trav0-03, gen-trav0-05, L1, L2; so does L2b); gen-trav-02 stops at s24 (the script's option steps find a menu with no lines, a known script issue); of the 30 travel sessions counted before, 29 agree and gen-travn-02 differs at s8 (a travel click with wait:600; the same script with wait:2000 agrees in every state and in sound; the change of the map pass timing with keyrep is the likely cause, attributed to wt-keyrep: the batch 21 C# agrees with the new recording; a script fault, fixed by the wait:2000 copy, see batch 22 below); the replay of the new recordings (583 s on 4 workers) repeats every one of the 836 verdicts. The sound and music requests of the original (sound_play root 047C:078A, music_play R043+0x09AE) are now compared in every fight and session: 825 of 836 runs agree (31,727 requests, equal in order), 11 differ (leads B22-L05 to L07: the order of a few battle sounds in four gate sessions and the cave fight, and sessions that already differ in their states; after the fixes of batch 22, 831 of 836 agree and the 5 left also differ in their states); no run whose states agree differs in map, travel or event music. Before the new recordings, the replay of batch 21 on the old ones: 768 fights and sessions (37 fights, 731 whole-game sessions), all agreeing: the replay of batch 21, 476 s on 6 workers, every run replayed (none needed a live run; the six new battle save and load sessions of round B3 were also run live once on the merged tree: all agree, 194 s on 3 workers) against the recordings of the last full live pass (batch 16: 590 fights and sessions, all agreeing, 8,135 s on 8 workers, after the tester's original side was changed); the 54 sessions that wait in a battle and press G or Q, whose timing the C# changed in batch 18, were run live once then: all agree (1,170 s on 6 workers; not repeated in batches 19 to 21, which changed no timing of the C# battle's clock; the recordings are the original's); the 28 card corpora give the same differing runs as batch 18 (none at all; four lines of the per-card reports moved in batch 19, cards 0x169 and 0x16A, whose battle-result runs now end at the death film; the reports of batches 20 and 21 are byte-identical to batch 19's, but for the lead corpus's, which gained the 33 runs of cards2), and the controlled-results corpus (2,004 runs: 1,940 agree, 64 differ for a listed reason) and the lead corpus of batch 19 (156 runs, all agree) give no other difference; the 30 battle call job files of batch 17 give the same summaries but for R016+0x0000's keyed jobs (batch 18: 651 moved from agree to not judged by a rule of the tester, below; batch 19: 1 more, a leader outside the party slots; batch 20: 32 rows of R014+0x08FE that differed in the random state or in missiles, or raised an exception, agree or are not judged as a leader outside the party slots; batch 21: unchanged): 23,943 jobs, 14,513 agree, 0 differ, 9,430 not judged; round B2's 17 job files: 5,015 jobs, 3,984 agree, 72 differ (66 of the early run e2, 5 of e11's leave_level, 1 of e12), 0 raise an exception, 959 not judged; round B3's three direct-call sets (R014+0x0F58, +0x1BA0, +0x2324, R018+0x0000, +0x0706, R016+0x0000): 1,079 jobs, 788 agree, 0 differ, the rest not judged, and 19 of 19 calls of the compression library; the world-call jobs of batch 20 (1,108 + 425 + 24 + 27 + 42 + 630 + 1,673 + 2,448 agreeing) are unchanged and round R2's five sets agree: 49 + 65 + 14 + 80 + 29, with 15 not judged). The last open lead before batch 14, a group move at
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
markers in the code, each with its reason and original coordinate: 206 original bugs the game
reproduces (43 with their own `OriginalBehaviour` flag), 327 registered divergences,
869 dormant branches kept as the original has them, 25 uncertain readings (the others were
naming only, or settled by an agreeing oracle or the code, now closed; each of the 25 is in
`UNCERTAIN.md` with the assumption it rests on). Every core-logic divergence has a
review row (`DIVERGENCE_REVIEW.md`: the not-modelled, deliberate-fix and undefined-memory classes in game logic; the host and
presentation entries are not reviewed there) with its action; in batch 16 six stale notes were removed and
the justifications given evidence (a search of the whole EXE for readers, a table, a test), and the
rows of game logic were read again against the original's code (100 rows: 84 right, 8 with a slip in
the text, corrected, 7 wrong, of which 3 are now implemented and 4 are leads below, 1 unsure).

Open items (2026-10-29, after batch 25), listed honestly:
* Batch 25 (batpal, horses; local/batch/batpal, horses, b25): The battle palette and the 'Saving Game' box (D-b797f7, D-56a675's loot page) are settled by batpal: the battle sets colours 0..15 to the driver's sixteen (R002+0x0378..0x037B, so colour 9 is (85,85,255) again) and its 0xFF fill reaches the DAC as white (R002+0x03B4..0x03C6, set_palette root 0410:0397: colours 32..79 where no enemy scheme takes them, 235..242 and 255), the party colours of the slots past the party are the save's (R002+0x045C), and a hot key's box is in the card window's font 1 (root 0000:001E; from the menu font 3): the vis10 battle, loot and map shots now have identical palettes apart from the cycles' phase, and the Saving Game box is identical in a battle and on the map. Leads left from those runs (not on the palette path): in D-b797f7's mines battle the original shows a post at x 265..267 (94 pixels) the C# does not draw; in D-99119d's prison battle the original's background colours are BKGNDPAL row 3, the C#'s row 0; the city gate at night (session batpal-screens) differs in four colours by one DAC step (160, 167, 234, 238). In batpal's loot picture the original's party panel shows a '-' before each member's numbers; ours does not (open lead, not yet located in the original's code). The party-wide mounted flag DS:E3F2 (R030+0x00E6) is written and never read (dormant Z-30f8f1): horses do not change the map's travel time in the original; the cluebook says they do (page 24), which stays a documentation discrepancy and a possible enhanced-mode option (rules.mounted_travel), not implemented.
* Batch 24: gen-bhurtk-02 and gen-bsafe-25 (next bullet), both tester leads. The battle clouds (D-09fbc1: edges sorted in with the fighters by depth, puffs clipped to the cloud's box) remain a registered visual difference: the vis10 session still shows 290 to 2,100 differing pixels per frame (batch 25 run), the palette entries 16..30 of each frame differing only by the cycles' phase; the pixel difference is not separated into the cloud and other causes. Four sessions agree but write no coverage file (gen-bheld-22, gen-bhurtk-05, gen-bkeys-25, gen-blue-02). Most battle direct-call and world-call job files of earlier rounds (judge-battle, battlecalls2/3, world r1, w1 to w5, rules, entry, r2, map drawing) were removed by the old retention rules: their coverage stays counted from their agreeing runs, but they cannot be rerun until they are regenerated or retired. Ctrl-F1 with the pack open (D-03c80e) would lose the item given when member 0's pack is full; it is not modelled (the C# gives nothing, the data-safe outcome), so a faithful reproduction behind a data-safety switch is still to be written. The ten card-text leads of the golden comparison are closed as the tester's (Batch 24 paragraph). The 60 unsure and 3 refuted unreachable-justification rows of batch 23 stay open. The linux-x64 build and CI (p1linux) and the launcher's audio and HD pack rows (p2packs) have not been tried by the user.
* Batch 23: the 60 unsure and 3 refuted unreachable-justification rows (above); no lead is open from scr1, discordq or cards3.
* Batch 22 leads (local/batch/b22/leads.tsv): gen-bsafe-36 and gen-bsafe-40 differ from their end state on the new recording (B22-L01, L02); gen-travn-02 differs at s8 with wait:600 and agrees with wait:2000 (B22-L03, probably the script); the order of a few sounds inside a battle tick differs from the original in the gate sessions gen-bkeys-05, gen-bkeys-15, gen-bsafe-36, gen-bsafe-40 and the cave fight (B22-L05, L06; no state differs in the first two or in the fight); the event music a player heard in DOSBox is not among the recorded sessions (B22-L08).
* Leads of batch 16, settled (local/batch/c2stale): the barter's buy on a lit row past the list's end and the cards 0x5F
  (sneaking) and 0x73 (before the city walls, also the cross-check's finding 21), which keep the previous card's texts, are
  reproduced as original bugs with tests and card-oracle runs (D-dd4c2e, D-ecc6f5, D-d4f515 done); the loot screen's G past
  the pile's end reads a DOS block the battle never clears (D-b00418, undefined memory, not reproducible) and Ctrl-F1 in the
  open pack depends on a Throw list of 19 lines in an earlier battle and the menu bar's geometry (D-03c80e, not modelled
  with evidence).
* Battle input sessions of round B1 (c3battle, batch 24; local/batch/c3battle): of the 24 early B1 sessions 23 now agree on the batch 22 original side and are
  registered (with c3-space-pause), after general compare rules of the tester (a sheet click after pointer moves, the F6 hot key, a closing click, a click read in the bar's
  list) and a tester hook for the scheduler's rise (gen-bluer-01); gen-bkeys-44's four hand corrections were host frame accounting and are gone.
  Still open: gen-bhurtk-02 (registered, differs at clock 751: the right button let go after crossing two menus; the lockstep rule RUP_FRAMES counts the box frames as
  no battle passes, which is wrong for this paused battle, but the pause-aware rule breaks four others, gen-bhurtk-04, gen-bkeys-03, -40 and -44, so the old rule stays: a
  TESTER lead, the distinction is not "paused"), and gen-bsafe-25 (not registered: a paused pass whose order computation took about 250 timer callbacks in the emulator, machine
  speed; an original-side log of DS:07D1 per paused pass would close it). The held cursor keys' count DS:07D6 is now one count for the program (CursorKeyState.HeldCallbacks,
  test BattleUiTests.AKeyHeldIntoABattleTakesItsSpeedFromTheProgramsCount); Space in a running battle is not a difference (session c3-space-pause).
* Round B2 leads L05 to L08 are triaged and settled in batch 20 (below); no job of them differs now. The 72 B2 jobs that still differ are the tester's: the early
  run e2 (66 jobs of R014+0x2170 whose original side predates the tick wait fix: the rewrite's timer-callback count is the original's plus exactly the 150 or 60 ticks
  of the message wait, and the clock and the times derived from it follow; replaced by the later runs of the same function, its coverage is not counted) and e11's and
  e12's leave_level jobs (5 and 1, whose fuzzed state puts an enemy in animation slot 1..3). The two leads the batch's audit found by reading are fixed (a2fix, below): B2-L16 (the
  attackers' turn of combat_draw at three draw sites) and B2-L17 (the Dissolve key outside order mode, behind `FixDissolveKeyMember`); the two draw sites
  it left unchecked were read in round B3 and need no change (below). Round B3's other doubts, not decided: the wave record of the reinforcement function
  (R014+0x2324) lives in a segment the captured state does not carry, so 72 of its 120 jobs are not judged; the loaders and drawing calls with DOS memory
  ownership (R025+0x09A6, R020+0x0000, R021+0x03FE, R014+0xE3C0) and the restore error arms of R003+0x0CE8 have no counterpart to compare; the held cursor keys' call
  count DS:07D6 is still per battle screen in the C#, where the original's is a timer global (only the speed of a key already held when a battle starts could differ); two consecutive bwait steps, or a hot key refused in a running battle, put the C#'s next key one or two passes late (the tester's; the sessions avoid both).
  The per-battle state carried from one battle to the next (a lead from the community's bug list) needs a run of two battles in the original; no tool for it
  exists yet.
* Round cards2 leads (batch 21, above): the 456 runs of card 0x0D from unreachable start states stay in the corpora as differing runs and count for nothing. The general
  risk found with card 0x76 (`CardMenu.PopupHint` takes the line's own pop-up handler, the original the box's table) was scanned in batch 24 by c1menus (a box probe over 159
  cards and the Enter mode over 146 cards): the fixes are in the Batch 24 paragraph below. The options-block lead (the shared block DS:EE53 kept up to date by the other cards, D-4d048d) was not
  touched: card 0x0D's menu difference was not stale option flags. Left open by the sheet: the negative battle result arms of R081+0x1F66, the city-content controls, the
  cached hireling count, event replace for saved games.
* Round R2 (batch 21): five leads, all the tester's, none a difference of the C# (below). Six travel-map sessions (L1, L2, gen-trav-02, gen-trav-11, gen-trav0-03,
  gen-trav0-05) agreed only with the queued change 05 of the tester's original side (`local/batch/orig-side-queue`: the original side counts the frames a scripted
  click is held on the map), which went in with the batch 22 live pass: all but gen-trav-02 (a script issue at s24) agree now and count in the coverage. Not done: the root text layout
  (0265:0134, 0265:0654: 48 outcomes; the original draws through the graphics driver, which the emulator does not load, and the C# lays text out by the font
  file's widths, a registered divergence), the clip-and-copy 0000:14F6 (no counterpart), the battle panel 0000:18FA and 1A9C, the menus and rectangles, the alchemy
  expiry R029+0x3FB2 (a 100-tick wait), the far-pointer date R029+0x5A2C, and R030+0x0C44's scroll and boundary groups (the marker at the view edges is not aimed).
* Round cards1 leads (14 judged in batch 19, below): what is left of them is registered, not reproduced: the new game's slot 4 holds the figure
  "0" of SAVES\DEFAULT in the original, which a C# new game does not model for a slot nobody held (D-dff113); a loaded saved game with two slots
  naming one record is read as two copies, so a later save would give the second a roster entry of its own (not seen in any oracle run, not
  changed); the stack or session leftovers D-9654eb (a text variable past msg_file), D-9903b8 (the abbot's score in the verdict of card 0x8D),
  the 'time 16' of card 0x41 and the hint value of card 0x11 depend on menu and text code the cards do not model. The ResumeBattle clause
  of the save writer (D-f631fd, below) was checked in the batch's audit: kept.
* Evidence round (batch 18, local/batch/evid1 and evid1o; batch 24's c5uncertain settled 11 of the 36, leaving 25): 36 uncertain readings remained (one, U-b280c3, the abbot's score in the verdict of card 0x8D, became the registered divergence D-9903b8 in batch 19), and batch 21 added one (the heir menu's hints of card 0x96, above), each in `UNCERTAIN.md`; those needing a session
  or a captured state are the stale-local cards (U-aff1d9, U-ecbac4, U-4c93ee, U-c18edd, U-4ffac2), the tester's interrupt table
  against the reference machine's (U-9860ee, U-6d067b, U-16b0a7, U-69b939), generator and battle states (U-2a08c3, U-8f4107, U-5bdb14,
  U-8df8a9, U-91948c, U-cb31b9, U-5d20fe) and screens and timing for the user's checks. Further: the other cards' stores of the shared options
  block DS:EE53 are not modelled (356 write sites; D-4d048d); whether an event created by a prayer on the creation sheet survives into the begun game;
  the creation session chargen-created still differs in the new name's stale tail bytes and the party seed; the copy-protection direct call
  returns after the five draws in the tester (not investigated); of the community's bug list, the inn saving, the neon palettes, the warehouse name,
  the merchant selection, the two menus, the strikes and the stale step after a re-path are not proven.
* Registered in batch 19 and re-derived from the EXE in its audit: door_set R023+0x04AC leaves its direction local unset for a door record with none of
  the four side flags (then the stale stack word picks the side, or no side when it is not 0..3) and the C# takes north (D-e92f84); no play state reaches
  it: its three callers take only records whose first word is above 0, and every flagless record of the prepared maps and the citadel's levels has 0
  there, while the castle generator flags every record. The wall search R006+0x1D48 reads its two 40-cell local arrays up to two cells outside, which the
  C# reads as 0 (D-0bf8d6; it needs a room or strip bound within two cells of the map's edge; 300,000 castle builds of the corpus's scenarios with other
  seeds never read outside, not proven by a bound). They were inline notes in comments, now markers with review rows; the text-variable guards of
  TextVariables (the third inline note of audit 18) were not a divergence of their own and the comment is reworded; its two other guards that give "" are registered
  as undefined-memory divergences (a city-name index past the record's 16 names, D-1cbe8f; a place past the location table, D-6c5593), both checked against the EXE
  and unreachable: DS:ED52 ($CityLocation) has no writer and stays 0 from the BSS clear, and every writer of cur_loc stores -1..413. The save writer's
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
* Registered as not modelled (14; most are justified as unreachable or without effect): the enemy item lists' counting
  (D-3859aa), the prayer without its attached presentation (D-e60a70), the
  deleted member's shield picture (D-34f49d), two omitted pop-up flags (D-920e47, D-40b596), the omitted previous-card global
  (D-58560f), the other cards' stores of the shared options block (D-4d048d), the fortress room bookkeeping (D-ca7108), a card number outside the table (D-5dc345), the rejection of an
  oversized message (D-3854a2), the copy-protection question (D-9c9eab) and the joystick with the overland loop's reload
  (D-959078); and, from the two Dissolve leads, consume_slot's write outside the packs (past them for the Dissolve Lock order's slot 0xFF of members 2 to 4, before them for the Dissolve key with nobody selected, D-5471a9) and member -1's count byte DS:9C53 (D-c055d0).

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

Batch 25 (2026-10-29): two branches integrated on master a65c6c4 (batpal, horses; local/batch/b25). batpal: the battle sets the display's colours 0..15 to the driver's start-up sixteen before its own palette (R002+0x0378..0x037B, root 047C:0584), so colour 9 is (85,85,255) as in the original and the loot page keeps it (D-56a675 closed); the battle's 0xFF fill of the palette buffer reaches the DAC as white (six bits: R002+0x03B4..0x03C6, root 0410:0397), so colours 32..79 where no enemy scheme writes, 235..242 and 255 are white; the colours of the party slots past the party are the save's records (R002+0x045C, colour 112); and a hot key's box (Alt-S Saving Game, Alt-Q, Alt-P and the Alt-D/F/M/C messages) is drawn in the card window's font 1, one row shorter than the menu's font 3 (root 0000:001E; D-b797f7 closed). All three are fixes in every preset. New leads (batpal's runs, repeated after the merge): the city gate at night differs by one DAC step in colours 160, 167, 234, 238; the prison battle's background colours are BKGNDPAL row 3 in the original and row 0 in the C#; the mines battle's post at x 265..267 is not drawn; the original's party panel in the loot picture shows a '-' before each member's numbers. horses: DS:E3F2 (the whole party mounted flag, R030+0x00E6, party query 5) has no reader in the whole program (static scans of the root and the 137 sections, IDA, runtime watches over ten sessions) and is not saved; horses do not change the map travel time: one DORMANT note on OverlandTravel.Begin (dormant Z-30f8f1), no behaviour change. Checks on the merged tree: build 0 warnings 0 errors; 12 of 12 test projects pass (1,692 tests, 0 skipped); replay 815 of 816 agree (2 workers, 2,040 s; gen-bhurtk-02 at clock 751 differs, the open tester lead); the 17 card corpora (controlled and lead corpora included) replay with 0 differing runs; the ten vis10 sessions and batpal's two show the pictures batpal reported (battle, loot and map palettes identical apart from the cycles' phase and the pointer; the four leads above unchanged); registers 327 divergences, 206 original bugs, 869 dormant (+1: Z-30f8f1), 25 uncertain; structure 0 open; names check 0 hits. No new session was added and the C# change touches only drawing, so coverage is unchanged (batch 24's figures).

Batch 24 (2026-10-29): eleven branches integrated on master 931a916 (c2stale, c4chargen, vis10, c3battle, c1menus, c5uncertain, vishair, cardmem, p1linux, p2packs, b24text) with the fix-ups of local/batch/b24, and a twelfth, linuxfix, after the checks below (host and launcher only: the Linux launcher shows and uses a game folder given at the start, takes typing and pasting and opens a folder dialog; sound diagnostics, --sound-test; its 51-session replay subset agrees). c2stale: the barter's buy on a row past the list reads the leftover listing (B-abb9be), cards 0x5F and 0x73 keep the previous card's texts (B-f40b26, B-37826f); the loot screen's G past the pile (D-b00418, undefined memory) and Ctrl-F1 with the pack open (D-03c80e) stay unmodelled. c1menus: a choice in a card's pop-up box is rated by the box's table whatever line it belongs to, the box layout stays the menu's after a choice there, and Enter in a box reads the entry's menu_slot (box handlers fixed in 13 cards, CardMenuBoxTests; D-21ff84 for the bare-retf handlers); the shared options block was checked (1,600 runs over 64 cards: nothing to fix). c4chargen: the party seed of a created party is the BIOS tick word (R037+0x9FD9) and the character record's first word is DS:907E (session chargen-created now agrees). vishair: the creation screen's figure colours start at palette entry 235, not 237 (R037+0x0B9C), and only the touched row is redrawn (182 sessions agree). c3battle: the 24 early B1 sessions (23 agree, above), DS:07D6 as a program-wide count. c5uncertain: 36 uncertain readings to 25 (two real fixes on the saint prayer page, U-a84a42 and U-9ecd08, one new registered divergence D-c6957e, the rest settled or documented), the original's leftover drawing reproduced in the Classic look (D-751050, D-5de66d, and the cards 0x5F / 0x73 of D-ecc6f5 / D-d4f515), with the new switch GameOptions.CleanDrawing keeping the clean drawing for the Fixed and Classic + comfort presets (D-7c23d5 and the other CleanDrawing divergences; no preset and no setting sets the switch yet, so the player's game draws these as the original), D-b0c9a6 (the stock shield until H) and D-b797f7 (the 'Saving Game' box in colour 9 in a battle) reproduced in every mode, and, after a load, the card screen restores its text window from the load screen's picture page (D-d4f515). cardmem: the card oracle replaces a worker after 8 cards (peak about 350 MB). p1linux: the linux-x64 publish profile, game-folder search and CI (never run). p2packs: the launcher's audio and HD pack rows, docs/PACKS.md and an example pack in the build output. b24text: the ten card-text leads (cards 06C 08A 091 0F7 123 127 132 139 144 14D) are the oracle's, not the game's: R039+0x1CFE returns -1 when no sabbat is planned, the text then reads locations[-1], which is the machine's interrupt vectors, filled with F800:0030 by the tester (hence "North of Flensburg"); the C# prints an empty place (D-09b57b, undefined memory), 06C's hint 9 reads a stack leftover (D-994942), and StartSabbat / PlanSabbat write through events[-1] when the log is full (D-54d606, D-185ea3; the C# writes nothing); no behaviour changed. The cards 093, 113 and 137 "texts from another set" under the box probe were the tester's corpus merge (re-indexed: 532 agree, 45 justified, 5 differ, the known four cards). Checks on the merged tree: build 0 warnings 0 errors; 12 of 12 test projects pass (1,666 tests, none skipped, DARKLANDS_DIR set); replay 815 of 816 agree (37 fights and 779 sessions, 1,217 s on 3 workers, every original side replayed), the one difference gen-bhurtk-02 (above); the ten vis10 sessions run on the merged tree (the idle shots of the card screens D-ecc6f5 and D-d4f515, also after a load, are identical; the others differ by the pointer, by the original's slower emulated stretch, or by the open leads above); the 17 card check corpora (4 of them regenerated on the merged tree; others of the earlier 30 were dropped for their cost, about 3 hours of original time, their coverage is already counted) replay with 0 differing runs; the committed golden cards-oracle.json.gz was regenerated (365 cards, 14,600 runs, 69 minutes of original time on 2 workers): 22 cards differ from the old file, in the sabbat location text and member names (the cards above), in the addresses of the end text after the relocation of the code segments, and in the stray-read report, none in the control flow, the draws or the saved games, and the traces gained 602 redraw lines (cards3o's); the C# agrees with it (CardOracleTests, runs that carry the stray flag counted as justified) and the C# golden traces did not change. Battle direct-call jobs: the 12 job files of round 1 still on disk give 6,453 agree, 0 differ and 7,217 not judged (the later sets' job files were removed by the retention rules; c3battle's run of them on its tree gave batch 22's figures, 19,285 agree and the same 72 differ); world-call jobs: the travel set 1,673 agree, 43 not judged, 16 differ (as in batch 22), the shop reputation corpus 2 of 2 (the other sets' job files are gone). Coverage 82.3% (35,213 of 42,794; 84.9% with the justified, 36,326); the card records 91.3% of blocks and 82.7% of outcomes (CARDS.md regenerated); registers 327 divergences, 206 original bugs, 868 dormant, 25 uncertain; structure 5,583 functions, 0 open.

Batch 23 (2026-10-28), road to 90%: four branches and one round integrated on 0.9.5 (master 2cc5219). scr1: 19 scripted whole-game sessions of the screens' open input paths (F6 screen, prayer page, load and title screens, party menu, card keys, character generator, camp; tools/oracle/gen_scr1.py); scr1o: its two leads were the tester's (the original refuses Begin with an empty roster as the C# does, R037+0x087A; the camp's rest runs until the next key, 53 hours to a wild-boar encounter, so the script waits longer), with sessions scr1-camp-2 and scr1-party-4; all agree live. discordq: five community claims checked on the original and agreed by the C# (the freelance rule by city record ordinal with the party seed as a signed 16-bit sum, gossip bands, retirees listed only where left, the character generator's roster from SAVES\DEFAULT), the card oracle atom lf (location flags), the party-seed save and load test and the session discordq-retire (run live once, 562 s). cards3: 8,527 agreeing units over 134 cards, +139 outcomes. cards3o: one real card fix (card 09C: the sabbat block of the witches of the blight hides line 3, grovel, not line 4, R083+0x01CD / DS:EE7C; WitchesOfTheBlightTests) and one tester fix (the oracle's stubs of the barter, camp and item-store screens now run their closing card_load through thunk 09C0:1EA5, so the picture after a shop compares as the original redraws it; the 12 + 2 lead runs enter cards-leads.json.gz and, agreeing, the coverage). Unreachable justifications: 1,367 rows (above). Checks on the merged tree: build 0 warnings 0 errors; 12 of 12 test projects pass (1,610 tests); replay 791 of 791 agree (37 fights and 754 sessions, 1,259 s with 3 workers, all replayed; discordq-retire ran live once, 562 s, in the first full run before cards3o, which also agreed 791 of 791 in 2,704 s with 2 workers), sound and music 791 of 791 agree; the 30 card corpora, controlled and lead corpora replay with 0 differing runs, reports identical to batch 22 except the leads (+14 runs); the committed golden cards-oracle.json.gz still agrees and was not regenerated. Coverage 82.3% (35,210 of 42,794; 84.9% with the justified, 36,323): +315 outcomes (screens +151, card scripts +159, rules +2, battles +2, card engine +1); registers unchanged (320 divergences, 203 original bugs, 870 dormant, 36 uncertain); structure 0 open.

Batch 22 (2026-10-28), release 0.9.5: five branches integrated on batch 21 (keyrep, pointer, panelfont, doormsg, audio) and the full live pass. keyrep: the held arrow on the map (DOSBox's typematic of 500 ms and then 33 ms, the pass ending at the next BIOS tick, R030+0x3096). pointer: no mouse pointer while a message box waits (root 047C:12C3 / 047C:1293 around root 0000:24BC). panelfont and doormsg: the battle's party panel draws in font slot 1 (names, values, the order letter, values in the name's colour, a plus above the maximum; root 0000:1716, 0000:1A9C) and the row notes (near a door, near the stairs, near a chest, near an exit, Exited: DS:01A8 to DS:01B0) in place of the bars; both change the same rows, and the merged panel keeps both (the note is laid out in the same font slot 1 as the name and the values; a rendered frame is in local/batch/b22). doormsg also fixes the double advance_hours(0) after a battle (root 03A1:0438) and the saved screen positions after an alarm or a pit (R014+0x105B). audio: the card oracle, the whole-game sessions and the fights compare the sound and music requests. Three fights (mine, castle, city13) died in the stopped pass with no report: workers starting from one snapshot stored it at the same moment and Windows refused the replace; the tester's store now retries. Checks on the merged tree: 12 of 12 test projects pass (1,606 tests; the golden card traces did not change), replay 769 of 769 on the old recordings, the 28 card corpora, the controlled and lead corpora, the card passes of cards1 and cards2, the battle call job sets of batches 17 to 21, the world-call jobs, the shop reputation corpus and the map drawing all identical to batch 21; of the 30 counted travel sessions 27 agree on the old recordings. Then the live pass above. Coverage 81.5% (34,895 of 42,794; 82.4% with the justified, 35,261), from the agreeing runs of the live pass (36 fights, 728 sessions and 45 travel and battle sessions of the second stage; the runs that differ in a state or in sound are left out), the other sources unchanged: +54 outcomes (battles +35, resident code +16, travel +3). Registers: 320 divergences (out: the panel redraw after damage, now made, and the PC/AT typematic; in: DOSBox's typematic and, after the lead fixes, record -1's undefined memory in the melee frame, D-74b517), 203 original bugs, 870 dormant, 36 uncertain (the map pass's clock() tick, now read from the BIOS tick); structure 0 open. The leads of the live pass, then fixed: gen-bsafe-36 and gen-bsafe-40 (B22-L01, L02) were the tester's, which counted a later click's hold twice once the new click and move passes were logged; gen-travn-02 (L03) was the script's, as the original side never runs a wait: step and the keyrep C# walks one step per BIOS tick as a PC does (292 steps to card 103 on both sides), so its copy with wait:2000 now stands in its place; the battle sound order of four gate sessions (L05) was the tester's, which gave a bwait after a shift, ctrl, state or wait step its neighbour's passes where the original side skips it (226 of 226 sessions now match the recorded counts); the cave fight's extra sound (L06) was the C#'s, which played the miss sound 0x11 for a hit with no target (R014+0x1A59..0x1A7E). After these fixes: 12 of 12 test projects, replay 769 of 769 agree, sound 831 of 836 (the 5 left also differ in their states); the coverage above is the live pass's, without these three sessions.

Batch 21 (2026-10-27), phase 2, five branches: a2fix (the two leads of the batch 20 audit, below), b3 and b3o (round B3, battle save and load), cards2 and cards2o
(round cards2, the per-card set-ups, above), r2 and r2o (round R2, rules, travel and the resident code). Coverage rose to 81.4% (34,841 of 42,794; 82.3% with the justified,
35,207), the functions the oracles enter to 89.8% of the blocks and 81.8% of the outcomes (+1,033 outcomes and +1,061 blocks); over every function +1,031 outcomes, of them 831 in the card scripts. Round B3: 105 new
open items (R003 77, R017 15, R016 11, R018 2) from direct calls (the compression library, 19 of 19 agree; the R016 key handler with the BIOS shift flags and the held-direction
bytes, 123 and 256 judged jobs agree; the old adapters of R014+0x0F58, +0x1BA0, +0x2324 and R018 with new states, 409 agree; none differs) and six sessions (b3-save-fight,
-save-fight2, -save-fight3, -probe1..3: a battle saved with Alt-S, loaded again with Alt-L, saved again, Alt-S refused in real combat), registered in `rerun_all.py`
and run live once on the merged tree: all agree. Their leads, judged in b3o: B3-L01 and L03 were the tester's (the dialog's passes were given to the C# twice); B3-L04 is
the original's own refusal (R014+0x036B..0x0388, the C# does the same); B3-L02 was real, minor, and is fixed: the battle file's bytes +0x156A..0x156F
(DS:D728, D73B, D7FE, D947 and the word D945, the cursor keys' state, written by R003+0x00DC and read back by R003+0x0CE8) were never written or read by the C#, so a
saved file kept the bytes of the last load, and a stale D73B = 1 would be a phantom keyboard click on the first pass after loading it in the original: now
`CursorKeyState` (program globals, shared by the program's battles and kept across loads; D945 enters the held keys' speed R014+0x36C4 and is cleared at the let-go
R014+0x38AC; `CursorKeyStateTests`, 3 tests; the save-fight sessions agree end to end). Not a data-safety case: no original bug, the written file only had stale bytes. The two draw
sites a2fix left unchecked: the dragon's victory draw does call the turn (R014+0x99F6) but nobody turns, since R014+0x0C6C..0x0CB4 cleared the status and the orders of every
record first, so the C# without a frame there is equivalent; the menu loop R015+0x00DC belongs to the developers' memory-statistics menu, which nothing in the EXE calls
(excluded in `coverage.csv`). The tester gained the click's battle pass in the log (change 02 of the original-side queue, prepared and tested privately, not applied).
Round cards2: the per-card set-ups (above) with one real fix (card 0x76) and one new uncertain reading (card 0x96's hints). Round R2: +207 open items (R029 29, R030 114, R031 10, R043 49,
R108 5) from the absent-member rule R029+0x2474 (49 jobs agree), the party drawing and the map's cell buffer (3,888 jobs agree, 232 not judged past the row buffer), the camp watch aimed over
terrain and settlement signatures (65 jobs), the full event table and hours (80), the day-of-year helpers R029+0x5848 and +0x5892 (14), the expiry handlers (29 agree, 15 not judged) and 30 travel-map sessions (`gen_screen_sessions.py trav`:
map keys, clicks, held keys, Shift and Ctrl F-keys, the seasons; replayed from R2's recordings on the merged tree, all agree). The shop price's reputation term was read from the asm (buy
R032+0x1A94..0x1AD1, sell +0x1C86..0x1CC3, the clamp R029+0x4F76: a 16-bit `imul` by 50, `idiv` by 100 truncating) and compared on 820 original-byte cases over reputations from -32768 to 32767
(both 16-bit wrap sides, -655 and -656, 654 to 656, +-99 and 100): all agree with `Trading.BuyPrice` and `SellPrice` (`TradingRepOracleTests`; one altered result makes it fail). R2's five leads
(a click on the map's bottom edge, states taken inside an encounter's battle, three sessions' waits) were the tester's: the original keeps aiming while the button is held (the C#'s hold was one map
pass short; change 05 of the original-side queue counts the frames, not applied; the host's `OverlandScreen.PassHook` and `DarklandsGame.Fighting` are opt-in tester hooks and change nothing in
play), a state taken in a battle compares the card only and writes no coverage, and the generated clicks wait 2,000 frames. Replay of everything on the merged tree: 768 of 768 agree; the card
corpora, the world calls and the battle call jobs as above. The golden card traces moved in two state digests (card 0x76) besides the earlier ones of a2fix.

Batch 20 (2026-10-27), two branches (b2l58, the first pass of the B2 leads L05 to L08, and b2l58o, the fixes of the two real ones). L06 (a leader byte 0xFF)
and the loot, stairs and empty-member parts of L07 were the tester's (every writer of DS:907B stores a party slot; the harness now skips such jobs). Two were real
and are fixed in the C#, each with a unit test: the battle end R014+0x08FE, when nobody able is left on the enemies' floor and somebody able is on another,
ends with the view centred on the first able member or the new leader and one frame drawn (floor_view_draw 09C0:1C2F, then combat_draw 14F7:0008, R014+0x0AAD..0x0ABE):
the renderer's random draws, the attackers turning toward their targets and the arrived missiles removed; the C# made that draw only at the end of the next tick, one
draw late, so every later random number differed (BattleEnd.IsOver now calls `RedrawAround`; `SwitchingToThePartysOtherFloorDrawsAFrameBeforeTheTestReturns`; leads L05 and L08;
jobs e9, e10 and 32 rows of the old job files now agree). And the Dissolve Lock action uses the pack entry the slot byte DS:C7CD names whatever the pack holds, a
data-damaging original bug: reproduced in the faithful mode and fixed in the player's game behind `FixStaleDissolveSlot` (B-dce22a, B-bf6904, D-a25000; the fifth `DataSafe` switch,
above; e3 now agrees throughout). The golden card traces were regenerated: only the state digests moved, as the state dump gained the new flag. Replay of everything on the merged
tree: 762 of 762 agree (472 s), the card corpora (30 jobs, byte-identical reports), cards1's own corpora and the world calls unchanged; coverage +3 outcomes and +1 block (the battle end's
new draw path): 33,810 outcomes, 79.0% (79.9% with the justified).

a2fix (2026-10-27), the two leads of the batch 20 audit. B2-L16: combat_draw (R014+0x6818) calls the attackers' turn R014+0x99F6 at R014+0x6926 whoever
calls it, and three C# draw sites left it out: after a loot pile (R014+0xE3B3), after a chest (R023+0x1532, whose floor_view_draw also centres the view on the
active member DS:17B0, R023+0x1525, where the C# kept the centre) and when an in-battle save resumes (R014+0x0307); all three now turn the attackers (test
`TheDrawsAfterALootPileAndAChestTurnTheAttackers`). The seven-headed dragon's victory draw (R014+0x0CB6..0x0CC7) and the draw after the menu loop at
R015+0x00DC were left to check; round B3 read both (batch 21, below): neither needs a change. B2-L17: the Dissolve key outside order mode reproduced in the faithful mode and fixed
in the player's game behind `FixDissolveKeyMember` (the sixth `DataSafe` switch, above). Battle jobs: the 30 judge-battle files and b2's 17 give results
byte-identical to batch 20; the replay of every fight and session (rerun_all default mode, 2 workers) 762 of 762 agree (none of them is known to reach the changed sites, so they show no regression rather than prove the fixes). The golden card traces moved in their state digests only (the new flag in the state dump).

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
coverage moved by 22 outcomes and 34 blocks, and one function entered (the 438 agreeing card runs a key-format slip had left out of the first
selection of this batch are counted after the audit's correction of the selection script, which now stops on a key that matches nothing; the now agreeing
jobs of e14 and e17 add little): 33,807 outcomes, 79.0% (79.9% with the justified).

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
those files agrees (batch 20: 14,513 of 23,943 agree, 0 differ, the rest not judged).

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
