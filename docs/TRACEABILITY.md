# Tracing the C# back to the original

Every reconstructed operation says where it comes from, how sure we are, and where it
deliberately differs. The C# keeps readable names; the trace lives in short comments and in
one shared map. A reference to an address does **not** prove fidelity: only tests and
oracles do, and the trace says which ones cover the code.

The trace names the original **by its coordinates only**: places in `DARKLAND.EXE` 483.07
that anyone can check against the game and the canonical RTLink atlas, independently of any
decompilation. The semantics were read from a verified reference decompilation; its function
names, source files and revision are not part of this repository. That reference is the Darklands-plus project (MIT licence); the source map builds on its analysis, and we thank its author. The mapping from a
coordinate to the reference's names and files is kept machine-local (see "The local mapping").

## Coordinates

| Kind | Written as | Meaning |
|---|---|---|
| Overlay function | `R044+0x0123` | RTLink directory record 44, payload offset 0x0123 = (frame − record's linked paragraph) × 16 + offset |
| Linked address | `1838:0123` | the original link coordinate (needs the record for context; several records share frames) |
| Resident function | `root 0000:0CAA` | the always-loaded image |
| Overlay thunk | `thunk 09C0:190F` | the RTLink call vector at root 09C0:XXXX, a 10-byte far jump to its target (the index gives the target) |
| Far data | `R136+0xD090` | an address in the data image (record 136, linked at 20A5): payload = (segment − 20A5) × 16 + offset |
| Data | `DS:E48A` | DGROUP (321A) offset |
| Segment of a record | `R029 seg 0E76` | the code of record 29 linked at frame 0E76 (a group of functions) |
| Graphics driver | `drv+0x1223` | offset in the code of the player's `MGRAPHIC.EXE` (after its 512-byte header), the driver the game loads at start-up (root 047C:07CE) and calls through the thunk slots root 047C:04C7 + 7 × n; slot n's target is the word at image offset 0x32 + 2n (0x30: the slot count) |
| Synthetic IDA address | `ida:0x2400123` | only in the atlas-pinned IDA materialization; never mixed with the above |

`RNNN` is the atlas directory index (record id), not the atlas "user section" (R047 is user
section 49). The canonical atlas is the RTLink atlas tool's `data/rtlink/atlas.json` (machine-local)
(`python -B -m darklands_ida.rtlink_atlas`). `docs/source-map/original-functions.csv` lists every
original entry by its coordinate, with its link-time address, size, kind and source status.

## Source status

| Status | Meaning |
|---|---|
| `matched` | the reference C of this function compiles to the original bytes (MSC 6.00A) |
| `unmatched` | reference C whose bytes differ (register/slot noise, or worse: read it with the asm) |
| `draft` | draft C of the reference: read with the disassembly, it may be wrong (the index notes whether the draft's bytes match) |
| `asm` | no C: reconstructed from the disassembly |

The index's `evidence` column says where a status comes from: `bytematch-600a` (a per-function
byte comparison of the recompiled C with the original under MSC 6.00A), `audit` (an independent
per-function recheck), `port-check` (an earlier port that checked the same functions).

## In the code

One comment per reconstructed operation (class, method or block), on the line above it:

```csharp
// orig: R044+0x0123 (1838:0123) matched; tests: CardSmokeTests
// orig: card-run R043+0x0A9A (150B:000A) draft (bytes match); tests: CardSmokeTests
// orig: roll-n root 0410:0008 matched
// orig: the prayer screen (thunk 09C0:1EAF), the card table R136+0xD090, DS:EB0A
```

The coordinate comes first (a readable name of the function may precede it: names such as
`card-run` or `party-wipe` are ordinary descriptions), then the link-time address for
overlays, the source status and the tests or oracles that cover it. Prose cites other functions
the same way ("calls R043+0x09AE"). `tools/source-map/orig_comment.py <coordinate>` prints the
comment for a coordinate.

Further markers, each on its own line where it applies:

* `// DORMANT: …` — a branch the original has but nothing reaches (kept, not deleted; say why
  it is unreachable and under which state it would run).
* `// DIVERGENCE: …` — the C# behaves differently on purpose (a bounds guard, a host
  difference) and what the original does instead.
* `// UNCERTAIN: …` — the mapping or meaning is a guess (a field's name, a draft's reading).
* `// C: …` — a C-specific point the port had to honour: 16-bit wrap-around, signed/unsigned
  comparison or division, integer promotion, right-to-left argument evaluation (the order of
  random draws), aliasing through pointers, side effects inside expressions, uninitialised
  locals or stale globals that the original reads.
* Original bugs keep using `OriginalBehaviour` flags, with a `// BUG:` comment.

## Rules

1. **Every branch of the source C is preserved**, including dormant and odd paths (dead
   handlers, impossible cases, option lines that nothing selects). If a branch truly cannot be
   expressed, it is listed as a `DIVERGENCE` with its condition.
2. Distinguish byte-matched C from draft C. For `draft` and `asm` sources, check the C against
   the disassembly where behaviour depends on it, and say so (`checked against asm`).
3. Review explicitly: arithmetic width and sign, evaluation order, aliasing, side effects.
   Note each case that matters with `// C:`.
4. Never simplify silently: an omission is a `DIVERGENCE`, a guess is `UNCERTAIN`.
5. Add the operation to `docs/source-map/map.csv`.
6. Cite coordinates only: no function names, file names, paths or revision of the reference
   decompilation in any tracked file (`NoThirdPartyNamesTests` checks this against the
   machine-local list `local/forbidden-identifiers.txt`).

## The shared map

`docs/source-map/map.csv`, one row per reconstructed operation:

| Column | Content |
|---|---|
| `csharp` | `Type` (a whole card or class) or `Type.Member()`; a namespace prefix is optional |
| `original` | the original coordinate(s) the operation reproduces, `;`-separated (also `DS:XXXX`, `drv+0x…`) |
| `coordinate` | the main coordinate: `R044+0x0123` / `root 0000:0CAA` / `DS:E48A` |
| `status` | `matched` / `unmatched` / `draft` / `asm` |
| `verified_by` | tests or oracle corpora covering it (`Oracle:rules.json`, `CardSmokeTests`), or empty |
| `notes` | `dormant: …`, `divergence: …`, `uncertain: …` in short |

`tests/Darklands.Core.Tests/SourceMapTests.cs` checks that every indexed coordinate (RNNN+0xPPPP,
root, thunk) named in `original` exists in the index and is cited in the C# file that defines
`csharp`, and that a row's status is the one the index gives.

## The local mapping

The reference decompilation itself and everything derived from it that carries its names stay on
the machine that has it, under the git-ignored `local/` folder. This is maintainer-only: a public
checkout has none of it, and the tools and tests that need it say so and stop or skip.

| File | What |
|---|---|
| `local/paths.json` | `reference` (the checkout), `python`, `audit`, ... (see `tools/local_paths.py`) |
| `local/reference-map.csv` | coordinate ↔ reference function name ↔ readable names ↔ source file ↔ status ↔ evidence ↔ revision, one row per index entry; its first eight columns are the published index |
| `local/tools/` | the generators that read the reference (the index builder, the per-function byte match, the static-analysis adapter of `tools/faithfulness/coverage.py`, the source lines of the branch report, the option-flag table) |
| `local/forbidden-identifiers.txt` | what no tracked file may contain |

`python -I tools/source-map/resolve.py R044+0x0123` prints the local mapping entry of a coordinate
(also of a readable name or `RNNN SSSS:OOOO`); it stops with a clear message when the mapping is
absent. Tests that need reference-side data read `local/reference-map.csv` and pass with a
"SKIPPED" message when it is missing.

## Looking things up

* **Status follows the source.** A function whose only reference C is a draft is `draft`, even where
  the draft's bytes match (the index notes "draft bytes match"); `matched`/`unmatched` describe the
  reference's byte-matched C only. The status is the per-function byte comparison of the index, not
  a source file's own header or a file-level summary (for the main loop root 03A1:000A the header
  claims a full match and a file-level summary counted 0/1; the per-function comparison matches).
* **Overlay thunks.** A call through `thunk 09C0:XXXX` is thunk number `(XXXX - 0x17E3) / 10` of the
  atlas (`data/rtlink/atlas.json`, key `thunks`): its `index` is the record and
  `linked_segment:linked_offset` the target, which the index gives in its `target` column
  (`thunk 09C0:190F` = `R029+0x54D8`, pass-hours; `thunk 09C0:1FB3`/`thunk 09C0:1FBD` =
  `R030+0x29BC`/`R030+0x29FA`, compass directions; `thunk 09C0:1FF9` = the camp `R036+0x0000`;
  `thunk 09C0:18DD` = the start screen `R037+0x9C48`; `thunk 09C0:18F1` = `music-start` `R043+0x09AE`).
* **Disassembling a draft.** Payload bytes of a record are at `payload_raw + offset` in `DARKLAND.EXE`
  (record `payload_raw` in the atlas); 16-bit capstone from that address reads the function. The draft
  C of `R030+0x0C44` and `R030+0x1300` was checked this way.
* **Time units.** `wait-ticks` (R030 seg 1767) waits on `clock()`; MSC 6 counts it in milliseconds.
  The PAN player and `root 0410:0302` use the timer driver's tick (about 14.3 ms measured for the PAN
  player); the message-box length of 1000 ms is an approximation.
* **The graphics driver.** The drawing primitives (palette, text, sprites, saved rectangles, the pointer)
  are in `MGRAPHIC.EXE`, which the reference has no C for: disassemble its code image (16-bit, origin 0
  after the 512-byte header; its data segment is paragraph 0x140, image offset 0x1400) and cite
  `drv+0xNNNN`. DGROUP data (window records, colour tables) is in the last directory record (the data
  image) at `payload_raw + (0x321A - linked_paragraph) * 16 + offset`.
* **Visual checks.** `tools/visuals/compare.py` plays the Quickstart opening in the headless host and
  compares each screen with DOSBox-X captures (640x400) pixel by pixel; `tools/visuals/pixdiff.py`
  compares one frame.

## Faithfulness checks (docs/faithfulness)

* **Branch coverage.** `tools/oracle/block_coverage.py <EXE> <game> <work dir>` runs every corpus builder with
  `DL_COVERAGE_DIR` set (run_unit then records executed blocks and conditional-branch outcomes in original
  coordinates), checks that each corpus regenerates as committed, enumerates every reached function's blocks and
  branches with capstone (`tools/oracle/cfg.py`) and writes `BRANCHES.md`, `branches.csv` and
  `branches-missed.csv`. Further coverage folders in the work dir (the card and saint oracles' `--coverage`) are
  sources too. With `<work dir>/ref-lines.json` (made by a machine-local tool from the reference) the
  reference's source line of each missed item goes to `local/faithfulness/branches-ref-lines.csv` only.
* **Never-exercised branches** are closed with oracle cases (the C# must agree), or listed in
  `unreachable.csv` with the verdict (`dormant`, `compiled-form`, `runtime-library`, ...), the reason and the C#
  place that keeps the branch with its `// DORMANT:` note. Verdicts: `dormant` (no play reaches the branch: a
  tautology, a repeated test, an unused threshold, a switch default no value reaches; a reason that starts with
  `callers:` rests on the values every caller passes, one that starts with `data:` on the values the game data
  holds; both count as `dormant`), `compiled-form` (the compiler's form of a construct), `excluded` and
  `runtime-library` (code that is not game logic). Many rows of the card records argue about the next-card
  word (DS:E48A, `following-card` in the reasons): it is written only by the card records themselves and by the card
  dispatcher (R030 1462:0000); the root code and the helper records R043 and R029 never write it, so a card
  record that sets it and then tests it is not disturbed in between. A builder may add generated cases guided by coverage
  (`run_unit.CoverageSet`, `coverage_defer` + `commit_coverage`): only those that exercise something new are kept.
* **Function coverage.** `tools/faithfulness/coverage.py` classifies every original function of the index as
  implemented (its coordinate cited by the C# or the map), excluded (a reviewed pattern of `exclusions.csv`,
  matched against `root SSSS:OOOO` or `RNNN+0xPPPP (SSSS:OOOO)`) or MISSING; its static analysis of the EXE
  comes through the machine-local adapter `local/tools/reference_program.py`.
* **Card oracle.** `tools/oracle/card_oracle.py` runs every original card function in `dosmachine.py` (overlays
  with their RTLink thunks, DOS files and memory) from the C#'s own start states (`CardOracleStates`, loaded by
  the original's own world and saved-game loaders) and records traces and the original's own saved game after
  the card; `CardOracleTests` replays them against the C#. `tools/oracle/saint_oracle.py` does the same for the
  help of every saint (`SaintHelpOracleTests`). `CARDS.md` is the per-card result (`tools/oracle/cards_report.py`);
  `card-divergences.json` lists the differences that are not bugs, with the reason.
