using System.Text.Json;

namespace Darklands.Rendering;

/// <summary>
/// A folder of replacement pictures, one PNG per asset, found by the asset's name
/// (<c>IndexedImage.AssetName</c>: <c>PICS\STARTSCR.PIC</c>, <c>COMMONSP.IMG#12</c>, <c>F01C.CAT/F01WKSW.IMC#3.5</c> ...).
/// </summary>
/// <remarks>
/// <para>A file is found by the name itself plus <c>.png</c>, with <c>\</c> and <c>/</c> as folders and <c>:</c> written
/// as <c>_</c>, in any letter case: <c>PICS/STARTSCR.PIC.png</c>, <c>COMMONSP.IMG#12.png</c>, <c>dropcap_D.png</c>.</para>
/// <para>An optional <c>pack.json</c> names other files: <c>{ "assets": { "PICS\\STARTSCR.PIC": "title.png" } }</c>
/// (comments and trailing commas allowed, paths relative to the pack); its entries win over the file names.</para>
/// <para>A picture replaces the whole asset: it is stretched over the asset's rectangle, so any multiple of the
/// asset's size (or none) works. Pictures are read when first asked for; a broken one is reported in
/// <see cref="Problems"/> and the asset keeps its original pixels.</para>
/// New code (replacement graphics, not part of the game).
/// </remarks>
public sealed class HdPack
{
    /// <summary>The manifest's file name inside the pack folder.</summary>
    public const string ManifestName = "pack.json";

    private static readonly JsonDocumentOptions ManifestOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<string, RgbaImage?> _loaded = new(StringComparer.Ordinal);
    private readonly List<string> _problems;
    private readonly Lock _gate = new();

    private HdPack(string? folder, Dictionary<string, string> files, List<string> problems)
    {
        Folder = folder;
        _files = files;
        _problems = problems;
    }

    /// <summary>No replacements: the frame is drawn as it is.</summary>
    public static HdPack Empty { get; } = new(null, new Dictionary<string, string>(StringComparer.Ordinal), []);

    /// <summary>The pack folder (null for <see cref="Empty"/>).</summary>
    public string? Folder { get; }

    /// <summary>How many assets have a replacement.</summary>
    public int Count => _files.Count;

    public bool IsEmpty => _files.Count == 0;

    /// <summary>What could not be read (a missing folder, a bad manifest entry, a broken picture), for the console.</summary>
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

    /// <summary>The key an asset name or a file path is found by: <c>/</c> for <c>\</c>, <c>_</c> for <c>:</c>, upper case.</summary>
    public static string Key(string assetName) => assetName.Replace('\\', '/').Replace(':', '_').ToUpperInvariant();

    /// <summary>Indexes the folder's PNG files and its manifest (the pictures are read later).</summary>
    public static HdPack Load(string folder)
    {
        var problems = new List<string>();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            problems.Add($"HD pack: folder {full} not found; no replacements");
            return new HdPack(full, files, problems);
        }
        // sorted, so that two files of one key (a.png and A.PNG on a case-sensitive disk) resolve the same way every run
        foreach (var path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                files.TryAdd(Key(Path.GetRelativePath(full, path)[..^4]), path);
            }
        }
        var manifest = Path.Combine(full, ManifestName);
        if (File.Exists(manifest))
        {
            ReadManifest(full, manifest, files, problems);
        }
        return new HdPack(full, files, problems);
    }

    private static void ReadManifest(string folder, string manifest, Dictionary<string, string> files, List<string> problems)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest), ManifestOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"HD pack: {manifest}: the top level must be an object; ignored");
                return;
            }
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "assets")
                {
                    problems.Add($"HD pack: {manifest}: unknown key \"{property.Name}\" ignored");
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"HD pack: {manifest}: \"assets\" must be an object of asset name: file; ignored");
                    continue;
                }
                foreach (var asset in property.Value.EnumerateObject())
                {
                    if (asset.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(asset.Value.GetString()))
                    {
                        problems.Add($"HD pack: {manifest}: \"{asset.Name}\" must name a file; ignored");
                        continue;
                    }
                    var path = Path.GetFullPath(Path.Combine(folder, asset.Value.GetString()!));
                    if (!File.Exists(path))
                    {
                        problems.Add($"HD pack: {manifest}: \"{asset.Name}\": {path} not found; ignored");
                        continue;
                    }
                    files[Key(asset.Name)] = path;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"HD pack: {manifest}: {e.Message}; manifest ignored");
        }
    }

    /// <summary>Whether the asset has a replacement file (read or not).</summary>
    public bool Has(string assetName) => _files.ContainsKey(Key(assetName));

    /// <summary>The asset's replacement picture, null when it has none or its file cannot be read.</summary>
    public RgbaImage? Find(string assetName)
    {
        var key = Key(assetName);
        return _files.TryGetValue(key, out var path) ? Load(key, path, assetName) : null;
    }

    /// <summary>
    /// Reads every picture of the pack on worker threads, so that a picture's first appearance does not wait for its
    /// file (a large PNG takes a tenth of a second). The pictures then stay in memory, as the ones read on demand do.
    /// </summary>
    public Task PreloadAsync() => Task.Run(() => Parallel.ForEach(_files.OrderBy(f => f.Key, StringComparer.Ordinal), f => Load(f.Key, f.Value, f.Key)));

    /// <summary>The picture of a key, read once (outside the lock, so that a reader on another thread never waits for it).</summary>
    private RgbaImage? Load(string key, string path, string assetName)
    {
        lock (_gate)
        {
            if (_loaded.TryGetValue(key, out var known))
            {
                return known;
            }
        }
        RgbaImage? image;
        string? problem = null;
        try
        {
            image = PngCodec.Read(path);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem = $"HD pack: {path}: {e.Message}; {assetName} keeps its original pixels";
            image = null;
        }
        lock (_gate)
        {
            if (_loaded.TryGetValue(key, out var first))
            {
                return first;
            }
            if (problem is not null)
            {
                _problems.Add(problem);
            }
            _loaded[key] = image;
            return image;
        }
    }
}
