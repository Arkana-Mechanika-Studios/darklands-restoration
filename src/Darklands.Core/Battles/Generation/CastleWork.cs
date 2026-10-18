namespace Darklands.Core.Battles.Generation;

/// <summary>
/// The castle generator's work area: one 64 KB segment holding, for up to five levels, the room
/// rectangles, the free strips around the keep, the stair state of each room, the room graph, the
/// wall bits, the cell kinds, the room kinds, the cell marks and the stairs. It is kept as the
/// original lays it out (fields at fixed offsets, every access wrapping inside the segment) because
/// the original indexes the arrays without bounds and its pointer arithmetic runs across arrays
/// and levels.
/// </summary>
// orig: work segment gen_work = DS:C105 (struct gwork in; offsets checked against gen_clear R005+0x005C asm)
// UNCERTAIN: the original allocates the segment once and gen_clear leaves a few fields alone (the stairs' link and side words, the
// area beyond 0xBA1D); they start at 0 here and keep their values between calls.
internal sealed class CastleWork
{
    public const int Levels = 5;
    public const int MaxRooms = 30;
    public const int Cells = 40;
    public const int MaxStairs = 4;

    // Offsets of the fields (struct gwork).
    private const int RoomsAt = 0x0000;
    private const int StripsAt = 0x04B0;
    private const int StairStateAt = 0x07D0;
    private const int LinksAt = 0x08FC;
    private const int WallsAt = 0x1BC1;
    private const int CellKindAt = 0x3B01;
    private const int RoomKindAt = 0x7981;
    private const int MarksAt = 0x7AAD;
    private const int StairsAt = 0xB92D;

    private readonly byte[] _memory = new byte[0x10000];

    /// <summary>A word of the segment (wraps at 64 KB like a 16-bit offset).</summary>
    public int Word(int at)
    {
        at &= 0xFFFF;
        return (short)(_memory[at] | _memory[(at + 1) & 0xFFFF] << 8);
    }

    public void SetWord(int at, int value)
    {
        at &= 0xFFFF;
        _memory[at] = (byte)value;
        _memory[(at + 1) & 0xFFFF] = (byte)(value >> 8);
    }

    // ---- rooms: {x0, x1, y0, y1} for 30 rooms of each level ----

    /// <summary>Room rectangle word <paramref name="field"/> (0 x0, 1 x1, 2 y0, 3 y1) of a room.</summary>
    public int Room(int level, int room, int field) => Word(RoomsAt + ((level * MaxRooms + room) * 4 + field) * 2);

    public void SetRoom(int level, int room, int field, int value) => SetWord(RoomsAt + ((level * MaxRooms + room) * 4 + field) * 2, value);

    // ---- free strips: {x0, x1, y0, y1, side} for 16 strips of each level ----

    /// <summary>Strip word <paramref name="field"/> (0 x0, 1 x1, 2 y0, 3 y1, 4 side) of a strip.</summary>
    public int Strip(int level, int slot, int field) => Word(StripsAt + ((level * 16 + slot) * 5 + field) * 2);

    public void SetStrip(int level, int slot, int field, int value) => SetWord(StripsAt + ((level * 16 + slot) * 5 + field) * 2, value);

    /// <summary>A strip word by its index in the level's array of ints (the original sometimes indexes it as int[5][16][5]).</summary>
    public int StripWord(int level, int index) => Word(StripsAt + (level * 80 + index) * 2);

    public void SetStripWord(int level, int index, int value) => SetWord(StripsAt + (level * 80 + index) * 2, value);

    // ---- stair state of each room ----

    public int StairState(int level, int room) => Word(StairStateAt + (level * MaxRooms + room) * 2);

    public void SetStairState(int level, int room, int value) => SetWord(StairStateAt + (level * MaxRooms + room) * 2, value);

    // ---- room graph: 31 × 31 bytes per level (room 30 stands for the outside) ----

    public int Link(int level, int a, int b) => _memory[LinksAt + (level * 31 + a) * 31 + b];

    public void SetLink(int level, int a, int b, int value) => _memory[LinksAt + (level * 31 + a) * 31 + b] = (byte)value;

    // ---- per-cell data: 40 × 40 cells per level, indexed (level * 40 + x) * 40 + y ----

    /// <summary>Wall bits of a cell by cell index: 1 north (y−), 2 east (x+), 4 south (y+), 8 west (x−).</summary>
    public int Walls(int cell) => _memory[(WallsAt + cell) & 0xFFFF];

    public void SetWalls(int cell, int value) => _memory[(WallsAt + cell) & 0xFFFF] = (byte)value;

    /// <summary>Kind of a cell by cell index: 0/1 solid, 2..8 floor of a room of that kind, 9 stairs up, 10 stairs down, 11 occupied.</summary>
    public int Kind(int cell) => Word(CellKindAt + cell * 2);

    public void SetKind(int cell, int value) => SetWord(CellKindAt + cell * 2, value);

    /// <summary>The mark of a cell by cell index (doors 3..6, stair fronts 7/10/11, objects and groups 12..15, party start 16).</summary>
    public int Mark(int cell) => Word(MarksAt + cell * 2);

    public void SetMark(int cell, int value) => SetWord(MarksAt + cell * 2, value);

    // ---- room kinds ----

    public int RoomKind(int level, int room) => Word(RoomKindAt + (level * MaxRooms + room) * 2);

    public void SetRoomKind(int level, int room, int value) => SetWord(RoomKindAt + (level * MaxRooms + room) * 2, value);

    // ---- stairs: 4 records of 6 words per level ----

    /// <summary>Stair word: 0 x, 1 y, 2 room, 3 partner stair on the neighbouring level, 4 direction (1 up, 2 down), 5 side (0 or 3).</summary>
    public int StairWord(int level, int stair, int field) => Word(StairsAt + ((level * MaxStairs + stair) * 6 + field) * 2);

    public void SetStairWord(int level, int stair, int field, int value) => SetWord(StairsAt + ((level * MaxStairs + stair) * 6 + field) * 2, value);

    // ---- gen_clear ----

    /// <summary>Clears the work area for a new battlefield.</summary>
    // orig: gen_clear R005+0x005C (1432:005C) draft; checked against asm
    public void Clear()
    {
        for (var level = 0; level < Levels; level++)
        {
            for (var a = 0; a < 31; a++)
            {
                for (var b = 0; b < 31; b++)
                {
                    SetLink(level, a, b, 0);
                }
            }
            for (var room = 0; room < MaxRooms; room++)
            {
                for (var field = 0; field < 4; field++)
                {
                    SetRoom(level, room, field, -1);
                }
                SetRoomKind(level, room, 0);
                SetStairState(level, room, 0);
            }
            for (var cell = level * Cells * Cells; cell < (level + 1) * Cells * Cells; cell++)
            {
                SetWalls(cell, 0);
                SetMark(cell, 0);
                SetKind(cell, 0);
            }
            for (var slot = 0; slot < 16; slot++)
            {
                for (var field = 0; field < 4; field++)
                {
                    SetStrip(level, slot, field, -1);
                }
                SetStrip(level, slot, 4, 8);
            }
            for (var stair = 0; stair < MaxStairs; stair++)
            {
                SetStairWord(level, stair, 4, 0);
                SetStairWord(level, stair, 2, -1);
                SetStairWord(level, stair, 1, -1);
                SetStairWord(level, stair, 0, -1);
            }
        }
    }
}
