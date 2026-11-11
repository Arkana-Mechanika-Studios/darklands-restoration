namespace Darklands.Platform.Reporting;

/// <summary>
/// The address of the project's issue form (<c>.github/ISSUE_TEMPLATE/bug_report.yml</c>) with some fields filled in. The query
/// names are the form's field ids (kind, version, system, what_happened); nothing private goes in (version, commit, system,
/// the first lines of an error).
/// </summary>
public static class IssueForm
{
    public const string Repository = "https://github.com/Arkana-Mechanika-Studios/darklands-restoration";

    public const string Template = "bug_report.yml";

    /// <summary>The longest address built; browsers and GitHub take more, but a short one is safe everywhere.</summary>
    public const int MaxLength = 6000;

    // the dropdown entries of the form's "kind" field (a prefilled value must be one of them)
    public const string KindCrash = "Crash";

    public const string KindDifference = "Difference from the original game";

    public const string KindOther = "Other bug";

    /// <param name="kind">One of the form's kinds.</param>
    /// <param name="version">The version with the commit.</param>
    /// <param name="system">Operating system and .NET.</param>
    /// <param name="happened">What happened: the trigger and the first lines of the error; cut to fit <see cref="MaxLength"/>; empty: the field is left empty.</param>
    /// <param name="title">The issue's title, or null.</param>
    public static string Url(string kind, string version, string system, string happened, string? title = null)
    {
        string Build(string text) =>
            Repository + "/issues/new?template=" + Template
            + "&kind=" + Uri.EscapeDataString(kind)
            + "&version=" + Uri.EscapeDataString(version)
            + "&system=" + Uri.EscapeDataString(system)
            + (title is null ? "" : "&title=" + Uri.EscapeDataString(title))
            + (text.Length == 0 ? "" : "&what_happened=" + Uri.EscapeDataString(text));

        var url = Build(happened);
        var text = happened;
        // shorten the text (the field that can be long) until the address fits
        while (url.Length > MaxLength && text.Length > 0)
        {
            text = text[..Math.Max(0, text.Length - Math.Max(20, (url.Length - MaxLength) / 3))];
            url = Build(text.TrimEnd() + " ...");
        }
        if (url.Length > MaxLength)
        {
            url = Repository + "/issues/new?template=" + Template + "&kind=" + Uri.EscapeDataString(kind)
                + "&version=" + Uri.EscapeDataString(version[..Math.Min(version.Length, 200)]);
        }
        return url;
    }

    /// <summary>The first <paramref name="lines"/> lines of <paramref name="text"/> (at most <paramref name="characters"/> characters).</summary>
    public static string FirstLines(string text, int lines = 6, int characters = 700)
    {
        var picked = string.Join('\n', text.Replace("\r", "").Split('\n').Take(lines));
        return picked.Length > characters ? picked[..characters] : picked;
    }
}
