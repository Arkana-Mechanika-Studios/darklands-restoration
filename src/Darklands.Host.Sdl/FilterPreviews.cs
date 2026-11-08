using System.Collections.Concurrent;
using System.Diagnostics;
using Darklands.Content;
using Darklands.Launcher;
using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// The launcher's picture filter previews (<see cref="IFilterPreviews"/>): a piece of a card's picture (the dragon over the burning
/// village, PICS\VILLAGE4.PIC: dithered smoke and fire, fine lines; its own palette holds all its colours), read from the
/// player's own game folder (nothing of the game is shipped or kept elsewhere), drawn through every filter by the CPU reference
/// (<see cref="PictureFilterReference"/>) on a worker thread, at the size the launcher shows it. Without a game, a pattern drawn
/// in code (dithering, a staircase diagonal, small letters). The launcher's frames only look at what is ready.
/// </summary>
// Host feature, new code (docs/SETTINGS.md, the launcher's Picture row).
internal sealed class FilterPreviews : IFilterPreviews
{
    /// <summary>The picture and the piece of it shown (game pixels): the dragon's head and wings over the burning roofs.</summary>
    internal const string PictureName = "VILLAGE4";

    internal const int CropX = 128, CropY = 48, CropWidth = 120, CropHeight = 75;

    private readonly object _gate = new();
    private (string? Folder, int Width, int Height, bool Curved)? _asked;
    private int _generation;
    private ConcurrentDictionary<PictureFilter, FilterPreview> _ready = new();
    private ConcurrentDictionary<PictureFilter, FilterPreview> _previous = new();

    /// <summary>Milliseconds from the last <see cref="Prepare"/> that started work to its last preview made (null while it runs).</summary>
    internal double? ReadyAfterMilliseconds { get; private set; }

    /// <summary>A task that ends when the last <see cref="Prepare"/>'s previews are all made (tests).</summary>
    internal Task Work { get; private set; } = Task.CompletedTask;

    public void Prepare(string? gameFolder, int width, int height, bool curvature)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        lock (_gate)
        {
            var asked = (gameFolder, width, height, curvature);
            if (_asked == asked)
            {
                return;
            }
            _asked = asked;
            var generation = ++_generation;
            // what was made for the last size stays on show until the new size's previews are made
            if (!_ready.IsEmpty)
            {
                _previous = _ready;
            }
            _ready = new ConcurrentDictionary<PictureFilter, FilterPreview>();
            var ready = _ready;
            ReadyAfterMilliseconds = null;
            Work = Task.Run(() => Make(generation, ready, gameFolder, width, height, curvature));
        }
    }

    public FilterPreview? Get(PictureFilter filter) =>
        _ready.TryGetValue(filter, out var preview) || _previous.TryGetValue(filter, out preview) ? preview : null;

    private void Make(int generation, ConcurrentDictionary<PictureFilter, FilterPreview> ready, string? folder, int width, int height, bool curvature)
    {
        var clock = Stopwatch.StartNew();
        var (source, fromGame) = Source(folder);
        foreach (var filter in Enum.GetValues<PictureFilter>())
        {
            if (generation != _generation)
            {
                return;   // asked again since: this round's work is no longer wanted
            }
            var argb = PictureFilterReference.Render(source, CropWidth, CropHeight, filter, width, height,
                curvature ? SdlShaderPictureFilter.CurvatureAmount : 0);
            ready[filter] = new FilterPreview(width, height, argb, fromGame);
        }
        if (generation == _generation)
        {
            ReadyAfterMilliseconds = clock.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>The piece of the card picture from the game folder, or the pattern; and whether it is the game's.</summary>
    internal static (uint[] Argb, bool FromGame) Source(string? folder)
    {
        if (folder is not null && GameFiles.IsGameFolder(folder))
        {
            try
            {
                var picture = new GameAssets(new GameFiles(folder)).Picture(PictureName);
                var image = picture.Image;
                // the colours the picture brings (the game sets them as it shows it; all of this picture's are among them)
                var colours = picture.EmbeddedPalette?.Entries;
                var argb = new uint[CropWidth * CropHeight];
                for (var y = 0; y < CropHeight; y++)
                {
                    for (var x = 0; x < CropWidth; x++)
                    {
                        var index = image.Pixels[(CropY + y) * image.Width + CropX + x];
                        var c = colours is not null && index < colours.Count && colours[index] is { } own ? own : picture.Palette[index];
                        argb[y * CropWidth + x] = 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
                    }
                }
                return (argb, true);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or UnauthorizedAccessException)
            {
                // a picture that cannot be read: the pattern
            }
        }
        return (Pattern(), false);
    }

    /// <summary>
    /// The pattern without a game: a checkerboard of two greens (dithering), a staircase diagonal between brown and blue, grey
    /// steps, and "DARKLANDS" in small letters, white on dark.
    /// </summary>
    internal static uint[] Pattern()
    {
        const uint GreenA = 0xFF2E8B3A, GreenB = 0xFF8BC34A, Brown = 0xFF7A4E2A, Blue = 0xFF3A5FA8, Dark = 0xFF1C1C24, White = 0xFFF0EAD8;
        var argb = new uint[CropWidth * CropHeight];
        for (var y = 0; y < CropHeight; y++)
        {
            for (var x = 0; x < CropWidth; x++)
            {
                argb[y * CropWidth + x] = y < 26 ? Dark
                    : x < 40 ? ((x + y) % 2 == 0 ? GreenA : GreenB)
                    : x < 80 ? (x - 40 + (y - 26) >= 45 ? Blue : Brown)
                    : 0xFF000000u | (uint)(40 + (x - 80) / 5 * 24) * 0x010101u;
            }
        }
        // the letters, 3 x 5 each with a column between, twice their size, from (8, 8)
        string[] glyphs =
        [
            "110101101101110", "010101111101101", "110101110101101", "101110100110101", "100100100100111",
            "010101111101101", "101111111101101", "110101101101110", "011100010001110",
        ];
        for (var g = 0; g < glyphs.Length; g++)
        {
            for (var i = 0; i < 15; i++)
            {
                if (glyphs[g][i] != '1')
                {
                    continue;
                }
                var (gx, gy) = (8 + g * 12 + i % 3 * 2, 8 + i / 3 * 2);
                for (var dy = 0; dy < 2; dy++)
                {
                    for (var dx = 0; dx < 2; dx++)
                    {
                        argb[(gy + dy) * CropWidth + gx + dx] = White;
                    }
                }
            }
        }
        return argb;
    }
}
