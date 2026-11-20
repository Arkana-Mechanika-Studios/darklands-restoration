using System.Text;
using System.Text.Json;
using Darklands.Content;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Workshop.Text;

/// <summary>The state of a text in a pack: not translated yet, translated, or translated with a problem the check found.</summary>
public enum TextState
{
    Untranslated,
    Translated,
    Problem,
}

/// <summary>One text of the game to translate: the template's message (its key, English, comments, screens) and the pack's words.</summary>
public sealed class TextEntry(TranslatorTemplate.Message template)
{
    public TranslatorTemplate.Message Template => template;

    public string Id => template.Id;

    public string Key => template.Key;

    /// <summary>The English as the template writes it (Fluent value: placeables, tags).</summary>
    public string English => template.Value;

    /// <summary>The pack's value as written in its file (null: not translated).</summary>
    public string? Translation { get; internal set; }

    /// <summary>The check's findings for this text (after <see cref="TextProject.Check"/>).</summary>
    public IReadOnlyList<LanguagePackCheck.Finding> Problems { get; internal set; } = [];

    public TextState State =>
        Translation is null ? TextState.Untranslated : Problems.Any(p => p.Severity != LanguagePackCheck.Severity.Note) ? TextState.Problem : TextState.Translated;

    /// <summary>Where the text is shown: its screens, or its card; "not seen" when no recorded session showed it.</summary>
    public IReadOnlyList<string> Screens => template.Screens.Count > 0 ? template.Screens : [TextProject.NotSeen];
}

/// <summary>
/// A translation pack as the translators' editor sees it (W-0 + E-text): every text of the player's game (the translator's template,
/// with the boxes the pack holds), the pack's words for them, and the pack's other messages (its own variables, anything else),
/// kept as they are. Saving writes the pack's <c>.ftl</c> files in the template's layout and order, each message under the
/// template's comment; a pack saved here loads in the game unchanged. The pack lives in its own folder, outside the repository.
/// </summary>
// Not in the original: the 1.1 Workshop (decision 13).
public sealed class TextProject
{
    /// <summary>The group of the texts no recorded session showed.</summary>
    public const string NotSeen = "not seen";

    private readonly Dictionary<string, TextEntry> _byId;
    private readonly List<(string File, FtlSource.Message Message)> _others;

    private TextProject(string folder, string language, GameFiles game, IReadOnlyList<TextBox> boxes, List<TextEntry> entries,
        List<(string, FtlSource.Message)> others)
    {
        Folder = folder;
        Language = language;
        Game = game;
        Boxes = boxes;
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        _others = others;
    }

    public string Folder { get; }

    public string Language { get; }

    public GameFiles Game { get; }

    public IReadOnlyList<TextBox> Boxes { get; }

    private Dictionary<string, Dictionary<string, SlotLayout>>? _layouts;

    /// <summary>The pack's screen layouts by screen, then slot (interface/layout.json), as edited; saved with the pack.</summary>
    public Dictionary<string, Dictionary<string, SlotLayout>> Layouts => _layouts ??= SlotLayouts.Load(Folder, out _);

    /// <summary>The layout of the screen text <paramref name="entry"/> (on the first of its screens that has one; null: the original's).</summary>
    public SlotLayout? LayoutOf(TextEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Screens.Select(s => Layouts.GetValueOrDefault(s)?.GetValueOrDefault(entry.Key)).FirstOrDefault(l => l is not null);
    }

    /// <summary>Sets (or with null or the original's, takes off) the layout of the screen text <paramref name="entry"/> on each of its screens.</summary>
    public void SetLayout(TextEntry entry, SlotLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var screen in entry.Screens)
        {
            if (!Layouts.TryGetValue(screen, out var slots))
            {
                Layouts[screen] = slots = new Dictionary<string, SlotLayout>(StringComparer.Ordinal);
            }
            if (layout is null || layout.IsOriginal)
            {
                slots.Remove(entry.Key);
            }
            else
            {
                slots[entry.Key] = layout;
            }
        }
        Dirty = true;
    }

    /// <summary>Every text, in the template's order.</summary>
    public IReadOnlyList<TextEntry> Entries { get; }

    /// <summary>The pack's messages that are not texts of the template (its own variables, a hand-written one), kept on save.</summary>
    public IReadOnlyList<(string File, FtlSource.Message Message)> Others => _others;

    /// <summary>Unsaved changes.</summary>
    public bool Dirty { get; private set; }

    /// <summary>The folder of the pack's text files.</summary>
    public string TextFolder => Path.Combine(Folder, LanguagePack.TextFolder(Language));

    /// <summary>Makes a new pack in <paramref name="folder"/> (its <c>pack.json</c>) and opens it.</summary>
    public static TextProject Create(string folder, string language, string name, string? author, string gameFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Directory.CreateDirectory(Path.Combine(folder, LanguagePack.TextFolder(language)));
        var manifest = new Dictionary<string, string> { ["language"] = language, ["name"] = name, ["base"] = "483.07" };
        if (author is { Length: > 0 })
        {
            manifest["author"] = author;
        }
        File.WriteAllText(Path.Combine(folder, LanguagePack.ManifestName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return Open(folder, gameFolder);
    }

    /// <summary>Opens the pack in <paramref name="folder"/> against the game in <paramref name="gameFolder"/> (its <c>boxes.json</c> when it has one).</summary>
    public static TextProject Open(string folder, string gameFolder)
    {
        var pack = LanguagePack.Load(folder, out var why) ?? throw new InvalidDataException(why);
        var game = new GameFiles(gameFolder);
        var boxesFile = Path.Combine(folder, TranslatorCommands.BoxesFile);
        // the pack's own boxes, else the ones the Workshop ships
        var boxes = File.Exists(boxesFile) ? TextBoxes.Read(boxesFile) : WorkshopBoxes.Default;
        var entries = TranslatorTemplate.Build(game, boxes).Select(m => new TextEntry(m)).ToList();
        var byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var others = new List<(string, FtlSource.Message)>();
        var text = Path.Combine(folder, LanguagePack.TextFolder(pack.Language));
        foreach (var (file, message) in pack.Messages)
        {
            if (byId.TryGetValue(message.Id, out var entry) && entry.Translation is null)
            {
                entry.Translation = message.Value;
            }
            else
            {
                others.Add((Path.GetRelativePath(text, Path.Combine(folder, file)).Replace('\\', '/'), message));
            }
        }
        return new TextProject(folder, pack.Language, game, boxes, entries, others);
    }

    /// <summary>The file a new pack variable goes to.</summary>
    public const string VariablesFile = "variables.ftl";

    /// <summary>The pack's own card-text variables (docs/LOCALISATION.md): name, the female and the male words (empty when not a $sex choice).</summary>
    public IReadOnlyList<(string Name, string Female, string Male)> Variables =>
        [.. _others.Where(o => o.Message.Id.StartsWith(LanguagePack.VariablePrefix, StringComparison.Ordinal))
            .Select(o => (o.Message.Id[LanguagePack.VariablePrefix.Length..], FluentChoice.Read(o.Message.Value)))
            .Select(v => (v.Item1,
                v.Item2?.Variants.FirstOrDefault(x => x.Key == "female").Words ?? "",
                v.Item2?.Variants.FirstOrDefault(x => x.Key == "male").Words ?? ""))];

    /// <summary>Sets the pack variable <paramref name="name"/> (letters and digits) to its female and male words; a new one goes to variables.ftl.</summary>
    public void SetVariable(string name, string female, string male)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!name.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("A variable's name is letters and digits only (him1).", nameof(name));
        }
        var id = LanguagePack.VariablePrefix + name;
        var value = FluentChoice.Write("sex", [new("female", female, false), new("male", male, true)]);
        var at = _others.FindIndex(o => o.Message.Id == id);
        var message = new FtlSource.Message(id, 0, new HashSet<string>(["sex"], StringComparer.Ordinal)) { Value = value };
        if (at >= 0)
        {
            _others[at] = (_others[at].File, message);
        }
        else
        {
            _others.Add((VariablesFile, message));
        }
        Dirty = true;
    }

    /// <summary>Removes the pack variable <paramref name="name"/>.</summary>
    public void RemoveVariable(string name)
    {
        if (_others.RemoveAll(o => o.Message.Id == LanguagePack.VariablePrefix + name) > 0)
        {
            Dirty = true;
        }
    }

    /// <summary>The text <paramref name="id"/> (null: none).</summary>
    public TextEntry? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Sets a text's words (Fluent value as written; null or empty: untranslated again).</summary>
    public void Set(TextEntry entry, string? translation)
    {
        ArgumentNullException.ThrowIfNull(entry);
        translation = string.IsNullOrWhiteSpace(translation) ? null : translation;
        if (entry.Translation != translation)
        {
            entry.Translation = translation;
            entry.Problems = [];
            Dirty = true;
        }
    }

    /// <summary>How many texts are translated, of how many (of <paramref name="entries"/>, else all).</summary>
    public (int Translated, int All) Progress(IEnumerable<TextEntry>? entries = null)
    {
        var list = (entries ?? Entries).ToList();
        return (list.Count(e => e.Translation is not null), list.Count);
    }

    /// <summary>The texts by where they are shown (a text shown on several screens is in each), the screens in name order, "not seen" last.</summary>
    public IReadOnlyList<(string Screen, IReadOnlyList<TextEntry> Entries)> ByScreen() =>
        [.. Entries.SelectMany(e => e.Screens.Select(s => (Screen: s, Entry: e)))
            .GroupBy(p => p.Screen, StringComparer.Ordinal)
            .OrderBy(g => g.Key == NotSeen ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, (IReadOnlyList<TextEntry>)g.Select(p => p.Entry).ToList()))];

    /// <summary>
    /// Writes the pack's files: one per template file (exe.ftl, ui.ftl, msg/&lt;FILE&gt;.ftl) with its translated texts in the
    /// template's order, each under the template's comment, and the pack's other messages in their own files as they were. A file
    /// the pack had is rewritten, never removed.
    /// </summary>
    public void Save()
    {
        var files = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        StringBuilder File(string name)
        {
            if (!files.TryGetValue(name, out var text))
            {
                files[name] = text = new StringBuilder("# Written by the Darklands Workshop's text editor (docs/LOCALISATION.md).\n\n");
            }
            return text;
        }
        foreach (var entry in Entries.Where(e => e.Translation is not null))
        {
            var text = File(entry.Template.File);
            foreach (var comment in entry.Template.Comments)
            {
                text.Append("# ").Append(comment).Append('\n');
            }
            text.Append(FtlSource.Write(entry.Id, entry.Translation!)).Append("\n\n");
        }
        foreach (var (file, message) in _others)
        {
            File(file).Append(FtlSource.Write(message.Id, message.Value)).Append("\n\n");
        }
        // a file the pack had and no longer fills keeps its place (empty but for the heading)
        if (Directory.Exists(TextFolder))
        {
            foreach (var existing in Directory.EnumerateFiles(TextFolder, "*.ftl", SearchOption.AllDirectories))
            {
                File(Path.GetRelativePath(TextFolder, existing).Replace('\\', '/'));
            }
        }
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(TextFolder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }
        if (_layouts is not null)
        {
            SlotLayouts.Save(Folder, _layouts);
        }
        Dirty = false;
    }

    /// <summary>Runs the game's pack check on the saved pack; each text gets its findings; the findings come back.</summary>
    public IReadOnlyList<LanguagePackCheck.Finding> Check()
    {
        var pack = LanguagePack.Load(Folder, out var why) ?? throw new InvalidDataException(why);
        var findings = LanguagePackCheck.Run(pack, new GameAssets(Game), [.. Entries.Select(e => e.Template)], Boxes);
        var byId = findings.Where(f => f.Id.Length > 0).GroupBy(f => f.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<LanguagePackCheck.Finding>)[.. g]);
        foreach (var entry in Entries)
        {
            entry.Problems = byId.GetValueOrDefault(entry.Id) ?? [];
        }
        return findings;
    }
}
