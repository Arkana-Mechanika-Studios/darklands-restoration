namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Decoded Darklands font-set resource, used by the graphics text renderer.
/// </summary>
public sealed record Fnt(
    string AssetName,
    IReadOnlyList<FntFont> Fonts)
{
    public int FontCount => Fonts.Count;
}

/// <summary>
/// One bitmap font definition from FONTS.FNT or FONTS.UTL.
/// </summary>
public sealed record FntFont(
    int Index,
    int WidthTableOffset,
    int HeaderOffset,
    int PixelDataOffset,
    byte StartCharCode,
    byte EndCharCode,
    byte CharacterDataWidth,
    byte Unknown05,
    byte CharacterHeight,
    byte CharacterSpacing,
    byte LineSpacing,
    byte Unknown01,
    byte[] CharacterWidths,
    byte[] PixelData)
{
    public int CharacterCount => EndCharCode - StartCharCode + 1;

    public int PixelDataLength => CharacterCount * CharacterDataWidth * CharacterHeight;

    public bool Contains(byte character)
    {
        return character >= StartCharCode && character <= EndCharCode;
    }

    public int GetCharacterIndex(byte character)
    {
        if (!Contains(character))
        {
            throw new ArgumentOutOfRangeException(nameof(character), "Character is not present in this font.");
        }

        return character - StartCharCode;
    }

    public int GetCharacterWidth(byte character)
    {
        return CharacterWidths[GetCharacterIndex(character)];
    }

    public int GetGlyphRowOffset(byte character, int row)
    {
        if (row < 0 || row >= CharacterHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Glyph row is outside the font height.");
        }

        return ((row * CharacterCount) + GetCharacterIndex(character)) * CharacterDataWidth;
    }

    public ReadOnlySpan<byte> GetGlyphRow(byte character, int row)
    {
        var offset = GetGlyphRowOffset(character, row);
        return PixelData.AsSpan(offset, CharacterDataWidth);
    }
}

