"""Unit tests of lockstep.py's waits for the C# host: the start pause's keys (pause_keys, the host's still:<n>) and the passes after a save's
or load's dialog when the original stopped before the script's end (dialog_gaps).

    tools/python/Scripts/python.exe -I -B tests/python/test_lockstep_waits.py
"""
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "oracle"))
import lockstep  # noqa: E402
import orig_side  # noqa: E402


def key(code, ms, battle_pass, clock, where):
    return "key %s at %d ms (battle pass %d, clock %d) in %s" % (code, ms, battle_pass, clock, where)


class PauseKeys(unittest.TestCase):
    def test_a_battle_begun_after_a_load_gets_the_pause_rule_in_passes(self):
        # Alt-L's L (outside any battle), the load, the battle's 8 running passes, then its start pause: the original gives the first key
        # after BATTLE_PAUSE_PASSES passes at the pause's clock; the host is told to wait as many passes (one less for the state step,
        # which costs it a frame)
        steps = [("key", "l"), ("wait", "600"), ("state", "b2"), ("key", "enter")]
        trace = ["pass clock 100 rand 0"] * 11 + ["pass clock %d rand 0" % c for c in range(634, 642)] + ["pass clock 642 rand 0"] * 400
        first = 11 + 8
        log = [key("266C", 1, 10, 5, "R037+0x8B76"), key("1C0D", 2, first + orig_side.BATTLE_PAUSE_PASSES, 642, "R016+0x0000")]
        self.assertEqual({1: orig_side.BATTLE_PAUSE_PASSES - 1}, lockstep.pause_keys(log, steps, trace))

    def test_a_key_not_given_by_the_pause_rule_keeps_its_wait(self):
        steps = [("key", "l"), ("wait", "600"), ("key", "enter")]
        trace = ["pass clock 642 rand 0"] * 400
        log = [key("266C", 1, 0, 5, "R037+0x8B76"), key("1C0D", 2, 100, 642, "R016+0x0000")]
        self.assertEqual({}, lockstep.pause_keys(log, steps, trace))


class DialogGaps(unittest.TestCase):
    STEPS = [("key", "alts"), ("wait", "100"), ("key", "enter"), ("wait", "300"), ("bwait", "20"), ("key", "up"), ("bwait", "7"),
             ("key", "altl"), ("wait", "100"), ("key", "y"), ("wait", "300"), ("key", "l"), ("wait", "600"), ("bwait", "20"),
             ("key", "space"), ("bwait", "5"), ("option", "1"), ("wait", "5"), ("key", "space"), ("bwait", "7"), ("key", "space"),
             ("bwait", "7"), ("key", "alts"), ("wait", "100"), ("key", "enter"), ("wait", "300"), ("bwait", "20"), ("key", "space")]
    LOG = [key("1F00", 1, 100, 2368, "R014+0x0000"), key("1C0D", 2, 100, 2368, "R037+0x2A68"), key("4800", 3, 767, 41622, "R016+0x0000"),
           key("2600", 4, 775, 41630, "R014+0x0000"), key("1579", 5, 775, 41630, "R037+0x8B76"), key("266C", 6, 775, 44127, "R037+0x8B76"),
           key("3920", 7, 1072, 5023, "R016+0x0000")]

    def tearDown(self):
        lockstep.ORIGINAL_END[:] = []

    def test_an_original_that_stopped_early_keeps_the_gaps_of_the_dialogs_it_reached(self):
        # the original's run ended at the option step (no menu): the bwaits after it are not logged, the last Alt-S never came
        lockstep.ORIGINAL_END[:] = ["end: option 1: the menu has 0 lines"]
        self.assertEqual([667, 297], lockstep.dialog_gaps(self.LOG, self.STEPS, 2))

    def test_a_last_dialog_with_no_key_after_it_still_pairs(self):
        # gen-carry-26: the script's last Alt-L ends in a state step (the game is played on without another key), so the log's last key is
        # read in the load dialog; the dialogs before it keep their gaps
        lockstep.ORIGINAL_END[:] = ["end: script ended"]
        steps = self.STEPS[:-6] + [("key", "altl"), ("wait", "100"), ("key", "y"), ("wait", "300"), ("key", "l"), ("wait", "600"), ("state", "b2")]
        log = self.LOG + [key("266C", 8, 1100, 6000, "R037+0x8B76")]
        self.assertEqual([667, 297], lockstep.dialog_gaps(log, steps, sum(1 for s in steps if s[0] == "bwait") - 2))

    def test_an_original_that_ran_the_whole_script_must_log_every_dialog(self):
        lockstep.ORIGINAL_END[:] = ["end: script ended"]
        self.assertEqual([], lockstep.dialog_gaps(self.LOG, self.STEPS, 2))


class LootTicks(unittest.TestCase):
    def test_two_battles_opening_their_loot_screens_at_one_clock_give_each_its_own_ticks(self):
        # gen-carry-17: the mine's ambush won at clock 2090 in two battles of the session (the pass heads 2089 -> 2096 each): both loot screens
        # take their 6 ticks, none is left in the second battle's pass jump (which would move the second's fight-recovery to 2096)
        trace = ["pass clock %d rand 0" % c for c in (2088, 2089, 2096, 2097, 700, 2088, 2089, 2096)]
        jumps = [(2089, 6), (2089, 6)]
        battles = lockstep.jump_battles(jumps, trace)
        log = [key("266C", 1, 2, 2090, "R002+0x0000 < R014+0x0000 < R040+0x0000"),
               key("266C", 2, 7, 2090, "R037+0x8B76 < R002+0x0000 < R014+0x0000 < R040+0x0000")]
        out, loot = lockstep.loot_ticks(jumps, log, battles, trace)
        self.assertEqual([0, 1], battles)
        self.assertEqual([(2090, 6), (1 << 16 | 2090, 6)], loot)
        self.assertEqual([(2089, 0), (1 << 16 | 2089, 0)], out)

    def test_a_loot_screen_of_one_battle_does_not_take_another_battles_jump(self):
        trace = ["pass clock %d rand 0" % c for c in (2088, 2089, 2096, 700, 2088, 2089, 2091)]
        jumps = [(2089, 6), (2089, 1)]
        battles = lockstep.jump_battles(jumps, trace)
        log = [key("266C", 1, 6, 2090, "R002+0x0000 < R014+0x0000 < R040+0x0000")]
        out, loot = lockstep.loot_ticks(jumps, log, battles, trace)
        self.assertEqual([(1 << 16 | 2090, 1)], loot)
        self.assertEqual([(2089, 6), (1 << 16 | 2089, 0)], out)


class SaveWraps(unittest.TestCase):
    def test_a_save_that_carries_the_clock_past_0xffff_is_a_jump_of_its_battle(self):
        # gen-carry-14: the in-battle Alt-S's Enter at battle pass 3 (head 41365), the next head 14135: 38305 callbacks, the 16-bit clock wrapped
        trace = ["pass clock %d rand 0" % c for c in (900, 41364, 41365, 14135, 14136, 14140)]
        jumps = [(14136, 3)]
        log = [key("1F00", 1, 3, 41366, "R002+0x0000 < R014+0x0000"), key("1C0D", 2, 3, 41366, "R002+0x0000 < R014+0x0000 < R037+0x2A68")]
        got = lockstep.save_wraps(jumps, trace, log)
        self.assertEqual([(41365, 38305), (14136, 3)], got)
        # the wrap is a fall of the clock as the C# host counts its battles: the jumps after it are keyed with the next battle
        self.assertEqual([0, 1], lockstep.jump_battles(got, trace))

    def test_a_new_battle_after_a_fall_of_the_clock_is_no_wrap(self):
        trace = ["pass clock %d rand 0" % c for c in (41364, 41365, 700, 701)]
        log = [key("266C", 1, 2, 41366, "R037+0x8B76")]
        self.assertEqual([], lockstep.save_wraps([], trace, log))


if __name__ == "__main__":
    unittest.main()
