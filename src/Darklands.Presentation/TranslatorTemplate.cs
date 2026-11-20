using System.Globalization;
using System.Text;
using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// The translator's template (docs/LOCALISATION.md, L-7w): every text a translation pack can translate today, in the pack's layout
/// (<c>text/en/exe.ftl</c>, <c>text/en/ui.ftl</c>, <c>text/en/msg/&lt;FILE&gt;.ftl</c>), the English as each message's value and a
/// comment above it: the key, the text's box (the room the screens give it, from a boxes file) and what each argument is. Built
/// from the player's own game files; the program holds none of the game's words.
/// </summary>
// Not in the original: the 1.1 translation packs. Loading the template as a pack gives every text back as the original's (the tests).
public static class TranslatorTemplate
{
    /// <summary>The language tag of the template (the English it is made of).</summary>
    public const string Language = "en";

    /// <summary>One message: its file (relative to <c>text/en/</c>), its Fluent id, its key, the value as written and the comment lines.</summary>
    public sealed record Message(string File, string Id, string Key, string Value, IReadOnlyList<string> Comments)
    {
        /// <summary>The English's coloured parts in order (the menus' hot-key letters, fixed in the code): a translation keeps them.</summary>
        public IReadOnlyList<string> HotKeys { get; init; } = [];

        /// <summary>
        /// Where the text is shown: the screens the recorded sessions drew it on (the boxes), a card text's card ("card $ARMOR00");
        /// empty: not seen in the recorded sessions.
        /// </summary>
        public IReadOnlyList<string> Screens { get; init; } = [];

        /// <summary>The variables the English value names (what a translation must keep).</summary>
        public IReadOnlySet<string> Variables => FtlSource.Variables(Value);
    }

    /// <summary>The template's messages for the game in <paramref name="files"/>, with the boxes of <paramref name="boxes"/> (may be empty).</summary>
    public static IReadOnlyList<Message> Build(GameFiles files, IReadOnlyList<TextBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(boxes);
        var byKey = boxes.ToDictionary(b => b.Key, StringComparer.Ordinal);
        var assets = new GameAssets(files);
        // a card text's line: the card window's font and the layout's one pixel of leading (CardScreen)
        var lineHeight = new Graphics.TextPainter(assets.Font(3)).LineHeight + 1;
        var messages = new List<Message>();
        foreach (var e in OriginalTextExtractor.Extract(files).Entries)
        {
            if (e.Key.StartsWith("exe:", StringComparison.Ordinal) && e.Origin == StringOrigin.Known)
            {
                messages.Add(ExeMessage(e.Key, e.Text, byKey.GetValueOrDefault(e.Key)));
            }
            else if (e.Key.StartsWith("msg:", StringComparison.Ordinal) && CardTextForm.ToWritten(e.Raw).Length > 0)
            {
                // (an empty block has nothing to translate)
                var hash = e.Key.IndexOf('#', StringComparison.Ordinal);
                var file = e.Key[4..hash];
                var block = assets.Messages(file).Blocks[int.Parse(e.Key.AsSpan(hash + 1), CultureInfo.InvariantCulture)];
                var (width, height) = CardBox(block);
                messages.Add(new Message("msg/" + file + ".ftl", KeyMangling.ToFluentId(e.Key), e.Key, CardValue(CardTextForm.ToWritten(e.Raw)),
                    [string.Create(CultureInfo.InvariantCulture, $"{e.Key} · a card text: box: {height / lineHeight} lines of {width} px (to the foot of the card's window)")])
                {
                    Screens = ["card " + file],
                });
            }
        }
        foreach (var id in OriginalMessageText.OwnIds.Order(StringComparer.Ordinal))
        {
            var english = OriginalMessageText.OwnWords(new Darklands.Core.GameText(id, new Placeholders()))!;
            var comment = id + (byKey.GetValueOrDefault(id) is { } box ? " · " + BoxWords(box) : "");
            messages.Add(new Message("ui.ftl", KeyMangling.ToFluentId(id), id, Escape(english, placeablesAsWritten: true), [comment])
            {
                Screens = byKey.GetValueOrDefault(id)?.Screens ?? [],
            });
        }
        return messages;
    }

    /// <summary>Writes <paramref name="messages"/> as a pack under <paramref name="folder"/> (its <c>pack.json</c> and <c>text/en/</c>).</summary>
    public static void Write(string folder, IReadOnlyList<Message> messages) =>
        Write(folder, messages, Language, "Translator's template (the original English)");

    /// <summary>Writes <paramref name="messages"/> as a pack of <paramref name="language"/> named <paramref name="name"/>.</summary>
    public static void Write(string folder, IReadOnlyList<Message> messages, string language, string name)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var text = Path.Combine(folder, LanguagePack.TextFolder(language));
        Directory.CreateDirectory(text);
        File.WriteAllText(Path.Combine(folder, LanguagePack.ManifestName),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["language"] = language, ["name"] = name, ["base"] = "483.07" }) + "\n");
        foreach (var group in messages.GroupBy(m => m.File, StringComparer.Ordinal))
        {
            var path = Path.Combine(text, group.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new StringBuilder();
            file.Append("# The translator's template (docs/LOCALISATION.md): the original English of the player's own game files.\n");
            file.Append("# Copy the file into your pack's text/<language>/ folder and write your words after each '='.\n\n");
            foreach (var m in group)
            {
                foreach (var c in m.Comments)
                {
                    file.Append("# ").Append(c).Append('\n');
                }
                file.Append(m.Id).Append(" =").Append(m.Value.StartsWith('\n') ? "" : " ").Append(m.Value).Append("\n\n");
            }
            File.WriteAllText(path, file.ToString(), new UTF8Encoding(false));
        }
    }

    /// <summary>A card text block's box: its wrapping width and the height from its first line to the foot of the card's text area.</summary>
    // C: CardScreen draws a block at (window x + block x + 5, block y + 5), wrapped in the block's width - 10
    public static (int Width, int Height) CardBox(MessageBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var area = CardScreen.TextArea;
        return (block.Width - 10, area.Y + area.Height - (block.Y + 5));
    }

    /// <summary>The box words of a comment: "box 148 px (English 131 px)", or the room to the screen's edge.</summary>
    public static string BoxWords(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return box.Room < 0
            ? string.Create(CultureInfo.InvariantCulture, $"English {box.English} px (no room known)")
            : box.Windowed
                ? string.Create(CultureInfo.InvariantCulture, $"box {box.Room} px (English {box.English} px)")
                : string.Create(CultureInfo.InvariantCulture, $"room {box.Room} px to the screen's edge (English {box.English} px)");
    }

    private static Message ExeMessage(string key, string english, TextBox? box)
    {
        // a format string: one the screens fill (TranslatorNotes names its arguments, or a recorded session drew it with arguments)
        var notes = TranslatorNotes.Arguments(key);
        var format = notes.Count > 0 || box?.Arguments is { Count: > 0 };
        var conversions = format ? OriginalFormat.Conversions(english) : [];
        string Plain(string part, bool first, bool last) =>
            Escape(format ? part.Replace("%%", "%", StringComparison.Ordinal) : part, placeablesAsWritten: false, edgeAtStart: first, edgeAtEnd: last);
        var value = new StringBuilder();
        var at = 0;
        var described = new List<string>();
        for (var n = 0; n < conversions.Count; n++)
        {
            var (start, length) = conversions[n];
            value.Append(Plain(english[at..start], n == 0, false));
            var name = "arg" + n.ToString(CultureInfo.InvariantCulture);
            value.Append("{ $").Append(name).Append(" }");
            var spec = english.Substring(start, length);
            var what = n < notes.Count ? notes[n] : "a value the screen puts in";
            described.Add($"${name}: {what}" + (spec is "%s" or "%d" or "%Fs" or "%u" or "%ld" ? "" : $" (the English writes it as {spec})"));
            at = start + length;
        }
        value.Append(Plain(english[at..], conversions.Count == 0, true));
        var comment = key + (box is null ? " · box ? (not drawn in the recorded sessions)" : " · " + BoxWords(box));
        if (described.Count > 0)
        {
            comment += "; " + string.Join(", ", described);
        }
        if (Darklands.Core.TextCodes.HotKeys(english) is { Count: > 0 } keys)
        {
            // every menu of the template's texts tests its keys in the code, whatever the text says (L-6b survey of the screens)
            comment += "; hot key " + string.Join(", ", keys.Select(k => "<c..>" + k + "</c>")) + ": the menu's key is fixed in the code, keep that letter coloured";
        }
        return new Message("exe.ftl", KeyMangling.ToFluentId(key), key, value.ToString(), [comment])
        {
            HotKeys = Darklands.Core.TextCodes.HotKeys(english),
            Screens = box?.Screens ?? [],
        };
    }

    // a card text's written form over several lines: a new line after each <br> and <p>, unless what follows would start the line with a
    //   character Fluent gives a meaning there (. [ *) or a space (the line break would eat it)
    /// <summary>A card text's written form as a Fluent value (over several lines, its edge spaces kept), as the template writes it.</summary>
    public static string CardValueOf(string written) => CardValue(written);

    private static string CardValue(string written)
    {
        var value = new StringBuilder("\n    ");
        for (var i = 0; i < written.Length; i++)
        {
            value.Append(written[i]);
            if (written[i] == '>' && (EndsWith(value, "<br>") || EndsWith(value, "<p>")) && i + 1 < written.Length && written[i + 1] is not ('.' or '[' or '*' or ' ' or '<'))
            {
                value.Append("\n    ");
            }
        }
        // Fluent drops the spaces at a value's start and end: those of the text become string literals
        var body = value.ToString(5, value.Length - 5);
        var lead = body.Length - body.TrimStart(' ').Length;
        var trail = body.Length - body.TrimEnd(' ').Length;
        if (lead == body.Length)
        {
            trail = 0;
        }
        var middle = body[lead..(body.Length - trail)];
        return "\n    " + string.Concat(Enumerable.Repeat("{\" \"}", lead)) + EscapeOddCharacters(middle) + string.Concat(Enumerable.Repeat("{\" \"}", trail));
    }

    private static bool EndsWith(StringBuilder s, string end) =>
        s.Length >= end.Length && s.ToString(s.Length - end.Length, end.Length) == end;

    /// <summary>
    /// A text as a Fluent value that gives it back exactly: braces and the characters a file cannot show plainly (control and colour
    /// codes) as string literals, a leading or trailing space as <c>{" "}</c>, an empty text as <c>{""}</c>.
    /// <paramref name="placeablesAsWritten"/>: the text already holds Fluent placeables (<c>{ $count }</c>), kept.
    /// </summary>
    public static string Escape(string text, bool placeablesAsWritten, bool edgeAtStart = true, bool edgeAtEnd = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return "";
        }
        var body = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (placeablesAsWritten && c == '{' && text.IndexOf('}', i) is var close and > 0)
            {
                body.Append(text, i, close - i + 1);
                i = close;
                continue;
            }
            body.Append(c switch
            {
                '{' => "{\"{\"}",
                '}' => "{\"}\"}",
                // the original's colour codes as tags (the menus' hot keys: <c12>M</c>ore), so a lone < or > is a literal
                '<' => "{\"<\"}",
                '>' => "{\">\"}",
                _ when Darklands.Core.TextCodes.ColourOf(c) >= 0 => Darklands.Core.TextCodes.ToTags(c.ToString()),
                _ when Odd(c) => Literal(c),
                ' ' when (i == 0 && edgeAtStart) || (i == text.Length - 1 && edgeAtEnd) => "{\" \"}",
                _ => c.ToString(),
            });
        }
        return body.ToString();
    }

    private static string EscapeOddCharacters(string value)
    {
        var body = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            body.Append(c != '\n' && Odd(c) ? Literal(c) : c.ToString());
        }
        return body.ToString();
    }

    // a character a text file does not show plainly: a control code, a colour code (Latin-1 from 0x7F, but the game's German letters),
    //   a byte kept as itself (GameTextCodec's private-use characters)
    private static bool Odd(char c) =>
        c < ' ' || (c >= '\u007F' && c <= 'ÿ' && "äöüÄÖÜß".IndexOf(c, StringComparison.Ordinal) < 0) || (c >= '' && c <= '');

    private static string Literal(char c) => "{\"\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "\"}";

    // every argument a notice asks for, as the Fluent placeable of its name (the English of the rewrite's own notices)
    private sealed class Placeholders : IReadOnlyDictionary<string, object>
    {
        public object this[string key] => "{ $" + key + " }";

        public IEnumerable<string> Keys => [];

        public IEnumerable<object> Values => [];

        public int Count => 0;

        public bool ContainsKey(string key) => true;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, object>>().GetEnumerator();

        public bool TryGetValue(string key, out object value)
        {
            value = this[key];
            return true;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
