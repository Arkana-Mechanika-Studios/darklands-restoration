"""Unit tests of savdiff.bsv_diff's words reported apart in a battle file saved outside a battle (the clock, the members' recovery times).

    tools/python/Scripts/python.exe -I -B tests/python/test_savdiff_bsv.py
"""
import struct
import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "oracle"))
import savdiff  # noqa: E402

VARS = 0x1978


def bsv(clock, recover, in_battle=0, extra=None):
    v = bytearray(VARS)
    v[0] = in_battle
    struct.pack_into("<H", v, savdiff.BSV_CLOCK, clock)
    for i, r in enumerate(recover):
        struct.pack_into("<H", v, savdiff.BSV_RECOVER + 2 * i, r)
    for at, b in (extra or {}).items():
        v[at] = b
    return struct.pack("<21H", VARS, *([0] * 20)) + bytes(v)


class OutOfBattleRecoveryTimes(unittest.TestCase):
    def setUp(self):
        # the blocks as they are (no implode): bsv_diff explodes each block
        self.explode = savdiff.explode
        savdiff.explode = lambda data: data

    def tearDown(self):
        savdiff.explode = self.explode

    def test_the_battle_drivers_fight_recovery_after_the_end_screens_is_reported_apart(self):
        # gen-carry-18: the C# host's words are its battle's last clock (0x08E5), the original's the clock its timer had reached at R002+0x05C1
        d = savdiff.bsv_diff(bsv(0x08E5, [0x08E5] * 5), bsv(0x14F0, [0x0A00] * 5))
        self.assertEqual({"kept": {"clock": "08E5/14F0", "recover": "08E5/0A00"}}, d)

    def test_in_a_battle_they_are_differences(self):
        d = savdiff.bsv_diff(bsv(2354, [4798] * 4 + [2090], 1), bsv(2354, [4797] + [4798] * 3 + [2090], 1))
        self.assertEqual("1849", d["vars"]["first"])

    def test_words_written_since_the_fight_recovery_are_differences(self):
        # a member's recovery not the one write of the battle's last clock (the endurance regain ran after it): not the end screens' time
        d = savdiff.bsv_diff(bsv(0x08E5, [0x08E5] * 4 + [0x1000]), bsv(0x14F0, [0x0A00] * 5))
        self.assertIn("vars", d)


if __name__ == "__main__":
    unittest.main()
