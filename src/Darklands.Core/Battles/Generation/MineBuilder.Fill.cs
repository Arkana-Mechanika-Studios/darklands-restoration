namespace Darklands.Core.Battles.Generation;

/// <summary>What fills a section: random walls, door frames, loot piles, chests, floor items and the enemy groups.</summary>
internal sealed partial class MineBuilder
{
    /// <summary>
    /// The walls of a section: random wall types (the parameters come from the mine type and section), runs of solid walls, floor
    /// heights and floor decoration; marks the flag map with the wall sides of every cell.
    /// </summary>
    // orig: R009+0x1358 (1432:1358) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // C: the cells are visited from x = 38 down to 1 and y = 38 down to 1, so a cell reads the neighbours at x + 1 and y + 1 after they were
    // redone; every wall choice is made from the cell's original walls (cure) but written over the redone cell.
    private void BuildWalls(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        var number = level.Number;
        var run = WallRunLength(t, section);
        var (decorationBase, decorationKinds, decorationOdds) = WallDecorationParameters(t, section);
        var floorRange = FloorTileRange(t, section);
        var (floorDecorationBase, floorDecorationKinds, floorDecorationOdds) = FloorDecorationParameters(t, section);
        var solidColumn = new bool[40];
        for (var x = 38; x > 0; x--)
        {
            var solidRow = false;
            for (var y = 38; y > 0; y--)
            {
                var original = level.Cell(x, y);
                if ((original & 0x8000_0000) != 0)
                {
                    continue;
                }
                _flags[number, x, y] = 0;
                var north = (int)(original >> 16) & 0xF;
                if (north != 0)
                {
                    if (run > 1)
                    {
                        if (!solidColumn[y] && Roll(run) == 1)
                        {
                            solidColumn[y] = true;
                        }
                        else if (solidColumn[y] && Roll(4) == 1)
                        {
                            solidColumn[y] = false;
                        }
                    }
                    north = solidColumn[y] ? 3 : Roll(2) == 1 ? _northWallA : _northWallB;
                    level.AndCell(x, y, ~0xF0000u);
                    level.OrCell(x, y, (uint)(north & 0xF) << 16);
                    if (!solidColumn[y] && decorationKinds > 0 && Roll(decorationOdds) == 1)
                    {
                        var decoration = (sbyte)(Roll(decorationKinds) + decorationBase - 1);
                        level.OrCell(x, y, (uint)((decoration & 0xF) << 4) << 16);
                    }
                }
                var east = (int)original & 0xF;
                if (east != 0)
                {
                    var next = level.Cell(x + 1, y);
                    east = (next & 0x8000_0000) == 0 ? (int)(next >> 8) & 0xF : Roll(2) == 1 ? _westWallA : _westWallB;
                    level.AndCell(x, y, ~0xFu);
                    level.OrCell(x, y, (uint)east & 0xF);
                }
                var south = (int)(original >> 24) & 0xF;
                if (south != 0)
                {
                    var next = level.Cell(x, y + 1);
                    south = (next & 0x8000_0000) == 0 ? (int)(next >> 16) & 0xF : Roll(2) == 1 ? _northWallA : _northWallB;
                    level.AndCell(x, y, ~0xF000000u);
                    level.OrCell(x, y, (uint)(south & 0xF) << 24);
                }
                var west = (int)(original >> 8) & 0xF;
                if (west != 0)
                {
                    if (run > 1)
                    {
                        if (!solidRow && Roll(run) == 1)
                        {
                            solidRow = true;
                        }
                        else if (solidRow && Roll(4) == 1)
                        {
                            solidRow = false;
                        }
                    }
                    west = solidRow ? 3 : Roll(2) == 1 ? _westWallA : _westWallB;
                    level.AndCell(x, y, ~0xF00u);
                    level.OrCell(x, y, (uint)(west & 0xF) << 8);
                }
                var floorDecoration = 0;
                var floorTile = level.Floor(x, y) >> 4;
                if (floorTile != 0)
                {
                    floorTile = (sbyte)(Roll(floorRange) + (sbyte)_floorTileBase - 1);
                    if (north == 3 && west == 3)
                    {
                        floorDecoration = 5;
                    }
                    else if (north == 3)
                    {
                        floorDecoration = 3;
                    }
                    else if (west == 3)
                    {
                        floorDecoration = 4;
                    }
                    else if (floorDecorationKinds > 0 && Roll(floorDecorationOdds) == 1)
                    {
                        floorDecoration = (sbyte)(Roll(floorDecorationKinds) + floorDecorationBase - 1);
                    }
                    level.SetFloor(x, y, (byte)((floorDecoration & 0xF) | (floorTile << 4)));
                }
                var furniture = (int)(original & 0xF0) >> 4;
                if (furniture != 0)
                {
                    furniture = 9;
                    level.AndCell(x, y, ~0xF0u);
                    level.OrCell(x, y, 0x90);
                    var turn = Roll(4) - 1;
                    level.OrCell(x, y, (uint)(turn & 3) << 28);
                }
                if (north != 0)
                {
                    _flags.Or(number, x, y, 0x80);
                }
                if (east != 0)
                {
                    _flags.Or(number, x, y, 0x40);
                }
                if (south != 0)
                {
                    _flags.Or(number, x, y, 0x20);
                }
                if (west != 0)
                {
                    _flags.Or(number, x, y, 0x10);
                }
                if (furniture != 0)
                {
                    _flags.Or(number, x, y, 1);
                }
            }
        }
    }

    /// <summary>Section 1's exits lead back to the overland map by card 12 (they already do; the write has no effect).</summary>
    // orig: R009+0x18FC (1432:18FC) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    // C: both cards are already 0x0C from the loader; the write changes nothing.
    private void SetExitCards(int t, int section)
    {
        var level = LevelOf(section);
        if (level is not null && section == 1)
        {
            var cards = level.Level.ExitCards;
            level.Level.ExitCards = cards with { Fled = 12, LeftBeforeFight = 12 };
        }
    }

    /// <summary>The walls the prepared map's door records name get a wall tile; items on the floor get their kind.</summary>
    // orig: R009+0x1948 (1432:1948) draft (close per the draft notes: stack slot order only);
    // tests: Oracle:mine.json
    private void MakeDoorFrames(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        var (tileBase, tileRange) = DoorWallParameters(t, section);
        for (var i = 0; i < (sbyte)level.Level.DoorCount; i++)
        {
            var door = level.Door(i);
            int shift;
            if ((door.FirstX & 0x80) != 0)
            {
                shift = 16;
            }
            else if ((door.FirstX & 0x40) != 0)
            {
                shift = 0;
            }
            else if ((door.FirstY & 0x80) != 0)
            {
                shift = 24;
            }
            else
            {
                shift = (door.FirstY & 0x40) != 0 ? 8 : 0;
            }
            int tile;
            if (door.Flag >= 0)
            {
                tile = Roll(tileRange) + tileBase - 1;
            }
            else
            {
                tile = shift >= 8 ? _westWallA : _northWallA;
            }
            var x = door.FirstX & 0x3F;
            var y = door.FirstY & 0x3F;
            level.OrCell(x, y, (uint)tile << shift);
            _flags.Or(level.Number, x, y, 2);
        }
        for (var i = 0; i < level.ObjectCount; i++)
        {
            var o = level.Object(i);
            if (o.Type == 1)
            {
                level.SetObject(i, o with { Room = Roll(12) + section + 15 });
            }
        }
    }

    /// <summary>Chests (made from the prepared map's chest and floor-item objects) and items on the floor.</summary>
    // orig: R009+0x1AF0 (1432:1AF0) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // C: a10/a14/a12 are locals of the whole pass: when the contents table has no entry for the mine and section they keep the values of the
    // chest before (zero before the first, UNCERTAIN: the original holds stack leftovers).
    private void MakeChests(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        int item = 0, quality = 0, grade = 0;
        for (var i = 0; i < level.ObjectCount; i++)
        {
            var o = level.Object(i);
            switch (o.Type)
            {
                case 1:
                    {
                        level.SetObject(i, o with { Room = Roll(10) + section + 20 });
                        var cells = level.FloorItem(o.Id);
                        for (var j = 0; j < 4; j++)
                        {
                            if (cells.Cell(j) != 0)
                            {
                                cells.SetCell(j, 6);
                            }
                        }
                        break;
                    }
                case 2 or 4:
                    {
                        (item, quality, grade) = ChestContents(t, section, item, quality, grade);
                        var chestIndex = o.Id;
                        var spare = o.Room;
                        var firstChest = level.Chest(chestIndex);
                        var id = firstChest.Id;
                        if (item >= 0x84 && item <= 0x99)
                        {
                            if (chestIndex >= 0)
                            {
                                firstChest.Id = id;
                                firstChest.Byte2 = item + 0x7C;
                                firstChest.Byte3 = quality;
                            }
                            o = o with { Type = 2, Word8 = 0, Room = -1 };
                        }
                        else
                        {
                            if (grade > 3)
                            {
                                grade = 3;
                            }
                            if (chestIndex >= 0)
                            {
                                firstChest.Id = id;
                                firstChest.Byte8 = item;
                                firstChest.Byte9 = quality;
                                firstChest.Grade = grade;
                            }
                            if (spare >= 0)
                            {
                                var other = level.Chest(spare);
                                other.Id = id;
                                other.Byte8 = item;
                                other.Byte9 = quality;
                                other.Grade = grade;
                            }
                            o = o with { Type = 4, Word8 = 0 };
                        }
                        level.SetObject(i, o);
                        var y0 = o.Y / 16;
                        var y1 = y0 + (o.Height >> 4) - 1;
                        var x0 = o.X / 32;
                        var x1 = x0 + (o.Width >> 5) - 1;
                        _flags.OccupyBlock(level.Number, x0, x1, y0, y1);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// The loot piles of a section: keeps those the contents function accepts (compacting the list), gives them ids and random
    /// contents. The pile list is reordered in place, copying only each pile's place, so a kept pile keeps the contents the
    /// earlier pile in its slot had.
    /// </summary>
    // orig: R009+0x1DA4 (1432:1DA4) draft (unmatched draft, far off per the draft notes); checked
    // against asm (the tail: only a pile with loot is kept; a locked door is not); tests: Oracle:mine.json
    // C: the draft's `d->x0a = 5; d->x0a = 0x25;` is a double store of byte 0x0A (the first value is lost).
    private void MakePiles(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        var kept = 0;
        var count = (sbyte)level.PileCount;
        if (count > 0)
        {
            var i = 0;
            do
            {
                if (12 - level.Number > 20 - _nextPileId)
                {
                    break;
                }
                var pile = level.Pile(kept);
                if (kept != i)
                {
                    pile.CopyPlaceFrom(level.Pile(i));
                }
                var result = FillPile(level, pile, t, section, kept);
                if (result != 0)
                {
                    level.OrCell(pile.CellX, pile.CellY, 0x50);
                    var x0 = pile.X / 32;
                    var x1 = x0 + (pile.Width >> 5) - 1;
                    var y0 = pile.Y / 16;
                    var y1 = y0 + (pile.Height >> 4) - 1;
                    _flags.Or(level.Number, pile.CellX, pile.CellY, 1);
                    _flags.OccupyBlock(level.Number, x0, x1, y0, y1);
                    if (result == 1)
                    {
                        pile.Id = _nextPileId++;
                        var variant = Roll(3);
                        _pileRollMade = true;
                        switch (variant)
                        {
                            case 1:
                                pile.ByteA = 0x25;
                                var strength = Math.Max(Math.Min(_g.Forces.RollDice(2, 2) + section / 4 - 3, 2), 0);
                                pile.Byte9 = strength * 30 + 20;
                                break;
                            case 2:
                                pile.Flags = ((section + 2) * 5) | 0x80;
                                break;
                        }
                        // Draft error: the draft advances the kept pile for result 2 as well; the asm (cmp byte ptr [bp-1], 1) only for 1.
                        // A locked door (2) marks its cell but is not kept as a pile.
                        kept++;
                    }
                }
            }
            while (++i < count);
        }
        level.PileCount = kept;
    }

    /// <summary>Adds a locked door at a loot pile's spot: a door object and a free door-spot record.</summary>
    // orig: R009+0x205E (1432:205E) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // C: the draft types the pile as an exit record; the cell is the pile's bytes +6/+7 and its facing byte +0x0C.
    private void AddLockedDoor(MineLevel level, PileRecord spot, int id)
    {
        var k = 0;
        for (; k < 5; k++)
        {
            var existing = level.DoorSpot(k);
            if (!(existing.X > 0 && existing.X < 0x500 && existing.Y > 0 && existing.Y < 0x280))
            {
                break;
            }
        }
        if (k >= 5)
        {
            return;
        }
        for (var j = 54; j > level.ObjectCount; j--)
        {
            level.SetObject(j, level.Object(j - 1));
        }
        var door = new BattlefieldObject(spot.X, spot.Y, spot.Width, spot.Height, 7, 0, k, id);
        level.SetObject(level.ObjectCount, door);
        level.ObjectCount++;
        var record = level.DoorSpot(k);
        record.Byte0 = 0;
        record.X = (sbyte)spot.CellX * 32 + 16;
        record.Y = (sbyte)spot.CellY * 16 + 8;
        record.Facing = spot.Id;
        switch ((sbyte)spot.Id)
        {
            case 0:
                record.Y += 0x18;
                break;
            case 2:
                record.X -= 0x30;
                break;
            case 4:
                record.Y -= 0x18;
                break;
            case 6:
                record.X += 0x30;
                break;
        }
        var y = record.Y / 16 + 1;
        var x = record.X / 32 + 1;
        _flags.OccupyAround(level.Number, x, y);
    }

    /// <summary>The six enemy groups of a section (the parameters give their contents).</summary>
    // orig: R009+0x2252 (1432:2252) draft (close per the draft notes: AX known-zero reuse); tests:
    // Oracle:mine.json
    private void PlaceMapGroups(int t, int section)
    {
        if (IsSmallMine(t))
        {
            return;
        }
        if (IsLair(t))
        {
            return;
        }
        var slot = SlotIndexOf(section);
        if (slot < 0)
        {
            return;
        }
        var level = _slots[slot]!;
        var number = level.Number;
        for (var i = 0; i < 6; i++)
        {
            var plan = GroupPlanFor(t, section, i);
            var (first, second) = AddPlannedKinds(number, plan);
            PlaceGroup(t, first, plan.Count1, plan.Reinforcement1, second, plan.Count2, plan.Reinforcement2, section, plan.Posted, i, -1, -1);
            if (i < 5)
            {
                ReleaseSpot(slot, i);
            }
        }
    }

    /// <summary>Adds the two kinds a group plan names to the level's kinds and sets their kill bytes; returns their kind indexes (−1 none).</summary>
    // C: monster-type-lookup, then battlefield-monster-table[level * 12 + kind].x0c = flag, for the first kind and then for the second.
    private (int First, int Second) AddPlannedKinds(int level, MineGroupPlan plan)
    {
        var first = Forces.AddKind(level, plan.Type1, plan.Quality1);
        if (first >= 0)
        {
            Forces.Kinds[level][first].EndsBattleWhenKilled = (sbyte)plan.Leader1;
        }
        var second = Forces.AddKind(level, plan.Type2, plan.Quality2);
        if (second >= 0)
        {
            Forces.Kinds[level][second].EndsBattleWhenKilled = (sbyte)plan.Leader2;
        }
        return (first, second);
    }

    /// <summary>The spot record i of the level in a slot is used up (its flag byte and x are cleared).</summary>
    private void ReleaseSpot(int slot, int index)
    {
        var spot = _slots[slot]!.WallGap(index);
        spot.X = 0;
        spot.Byte1 = 0;
    }

    /// <summary>
    /// An enemy group on the level of <paramref name="section"/>: kinds t1 and t2 with counts n1 and n2 (a value above zero in x1 or x2 gives
    /// the group a reinforcement record: x % 10 at a time, x / 10 in all); the position comes from marker 6 (mine 0x6c), from the
    /// given point or from the section's spot record <paramref name="spot"/>.
    /// </summary>
    // orig: R009+0x23D6 (1432:23D6) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // DIVERGENCE: the original writes the group into slot group-total of level lvno even past 10 (into the next level's slots); the list grows here.
    private void PlaceGroup(int t, int type1, int count1, int reinforcement1, int type2, int count2, int reinforcement2, int section, bool posted,
        int spot, int pointY, int facing)
    {
        // C: for the point form the third-last argument is the x of the point and `spot` carries it.
        var pointX = spot;
        if (type1 < 0 && type2 < 0)
        {
            return;
        }
        if (spot < 0)
        {
            return;
        }
        if (spot >= 6 && pointY < 0)
        {
            return;
        }
        var slot = SlotIndexOf(section);
        if (slot < 0)
        {
            return;
        }
        var level = _slots[slot]!;
        var number = level.Number;
        var group = new EnemyGroup();
        if (t == 0x6C && spot >= 5)
        {
            var k = 0;
            for (; k < BattlefieldLevel.MaxObjects; k++)
            {
                var candidate = level.Object(k);
                if (candidate.Type == 0x65 && candidate.Id == 6)
                {
                    break;
                }
            }
            if (k >= BattlefieldLevel.MaxObjects)
            {
                return;
            }
            var marker = level.Object(k);
            group.X = (short)(marker.X + 16);
            group.Y = (short)(marker.Y + 8);
            group.Room = (sbyte)marker.Room;
        }
        else if (pointY > 0)
        {
            group.X = (short)pointX;
            group.Y = (short)pointY;
            group.Room = (sbyte)facing;
        }
        else
        {
            if (spot >= 5)
            {
                return;
            }
            var record = level.WallGap(spot);
            group.X = (short)record.X;
            group.Y = (short)record.Y;
            group.Room = (sbyte)record.Byte3;
        }
        var x = group.X / 32;
        var y = group.Y / 16;
        if (posted && !IsFixedLayout(t))
        {
            group.X = (short)-group.X;
        }
        _flags.Or(number, x, y, 2);
        group.Spread = (short)(posted ? 32 : 16);
        group.HasReinforcements = 0;
        var j = 0;
        if (type1 >= 0 && (count1 > 0 || reinforcement1 > 0))
        {
            group.Kinds[0] = (sbyte)type1;
            group.Counts[0] = (sbyte)count1;
            j = 1;
            if (reinforcement1 > 0)
            {
                SetReinforcement(group, type1, reinforcement1);
            }
        }
        if (type2 >= 0 && (count2 > 0 || reinforcement2 > 0))
        {
            group.Kinds[j] = (sbyte)type2;
            group.Counts[j] = (sbyte)count2;
            j++;
            if (reinforcement2 > 0)
            {
                // DORMANT: no row of the group tables sets the second kind's reinforcement (its table value is always 0).
                SetReinforcement(group, type2, reinforcement2);
            }
        }
        if (j > 0)
        {
            Forces.Groups[number].Add(group);
        }
    }

    /// <summary>The reinforcement record of a group: the kind, where they arrive (the group's own spot) and how many come at a time and in all.</summary>
    private static void SetReinforcement(EnemyGroup group, int kind, int value)
    {
        group.HasReinforcements = 1;
        group.ReinforcementKind = (sbyte)kind;
        group.Byte15 = (sbyte)group.Spread;
        group.ReinforcementX = group.X;
        group.ReinforcementY = group.Y;
        group.ReinforcementBatch = (sbyte)(value % 10);
        group.ReinforcementPool = (sbyte)(value / 10);
        group.ReinforcementFacing = 4;
    }

    /// <summary>
    /// Special mines: the exit placeholders (object type 5) become real objects after the map's own; on section 13 of mines 0x0e..0x10 they
    /// are doors.
    /// </summary>
    // orig: R009+0x26CC (1432:26CC) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    // DORMANT: DS:11DD (the debugging switch) is 0 in the shipped game, so the early return never happens.
    // DORMANT: the arm for 0x84..0x86 never runs (LoadSection hands over the remapped type 0x0e..0x10).
    private void ConvertExitMarkers(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        if (_g.Context.MineDebugSwitch && !IsFixedLayout(t))
        {
            return;
        }
        var count = level.ObjectCount;
        for (var i = count; i < BattlefieldLevel.MaxObjects; i++)
        {
            var o = level.Object(i);
            if (o.Type != 5)
            {
                continue;
            }
            if (count != i)
            {
                var other = level.Object(count);
                level.SetObject(i, other);
                level.SetObject(count, o);
            }
            var marker = level.Object(count) with { Room = 0x30 };
            if (((t >= 0x0E && t <= 0x10) || (t >= 0x84 && t <= 0x86)) && section == 13)
            {
                marker = marker with { Type = 7, Word8 = 0, Room = 0xE9 };
            }
            level.SetObject(count, marker);
            level.ObjectCount = ++count;
            var x0 = marker.X / 32;
            var x1 = x0 + (marker.Width >> 5) - 1;
            var y0 = marker.Y / 16;
            var y1 = y0 + (marker.Height >> 4) - 1;
            _flags.OccupyBlock(level.Number, x0, x1, y0, y1);
            var spot = level.DoorSpot(marker.Id);
            _flags.OccupyAround(level.Number, spot.X / 32, spot.Y / 16);
        }
    }
}
