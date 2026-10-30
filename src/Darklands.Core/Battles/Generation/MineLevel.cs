using System.Buffers.Binary;

namespace Darklands.Core.Battles.Generation;

/// <summary>
/// A mine section while it is built: its floor image (the prepared map "iminegen.nnn" / "iminspec.nnn" of IMAPS.CAT, in one of
/// the four level slots) with the records the mine generator reads and writes given names. The image stays whole bytes, so
/// quirks that depend on the byte layout (stale fields, copies of single fields) behave as in the original.
/// </summary>
// orig: struct level (draft draft; offsets checked against the asm of the functions that use them)
internal sealed class MineLevel(BattlefieldLevel level)
{
    private const int HeightsAt = 0x1900;
    private const int ObjectCountAt = 0x2184;
    private const int ObjectsAt = 0x2186;
    private const int PartListsAt = 0x248A;
    private const int PartCellsAt = 0x24C6;
    private const int ExitsAt = 0x2FB8;
    private const int PilesAt = 0x300E;
    private const int ChestsAt = 0x316E;
    private const int FloorItemsAt = 0x3236;
    private const int RectanglesAt = 0x3328;
    private const int DoorSpotsAt = 0x3350;
    private const int WallGapSpotsAt = 0x3382;

    /// <summary>The longest part-cell list (words from +0x24C6).</summary>
    public const int PartCellWords = 0x578;

    public BattlefieldLevel Level { get; } = level;

    public byte[] Image => Level.Image;

    /// <summary>+0x1F40: the level number the section is saved under.</summary>
    public int Number
    {
        get => (sbyte)Image[0x1F40];
        set => Image[0x1F40] = (byte)value;
    }

    /// <summary>A cell (long at cell[x][y]); an index past the 40 × 40 table reads the neighbouring row, as in the original.</summary>
    // C: cell[x][y] is a flat table: a y of 40 is the next column's cell 0. Outside the table nothing is read (0) or written.
    public uint Cell(int x, int y)
    {
        var at = 4 * (40 * x + y);
        return at is >= 0 and <= HeightsAt - 4 ? BinaryPrimitives.ReadUInt32LittleEndian(Image.AsSpan(at)) : 0;
    }

    public void SetCell(int x, int y, uint value)
    {
        var at = 4 * (40 * x + y);
        if (at is >= 0 and <= HeightsAt - 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Image.AsSpan(at), value);
        }
    }

    /// <summary>cell[x][y] |= bits.</summary>
    public void OrCell(int x, int y, uint bits) => SetCell(x, y, Cell(x, y) | bits);

    /// <summary>cell[x][y] &amp;= mask.</summary>
    public void AndCell(int x, int y, uint mask) => SetCell(x, y, Cell(x, y) & mask);

    /// <summary>hgt[x][y]: the floor byte (floor tile in the high nibble, decoration in the low).</summary>
    public byte Floor(int x, int y)
    {
        var at = HeightsAt + 40 * x + y;
        return at is >= HeightsAt and < HeightsAt + 1600 ? Image[at] : (byte)0;
    }

    public void SetFloor(int x, int y, byte value)
    {
        var at = HeightsAt + 40 * x + y;
        if (at is >= HeightsAt and < HeightsAt + 1600)
        {
            Image[at] = value;
        }
    }

    /// <summary>+0x1F4C/+0x1F4E: where the party enters.</summary>
    public int StartX
    {
        get => Word(0x1F4C);
        set => PutWord(0x1F4C, value);
    }

    public int StartY
    {
        get => Word(0x1F4E);
        set => PutWord(0x1F4E, value);
    }

    /// <summary>+0x1F52: how many door records the map has (the draft calls them r14).</summary>
    public int DoorCount => Image[0x1F52];

    /// <summary>A door record (+0x1F54): word 0 a flag (negative = no door piece), +8 and +9 the cell with its side flags.</summary>
    public DoorRecord Door(int index) => new(Image, 0x1F54 + 14 * index);

    /// <summary>+0x2184: the objects that come before the generator's own (types 0, 1, 2, 4, 6).</summary>
    public int ObjectCount
    {
        get => Image[ObjectCountAt];
        set => Image[ObjectCountAt] = (byte)value;
    }

    public BattlefieldObject Object(int index) => Level.Object(index);

    public void SetObject(int index, BattlefieldObject value) => Level.SetObject(index, value);

    /// <summary>+0x2488: how many part lists (rooms) the level has.</summary>
    public int PartListCount
    {
        get => Image[0x2488];
        set => Image[0x2488] = (byte)value;
    }

    /// <summary>+0x248A + 2·i: the byte offset of part list i in the cell table.</summary>
    public int PartListStart(int index) => Word(PartListsAt + 2 * index);

    public void SetPartListStart(int index, int value) => PutWord(PartListsAt + 2 * index, value);

    /// <summary>+0x24C6 + 2·i: a word of the part-cell table ((x &lt;&lt; 8) + y, −1 ends a list).</summary>
    public int PartCell(int index) => index is >= 0 and < PartCellWords ? Word(PartCellsAt + 2 * index) : 0;

    public void SetPartCell(int index, int value)
    {
        if (index is >= 0 and < PartCellWords)
        {
            PutWord(PartCellsAt + 2 * index, value);
        }
    }

    /// <summary>+0x2FB6: how many exits are in use.</summary>
    public int ExitCount
    {
        get => Image[0x2FB6];
        set => Image[0x2FB6] = (byte)value;
    }

    public ExitRecord Exit(int index) => new(Image, ExitsAt + 14 * index);

    /// <summary>Copies exit <paramref name="from"/> onto exit <paramref name="to"/> (14 bytes).</summary>
    public void CopyExit(int to, int from) => Image.AsSpan(ExitsAt + 14 * from, 14).CopyTo(Image.AsSpan(ExitsAt + 14 * to, 14));

    public byte[] ExitBytes(int index) => Image.AsSpan(ExitsAt + 14 * index, 14).ToArray();

    public void SetExitBytes(int index, byte[] bytes) => bytes.CopyTo(Image.AsSpan(ExitsAt + 14 * index, 14));

    /// <summary>+0x300C: how many item piles the section has.</summary>
    public int PileCount
    {
        get => Image[0x300C];
        set => Image[0x300C] = (byte)value;
    }

    public PileRecord Pile(int index) => new(Image, PilesAt + 0x2C * index);

    /// <summary>A chest record (+0x316E, 10 bytes).</summary>
    public ChestRecord Chest(int index) => new(Image, ChestsAt + 10 * index);

    /// <summary>A floor-item record (+0x3236, 12 bytes: four cell x, then four cell y).</summary>
    public FloorItemRecord FloorItem(int index) => new(Image, FloorItemsAt + 12 * index);

    /// <summary>+0x3326: how many start rectangles.</summary>
    public int RectangleCount
    {
        get => Image[0x3326];
        set => Image[0x3326] = (byte)value;
    }

    /// <summary>Rectangle 0 (+0x3328): x, y, width, height.</summary>
    public void SetStartRectangle(int x, int y, int width, int height)
    {
        PutWord(RectanglesAt, x);
        PutWord(RectanglesAt + 2, y);
        PutWord(RectanglesAt + 4, width);
        PutWord(RectanglesAt + 6, height);
    }

    public (int X, int Y, int Width, int Height) StartRectangle =>
        (Word(RectanglesAt), Word(RectanglesAt + 2), Word(RectanglesAt + 4), Word(RectanglesAt + 6));

    /// <summary>+0x3346: the number of item piles in the prepared map, less one (a leftover of the original's loop counter).</summary>
    public int PileCountLess1
    {
        set => PutWord(0x3346, value);
    }

    /// <summary>+0x3348: how the section was turned (the rotation index).</summary>
    public int Turn
    {
        set => PutWord(0x3348, value);
    }

    /// <summary>A door-spot record (+0x3350, 10 bytes).</summary>
    public DoorSpotRecord DoorSpot(int index) => new(Image, DoorSpotsAt + 10 * index);

    /// <summary>A wall-gap record (+0x3382, 10 bytes).</summary>
    public WallGapRecord WallGap(int index) => new(Image, WallGapSpotsAt + 10 * index);

    private int Word(int at) => BinaryPrimitives.ReadInt16LittleEndian(Image.AsSpan(at));

    private void PutWord(int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(Image.AsSpan(at), (short)value);
}

/// <summary>A record in a level image: a byte offset with word and byte access.</summary>
internal readonly struct ImageRecord(byte[] image, int at)
{
    public int Word(int offset) => BinaryPrimitives.ReadInt16LittleEndian(image.AsSpan(at + offset));

    public void SetWord(int offset, int value) => BinaryPrimitives.WriteInt16LittleEndian(image.AsSpan(at + offset), (short)value);

    public byte Byte(int offset) => image[at + offset];

    public sbyte SByte(int offset) => (sbyte)image[at + offset];

    public void SetByte(int offset, int value) => image[at + offset] = (byte)value;
}

/// <summary>A door record of the prepared map (14 bytes at +0x1F54).</summary>
internal readonly struct DoorRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    /// <summary>+0: the position word; negative marks a door without a piece.</summary>
    public int Flag => _r.Word(0);

    /// <summary>+8: cell x of the first half (low 6 bits) with side flags 0x80 and 0x40.</summary>
    public int FirstX => _r.Byte(8);

    public int FirstY => _r.Byte(9);
}

/// <summary>An exit of a section (14 bytes at +0x2FB8): the door-like strip on the edge and where it leads.</summary>
internal readonly struct ExitRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    /// <summary>+0/+2: the strip's position (map pixels); +4/+5 its size.</summary>
    public int X { get => _r.Word(0); set => _r.SetWord(0, value); }

    public int Y { get => _r.Word(2); set => _r.SetWord(2, value); }

    public int Width { get => _r.Byte(4); set => _r.SetByte(4, value); }

    public int Height { get => _r.Byte(5); set => _r.SetByte(5, value); }

    /// <summary>+6: the level number on the other side.</summary>
    public int TargetLevel { get => _r.SByte(6); set => _r.SetByte(6, value); }

    /// <summary>+7: the part list (room) on the other side the party arrives in.</summary>
    public int ArrivalRoom { get => _r.SByte(7); set => _r.SetByte(7, value); }

    /// <summary>+8/+0x0A: where the party arrives (map pixels).</summary>
    public int ArrivalX { get => _r.Word(8); set => _r.SetWord(8, value); }

    public int ArrivalY { get => _r.Word(10); set => _r.SetWord(10, value); }

    /// <summary>+0x0C: the facing on arrival (0..7).</summary>
    public int Facing { get => _r.SByte(12); set => _r.SetByte(12, value); }

    /// <summary>+0x0D: the exit's index on the other side.</summary>
    public int TargetExit { get => _r.SByte(13); set => _r.SetByte(13, value); }
}

/// <summary>
/// An item pile of a section (0x2C bytes at +0x300E): a spot in the prepared map where the generator drops loot (or, for a few
/// sections, makes a locked door), with room for four item entries.
/// </summary>
// C: the draft calls these "groups" (struct grp2); they hold item types and weights (battlefield-item-table +0x25), hence "pile".
internal readonly struct PileRecord(byte[] image, int at)
{
    private const int EntriesAt = 0x14;
    private const int EntrySize = 6;
    private readonly ImageRecord _r = new(image, at);

    /// <summary>+0/+2: position (map pixels); +4/+5 size.</summary>
    public int X { get => _r.Word(0); set => _r.SetWord(0, value); }

    public int Y { get => _r.Word(2); set => _r.SetWord(2, value); }

    public int Width { get => _r.Byte(4); set => _r.SetByte(4, value); }

    public int Height { get => _r.Byte(5); set => _r.SetByte(5, value); }

    /// <summary>+6/+7: the cell.</summary>
    public int CellX { get => _r.Byte(6); set => _r.SetByte(6, value); }

    public int CellY { get => _r.Byte(7); set => _r.SetByte(7, value); }

    /// <summary>+8: flags (0x80 and the section's strength).</summary>
    public int Flags { get => _r.Byte(8); set => _r.SetByte(8, value); }

    /// <summary>+9.</summary>
    public int Byte9 { get => _r.SByte(9); set => _r.SetByte(9, value); }

    /// <summary>+0x0A.</summary>
    public int ByteA { get => _r.SByte(10); set => _r.SetByte(10, value); }

    /// <summary>+0x0B: how many entries are filled (0..4).</summary>
    public int Entries { get => _r.Byte(0x0B); set => _r.SetByte(0x0B, value); }

    /// <summary>+0x0C: the pile's id (a running number across sections); before the generator the facing of the spot.</summary>
    public int Id { get => _r.SByte(0x0C); set => _r.SetByte(0x0C, value); }

    /// <summary>+0x10: a number 1..10 drawn for the pile (its money?).</summary>
    public int Amount { get => _r.Word(0x10); set => _r.SetWord(0x10, value); }

    /// <summary>Entry i: item type (word), two quality bytes, count and weight.</summary>
    public void SetEntry(int i, int type, int byte2, int byte3, int count, int weight)
    {
        var e = EntriesAt + EntrySize * i;
        _r.SetWord(e, type);
        _r.SetByte(e + 2, byte2);
        _r.SetByte(e + 3, byte3);
        _r.SetByte(e + 4, count);
        _r.SetByte(e + 5, weight);
    }

    /// <summary>Copies the position, size and cell of another pile onto this one (the compaction of the list copies only these).</summary>
    public void CopyPlaceFrom(PileRecord other)
    {
        _r.SetWord(0, other._r.Word(0));
        _r.SetWord(2, other._r.Word(2));
        _r.SetByte(4, other._r.Byte(4));
        _r.SetByte(5, other._r.Byte(5));
        _r.SetByte(6, other._r.Byte(6));
        _r.SetByte(7, other._r.Byte(7));
    }
}

/// <summary>A chest record (10 bytes at +0x316E).</summary>
internal readonly struct ChestRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    /// <summary>+0: the chest's id word.</summary>
    public int Id { get => _r.Word(0); set => _r.SetWord(0, value); }

    /// <summary>+2/+3: contents (item, quality) as a floor pile; +6 a grade 0..3; +8/+9 contents (item, quality) of a chest.</summary>
    public int Byte2 { set => _r.SetByte(2, value); }

    public int Byte3 { set => _r.SetByte(3, value); }

    public int Grade { set => _r.SetByte(6, value); }

    public int Byte8 { set => _r.SetByte(8, value); }

    public int Byte9 { set => _r.SetByte(9, value); }
}

/// <summary>A floor-item record (12 bytes at +0x3236): up to four cells where a type-1 object's items lie.</summary>
internal readonly struct FloorItemRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    public int Cell(int i) => _r.Byte(8 + i);

    public void SetCell(int i, int value) => _r.SetByte(8 + i, value);
}

/// <summary>A door-spot record (10 bytes at +0x3350): where a door the generator adds stands and which way it faces.</summary>
internal readonly struct DoorSpotRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    public int Byte0 { set => _r.SetByte(0, value); }

    /// <summary>+5: the facing.</summary>
    public int Facing { get => _r.SByte(5); set => _r.SetByte(5, value); }

    /// <summary>+6/+8: the position (map pixels).</summary>
    public int X { get => _r.Word(6); set => _r.SetWord(6, value); }

    public int Y { get => _r.Word(8); set => _r.SetWord(8, value); }
}

/// <summary>A wall-gap record (10 bytes at +0x3382): the carved gap in a wall, its cell with side flag, its room and the exit beyond.</summary>
internal readonly struct WallGapRecord(byte[] image, int at)
{
    private readonly ImageRecord _r = new(image, at);

    /// <summary>+0: 0 for a gap the generator carved.</summary>
    public int Byte0 { set => _r.SetByte(0, value); }

    /// <summary>+1: cell x (with side flags 0x80 and 0x40); 0 marks the record free.</summary>
    public int Byte1 { get => _r.Byte(1); set => _r.SetByte(1, value); }

    /// <summary>+2: cell y (with side flags 0x80 and 0x40).</summary>
    public int Byte2 { get => _r.Byte(2); set => _r.SetByte(2, value); }

    /// <summary>+3: the part list.</summary>
    public int Byte3 { get => _r.SByte(3); set => _r.SetByte(3, value); }

    /// <summary>+4/+6: where the party arrives from the gap.</summary>
    public int X { get => _r.Word(4); set => _r.SetWord(4, value); }

    public int Y { get => _r.Word(6); set => _r.SetWord(6, value); }

    /// <summary>+8: the facing.</summary>
    public int Facing { get => _r.SByte(8); set => _r.SetByte(8, value); }
}

/// <summary>
/// The flag maps of the mine's levels (40 × 40 bytes per level, one block for all sixteen): wall sides 0x80/0x40/0x20/0x10 of the
/// cell, 2 = occupied, 1 = marked. Indexes run on into the next row and level, as in the original.
/// </summary>
// orig: DS:E033 (based on DS:DFC3), 0x6400 bytes (draft draft)
internal sealed class MineFlags
{
    public const int Levels = 16;
    private const int Margin = 160;
    private readonly byte[] _flags = new byte[Levels * 1600 + 2 * Margin];

    public byte this[int level, int x, int y]
    {
        get
        {
            var at = Margin + level * 1600 + 40 * x + y;
            return at >= 0 && at < _flags.Length ? _flags[at] : (byte)0;
        }
        set
        {
            var at = Margin + level * 1600 + 40 * x + y;
            if (at >= 0 && at < _flags.Length)
            {
                _flags[at] = value;
            }
        }
    }

    /// <summary>flags[x][y] |= bits.</summary>
    public void Or(int level, int x, int y, int bits) => this[level, x, y] = (byte)(this[level, x, y] | bits);

    /// <summary>Marks the 3 × 3 cells around a cell as occupied.</summary>
    public void OccupyAround(int level, int x, int y)
    {
        for (var xx = x - 1; xx <= x + 1; xx++)
        {
            for (var yy = y - 1; yy <= y + 1; yy++)
            {
                Or(level, xx, yy, 2);
            }
        }
    }

    /// <summary>Marks a block of cells as occupied (x0..x1, y0..y1).</summary>
    public void OccupyBlock(int level, int x0, int x1, int y0, int y1)
    {
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                Or(level, x, y, 2);
            }
        }
    }
}
