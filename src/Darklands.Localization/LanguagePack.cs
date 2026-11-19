using System.Text.Json;

namespace Darklands.Localization;

/// <summary>
/// A translation pack: a folder with a <c>pack.json</c> that names its language (<c>{ "language": "de", "name": "...",
/// "author": "...", "base": "483.07" }</c>) and Project Fluent files under <c>text/&lt;language&gt;/</c> (<c>text/de/exe.ftl</c>,
/// <c>text/de/msg/&lt;FILE&gt;.ftl</c>, <c>text/de/ui.ftl</c>: every <c>*.ftl</c> there, read in path order; the mods' layout, so the
/// 1.2 mod loader takes a language pack unchanged as a cosmetic mod). Its
/// <see cref="Localizer"/> answers the keys the pack translates; for any other key the caller keeps the original text.
/// Loading never throws for a broken pack: what is wrong is in <see cref="Problems"/> and the valid entries are kept.
/// </summary>
// Not in the original: the 1.1 translation packs (docs/LOCALISATION.md).
public sealed class LanguagePack
{
    /// <summary>The manifest's file name.</summary>
    public const string ManifestName = "pack.json";

    /// <summary>The folder of a language's Fluent files, under the pack's folder.</summary>
    public static string TextFolder(string language) => Path.Combine("text", language);

    private LanguagePack(string folder, string language, string? name, string? author, string? baseVersion, FluentLocalizer localizer,
        IReadOnlyList<LocalizationDiagnostic> problems, int files)
    {
        Folder = folder;
        Language = language;
        Name = name;
        Author = author;
        Base = baseVersion;
        Localizer = localizer;
        Problems = problems;
        Files = files;
    }

    /// <summary>The pack's folder.</summary>
    public string Folder { get; }

    /// <summary>The language tag of the manifest (<c>de</c>, <c>fr</c>, <c>pt-BR</c>; <c>qps</c> the pseudo-locale).</summary>
    public string Language { get; }

    /// <summary>The pack's name (the manifest's, else the folder's).</summary>
    public string? Name { get; }

    public string? Author { get; }

    /// <summary>The game version the pack translates (the manifest's "base"; the only one supported: 483.07).</summary>
    public string? Base { get; }

    /// <summary>The pack's texts over no fallback: <see cref="ILocalizer.IsTranslated"/> says which keys it has.</summary>
    public FluentLocalizer Localizer { get; }

    /// <summary>The problems found while loading (an unreadable manifest, a Fluent syntax error with its file and line).</summary>
    public IReadOnlyList<LocalizationDiagnostic> Problems { get; }

    /// <summary>How many <c>.ftl</c> files were read.</summary>
    public int Files { get; }

    /// <summary>Whether <paramref name="folder"/> holds a manifest that names a language (a translation pack, not another kind of pack).</summary>
    public static bool IsPack(string folder) => ReadManifest(folder, out _) is { Language: not null };

    /// <summary>Loads the pack in <paramref name="folder"/>; null when the folder holds no translation pack (its reason in <paramref name="why"/>).</summary>
    public static LanguagePack? Load(string folder, out string? why)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Directory.Exists(folder))
        {
            why = $"{folder}: no such folder";
            return null;
        }
        var manifest = ReadManifest(folder, out why);
        if (manifest is null)
        {
            return null;
        }
        if (manifest.Language is not { Length: > 0 } language)
        {
            why = $"{Path.Combine(folder, ManifestName)}: no \"language\": not a translation pack";
            return null;
        }
        var localizer = new FluentLocalizer(language, OriginalTable.Empty);
        var problems = new List<LocalizationDiagnostic>();
        var text = Path.Combine(folder, TextFolder(language));
        if (!Directory.Exists(text))
        {
            problems.Add(new LocalizationDiagnostic(ManifestName, 0, $"no {TextFolder(language).Replace('\\', '/')} folder: nothing to translate with"));
        }
        if (manifest.Base is { } version && version != "483.07")
        {
            problems.Add(new LocalizationDiagnostic(ManifestName, 0, $"made for the game's version {version}; only 483.07 is supported"));
        }
        var files = Directory.Exists(text)
            ? Directory.EnumerateFiles(text, "*.ftl", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList()
            : [];
        foreach (var file in files)
        {
            try
            {
                problems.AddRange(localizer.AddOverlay(Path.GetRelativePath(folder, file), File.ReadAllText(file)));
            }
            catch (IOException e)
            {
                problems.Add(new LocalizationDiagnostic(Path.GetRelativePath(folder, file), 0, e.Message));
            }
        }
        why = null;
        return new LanguagePack(folder, language, manifest.Name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), manifest.Author,
            manifest.Base, localizer, problems, files.Count);
    }

    private sealed record Manifest(string? Language, string? Name, string? Author, string? Base);

    private static Manifest? ReadManifest(string folder, out string? why)
    {
        var path = Path.Combine(folder, ManifestName);
        if (!File.Exists(path))
        {
            why = $"{folder}: no {ManifestName}";
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            string? Text(string name) =>
                json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
            why = null;
            return new Manifest(Text("language"), Text("name"), Text("author"), Text("base"));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            why = $"{path}: {e.Message}";
            return null;
        }
    }
}
