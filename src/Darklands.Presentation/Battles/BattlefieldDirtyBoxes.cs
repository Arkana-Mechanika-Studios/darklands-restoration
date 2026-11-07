namespace Darklands.Presentation.Battles;

/// <summary>
/// The view slots a frame's sprites touch, each with the box (relative to the view) those sprites cover: the battle redraws the wall
/// pieces of these slots in its depth-sorted list, clipped to the slot's box, so a piece that stands in front of a sprite covers it.
/// Only the boxes are kept: the screen is still drawn whole every frame (the original redraws only these tiles).
/// </summary>
// orig: R014+0x7BBE (14F7:13AE) checked against the asm: a sprite's box (entry +6..+0xC, clipped to the view by R014+0x8680) grows the
// C: boxes DS:E16A / E16C / E16E / E170 (x0, y0, x1, y1; reset to 9999 / −9999 by R014+0x6818) of the 23 slots round the slot of its map
// C: point (base = DS:1A5D[r] + 10r + c + 0x4B for r = (y − DS:C293) >> 4, c = (x − DS:C26C) >> 5: offsets DS:1A72, row offsets DS:1A8A, a
// C: slot kept when 0 <= s < 140 and s / 10 − row offset == base / 10, unsigned); for a combatant's sprites (arg 6 not −1) also the 10
// C: slots DS:1AA2 / 1AAC when its set's size class DS:C763 has bit 0, the 8 slots DS:1AB6 / 1ABE when it has bit 2, and the 3 slots
// C: DS:1AC6 / 1ACA when some furniture varies (DS:C907). R014+0x827A: an animated piece's box grows the 14 slots DS:1ACE / 1ADC round
// C: its own slot (s / 10 == row offset + slot / 10). R014+0x88E6: x0 = 9999 is a clean slot; the box is clamped to 0..0x103 × 0..0xC7.
// tests: BattlefieldDirtyBoxesTests (the tables against the EXE)
public sealed class BattlefieldDirtyBoxes
{
    public const int Slots = BattlefieldView.Rows * BattlefieldView.Columns;

    /// <summary>DS:1A5D[r] for r = −13..20 (DS:1A50..1A71): the slot base's row correction (3 − ⌊(r + 7) / 2⌋ inside the view).</summary>
    public static readonly sbyte[] RowCorrection =
        [16, 16, 16, 0, 4, 4, 3, 3, 2, 2, 1, 1, 0, 0, -1, -1, -2, -2, -3, -3, -4, -4, 0, 0, 0, 0, -8, 8, -8, 8, -5, -5, 5, 5];

    public const int FirstRow = -13;

    /// <summary>DS:1A72 / DS:1A8A: the slots round a sprite's slot and their row offsets.</summary>
    public static readonly sbyte[] Round = [0, -31, -30, -29, -28, -21, -20, -19, -12, -11, -10, -9, -8, -1, 1, 8, 9, 10, 11, 12, 19, 20, 21];

    public static readonly sbyte[] RoundRows = [0, -3, -3, -3, -3, -2, -2, -2, -1, -1, -1, -1, -1, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2];

    /// <summary>DS:1AA2 / DS:1AAC: the further slots of a wide sprite (size class bit 0).</summary>
    public static readonly sbyte[] Wide = [-22, -18, -13, -7, -2, 2, 7, 13, 18, 22];

    public static readonly sbyte[] WideRows = [-2, -2, -1, -1, 0, 0, 1, 1, 2, 2];

    /// <summary>DS:1AB6 / DS:1ABE: the further slots of a big sprite (size class bit 2).</summary>
    public static readonly sbyte[] Big = [-2, 2, -32, -33, -39, -40, -41, -42];

    public static readonly sbyte[] BigRows = [0, 0, -3, -3, -4, -4, -4, -4];

    /// <summary>DS:1AC6 / DS:1ACA: the further slots when some furniture varies.</summary>
    public static readonly sbyte[] Varying = [29, 30, 31];

    public static readonly sbyte[] VaryingRows = [3, 3, 3];

    /// <summary>DS:1ACE / DS:1ADC: the slots round an animated piece's slot.</summary>
    public static readonly sbyte[] Piece = [0, -1, 1, -20, -12, -11, -10, -9, 9, 10, 11, 19, 20, 21];

    public static readonly sbyte[] PieceRows = [0, 0, 0, -2, -1, -1, -1, -1, 1, 1, 1, 2, 2, 2];

    private const int Clean = 9999;
    private readonly int[] _x0 = new int[Slots], _y0 = new int[Slots], _x1 = new int[Slots], _y1 = new int[Slots];

    public BattlefieldDirtyBoxes() => Clear();

    /// <summary>The boxes of this frame's sprites and animated pieces (DS:E172, DS:E174 entries): the only places the redraw reaches the
    /// screen.</summary>
    // orig: R014+0x7BBE / R014+0x827A append each non-empty box to DS:E172 (cleared on the back page by root 0x34D6 before the redraw);
    // C: the screen copy after the blits takes only these boxes (vis10 of the city-guard card's battle, synthetic DKSAVE0-away7a: a write
    // C: watch on back-page byte D000:55A4 shows R014+0x74A2 blitting an east wall's 0xD8 over the 0xB8 a north wall left there in the
    // C: full paint each pass, while the screen keeps the 0xB8: no box of that frame covers the pixel)
    public List<(int X, int Y, int W, int H)> Rects { get; } = [];

    public void Clear()
    {
        Rects.Clear();
        Array.Fill(_x0, Clean);
        Array.Fill(_y0, Clean);
        Array.Fill(_x1, -Clean);
        Array.Fill(_y1, -Clean);
    }

    /// <summary>A sprite at map point (x, y) with its box (relative to the view, clipped to it): <paramref name="sizeClass"/> is the
    /// combatant's set's size class, −1 for a sprite that is not a combatant's.</summary>
    public void MarkSprite(int x, int y, int cameraX, int cameraY, (int X, int Y, int W, int H) box, int sizeClass, bool furnitureVaries)
    {
        if (box.W == 0 || box.H == 0)
        {
            return;
        }
        Rects.Add(box);
        var r = FloorDiv(y - cameraY, 16);
        if (r - FirstRow is < 0 or >= 34)
        {
            // C: outside DS:1A50..1A71 the base reads further words; no sprite of the view stands that far off it (its box would be empty)
            return;
        }
        var slotBase = (short)(RowCorrection[r - FirstRow] + 10 * r + FloorDiv(x - cameraX, 32) + 0x4B);
        var baseRow = (ushort)slotBase / 10;
        MarkRound(slotBase, baseRow, Round, RoundRows, box);
        if (sizeClass < 0)
        {
            return;
        }
        if ((sizeClass & 1) != 0)
        {
            MarkRound(slotBase, baseRow, Wide, WideRows, box);
        }
        if ((sizeClass & 4) != 0)
        {
            MarkRound(slotBase, baseRow, Big, BigRows, box);
        }
        if (furnitureVaries)
        {
            MarkRound(slotBase, baseRow, Varying, VaryingRows, box);
        }
    }

    /// <summary>An animated piece of view slot <paramref name="slot"/> with its box (relative to the view, clipped to it).</summary>
    public void MarkPiece(int slot, (int X, int Y, int W, int H) box)
    {
        if (box.W == 0 || box.H == 0)
        {
            return;
        }
        Rects.Add(box);
        for (var k = 0; k < Piece.Length; k++)
        {
            var s = Piece[k] + slot;
            if (s is >= 0 and < Slots && s / 10 == PieceRows[k] + slot / 10)
            {
                Grow(s, box);
            }
        }
    }

    /// <summary>The box of a slot clamped to the view (x, y, width, height), null for a clean slot.</summary>
    public (int X, int Y, int W, int H)? Box(int slot)
    {
        if (_x0[slot] == Clean)
        {
            return null;
        }
        int x0 = Math.Max(_x0[slot], 0), y0 = Math.Max(_y0[slot], 0), x1 = Math.Min(_x1[slot], 0x103), y1 = Math.Min(_y1[slot], 0xC7);
        return (x0, y0, x1 + 1 - x0, y1 + 1 - y0);
    }

    private void MarkRound(int slotBase, int baseRow, sbyte[] offsets, sbyte[] rows, (int X, int Y, int W, int H) box)
    {
        for (var k = 0; k < offsets.Length; k++)
        {
            var s = offsets[k] + slotBase;
            if (s is >= 0 and < Slots && s / 10 - rows[k] == baseRow)
            {
                Grow(s, box);
            }
        }
    }

    private void Grow(int s, (int X, int Y, int W, int H) box)
    {
        _x0[s] = Math.Min(_x0[s], box.X);
        _y0[s] = Math.Min(_y0[s], box.Y);
        _x1[s] = Math.Max(_x1[s], box.X + box.W - 1);
        _y1[s] = Math.Max(_y1[s], box.Y + box.H - 1);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
