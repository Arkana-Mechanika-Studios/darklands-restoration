namespace Darklands.Core.Battles.Generation;

/// <summary>Joining the sections of a mine: exits, carved wall gaps and the party's entrance.</summary>
internal sealed partial class MineBuilder
{
    /// <summary>
    /// The section and slot a connection goes to: section <paramref name="b"/> if it has a slot, else section <paramref name="c"/>;
    /// slot −1 when neither is placed.
    /// </summary>
    private (int Section, int Slot) ConnectionTarget(int b, int c)
    {
        var section = 0;
        var slot = -1;
        if (b is >= 0 and < SectionCount)
        {
            section = b;
            slot = _slotOfSection[b];
        }
        if (slot is < 0 or >= SlotCount)
        {
            if (c is >= 0 and < SectionCount)
            {
                section = c;
                slot = _slotOfSection[c];
            }
        }
        return (section, slot);
    }

    /// <summary>Connects section <paramref name="a"/> with section <paramref name="b"/> (or <paramref name="c"/> if b has no slot) through an exit on each side.</summary>
    // orig: R009+0x2964 (1432:2964) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // C: if neither b nor c has a slot the first section still gets its exit marked (a dead end); `bs` is read only when a slot was found.
    private void Join(int t, int a, int b, int c)
    {
        var slotA = a is >= 0 and < SectionCount ? _slotOfSection[a] : -1;
        var (sectionB, slotB) = ConnectionTarget(b, c);
        if (slotA is < 0 or >= SlotCount || slotB >= SlotCount)
        {
            return;
        }
        var levelA = _slots[slotA]!;
        var exitA = PickExit(t, a, add: true);
        var pa = levelA.Exit(exitA);
        var x = pa.X / 32 + 1;
        var y = pa.Y / 16 + 1;
        levelA.OrCell(x, y, 0x10);
        _flags.OccupyAround(levelA.Number, x, y);
        _flags.Or(levelA.Number, x, y, 1);
        if (slotB < 0)
        {
            return;
        }
        var levelB = _slots[slotB]!;
        pa.TargetLevel = levelB.Number;
        var exitB = PickExit(t, sectionB, add: true);
        var pb = levelB.Exit(exitB);
        pb.TargetLevel = levelA.Number;
        pa.TargetExit = exitB;
        pb.TargetExit = exitA;
        (pa.ArrivalRoom, pb.ArrivalRoom) = (pb.ArrivalRoom, pa.ArrivalRoom);
        (pa.ArrivalX, pb.ArrivalX) = (pb.ArrivalX, pa.ArrivalX);
        (pa.ArrivalY, pb.ArrivalY) = (pb.ArrivalY, pa.ArrivalY);
        (pa.Facing, pb.Facing) = (pb.Facing, pa.Facing);
        x = pb.X / 32 + 1;
        y = pb.Y / 16 + 1;
        levelB.OrCell(x, y, 0xA0);
        _flags.OccupyAround(levelB.Number, x, y);
        _flags.Or(levelB.Number, x, y, 1);
    }

    /// <summary>
    /// Opens a passage from a section through one of its wall-gap objects (type 8): carves the wall cell, adds a part list for it,
    /// makes an exit to section b (or c), or a dead end with a guard group for some sections.
    /// </summary>
    // orig: R009+0x2C98 (1432:2C98) draft (unmatched draft, far off per the draft notes); checked
    // against asm (the four direction arms); tests: Oracle:mine.json
    // C: with no wall-gap object at all the search leaves i at 55 and reads the bytes after the object table; they are not type 8.
    private void CarveGap(int t, int section, int b, int c, int rotation)
    {
        var slotA = section is >= 0 and < SectionCount ? _slotOfSection[section] : -1;
        var (sectionB, slotB) = ConnectionTarget(b, c);
        if (slotA is < 0 or >= SlotCount || slotB >= SlotCount)
        {
            return;
        }
        var level = _slots[slotA]!;
        var number = level.Number;
        var first = 0;
        while (first < BattlefieldLevel.MaxObjects && level.Object(first).Type != 8)
        {
            first++;
        }
        var end = first + 1;
        while (end < BattlefieldLevel.MaxObjects && level.Object(end).Type == 8)
        {
            end++;
        }
        var gaps = end - first;
        if (gaps <= 0)
        {
            // DORMANT: gaps is at least 1.
            return;
        }
        var (index, gap, dir) = ChooseGap(level, first, gaps, rotation);
        if (gap.Type != 8)
        {
            return;
        }
        var mode = GapMode(t, section);
        var wall = mode switch { 2 => 6u, 3 => 5u, 0 => 0u, _ => 8u } & 0xFu;
        var x = gap.X / 32;
        var y = gap.Y / 16;
        _flags.OccupyAround(number, x, y);
        level.SetFloor(x, y, (byte)(level.Floor(x, y) & 0xF0));
        var carved = (uint)_westWallA | (uint)_westWallA << 8 | (uint)_northWallA << 16 | (uint)_northWallA << 24 | 0x10;
        // The old cell gets the frame wall on the side of the passage, the new cell the one facing back; the new cell starts as plain
        // rock. The arms for directions 2 and 4 (east, south) are checked against the asm but run only when none of the section's gaps
        // faces north or west in 15 tries: no prepared map does at any of the eight turns, so the oracle never reaches them.
        switch (dir)
        {
            case 0:
                level.AndCell(x, y, ~0xF0000u);
                // Draft error: the draft clears the north nibble of the carved cell here; the asm (and byte ptr [bp-69h], 0F0h) clears the south one.
                carved &= ~0xF000000u;
                if (mode != 0)
                {
                    level.OrCell(x, y, wall << 16);
                    carved |= wall << 24;
                }
                level.AndCell(x, y, ~0xF00000u);
                y--;
                break;
            case 2:
                level.AndCell(x, y, ~0xFu);
                carved &= ~0xF00u;
                if (mode != 0)
                {
                    level.OrCell(x, y, wall);
                    carved |= wall << 8;
                }
                x++;
                break;
            case 4:
                level.AndCell(x, y, ~0xF000000u);
                carved &= ~0xF0000u;
                if (mode != 0)
                {
                    level.OrCell(x, y, wall << 24);
                    carved |= wall << 16;
                }
                y++;
                break;
            case 6:
                level.AndCell(x, y, ~0xF00u);
                carved &= ~0xFu;
                if (mode != 0)
                {
                    level.OrCell(x, y, wall << 8);
                    carved |= wall;
                }
                x--;
                break;
        }
        _flags.Or(number, x, y, 1);
        level.SetCell(x, y, carved);
        level.SetFloor(x, y, (byte)(level.Floor(x, y) | (byte)((sbyte)_floorTileBase << 4)));
        level.SetFloor(x, y, (byte)(level.Floor(x, y) & 0xF0));
        AddPartList(level, x, y);
        // The gap object moves to the end of the objects the map had and becomes the object of the carved passage.
        for (var q = index; q > level.ObjectCount; q--)
        {
            level.SetObject(q, level.Object(q - 1));
        }
        var objectIndex = level.ObjectCount;
        level.SetObject(objectIndex, gap);
        level.ObjectCount++;
        var spotIndex = 0;
        while (spotIndex < 5 && level.WallGap(spotIndex).Byte1 != 0)
        {
            spotIndex++;
        }
        spotIndex = Math.Min(spotIndex, 4);
        var spot = level.WallGap(spotIndex);
        var lastList = level.PartListCount - 1;
        var placed = gap;
        if (mode == 0)
        {
            placed = placed with { Type = 6, Word8 = 0, Id = lastList };
        }
        else if (mode <= 3)
        {
            // DORMANT: the else of this test (a mode above 3) does not exist; modes are 0..3.
            placed = placed with { Type = 8, Word8 = 0, Id = spotIndex, Room = ItemOfGap(t, section) };
            spot.Byte0 = 0;
            spot.Byte1 = x;
            spot.Byte2 = y;
            switch (dir)
            {
                case 0:
                    spot.Byte2 |= 0x80;
                    break;
                case 2:
                    spot.Byte2 |= 0x40;
                    break;
                case 4:
                    spot.Byte1 |= 0x80;
                    break;
                case 6:
                    spot.Byte1 |= 0x40;
                    break;
            }
            spot.Byte3 = lastList;
        }
        level.SetObject(objectIndex, placed);
        if (((t == 0x0B || t == 0x82) && (section == 3 || section == 10)) ||
            ((t == 0x0D || t == 0x83) && (section == 1 || section == 7 || section == 11)))
        {
            PlaceGuards(t, section, level, placed, lastList);
        }
        for (var q = 5; q > level.ExitCount; q--)
        {
            level.CopyExit(q, q - 1);
        }
        _fileExits[slotA] = Math.Min(_fileExits[slotA] + 1, 6);
        var exitIndex = level.ExitCount;
        level.ExitCount++;
        var exit = level.Exit(exitIndex);
        exit.X = x * 32 + 4;
        exit.Y = y * 16 + 2;
        exit.Width = 0x18;
        exit.Height = 0x0C;
        var arrivalX = x * 32 + 16;
        var arrivalY = y * 16 + 8;
        switch (dir)
        {
            case 0:
                exit.Height += 6;
                arrivalY += 0x20;
                break;
            case 2:
                exit.X -= 12;
                exit.Width += 12;
                arrivalX -= 0x40;
                break;
            case 4:
                exit.Y -= 6;
                exit.Height += 6;
                arrivalY -= 0x20;
                break;
            case 6:
                exit.Width += 12;
                arrivalX += 0x40;
                break;
        }
        spot.Y = arrivalY;
        spot.Facing = dir;
        spot.X = arrivalX;
        _flags.OccupyAround(number, arrivalX / 32 + 1, arrivalY / 16 + 1);
        if (slotB < 0)
        {
            exit.TargetLevel = level.Number;
            exit.TargetExit = exitIndex;
            exit.ArrivalRoom = lastList;
            exit.ArrivalX = spot.X;
            exit.ArrivalY = spot.Y;
            exit.Facing = dir;
            return;
        }
        var exitB = PickExit(t, sectionB, add: true);
        var levelB = _slots[slotB]!;
        var other = levelB.Exit(exitB);
        exit.TargetLevel = levelB.Number;
        other.TargetLevel = level.Number;
        exit.TargetExit = exitB;
        other.TargetExit = exitIndex;
        exit.ArrivalRoom = other.ArrivalRoom;
        other.ArrivalRoom = lastList;
        exit.ArrivalX = other.ArrivalX;
        other.ArrivalX = spot.X;
        exit.ArrivalY = other.ArrivalY;
        other.ArrivalY = spot.Y;
        exit.Facing = other.Facing;
        other.Facing = dir;
        x = other.X / 32 + 1;
        y = other.Y / 16 + 1;
        levelB.OrCell(x, y, 0xA0);
        _flags.OccupyAround(levelB.Number, x, y);
        _flags.Or(levelB.Number, x, y, 1);
    }

    /// <summary>The wall-gap object to carve: tries random ones until a gap facing north or west turns up (after four tries it walks them in order).</summary>
    private (int Index, BattlefieldObject Gap, int Direction) ChooseGap(MineLevel level, int first, int gaps, int rotation)
    {
        var k = 0;
        var ok = false;
        var index = 0;
        var gap = default(BattlefieldObject);
        var dir = 0;
        while (!ok && k < 15)
        {
            var m = k >= 4 ? k - 4 : Roll(gaps) - 1;
            m = Math.Min(gaps - 1, m);
            index = m + first;
            gap = level.Object(index);
            dir = MapTurn.TurnFacing((byte)gap.Id, rotation);
            ok = gap.Type == 8 && (dir == 0 || dir == 6);
            k++;
        }
        return (index, gap, dir);
    }

    /// <summary>What the carved gap becomes: 1 an exit gap, 2 and 3 exit gaps with other walls (mine 0x0d), 0 a plain opening (debugging switch).</summary>
    // DORMANT: mode 0 needs DS:11DD (the debugging switch, 0 in the shipped game); no mode above 3 exists.
    private int GapMode(int t, int section)
    {
        var mode = 1;
        if (t == 0x0D)
        {
            if (section is >= 7 and <= 8)
            {
                mode = 2;
            }
            else if (section is >= 11 and <= 12)
            {
                mode = 3;
            }
        }
        return _g.Context.MineDebugSwitch ? 0 : mode;
    }

    /// <summary>Adds a part list (room) holding the carved cell after the last list.</summary>
    private static void AddPartList(MineLevel level, int x, int y)
    {
        if (level.PartListCount >= 30)
        {
            return;
        }
        var q = level.PartListStart(level.PartListCount) / 2;
        while (q < MineLevel.PartCellWords && level.PartCell(q) != -1)
        {
            q++;
        }
        level.SetPartListStart(level.PartListCount, ++q * 2);
        level.PartListCount++;
        level.SetPartCell(q, (x << 8) + y);
        level.SetPartCell(++q, -1);
    }

    /// <summary>The guard group at the end of a dead-end passage.</summary>
    private void PlaceGuards(int t, int section, MineLevel level, BattlefieldObject gap, int lastList)
    {
        var number = level.Number;
        var plan = GroupPlanFor(t, section, 10);
        var (first, second) = AddPlannedKinds(number, plan);
        PlaceGroup(t, first, plan.Count1, plan.Reinforcement1, second, plan.Count2, plan.Reinforcement2, section, plan.Posted,
            gap.X + 16, gap.Y + 8, lastList);
    }

    /// <summary>The party's entrance: an exit of section <paramref name="section"/> (or b, c) becomes the start rectangle and position.</summary>
    // orig: R009+0x37B4 (1432:37B4) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    private void PlaceParty(int t, int section, int b, int c)
    {
        var u = -1;
        var s = -1;
        if (section is >= 0 and < SectionCount)
        {
            u = section;
            s = _slotOfSection[section];
        }
        if (s is < 0 or >= SlotCount)
        {
            if (b is >= 0 and < SectionCount)
            {
                u = b;
                s = _slotOfSection[b];
            }
        }
        if (s is < 0 or >= SlotCount)
        {
            if (c is >= 0 and < SectionCount)
            {
                u = c;
                s = _slotOfSection[c];
            }
        }
        if (u < 0 || s is < 0 or >= SlotCount)
        {
            return;
        }
        var level = _slots[s]!;
        var number = level.Number;
        var e = PickExit(t, u, add: false);
        var exit = level.Exit(e);
        level.SetStartRectangle(exit.X, exit.Y, exit.Width, exit.Height);
        level.RectangleCount = 1;
        var x = exit.X / 32 + 1;
        var y = exit.Y / 16 + 1;
        level.OrCell(x, y, 0xA0);
        level.StartX = exit.ArrivalX;
        level.StartY = exit.ArrivalY;
        level.Level.PartyRoom = exit.ArrivalRoom;
        level.Level.PartyFacing = (exit.Facing ^ 4) & 0xFF;
        _flags.Or(number, x, y, 1);
        var (rx, ry, rw, rh) = level.StartRectangle;
        var x0 = rx / 32;
        var x1 = x0 + rw / 32 - 1;
        var y0 = ry / 16;
        var y1 = y0 + rh / 16 - 1;
        _flags.OccupyBlock(number, x0, x1, y0, y1);
        _flags.OccupyAround(number, level.StartX / 32, level.StartY / 16);
    }

    /// <summary>
    /// Picks one of the section's exits from the map file (the fixed one for some mines) and moves it to the end of the used exits
    /// (<paramref name="add"/>) or of the unused ones; returns its index.
    /// </summary>
    // orig: R009+0x3A90 (1432:3A90) draft (close per the draft notes: frame 1 word larger, sub
    // ax,ax; inc ax); checked against asm (the type dispatch and the 0x82 fall-through); tests: Oracle:mine.json
    private int PickExit(int t, int section, bool add)
    {
        var slot = SlotIndexOf(section);
        if (slot < 0)
        {
            return 0;
        }
        var level = _slots[slot]!;
        var cnt = _fileExits[slot] - level.ExitCount;
        var k = -1;
        switch (t)
        {
            case 0x0B:
                if (section == 13)
                {
                    k = 1;
                }
                break;
            case 0x82 when section == 13:
                k = 1;
                break;
            case 0x82 or 0x0A or 0x83 or 0x84 or 0x85 or 0x86:
                // C: the 0x82 case falls through into this arm when the section is not 13. DS:C0FE (forceSide) is −1 in the shipped game.
                var force = _g.Context.ForceSide;
                if (force >= 0 && cnt > force)
                {
                    k = force;
                }
                break;
        }
        if (k < 0)
        {
            k = Roll(cnt) + level.ExitCount - 1;
        }
        int n;
        if (add)
        {
            n = level.ExitCount++;
        }
        else
        {
            n = --_fileExits[slot];
        }
        if (n != k)
        {
            var temp = level.ExitBytes(k);
            level.CopyExit(k, n);
            level.SetExitBytes(n, temp);
        }
        return n;
    }
}
