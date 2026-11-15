# Dissolve Lock free opening: what the original does (2026-11-15)

Case (decision of 2026-11-15, from a community report): in a battle a member is ordered Dissolve Lock and the order waits; meanwhile
his Eater-Water is moved away on the character sheet. The order still opens the lock.

## Sessions

Synthetic save `DKSAVE0-eater2` (`synth_saves.py eater2`): Hans (party slot 2) carries one Eater-Water (code 99, quality 30) as the first
stack of his pack. Battle: request 0 / battlefield 137 / seed 2 (a chest with a magic lock, lock byte 0x8F, beside the party).
Hans is selected (3) and ordered Dissolve Lock (D); then

| session | what happens while the order waits | what the order does in the original |
|---|---|---|
| `dissolve-free-control` | nothing | the Eater-Water (code 99) is used up; the chest opens (loot window) |
| `dissolve-free-moved` | his sheet (F3, pack, Shift+4) gives the Eater-Water stack to the member of party slot 3; one Esc leaves the sheet, the order stands | the stack that slid into the old slot (code 30) is used up, the Eater-Water is not (it is with the other member); the chest opens |

(Two Escs leave the sheet and cancel the order: the second Esc is the battle's Cancel Giving Order.)

The pack after the order, from the original's own Alt-S save (evidence variants `evidence-sessions/dissolve-free-*-save.steps`):

| run | Hans's pack |
|---|---|
| control | 8 stacks, Eater-Water gone, code 30 first |
| moved | 7 stacks: code 30 gone as well, the Eater-Water in the other member's pack |
| moved, Eater-Water last (probe, `scratch\free-opening`) | the stale slot is past the last stack: the stale entry's count goes down and the pack's count with it (7); here the Eater-Water itself was the last stack |

## Our engine

* Classic (`OriginalBehaviour.Faithful`, all fixes off): agrees with the original on every battle pass and on the pack (the stack at the old
  slot is used up, the chest opens).
* Fix on (`--datasafe`, `FixStaleDissolveSlot`): nothing is used up (Hans keeps all 8 stacks, code 30 included), the chest opens: the free
  opening. This part departs from the original by design.

## Open finding found on the way (not part of this case)

A save written with Alt-S in a battle differs from the original in byte 0x1C of party records 1..3 (the original has the party slot,
3, 2, 1; we write the byte as read, 0 in DKSAVE0). It is there with no order at all (an Alt-S right after the battle starts), so the
evidence sessions are kept out of the replay set (`tools/oracle/evidence-sessions/`). Reported to the coordinator.

Run folders: `scratch\free-opening` (`probe1`, `probe2`, `run1..run5`, `final`); recordings in `code\local\oracle-recordings`.
