using System.Globalization;
using Darklands.Content;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Presentation.Graphics;

/// <summary>
/// A translation pack's own decorated capitals (docs/LOCALISATION.md, "Decorated capitals"): one picture a letter in the pack's
/// <c>capitals</c> folder, named by the letter (<c>Φ.png</c>) or its code point (<c>U+03A6.png</c>, <c>03A6.png</c>), the size of
/// the game's A–Z pictures (20×20) and in their colours (the colours of ILLMCAPS.PIC the game's capitals use; transparent pixels:
/// none). A card text starting with such a letter takes the pack's picture first, then the game's way (its picture of the Latin
/// look-alike, then the letter doubled in a box). Without a pack, or a pack with none, nothing changes.
/// </summary>
// Not in the original: the 1.1 translations' capitals.
public sealed class PackCapitals
{
    /// <summary>The pack's folder of capitals.</summary>
    public const string Folder = "capitals";

    /// <summary>A capital's size (the game's pictures').</summary>
    public const int Size = 20;

    /// <summary>
    /// How the program reads a PNG (width, height, ARGB a pixel): set by the hosts (the PNG reader is theirs); null: a pack's
    /// capitals cannot be read and the game's way is used.
    /// </summary>
    public static Func<string, (int Width, int Height, uint[] Argb)>? ReadImage { get; set; }

    private readonly Dictionary<int, IndexedImage> _letters;

    private PackCapitals(Dictionary<int, IndexedImage> letters, IReadOnlyList<string> problems)
    {
        _letters = letters;
        Problems = problems;
    }

    /// <summary>None (no pack, or a pack without capitals).</summary>
    public static PackCapitals None { get; } = new([], []);

    /// <summary>The letters the pack draws, by code point.</summary>
    public IReadOnlyCollection<int> Letters => _letters.Keys;

    /// <summary>What could not be used (a picture of another size, a colour not the capitals', a name that is no letter).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The pack's picture of <paramref name="code"/>, or null.</summary>
    public IndexedImage? Get(int code) => _letters.GetValueOrDefault(code);

    /// <summary>The capitals of the pack in <paramref name="pack"/> (none: <see cref="None"/>); their colours from the game's.</summary>
    public static PackCapitals Load(string pack, GameAssets assets)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(assets);
        var folder = Path.Combine(pack, Folder);
        if (!Directory.Exists(folder) || ReadImage is not { } read)
        {
            return None;
        }
        var letters = new Dictionary<int, IndexedImage>();
        var problems = new List<string>();
        var colours = Colours(assets);
        foreach (var file in Directory.EnumerateFiles(folder, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (CodeOf(name) is not { } code)
            {
                problems.Add($"capitals/{name}: its name is not one letter (Φ.png) or a code point (U+03A6.png)");
                continue;
            }
            try
            {
                var (width, height, argb) = read(file);
                var (image, why) = Convert(width, height, argb, colours, code);
                if (image is null)
                {
                    problems.Add($"capitals/{name}: {why}");
                }
                else
                {
                    letters[code] = image;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                problems.Add($"capitals/{name}: {e.Message}");
            }
        }
        return new PackCapitals(letters, problems);
    }

    /// <summary>The letter a capital's file names: the letter itself (<c>Φ.png</c>) or its code point (<c>U+03A6.png</c>, <c>03A6.png</c>); null: none.</summary>
    public static int? CodeOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName).Normalize(System.Text.NormalizationForm.FormC);
        if (stem.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[2..];
        }
        else if (stem.Length is 1 or 2 && char.ConvertToUtf32(stem, 0) is var single && char.ConvertFromUtf32(single).Length == stem.Length)
        {
            return single;
        }
        return stem.Length is >= 4 and <= 6 && int.TryParse(stem, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) && hex is > 0x20 and <= 0x10FFFF
            ? hex
            : null;
    }

    /// <summary>The file name a capital of <paramref name="code"/> gets (its code point: every file system can hold it).</summary>
    public static string FileNameOf(int code) => $"U+{code:X4}.png";

    /// <summary>
    /// The colours a capital may use (the game's capitals' own: each colour their pictures use, by its index, the black the card's 5),
    /// as RGB (0xRRGGBB).
    /// </summary>
    public static IReadOnlyDictionary<int, byte> Colours(GameAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var colours = new Dictionary<int, byte>();
        if (!assets.PictureExists("Illmcaps"))
        {
            return colours;
        }
        var palette = assets.Picture("Illmcaps").Palette;
        var used = new SortedSet<byte>();
        for (var letter = (byte)'A'; letter <= (byte)'Z'; letter++)
        {
            if (DropCaps.Get(assets, letter) is { } picture)
            {
                foreach (var p in picture.Pixels)
                {
                    if (p != 0)
                    {
                        used.Add(p);
                    }
                }
            }
        }
        foreach (var index in used)
        {
            // a card picture's colour 5 is black (as the card screen shows the capitals)
            var c = index == 5 ? new Rgb24(0, 0, 0) : palette[index];
            colours.TryAdd(c.R << 16 | c.G << 8 | c.B, index);
        }
        return colours;
    }

    /// <summary>A PNG's pixels as a capital (null and why when it is not the size or uses another colour).</summary>
    public static (IndexedImage? Image, string? Problem) Convert(int width, int height, uint[] argb, IReadOnlyDictionary<int, byte> colours, int code)
    {
        ArgumentNullException.ThrowIfNull(argb);
        ArgumentNullException.ThrowIfNull(colours);
        if (width != Size || height != Size || argb.Length != Size * Size)
        {
            return (null, $"{width}×{height} pixels; a capital is {Size}×{Size}, as the game's");
        }
        if (colours.Count == 0)
        {
            return (null, "the game's capitals (ILLMCAPS.PIC) are not there to take the colours from");
        }
        var pixels = new byte[Size * Size];
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = argb[i];
            if (pixel >> 24 < 0x80)
            {
                pixels[i] = 0;
                continue;
            }
            if (!colours.TryGetValue((int)(pixel & 0xFFFFFF), out var index))
            {
                return (null, $"the colour #{pixel & 0xFFFFFF:X6} at ({i % Size}, {i / Size}) is not one of the game's capitals' colours");
            }
            pixels[i] = index;
        }
        return (new IndexedImage(Size, Size, pixels, $"dropcap:U+{code:X4}"), null);
    }

    /// <summary>The game's capital of <paramref name="letter"/> (A–Z) as ARGB in its colours (a template to start from), or null.</summary>
    public static uint[]? Template(GameAssets assets, char letter)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (DropCaps.Get(assets, (byte)letter) is not { } picture)
        {
            return null;
        }
        var byIndex = Colours(assets).ToDictionary(c => c.Value, c => c.Key);
        return [.. picture.Pixels.ToArray().Select(p => p == 0 ? 0u : 0xFF000000u | (uint)byIndex.GetValueOrDefault(p))];
    }

    /// <summary>A pack capital's picture as ARGB (the Workshop's list), or null.</summary>
    public uint[]? Argb(int code, GameAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (Get(code) is not { } picture)
        {
            return null;
        }
        var byIndex = Colours(assets).ToDictionary(c => c.Value, c => c.Key);
        return [.. picture.Pixels.ToArray().Select(p => p == 0 ? 0u : 0xFF000000u | (uint)byIndex.GetValueOrDefault(p))];
    }
}
