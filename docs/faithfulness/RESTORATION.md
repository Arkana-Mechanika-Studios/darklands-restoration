# Restoration register (first pass)

`restoration.csv` lists every candidate for the **Restored** rule set: content the original game contains or
promises but never delivers, and what its designers said they meant. It is the starting list of track C3 (post-1.0
design, change class "Restoration" in A5). One row is one restorable thing, not one line of code: the dormant and
unreachable registers hold the code-level facts, this register groups them by what the player would gain.

Rules it follows:

- **Research informs; it does not by itself change rules.** A row is a proposal with its evidence. Nothing here is
  in the game. A restoration ships only as a gated rules change (Rules axis: restored) or as a first-party mod, with a
  test, and never touches Classic (A1).
- **Classic stays the reference.** Every candidate is something the faithful game does *not* do. The oracle still
  checks the original behaviour; the restored behaviour sits behind its own switch.
- **Restored includes Fixed.** A row that is also an original bug (see `ORIGINAL_BUGS.md`) is flagged in its note:
  whichever of the two sets takes it, the other inherits it. The boundary with C4 is decided per row, with the
  evidence below.

## The file

`restoration.csv`, UTF-8, one header row, fields quoted when needed. Hand-curated (no generator); `registers.py` does
not touch it.

| column | meaning |
|---|---|
| `id` | `R-` and six hex digits, the first six of the SHA-1 of `restoration|<key>`: stable as long as the key stays. Same style as the other registers' ids |
| `key` | short unchanging name of the item (the id derives from it) |
| `title` | what is missing, in one line |
| `kind` | `dormant code`, `unused data`, `unused text`, `half-wired feature`, `documented-not-implemented`, `designer statement` |
| `level` | the evidence level that makes it a candidate (below) |
| `other_levels` | further levels with sources, `;`-separated |
| `sources` | register ids (`Z-xxxxxx` dormant, `B-xxxxxx` original bugs), card or file names, archive messages (`#nnnn`, the group's own numbers), documents (manual, clue book, 1992 conference, design papers) |
| `restoring` | what restoring it would mean, in our words |
| `size` | S (about 1 to 3 sessions), M (about a week), L (several weeks), XL (a phase), as in the release plan |
| `status` | `candidate`, `needs research` or `rejected` |
| `evidence` | the evidence trail: each source, what it says, its reliability (below), `;`-separated |
| `note` | for `candidate`: the caveat; for `needs research`: what is unknown; for `rejected`: the reason |
| `revisit_if` | for `rejected` and `waits`: what would reopen the row (a text found, a design paper, a version difference, a part restored first) |
| `mod_possible` | for `rejected` and `waits`: whether the item could come back as new content (a mod, our own design) and what that needs (new text, a reward or destination, new data, a new picture, the data or world seam), or why not |

### Evidence levels (design C3)

1. present in the program or data and reachable in principle, but unused (dormant code, unused records, unused text);
2. present but incomplete (half-wired features, placeholder texts);
3. described in the manual, the clue book or other original documentation;
4. stated by a designer (posts, interviews, the 1992 conference, the mailing-list archive);
5. design documents and code held in archives (the design papers of the team);
6. historical research, where the game aims at history.

Besides the levels, the `evidence` column marks a source's kind: `code` and `data` (the original's program and files, with `oracle`
where the original was run on it), `V` (present in an earlier release, 483.04 to 483.06, against 483.07), `M` the manual and `C` the
clue book (level 3; the clue book says the manual was printed before the game was finished and ranks itself above it), `D` a designer's
statement (level 4, with its year), `P` a design paper (level 5), `T` a third party's reading of the game files (reliable for what the
files hold, speculation where it guesses intent), `G` the community archive (`O` an observation in play, `F` a file analysis, `H`
hearsay), with the group's message numbers.

A row records the level that makes it a candidate and lists the others. Level 1 and 2 rows are checkable against our
own registers and the original files; level 3 to 5 rows rest on documents; no row rests on level 6 alone yet.

### Status

- `candidate`: complete or nearly complete in the original files, evidence is solid, what restoring means is clear.
  Still needs a decision of the user, a test and a switch before it enters a rule set.
- `needs research`: promising but something is unknown (what the text holds, the amount, the intent, who owns it).
  The note says exactly what.
- `rejected`: recorded so it is not raised again, with the reason (nothing to restore, designers chose against it,
  needs art or a map that never existed, debug aid). A rejected row is not forgotten: it stays in the register with what would
  reopen it (`revisit_if`) and whether it could come back as new content (`mod_possible`): a rejected restoration may still be
  a mod or our own design, which is new content and never part of Restored. The rejected rows are reviewed whenever new sources
  arrive (an earlier release, a design paper, a statement).
- `built`: in the Restored rule set: a switch of class `restoration` in [switches.csv](switches.csv) (presets `restored`
  only, its `register` this row's id), named in the note with the check against the original and the test.
  `SwitchRegisterTests` keeps the two registers in step.
- `fixed`: not a restoration after all but an original bug whose fix is in the Fixed rule set (a gameplay switch of class
  `gameplay`, its id named in the note); Restored inherits it.
- `waits`: a candidate that cannot be built yet: it needs saved state the original's `.SAV` does not hold, or new data
  (records, pictures) that need the data seam. The note says which.

## Method

1. Read every row of `dormant.csv` (875) and decide: restorable content, or scaffolding of the original program.
   Rows tell of dead handlers, hidden option lines with no text, empty initialisers, arms that cannot run; most are
   not content. Rows that show *finished logic or text that no path reaches* became candidates; rows that group into
   one feature became one item.
2. Classify every row of `unreachable.csv` (4,249 blocks) by the reason given for its unreachability. Its blocks are
   fine-grained proofs of the same facts; the keyword search for text, cards and features found nothing that the
   dormant rows do not already cover. These rows were classified by their justification text and sampled, not read one
   by one.
3. Scan `original_bugs.csv` (277) for bugs that are really unfinished content (a mechanism computed and never used, a
   description the code does not honour).
4. Add what the documents describe and the game does not do: the developer-intent report, the clue book and manual
   notes, the 1992 conference, the design papers of the team, the history notes and the community archive.

## What the first pass found

70 items. Counts by kind and status:

| kind | candidate | needs research | rejected | total |
|---|---:|---:|---:|---:|
| dormant code | 11 | 11 | 1 | 23 |
| half-wired feature | 3 | 11 | 2 | 16 |
| documented-not-implemented | 2 | 8 | 3 | 13 |
| designer statement | 1 | 1 | 5 | 7 |
| unused data | 1 | 3 | 1 | 5 |
| unused text | 1 | 5 | 0 | 6 |
| **total** | **19** | **39** | **12** | **70** |

By evidence level (the level that makes it a candidate):

| level | candidate | needs research | rejected | total |
|---|---:|---:|---:|---:|
| 1 present, unused | 17 | 18 | 2 | 37 |
| 2 present, incomplete | 0 | 11 | 2 | 13 |
| 3 manual or clue book | 1 | 2 | 1 | 4 |
| 4 designer statement | 1 | 2 | 4 | 7 |
| 5 design papers | 0 | 6 | 3 | 9 |
| 6 historical research | 0 | 0 | 0 | 0 |

By size guess: S 33, M 22, L 9, XL 6 (candidates: 16 S, 3 M).

The counts are those of the first pass. Since then four candidates are `built` and one `waits` (restore-r1, 2026-11-24: see
[In the Restored rule set](#in-the-restored-rule-set)), leaving 14 candidates; the first research pass of the `needs research` rows (same day)
made two of them candidates (fortress-commission-pay, dragon-lair-icon) and rejected nine, with the reasons in their notes:
16 candidates, 4 built, 1 waits, 28 needs research, 21 rejected. The research in the developer sources, the manual and clue book, the
community archive and the earlier releases (same day) gave every row its evidence trail and moved three: dragon-lair-icon to
`rejected` (the clue book says the lairs are hidden on purpose), relic-powers back to `needs research` (unused values in the relics'
records), and six new rows (three from the earlier releases, three from the design papers and the files): 76 rows, 15 candidates,
4 built, 1 waits, 34 needs research, 22 rejected. The user's decisions of the same day (the decision sheet) and
the code comparison of the earlier releases brought it to 82 rows: 14 candidates, 2 built, 3 fixed, 3 waits, 35 needs research,
25 rejected.

### Rows skipped, by reason

`dormant.csv`, 875 rows: 191 sit inside a `candidate` or `needs research` item, 11 inside a `rejected` item (the
decision is recorded there), and 673 were skipped:

| reason | rows |
|---|---:|
| default handler ("screwed up.") of option lines the card never offers | 166 |
| hidden or placeholder option lines with no content of their own | 156 |
| pop-up scaffolding: no item or saint line, empty or unused slot | 99 |
| option lines hidden by a text-block switch (variants of the same menu) | 56 |
| scaffolding: empty, uncalled or menu-less handlers | 53 |
| value-range or redundant-test guard (the arm cannot run) | 50 |
| battle or map generator internals (impossible arms, range guards) | 50 |
| guard or arm that cannot run (rules, cards and input handling) | 18 |
| screen or presentation plumbing | 13 |
| debug aid, DOS or host plumbing | 12 |

`unreachable.csv`, 4,249 blocks: none was turned into an item of its own (see Method 2). By reason:

| reason | blocks |
|---|---:|
| value-range proof (argument, roll or counter cannot leave its range) | 745 |
| other proof of unreachability | 725 |
| card set-up leaves the following-card unchanged (exit test cannot fail) | 585 |
| card menu scaffolding (option handlers, pop-ups, stale flags) | 566 |
| covered by a dormant row of the same function | 437 |
| compiled form of a reachable test (flag re-test, masked roll) | 370 |
| battle or map generator range guard | 327 |
| C run-time library, not ported | 153 |
| battle result cannot be negative | 124 |
| DOS, file or memory failure path | 90 |
| excluded: not game logic (memory, swap, host) | 84 |
| debug switch or force value (constant in the shipped game) | 27 |
| disassembly artefact (operand byte read as a branch) | 16 |

`original_bugs.csv`, 277 rows: 24 of them are cited by 9 items (the politics set-up and the revolution cards, the unused
come-back appointment, the fortress commission, the jewellers' night card, the offer between fight waves that never
shows, the potion and saint descriptions, saving in battle); the others are exploits or wrong formulas, the work of C4.

The skip reasons were assigned by keyword rules over the register notes after reading every dormant note; counts of
the large buckets are firm, the boundary between neighbouring buckets is not.

## The first candidates for the Restored preset

Ranked by how solid the evidence is, how little else has to exist first, and how small the change is. None needs an
engine seam beyond the 1.1 plan (Restored is its last step); none touches Classic.

1. **Mounted travel** (R-4e7c44, level 1+3+4, S). The original works out each time the map opens whether the whole
   party is mounted and never reads the answer; the clue book says an all-mounted party is faster. The amount is the
   open part.
2. **Hall of Fame** (R-8a277c, level 3+1, M). Promised in the manual, absent in every version; the start screen loads
   a fourth button and never draws it, and the inn already has a retire line. No rule changes.
3. **Physician's house** (R-5a84a7, level 1, S). A complete service (treatment for pay, study under the physician)
   whose business-district entrance is never enabled (built later: the crafts quarter's door is open in the original).
4. **Come-back appointment** (R-ceb4d1, level 1, S). A one-day welcome mechanism whose event never gets the flags its
   handler needs; also registered as a bug, so Fixed may take it first.
5. **Weapons training with the guard** (R-d1c1d4, level 1, S). Chance, offers and prices are written; the council
   hall line is never raised.
6. **Kidnapper quest chain** (R-6371a7, level 1+2, M). Two cards with text exist; the line that links them is never
   enabled.
7. **Jewellers' guild** (R-726c2f, level 1, S). Two whole cards (day and night) reachable only through a line that is
   never offered.
8. **Arms Outfitter** (R-c96902, level 1, S). A shop on Soldier's Road, written, never offered.
9. **Monastery lines** (R-8da2a6, level 1, S). Asking about the monks' problems and speaking with the abbot (the way
   into the monks' task).
10. **Night-long slum sneak** (R-97c6d1, level 1, S). A full scene behind a switched-off option.

Close behind: old woman's lodging (store items, add a friend), flood saints, small cave sightings, the two unused
city-news texts, the names never drawn, a minimum price of one pfennig.

## In the Restored rule set

Built from the candidates, smallest and best evidenced first. Each is a switch `restore.<area>.<name>` on in Restored only;
Original and Fixed do not change (the full replay is the gate), and the original's saved games stay readable both ways.
Before building, each was checked against the original program: the cards the restoration opens run in the card oracle
(the original's code on the same start state, compared with the C#), and the hidden lines it offers were forced in the
original's own menu (`F<block>.<line>` runs) and replayed by the C#.

| id | switch | what the player gets |
|---|---|---|
| R-5a84a7 | `restore.city.physician-street` | the business district's door to the physician's house |
| R-d1c1d4 | `restore.city.guard-training` | weapons training with the city guard, once a day, in the council hall |

Decided by the user on 2026-11-24: the Jewelers' guild (R-726c2f) waits on the politics season (without it the guild is an empty
visit); the "come back" appointment (R-ceb4d1), the fortress's commission pay (R-91ad2e) and the village Schulz's reward are
original bugs and went to the Fixed rule set as gameplay fixes (status `fixed`).

## What this pass did not do

- No level 6 item yet: nothing in the sources read points at a historical correction as the designers' aim. The
  research corpus feeds this later.
- Two audits are listed as items, not done: a scan of every message file for blocks no path shows
  (`unused-card-text-blocks`; the 24 dormant rows that already name one are cited) and a scan of the picture,
  enemy and item data for unused records (`unused-pictures`, `cut-monsters-and-weapons`).
- The design papers of the team were read from the findings notes, not page by page; two folders of the collection
  are still to come.
- Whether an item is *intended* content or a cut feature that was cut for a reason is not decided by this register:
  that needs a human read of each card's text (and the 483.06 files for `versions-removed-texts`).

## Using the register

- Every decision starts with research in our own sources, recorded in the row's `evidence`: the code and data and our
  registers; the research index of the community archive and its reports (developer intent, bug catalogue, history); the
  developer sources (the team's design papers, the 1992 conference, the designer's later posts and interviews, readings of the
  game files, the press, the manual and clue book text); the community archive; the earlier releases (483.04 to 483.06). When a
  new source arrives, the `rejected` and `waits` rows are read again against their `revisit_if`.
- Add a row when a source turns up; keep the key and id stable; never reuse an id.
- Move a row to `candidate` only with its evidence written in `sources`; a `rejected` row keeps its reason.
- When an item enters a rule set, add its switch id in the switch register and link the two by `id`.
