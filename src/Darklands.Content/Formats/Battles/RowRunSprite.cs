namespace Darklands.Content.Formats.Battles;

/// <summary>
/// A battle sprite in the graphics driver's row-run form, used by the battlefield tiles
/// (BC tile sets), the misc battle graphics (BATTLEGR.IMG) and the IMC animation frames:
/// a width byte, a height byte, then for each row a pixel count, the row's first column and
/// that many pixels. Colour 0 is transparent.
/// </summary>
// orig: gfx_draw_sprite root 047C (the graphics driver interface; the drawing itself is in MGRAPHIC.EXE, not in DARKLAND.EXE) asm
// C: the format is read from the data (every BC tile parses exactly within its record size, IMC frames as the
// old engine's CleanCombatPreparedPresentation does); whether the first byte is width − 1 or a hot spot for IMC frames is
// not settled, so nothing relies on Width for frames.
public sealed class RowRunSprite
{
    private readonly (int X, byte[] Pixels)[] _rows;

    private RowRunSprite(int width, int height, (int X, byte[] Pixels)[] rows, string? assetName)
    {
        Width = width;
        Height = height;
        _rows = rows;
        AssetName = assetName;
    }

    /// <summary>An empty sprite (nothing to draw).</summary>
    public static RowRunSprite Empty { get; } = new(0, 0, [], null);

    /// <summary>The width the sprite's header gives (its widest row may be one less).</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>Where the sprite came from, for replacement graphics; the scheme of <see cref="Graphics.IndexedImage.AssetName"/>. Not in the original.</summary>
    public string? AssetName { get; }

    public int RowCount => _rows.Length;

    public bool IsEmpty => _rows.All(r => r.Pixels.Length == 0);

    /// <summary>The first column and the pixels of a row.</summary>
    public (int X, byte[] Pixels) Row(int row) => _rows[row];

    /// <summary>Decodes the sprite that starts at <paramref name="offset"/>.</summary>
    public static RowRunSprite Decode(ReadOnlySpan<byte> data, int offset, string? assetName = null)
    {
        if (offset < 0 || offset + 2 > data.Length)
        {
            throw new InvalidDataException("Row-run sprite header lies outside its data.");
        }
        var width = data[offset] + 1;
        var height = data[offset + 1];
        var rows = new (int, byte[])[height];
        var p = offset + 2;
        for (var row = 0; row < height; row++)
        {
            if (p + 2 > data.Length)
            {
                throw new InvalidDataException("Row-run sprite is truncated.");
            }
            var count = data[p];
            var x = data[p + 1];
            p += 2;
            if (p + count > data.Length)
            {
                throw new InvalidDataException("Row-run sprite row is truncated.");
            }
            rows[row] = (x, data.Slice(p, count).ToArray());
            p += count;
        }
        return new RowRunSprite(width, height, rows, assetName);
    }

    /// <summary>The same sprite with every colour passed through a 256-entry table (0 stays transparent); it keeps its asset name.</summary>
    public RowRunSprite Recolored(ReadOnlySpan<byte> table)
    {
        var rows = new (int, byte[])[_rows.Length];
        for (var r = 0; r < rows.Length; r++)
        {
            var pixels = _rows[r].Pixels.ToArray();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] != 0)
                {
                    pixels[i] = table[pixels[i]];
                }
            }
            rows[r] = (_rows[r].X, pixels);
        }
        return new RowRunSprite(Width, Height, rows, AssetName);
    }
}
