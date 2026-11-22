using Darklands.Content;
using Darklands.Localization;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// Whether a language pack needs letters the original fonts lack (its texts hold letters the game's fonts have no glyph for) and
/// brings no font of its own: such a pack plays with the HD fonts (the launcher switches them on, the game started with
/// <c>--language-pack</c> alone takes them), or its letters show as '?'.
/// </summary>
// Not in the original: the 1.1 translations.
public static class PackLetters
{
    /// <summary>The letters of <paramref name="pack"/>'s texts that the game's own font (slot 1) has no glyph for, each once.</summary>
    public static string Lacking(GameFiles files, LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var font = new TextPainter(new GameAssets(files).Font(1)).CodeFont;
        var lacking = new SortedSet<char>();
        foreach (var (_, message) in pack.Messages)
        {
            foreach (var c in message.Value)
            {
                if (c >= 0x80 && !char.IsWhiteSpace(c) && !font.Has(c))
                {
                    lacking.Add(c);
                }
            }
        }
        return string.Concat(lacking);
    }

    /// <summary>
    /// The language's English name ("Russian") when the pack in <paramref name="packFolder"/> needs letters the original fonts of the
    /// game in <paramref name="gameFolder"/> lack and has no font of its own; null when it does not (or cannot be read).
    /// </summary>
    public static string? NeedsFonts(string packFolder, string? gameFolder)
    {
        try
        {
            if (gameFolder is null || !GameFiles.IsGameFolder(gameFolder) || Directory.Exists(Path.Combine(packFolder, FontPack.Folder))
                || LanguagePack.Load(packFolder, out _) is not { FontsOnly: false } pack || pack.Language is null or "en"
                || Lacking(new GameFiles(gameFolder), pack).Length == 0)
            {
                return null;
            }
            return LanguageName(pack.Language);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>"Russian" for <c>ru</c> (the language's English name), the tag itself when unknown.</summary>
    public static string LanguageName(string tag)
    {
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(tag.Split('-')[0]);
            return culture.EnglishName is { Length: > 0 } name && !name.StartsWith("Unknown", StringComparison.Ordinal) ? name : tag;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return tag;
        }
    }
}
