using System.Text;
using System.Text.RegularExpressions;

namespace Darklands.Workshop.Text;

/// <summary>One term of a pack's glossary: the English term, its translation, a note.</summary>
public sealed record GlossaryTerm(string Term, string Translation, string Note);

/// <summary>
/// A pack's glossary (W-0 + E-text): the translators' agreed terms (saints, places, items, trade words), kept as <c>glossary.tsv</c>
/// in the pack's folder (term, translation, note; one a line, tab-separated). The game does not read it. The editor lists the terms
/// a text's English holds, and notes a translation that does not use a term's agreed words.
/// </summary>
// Not in the original: the 1.1 Workshop.
public sealed class Glossary
{
    /// <summary>The glossary's file name in a pack's folder.</summary>
    public const string FileName = "glossary.tsv";

    private readonly List<GlossaryTerm> _terms;

    private Glossary(List<GlossaryTerm> terms) => _terms = terms;

    public IReadOnlyList<GlossaryTerm> Terms => _terms;

    /// <summary>The glossary of the pack in <paramref name="folder"/> (none: empty).</summary>
    public static Glossary Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        var terms = new List<GlossaryTerm>();
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }
                var parts = line.Split('\t');
                if (parts[0].Trim().Length > 0)
                {
                    terms.Add(new GlossaryTerm(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "", parts.Length > 2 ? parts[2].Trim() : ""));
                }
            }
        }
        return new Glossary(terms);
    }

    /// <summary>Adds or replaces <paramref name="term"/> (by its English term, case ignored).</summary>
    public void Set(GlossaryTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        _terms.RemoveAll(t => string.Equals(t.Term, term.Term, StringComparison.OrdinalIgnoreCase));
        if (term.Term.Trim().Length > 0)
        {
            _terms.Add(term);
        }
    }

    /// <summary>Removes the term <paramref name="term"/> (case ignored).</summary>
    public void Remove(string term) => _terms.RemoveAll(t => string.Equals(t.Term, term, StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes the glossary to the pack in <paramref name="folder"/>.</summary>
    public void Save(string folder)
    {
        var text = new StringBuilder("# term\ttranslation\tnote (the Darklands Workshop's glossary; the game does not read it)\n");
        foreach (var t in _terms.OrderBy(t => t.Term, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(Clean(t.Term)).Append('\t').Append(Clean(t.Translation)).Append('\t').Append(Clean(t.Note)).Append('\n');
        }
        File.WriteAllText(Path.Combine(folder, FileName), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The terms <paramref name="english"/> holds (as whole words, case ignored).</summary>
    public IReadOnlyList<GlossaryTerm> In(string english) =>
        [.. _terms.Where(t => Regex.IsMatch(english ?? "", @"(?<![\p{L}\p{N}])" + Regex.Escape(t.Term) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase))];

    /// <summary>The terms of <paramref name="english"/> whose agreed translation <paramref name="translation"/> does not use.</summary>
    public IReadOnlyList<GlossaryTerm> Missed(string english, string translation) =>
        [.. In(english).Where(t => t.Translation.Length > 0 && (translation ?? "").IndexOf(t.Translation, StringComparison.OrdinalIgnoreCase) < 0)];

    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}
