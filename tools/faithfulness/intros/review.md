The divergences in core logic that threaten 100% faithfulness, with a recommendation.

* **Part A** lists every core-logic divergence classed `not-modelled`, `deliberate-fix` or
  `other`, with the action: `implement` (make it as the original), `implement-later` (needs a
  subsystem first), `justify` (keep; the reason is given) or `remove-marker` (the divergence is gone; the marker is to be removed).
* **Part B** lists the core-logic `undefined-memory` divergences: where the original reads or
  writes outside its defined data. For each: is the original's outcome deterministic, so that it
  could be reproduced exactly (a table that follows the array in the data image, a known stack
  leftover, a global the previous card left behind)? "checked against the original" means the
  reference C or the disassembly (and, where a table was involved, the data image) was read for
  that entry; otherwise the recommendation rests on the note alone.

The text of each recommendation lives in `tools/faithfulness/review_notes.csv` (keyed by entry
id); this file is generated from it. Nothing here changes behaviour.
