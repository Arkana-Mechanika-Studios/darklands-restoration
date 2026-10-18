# Faithfulness: where it stands

All checks run against the original DARKLAND.EXE (483.07), executed in an emulator, never
against another reimplementation.

## Coverage of the original functions

See `COVERAGE.md`. Every original function reachable from the game logic is implemented with a
trace, excluded for a reviewed reason (`exclusions.csv`), or listed in `missing-allowlist.txt`;
`FaithfulnessCoverageTests` keeps the list shrinking.

## Branch coverage of the original code

`BRANCHES.md` / `branches.csv`: blocks and branch outcomes of the original code exercised by the
oracle runs (where the C# agrees), per function; never-exercised branches are either reached by
new cases or listed in `unreachable.csv` with a justification.

| scope | blocks | branch outcomes |
|---|---|---|
| the 479 functions of the unit corpora, before | 88.6% | 81.2% |
| the same, after the gap-closing pass | 89.9% | 82.1% |
| the same, without the C run-time library | 90.4% | 83.4% |
| all 4,369 functions reached, card and saint oracles included | 70.6% | 61.2% |

`unreachable.csv`: 208 entries (51 dormant branches — tautologies, repeated tests, unused
thresholds —, 4 compiled forms, 153 in the run-time library). About 3,000 never-exercised items
remain open, mostly in the battle, castle and mine generators.

## Structure of the C# against the original (static)

`STRUCTURE.md` / `structure.csv` (`tools/faithfulness/structure.py`): without running anything, every
original function the C# cites is compared with the C# that implements it: the calls with their constant
arguments, the random draws, the call order, the constants tested and written, and the number of
branches, read from the EXE bytes and from the C# (Roslyn). It covers every cited function at once,
including paths no oracle case reaches. Its findings are leads to review; a reviewed lead goes to
`structure-reviewed.csv` with the reason.

## Card oracle

`CARDS.md`: every one of the 365 original card functions runs in the emulator with a scripted
choice policy and is compared with the C# card (texts, option states, hints, random draws, state
changes, next card) — 14,600 runs:

* 13,542 agree (92.8%);
* 1,058 differ for one of 24 listed reasons (`card-divergences.json`) — cards run without their
  quest, where the original reads index −1 of a table (416); stack leftovers returned as hint
  results; option flags left by an earlier card; missing message blocks or files; the endless
  loop of card 0xAB; crashes of the original;
* 0 differ without a reason.

## Saint oracle

4,352 runs covering all 136 saints: all agree.

## Other oracles

About 12,000 further cases: money, calendar and random numbers, events, party and location
rules, prices and stock, character creation, names, the saints' help, attack and damage, enemy
setup, collisions and line of sight, path finding, every battlefield generator, spell effects,
memory-layout effects (writes outside arrays the original performs).

## Divergences and original bugs

`DIVERGENCES.md`, `ORIGINAL_BUGS.md`, `UNCERTAIN.md`, `DIVERGENCE_REVIEW.md`: generated from the
markers in the code, each with its reason and original coordinate.

## Presentation

The graphics driver's behaviour (start-up colours, fonts, XOR option lines, the fade,
see-through restores) was reconstructed from the original driver; the Quickstart sequence
matches the original DOSBox-X captures pixel for pixel (`tools/visuals/compare.py`).
