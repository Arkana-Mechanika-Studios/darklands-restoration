# Original-function index

`original-functions.csv` lists every entry of the original `DARKLAND.EXE` 483.07 that the C#
can cite, keyed by its **coordinate** (docs/TRACEABILITY.md): 10233 functions, 753 RTLink overlay
thunks, 188 far-data addresses in the data image and 7 bare code addresses. Each row gives the
link-time address, the size, the kind and the source status of the function in the reference
decompilation, with the evidence for that status. Coordinates are independent of any
decompilation: they can be checked against the game's own bytes and the canonical RTLink atlas
(a machine-local tool: `python -B -m darklands_ida.rtlink_atlas`).

It is a **lookup table for traceability**: `map.csv` and the `// orig:` comments in the C# cite a
coordinate, and this table says what is there and how far the reference's C for it can be trusted.
It is generated; do not edit it by hand.

## Columns

| column | meaning |
|---|---|
| `coordinate` | `RNNN+0xPPPP` (atlas record NNN, payload offset PPPP = (frame − linked paragraph) × 16 + offset), `root SSSS:OOOO` (resident code), `thunk 09C0:XXXX` (RTLink call vector), `R136+0xPPPP` (far data: the data image, linked at 20A5) |
| `linked` | `SSSS:OOOO`, the link-time (logical) address; for root a real resident address |
| `size` | function length in bytes (empty when unknown: data, addresses) |
| `kind` | `function`, `thunk`, `data` or `address` (below) |
| `status` | `matched` / `unmatched` = the reference's C of the function recompiles to the original bytes under MSC 6.00A / does not; `draft` = only draft C exists (the note says whether its bytes match); `asm` = no C (reconstructed from the disassembly); empty for thunks and data |
| `evidence` | where the status comes from: `bytematch-600a` (the per-function byte comparison), `+audit` (an independent per-function MSC 6.00A recheck agrees), `+port-check` (an earlier port that checked the same functions agrees); empty for rows without a status |
| `target` | thunks: what the thunk jumps to, `RNNN+0xPPPP (SSSS:OOOO)` or `root SSSS:OOOO` |
| `note` | free text: the draft's byte result, the thunk's atlas status, the function an address lies in, a recheck that disagrees |

## Entry kinds

* `function` (10233): every function of the reference's analysis of the EXE (10106), plus 127 tiny
  functions (mostly 2-byte `retf; nop` stubs) the analysis never listed but the reference's C defines
  at their own address (note: "entry not in the analysed function list").
* `thunk` (753): the RTLink call vector at **root `09C0:XXXX`**, a 10-byte far jump to its target. The
  atlas thunk at raw file offset `0x1800 + 0x9C00 + XXXX` agrees with the target for all 753; the note
  carries its atlas status: 740 `mapped`, 12 `excluded_image` (target image not admitted by the
  atlas; the target is still given), 1 `resident_sentinel` (`thunk 09C0:17E3`, target root 0000:25F0).
* `data` (188): far data at `SSSS:OOOO` in the data image **R136** (atlas `data_image`, user section 138,
  linked at `20A5`), payload = `(SSSS - 0x20A5) * 16 + OOOO`. DGROUP is `321A` (cited as `DS:XXXX`, not
  indexed). The `290E` addresses are tables (e.g. R136+0xA70F shop names, R136+0xC02B the shop stock,
  R136+0xBBE7 screen boxes, read from 290E:3559). The size is unknown.
* `address` (7): code addresses the C# cites that are not function entries: an interior address
  (`R044+0x0123`, inside R044+0x0000; `R089+0x0968`), a root code address named like data
  (`root 0000:2652`, the same code as root 0265:0002), and the 7-byte `jmp far 0:0` stub slots 103 and
  104 of the sound-driver table at 047C:0791 (`root 047C:0798`, `root 047C:079F`; patched at run time,
  so the file bytes are zero), and two timer-driver addresses no analysed function covers (`root 047C:000A`,
  and the interrupt handler `root 047C:01EA`), cited by the source map.

## Counts

| kind | status | root | overlay | total |
|---|---|---|---|---|
| function | matched | 26 | 8501 | 8527 |
| function | draft (bytes match) | 45 | 641 | 686 |
| function | draft (bytes differ) | 120 | 337 | 457 |
| function | asm | 267 | 296 | 563 |
| thunk | | 753 | | 753 |
| data | | | 188 | 188 |
| address | asm | 5 | 2 | 7 |

## Regenerating

```
python tools/source-map/build_original_index.py [--map local/reference-map.csv]
```

The script writes the first eight columns of the machine-local mapping `local/reference-map.csv`
(git-ignored), checking that every coordinate is well-formed and unique and that every kind and
status is known. The mapping holds, for each coordinate, what the reference decompilation calls the
function, its readable names, its source file, its own status fields and its revision; it is made on
the machine that has the reference checkout (`reference` in `local/paths.json`) by the local tools in
`local/tools/`, from the reference's analysis, its source tree, a per-function byte comparison under
MSC 6.00A (each compiled function relocated to its
original address so that calls, jump tables and data references resolve in place) and the rechecks
listed under `evidence`. `python -I tools/source-map/resolve.py <coordinate>` prints the mapping entry
of a coordinate; without the local mapping it says so and stops.

Checks made when the mapping was built: for every function the linear address equals
`frame * 16 + offset` and the payload range lies inside the atlas record's allocation (no negative
offsets, no overruns); every coordinate is unique; every thunk agrees with the atlas.

## RNNN = directory index (not user section)

`RNNN` is the atlas **directory index / record id NNN** (`R047`), *not* the atlas `user_section`
(which is id + 2 for all 137 records: R000 -> 2, R004 -> 6, R029 -> 31, R047 -> 49, R135 -> 137).
Verification: for all 135 overlay records with functions, the lowest link-time frame of their code
equals the atlas record's `linked_paragraph` (R047: 6200 = 0x1838); the two records that carry
source-navigation views (R014, R047) list logical segments that contain every frame of their code
(R047: 1838, 1852, 1974, 1A44); and every derived payload range lies inside `allocated_bytes` of
record NNN (9653 overlay functions).

## Limits

* An address does not prove fidelity. A row says where the original bytes are and how far the
  reference's C for them can be trusted; it does not say the C# behaves like them. `matched` only
  means that the reference's C recompiles to the same bytes.
* `payload` identifies bytes in the image payload. It does not give instruction alignment (the
  offset is a function entry per the reference's analysis, not independently disassembled here), and
  it says nothing about residency: a linked paragraph is not a session address, and overlays are
  loaded at run time wherever RTLink places them.
* `linked` for overlays is a logical address; several records link at the same paragraph (e.g.
  `0E76`), so `SSSS:OOOO` alone is ambiguous without the record.
* `asm` means the reference has no C for the function; it may still be a C-compiled original, a
  library routine or RTLink run-time code.
* Function boundaries, sizes and the pairing of compiled functions to originals come from the
  reference's analysis (the byte comparison pairs compiled and original functions in order, per
  source file); a source file whose function count differs from the original unit leaves the rest `asm`.
* `draft` includes drafts known to misbehave in play; "draft bytes match" is a byte comparison, not a
  behavioural claim.
* The status is tied to the toolchain (MSC 6.00A) and to the reference revision recorded in the local
  mapping; rebuild the mapping after reference changes.
