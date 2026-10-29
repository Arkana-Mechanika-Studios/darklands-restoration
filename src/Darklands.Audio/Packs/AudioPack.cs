using System.Collections.Concurrent;
using System.Text.Json;

namespace Darklands.Audio.Packs;

/// <summary>One replacement of a pack: the file that plays instead of a cue, and how.</summary>
public sealed record AudioReplacement(AudioCue Cue, string Path, bool Loop, float Volume);

/// <summary>
/// A folder of replacement music and battle effects (docs/PACKS.md): <c>.ogg</c> or <c>.wav</c> files named after the
/// cues of <see cref="AudioCatalogue"/>, anywhere in the folder (<c>music/inn-1.ogg</c>, <c>battle-death.wav</c>,
/// <c>music-19.ogg</c>), and an optional <c>audio.json</c> that names other files and sets a cue's loop and volume.
/// </summary>
/// <remarks>
/// <para><c>audio.json</c>: <c>{ "volume": 0.8, "cues": { "inn-1": "tavern.ogg", "battle-death": { "file": "thud.wav",
/// "loop": false, "volume": 0.5 } } }</c> (comments and trailing commas allowed, paths relative to the pack). Its entries win
/// over the file names; "volume" at the top scales every cue.</para>
/// <para>A file is decoded on a worker thread the first time it is needed (or by <see cref="PreloadAsync"/>); one that cannot
/// be read is reported in <see cref="Problems"/> and the game's own sound plays instead.</para>
/// New code (replacement sound, not part of the game).
/// </remarks>
public sealed class AudioPack
{
    /// <summary>The manifest's file name inside the pack folder (a picture pack's pack.json can sit beside it).</summary>
    public const string ManifestName = "audio.json";

    private static readonly JsonDocumentOptions ManifestOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly Dictionary<(AudioKind, int), AudioReplacement> _replacements;
    private readonly ConcurrentDictionary<(string Path, int Rate), Lazy<Task<AudioClip?>>> _clips = new();
    private readonly List<string> _problems;
    private readonly Lock _gate = new();

    private AudioPack(string? folder, Dictionary<(AudioKind, int), AudioReplacement> replacements, List<string> problems)
    {
        Folder = folder;
        _replacements = replacements;
        _problems = problems;
    }

    /// <summary>No replacements.</summary>
    public static AudioPack Empty { get; } = new(null, [], []);

    public string? Folder { get; }

    public int Count => _replacements.Count;

    public bool IsEmpty => _replacements.Count == 0;

    /// <summary>The replacements, in catalogue order (music then effects, by number).</summary>
    public IEnumerable<AudioReplacement> Replacements => _replacements.Values.OrderBy(r => r.Cue.Kind).ThenBy(r => r.Cue.Number);

    /// <summary>What could not be read (a missing folder, an unknown name, a bad manifest entry, a broken file), for the console.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_gate)
            {
                return [.. _problems];
            }
        }
    }

    /// <summary>The replacement of a number, null when the game's own sound plays.</summary>
    public AudioReplacement? Find(AudioKind kind, int number) =>
        kind == AudioKind.Music && AudioCatalogue.IsControl(number) ? null : _replacements.GetValueOrDefault((kind, number));

    /// <summary>Indexes the folder's sound files and its manifest (the files are decoded later).</summary>
    public static AudioPack Load(string folder)
    {
        var problems = new List<string>();
        var replacements = new Dictionary<(AudioKind, int), AudioReplacement>();
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            problems.Add($"Audio pack: folder {full} not found; the game's own sound plays");
            return new AudioPack(full, replacements, problems);
        }
        // sorted, so that two files of one cue resolve the same way on every disk (the first wins; .ogg before .wav)
        foreach (var path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".ogg" or ".wav"))
            {
                continue;
            }
            if (AudioCatalogue.Find(Path.GetFileNameWithoutExtension(path)) is not { } cue)
            {
                problems.Add($"Audio pack: {Path.GetRelativePath(full, path)}: no cue of that name (docs/PACKS.md); ignored");
                continue;
            }
            if (!replacements.TryAdd((cue.Kind, cue.Number), new AudioReplacement(cue, path, cue.Loops, 1f)))
            {
                problems.Add($"Audio pack: {Path.GetRelativePath(full, path)}: {cue.Name} has another file already; ignored");
            }
        }
        var manifest = Path.Combine(full, ManifestName);
        if (File.Exists(manifest))
        {
            ReadManifest(full, manifest, replacements, problems);
        }
        return new AudioPack(full, replacements, problems);
    }

    private static void ReadManifest(string folder, string manifest, Dictionary<(AudioKind, int), AudioReplacement> replacements,
        List<string> problems)
    {
        void Problem(string text) => problems.Add($"Audio pack: {manifest}: {text}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest), ManifestOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Problem("the top level must be an object; ignored");
                return;
            }
            var volume = 1f;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "volume" when property.Value.TryGetSingle(out var v) && v is >= 0 and <= 4:
                        volume = v;
                        break;
                    case "volume":
                        Problem("\"volume\" must be a number from 0 to 4; ignored");
                        break;
                    case "cues" when property.Value.ValueKind == JsonValueKind.Object:
                        foreach (var entry in property.Value.EnumerateObject())
                        {
                            ReadCue(folder, entry, replacements, Problem);
                        }
                        break;
                    case "cues":
                        Problem("\"cues\" must be an object of cue name: file or { file, loop, volume }; ignored");
                        break;
                    default:
                        Problem($"unknown key \"{property.Name}\" ignored");
                        break;
                }
            }
            if (volume != 1f)
            {
                foreach (var key in replacements.Keys.ToList())
                {
                    replacements[key] = replacements[key] with { Volume = replacements[key].Volume * volume };
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"Audio pack: {manifest}: {e.Message}; manifest ignored");
        }
    }

    private static void ReadCue(string folder, JsonProperty entry, Dictionary<(AudioKind, int), AudioReplacement> replacements,
        Action<string> problem)
    {
        if (AudioCatalogue.Find(entry.Name) is not { } cue)
        {
            problem($"\"{entry.Name}\": no cue of that name (docs/PACKS.md); ignored");
            return;
        }
        replacements.TryGetValue((cue.Kind, cue.Number), out var known);
        string? file = null;
        var loop = known?.Loop ?? cue.Loops;
        var volume = known?.Volume ?? 1f;
        if (entry.Value.ValueKind == JsonValueKind.String)
        {
            file = entry.Value.GetString();
        }
        else if (entry.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in entry.Value.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "file" when p.Value.ValueKind == JsonValueKind.String:
                        file = p.Value.GetString();
                        break;
                    case "loop" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        loop = p.Value.GetBoolean();
                        break;
                    case "volume" when p.Value.TryGetSingle(out var v) && v is >= 0 and <= 4:
                        volume = v;
                        break;
                    default:
                        problem($"\"{entry.Name}\": \"{p.Name}\" ignored (file: a path, loop: true/false, volume: 0 to 4)");
                        break;
                }
            }
        }
        else
        {
            problem($"\"{entry.Name}\" must name a file or be {{ file, loop, volume }}; ignored");
            return;
        }
        string path;
        if (file is not null)
        {
            path = Path.GetFullPath(Path.Combine(folder, file));
            if (!File.Exists(path))
            {
                problem($"\"{entry.Name}\": {path} not found; ignored");
                return;
            }
        }
        else if (known is not null)
        {
            path = known.Path;
        }
        else
        {
            problem($"\"{entry.Name}\": no file named and none found by the name; ignored");
            return;
        }
        replacements[(cue.Kind, cue.Number)] = new AudioReplacement(cue, path, loop, volume);
    }

    /// <summary>The decoded file of a replacement at <paramref name="sampleRate"/>; null when it cannot be read (reported once).</summary>
    public Task<AudioClip?> ClipAsync(AudioReplacement replacement, int sampleRate) =>
        _clips.GetOrAdd((replacement.Path, sampleRate), key => new Lazy<Task<AudioClip?>>(() => Task.Run(() => Decode(key.Path, key.Rate)))).Value;

    /// <summary>Decodes every file of the pack on worker threads, so that a cue's first play does not wait for its file.</summary>
    public Task PreloadAsync(int sampleRate) => Task.WhenAll(_replacements.Values.Select(r => ClipAsync(r, sampleRate)));

    private AudioClip? Decode(string path, int sampleRate)
    {
        try
        {
            return AudioClip.Load(path, sampleRate);
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                _problems.Add($"Audio pack: {path}: {e.Message}; the game's own sound plays");
            }
            return null;
        }
    }
}
