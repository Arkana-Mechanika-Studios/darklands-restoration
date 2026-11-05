using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>For one original font: which byte codes it can draw (an in-range code with at least one lit pixel).</summary>
/// <param name="File">The font file ("FONTS.FNT", "FONTS.UTL").</param>
/// <param name="Font">Font number in the file.</param>
/// <param name="FirstCode">First byte code of the font's range.</param>
/// <param name="LastCode">Last byte code of the font's range.</param>
/// <param name="Drawn">Codes in the range whose glyph is not empty, ascending.</param>
public sealed record FontCoverageEntry(string File, int Font, int FirstCode, int LastCode, IReadOnlyList<byte> Drawn)
{
    /// <summary>True when the font has a non-empty glyph for the code.</summary>
    public bool Covers(byte code) => Drawn.Contains(code);
}

/// <summary>Which byte codes each original bitmap font can draw (for deciding which translated characters need a font pack).</summary>
public static class FontCoverage
{
    private static readonly string[] FontFiles = ["FONTS.FNT", "FONTS.UTL"];

    public static IReadOnlyList<FontCoverageEntry> Read(GameFiles files)
    {
        var result = new List<FontCoverageEntry>();
        foreach (var name in FontFiles)
        {
            if (!files.Exists(name))
            {
                continue;
            }
            var fnt = new FntDecoder().Decode(name, files.Read(name));
            foreach (var font in fnt.Fonts)
            {
                var drawn = new List<byte>();
                for (var c = (int)font.StartCharCode; c <= font.EndCharCode; c++)
                {
                    var code = (byte)c;
                    var lit = false;
                    for (var row = 0; row < font.CharacterHeight && !lit; row++)
                    {
                        foreach (var b in font.GetGlyphRow(code, row))
                        {
                            if (b != 0)
                            {
                                lit = true;
                                break;
                            }
                        }
                    }
                    if (lit)
                    {
                        drawn.Add(code);
                    }
                }
                result.Add(new FontCoverageEntry(name, font.Index, font.StartCharCode, font.EndCharCode, drawn));
            }
        }
        return result;
    }
}
