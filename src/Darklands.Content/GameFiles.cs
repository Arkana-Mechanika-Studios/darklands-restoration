namespace Darklands.Content;

/// <summary>
/// The player's Darklands folder. Names are looked up without regard to case, as DOS did,
/// and may contain a sub-folder ("PICS\STARTSCR.PIC").
/// </summary>
public sealed class GameFiles
{
    private readonly string _root;

    public GameFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Darklands folder not found: {root}");
        }
        _root = root;
    }

    public string Root => _root;

    /// <summary>True when the folder looks like a Darklands installation.</summary>
    public static bool IsGameFolder(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path).Any(f => string.Equals(Path.GetFileName(f), "DARKLAND.EXE", StringComparison.OrdinalIgnoreCase));

    private readonly Dictionary<string, byte[]> _replaced = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists(string name) => _replaced.ContainsKey(name) || Find(name) is not null;

    public byte[] Read(string name) =>
        _replaced.TryGetValue(name, out var bytes)
            ? bytes
            : File.ReadAllBytes(Find(name) ?? throw new FileNotFoundException($"Game file not found: {name}"));

    /// <summary>
    /// Makes every later <see cref="Read"/> / <see cref="Exists"/> of <paramref name="name"/> see <paramref name="contents"/> instead of the
    /// file (the folder itself is not touched).
    /// </summary>
    public void Replace(string name, byte[] contents) => _replaced[name] = contents;

    /// <summary>
    /// The player's override of CONFIG.DRK's four words (card number, port, IRQ, DMA), applied as if the file held them: a null
    /// field keeps the file's own word (without the file: the usual Sound Blaster 220h, IRQ 7, DMA 1, card 0).
    /// Whatever the game reads CONFIG.DRK for (the driver's letter, the resources it starts the driver with) then follows.
    /// </summary>
    // orig: R000+0x0114 (0E76:0114) draft - fread(DS:A86E, 8, 1, f): card, port, IRQ, DMA as four little-endian words.
    public void OverrideConfig(int? card, int? port, int? irq, int? dma)
    {
        byte[] bytes;
        if (Exists("CONFIG.DRK"))
        {
            bytes = [.. Read("CONFIG.DRK")];
            if (bytes.Length < 8)
            {
                Array.Resize(ref bytes, 8);
            }
        }
        else
        {
            bytes = [0, 0, 0x20, 0x02, 7, 0, 1, 0];
        }
        void Set(int index, int? value)
        {
            if (value is { } word)
            {
                bytes[index * 2] = (byte)word;
                bytes[index * 2 + 1] = (byte)(word >> 8);
            }
        }
        Set(0, card);
        Set(1, port);
        Set(2, irq);
        Set(3, dma);
        Replace("CONFIG.DRK", bytes);
    }

    /// <summary>The names of the files of a sub-folder matching a DOS pattern ("F??STAT.PIC"), in name order.</summary>
    public IReadOnlyList<string> List(string folder, string pattern)
    {
        var directory = string.IsNullOrEmpty(folder) ? _root : FindFolder(folder);
        return directory is null
            ? []
            : [.. Directory.EnumerateFiles(directory, pattern).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];
    }

    private string? FindFolder(string name) =>
        Directory.EnumerateDirectories(_root).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The full path of a game file, or null.</summary>
    public string? Find(string name)
    {
        var current = _root;
        foreach (var part in name.Split('\\', '/'))
        {
            var exact = Path.Combine(current, part);
            if (File.Exists(exact) || Directory.Exists(exact))
            {
                current = exact;
                continue;
            }
            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }
            current = match;
        }
        return File.Exists(current) ? current : null;
    }
}
