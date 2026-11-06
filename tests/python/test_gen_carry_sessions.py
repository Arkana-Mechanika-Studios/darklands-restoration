"""Unit tests of gen_carry_sessions.py: a walk that meets a road card in the original (ROAD_CARDS) ends in that card's fight, and the
other seeds' steps stay as the set holds them.

    tools/python/Scripts/python.exe -I -B tests/python/test_gen_carry_sessions.py
"""
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "oracle"))
import gen_carry_sessions as gen  # noqa: E402


class RoadCards(unittest.TestCase):
    def test_the_walk_that_meets_a_road_card_ends_in_its_fight(self):
        # gen-carry-06: its second walk to the mine meets card 0x24 in the original; the mine card's keys would land in that fight
        _, text = gen.generate(6)
        steps = text.splitlines()
        self.assertIn("road card", steps[0])
        self.assertEqual(gen.TO_MINE[:4] + ["bwait:800"], steps[-5:])
        self.assertEqual(2, steps.count(gen.TO_MINE[0]))

    def test_the_other_seeds_are_made_as_the_set_holds_them(self):
        for seed in (5, 7, 28):
            _, text = gen.generate(seed)
            held = (gen.SESSIONS_DIR / ("gen-carry-%02d.steps" % seed)).read_text(encoding="utf-8")
            self.assertEqual(held, text, seed)


if __name__ == "__main__":
    unittest.main()
