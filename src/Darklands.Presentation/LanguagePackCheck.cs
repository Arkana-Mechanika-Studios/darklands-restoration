using System.Globalization;
using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Localization;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The translation pack's check (docs/LOCALISATION.md, L-7w): what in a pack the game cannot use or show as the translator meant —
/// a Fluent error, an id no text has, a variable the screen does not give, a card text whose options or passages are not the
/// original's, a character the font lacks, and a text wider than its box (measured in the font it is drawn in) or a card text
/// that runs past the foot of the card's window.
/// </summary>
// Not in the original: the 1.1 translation packs. The fonts are the original's until font packs (L-6b) give a pack its own.
public static class LanguagePackCheck
{
    /// <summary>How bad a finding is: the pack's text is not used (<see cref="Error"/>), it does not fit (<see cref="Overflow"/>), or a note.</summary>
    public enum Severity
    {
        Error,
        Overflow,
        Note,
    }

    /// <summary>One finding: its severity, where (the file and line of the pack), the message id and what is wrong.</summary>
    public sealed record Finding(Severity Severity, string Where, string Id, string Message)
    {
        public override string ToString() =>
            $"{Where}: {Severity.ToString().ToLowerInvariant()}: {(Id.Length > 0 ? Id + ": " : "")}{Message}";
    }

    /// <summary>
    /// The findings for <paramref name="pack"/> against the <paramref name="template"/> of the player's game (its texts and keys) and the
    /// <paramref name="boxes"/> the screens drew; <paramref name="font"/> gives the painter of a font slot (default: the game's fonts).
    /// </summary>
    public static IReadOnlyList<Finding> Run(LanguagePack pack, GameAssets assets, IReadOnlyList<TranslatorTemplate.Message> template,
        IReadOnlyList<TextBox> boxes, Func<int, TextPainter>? font = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(boxes);
        var painters = new Dictionary<int, TextPainter>();
        TextPainter Painter(int slot) => painters.TryGetValue(slot, out var p) ? p : painters[slot] = font?.Invoke(slot) ?? new TextPainter(assets.Font(slot));
        var findings = new List<Finding>();
        foreach (var problem in pack.Problems)
        {
            findings.Add(new Finding(Severity.Error, problem.Line > 0 ? $"{problem.Source}:{problem.Line}" : problem.Source, "", problem.Message));
        }
        var byId = template.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var box = boxes.ToDictionary(b => b.Key, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, message) in pack.Messages)
        {
            var where = $"{file.Replace('\\', '/')}:{message.Line}";
            void Add(Severity s, string what) => findings.Add(new Finding(s, where, message.Id, what));
            if (!seen.Add(message.Id))
            {
                continue;
            }
            if (!byId.TryGetValue(message.Id, out var english))
            {
                Add(Severity.Error, "no text of the game has this id (a typing mistake, or a text not translatable yet): it is not used");
                continue;
            }
            foreach (var extra in message.Variables.Except(english.Variables).Order(StringComparer.Ordinal))
            {
                Add(Severity.Error, $"uses ${extra}, which the screen does not give (it has {Names(english.Variables)})");
            }
            foreach (var missing in english.Variables.Except(message.Variables).Order(StringComparer.Ordinal))
            {
                Add(Severity.Note, $"does not use ${missing}");
            }
            if (english.Key.StartsWith("msg:", StringComparison.Ordinal))
            {
                CheckCardText(pack, assets, english.Key, Painter(3), Add);
                continue;
            }
            var arguments = box.GetValueOrDefault(english.Key)?.Arguments?.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.Ordinal)
                ?? english.Variables.ToDictionary(v => v, v => (object?)"0", StringComparer.Ordinal);
            var words = pack.Localizer.Get(english.Key, arguments);
            var b = box.GetValueOrDefault(english.Key);
            var painter = Painter(b is { Font: > 0 } ? b.Font : 1);
            if (Unshowable(words, painter) is { Length: > 0 } lacking)
            {
                Add(Severity.Error, $"the font has no {lacking}: drawn as nothing or '?'");
            }
            if (b is { Room: >= 0 })
            {
                var width = painter.Measure(words);
                if (width > b.Room)
                {
                    Add(Severity.Overflow, string.Create(CultureInfo.InvariantCulture,
                        $"{width} px wide, {b.Room} px of room ({(b.Windowed ? "cut at the window's edge" : "runs off the screen")}; the English {b.English} px)"));
                }
            }
        }
        return findings;
    }

    private static string Names(IReadOnlySet<string> variables) =>
        variables.Count == 0 ? "none" : string.Join(", ", variables.Order(StringComparer.Ordinal).Select(v => "$" + v));

    // the characters of words the font cannot draw (the game has no byte for them, or the font no glyph), once each
    private static string Unshowable(string words, TextPainter painter)
    {
        var lacking = new List<char>();
        foreach (var c in words)
        {
            var b = Darklands.Core.GameTextCodec.ToByte(c);
            // a Latin-1 letter other than the game's seven (é, ç ...) has a colour code's byte: it would change the colour, not draw
            var colourByte = c is >= ' ' and <= 'þ' && "äöüÄÖÜß".IndexOf(c, StringComparison.Ordinal) < 0;
            var none = colourByte || (b == Darklands.Core.GameTextCodec.Unshowable && c != '?') || (b is >= 0x20 and < 0x80 && painter.Advance(b) == 0 && c != ' ');
            if (none && !lacking.Contains(c))
            {
                lacking.Add(c);
            }
        }
        return string.Join(" ", lacking.Select(c => "'" + c + "'"));
    }

    private static void CheckCardText(LanguagePack pack, GameAssets assets, string key, TextPainter painter, Action<Severity, string> add)
    {
        var hash = key.IndexOf('#', StringComparison.Ordinal);
        var block = assets.Messages(key[4..hash]).Blocks[int.Parse(key.AsSpan(hash + 1), CultureInfo.InvariantCulture)];
        var translation = pack.Localizer.Get(key);
        if (CardTextForm.Check(translation, block.Text) is { } problem)
        {
            add(Severity.Error, problem + ": the original text is shown");
            return;
        }
        var bytes = CardTextForm.ToBytes(translation, out _)!;
        if (Unshowable(Darklands.Core.GameTextCodec.Decode(bytes.Where(b => b >= 0x20).ToArray()), painter) is { Length: > 0 } lacking)
        {
            add(Severity.Error, $"the font has no {lacking}: drawn as nothing or '?'");
        }
        var bottom = CardScreen.TextArea.Y + CardScreen.TextArea.Height;
        var english = Foot(assets, painter, block, block.Text);
        var translated = Foot(assets, painter, block, bytes);
        if (translated >= bottom && translated > english)
        {
            add(Severity.Overflow, string.Create(CultureInfo.InvariantCulture,
                $"runs past the foot of the card's window (every passage shown, the $Names as written; the English ends at row {english})"));
        }
    }

    // the lowest row a block's text reaches, laid out as the card screen does with every passage shown and every option line enabled
    private static int Foot(GameAssets assets, TextPainter painter, MessageBlock block, byte[] text)
    {
        var canvas = new Canvas(new Screen());
        new CardTextLayout(painter).Draw(canvas, text, CardScreen.WindowX + block.X + 5, block.Y + 5, block.Width - 10, new CardTextLayout.Settings
        {
            Conditions = Enumerable.Repeat(true, 32).ToList(),
            Color = 15,
            OptionColor = 15,
            DropCap = letter => DropCaps.Get(assets, letter),
        });
        var pixels = canvas.Screen.Pixels;
        for (var y = Screen.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                if (pixels[y * Screen.Width + x] != 0)
                {
                    return y;
                }
            }
        }
        return 0;
    }
}
