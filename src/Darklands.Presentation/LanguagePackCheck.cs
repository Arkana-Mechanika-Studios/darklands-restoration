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
        var findings = new List<Finding>();
        // the pack's own fonts (fonts/slotN.json): a text of their slot is measured in them
        var packFonts = FontPack.Load(pack.Folder, problem => findings.Add(new Finding(Severity.Error, FontPack.Folder, "", problem)));
        TextPainter Painter(int slot) => painters.TryGetValue(slot, out var p)
            ? p
            : painters[slot] = font?.Invoke(slot) ?? new TextPainter(assets.Font(slot)) { PackFont = packFonts.GetValueOrDefault(slot) };
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
            if (message.Id.StartsWith(LanguagePack.VariablePrefix, StringComparison.Ordinal))
            {
                // a pack's own card-text variable: a name of letters and digits, chosen by $sex only
                if (!pack.Variables.Contains(message.Id[LanguagePack.VariablePrefix.Length..]))
                {
                    Add(Severity.Error, "a variable's name is letters and digits only (var-him1): it is not used");
                }
                else if (message.Variables.Except(["sex"]).Any())
                {
                    Add(Severity.Error, $"a variable may only use $sex (female or male), not {string.Join(", ", message.Variables.Except(["sex"]).Select(v => "$" + v))}");
                }
                continue;
            }
            if (!byId.TryGetValue(message.Id, out var english))
            {
                Add(Severity.Error, "no text of the game has this id (a typing mistake, or a text not translatable yet): it is not used");
                continue;
            }
            // a name argument's forms ($arg0_acc, $arg0_gender: Core.DataName) go with the argument
            foreach (var extra in message.Variables.Except(english.Variables).Where(v => !IsNameForm(v, english.Variables)).Order(StringComparer.Ordinal))
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
            var words = Darklands.Core.TextCodes.FromTags(pack.Localizer.Get(english.Key, arguments));
            var b = box.GetValueOrDefault(english.Key);
            var painter = Painter(b is { Font: > 0 } ? b.Font : 1);
            if (Unshowable(Darklands.Core.TextCodes.OfText(words), painter) is { Length: > 0 } lacking)
            {
                Add(Severity.Error, $"the font has no {lacking}: drawn as '?' (a font pack can add letters)");
            }
            if (english.HotKeys.Count > 0)
            {
                // every menu of these texts tests its keys in the code (the hot key's letter is fixed): the coloured letters must stay
                var keys = Darklands.Core.TextCodes.HotKeys(words);
                if (!keys.SequenceEqual(english.HotKeys, StringComparer.OrdinalIgnoreCase))
                {
                    Add(Severity.Error, $"its coloured hot keys ({Quoted(keys)}) are not the English's ({Quoted(english.HotKeys)}): the menu's keys are " +
                        "fixed in the code, so keep those letters coloured (<c12>M</c>)");
                }
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

    private static string Quoted(IReadOnlyList<string> keys) => keys.Count == 0 ? "none" : string.Join(", ", keys.Select(k => "'" + k + "'"));

    /// <summary>Whether <paramref name="variable"/> is a form of one of <paramref name="given"/> (<c>arg0_acc</c> of <c>arg0</c>).</summary>
    // Not in the original: the 1.1 translations (the data files' names in sentences).
    public static bool IsNameForm(string variable, IReadOnlySet<string> given)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(given);
        var cut = variable.LastIndexOf('_');
        return cut > 0 && given.Contains(variable[..cut]) && Darklands.Core.DataName.Attributes.Contains(variable[(cut + 1)..]);
    }

    /// <summary>The letters of a text the painter's font has no glyph for (the game's font: those its bytes cannot hold), once each, quoted.</summary>
    public static string Unshowable(int[] codes, TextPainter painter)
    {
        var lacking = new List<int>();
        foreach (var code in codes)
        {
            if (code > ' ' && !Darklands.Core.TextCodes.IsColour(code) && !painter.CodeFont.Has(code) && !lacking.Contains(code))
            {
                lacking.Add(code);
            }
        }
        return string.Join(" ", lacking.Select(c => "'" + char.ConvertFromUtf32(c) + "'"));
    }

    private static void CheckCardText(LanguagePack pack, GameAssets assets, string key, TextPainter painter, Action<Severity, string> add)
    {
        var hash = key.IndexOf('#', StringComparison.Ordinal);
        var block = assets.Messages(key[4..hash]).Blocks[int.Parse(key.AsSpan(hash + 1), CultureInfo.InvariantCulture)];
        var translation = pack.Localizer.Get(key);
        if (CardTextForm.Check(translation, block.Text, pack.Variables) is { } problem)
        {
            add(Severity.Error, problem + ": the original text is shown");
            return;
        }
        // by codes, as the card screen lays a translation out when it needs the Unicode path (the same loop as the bytes')
        var codes = CardTextForm.ToCodes(translation, out _)!;
        if (Unshowable(codes, painter) is { Length: > 0 } lacking)
        {
            add(Severity.Error, $"the font has no {lacking}: drawn as '?' (a font pack can add letters)");
        }
        var bottom = CardScreen.TextArea.Y + CardScreen.TextArea.Height;
        var english = Foot(assets, painter, block, Darklands.Core.TextCodes.OfBytes(block.Text));
        var translated = Foot(assets, painter, block, codes);
        if (translated >= bottom && translated > english)
        {
            // a text in pages (CardPaging: the game shows a pack's long text so) is a note; one that cannot be paged (its option lines
            //   alone do not fit, or a word is wider than the column) runs past the foot
            var conditions = Enumerable.Repeat(true, 32).ToList();
            if (Graphics.CardPaging.ForBlock(painter, block, codes, conditions, [], letter => DropCaps.Get(assets, letter)) is { } paging)
            {
                add(Severity.Note, string.Create(CultureInfo.InvariantCulture,
                    $"laid out in {paging.Pages.Count} pages (every passage shown, the $Names as written): the game shows it page by page"));
            }
            else
            {
                add(Severity.Overflow, string.Create(CultureInfo.InvariantCulture,
                    $"runs past the foot of the card's window (every passage shown, the $Names as written; the English ends at row {english})"));
            }
        }
    }

    // the lowest row a block's text reaches, laid out as the card screen does with every passage shown and every option line enabled
    private static int Foot(GameAssets assets, TextPainter painter, MessageBlock block, int[] text)
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
