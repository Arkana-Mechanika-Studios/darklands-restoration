"""Keys given at a timer tick, whatever the game is doing (an original-side module of the sessions that use the step; the others do not
import it, so their recordings keep their keys).

  tkey:<n>:<key>[,<key>...]   once the step is the script's next one (the step before it was taken), the game's timer callback (root
                              047C:000A, called by its INT 8 handler) runs n more times; then the keys are in the keyboard buffer at once,
                              in order: the game's next look (key-waiting-test 047C:073D) finds the first, a read takes it, and each read
                              after it finds the next without the idle wait a `key:` step has. A key pressed in a loop that does not look at
                              the keyboard (the map's rest, R030+0x0C44: delay, a step's time, delay, then the test), or two keys pressed
                              before one pass reads them.

The C# host (Darklands.Host.Headless ScriptedHost) presses them n / 60 s later on its own clock (the callback's six calls in seven
frames of 70.086 Hz). lockstep.py swaps orig_side.Session for TickKeySession for a script with a tkey step and hashes
this module with the original side.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orig_side  # noqa: E402

STEP = "tkey"


def uses(steps) -> bool:
    return any(kind == STEP for kind, _ in steps)


def parse(value: str) -> tuple[int, list[str]]:
    n, _, keys = value.partition(":")
    return int(n), [k for k in keys.split(",") if k]


class TickKeySession(orig_side.Session):
    """A Session with the tkey step: counted down at every call of the game's timer callback, then turned into key steps the game finds
    in its keyboard buffer at once."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.tick_keys_left = 0
        self.hook(*orig_side.TIMER_CALLBACK, self._tick_key, record="root")

    def _tick_key(self, m):
        if not self.script or self.script[0][0] != STEP:
            return
        n, keys = parse(self.script[0][1])
        if n > 0:
            self.script[0] = (STEP, "%d:%s" % (n - 1, ",".join(keys)))
            return
        self.script[0:1] = [("key", k) for k in keys]
        self._buffered = True
        self.tick_keys_left = len(keys) - 1
        self.log.append("tkey %s in the buffer at %d ms (battle pass %d) in %s" % (",".join(keys), self.ms, self.battle_passes, " < ".join(self.call_stack[-3:])))

    def _next_key(self):
        if self.script and self.script[0][0] == STEP:
            return None             # not pressed yet: nothing waits
        return super()._next_key()

    def _get_key(self, m):
        if self.script and self.script[0][0] == STEP:
            # a read that waits for a key (047C:0744) before the tick: the game waits until the keys come (the emulated time stands here,
            # as at any blocking read of the tester)
            self.script[0] = (STEP, "0:" + ",".join(parse(self.script[0][1])[1]))
            self._tick_key(m)
        out = super()._get_key(m)
        if self.tick_keys_left > 0:
            self.tick_keys_left -= 1
            self._buffered = True       # the next key pressed at the same tick waits in the buffer too
        return out
