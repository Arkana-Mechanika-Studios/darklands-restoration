using Darklands.Content.Formats.Battles;

namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// COMMONSP.IMG, the fourteen row-run sprites every screen shares: the mouse pointer, the pieces of
/// the party-panel frames (plain and lit for the selected member), the corners of framed boxes, the
/// backgrounds of a panel row and of the whole panel column, and the dead member's shield.
/// </summary>
// orig: R000+0x1010 (0E76:1010) draft: file-load-far-buffer("commonsp.img"), request-work-buffer(first word, "CnnmSp"), then the 14 paragraph offsets of the header become mouse-cursor-default, DS:A3D6, DS:A3E5, DS:A2E9, DS:A33B, DS:A33E, DS:A392, DS:A660, DS:A662, DS:A664, DS:A666, DS:A626, DS:A628, DS:A2E5 in that order; tests: CommonSpritesTests
public sealed class CommonSprites
{
    /// <summary>The number of sprites in the file.</summary>
    public const int Count = 14;

    private CommonSprites(ImageBank bank) => Bank = bank;

    /// <summary>All fourteen sprites, in file order.</summary>
    public ImageBank Bank { get; }

    /// <summary>The ordinary mouse pointer (<c>mouse-cursor-default</c>).</summary>
    public RowRunSprite Pointer => Bank[0];

    /// <summary>The line above a panel row (<c>DS:A3D6</c>); lit: <see cref="PanelTopLit"/>.</summary>
    public RowRunSprite PanelTop => Bank[1];

    /// <summary>The line below a panel row (<c>DS:A3E5</c>).</summary>
    public RowRunSprite PanelBottom => Bank[2];

    /// <summary>The left and right edge of a panel row (<c>DS:A2E9</c>).</summary>
    public RowRunSprite PanelSide => Bank[3];

    /// <summary>The edge of the selected member's row (<c>DS:A33B</c>).</summary>
    public RowRunSprite PanelSideLit => Bank[4];

    /// <summary>The line above the selected member's row (<c>DS:A33E</c>).</summary>
    public RowRunSprite PanelTopLit => Bank[5];

    /// <summary>The line below the selected member's row (<c>DS:A392</c>).</summary>
    public RowRunSprite PanelBottomLit => Bank[6];

    /// <summary>The top-left corner of a framed box (<c>DS:A660</c>).</summary>
    public RowRunSprite CornerTopLeft => Bank[7];

    /// <summary>The bottom-left corner (<c>DS:A662</c>).</summary>
    public RowRunSprite CornerBottomLeft => Bank[8];

    /// <summary>The top-right corner (<c>DS:A664</c>).</summary>
    public RowRunSprite CornerTopRight => Bank[9];

    /// <summary>The bottom-right corner (<c>DS:A666</c>).</summary>
    public RowRunSprite CornerBottomRight => Bank[10];

    /// <summary>The background of one member's row, 0x37 x 0x26 (<c>DS:A626</c>).</summary>
    public RowRunSprite RowBackground => Bank[11];

    /// <summary>A strip of the panel column's background, 11 rows high, tiled down the column (<c>DS:A628</c>).</summary>
    public RowRunSprite ColumnBackground => Bank[12];

    /// <summary>The shield of a dead member (<c>DS:A2E5</c>).</summary>
    public RowRunSprite DeadShield => Bank[13];

    public static CommonSprites Decode(byte[] file) => new(ImageBank.Decode(file, Count, "COMMONSP.IMG"));
}
