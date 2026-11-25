using Darklands.Content.Formats.Text.Strings;
using Darklands.Content;
using Darklands.Content.Formats.Graphics;
using Darklands.Content.Formats.Text;
using Darklands.Core;
using Darklands.Localization;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Workshop.Text;

/// <summary>
/// A text drawn as the game draws it (the editor's live preview): its picture (ARGB, the game's palette), how wide it is against its
/// box, and what stops it being used. A screen text in its slot's font (the pack's font when it has one) on one line with the box
/// marked; a card text laid out in its block with the game's own layout, every passage shown, against the foot of the card's window.
/// </summary>
public sealed record PreviewResult(int Width, int Height, uint[] Argb, int Measured, int Room, int English, string? Problem, bool CardText,
    int Foot = 0, int FootLimit = 0, int Pages = 1)
{
    /// <summary>
    /// Output pixels per game pixel of <see cref="Argb"/> (<see cref="Width"/> times it by <see cref="Height"/> times it): 1, or more
    /// when a pack's TrueType font draws the text (its glyphs drawn again at that resolution, as the game draws them at the window's).
    /// </summary>
    public int Scale { get; init; } = 1;

    /// <summary>The text has no translation yet: the picture is the English (in the pack's fonts, as the game would draw it).</summary>
    public bool Untranslated { get; init; }

    /// <summary>The words do not fit: wider than the box, or past the card window's foot.</summary>
    public bool Overflows => CardText ? Foot >= FootLimit : Room > 0 && Measured > Room;

    /// <summary>
    /// A description of the data files (1.1.3): laid out in its screen's column (<see cref="CardText"/> is set: measured against a
    /// foot), and where that column is, as "its box on the prayer screen"; a city's description, drawn on one line, where it is
    /// drawn ("the card texts that show $PlaceDesc"); null for other texts.
    /// </summary>
    public string? Column { get; init; }
}

/// <summary>The editor's live preview of a project's texts (W-0 + E-text): the same painter and layout the game uses.</summary>
// Not in the original: the 1.1 Workshop.
public sealed class TextPreview(TextProject project)
{
    private const byte Ink = 15;

    /// <summary>
    /// How a scalable (TrueType) font's glyphs on a frame are drawn again at <c>scale</c> output pixels per game pixel: the whole
    /// screen's ARGB (0: nothing there), as the game's text layer draws them at the window's resolution; set by the Workshop (the
    /// layer is Rendering's); null: such glyphs are shown at the game's 320x200 only.
    /// </summary>
    public static Func<Screen, int, uint[]?>? DrawScalable { get; set; }

    /// <summary>The preview's resolution for a TrueType font's text (its largest on-screen scale).</summary>
    public const int HighScale = 4;
    private readonly GameAssets _assets = new(project.Game);
    private readonly Dictionary<int, TextPainter> _painters = [];
    private IReadOnlyDictionary<int, IFont>? _fonts;
    private Rgb24[]? _cardPalette;

    private PackCapitals? _capitals;

    /// <summary>Reads the pack's fonts and capitals again (after the Workshop built its font pack, or a capital was imported).</summary>
    public void ReloadFonts()
    {
        _fonts = null;
        _capitals = null;
        _painters.Clear();
    }

    /// <summary>The pack's own decorated capitals (capitals/*.png), as the game reads them.</summary>
    public PackCapitals Capitals => _capitals ??= PackCapitals.Load(project.Folder, _assets);

    /// <summary>
    /// The capital a card text starting with <paramref name="code"/> gets, as the game draws it (the pack's picture, the game's, its
    /// look-alike's, or the letter in a box): 20×20 ARGB in the card's colours.
    /// </summary>
    public uint[] Capital(int code)
    {
        var canvas = new Canvas(new Screen());
        var painter = Painter(3);
        new CardTextLayout(painter).Draw(canvas, [code, ' ', 'x'], 0, 0, 200, new CardTextLayout.Settings
        {
            Color = Ink,
            DropCap = letter => DropCaps.Get(_assets, letter),
        });
        var whole = Picture(canvas, Screen.Width, Screen.Height, CardPalette());
        return Crop(whole, 1, 0, 0, PackCapitals.Size, PackCapitals.Size);
    }

    /// <summary>The hint for letters the font has no glyph for (null: none lacking).</summary>
    public static string? LackingHint(string lacking) =>
        lacking.Length == 0 ? null : $"The game's fonts lack {lacking}: drawn as '?'. Build the font pack (Font pack… in the toolbar).";

    /// <summary>The picture of <paramref name="entry"/> with <paramref name="value"/> (a Fluent value as written; null: the English).</summary>
    public PreviewResult Render(TextEntry entry, string? value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var untranslated = value is null;
        value ??= entry.English;
        var localizer = new FluentLocalizer(project.Language, OriginalTable.Empty, null, pseudoTransform: false);
        var problems = localizer.AddOverlay("preview", FtlSource.Write(entry.Id, value) + "\n");
        if (problems.Count > 0)
        {
            return Empty(problems[0].Message, entry.Key.StartsWith("msg:", StringComparison.Ordinal));
        }
        var result = entry.Key.StartsWith("msg:", StringComparison.Ordinal) ? Card(entry, localizer.Get(entry.Key))
            : DescriptionBoxes.Of(entry.Key) is { Count: > 0 } boxes ? Description(boxes, TextCodes.FromTags(localizer.Get(entry.Key)))
            : Line(entry, localizer);
        return untranslated ? result with { Untranslated = true } : result;
    }

    // the hint for letters the fonts lack: a Chinese, Japanese or Korean pack's is a TrueType font's, not the pixel font pack's
    private string? Lacking(string lacking) =>
        lacking.Length == 0 ? null : FontNeeds.IsCjk(project.Language) ? $"The fonts lack {lacking}: drawn as '?'. {FontNeeds.CjkFontHint}" : LackingHint(lacking);

    private TextPainter Painter(int slot)
    {
        _fonts ??= FontPack.Load(project.Folder, _ => { });
        return _painters.TryGetValue(slot, out var p) ? p : _painters[slot] = new TextPainter(_assets.Font(slot))
        {
            PackFont = _fonts.GetValueOrDefault(slot),
            BreakAfter = LineBreaks.For(project.Language),
            Capitals = Capitals.Letters.Count > 0 ? Capitals.Get : null,
        };
    }

    // a screen text: its words with the arguments a session drew it with, on one line in its font, the box's edge marked
    private PreviewResult Line(TextEntry entry, FluentLocalizer localizer)
    {
        var box = project.Boxes.FirstOrDefault(b => b.Key == entry.Key);
        var arguments = box?.Arguments?.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.Ordinal)
            ?? entry.Template.Variables.ToDictionary(v => v, v => (object?)"0", StringComparer.Ordinal);
        var words = TextCodes.FromTags(localizer.Get(entry.Key, arguments));
        // the pack's layout of the text (interface/layout.json): its font, its width (else the room left after its move), its lines
        var layout = project.LayoutOf(entry);
        // a city's description (1.1.3) is drawn inside card texts ($PlaceDesc): the card window's font
        var city = DescriptionBoxes.IsCityDescription(entry.Key);
        var painter = Painter(layout is { Font: >= 1 and <= 3 } ? layout.Font : box is { Font: > 0 } ? box.Font : city ? 3 : 1);
        var moved = (layout?.X ?? box?.X ?? 0) - (box?.X ?? 0);
        var room = box is { Room: > 0 } ? layout?.Width ?? box.Room - moved : layout?.Width ?? 0;
        var lines = layout is null ? [words] : painter.Wrap(words, room > 0 ? room : Screen.Width, Math.Max(1, layout.Lines));
        var measured = lines.Max(painter.Measure);
        var width = Math.Clamp(Math.Max(measured, room) + 6, 16, Screen.Width);
        var height = painter.LineHeight * lines.Count + 6;
        var canvas = new Canvas(new Screen());
        for (var line = 0; line < lines.Count; line++)
        {
            painter.Draw(canvas, 3, 3 + line * painter.LineHeight, lines[line], Ink);
        }
        var k = ScaleOf(painter);
        var argb = Crop(Picture(canvas, Palette256.DefaultVga.Entries.ToArray(), k), k, 0, 0, width, height);
        if (room > 0 && room + 3 < width)
        {
            // the box's right edge, a dotted line
            VerticalDots(argb, width * k, height * k, (room + 3) * k, k);
        }
        var lacking = Lacking(LanguagePackCheck.Unshowable(TextCodes.OfText(words), painter));
        return new PreviewResult(width, height, argb, measured, room, box?.English ?? 0, lacking, false)
        {
            Scale = k,
            Column = city ? "the card texts that show " + DescriptionBoxes.CityVariable : null,
        };
    }

    // a description (1.1.3): laid out in each of its screens' columns as the screen does, against the column's foot; the box it fills
    //   most is shown (a formula's: the formula page's or the book's)
    private PreviewResult Description(IReadOnlyList<DescriptionBoxes.Box> boxes, string words)
    {
        var codes = TextCodes.OfText(words);
        PreviewResult? worst = null;
        foreach (var box in boxes)
        {
            var painter = Painter(box.Font);
            var canvas = new Canvas(new Screen());
            new CardTextLayout(painter).Draw(canvas, codes, box.X, box.Y, box.Width,
                new CardTextLayout.Settings { Color = Ink, Spacing = box.Spacing, Leading = box.Leading });
            var bottom = DescriptionBoxes.Bottom(box, painter, codes);
            // the column from 3 px left of it to 3 px right, from its first row to past the foot or the text's end
            var (left, top) = (box.X - 3, box.Y - 3);
            var width = box.Width + 6;
            var height = Math.Min(Screen.Height, Math.Max(box.Foot, bottom + 1) + 4) - top;
            var k = ScaleOf(painter);
            var argb = Crop(Picture(canvas, Palette256.DefaultVga.Entries.ToArray(), k), k, left, top, width, height);
            HorizontalDots(argb, width * k, (box.Foot - top) * k, k);
            var result = new PreviewResult(width, height, argb, 0, box.Width, 0, Lacking(LanguagePackCheck.Unshowable(codes, painter)), true,
                bottom, box.Foot) { Scale = k, Column = "its box " + box.Where };
            if (worst is null || result.Foot - result.FootLimit > worst.Foot - worst.FootLimit)
            {
                worst = result;
            }
        }
        return worst!;
    }

    // a card text: laid out in its block as the card screen does (every passage shown), against the foot of the card's window
    private PreviewResult Card(TextEntry entry, string written)
    {
        var hash = entry.Key.IndexOf('#', StringComparison.Ordinal);
        var block = _assets.Messages(entry.Key[4..hash]).Blocks[int.Parse(entry.Key.AsSpan(hash + 1), System.Globalization.CultureInfo.InvariantCulture)];
        // the pack's own variables, as the project holds them (no reading of the pack at each key)
        var variables = project.Others.Select(o => o.Message.Id)
            .Where(id => id.StartsWith(LanguagePack.VariablePrefix, StringComparison.Ordinal))
            .Select(id => id[LanguagePack.VariablePrefix.Length..]).ToHashSet(StringComparer.Ordinal);
        var problem = CardTextForm.Check(written, block.Text, variables);
        var codes = CardTextForm.ToCodes(written, out var syntax);
        if (codes is null)
        {
            return Empty(syntax ?? problem, true);
        }
        var (width, height) = TranslatorTemplate.CardBox(block);
        var (x, y) = (CardScreen.WindowX + block.X + 5, block.Y + 5);
        var canvas = new Canvas(new Screen());
        var painter = Painter(3);
        new CardTextLayout(painter).Draw(canvas, codes, x, y, width, new CardTextLayout.Settings
        {
            Conditions = Enumerable.Repeat(true, 32).ToList(),
            Color = Ink,
            OptionColor = Ink,
            DropCap = letter => DropCaps.Get(_assets, letter),
        });
        var foot = 0;
        for (var row = Screen.Height - 1; row >= 0 && foot == 0; row--)
        {
            for (var column = 0; column < Screen.Width; column++)
            {
                if (canvas.Screen.Pixels[row * Screen.Width + column] != 0)
                {
                    foot = row;
                    break;
                }
            }
        }
        var limit = y + height;
        if (Lacking(LanguagePackCheck.Unshowable(codes, painter)) is { } missing)
        {
            problem = problem is null ? missing : problem + "\n" + missing;
        }
        // a text too long for the window: the game shows it in pages (CardPaging); the pages one under another, each labelled
        if (foot >= limit && CardPaging.ForBlock(painter, block, codes, Enumerable.Repeat(true, 32).ToList(), [], letter => DropCaps.Get(_assets, letter))
            is { } paging)
        {
            return Pages(paging, block, painter, x, y, width, limit, problem);
        }
        // the card window (from its left edge) with the window's foot marked
        var left = CardScreen.WindowX;
        var pictureWidth = Screen.Width - left;
        var k = ScaleOf(painter);
        var argb = Crop(Picture(canvas, CardPalette(), k), k, left, 0, pictureWidth, Screen.Height);
        if (limit < Screen.Height)
        {
            HorizontalDots(argb, pictureWidth * k, limit * k, k);
        }
        return new PreviewResult(pictureWidth, Screen.Height, argb, 0, width, 0, problem, true, foot, limit) { Scale = k };
    }

    // the card's colours: the VGA colours, the decorated capitals' own colours from their picture (as a card's picture brings them)
    private Rgb24[] CardPalette()
    {
        if (_cardPalette is null)
        {
            _cardPalette = Palette256.DefaultVga.Entries.ToArray();
            if (_assets.PictureExists("Illmcaps"))
            {
                var caps = _assets.Picture("Illmcaps");
                foreach (var index in caps.Image.Pixels.ToArray().Distinct().Where(i => i != 0 && i != Ink))
                {
                    _cardPalette[index] = caps.Palette[index];
                }
                // a card picture's colour 5 is black (the capitals' black; the VGA colours have magenta there)
                _cardPalette[5] = new Rgb24(0, 0, 0);
            }
        }
        return _cardPalette;
    }

    private const int Gap = 4;

    // the pages of a long text, each the card window as the game draws it (the "more" prompt at the foot of each but the last, "page
    //   1/2" at its top left), one under another
    private PreviewResult Pages(CardPaging paging, MessageBlock block, TextPainter painter, int x, int y, int width, int limit, string? problem)
    {
        var left = CardScreen.WindowX;
        var pictureWidth = Screen.Width - left;
        var n = paging.Pages.Count;
        var height = n * Screen.Height + (n - 1) * Gap;
        var k = ScaleOf(painter);
        var argb = new uint[pictureWidth * k * height * k];
        Array.Fill(argb, 0xFF806040u);
        var label = Painter(1);
        var foot = 0;
        for (var p = 0; p < n; p++)
        {
            var canvas = new Canvas(new Screen());
            var last = p == n - 1;
            new CardTextLayout(painter).Draw(canvas, paging.Pages[p], x, y, width, new CardTextLayout.Settings
            {
                Color = Ink,
                OptionColor = Ink,
                DropCap = p == 0 ? letter => DropCaps.Get(_assets, letter) : null,
            });
            if (last)
            {
                foot = CardPaging.Foot(canvas);
            }
            else
            {
                var more = MoreWords();
                painter.Draw(canvas, left + block.X + block.Width - 5 - painter.Measure(more), CardPaging.WindowFoot - painter.LineHeight - 1, more, Ink);
            }
            label.Draw(canvas, left + 2, 1, $"page {p + 1}/{n}", 14);
            var page = Crop(Picture(canvas, CardPalette(), k), k, left, 0, pictureWidth, Screen.Height);
            var top = p * (Screen.Height + Gap) * k;
            Array.Copy(page, 0, argb, top * pictureWidth * k, page.Length);
            if (limit < Screen.Height)
            {
                HorizontalDots(argb, pictureWidth * k, top + limit * k, k);
            }
        }
        return new PreviewResult(pictureWidth, height, argb, 0, width, 0, problem, true, foot, limit, n) { Scale = k };
    }

    // the "more" prompt in the pack's words when it translates it (the 1.2 translations: the untranslated-text audit), else the English
    private string MoreWords()
    {
        if (project.Entries.FirstOrDefault(e => e.Key == CardScreen.MorePrompt) is { Translation: { } words } entry)
        {
            var localizer = new FluentLocalizer(project.Language, OriginalTable.Empty, null, pseudoTransform: false);
            if (localizer.AddOverlay("more", FtlSource.Write(entry.Id, words) + "\n").Count == 0)
            {
                return localizer.Get(entry.Key);
            }
        }
        return OriginalMessageText.OwnWords(GameText.Of(CardScreen.MorePrompt)) ?? "";
    }

    // a painter's resolution: a TrueType font's text at the high scale (when the Workshop can draw it so), else the game's pixels
    private static int ScaleOf(TextPainter painter) => painter.PackFont is IScalableFont && DrawScalable is not null ? HighScale : 1;

    // the whole screen at k output pixels per game pixel: its pixels enlarged, and a TrueType font's glyphs drawn again over them at
    //   that resolution (as the game's text layer draws them over the scaled frame)
    private static uint[] Picture(Canvas canvas, Rgb24[] palette, int k)
    {
        var one = Picture(canvas, Screen.Width, Screen.Height, palette);
        if (k == 1)
        {
            return one;
        }
        var wide = Screen.Width * k;
        var argb = new uint[wide * Screen.Height * k];
        for (var y = 0; y < Screen.Height * k; y++)
        {
            for (var x = 0; x < wide; x++)
            {
                argb[y * wide + x] = one[y / k * Screen.Width + x / k];
            }
        }
        for (var i = 0; i < 256; i++)
        {
            var c = i == 0 ? Paper : palette[i];
            canvas.Screen.Palette[i] = (uint)c.R << 16 | (uint)c.G << 8 | c.B;
        }
        if (DrawScalable?.Invoke(canvas.Screen, k) is { } layer && layer.Length == argb.Length)
        {
            for (var i = 0; i < argb.Length; i++)
            {
                if (layer[i] != 0)
                {
                    argb[i] = layer[i];
                }
            }
        }
        return argb;
    }

    // a part of a whole screen's picture at k (its place and size in game pixels)
    private static uint[] Crop(uint[] whole, int k, int left, int top, int width, int height)
    {
        var (wide, w) = (Screen.Width * k, width * k);
        var argb = new uint[w * height * k];
        for (var row = 0; row < height * k; row++)
        {
            Array.Copy(whole, (top * k + row) * wide + left * k, argb, row * w, w);
        }
        return argb;
    }

    private const uint Mark = 0xFFFF4040u;

    // a dotted line across the picture at output row y (a game pixel's dots, half its height thick)
    private static void HorizontalDots(uint[] argb, int width, int y, int k)
    {
        for (var row = y; row < y + Math.Max(1, k / 2) && (row + 1) * width <= argb.Length; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (column / k % 2 == 0)
                {
                    argb[row * width + column] = Mark;
                }
            }
        }
    }

    // a dotted line down the picture at output column x
    private static void VerticalDots(uint[] argb, int width, int height, int x, int k)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = x; column < Math.Min(width, x + Math.Max(1, k / 2)); column++)
            {
                if (row / k % 2 == 0)
                {
                    argb[row * width + column] = Mark;
                }
            }
        }
    }

    private static readonly Rgb24 Paper = new(36, 28, 20);

    private static uint[] Picture(Canvas canvas, int width, int height, Rgb24[] palette)
    {
        var argb = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = canvas.Screen.Pixels[y * Screen.Width + x];
                var c = index == 0 ? Paper : palette[index];
                argb[y * width + x] = 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
            }
        }
        return argb;
    }

    private static PreviewResult Empty(string? problem, bool card) => new(1, 1, [0xFF241C14u], 0, 0, 0, problem, card);
}
