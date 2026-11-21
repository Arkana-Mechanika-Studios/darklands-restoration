using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Darklands.Workshop.Text;

/// <summary>
/// The export and import in XLIFF 2.0 (for CAT tools and DeepL): one <c>&lt;unit&gt;</c> a text (its key the unit's id, where it is
/// shown its name), its context in <c>&lt;notes&gt;</c> (the room, what it must keep, its glossary terms), the English in
/// <c>&lt;source&gt;</c> and the translation in <c>&lt;target&gt;</c>. A text's <c>$Names</c>, <c>{…}</c> placeables and tags are inline
/// codes (<c>&lt;ph disp="$Money1"/&gt;</c>) the tools keep apart from the words; reading turns them back.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static partial class TextXliff
{
    private static readonly XNamespace Ns = "urn:oasis:names:tc:xliff:document:2.0";

    // what a translation keeps as it is: a $Name, a {…} placeable, a tag
    [GeneratedRegex(@"\$[A-Za-z][A-Za-z0-9_]*|\{[^}]*\}|<[^>]+>")]
    private static partial Regex Codes();

    /// <summary>The items as an XLIFF 2.0 document from English to the project's language.</summary>
    public static string Write(TextProject project, IReadOnlyList<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(items);
        var file = new XElement(Ns + "file", new XAttribute("id", "darklands"),
            new XElement(Ns + "notes", TextExport.Instructions(project).Select(l => new XElement(Ns + "note", new XAttribute("category", "instructions"), l))));
        foreach (var item in items)
        {
            var notes = new XElement(Ns + "notes");
            void Note(string category, string text)
            {
                if (text.Length > 0)
                {
                    notes.Add(new XElement(Ns + "note", new XAttribute("category", category), text));
                }
            }
            Note("location", item.Where);
            Note("room", item.Room);
            Note("keep", string.Join("; ", item.Keep));
            Note("glossary", string.Join("; ", item.Terms.Select(t => $"{t.Term} = {t.Translation}")));
            Note("description", item.Note == item.Room ? "" : item.Note);
            var segment = new XElement(Ns + "segment", new XElement(Ns + "source", Inline(item.English)));
            if (item.Translation is { Length: > 0 } translation)
            {
                segment.Add(new XElement(Ns + "target", Inline(translation)));
            }
            file.Add(new XElement(Ns + "unit", new XAttribute("id", item.Key), new XAttribute("name", item.Where),
                notes.HasElements ? notes : null, segment));
        }
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "xliff", new XAttribute("version", "2.0"), new XAttribute("srcLang", "en"), new XAttribute("trgLang", project.Language), file));
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        document.Save(writer);
        return writer.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal) + "\n";
    }

    // a text's words and its inline codes
    private static IEnumerable<object> Inline(string text)
    {
        var at = 0;
        var id = 0;
        foreach (Match m in Codes().Matches(text))
        {
            if (m.Index > at)
            {
                yield return text[at..m.Index];
            }
            yield return new XElement(Ns + "ph", new XAttribute("id", (++id).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("disp", m.Value), new XAttribute("equiv", m.Value));
            at = m.Index + m.Length;
        }
        if (at < text.Length)
        {
            yield return text[at..];
        }
    }

    /// <summary>The translations of an XLIFF document (the units with a target): their keys and words, the inline codes turned back.</summary>
    public static IReadOnlyList<ImportedText> Read(string xliff)
    {
        ArgumentNullException.ThrowIfNull(xliff);
        var document = XDocument.Parse(xliff);
        var result = new List<ImportedText>();
        foreach (var unit in document.Descendants().Where(e => e.Name.LocalName == "unit"))
        {
            if (unit.Attribute("id")?.Value is not { Length: > 0 } key)
            {
                continue;
            }
            var target = string.Concat(unit.Descendants().Where(e => e.Name.LocalName == "target").Select(Words));
            if (target.Length > 0)
            {
                result.Add(new ImportedText(key, target));
            }
        }
        return result;
    }

    private static string Words(XElement target) => string.Concat(target.Nodes().Select(n => n switch
    {
        XText t => t.Value,
        XElement { Name.LocalName: "ph" } ph => ph.Attribute("equiv")?.Value ?? ph.Attribute("disp")?.Value ?? "",
        XElement e => Words(e),
        _ => "",
    }));
}
