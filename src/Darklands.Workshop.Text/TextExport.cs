using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Darklands.Localization;

namespace Darklands.Workshop.Text;

/// <summary>
/// One text as a whole-file export gives it (to a CAT tool, a spreadsheet or an AI chat): its key (a card text's row:
/// <c>msg:$CITYC00#0/option3</c>), where the game shows it, the English, the translation so far, the room it has, what must stay
/// (its <c>$Names</c> and arguments, its hot key), the glossary terms its English holds, and the template's note.
/// </summary>
public sealed record ExportItem(string Key, string Where, string English, string? Translation, string Room, IReadOnlyList<string> Keep,
    IReadOnlyList<GlossaryTerm> Terms, string Note);

/// <summary>
/// The Workshop's whole-file export (docs/LOCALISATION.md): a selection of a project's texts as JSON or CSV, each with its context,
/// a card text in its rows (introduction, passages, options: their markers, "…" and indents are put back on import), and an
/// instruction header so a file pasted into an AI chat gives a usable answer. Nothing is sent anywhere.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class TextExport
{
    /// <summary>The style line when the pack says none (pack.json "style", the Workshop's "Notes for translators and AI").</summary>
    public const string DefaultStyle = "Medieval Germany, 15th century; address the party as \"you\" (second person).";

    /// <summary>The instruction header of a file of <paramref name="project"/>'s texts.</summary>
    public static IReadOnlyList<string> Instructions(TextProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var language = Languages.Of(project.Language) is { } known ? $"{known.English} ({project.Language})" : project.Language;
        var style = project.Style is { Length: > 0 } own ? own : DefaultStyle;
        return
        [
            $"Darklands translation, English → {language}. Translate the \"english\" of each text into \"translation\".",
            "Keep every $Name and every {…} placeholder as it is, and the hot-key letter when \"keep\" names one. Stay within \"room\".",
            "Use the glossary's terms. Leave \"key\" unchanged. A text's \"english\" starting with \"…\" continues the line before it: translate it so.",
            .. style.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0),
        ];
    }

    /// <summary>The export items of <paramref name="entries"/>: a card text as its rows (<paramref name="cardRows"/>), else whole.</summary>
    public static IReadOnlyList<ExportItem> Items(TextProject project, IEnumerable<TextEntry> entries, Glossary glossary, bool cardRows = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(glossary);
        var items = new List<ExportItem>();
        foreach (var entry in entries)
        {
            var (_, title) = TextTitles.Group(entry);
            var where = entry.Key.StartsWith("msg:", StringComparison.Ordinal) ? "card: " + title : TextTitles.Section(entry) == "Screens" ? "screen: " + title : title;
            var note = Note(entry);
            var keep = Keep(entry);
            if (cardRows && entry.Key.StartsWith("msg:", StringComparison.Ordinal))
            {
                var english = CardRows.Parse(entry.English);
                if (CardRows.Against(english, entry.Translation) is { } rows)
                {
                    var option = 0;
                    var passage = 0;
                    for (var i = 0; i < english.Count; i++)
                    {
                        var row = english[i];
                        var part = row.Kind switch
                        {
                            CardRowKind.Option => "option" + (++option).ToString(CultureInfo.InvariantCulture),
                            CardRowKind.Passage => "passage" + (++passage).ToString(CultureInfo.InvariantCulture),
                            _ => "intro",
                        };
                        var lead = row.Prefix.Contains("...", StringComparison.Ordinal) ? "… " : "";
                        var text = lead + (row.Field ?? CardRow.Unwrapped(row.Content));
                        var translated = entry.Translation is null ? null : rows[i].Field ?? CardRow.Unwrapped(rows[i].Content);
                        items.Add(new ExportItem($"{entry.Key}/{part}", where + (row.Kind == CardRowKind.Passage ? " · only when it applies" : ""),
                            text, translated, note, [.. keep.Where(k => text.Contains(k.TrimStart('$'), StringComparison.Ordinal) || !k.StartsWith('$'))],
                            glossary.In(text), ""));
                    }
                    continue;
                }
            }
            items.Add(new ExportItem(entry.Key, where, entry.English, entry.Translation, Room(entry), keep, glossary.In(entry.English), note));
        }
        return items;
    }

    // what must stay: the English's $Names and arguments (a card text's are written bare, $Money1), its hot keys
    private static IReadOnlyList<string> Keep(TextEntry entry) =>
    [
        .. entry.Template.Variables.Select(v => "$" + v)
            .Union(System.Text.RegularExpressions.Regex.Matches(entry.English, @"\$[A-Za-z][A-Za-z0-9_]*").Select(m => m.Value), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
        .. entry.Template.HotKeys.Select(k => "hot key " + k),
    ];

    // the template's note without its key: the box, what each argument is
    private static string Note(TextEntry entry)
    {
        var first = entry.Template.Comments.FirstOrDefault() ?? "";
        return first.StartsWith(entry.Key + " · ", StringComparison.Ordinal) ? first[(entry.Key.Length + 3)..] : first;
    }

    private static string Room(TextEntry entry) => Note(entry) switch
    {
        var n when n.StartsWith("room ", StringComparison.Ordinal) => n.Split(';')[0],
        var n when n.StartsWith("a card text: box: ", StringComparison.Ordinal) => n["a card text: box: ".Length..],
        // a description of the data files (1.1.3): its column's lines on each screen
        var n when n.Contains(" · box: ", StringComparison.Ordinal) => n[(n.IndexOf(" · box: ", StringComparison.Ordinal) + " · box: ".Length)..],
        _ => "",
    };

    /// <summary>The items as JSON: the header (instructions, language), then one object a text.</summary>
    public static string Json(TextProject project, IReadOnlyList<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(items);
        var texts = new JsonArray();
        foreach (var item in items)
        {
            var text = new JsonObject
            {
                ["key"] = item.Key,
                ["where"] = item.Where,
                ["english"] = item.English,
                ["translation"] = item.Translation ?? "",
                ["room"] = item.Room,
            };
            if (item.Keep.Count > 0)
            {
                text["keep"] = new JsonArray([.. item.Keep.Select(k => JsonValue.Create(k))]);
            }
            if (item.Terms.Count > 0)
            {
                text["glossary"] = new JsonArray([.. item.Terms.Select(t => new JsonObject { ["term"] = t.Term, ["translation"] = t.Translation })]);
            }
            if (item.Note.Length > 0 && item.Note != item.Room)
            {
                text["note"] = item.Note;
            }
            texts.Add(text);
        }
        var root = new JsonObject
        {
            ["instructions"] = new JsonArray([.. Instructions(project).Select(l => JsonValue.Create(l))]),
            ["source"] = "en",
            ["target"] = project.Language,
            [WorkshopVersion.ManifestField] = WorkshopVersion.Current,
            ["texts"] = texts,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    /// <summary>The CSV columns, in order.</summary>
    public static readonly IReadOnlyList<string> CsvColumns = ["key", "where", "english", "translation", "room", "keep", "glossary", "note"];

    /// <summary>The items as CSV (UTF-8, RFC 4180 quoting): the header as # lines, the columns' names, then one row a text.</summary>
    public static string Csv(TextProject project, IReadOnlyList<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(items);
        var csv = new StringBuilder();
        csv.Append("# written by ").Append(WorkshopVersion.Label).Append("\r\n");
        foreach (var line in Instructions(project))
        {
            csv.Append("# ").Append(line).Append("\r\n");
        }
        csv.Append(string.Join(",", CsvColumns)).Append("\r\n");
        foreach (var item in items)
        {
            string[] cells =
            [
                item.Key, item.Where, item.English, item.Translation ?? "", item.Room, string.Join("; ", item.Keep),
                string.Join("; ", item.Terms.Select(t => $"{t.Term} = {t.Translation}")), item.Note == item.Room ? "" : item.Note,
            ];
            csv.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny([',', '"', '\n', '\r']) < 0 && !cell.StartsWith('#') ? cell : "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
