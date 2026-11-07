"""Synthetic saved games for the oracle sessions: a real saved game of the game folder with its party's data edited, so that the screens' walks meet
data the original saves do not hold (the same way the DKSAVE40N copy is made). Both games load whatever the file holds.

usage: python -I -B synth_saves.py <variant> <source .SAV> <target .SAV>
variants:
  pots    each of the first four members' packs gets one stack of every potion (codes 0x69..0x74, type = code + 0x25) in each quality band of
          R031+0x43B2 (up to 25, 35, 45 and above 45), stack sizes 1 and 2, before the items the pack holds (the first lines); 12 stacks a member
  pots3   the same for three members (DKSAVE99's fourth member's picture is missing from the game folder)
  kitchen every item of DARKLAND.LST once over the first four members' packs (kitchen3: three), before the items they hold
  weak<card>, card<card>  controls: as away<card> without the Away member / with the party unchanged (any card, hex)
  away<card>  (24, 7b, a0, 16, 7a, a5) member 4 Away, members 0..3 at endurance 1, the game saved at card <card> (0x24 Footpads, 0x7B RebelMobInTheStreets, 0xA0 WolfPackOnTheTrail, 0x16 MarketAtNight ...): see local/batch/res5
  dup<a><b>  party slot <b> names the record of slot <a> (party id word +0xF3 + 2b = that of slot a): two slots naming one record
  ev300   the event table full: the game's events and fillers (a place fact of type 0x7F at place 413) up to the 300 slots (full_events)
  month<N>[d<D>]  the date moved to month N (and day D): the seasons of the travel map
  pos<column>x<row>  the party moved to map cell (column, row) (header words +0x7E / +0x80); the .BSV beside the source is copied: the starts of the travel map's edge and paging walks
  out3    member 3 unconscious (status 2), a member within the party size who is not able
  sea     the party moved onto the open sea at map cell (72, 200): column 72 is deep water (ocean) from row 201 up to the map's top, so a walk
          north meets no encounter roll (a step onto deep water ends before it) and pages the map's row buffer backwards with its top
          past 0x41 (R030+0x0C44's 11D8 arm); the month moved to 6 (the walk's two weeks end in month 7)
  hurt    the first four members hurt and ill for the battle's party panel: endurance / strength at 1, 0 and above the maximum, a dead,
          an unconscious and a poisoned member (status byte +0x14)
  king, stonebox  a game saved in the mine of Rammelsburg's battle (the original's DKSAVE2 of savemid-mine, with its .BSV) moved to level 3
          before the dwarf king's riddle door / to level 6 before the stone box's door (battle_level; the .BSV is written beside the target)
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))      # savdiff (explode) beside this file

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


def out3(b: bytearray) -> None:
    """member 3 unconscious (status 2): a party slot within the party size that is not able (the map's Ctrl-F4, R030+0x0EEC)."""
    b[HEAD_END + 3 * CHAR + STATUS] = 2


def sea(b: bytearray) -> None:
    """the party at map cell (72, 200) (header +0x7E / +0x80), out on the sea: world-map-start puts the view's buffer top at 124 and its
    first shown row at 60 below it (the least it makes for a row of 200 or more), the marker standing on row 201. The month is moved from
    5 to 6 (header +0x6A): the walk's two weeks then end in month 7, not 6, whose start (a month divisible by 3) would run the ruined
    robber-knight castle search from locations[-1] in the wilderness (undefined memory, D-c22bd4)."""
    struct.pack_into("<HH", b, 0x7E, 72, 200)
    struct.pack_into("<H", b, 0x6A, 6)


def away(b: bytearray) -> None:
    """member 4 away (status 5)."""
    b[HEAD_END + 4 * CHAR + STATUS] = 5


def hurt_status(b: bytearray) -> None:
    """hurt's status byte alone: member 4 unconscious."""
    b[HEAD_END + 4 * CHAR + STATUS] = 2


CARD_AT, SAVED_CARD_AT, ENDURANCE = 0x82, 0x86, 0x5D


def away_card(card: int, weak: bool = True, gone: bool = True):
    """member 4 away (status 5), the game saved at card <card> (a battle card: loading it starts the card), members 0..3 at endurance 1 so
    that a card fight lost on the floor leaves nobody able and battle result 5 reaches the card (the Away member keeps the party alive)."""
    def edit(b: bytearray) -> None:
        if gone:
            away(b)
        saved = struct.unpack_from("<H", b, CARD_AT)[0]          # the card the game was saved at: where the card goes on afterwards
        struct.pack_into("<H", b, CARD_AT, card)
        struct.pack_into("<H", b, SAVED_CARD_AT, saved)
        if weak:
            for k in range(4):
                b[HEAD_END + k * CHAR + ENDURANCE] = 1
    return edit


def marching_minus_one(b: bytearray) -> None:
    """the marching order's length byte (header +0xA0, DS:C629) is 0xFF = -1: a value the game never writes, which R016+0x0DA6 meets as a signed byte
    (the order is then rebuilt at the battle's start)."""
    b[0xA0] = 0xFF


def hour_minus_three(b: bytearray) -> None:
    """the hour of the day (header +0x6E, DS:00E0) is -3: a value the clock never holds; the camp's day (R036+0x1739) then gives the hourglass the
    band -3 / 3 = -1 (R043+0x34B8..0x3590 -> R043+0x37CA)."""
    struct.pack_into("<h", b, 0x6E, -3)


def relics(b: bytearray, lst_path: Path = LST) -> None:
    """a relic donation past an empty pack (R031+0x5664 / R031+0x57FC): member 0 carries nothing, member 1 holds a stack of two of item 150
    (Thorn of the Crown, item flag 0x1000) before its own items."""
    lst = lst_path.read_bytes()
    at = 3 + 150 * 0x2E
    b[HEAD_END + PACK_COUNT] = 0
    base = HEAD_END + CHAR
    count = struct.unpack_from("b", b, base + PACK_COUNT)[0]
    old = [bytes(b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)]) for i in range(count)]
    new = ([struct.pack("<HBBBB", 150, lst[at + 0x1E], lst[at + 0x26], 2, lst[at + 0x25])] + old)[:64]
    for i, e in enumerate(new):
        b[base + PACK + ENTRY * i:base + PACK + ENTRY * (i + 1)] = e
    b[base + PACK_COUNT] = len(new)


def dragon_events(b: bytearray) -> None:
    """an event of kind 0x2A (value 10: the place's danger replaced, as the dragon's quest makes it, R039+0x11FC) at every place whose notice area
    reaches a cell within 40 of the party's map position (header +0x7E / +0x80): the aim was that the travel step's weight R030+0x2D44 then finds the event and reaches R030+0x2DAD. An unproven attempt: the run (local/batch/b28 unreach28) saw
    2D44 take near = -1, so 0x2DAD was not reached and the session using it (uj-dragon1) was dropped; the variant is kept for a later try. The event record is the 0x2B one of the same saved game, copied with the kind, location and value changed."""
    at = HEAD_END - 0x189 + 0xEF + 4 + 10 + 20 + 5 * 0x18
    characters = struct.unpack_from("<H", b, 0xF1)[0]
    at += characters * CHAR
    events = struct.unpack_from("<H", b, at)[0]
    first = at + 2
    records = [bytes(b[first + i * 0x30:first + (i + 1) * 0x30]) for i in range(events)]
    template = next(r for r in records if struct.unpack_from("<H", r, 0x22)[0] == 0x2B)
    locs_at = first + events * 0x30
    column, row = struct.unpack_from("<HH", b, 0x7E)
    chosen = []
    for i in range(0x19E):
        r = locs_at + 2 + i * 0x3A
        x, y, u = struct.unpack_from("<HHH", b, r + 4)[0], struct.unpack_from("<H", b, r + 6)[0], struct.unpack_from("<H", b, r + 8)[0]
        if any(not (x < c + 1 - (u + 1) // 2 or x > c + (u + 1) // 2 or y < w - u or y > w + u + 1) for c in range(column - 40, column + 41, 2) for w in range(row - 40, row + 41, 2)):
            chosen.append(i)
    new = []
    for i in chosen:
        e = bytearray(template)
        struct.pack_into("<HHHH", e, 0x1A, 0xFFFE, i, 10, 0)
        struct.pack_into("<H", e, 0x22, 0x2A)
        new.append(bytes(e))
    out = bytearray(b[:at]) + struct.pack("<H", events + len(new)) + b"".join(records) + b"".join(new) + b[locs_at:]
    b[:] = out


def full_events(b: bytearray, capacity: int = 300) -> None:
    """the event table filled to all its 300 slots (the open arms of event-create / event-refresh / event-advance R029+0x2C4E / 0x2D5C / 0x2E74:
    -1 when no slot is free, R029+0x3144): the game's own events first, then fillers, each a copy of the game's event that ends last with the
    kind 0x1C (a place's fact), type 0x7F, owner 0x7FF0, the last place (413), value 0, no expiry handler, no flags, tag 0."""
    at = 0xEF + 4 + 10 + 20 + 5 * 0x18 + struct.unpack_from("<H", b, 0xF1)[0] * CHAR
    events = struct.unpack_from("<H", b, at)[0]
    first = at + 2
    records = [bytes(b[first + i * 0x30:first + (i + 1) * 0x30]) for i in range(events)]
    template = max(records, key=lambda r: struct.unpack_from("<4H", r, 0x12)[::-1])
    fillers = []
    for _ in range(max(0, capacity - events)):
        e = bytearray(template)
        struct.pack_into("<H", e, 0x00, 0x7F)
        struct.pack_into("<HHHHHHH", e, 0x1A, 0x7FF0, 413, 0, 0, 0x1C, 0, 0)
        struct.pack_into("<HHHH", e, 0x28, 0, 0, 0, 0)
        fillers.append(bytes(e))
    b[:] = bytearray(b[:at]) + struct.pack("<H", events + len(fillers)) + b"".join(records) + b"".join(fillers) + b[first + events * 0x30:]


def implode_literals(data: bytes, dictbits: int = 6) -> bytes:
    """data as a PKWARE DCL block that holds only literals (binary mode: a 0 bit and the byte's 8 bits each) and the end code (length 519):
    a valid block for any explode (savdiff.explode, the original's R003, Darklands.Persistence.Explode), only larger than the original's."""
    import savdiff
    count, sym = savdiff.LEN
    codes, code, first, index = {}, 0, 0, 0
    for length in range(1, 14):          # the canonical codes as savdiff.explode's decode reads them (each code bit inverted)
        for k in range(count[length]):
            codes[sym[index + k]] = (length, first + k)
        index += count[length]
        first = (first + count[length]) << 1
    bits: list[int] = []
    for byte in data:
        bits.append(0)
        bits.extend((byte >> k) & 1 for k in range(8))
    length, value = codes[15]                # BASE[15] + 255 = 519: the end
    bits.append(1)
    bits.extend(((value >> (length - 1 - k)) & 1) ^ 1 for k in range(length))
    bits.extend((255 >> k) & 1 for k in range(8))
    out = bytearray([0, dictbits])
    for at in range(0, len(bits), 8):
        out.append(sum(bit << k for k, bit in enumerate(bits[at:at + 8])))
    assert savdiff.explode(bytes(out)) == bytes(data)
    return bytes(out)


VARS_FLOOR, VARS_MOVING, VARS_SHOWN, VARS_PARTY, VARS_ENEMY, VARS_PLACE, VARS_OPENED = 0x1600, 0x15A5, 0x16D1, 0x16D2, 0x16D3, 0x1675, 0x168F


def battle_level(level: int, spots: list[tuple[int, int]], groups: tuple[int, ...]):
    """A game saved in a battle moved to another level of the same battlefield: the party's slots 0..3 stand at <spots> on level <level>
    (their records' position and destination, action 0, screen place 0, 0), and the battle file's floor shown, party floor, enemies' floor and the members'
    floors (BattleVariables FloorShown / PartyFloor / EnemyFloor / Floor) name that level, their moving flags and place actions cleared, and
    the wall groups <groups> of that level count as opened (BattleVariables DoorsOpened, 2E38:0BB6: the level's hidden areas, which the battle
    shows as void until a member walks in, R023 0000; the party's room must be one). The
    levels themselves are the file's (a persistent battlefield saves every level). The .BSV's variables block is written back with
    implode_literals, its header's first size word set to it."""
    def edit(b: bytearray, bsv: bytearray) -> None:
        import savdiff
        n = struct.unpack_from("<H", b, 0xF1)[0]
        for k in range(n):
            base = HEAD_END + k * CHAR
            slot = b[base + 0x1C]
            if slot < len(spots) and b[base + STATUS]:
                x, y = spots[slot]
                struct.pack_into("<hhhh", b, base + 4, x, y, x, y)
                # the screen place (+0x00, +0x02) outside the view's margins: the battle's first tick then centres the view on the
                # active member (R014+0x1065..0x10A7), as no centre is kept for the new level (DS:C866 / DS:C886 are 0 after a load)
                struct.pack_into("<hh", b, base, 0, 0)
                struct.pack_into("<H", b, base + 0x10, 0)
        size = struct.unpack_from("<H", bsv, 0)[0]
        block = bytes(bsv[0x2A:0x2A + size])
        v = bytearray(savdiff.explode(block))
        v[VARS_SHOWN] = v[VARS_PARTY] = v[VARS_ENEMY] = level
        opened = struct.unpack_from("<I", v, VARS_OPENED + 4 * level)[0]
        struct.pack_into("<I", v, VARS_OPENED + 4 * level, opened | sum(1 << g for g in groups))
        for i in range(5):
            v[VARS_FLOOR + i] = level
            v[VARS_MOVING + i] = 0
            v[VARS_PLACE + i] = 0
        new = implode_literals(bytes(v), block[1])
        bsv[:] = bsv[:0x2A] + new + bsv[0x2A + size:]
        struct.pack_into("<H", bsv, 0, len(new))
    return edit


# the mine of Rammelsburg (battlefield 0x0B, DKSAVE99's mine quest of sort 0) as the original saved it in savemid-mine (DKSAVE2): level 3
# (section 10) holds the dwarf king's riddle door (a wall alarm, card 0xD4 $MineT17, beyond it 0xE5 $MineB02) over the ladder to level 5;
# the party in the closed room south of it (wall group 7; the ladder's cell is group 16). Level 6 (section 14) holds the stone box's door (a furniture alarm, card 0xE4 $MineB01), the party in its room (wall group 6).
BATTLE_LEVELS = {"king": battle_level(3, [(1040, 140), (1008, 140), (1072, 140), (1040, 154)], (7,)),
                 "stonebox": battle_level(6, [(976, 312), (944, 296), (944, 328), (944, 312)], (6,))}


VARIANTS = {"dragonev": dragon_events, "ev300": full_events, "relics": relics, "marchm1": marching_minus_one, "hourm3": hour_minus_three, "dead": dead, "out3": out3, "sea": sea, "away": away, "hurtattr": hurt_attributes, "hurtstatus": hurt_status, "kitchen": kitchen, "kitchen3": lambda b: kitchen(b, 3), "pots": pots, "pots3": lambda b: pots(b, 3), "hurt": hurt,
            "away24": away_card(0x24), "away7b": away_card(0x7B), "awaya0": away_card(0xA0),
            "away16": away_card(0x16), "away7a": away_card(0x7A), "awaya5": away_card(0xA5)}


def main() -> None:
    variant, source, target = sys.argv[1:4]
    b = bytearray(Path(source).read_bytes())
    if variant not in VARIANTS and variant.startswith(("weak", "card")):
        # the controls of the away<card> saves (local/batch/res5b): weak<card> the same without the Away member, card<card> the game
        # saved at the card with the party as it was
        VARIANTS[variant] = away_card(int(variant[4:], 16), variant.startswith("weak"), False)
    if variant not in VARIANTS and variant.startswith("dup"):
        # dup<a><b>: two party slots naming one record (round cards1's lead: what a load and a later save make of it)
        def dup(b, a=int(variant[3]), c=int(variant[4])):
            struct.pack_into("<h", b, 0xF3 + 2 * c, struct.unpack_from("<h", b, 0xF3 + 2 * a)[0])
        VARIANTS[variant] = dup
    if variant not in VARIANTS and variant.startswith("month"):
        # month<N>[d<D>]: the game's date moved to month N (0..11) and day D (header +0x6A / +0x6C): the seasons of the travel map (R2)
        month, _, day = variant[5:].partition("d")
        def moved(b, month=int(month), day=int(day) if day else None):
            struct.pack_into("<H", b, 0x6A, month)
            if day is not None:
                struct.pack_into("<H", b, 0x6C, day)
        VARIANTS[variant] = moved
    if variant not in VARIANTS and variant.startswith("pos"):
        # pos<column>x<row>: the party's map cell moved (header words +0x7E / +0x80, OriginalSaves.MapXAt / MapYAt): the starts of the travel map's
        # edge and paging walks (batch 33 clamp, batch 34 m4..m6, batch 35 d). The battle file beside the source is copied beside the target.
        column, _, row = variant[3:].partition("x")
        VARIANTS[variant] = lambda b, c=int(column), r=int(row): struct.pack_into("<HH", b, 0x7E, c, r)
        source_bsv = Path(source).with_suffix(".BSV")
        if source_bsv.exists():
            Path(target).parent.mkdir(parents=True, exist_ok=True)
            Path(target).with_suffix(".BSV").write_bytes(source_bsv.read_bytes())
    if variant in BATTLE_LEVELS:
        # a game saved in a battle: the .BSV beside the source is edited too and written beside the target
        bsv = bytearray(Path(source).with_suffix(".BSV").read_bytes())
        BATTLE_LEVELS[variant](b, bsv)
        Path(target).parent.mkdir(parents=True, exist_ok=True)
        Path(target).with_suffix(".BSV").write_bytes(bytes(bsv))
    elif variant == "relics":
        relics(b, Path(source).resolve().parent.parent / "DARKLAND.LST")           # the game folder's list, next to SAVES/
    else:
        VARIANTS[variant](b)
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    Path(target).write_bytes(bytes(b))


if __name__ == "__main__":
    main()
