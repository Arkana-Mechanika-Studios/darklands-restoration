using System.Buffers.Binary;

namespace Darklands.Core.Battles;

/// <summary>
/// One level of a battlefield (the floor image, <c>level%d.flr</c>, 0x33FC bytes): 40 × 40
/// cells of 32 × 16 map pixels, each with its walls and furniture and a floor tile, then the
/// level's records: where the party enters, the cards for each way of leaving, doors, marked
/// spots (objects), exits, and raised-edge rectangles. The image is kept whole, as the
/// generators and the battle read and write it; the accessors give its parts names.
/// </summary>
// orig: floor_img R136+0x0000 (0x33FC bytes, level%d.flr); layout as read by floor_view_draw, map_turn and the R010 generator
// UNCERTAIN: records not used yet (part lists +0x2488, chests +0x300C, door tables +0x3350/+0x3382) are kept as bytes only.
public sealed class BattlefieldLevel
{
    public const int Size = 40;
    public const int CellWidth = 32;
    public const int CellHeight = 16;
    public const int ImageSize = 0x33FC;

    public const int MaxDoors = 40;
    public const int MaxObjects = 55;
    public const int MaxExits = 6;
    public const int RecordSize = 14;

    private const int FloorBytes = 0x1900;
    private const int LevelNumberAt = 0x1F40;
    private const int ExitCardsAt = 0x1F42;
    private const int PartyStartAt = 0x1F4C;
    private const int DoorCountAt = 0x1F52;
    private const int DoorsAt = 0x1F54;
    private const int ObjectCountAt = 0x2184;
    private const int ObjectsAt = 0x2186;
    private const int PartListCountAt = 0x2488;
    private const int ExitCountAt = 0x2FB6;
    private const int ExitsAt = 0x2FB8;
    private const int RectangleCountAt = 0x3326;
    private const int RectanglesAt = 0x3328;

    /// <summary>A new level with every cell void.</summary>
    public BattlefieldLevel()
        : this(new byte[ImageSize])
    {
        for (var i = 0; i < Size * Size; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Image.AsSpan(4 * i), BattlefieldCell.Void.Bits);
        }
    }

    /// <summary>A level over a floor image (an IMAPS member or a saved level).</summary>
    public BattlefieldLevel(byte[] image)
    {
        if (image.Length < ImageSize)
        {
            throw new ArgumentException("A floor image is 0x33FC bytes.", nameof(image));
        }
        Image = image;
    }

    /// <summary>The floor image itself.</summary>
    public byte[] Image { get; }

    public BattlefieldLevel Clone() => new((byte[])Image.Clone());

    /// <summary>The cell at column <paramref name="x"/>, row <paramref name="y"/>; void outside the level.</summary>
    public BattlefieldCell this[int x, int y]
    {
        get => (uint)x < Size && (uint)y < Size ? new BattlefieldCell(BinaryPrimitives.ReadUInt32LittleEndian(Image.AsSpan(CellAt(x, y)))) : BattlefieldCell.Void;
        set
        {
            if ((uint)x < Size && (uint)y < Size)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Image.AsSpan(CellAt(x, y)), value.Bits);
            }
        }
    }

    /// <summary>Floor tile number (high nibble of the floor byte, 0 none).</summary>
    public int FloorTile(int x, int y) => FloorByte(x, y) >> 4;

    /// <summary>Floor decoration tile number (low nibble of the floor byte).</summary>
    public int FloorDecoration(int x, int y) => FloorByte(x, y) & 0xF;

    public byte FloorByte(int x, int y) => (uint)x < Size && (uint)y < Size ? Image[FloorBytes + x * Size + y] : (byte)0;

    public void SetFloorByte(int x, int y, byte value)
    {
        if ((uint)x < Size && (uint)y < Size)
        {
            Image[FloorBytes + x * Size + y] = value;
        }
    }

    /// <summary>+0x1F40: the number the level is saved under.</summary>
    public int LevelNumber
    {
        get => Image[LevelNumberAt];
        set => Image[LevelNumberAt] = (byte)value;
    }

    /// <summary>+0x1F42: the cards each way of leaving the level leads to.</summary>
    public LevelExitCards ExitCards
    {
        get => new(Word(ExitCardsAt), Word(ExitCardsAt + 2), Word(ExitCardsAt + 4), Word(ExitCardsAt + 6), Word(ExitCardsAt + 8));
        set
        {
            PutWord(ExitCardsAt, value.Won);
            PutWord(ExitCardsAt + 2, value.Unknown);
            PutWord(ExitCardsAt + 4, value.Fled);
            PutWord(ExitCardsAt + 6, value.Lost);
            PutWord(ExitCardsAt + 8, value.LeftBeforeFight);
        }
    }

    /// <summary>+0x1F4C/+0x1F4E: where the party enters (map pixels).</summary>
    public MapPoint PartyStart
    {
        get => new(Word(PartyStartAt), Word(PartyStartAt + 2));
        set
        {
            PutWord(PartyStartAt, value.X);
            PutWord(PartyStartAt + 2, value.Y);
        }
    }

    /// <summary>+0x1F50: the direction the party faces (0..7).</summary>
    public int PartyFacing
    {
        get => Image[PartyStartAt + 4];
        set => Image[PartyStartAt + 4] = (byte)value;
    }

    /// <summary>+0x1F51: the room the party starts in (−1 none).</summary>
    public int PartyRoom
    {
        get => (sbyte)Image[PartyStartAt + 5];
        set => Image[PartyStartAt + 5] = (byte)value;
    }

    /// <summary>+0x1F52: the number of door records.</summary>
    public int DoorCount
    {
        get => Image[DoorCountAt];
        set => Image[DoorCountAt] = (byte)value;
    }

    /// <summary>A door record (14 bytes at +0x1F54).</summary>
    public Span<byte> Door(int index) => Image.AsSpan(DoorsAt + RecordSize * index, RecordSize);

    /// <summary>+0x2184: the number of objects (marked spots).</summary>
    public int ObjectCount
    {
        get => Image[ObjectCountAt];
        set => Image[ObjectCountAt] = (byte)value;
    }

    /// <summary>An object record (+0x2186, 14 bytes).</summary>
    public BattlefieldObject Object(int index)
    {
        var r = Image.AsSpan(ObjectsAt + RecordSize * index, RecordSize);
        return new BattlefieldObject(Word(r, 0), Word(r, 2), r[4], r[5], Word(r, 6), Word(r, 8), Word(r, 10), Word(r, 12));
    }

    public void SetObject(int index, BattlefieldObject o)
    {
        var at = ObjectsAt + RecordSize * index;
        PutWord(at, o.X);
        PutWord(at + 2, o.Y);
        Image[at + 4] = (byte)o.Width;
        Image[at + 5] = (byte)o.Height;
        PutWord(at + 6, o.Type);
        PutWord(at + 8, o.Word8);
        PutWord(at + 10, o.Id);
        PutWord(at + 12, o.Room);
    }

    /// <summary>+0x2488: the number of part lists.</summary>
    public int PartListCount
    {
        get => Image[PartListCountAt];
        set => Image[PartListCountAt] = (byte)value;
    }

    /// <summary>+0x2FB6: the number of exits.</summary>
    public int ExitCount
    {
        get => Image[ExitCountAt];
        set => Image[ExitCountAt] = (byte)value;
    }

    /// <summary>An exit record (14 bytes at +0x2FB8).</summary>
    public Span<byte> Exit(int index) => Image.AsSpan(ExitsAt + RecordSize * index, RecordSize);

    /// <summary>+0x3326: the number of raised-edge rectangles.</summary>
    public int RectangleCount => Image[RectangleCountAt];

    /// <summary>A rectangle (+0x3328, 10 bytes: x, y, width, height in map pixels).</summary>
    public (int X, int Y, int Width, int Height) Rectangle(int index)
    {
        var at = RectanglesAt + 10 * index;
        return (Word(at), Word(at + 2), Word(at + 4), Word(at + 6));
    }

    /// <summary>A byte of the image (the records the battle reads and writes: doors, triggers, chests …).</summary>
    public byte GetByte(int at) => Image[at];

    public void SetByte(int at, int value) => Image[at] = (byte)value;

    /// <summary>A signed word of the image.</summary>
    public short GetWord(int at) => Word(at);

    public void SetWord(int at, int value) => PutWord(at, value);

    /// <summary>Byte <paramref name="k"/> (0..3) of the cell at column <paramref name="x"/>, row <paramref name="y"/> (no bounds test: the original indexes the image).</summary>
    // C: x * 40 + y may fall outside the cells (into the floor bytes and records after them, or before the image): kept inside the image here.
    public byte CellByte(int x, int y, int k) => Image[Math.Clamp(CellAt(x, y) + k, 0, ImageSize - 1)];

    public void SetCellByte(int x, int y, int k, int value)
    {
        var at = CellAt(x, y) + k;
        if (at >= 0 && at < ImageSize)
        {
            Image[at] = (byte)value;
        }
    }

    /// <summary>True when the cell is not void.</summary>
    public bool IsInside(int x, int y) => !this[x, y].IsVoid;

    private static int CellAt(int x, int y) => (x * Size + y) * 4;

    private short Word(int at) => BinaryPrimitives.ReadInt16LittleEndian(Image.AsSpan(at));

    private static short Word(ReadOnlySpan<byte> r, int at) => BinaryPrimitives.ReadInt16LittleEndian(r[at..]);

    private void PutWord(int at, int value) => BinaryPrimitives.WriteInt16LittleEndian(Image.AsSpan(at), (short)value);
}

/// <summary>An object record of a level: a marked spot (type 0x65 group markers with an id), a door piece, a trigger ...</summary>
/// <param name="Type">+6: 0x65 marks a spot for the generator; −1 a deleted marker.</param>
/// <param name="Id">+0x0A: which spot (party starts 21..23, enemy spots 2..9, reinforcement entries 11..14).</param>
/// <param name="Room">+0x0C: room id (−1 none) or another value by type.</param>
public readonly record struct BattlefieldObject(int X, int Y, int Width, int Height, int Type, int Word8, int Id, int Room)
{
    public const int GroupMarker = 0x65;

    /// <summary>The centre of a one-cell marker.</summary>
    public MapPoint Centre => new(X + 16, Y + 8);
}

/// <summary>The cards a level leads to (floor image +0x1F42 won, +0x1F44 ?, +0x1F46 fled, +0x1F48 lost, +0x1F4A left without a fight).</summary>
public readonly record struct LevelExitCards(int Won, int Unknown, int Fled, int Lost, int LeftBeforeFight);

/// <summary>A position in map pixels.</summary>
public readonly record struct MapPoint(int X, int Y)
{
    public int Column => X >> 5;

    public int Row => Y >> 4;

    public static MapPoint CentreOf(int column, int row) => new(column * BattlefieldLevel.CellWidth + 16, row * BattlefieldLevel.CellHeight + 8);
}
