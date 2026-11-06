"""A left button pressed at a timer tick, whatever the game is doing (an original-side module of the sessions that use the step; the others do
not import it, so their recordings keep their keys).

  tclick:<n>:<x>,<y>          once the step is the script's next one (the step before it was taken), the game's timer callback (root
                              047C:000A, called by its INT 8 handler) runs n more times; then the pointer is at (x, y) and the left button is
                              down, as the driver's event callback (root 047C:1395) leaves it: DS:6EF0 bit 0 and the press word DS:6EF2. It is
                              let go as a `click:` is (orig_side.Session._release: CLICK_MS after the game read the button). A press inside a
                              loop that polls no input but the button between its waits: the map's rest (R030+0x0C44 case 0x1E41, `if
                              (mouse-button-state & 3) break` at R030+0x0F60 after each key test).

The release logs "tclick held <ms>" (the press to the release, on the emulated clock): lockstep.py gives the step to the C# host
(Darklands.Host.Headless ScriptedHost) as tclick:<n>:<x>,<y>,<ms>, which presses the button n / 60 s later on its own clock (as tkey's
keys) and holds it that long. lockstep.py wraps orig_side.Session with session_class for a script with a tclick step and hashes this
module with the original side.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orig_side  # noqa: E402

STEP = "tclick"


def uses(steps) -> bool:
    return any(kind == STEP for kind, _ in steps)


def parse(value: str) -> tuple[int, int, int]:
    n, _, at = value.partition(":")
    x, y = (int(v) for v in at.split(",")[:2])
    return int(n), x, y


def holds(log) -> list[int]:
    """The ms each tclick's button was held in the original, in the order of the steps (from the log's "tclick held" lines)."""
    return [int(l.split()[2]) for l in log if l.startswith("tclick held ")]


def session_class(base):
    """base (orig_side.Session or a subclass) with the tclick step: counted down at every call of the game's timer callback, then the
    left button pressed."""

    class TickClickSession(base):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.tick_click_ms = None       # when the held button was pressed by a tclick
            self.hook(*orig_side.TIMER_CALLBACK, self._tick_click, record="root")

        def _tick_click(self, m):
            if not self.script or self.script[0][0] != STEP:
                return
            n, x, y = parse(self.script[0][1])
            if n > 0:
                self.script[0] = (STEP, "%d:%d,%d" % (n - 1, x, y))
                return
            self._tick_press(x, y)

        def _tick_press(self, x: int, y: int):
            self.script.pop(0)
            if self.held:
                self._release()
            ds = orig_side.DS
            self.put_word(ds, orig_side.POINTER_COLUMN, x)
            self.put_word(ds, orig_side.POINTER_ROW, y)
            self.mouse[0], self.mouse[1] = x, y
            # as orig_side's click: the driver's event callback stores the button state in DS:6EF0 and ORs it into the press word DS:6EF2
            self.mouse[2] = 1 | (2 if self.rheld else 0)
            self.presses[0] += 1
            self.press_at[0] = (x, y)
            self.held = True
            self.held_bit = 1
            self.button_read = False
            self.map_reads = 0
            self.press_ms = self.ms
            self.put_word(ds, orig_side.MOUSE_BUTTON_STATE, self.mouse[2])
            self.put_word(ds, orig_side.MOUSE_PRESSES, self.word(ds, orig_side.MOUSE_PRESSES) | 1)
            self.tick_click_ms = self.ms
            self.log.append("tclick %d,%d pressed at %d ms in %s" % (x, y, self.ms, " < ".join(self.call_stack[-3:])))

        def _release(self):
            super()._release()
            if self.tick_click_ms is not None:
                self.log.append("tclick held %d at %d ms" % (self.ms - self.tick_click_ms, self.ms))
                self.tick_click_ms = None

        def _next_key(self):
            if self.script and self.script[0][0] == STEP:
                return None             # not pressed yet: nothing waits
            return super()._next_key()

        def _get_key(self, m):
            if self.script and self.script[0][0] == STEP:
                # a read that waits for a key before the tick: the button comes at once (the emulated time stands here, as at any blocking
                # read of the tester); the read lets it go before it takes the next key
                _, x, y = parse(self.script[0][1])
                self._tick_press(x, y)
            return super()._get_key(m)

    return TickClickSession
