# Faithfulness: where it stands

All checks run against the original DARKLAND.EXE (483.07), executed in an emulator or read from
its bytes, never against another reimplementation.

## The claim for 1.0

No known divergence, strong evidence, published coverage:

* every original function the C# cites is compared with it statically, and no lead is open;
* every oracle agrees with the original wherever it runs, or differs for a listed reason;
* whole scripted game sessions, played in the original and in the C# side by side, give the same
  game state at every step, a battle included;
* fights started in the original and in the C# from the same situation agree at every combat tick;
* the branch coverage of the original reached by the oracles is published below with its gaps;
* a divergence found later is fixed with the same tools, and registered with its reason.

It is not a proof that every branch of the original has run: counted over every function the C#
implements, about 52% of the branch outcomes have run in a comparison with the original, and the
rest is listed (`branches-missed.csv` for the functions the oracles enter). The best-covered areas
are the battlefield generators and the rules; the least covered are the screens, and in battle the
spells, potions and the player's orders (see below). In the card scripts, the open branches lie behind particular quest
stages, items and location facts; reaching them with start states made for each card continues
after 1.0.

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
| original functions compared | 5,562 |
| comparisons without a finding | 3,159 of 3,445 |
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
| the 5,208 functions the oracles enter | 71.6% (34,443 / 48,082) | 61.8% (23,145 / 37,434) |
| **every function the C# implements (9,281)** | | **52.4% (22,408 / 42,778)** |

The last line is the whole-program figure: the functions no oracle enters count with all their
branch outcomes, decoded from the EXE (`tools/faithfulness/structure_exe.py`). The two decoders
differ slightly on the resident code, hence 22,408 against 23,145 exercised. By area:

| area | functions run / implemented | branch outcomes exercised / total | |
|---|---:|---:|---:|
| battlefield generators | 222 / 226 | 6,783 / 8,524 | 80% |
| rules | 309 / 358 | 1,723 / 2,584 | 67% |
| travel (overland map) | 22 / 24 | 495 / 982 | 50% |
| card engine | 31 / 36 | 336 / 686 | 49% |
| card scripts | 4,025 / 7,949 | 7,116 / 14,614 | 49% |
| battles (tactical combat) | 195 / 314 | 3,593 / 7,516 | 48% |
| resident code | 113 / 160 | 695 / 1,640 | 42% |
| screens (inventory, barter, creation ...) | 85 / 202 | 1,604 / 6,070 | 26% |

The whole-game sessions and the battle oracle record the original's coverage where they agree with
the C#; the screens are checked mostly by the structure comparison and the C#'s own tests.

27,928 items of the functions the oracles enter were never exercised: 1,207 are justified in
`unreachable.csv` (dormant branches — tautologies, repeated tests, unused thresholds —, compiled
forms, the C run-time library, drivers, memory and file helpers, the copy protection, code nothing
reaches), 26,721 are open. The battle oracle and the whole-game sessions entered about 380
functions no run had entered, whose unreached branches are now listed too. By area, the open items
lie in the card scripts (about 12,100), the battles (about 4,400), the screens (about 3,900), the
battlefield generators (about 3,000), the rules (about 900) and the overland map (about 800).

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
the attack rates and the random generator's state at every combat tick. 25 fights so far, over
city battlefields, wildernesses, mines, castles, a fortress, tombs and a cave:

* 15 agree at every compared tick for their whole run (gate, street, wild, mine and castle, and ten
  more: four city battlefields, two wildernesses, two castles, a fortress and a mine);
* 10 show a first difference, not yet investigated (cave60, city13, city2b, castle04, mine10,
  mine80, tombs5f, wild40, wild50, wild6f): some from the first tick (a combatant's range or
  fighting value, a facing, a position), others late in the fight (an attribute, a destination).
  As with the first five, part of these may be the tester's own; they are listed as open leads,
  and their coverage is not counted.

The fights brought the battle code run against the original from 32 to 195 of 314 functions. The
first five found one divergence in the C# (the first frame also turns whoever just got an action
toward its target, so enemies face their targets from the start); the other differences they showed
were the tester's own (fields the original has not set yet, the pause, the set-up and the clock's
jumps during long passes), passed to the C# by opt-in hooks that leave normal play unchanged. Not yet
compared: spells, potions and the player's orders.

## Saint oracle

4,352 runs covering all 136 saints: all agree.

## Other oracles

About 12,000 further cases: money, calendar and random numbers, events, party and location
rules, prices and stock, character creation, names, the saints' help, attack and damage, enemy
setup, collisions and line of sight, path finding, every battlefield generator, spell effects,
memory-layout effects (writes outside arrays the original performs).

## Divergences and original bugs

`DIVERGENCES.md`, `ORIGINAL_BUGS.md`, `UNCERTAIN.md`, `DIVERGENCE_REVIEW.md`: generated from the
markers in the code, each with its reason and original coordinate: 156 original bugs the game
reproduces (each can be fixed by its own `OriginalBehaviour` flag), 306 registered divergences,
849 dormant branches kept as the original has them, 304 uncertain readings.

## Presentation

The graphics driver's behaviour (start-up colours, fonts, XOR option lines, the fade,
see-through restores) was reconstructed from the original driver; the Quickstart sequence
matches the original DOSBox-X captures pixel for pixel (`tools/visuals/compare.py`). Drawing and
timing are otherwise checked by playing.
