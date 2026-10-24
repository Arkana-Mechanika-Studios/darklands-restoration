"""Synthetic saved games for the oracle sessions: a real saved game of the game folder with its party's data edited, so that the screens' walks meet
data the original saves do not hold (the same way the DKSAVE40N copy is made). Both games load whatever the file holds.

usage: python -I -B synth_saves.py <variant> <source .SAV> <target .SAV>
variants:
  pots    each of the first four members' packs gets one stack of every potion (codes 0x69..0x74, type = code + 0x25) in each quality band of
          R031+0x43B2 (up to 25, 35, 45 and above 45), stack sizes 1 and 2, before the items the pack holds (the first lines); 12 stacks a member
  pots3   the same for three members (DKSAVE99's fourth member's picture is missing from the game folder)
  kitchen every item of DARKLAND.LST once over the first four members' packs (kitchen3: three), before the items they hold
  hurt    the first four members hurt and ill for the battle's party panel: endurance / strength at 1, 0 and above the maximum, a dead,
          an unconscious and a poisoned member (status byte +0x14)
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HEAD_END, CHAR = 0x189, 0x22A
PACK, PACK_COUNT, ENTRY = 0xAA, 0x7E, 6
POTIONS = list(range(0x69, 0x75))
BANDS = (20, 30, 40, 60)


def potion_entry(code: int, quality: int, count: int) -> bytes:
    return struct.pack("<HBBBB", code, code + 0x25, quality, count, 1)


def pots(b: bytearray, members: int = 4) -> None:
    n = struct.unpack_from("<H", b, 0xF1)[0]
    stacks = [(c, q) for q in BANDS for c in POTIONS]         # 48 stacks, spread over the first members
    for member in range(min(members, n)):
        base = HEAD_END + member * CHAR
        count = struct.unpack_from("b", b, base + PACK_COUNT)[0]
        mine = [s for k, s in enumerate(stacks) if k % members == member][:16]
        old = [bytes(b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)]) for i in range(count)]
        new = [potion_entry(code, quality, 1 + (code + quality) % 2) for code, quality in mine] + old        # the potions first: the first lines
        new = new[:64]
        for i, e in enumerate(new):
            b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)] = e
        b[base + PACK_COUNT] = len(new)


LST = Path(__file__).resolve().parent.parent.parent.parent / "game" / "DARKLAND" / "DARKLAND.LST"


def kitchen(b: bytearray, members: int = 4) -> None:
    """Every item of DARKLAND.LST once, spread over the members' packs (member k: codes k, k + members, ...), quality cycling through the
    default, 20, 40, 60, 80, count 1..3, the list's weight and type; the packs' own items are kept after them."""
    lst = LST.read_bytes()
    items = lst[0]
    n = struct.unpack_from("<H", b, 0xF1)[0]
    for member in range(min(members, n)):
        base = HEAD_END + member * CHAR
        count = struct.unpack_from("b", b, base + PACK_COUNT)[0]
        old = [bytes(b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)]) for i in range(count)]
        new = []
        for code in range(member, items, members):
            at = 3 + code * 0x2E
            quality = (lst[at + 0x26], 20, 40, 60, 80)[code % 5]
            new.append(struct.pack("<HBBBB", code, lst[at + 0x1E], quality, 1 + code % 3, lst[at + 0x25]))
        new = (new + old)[:64]
        for i, e in enumerate(new):
            b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)] = e
        b[base + PACK_COUNT] = len(new)


ATTR, ATTR_MAX, STATUS = 0x5D, 0x64, 0x14


def hurt(b: bytearray) -> None:
    """The party panel of a battle with a hurt party (members 1..4 of a party of five): member 1 at 3 endurance and 2 strength, member 2 with
    endurance 25 above its maximum and no divine favour, member 3 with strength 0, member 4 unconscious (status 2)."""
    def member(k: int) -> int:
        return HEAD_END + k * CHAR

    b[member(1) + ATTR + 0] = 3
    b[member(1) + ATTR + 1] = 2
    b[member(2) + ATTR + 0] = min(255, b[member(2) + ATTR_MAX + 0] + 25)
    b[member(2) + ATTR + 6] = 0
    b[member(3) + ATTR + 1] = 0
    b[member(4) + STATUS] = 2


def hurt_attributes(b: bytearray) -> None:
    """hurt without the status byte: only the attribute extremes."""
    hurt(b)
    b[HEAD_END + 4 * CHAR + STATUS] = 1


def dead(b: bytearray) -> None:
    """member 4 dead (status 3)."""
    b[HEAD_END + 4 * CHAR + STATUS] = 3


def away(b: bytearray) -> None:
    """member 4 away (status 5)."""
    b[HEAD_END + 4 * CHAR + STATUS] = 5


def hurt_status(b: bytearray) -> None:
    """hurt's status byte alone: member 4 unconscious."""
    b[HEAD_END + 4 * CHAR + STATUS] = 2


VARIANTS = {"dead": dead, "away": away, "hurtattr": hurt_attributes, "hurtstatus": hurt_status, "kitchen": kitchen, "kitchen3": lambda b: kitchen(b, 3), "pots": pots, "pots3": lambda b: pots(b, 3), "hurt": hurt}


def main() -> None:
    variant, source, target = sys.argv[1:4]
    b = bytearray(Path(source).read_bytes())
    VARIANTS[variant](b)
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    Path(target).write_bytes(bytes(b))


if __name__ == "__main__":
    main()
