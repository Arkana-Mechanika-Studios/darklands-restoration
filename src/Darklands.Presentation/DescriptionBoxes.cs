using System.Globalization;
using Darklands.Content;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The boxes of the data files' descriptions (tables.ftl, since 1.1.3): word-wrapped texts, so their room is a column and a foot
/// that the screens' code fixes, as a card text's is, not a width a recorded session measures (the boxes file holds one line's
/// width). A saint's description on the prayer screen; a formula's on the alchemy screen's book and on the formula page. A city's
/// description has none of its own: it is part of the card texts that show <c>$PlaceDesc</c>.
/// </summary>
// Not in the original: the 1.1.3 translations of the descriptions.
public static class DescriptionBoxes
{
    /// <summary>
    /// One place a description is drawn: the screen, the column's left edge, first row and width, the first row below the room
    /// (<paramref name="Foot"/>: what the screen draws there or the end of its page), the font slot and the line step.
    /// </summary>
    public sealed record Box(string Screen, string Where, int X, int Y, int Width, int Foot, int Font, int Spacing = 8, int Leading = 1)
    {
        /// <summary>The lines that fit between the first row and the foot in a font <paramref name="height"/> pixels high.</summary>
        public int Lines(int height) => (Foot - Y - height) / (Spacing + Leading) + 1;

        /// <summary>The box in the template's words: "12 lines of 156 px (font 2) on the prayer screen".</summary>
        public string Words(int height) =>
            string.Create(CultureInfo.InvariantCulture, $"{Lines(height)} lines of {Width} px (font {Font}) {Where}");
    }

    // orig: saint-info-paint R034+0x0BC4: the description by text-paint-wrapped in 0x9C from (0x94, 0x25), spacing 8, leading 1, font slot 2
    //   (SaintPrayerScreen.DrawInfo); the line "Success: .. Of Remaining: .." is drawn at row 0x93 in the same column.
    /// <summary>A saint's description on the prayer screen.</summary>
    public static readonly Box Saint = new("SaintPrayerScreen", "on the prayer screen", 0x94, 0x25, 0x9C, 0x93, 2);

    // orig: R033+0x16FA (alchemy-screen-body): the description wrapped in 0x5F from (0x49, 0x52) in font slot 1 (AlchemyScreen.ShowFormula);
    //   the book's left page of alcscrn0 ends at row 0xA5.
    /// <summary>A formula's description on the formula page (from the character sheet's list).</summary>
    public static readonly Box FormulaPage = new("AlchemyScreen", "on the formula page", 0x49, 0x52, 0x5F, 0xA5, 1);

    // orig: R033+0x1018: the description wrapped in 0x69 from (0x58, 0x6B), after the area (0x55, 0x50, 0x6E x 0x54) is restored from
    //   the picture (AlchemyScreen.DrawSelectedFormula): its foot is row 0xA4.
    /// <summary>A formula's description in the alchemy screen's book.</summary>
    public static readonly Box FormulaBook = new("AlchemyScreen", "in the alchemy screen's book", 0x58, 0x6B, 0x69, 0xA4, 1);

    /// <summary>The boxes of a description's key (none for a city's description or another key).</summary>
    public static IReadOnlyList<Box> Of(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.EndsWith(".desc", StringComparison.Ordinal))
        {
            return [];
        }
        return key.StartsWith("snt:saint/", StringComparison.Ordinal) ? [Saint]
            : key.StartsWith("alc:", StringComparison.Ordinal) ? [FormulaPage, FormulaBook]
            : [];
    }

    /// <summary>True for the key of a city's description (DARKLAND.DSC), shown in card texts by <c>$PlaceDesc</c>.</summary>
    public static bool IsCityDescription(string key) =>
        key.StartsWith("dsc:", StringComparison.Ordinal) && key.EndsWith(".desc", StringComparison.Ordinal);

    /// <summary>The card variable that shows a city's description.</summary>
    public const string CityVariable = "$PlaceDesc";

    /// <summary>
    /// The lowest row <paramref name="codes"/> reach laid out in <paramref name="box"/> with <paramref name="painter"/> (the box's
    /// font), as the screen lays it out; -1 for nothing drawn.
    /// </summary>
    public static int Bottom(Box box, TextPainter painter, int[] codes)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(codes);
        var canvas = new Canvas(new Darklands.Platform.Screen());
        new CardTextLayout(painter).Draw(canvas, codes, box.X, box.Y, box.Width,
            new CardTextLayout.Settings { Color = 15, Spacing = box.Spacing, Leading = box.Leading });
        var pixels = canvas.Screen.Pixels;
        var width = Darklands.Platform.Screen.Width;
        for (var y = Darklands.Platform.Screen.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x] != 0)
                {
                    return y;
                }
            }
        }
        return -1;
    }

    /// <summary>The template's words for a description's box (null for a key that is not a description with a box).</summary>
    public static string? BoxWords(string key, Func<int, TextPainter> painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        var boxes = Of(key);
        return boxes.Count == 0 ? null
            : "box: " + string.Join(", ", boxes.Select(b => b.Words(painter(b.Font).Font.CharacterHeight)));
    }
}
