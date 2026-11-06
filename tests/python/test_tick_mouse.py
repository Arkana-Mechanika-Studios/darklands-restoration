"""Unit tests of the opt-in tclick step (tools/oracle/tick_mouse.py: a left button pressed at a timer tick, lockstep.py): the countdown at
the timer callback, the press as the driver leaves it, the hold the C# host is given; and that no other run's key depends on the module.

    tools/python/Scripts/python.exe -I -B tests/python/test_tick_mouse.py
"""
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "oracle"))
import orig_side  # noqa: E402
import recording  # noqa: E402
import tick_mouse  # noqa: E402


class FakeSession:
    """What tick_mouse's session uses of orig_side.Session: the script, the mouse fields, DGROUP words, the release."""

    def __init__(self, script):
        self.script = list(script)
        self.words = {}
        self.hooks = []
        self.log = []
        self.call_stack = ["R030+0x0C44"]
        self.mouse = [160, 100, 0]
        self.presses, self.press_at = [0, 0], [None, None]
        self.held, self.held_bit, self.button_read, self.map_reads, self.press_ms, self.rheld = False, 1, False, 0, 0, False
        self.ms = 1000
        self.released = 0

    def hook(self, seg, off, handler, record=None):
        self.hooks.append(((seg, off), handler))

    def put_word(self, seg, off, value):
        self.words[off] = value & 0xFFFF

    def word(self, seg, off):
        return self.words.get(off, 0)

    def _release(self):
        self.held = False
        self.released += 1

    def _next_key(self):
        return 0x1E61

    def _get_key(self, m):
        return (0x1E61, 0)


Session = tick_mouse.session_class(FakeSession)


def tick(s, n=1):
    handler = next(h for at, h in s.hooks if at == orig_side.TIMER_CALLBACK)
    for _ in range(n):
        handler(s)


class TickClick(unittest.TestCase):
    def test_the_button_is_down_after_n_more_callbacks(self):
        s = Session([("tclick", "2:30,185"), ("wait", "300")])
        tick(s, 2)
        self.assertEqual([("tclick", "0:30,185"), ("wait", "300")], s.script)
        self.assertFalse(s.held)
        self.assertIsNone(s._next_key())         # nothing waits while the press is due
        tick(s)
        self.assertEqual([("wait", "300")], s.script)
        self.assertTrue(s.held)
        self.assertEqual(1, s.words[orig_side.MOUSE_BUTTON_STATE])
        self.assertEqual(1, s.words[orig_side.MOUSE_PRESSES])
        self.assertEqual((30, 185), (s.words[orig_side.POINTER_COLUMN], s.words[orig_side.POINTER_ROW]))
        self.assertEqual([1, 0], s.presses)

    def test_the_callbacks_before_the_step_is_next_do_not_count(self):
        s = Session([("key", "a"), ("tclick", "1:30,185")])
        tick(s, 5)
        self.assertEqual([("key", "a"), ("tclick", "1:30,185")], s.script)

    def test_the_release_logs_the_hold(self):
        s = Session([("tclick", "0:30,185")])
        tick(s)
        s.ms += 382
        s._release()
        self.assertEqual([382], tick_mouse.holds(s.log))

    def test_a_blocking_read_presses_it_at_once(self):
        s = Session([("tclick", "9:30,185"), ("key", "a")])
        s._get_key(s)
        self.assertEqual([("key", "a")], s.script)
        self.assertTrue(s.held)


class DefaultKey(unittest.TestCase):
    def test_only_a_tclick_run_hashes_the_module(self):
        self.assertFalse(tick_mouse.uses([("key", "a"), ("tkey", "1:z")]))
        self.assertTrue(tick_mouse.uses([("tclick", "1:30,185")]))
        self.assertNotIn("tick_mouse", recording.source_hashes(("orig_side",)))
        self.assertNotIn("tick_mouse", recording.source_hashes(("orig_side", "tick_keys")))
        self.assertIn("tick_mouse", recording.source_hashes(("orig_side", "tick_mouse")))


if __name__ == "__main__":
    unittest.main()
