using System.Text;
using System.Text.Json;

namespace Darklands.Workshop.Text;

/// <summary>One text an import brought: its key (a whole text's, or a card text's row: <c>msg:$CITYC00#0/option3</c>) and its words.</summary>
public sealed record ImportedText(string Key, string Translation);

/// <summary>
/// What an import did: the texts it set (their keys), the ones it kept because they were already reviewed (with the file's words,
/// for Use), those it could not take (an unknown key, a card text's rows that are not the English's), and each changed text's
/// translation and review mark before (for Undo).
/// </summary>
public sealed class ImportResult
{
    public List<string> Imported { get; } = [];

    public List<(string Key, string Theirs)> Kept { get; } = [];

    public List<(string Key, string Reason)> NotImported { get; } = [];

    internal Dictionary<string, (string? Translation, bool Marked)> Before { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The Workshop's import (docs/LOCALISATION.md): a file of texts made by its export, or an AI chat's answer to one (JSON, a fenced
/// JSON block inside a longer answer, or CSV), read back into the project. A card text's rows are put back into its written form
/// (markers, lead-ins and indents the English's). Every text it sets is marked "needs review"; a text already reviewed (translated
/// and not marked) is never overwritten: it is listed with the file's words, which <see cref="Use"/> takes. Nothing is checked here:
/// the window runs the game's check after it.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class TextImport
{
    /// <summary>The texts of <paramref name="text"/> (a file's content or an AI's answer); <paramref name="extension"/> picks CSV (".csv").</summary>
    public static IReadOnlyList<ImportedText> Read(string text, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Csv(text);
        }
        // an AI's answer: the JSON may sit in a fenced block among other words
        var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var body = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", body, StringComparison.Ordinal);
            text = end > body ? text[body..end] : text[body..];
        }
        var first = text.IndexOfAny(['{', '[']);
        if (first < 0)
        {
            return Csv(text);
        }
        using var json = JsonDocument.Parse(text[first..], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var texts = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement
            : json.RootElement.TryGetProperty("texts", out var t) ? t : default;
        if (texts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var result = new List<ImportedText>();
        foreach (var item in texts.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } k
                && item.TryGetProperty("translation", out var tr) && tr.ValueKind == JsonValueKind.String && tr.GetString() is { Length: > 0 } words)
            {
                result.Add(new ImportedText(k, words));
            }
        }
        return result;
    }

    // CSV as the export writes it: # lines, a header naming "key" and "translation", RFC 4180 quoting
    private static List<ImportedText> Csv(string text)
    {
        var rows = Rows(text).Where(r => r.Count > 0 && !(r.Count == 1 && r[0].Length == 0) && !r[0].StartsWith('#')).ToList();
        if (rows.Count == 0)
        {
            return [];
        }
        var header = rows[0];
        var (ki, ti) = (header.IndexOf("key"), header.IndexOf("translation"));
        if (ki < 0 || ti < 0)
        {
            return [];
        }
        return [.. rows.Skip(1).Where(r => r.Count > Math.Max(ki, ti) && r[ki].Length > 0 && r[ti].Length > 0).Select(r => new ImportedText(r[ki], r[ti]))];
    }

    private static IEnumerable<List<string>> Rows(string text)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
                continue;
            }
            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    yield return row;
                    row = [];
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            yield return row;
        }
    }

    /// <summary>Puts <paramref name="texts"/> into <paramref name="project"/> by the rules above; <paramref name="marks"/> gets the review marks.</summary>
    public static ImportResult Apply(TextProject project, ReviewMarks marks, IReadOnlyList<ImportedText> texts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(texts);
        var result = new ImportResult();
        var byKey = project.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        // a card text's rows, gathered by their text
        var rowsOf = new Dictionary<string, List<(string Part, string Words)>>(StringComparer.Ordinal);
        var whole = new List<(TextEntry Entry, string Words)>();
        foreach (var t in texts)
        {
            var slash = t.Key.LastIndexOf('/');
            if (slash > 0 && t.Key.StartsWith("msg:", StringComparison.Ordinal) && byKey.ContainsKey(t.Key[..slash]))
            {
                if (!rowsOf.TryGetValue(t.Key[..slash], out var parts))
                {
                    rowsOf[t.Key[..slash]] = parts = [];
                }
                parts.Add((t.Key[(slash + 1)..], t.Translation));
            }
            else if (byKey.TryGetValue(t.Key, out var entry))
            {
                whole.Add((entry, t.Translation));
            }
            else
            {
                result.NotImported.Add((t.Key, "no text of the game has this key"));
            }
        }
        foreach (var (key, parts) in rowsOf)
        {
            var entry = byKey[key];
            var english = CardRows.Parse(entry.English);
            if (CardRows.Against(english, entry.Translation) is not { } rows)
            {
                result.NotImported.Add((key, "its translation's lines are not the English's: import it raw, or fix it in the Raw view"));
                continue;
            }
            var list = rows.ToList();
            var (option, passage) = (0, 0);
            for (var i = 0; i < english.Count; i++)
            {
                var part = english[i].Kind switch
                {
                    CardRowKind.Option => "option" + (++option).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CardRowKind.Passage => "passage" + (++passage).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => "intro",
                };
                if (parts.LastOrDefault(p => p.Part == part) is { Words: { } words })
                {
                    // the "…" the export put before an option's words (the English's lead-in, put back by the row)
                    words = words.TrimStart();
                    if (words.StartsWith('…'))
                    {
                        words = words[1..].TrimStart();
                    }
                    else if (words.StartsWith("...", StringComparison.Ordinal))
                    {
                        words = words[3..].TrimStart();
                    }
                    list[i] = (list[i].Content.Length == 0 ? english[i] with { Content = "" } : list[i]).WithField(words);
                }
                else if (list[i].Content.Length == 0)
                {
                    // a row the file does not give, in a text not translated yet: the English's, to translate later
                    list[i] = english[i];
                }
            }
            whole.Add((entry, CardRows.Write(list)));
        }
        foreach (var (entry, words) in whole)
        {
            if (entry.Translation == words)
            {
                continue;
            }
            if (entry.Translation is not null && !marks.Has(entry.Key))
            {
                // reviewed: kept; the file's words wait for Use
                result.Kept.Add((entry.Key, words));
                continue;
            }
            result.Before.TryAdd(entry.Key, (entry.Translation, marks.Has(entry.Key)));
            project.Set(entry, words);
            marks.Set(entry.Key, true);
            result.Imported.Add(entry.Key);
        }
        return result;
    }

    /// <summary>Takes the file's words for a text the import kept (reviewed): set, and marked for review.</summary>
    public static void Use(TextProject project, ReviewMarks marks, ImportResult result, string key)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Kept.FirstOrDefault(k => k.Key == key) is not { Theirs: { } words } || project.Entries.FirstOrDefault(e => e.Key == key) is not { } entry)
        {
            return;
        }
        result.Before.TryAdd(key, (entry.Translation, marks.Has(key)));
        project.Set(entry, words);
        marks.Set(key, true);
        result.Kept.RemoveAll(k => k.Key == key);
        result.Imported.Add(key);
    }

    /// <summary>Puts back every text the import (and its Use) changed, with its review mark as it was.</summary>
    public static void Undo(TextProject project, ReviewMarks marks, ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(result);
        var byKey = project.Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        foreach (var (key, (translation, marked)) in result.Before)
        {
            project.Set(byKey[key], translation);
            marks.Set(key, marked);
        }
        result.Before.Clear();
        result.Imported.Clear();
    }
}
