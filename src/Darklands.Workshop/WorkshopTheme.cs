using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's look (W-0 polish): the game's colours (dark wood, parchment, gold) on the Fluent theme's dark variant, flat and
/// with the contrast of print: dark ink on parchment for what is read and written, parchment on wood for the frame, gold for what is
/// chosen. The colour of each kind of chip in a text (line ends, option lines, names, colour tags).
/// </summary>
// Not in the original: the 1.1 Workshop. Our own colours, chosen from the game's palette; no picture of the game.
internal static class WorkshopTheme
{
    public static readonly Color Wood = Color.FromRgb(0x2A, 0x1F, 0x14);
    public static readonly Color WoodLight = Color.FromRgb(0x3C, 0x2E, 0x1E);
    public static readonly Color WoodLine = Color.FromRgb(0x5A, 0x46, 0x2E);
    public static readonly Color Parchment = Color.FromRgb(0xF2, 0xE8, 0xD0);
    public static readonly Color ParchmentDim = Color.FromRgb(0xD8, 0xCA, 0xA8);
    public static readonly Color Ink = Color.FromRgb(0x20, 0x18, 0x10);
    public static readonly Color Gold = Color.FromRgb(0xC8, 0xA0, 0x48);
    public static readonly Color GoldDark = Color.FromRgb(0x7A, 0x5C, 0x1E);
    public static readonly Color Translated = Color.FromRgb(0x7C, 0xB0, 0x5A);
    public static readonly Color Problem = Color.FromRgb(0xE0, 0x62, 0x4A);
    public static readonly Color Untranslated = Color.FromRgb(0x9A, 0x8A, 0x70);

    // the chips' colours inside the texts (on parchment): line ends, option lines and indents, names and arguments, colour tags
    public static readonly Color ChipBreak = Color.FromRgb(0x8A, 0x8A, 0x8A);
    public static readonly Color ChipOption = Color.FromRgb(0x1F, 0x5E, 0x9E);
    public static readonly Color ChipName = Color.FromRgb(0x2E, 0x7D, 0x32);
    public static readonly Color ChipColour = Color.FromRgb(0xB0, 0x30, 0x20);
    public static readonly Color ChipLiteral = Color.FromRgb(0x9A, 0x7A, 0x40);

    public static IBrush Brush(Color c) => new SolidColorBrush(c);

    /// <summary>The colour a chip's text takes (null: words, the ink).</summary>
    public static Color? ChipColourOf(Darklands.Workshop.Text.TextToken token) => token.Kind switch
    {
        Darklands.Workshop.Text.TokenKind.Name => ChipName,
        Darklands.Workshop.Text.TokenKind.Placeable when token.Text.StartsWith("{\"", StringComparison.Ordinal) => ChipLiteral,
        Darklands.Workshop.Text.TokenKind.Placeable => ChipName,
        Darklands.Workshop.Text.TokenKind.Tag when token.Text is "<br>" or "<p>" => ChipBreak,
        Darklands.Workshop.Text.TokenKind.Tag when token.Text == "</c>" || token.Text.StartsWith("<c", StringComparison.Ordinal) && token.Text.Length > 3 && char.IsAsciiDigit(token.Text[2]) => ChipColour,
        Darklands.Workshop.Text.TokenKind.Tag => ChipOption,
        _ => null,
    };

    /// <summary>The theme's resources and styles on the application.</summary>
    public static void Apply(Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Dark;
        var r = app.Resources;
        // the Fluent theme's own keys, so every control takes the colours
        r["SystemAccentColor"] = Gold;
        r["SystemAccentColorLight1"] = Gold;
        r["SystemAccentColorDark1"] = GoldDark;
        r["SolidBackgroundFillColorBase"] = Wood;
        r["ApplicationPageBackgroundThemeBrush"] = Brush(Wood);
        r["TextControlBackground"] = Brush(Parchment);
        r["TextControlBackgroundPointerOver"] = Brush(Parchment);
        r["TextControlBackgroundFocused"] = Brush(Color.FromRgb(0xFA, 0xF2, 0xDE));
        r["TextControlForeground"] = Brush(Ink);
        r["TextControlForegroundPointerOver"] = Brush(Ink);
        r["TextControlForegroundFocused"] = Brush(Ink);
        r["TextControlPlaceholderForeground"] = Brush(Untranslated);
        r["TextControlPlaceholderForegroundFocused"] = Brush(Untranslated);
        r["TextControlBorderBrush"] = Brush(WoodLine);
        r["TextControlBorderBrushFocused"] = Brush(Gold);
        r["TextControlSelectionHighlightColor"] = Brush(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B));
        r["ButtonBackground"] = Brush(WoodLight);
        r["ButtonBackgroundPointerOver"] = Brush(WoodLine);
        r["ButtonBackgroundPressed"] = Brush(GoldDark);
        r["ButtonForeground"] = Brush(Parchment);
        r["ButtonForegroundPointerOver"] = Brush(Parchment);
        r["ButtonBorderBrush"] = Brush(WoodLine);
        r["ListBoxItemBackgroundSelected"] = Brush(Color.FromArgb(0x60, Gold.R, Gold.G, Gold.B));
        r["ListBoxItemBackgroundSelectedPointerOver"] = Brush(Color.FromArgb(0x80, Gold.R, Gold.G, Gold.B));
        r["ComboBoxBackground"] = Brush(WoodLight);
        r["ComboBoxForeground"] = Brush(Parchment);
        r["ComboBoxBorderBrush"] = Brush(WoodLine);
        r["ExpanderHeaderBackground"] = Brush(WoodLight);
        r["ExpanderHeaderBackgroundPointerOver"] = Brush(WoodLine);
        r["ExpanderHeaderBackgroundPressed"] = Brush(WoodLine);
        r["ExpanderHeaderForeground"] = Brush(Parchment);
        r["ExpanderHeaderBorderBrush"] = Brush(WoodLine);
        r["ExpanderContentBackground"] = Brush(Wood);
        r["ExpanderContentBorderBrush"] = Brush(WoodLine);
    }

    /// <summary>The Workshop's icon: an open book in gold on dark wood (our own drawing).</summary>
    public static WindowIcon Icon()
    {
        const int Size = 32;
        var bitmap = new WriteableBitmap(new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            var pixels = new int[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var edge = x is 0 or Size - 1 || y is 0 or Size - 1;
                    var page = y is >= 8 and <= 24 && (x is >= 5 and <= 14 || x is >= 17 and <= 26);
                    var spine = x is 15 or 16 && y is >= 7 and <= 25;
                    var line = page && y % 3 == 0 && x is not (5 or 14 or 17 or 26);
                    var c = edge ? Gold : spine ? GoldDark : line ? WoodLine : page ? Parchment : Wood;
                    pixels[y * Size + x] = unchecked((int)(0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B));
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
