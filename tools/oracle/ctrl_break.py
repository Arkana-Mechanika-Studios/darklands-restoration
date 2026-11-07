"""Ctrl-Break, as the BIOS delivers it (an original-side module of the sessions that use the step; the others do not import it, so
their recordings keep their keys).

  kbreak:<n>[:<key>]          once the step is the script's next one (the step before it was taken), the game's timer callback (root
                              047C:000A, called by its INT 8 handler) runs n more times; then Ctrl-Break: the BIOS keyboard interrupt
                              empties the buffer (0040:001A = 0040:001C), sets bit 7 of 0040:0071, puts the key word 0000h in the
                              buffer and runs INT 1Bh through the vector table (the game's handler root 044A:013F sets DS:07F4 = FFh).
                              key-waiting-test (047C:073D: INT 16h function 1, `je` on ZF, else AX) gives 0 for that word, as for an
                              empty buffer, and so for every key behind it; read-key (047C:0744: INT 16h function 0) takes it at once.
                              With <key>, the key is pressed first (the looks find it), and the Ctrl-Break interrupt comes just before
                              the read that would take it: the key is flushed and the read takes 0000h (a loop's `if (test()) key =
                              read()` gets key 0: the map's R030+0x0540..0x0566 and so R030+0x0C44 with key 0, 0x12E7 taken). Without
                              <key> the interrupt is taken at the game's next look or read of the keyboard (the flag is only read after
                              one: quit-check root 0000:1338 in press-to-continue R043+0x3343 and card-choice-menu R043+0x1DEB).

The C# host (Darklands.Host.Headless ScriptedHost) does the same with IInput.TakeCtrlBreak at GameUi's looks and reads, n / 60 s later
on its own clock (as tkey's keys). lockstep.py wraps orig_side.Session with session_class for a script with a kbreak step and hashes
this module with the original side.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import orig_side  # noqa: E402

STEP = "kbreak"
BREAK_VECTOR = 0x1B
BIOS_BREAK = (0x40, 0x71)      # 0040:0071, bit 7: Ctrl-Break was pressed (the BIOS sets it, nothing clears it)
QUIT_FLAG = 0x07F4             # DS:07F4, set by the game's INT 1Bh handler (root 044A:013F), read by quit-check root 0000:1338


def uses(steps) -> bool:
    return any(kind == STEP for kind, _ in steps)


def parse(value: str) -> tuple[int, str]:
    n, _, key = value.partition(":")
    return int(n), key


def session_class(base):
    """base (orig_side.Session or a subclass) with the kbreak step."""

    class CtrlBreakSession(base):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            self.break_head = False         # the key word 0000h at the buffer's head
            self.break_key = None           # a kbreak's key pressed, not yet looked at or read
            self.break_due = False          # the interrupt came: the game's next look or read finds its effect
            self.break_flush = 0            # the keys that were in the buffer when it came
            self.hook(*orig_side.TIMER_CALLBACK, self._tick_break, record="root")

        def _waiting(self) -> int:
            # the keys in the buffer: the script's key the last look found (orig_side's _buffered) and a tkey's keys not read yet
            return max(1 if self._buffered else 0, getattr(self, "tick_keys_waiting", 0))

        def _interrupt(self):
            self.break_due = True
            self.break_flush = self._waiting()

        def _tick_break(self, m):
            # (a tick inside a state's capture counts too, as for tkey: the capture runs where the game is idle)
            if not self.script or self.script[0][0] != STEP:
                return
            n, key = parse(self.script[0][1])
            if n > 0:
                self.script[0] = (STEP, "%d:%s" % (n - 1, key) if key else str(n - 1))
                return
            if key:
                self.script[0] = ("key", key)
                self._buffered = True
                self.break_key = orig_side.key_code(key)
                self._break_name = key
                self.log.append("kbreak %s pressed at %d ms in %s" % (key, self.ms, " < ".join(self.call_stack[-3:])))
            else:
                self.script.pop(0)
                self._interrupt()
                self.log.append("kbreak at %d ms in %s" % (self.ms, " < ".join(self.call_stack[-3:])))

        def _ctrl_break(self, m):
            """The BIOS keyboard interrupt for Ctrl-Break (E0 46): the buffer emptied, 0040:0071 bit 7, 0000h stored, INT 1Bh."""
            self.break_due = False
            flushed = []
            while len(flushed) < self.break_flush and self.script and self.script[0][0] == "key":
                flushed.append(self.script.pop(0)[1])
            if flushed:
                self._buffered = False
                if hasattr(self, "tick_keys_waiting"):
                    self.tick_keys_waiting = self.tick_keys_left = 0
            self.write(*BIOS_BREAK, bytes([self.byte(*BIOS_BREAK) | 0x80]))
            self.break_head = True
            seg, off = self.vector(BREAK_VECTOR)
            self.call(seg, off, [(self.uc.reg_read(orig_side.REG_EFLAGS) | 0x200) & 0xFFFF])
            self.log.append("ctrl-break at %d ms (flushed %s; INT 1Bh %04X:%04X, DS:07F4 = %02X) in %s" % (
                self.ms, ",".join(flushed) or "nothing", seg, off, self.byte(orig_side.DS, QUIT_FLAG), " < ".join(self.call_stack[-3:])))

        def _next_key(self):
            if self.script and self.script[0][0] == STEP:
                return None             # not pressed yet: nothing waits
            return super()._next_key()

        def _key_pressed(self, m):
            if self.break_due:
                self._ctrl_break(m)
            if self.break_head:
                return (0, 0)           # INT 16h function 1 finds 0000h: AX = 0, as for no key
            out = super()._key_pressed(m)
            if self.break_due:
                # the interrupt came inside this look (a tick in a state's capture): it is ahead of the key the look would find
                self._ctrl_break(m)
                return (0, 0)
            return out

        def _get_key(self, m):
            if self.break_key is not None and self.script and self.script[0] == ("key", self._break_name):
                # the read that would take the kbreak's key: the interrupt comes just before it
                self.break_key = None
                self._interrupt()
            if self.break_due:
                self._ctrl_break(m)
            if self.break_head:
                self.break_head = False
                self.waits = 0
                self.idle_since = self.ms
                self.log.append("key 0000 at %d ms (battle pass %d, clock %d) in %s" % (
                    self.ms, self.battle_passes, self.word(orig_side.DS, orig_side.FIGHT_CLOCK), " < ".join(self.call_stack[-4:])))
                return (0, 0)
            out = super()._get_key(m)
            if self.break_key is not None and out[0] == self.break_key:
                # a read that waited for the key (it was not in the buffer yet at the read's start): the interrupt after it
                self.break_key = None
                self._interrupt()
            return out

    return CtrlBreakSession
