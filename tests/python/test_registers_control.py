"""The faithfulness registers refuse a marker holding a raw control character (2026-11-25: a backslash-b in a source comment had
become a backspace byte and gone into docs/faithfulness/dormant.csv unseen).

    tools/python/Scripts/python.exe -I -B tests/python/test_registers_control.py
"""
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "faithfulness"))
import registers  # noqa: E402


class RefuseControlCharacters(unittest.TestCase):
    def test_a_plain_note_passes(self):
        registers.refuse_control_characters([SimpleNamespace(file="a.cs", line=3, note="pics" + chr(92) + "button4.pic is loaded")])

    def test_tabs_and_line_ends_pass(self):
        registers.refuse_control_characters([SimpleNamespace(file="a.cs", line=3, note="one" + chr(9) + "two" + chr(13) + chr(10) + "three")])

    def test_a_control_character_is_refused_with_its_place(self):
        with self.assertRaises(SystemExit) as stop:
            registers.refuse_control_characters([
                SimpleNamespace(file="ok.cs", line=1, note="fine"),
                SimpleNamespace(file="src/StartScreen.cs", line=20, note="pics" + chr(8) + "utton4.pic"),
            ])
        self.assertIn("src/StartScreen.cs:20", str(stop.exception))
        self.assertNotIn("ok.cs", str(stop.exception))


if __name__ == "__main__":
    unittest.main()
