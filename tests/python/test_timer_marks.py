"""Unit tests of the opt-in timer marks (tools/oracle/timer_marks.py, lockstep.py --timer-marks): where the original's timer callbacks
landed inside the endurance regain's loop (regain_rises), trigger-check's clock read (the rise -8 of clock_jumps) and the pile filler's
stale word (pile_words); and that the default run's key does not depend on the module.

    tools/python/Scripts/python.exe -I -B tests/python/test_timer_marks.py
"""
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "oracle"))
import lockstep  # noqa: E402
import orig_side  # noqa: E402
import recording  # noqa: E402


def trace(*clocks):
    return ["pass clock %d rand 0" % c for c in clocks]


class RegainRises(unittest.TestCase):
    def test_a_callback_inside_the_loop_after_a_loot_screen_comes_off_the_loot_ticks(self):
        # gen-carry-11: head 2089, the end test's loot screen at 2090, the regain read 2097 for member 0 and 2098 for members 1..3, the next
        # head 2098: of the 8 ticks the loot screen had, 1 is given before member 1's read
        t = trace(2088, 2089, 2098, 2099)
        jumps, loot = [(2089, 0)], [(2090, 8)]
        log = ["regain clocks 2097,2098,2098,2098,-1 then 2098 (battle pass 2)"]
        j, l, r = lockstep.regain_rises(jumps, loot, log, t)
        self.assertEqual([(2089, 0)], j)
        self.assertEqual([(2090, 7)], l)
        self.assertEqual([(2097, {1: 1})], r)

    def test_without_a_loot_screen_the_rise_comes_off_the_jumps_ticks_after_the_pass(self):
        t = trace(500, 501, 530)
        j, l, r = lockstep.regain_rises([(501, 28, "3:2")], [], ["regain clocks 528,528,529,-1,529 (battle pass 2)"], t)
        self.assertEqual([(501, 27, "3:2")], j)
        self.assertEqual([], l)
        self.assertEqual([(528, {2: 1})], r)

    def test_a_later_battles_regain_is_keyed_by_its_battle(self):
        # the clock went back at the third head: the second battle's regain before its fourth head
        t = trace(3000, 3001, 100, 101, 140)
        j, l, r = lockstep.regain_rises([(1 << 16 | 101, 38)], [], ["regain clocks 139,140,140,140,-1 (battle pass 4)"], t)
        self.assertEqual([(1 << 16 | 101, 37)], j)
        self.assertEqual([(1 << 16 | 139, {1: 1})], r)

    def test_a_callback_after_the_loop_is_given_after_the_last_read(self):
        # gen-carry-33: the loot screen at 3906, every member's read 3913, the clock 3914 before fight-input
        t = trace(3904, 3905, 3914)
        j, l, r = lockstep.regain_rises([(3905, 0)], [(3906, 8)], ["regain clocks 3913,3913,3913,3913,-1 then 3914 (battle pass 2)"], t)
        self.assertEqual([(3906, 7)], l)
        self.assertEqual([(3913, {lockstep.REGAIN_AFTER: 1})], r)

    def test_a_regain_with_no_jump_to_take_from_is_not_given(self):
        j, l, r = lockstep.regain_rises([], [], ["regain clocks 10,11,11,11,-1 (battle pass 1)"], trace(10, 11))
        self.assertEqual(([], [], []), (j, l, r))


class TriggerCheck(unittest.TestCase):
    def test_the_check_s_clock_read_is_a_rise_of_its_own_before_the_scheduler(self):
        # gen-carry-12: the save's pass (head 2329) read the clock for the activations test at 40756, the check's end read 40757, the
        # tick's scheduler 40757, the next head 40821
        t = orig_side.ClockTrace()
        t.append("pass clock 2329 rand 0")
        t.phase_clocks += [(-7, 40756), (-8, 40757), (-1, 40757)]
        t.append("pass clock 40821 rand 0")
        self.assertEqual([(2329, 40821 - 40757, "-7:%d" % (40756 - 2330), "-8:1")], orig_side.clock_jumps(t))


class PileWords(unittest.TestCase):
    def test_the_words_in_order(self):
        log = ["srand 3341 at 1 ms in x", "pile word 0000", "pile word 5E37", "card 3 at 5 ms", "pile word 29CB"]
        self.assertEqual([0, 0x5E37, 0x29CB], lockstep.pile_words(log))


class DefaultKey(unittest.TestCase):
    def test_the_default_run_does_not_hash_the_module(self):
        # the original side's sources of a default session: orig_side and what it imports, never timer_marks (so no recording expires)
        self.assertNotIn("timer_marks", recording.source_hashes(("orig_side",)))
        self.assertIn("timer_marks", recording.source_hashes(("orig_side", "timer_marks")))


if __name__ == "__main__":
    unittest.main()
