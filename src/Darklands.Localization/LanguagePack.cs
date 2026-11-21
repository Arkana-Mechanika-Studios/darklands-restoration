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
        IReadOnlyList<LocalizationDiagnostic> problems, int files, IReadOnlyList<(string File, FtlSource.Message Message)> messages)
    {
        Messages = messages;
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

    /// <summary>
    /// The Workshop that last wrote the pack (the manifest's "workshop", as <c>1.1.0-beta.1</c>; null: not said). The game does nothing
    /// with it but say it in a problem report.
    /// </summary>
    // Not in the original: the 1.1 Workshop's versions.
    public string? Workshop { get; private init; }

    /// <summary>The Workshop that last wrote the pack in <paramref name="folder"/> (its manifest's "workshop"), or null.</summary>
    public static string? WorkshopOf(string folder) => ReadManifest(folder, out _)?.Workshop;

    /// <summary>The pack's texts over no fallback: <see cref="ILocalizer.IsTranslated"/> says which keys it has.</summary>
    public FluentLocalizer Localizer { get; }

    /// <summary>The pack's screen layouts by screen, then slot (<see cref="SlotLayouts"/>, interface/layout.json; empty: every text where the original draws it).</summary>
    public IReadOnlyDictionary<string, Dictionary<string, SlotLayout>> Layouts { get; init; } = new Dictionary<string, Dictionary<string, SlotLayout>>(StringComparer.Ordinal);

    /// <summary>The problems found while loading (an unreadable manifest, a Fluent syntax error with its file and line).</summary>
    public IReadOnlyList<LocalizationDiagnostic> Problems { get; }

    /// <summary>How many <c>.ftl</c> files were read.</summary>
    public int Files { get; }

    /// <summary>The prefix of a pack's own card-text variables: <c>var-him1</c> is <c>$him1</c>.</summary>
    public const string VariablePrefix = "var-";

    /// <summary>
    /// The pack's own card-text variables (docs/LOCALISATION.md): the names its <c>var-&lt;name&gt;</c> messages define, which its card
    /// texts may use beside the English <c>$Names</c> (a language's inflected pronouns: <c>$him1</c>).
    /// </summary>
    public IReadOnlySet<string> Variables => _variables ??= Messages
        .Select(m => m.Message.Id)
        .Where(id => id.StartsWith(VariablePrefix, StringComparison.Ordinal) && id.Length > VariablePrefix.Length && id[VariablePrefix.Length..].All(char.IsAsciiLetterOrDigit))
        .Select(id => id[VariablePrefix.Length..])
        .ToHashSet(StringComparer.Ordinal);

    private IReadOnlySet<string>? _variables;

    /// <summary>The words of the pack's variable <paramref name="name"/> for the chosen one's sex (<c>$sex</c>: female or male), or null.</summary>
    public string? Variable(string name, bool female) =>
        Variables.Contains(name)
            ? Localizer.GetById(VariablePrefix + name, new Dictionary<string, object?> { ["sex"] = female ? "female" : "male" })
            : null;

    /// <summary>The messages the files define as written (the file relative to the pack's folder): for the pack's checks.</summary>
    public IReadOnlyList<(string File, FtlSource.Message Message)> Messages { get; }

    /// <summary>Whether <paramref name="folder"/> holds a manifest that names a language (a translation pack, not another kind of pack).</summary>
    public static bool IsPack(string folder) => ReadManifest(folder, out _) is { Language: not null } or { Fonts: true };

    /// <summary>
    /// Whether the folder holds a font-only pack: <c>{"fonts": true}</c> with no language (or <c>"en"</c>) and no translations: the
    /// game's English drawn in its fonts (high-resolution text for English players).
    /// </summary>
    public static bool IsFontPack(string folder) => ReadManifest(folder, out _) is { Fonts: true, Language: null or "en" };

    /// <summary>Whether this pack only brings fonts (<see cref="IsFontPack"/>).</summary>
    public bool FontsOnly { get; private init; }

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
        var fontsOnly = manifest.Fonts && manifest.Language is null or "en";
        if (manifest.Language is not { Length: > 0 } && !fontsOnly)
        {
            why = $"{Path.Combine(folder, ManifestName)}: no \"language\": not a translation pack";
            return null;
        }
        var language = manifest.Language is { Length: > 0 } named ? named : "en";
        // the pack's texts as written (a qps pack is made already transformed: PseudoLocale's own letters are not the game fonts')
        var localizer = new FluentLocalizer(language, OriginalTable.Empty, null, pseudoTransform: false);
        var problems = new List<LocalizationDiagnostic>();
        var text = Path.Combine(folder, TextFolder(language));
        if (!Directory.Exists(text) && !fontsOnly)
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
        var messages = new List<(string, FtlSource.Message)>();
        foreach (var file in files)
        {
            try
            {
                var relative = Path.GetRelativePath(folder, file);
                var source = File.ReadAllText(file);
                problems.AddRange(localizer.AddOverlay(relative, source));
                messages.AddRange(FtlSource.Messages(source).Select(m => (relative, m)));
            }
            catch (IOException e)
            {
                problems.Add(new LocalizationDiagnostic(Path.GetRelativePath(folder, file), 0, e.Message));
            }
        }
        why = null;
        // its screen layouts (interface/layout.json): where its texts are drawn wider, on more lines or in another font
        var layouts = SlotLayouts.Load(folder, out var layoutProblem);
        if (layoutProblem is not null)
        {
            problems.Add(new LocalizationDiagnostic(SlotLayouts.FileName, 0, layoutProblem));
        }
        return new LanguagePack(folder, language, manifest.Name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), manifest.Author,
            manifest.Base, localizer, problems, files.Count, messages) { Layouts = layouts, FontsOnly = fontsOnly, Workshop = manifest.Workshop };
    }

    // the manifest's fields the game reads; any other field (a Workshop's "style", a future one) is ignored
    private sealed record Manifest(string? Language, string? Name, string? Author, string? Base, bool Fonts = false, string? Workshop = null);

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
            var fonts = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("fonts", out var f) && f.ValueKind == JsonValueKind.True;
            return new Manifest(Text("language"), Text("name"), Text("author"), Text("base"), fonts, Text("workshop"));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            why = $"{path}: {e.Message}";
            return null;
        }
    }
}
