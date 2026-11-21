using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Presentation.Graphics;

namespace Darklands.Workshop.Text;

/// <summary>Where a card text's first letter takes its decorated capital from.</summary>
public enum CapitalSource
{
    /// <summary>The game's own picture (A–Z).</summary>
    Game,

    /// <summary>The pack's picture (capitals/).</summary>
    Pack,

    /// <summary>The game's picture of its Latin look-alike (É: E; В: B).</summary>
    LookAlike,

    /// <summary>None: the letter is drawn at twice its size in a box.</summary>
    Box,
}

/// <summary>A first letter of the pack's card texts: how many start with it, and where its capital comes from.</summary>
public sealed record CapitalUse(int Code, int Texts, CapitalSource Source)
{
    /// <summary>The letter.</summary>
    public string Letter => char.ConvertFromUtf32(Code);

    /// <summary>The Latin letter whose picture a look-alike takes (0: none).</summary>
    public char LookAlike => (char)Darklands.Presentation.Fonts.PixelFontBuilder.DropCapOf(Code);
}

/// <summary>
/// The decorated capitals of a pack (docs/LOCALISATION.md, "Decorated capitals"): the first letters its card texts start with, each
/// with where its picture comes from; a translator's PNG checked and put into the pack; the game's A–Z pictures as templates.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class CapitalList
{
    /// <summary>The first letters of <paramref name="project"/>'s card texts (translated ones), the most used first; its own pictures listed too.</summary>
    public static IReadOnlyList<CapitalUse> Of(TextProject project, PackCapitals capitals)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(capitals);
        var counts = new Dictionary<int, int>();
        foreach (var entry in project.Entries.Where(e => e.Key.StartsWith("msg:", StringComparison.Ordinal) && e.Translation is not null))
        {
            if (CardTextForm.ToCodes(entry.Translation!, out _) is { Length: > 0 } codes && codes[0] is var first and > 0 and <= 0xFFFF
                && char.IsLetter((char)first) && char.IsUpper((char)first))
            {
                counts[first] = counts.GetValueOrDefault(first) + 1;
            }
        }
        foreach (var own in capitals.Letters)
        {
            counts.TryAdd(own, 0);
        }
        return [.. counts.Select(c => new CapitalUse(c.Key, c.Value, SourceOf(c.Key, capitals)))
            .OrderBy(c => c.Source == CapitalSource.Box ? 0 : 1).ThenByDescending(c => c.Texts).ThenBy(c => c.Code)];
    }

    /// <summary>Where <paramref name="code"/>'s capital comes from.</summary>
    public static CapitalSource SourceOf(int code, PackCapitals capitals)
    {
        ArgumentNullException.ThrowIfNull(capitals);
        return capitals.Get(code) is not null ? CapitalSource.Pack
            : code is >= 'A' and <= 'Z' ? CapitalSource.Game
            : Darklands.Presentation.Fonts.PixelFontBuilder.DropCapOf(code) != 0 ? CapitalSource.LookAlike
            : CapitalSource.Box;
    }

    /// <summary>
    /// Puts the PNG <paramref name="png"/> into the pack as <paramref name="code"/>'s capital (capitals/U+XXXX.png) when it is one
    /// (the size and colours of the game's); returns why not, else null.
    /// </summary>
    public static string? Import(TextProject project, int code, string png)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(png);
        if (PackCapitals.ReadImage is not { } read)
        {
            return "this program cannot read PNG pictures";
        }
        try
        {
            var (width, height, argb) = read(png);
            var (image, why) = PackCapitals.Convert(width, height, argb, PackCapitals.Colours(new GameAssets(project.Game)), code);
            if (image is null)
            {
                return why;
            }
            var folder = Directory.CreateDirectory(Path.Combine(project.Folder, PackCapitals.Folder)).FullName;
            // one picture a letter: another name of the same letter (Φ.png) gives way to this one
            foreach (var other in Directory.EnumerateFiles(folder, "*.png").Where(f => PackCapitals.CodeOf(Path.GetFileName(f)) == code).ToList())
            {
                File.Delete(other);
            }
            File.Copy(png, Path.Combine(folder, PackCapitals.FileNameOf(code)), overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return e.Message;
        }
    }

    /// <summary>Takes <paramref name="code"/>'s picture out of the pack (its capitals/ file); true when there was one.</summary>
    public static bool Remove(TextProject project, int code)
    {
        ArgumentNullException.ThrowIfNull(project);
        var folder = Path.Combine(project.Folder, PackCapitals.Folder);
        if (!Directory.Exists(folder))
        {
            return false;
        }
        var files = Directory.EnumerateFiles(folder, "*.png").Where(f => PackCapitals.CodeOf(Path.GetFileName(f)) == code).ToList();
        files.ForEach(File.Delete);
        return files.Count > 0;
    }

    /// <summary>The game's 26 capitals as ARGB (A–Z, 20×20 each, in their colours): templates to draw a letter's own from.</summary>
    public static IReadOnlyList<(char Letter, uint[] Argb)> Templates(GameFiles game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var assets = new GameAssets(game);
        var list = new List<(char, uint[])>();
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            if (PackCapitals.Template(assets, letter) is { } argb)
            {
                list.Add((letter, argb));
            }
        }
        return list;
    }
}
