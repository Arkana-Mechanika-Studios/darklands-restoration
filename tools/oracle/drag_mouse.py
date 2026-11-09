"""The left button held and let go, with moves while it is held: a drag (an original-side module of the sessions that use the step; the
others do not import it, so their recordings keep their keys).

  ldown:<x>,<y>    taken as a click is (once the game is idle): the pointer at (x, y) and the left button down, as the driver's event
                   callback (root 047C:1395) leaves it: DS:6EF0 bit 0, the press word DS:6EF2, the press count and place of INT 33h
                   function 5. It is not let go by itself (a click is let go CLICK_MS after the game read the button).
  wait:<n>         while the button is held: n calls of the game's timer callback (root 047C:000A) pass before the next step.
  move:<x>,<y>     while held: the pointer goes there at the next timer callback.
  lup              while held: the button is let go at the next timer callback, where the pointer is (the release count and place of
                   function 6, DS:6EF0 bit 0 cleared).
A loop that waits for the release (the character sheet's list line, R031+0x1E42's `while (mouse-button-state & 1);`) polls no key, so
the steps while the button is held are taken at the timer callback, which its INT 8 handler calls whatever the game is doing.

The C# host (Darklands.Host.Headless ScriptedHost) has ldown / move / lup as steps of its own, a wait in its frames. lockstep.py wraps
orig_side.Session with session_class for a script with an ldown step and hashes this module with the original side.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orig_side  # noqa: E402

STEP = "ldown"


def uses(steps) -> bool:
    return any(kind == STEP for kind, _ in steps)


def session_class(base):
    """base (orig_side.Session or a subclass) with ldown, and move / wait / lup while the left button is held."""
    if STEP not in orig_side.INPUT_KINDS:
        # an input step for this process only (orig_side's _next_key then takes it as it takes a click: the pointer, the log line)
        orig_side.INPUT_KINDS = orig_side.INPUT_KINDS + (STEP,)

    class DragSession(base):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.left_held = False
            self.drag_wait = 0
            self.hook(*orig_side.TIMER_CALLBACK, self._drag_tick, record="root")

        def _next_key(self):
            if self.left_held:
                return None             # the steps while the button is held come at the timer callback
            # the base takes the non-input steps before the ldown (a state, a wait) and the ldown itself in one call: an ldown fewer is it
            before = sum(1 for kind, _ in self.script if kind == STEP)
            key = super()._next_key()
            if sum(1 for kind, _ in self.script if kind == STEP) < before:
                self._left_press()
            return key

        def _left_press(self):
            ds = orig_side.DS
            x, y = self.mouse[0], self.mouse[1]
            self.mouse[2] |= 1
            self.presses[0] += 1
            self.press_at[0] = (x, y)
            self.left_held = True
            self.drag_wait = 0
            self.put_word(ds, orig_side.MOUSE_BUTTON_STATE, self.word(ds, orig_side.MOUSE_BUTTON_STATE) | 1)
            self.put_word(ds, orig_side.MOUSE_PRESSES, self.word(ds, orig_side.MOUSE_PRESSES) | 1)

        def _drag_tick(self, m):
            if not self.left_held:
                return
            if self.drag_wait > 0:
                self.drag_wait -= 1
                return
            if not self.script:
                return
            kind, value = self.script[0]
            ds = orig_side.DS
            where = " < ".join(self.call_stack[-3:])
            if kind == "wait":
                self.script.pop(0)
                self.drag_wait = int(value)
            elif kind == "move":
                self.script.pop(0)
                x, y = (int(v) for v in value.split(","))
                self.put_word(ds, orig_side.POINTER_COLUMN, x)
                self.put_word(ds, orig_side.POINTER_ROW, y)
                self.mouse[0], self.mouse[1] = x, y
                self.log.append("move %s at %d ms (held) in %s" % (value, self.ms, where))
            elif kind == "lup":
                self.script.pop(0)
                self.mouse[2] &= ~1
                self.releases[0] += 1
                self.release_at[0] = (self.mouse[0], self.mouse[1])
                self.put_word(ds, orig_side.MOUSE_BUTTON_STATE, self.word(ds, orig_side.MOUSE_BUTTON_STATE) & ~1)
                self.left_held = False
                self.waits = 0
                self.idle_since = self.ms
                self.log.append("lup at %d ms in %s" % (self.ms, where))
            else:
                raise orig_side.Stop("%s while the left button is held (ldown): only wait, move and lup" % kind)

        def _get_key(self, m):
            if self.left_held:
                raise orig_side.Stop("a read that waits for a key while the left button is held (ldown): let it go first (lup)")
            return super()._get_key(m)

    return DragSession
