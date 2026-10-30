namespace Darklands.Core.Battles;

/// <summary>
/// One cell of a battlefield level (32 × 16 map pixels): the walls on its sides, the
/// decorations on them, its furniture, and whether it is outside the field. Tile numbers
/// (1..15, 0 none) index the battlefield's tile sets.
/// </summary>
// orig: floor-view-paint R017+0x0000 (1EF8:0000) draft (how the cell long is decoded); checked against the asm R017+0x0154..0x01DE
public readonly record struct BattlefieldCell(uint Bits)
{
    /// <summary>A cell outside the battlefield: nothing is drawn and nobody may enter.</summary>
    public static BattlefieldCell Void => new(0x8000_0000);

    public bool IsVoid => (Bits & 0x8000_0000) != 0;

    /// <summary>Wall along the east side (x + 1 edge), drawn with the west-wall tiles.</summary>
    public int EastWall => (int)(Bits & 0xF);

    /// <summary>Furniture tile (doors, tables, trees ...).</summary>
    public int Furniture => (int)(Bits >> 4) & 0xF;

    /// <summary>Wall along the west side (x edge).</summary>
    public int WestWall => (int)(Bits >> 8) & 0xF;

    /// <summary>Decoration on the west wall.</summary>
    public int WestWallDecoration => (int)(Bits >> 12) & 0xF;

    /// <summary>Wall along the north side (y edge).</summary>
    public int NorthWall => (int)(Bits >> 16) & 0xF;

    /// <summary>Decoration on the north wall.</summary>
    public int NorthWallDecoration => (int)(Bits >> 20) & 0xF;

    /// <summary>Wall along the south side (y + 1 edge), drawn with the north-wall tiles.</summary>
    public int SouthWall => (int)(Bits >> 24) & 0xF;

    /// <summary>Which way the furniture faces (0..3); for doors the open state.</summary>
    public int FurnitureOrientation => (int)(Bits >> 28) & 3;

    public BattlefieldCell WithFurniture(int tile, int orientation) =>
        new((Bits & ~0x3000_00F0u) | (uint)(tile & 0xF) << 4 | (uint)(orientation & 3) << 28);
}
