using System.Globalization;

namespace Darklands.Core.Battles.Generation;

/// <summary>Loading, preparing and saving the sections of a mine.</summary>
internal sealed partial class MineBuilder
{
    /// <summary>The slot a section lives in, or −1 when it has none (the C guards <c>sec in 0..15</c> then <c>slot in 0..3</c>).</summary>
    private int SlotIndexOf(int section)
    {
        var slot = section is >= 0 and < SectionCount ? _slotOfSection[section] : -1;
        return slot is >= 0 and < SlotCount ? slot : -1;
    }

    private MineLevel? LevelOf(int section)
    {
        var slot = SlotIndexOf(section);
        return slot < 0 ? null : _slots[slot];
    }

    /// <summary>The level slot (0..3) for a section: the one it has or a free one.</summary>
    // orig: R009+0x12B2 (1432:12B2) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int SlotFor(int section)
    {
        var slot = section is >= 0 and < SectionCount ? _slotOfSection[section] : -1;
        if (slot is >= 0 and < SlotCount)
        {
            _sectionOfSlot[slot] = section;
            return slot;
        }
        for (var i = 0; i < SlotCount; i++)
        {
            if (_sectionOfSlot[i] == section)
            {
                return _slotOfSection[section] = i;
            }
        }
        for (var i = 0; i < SlotCount; i++)
        {
            if (_sectionOfSlot[i] < 0 || _sectionOfSlot[i] >= 17)
            {
                _sectionOfSlot[i] = section;
                _slotOfSection[section] = i;
                _sectionPlaced[section] = true;
                return i;
            }
        }
        // The original prints "ERROR: no slot in which to store mine section %d" and exits.
        throw new InvalidOperationException($"no slot in which to store mine section {section}");
    }

    /// <summary>
    /// Loads section <paramref name="section"/> of mine <paramref name="t"/> as level <paramref name="levelNumber"/> into a free slot:
    /// a special map or a random unused general one, turned by <paramref name="rotation"/> (random unless the layout is fixed and
    /// the value is not negative), then walls, heights, doors, chests, piles and groups are made.
    /// </summary>
    // orig: R009+0x0D90 (1432:0D90) draft (close per the draft notes: 4 lines, compare operand
    // order); tests: Oracle:mine.json
    private void LoadSection(int t, int section, int levelNumber, ref int rotation, bool special)
    {
        var slotIndex = -1;
        if (section is >= 0 and < SectionCount)
        {
            slotIndex = SlotFor(section);
        }
        if (slotIndex is < 0 or >= SlotCount)
        {
            return;
        }
        var fixedLayout = IsFixedLayout(t);
        t = t switch { 0x82 => 0x0B, 0x83 => 0x0D, 0x84 => 0x0E, 0x85 => 0x0F, 0x86 => 0x10, _ => t };
        var level = ReadSectionMap(slotIndex, MapNumber(t, section, fixedLayout, _g.Context.ForceMap));
        if (fixedLayout && rotation >= 0)
        {
            rotation %= 8;
        }
        else
        {
            rotation = Roll(8) - 1;
        }
        MapTurn.Apply(level.Level, rotation, turnExitTargets: true);
        PrepareRecords(level, slotIndex, levelNumber, rotation, IsSmallMine(t));
        // C: the wall pass clears a 40-byte array in a frame that covers the slot the pile filler later reads as a stale word.
        _pileRollMade = false;
        BuildWalls(t, section);
        SetExitCards(t, section);
        MakeDoorFrames(t, section);
        MakePiles(t, section);
        if (special || _specialVariant)
        {
            ConvertExitMarkers(t, section);
        }
        MakeChests(t, section);
        PlaceMapGroups(t, section);
        if (level.StartX > 0 && level.StartY > 0)
        {
            _flags.OccupyAround(level.Number, level.StartX / 32, level.StartY / 16);
        }
    }

    /// <summary>Reads the chosen prepared map into a slot (a special map marks the slot, a general one the mine's list of used maps).</summary>
    private MineLevel ReadSectionMap(int slotIndex, int map)
    {
        string name;
        if (map >= 0)
        {
            name = string.Create(CultureInfo.InvariantCulture, $"iminspec.{map:000}");
            _specialSlots |= 1 << slotIndex;
        }
        else
        {
            var mapIndex = map is < 0 and > -100 ? -map : PickGeneralMap();
            name = string.Create(CultureInfo.InvariantCulture, $"iminegen.{mapIndex - 1:000}");
            MarkGeneralMapUsed(mapIndex - 1);
        }
        var level = new MineLevel(_g.Context.Maps.Load(name));
        _slots[slotIndex] = level;
        return level;
    }

    /// <summary>Records the turn and level number, keeps the file's exit count aside, and resets the cards, the object count and the flag map.</summary>
    private void PrepareRecords(MineLevel level, int slotIndex, int levelNumber, int rotation, bool smallMine)
    {
        var piles = (sbyte)level.PileCount;
        var pileIndex = 0;
        for (; piles > pileIndex; pileIndex++)
        {
            var pile = level.Pile(pileIndex);
            pile.Id = MapTurn.TurnFacing((byte)pile.Id, rotation);
        }
        level.PileCountLess1 = pileIndex - 1;
        level.Turn = rotation;
        level.Number = levelNumber;
        _fileExits[slotIndex] = level.ExitCount;
        level.ExitCount = 0;
        if (!smallMine)
        {
            level.RectangleCount = 0;
        }
        FindFirstGeneratedObject(level);
        level.Level.ExitCards = new LevelExitCards(-2, 0x2E, 0x0C, 0x2E, 0x0C);
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                _flags[levelNumber, x, y] = 0xFF;
            }
        }
    }

    /// <summary>The number of the map a section is loaded from: ≥ 0 a special map (iminspec.nnn), −1..−99 a given general map, −100 a random one.</summary>
    // C: numl is a signed word; the debugging override DS:C100 (forceMap, −1 in the shipped game) counts general maps from 1.
    private int MapNumber(int t, int section, bool fixedLayout, int forceMap)
    {
        var map = -100;
        if (fixedLayout && forceMap >= 0)
        {
            map = -forceMap;
        }
        else if (_specialVariant)
        {
            map = 13;
        }
        switch (t)
        {
            case 0x0A:
                if (forceMap >= 0)
                {
                    map = -forceMap;
                }
                break;
            case 0x0B:
                if (section == 13)
                {
                    map = 0;
                }
                else if (section == 14)
                {
                    map = 9;
                }
                break;
            case 0x0C or 0x7F or 0x80 or 0x81:
                map = 1;
                break;
            case 0x6C:
                map = 2;
                break;
            case 0x6D:
                map = 3;
                break;
            case 0x0E or 0x0F or 0x10:
                map = section switch { 10 => 10, 11 => 11, 12 => 12, 13 => 4, _ => map };
                break;
            case 0x66:
                map = 5;
                break;
            case 0x67:
                map = 6;
                break;
            case 0x68:
                map = 7;
                break;
            case 0x69:
                map = 8;
                break;
        }
        return map;
    }

    /// <summary>A random general map among those this mine has not used yet (one past its index, as the C counts).</summary>
    private int PickGeneralMap()
    {
        var unused = 0;
        for (var i = 0; i < GeneralMapCount; i++)
        {
            if (!_usedGeneralMaps[i])
            {
                unused++;
            }
        }
        if (unused <= 0)
        {
            unused = GeneralMapCount;
        }
        var wanted = Roll(unused);
        var seen = 0;
        var kn = 0;
        for (; kn < GeneralMapCount; kn++)
        {
            if (seen >= wanted)
            {
                break;
            }
            if (!_usedGeneralMaps[kn])
            {
                seen++;
            }
        }
        return kn;
    }

    private void MarkGeneralMapUsed(int index)
    {
        // DIVERGENCE: a debugging override beyond the 21 general maps would write past DS:E00D into the next table; ignored here.
        if (index is >= 0 and < GeneralMapCount)
        {
            _usedGeneralMaps[index] = true;
        }
    }

    /// <summary>The objects before the generator's own: the run of types 0, 1, 2, 4, 6 at the start of the object table.</summary>
    private static void FindFirstGeneratedObject(MineLevel level)
    {
        var count = 0;
        do
        {
            var type = level.Object(count).Type;
            if (type != 6 && type != 0 && type != 1 && type != 2 && type != 4)
            {
                break;
            }
        }
        while (++count < BattlefieldLevel.MaxObjects);
        level.ObjectCount = count;
    }

    /// <summary>Finishes a section: floor decoration, forgets the groups of a level the party has cleared, saves it and frees its slot.</summary>
    // orig: R009+0x3E1C (1432:3E1C) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    private void SaveSection(int t, int section)
    {
        var slot = SlotIndexOf(section);
        if (slot < 0)
        {
            return;
        }
        var level = _slots[slot]!;
        var number = level.Number;
        Decorate(t, section);
        var memory = _g.Context.Memory;
        if (_g.Persistent && (memory.ClearedLevels & (1 << number)) != 0 && (_specialSlots & (1 << slot)) == 0)
        {
            Forces.Groups[number].Clear();
            for (var i = 0; i < level.PileCount; i++)
            {
                var pile = level.Pile(i);
                pile.Entries = 0;
                pile.Amount = 0;
            }
        }
        _g.Save(level.Level);
        _fileExits[slot] = 0;
        _sectionOfSlot[slot] = -1;
        _slotOfSection[section] = -1;
        _specialSlots &= ~(1 << slot);
    }

    /// <summary>Random floor decoration (rubble and the like) on free cells whose neighbourhood allows it.</summary>
    // orig: R009+0x3BEA (1432:3BEA) draft (unmatched draft, far off per the draft notes); tests:
    // Oracle:mine.json
    private void Decorate(int t, int section)
    {
        var level = LevelOf(section);
        if (level is null)
        {
            return;
        }
        if ((t == 0x0B || t == 0x82) && section == 13)
        {
            return;
        }
        var odds = DecorationOdds(t, section);
        if (odds <= 0)
        {
            return;
        }
        // DS:13D0, the neighbourhood patterns the castle generator uses too.
        var rules = _g.Context.Tables.NeighbourhoodPatterns;
        var number = level.Number;
        for (var x = 1; x < 39; x++)
        {
            for (var y = 1; y < 39; y++)
            {
                if ((level.Cell(x, y) & 0x8000_0000) != 0)
                {
                    continue;
                }
                if ((_flags[number, x, y] & 3) != 0)
                {
                    continue;
                }
                if ((level.Cell(x, y) & 0xF0) != 0)
                {
                    continue;
                }
                if (Roll(odds) != 1)
                {
                    continue;
                }
                // The eight neighbours' flags say which sides of the cell are walled or taken: one mask for the east side and the
                // north row (m1), one for the west side and the south row (m2).
                var north = 0;
                if ((_flags[number, x, y + 1] & 0x81) != 0)
                {
                    north = 1;
                }
                if ((_flags[number, x - 1, y + 1] & 0xC1) != 0)
                {
                    north |= 2;
                }
                if ((_flags[number, x - 1, y] & 0x41) != 0)
                {
                    north |= 4;
                }
                if ((_flags[number, x - 1, y - 1] & 0x61) != 0)
                {
                    north |= 8;
                }
                var south = 0;
                if ((_flags[number, x, y - 1] & 0x21) != 0)
                {
                    south = 1;
                }
                if ((_flags[number, x + 1, y - 1] & 0x31) != 0)
                {
                    south |= 2;
                }
                if ((_flags[number, x + 1, y] & 0x11) != 0)
                {
                    south |= 4;
                }
                if ((_flags[number, x + 1, y + 1] & 0x91) != 0)
                {
                    south |= 8;
                }
                if ((rules[north] & (1 << south)) != 0)
                {
                    continue;
                }
                var decoration = RandomDecoration(t, section);
                if (decoration == 0)
                {
                    continue;
                }
                level.OrCell(x, y, (uint)(decoration & 0xF) << 4);
                level.OrCell(x, y, (uint)((Roll(4) - 1) & 3) << 28);
                _flags.Or(number, x, y, 1);
                level.SetFloor(x, y, (byte)(level.Floor(x, y) & 0xF0));
            }
        }
    }
}
