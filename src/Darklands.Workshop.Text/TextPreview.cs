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
    int Foot = 0, int FootLimit = 0)
{
    /// <summary>The words do not fit: wider than the box, or past the card window's foot.</summary>
    public bool Overflows => CardText ? Foot >= FootLimit : Room > 0 && Measured > Room;
}

/// <summary>The editor's live preview of a project's texts (W-0 + E-text): the same painter and layout the game uses.</summary>
// Not in the original: the 1.1 Workshop.
public sealed class TextPreview(TextProject project)
{
    private const byte Ink = 15;
    private readonly GameAssets _assets = new(project.Game);
    private readonly Dictionary<int, TextPainter> _painters = [];
    private IReadOnlyDictionary<int, IFont>? _fonts;

    /// <summary>The picture of <paramref name="entry"/> with <paramref name="value"/> (a Fluent value as written; null: the English).</summary>
    public PreviewResult Render(TextEntry entry, string? value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        value ??= entry.English;
        var localizer = new FluentLocalizer(project.Language, OriginalTable.Empty, null, pseudoTransform: false);
        var problems = localizer.AddOverlay("preview", FtlSource.Write(entry.Id, value) + "\n");
        if (problems.Count > 0)
        {
            return Empty(problems[0].Message, entry.Key.StartsWith("msg:", StringComparison.Ordinal));
        }
        return entry.Key.StartsWith("msg:", StringComparison.Ordinal) ? Card(entry, localizer.Get(entry.Key)) : Line(entry, localizer);
    }

    private TextPainter Painter(int slot)
    {
        _fonts ??= FontPack.Load(project.Folder, _ => { });
        return _painters.TryGetValue(slot, out var p) ? p : _painters[slot] = new TextPainter(_assets.Font(slot)) { PackFont = _fonts.GetValueOrDefault(slot) };
    }

    // a screen text: its words with the arguments a session drew it with, on one line in its font, the box's edge marked
    private PreviewResult Line(TextEntry entry, FluentLocalizer localizer)
    {
        var box = project.Boxes.FirstOrDefault(b => b.Key == entry.Key);
        var arguments = box?.Arguments?.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.Ordinal)
            ?? entry.Template.Variables.ToDictionary(v => v, v => (object?)"0", StringComparer.Ordinal);
        var words = TextCodes.FromTags(localizer.Get(entry.Key, arguments));
        var painter = Painter(box is { Font: > 0 } ? box.Font : 1);
        var measured = painter.Measure(words);
        var room = box is { Room: > 0 } ? box.Room : 0;
        var width = Math.Clamp(Math.Max(measured, room) + 6, 16, Screen.Width);
        var height = painter.LineHeight + 6;
        var canvas = new Canvas(new Screen());
        painter.Draw(canvas, 3, 3, words, Ink);
        var argb = Picture(canvas, width, height);
        if (room > 0 && room + 3 < width)
        {
            // the box's right edge, a dotted line
            for (var y = 0; y < height; y += 2)
            {
                argb[y * width + room + 3] = 0xFFFF4040u;
            }
        }
        return new PreviewResult(width, height, argb, measured, room, box?.English ?? 0, null, false);
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
        // the card window (from its left edge) with the window's foot marked
        var left = CardScreen.WindowX;
        var pictureWidth = Screen.Width - left;
        var whole = Picture(canvas, Screen.Width, Screen.Height);
        var argb = new uint[pictureWidth * Screen.Height];
        for (var row = 0; row < Screen.Height; row++)
        {
            Array.Copy(whole, row * Screen.Width + left, argb, row * pictureWidth, pictureWidth);
        }
        if (limit < Screen.Height)
        {
            for (var column = 0; column < pictureWidth; column += 2)
            {
                argb[limit * pictureWidth + column] = 0xFFFF4040u;
            }
        }
        return new PreviewResult(pictureWidth, Screen.Height, argb, 0, width, 0, problem, true, foot, limit);
    }

    private static uint[] Picture(Canvas canvas, int width, int height)
    {
        var palette = Palette256.DefaultVga;
        var argb = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = canvas.Screen.Pixels[y * Screen.Width + x];
                var c = index == 0 ? new Rgb24(36, 28, 20) : palette[index];
                argb[y * width + x] = 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
            }
        }
        return argb;
    }

    private static PreviewResult Empty(string? problem, bool card) => new(1, 1, [0xFF241C14u], 0, 0, 0, problem, card);
}
