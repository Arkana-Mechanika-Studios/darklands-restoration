using System.Text.RegularExpressions;

namespace Darklands.Workshop.Text;

/// <summary>A language a pack may be written in: its tag, its own name and its English name.</summary>
public sealed record Language(string Tag, string Native, string English)
{
    /// <summary>"Polski · Polish" (the list's line).</summary>
    public override string ToString() => Native == English ? Native : $"{Native} · {English}";
}

/// <summary>
/// The evident languages of a new pack (W-0 polish: the new pack dialog), each by its own name, its English name and its tag, and
/// the check of another tag (a well-formed BCP 47 language tag).
/// </summary>
// Not in the original: the 1.1 Workshop.
public static partial class Languages
{
    /// <summary>The list, in the order of their English names.</summary>
    public static IReadOnlyList<Language> All { get; } =
    [
        new("ar", "العربية", "Arabic"), new("eu", "Euskara", "Basque"), new("bg", "Български", "Bulgarian"), new("ca", "Català", "Catalan"),
        new("zh-Hans", "简体中文", "Chinese, Simplified"), new("zh-Hant", "繁體中文", "Chinese, Traditional"), new("hr", "Hrvatski", "Croatian"),
        new("cs", "Čeština", "Czech"), new("da", "Dansk", "Danish"), new("nl", "Nederlands", "Dutch"), new("en", "English", "English"),
        new("eo", "Esperanto", "Esperanto"), new("et", "Eesti", "Estonian"), new("fi", "Suomi", "Finnish"), new("fr", "Français", "French"),
        new("gl", "Galego", "Galician"), new("de", "Deutsch", "German"), new("el", "Ελληνικά", "Greek"), new("he", "עברית", "Hebrew"),
        new("hu", "Magyar", "Hungarian"), new("is", "Íslenska", "Icelandic"), new("ga", "Gaeilge", "Irish"), new("it", "Italiano", "Italian"),
        new("ja", "日本語", "Japanese"), new("ko", "한국어", "Korean"), new("la", "Latina", "Latin"), new("lv", "Latviešu", "Latvian"),
        new("lt", "Lietuvių", "Lithuanian"), new("nb", "Norsk bokmål", "Norwegian Bokmål"), new("nn", "Norsk nynorsk", "Norwegian Nynorsk"),
        new("pl", "Polski", "Polish"), new("pt", "Português", "Portuguese"), new("pt-BR", "Português (Brasil)", "Portuguese, Brazil"),
        new("ro", "Română", "Romanian"), new("ru", "Русский", "Russian"), new("sr", "Српски", "Serbian"), new("sk", "Slovenčina", "Slovak"),
        new("sl", "Slovenščina", "Slovenian"), new("es", "Español", "Spanish"), new("es-419", "Español (Latinoamérica)", "Spanish, Latin America"),
        new("sv", "Svenska", "Swedish"), new("tr", "Türkçe", "Turkish"), new("uk", "Українська", "Ukrainian"), new("cy", "Cymraeg", "Welsh"),
    ];

    /// <summary>The languages whose own name, English name or tag holds <paramref name="query"/> (any case; empty: all).</summary>
    public static IReadOnlyList<Language> Find(string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            return All;
        }
        bool Has(string s) => s.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        return [.. All.Where(l => Has(l.Native) || Has(l.English) || Has(l.Tag))
            .OrderBy(l => l.Tag.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0 : l.Native.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) || l.English.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 1 : 2)];
    }

    /// <summary>The listed language of <paramref name="tag"/> (any case), or null.</summary>
    public static Language? Of(string tag) => All.FirstOrDefault(l => l.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));

    // language (2-3 letters, or 4-8), extlang, script, region, variants, extensions, private use (RFC 5646's well-formed tags, without the grandfathered ones)
    [GeneratedRegex(@"^(?:[a-zA-Z]{2,3}(?:-[a-zA-Z]{3}){0,3}|[a-zA-Z]{4,8})(?:-[a-zA-Z]{4})?(?:-(?:[a-zA-Z]{2}|\d{3}))?(?:-(?:[a-zA-Z\d]{5,8}|\d[a-zA-Z\d]{3}))*(?:-[0-9a-wy-zA-WY-Z](?:-[a-zA-Z\d]{2,8})+)*(?:-x(?:-[a-zA-Z\d]{1,8})+)?$")]
    private static partial Regex Bcp47();

    /// <summary>Whether <paramref name="tag"/> is a well-formed BCP 47 language tag ("fr", "pt-BR", "sr-Latn", "es-419", "gsw-CH").</summary>
    public static bool IsWellFormed(string? tag) => tag is { Length: > 0 and <= 35 } && Bcp47().IsMatch(tag);
}
