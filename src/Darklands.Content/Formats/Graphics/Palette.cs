namespace Darklands.Content.Formats.Graphics;

/// <summary>
/// Full 256-color VGA palette expressed as RGB values.
/// </summary>
public sealed class Palette256
{
    public const int ColorCount = 256;

    private readonly Rgb24[] _entries;

    public Palette256(IEnumerable<Rgb24> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        _entries = entries.ToArray();
        if (_entries.Length != ColorCount)
        {
            throw new ArgumentException($"A VGA palette must contain exactly {ColorCount} entries.", nameof(entries));
        }
    }

    public static Palette256 Black { get; } = new Palette256(Enumerable.Repeat(Rgb24.Black, ColorCount));

    public static Palette256 DefaultVga { get; } = new Palette256(BuildDefaultVgaEntries());

    public Rgb24 this[int index] => _entries[index];

    public ReadOnlySpan<Rgb24> Entries => _entries;

    private static IEnumerable<Rgb24> BuildDefaultVgaEntries()
    {
        Rgb24[] colors =
        {
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 170),
            new Rgb24(0, 170, 0),
            new Rgb24(0, 170, 170),
            new Rgb24(170, 0, 0),
            new Rgb24(170, 0, 170),
            new Rgb24(170, 85, 0),
            new Rgb24(170, 170, 170),
            new Rgb24(85, 85, 85),
            new Rgb24(85, 85, 255),
            new Rgb24(85, 255, 85),
            new Rgb24(85, 255, 255),
            new Rgb24(255, 85, 85),
            new Rgb24(255, 85, 255),
            new Rgb24(255, 255, 85),
            new Rgb24(255, 255, 255),
            new Rgb24(0, 0, 0),
            new Rgb24(20, 20, 20),
            new Rgb24(32, 32, 32),
            new Rgb24(44, 44, 44),
            new Rgb24(56, 56, 56),
            new Rgb24(68, 68, 68),
            new Rgb24(80, 80, 80),
            new Rgb24(96, 96, 96),
            new Rgb24(112, 112, 112),
            new Rgb24(128, 128, 128),
            new Rgb24(144, 144, 144),
            new Rgb24(160, 160, 160),
            new Rgb24(180, 180, 180),
            new Rgb24(200, 200, 200),
            new Rgb24(224, 224, 224),
            new Rgb24(252, 252, 252),
            new Rgb24(0, 0, 252),
            new Rgb24(64, 0, 252),
            new Rgb24(124, 0, 252),
            new Rgb24(188, 0, 252),
            new Rgb24(252, 0, 252),
            new Rgb24(252, 0, 188),
            new Rgb24(252, 0, 124),
            new Rgb24(252, 0, 64),
            new Rgb24(252, 0, 0),
            new Rgb24(252, 64, 0),
            new Rgb24(252, 124, 0),
            new Rgb24(252, 188, 0),
            new Rgb24(252, 252, 0),
            new Rgb24(188, 252, 0),
            new Rgb24(124, 252, 0),
            new Rgb24(64, 252, 0),
            new Rgb24(0, 252, 0),
            new Rgb24(0, 252, 64),
            new Rgb24(0, 252, 124),
            new Rgb24(0, 252, 188),
            new Rgb24(0, 252, 252),
            new Rgb24(0, 188, 252),
            new Rgb24(0, 124, 252),
            new Rgb24(0, 64, 252),
            new Rgb24(124, 124, 252),
            new Rgb24(156, 124, 252),
            new Rgb24(188, 124, 252),
            new Rgb24(220, 124, 252),
            new Rgb24(252, 124, 252),
            new Rgb24(252, 124, 220),
            new Rgb24(252, 124, 188),
            new Rgb24(252, 124, 156),
            new Rgb24(252, 124, 124),
            new Rgb24(252, 156, 124),
            new Rgb24(252, 188, 124),
            new Rgb24(252, 220, 124),
            new Rgb24(252, 252, 124),
            new Rgb24(220, 252, 124),
            new Rgb24(188, 252, 124),
            new Rgb24(156, 252, 124),
            new Rgb24(124, 252, 124),
            new Rgb24(124, 252, 156),
            new Rgb24(124, 252, 188),
            new Rgb24(124, 252, 220),
            new Rgb24(124, 252, 252),
            new Rgb24(124, 220, 252),
            new Rgb24(124, 188, 252),
            new Rgb24(124, 156, 252),
            new Rgb24(180, 180, 252),
            new Rgb24(196, 180, 252),
            new Rgb24(216, 180, 252),
            new Rgb24(232, 180, 252),
            new Rgb24(252, 180, 252),
            new Rgb24(252, 180, 232),
            new Rgb24(252, 180, 216),
            new Rgb24(252, 180, 196),
            new Rgb24(252, 180, 180),
            new Rgb24(252, 196, 180),
            new Rgb24(252, 216, 180),
            new Rgb24(252, 232, 180),
            new Rgb24(252, 252, 180),
            new Rgb24(232, 252, 180),
            new Rgb24(216, 252, 180),
            new Rgb24(196, 252, 180),
            new Rgb24(180, 252, 180),
            new Rgb24(180, 252, 196),
            new Rgb24(180, 252, 216),
            new Rgb24(180, 252, 232),
            new Rgb24(180, 252, 252),
            new Rgb24(180, 232, 252),
            new Rgb24(180, 216, 252),
            new Rgb24(180, 196, 252),
            new Rgb24(0, 0, 112),
            new Rgb24(28, 0, 112),
            new Rgb24(56, 0, 112),
            new Rgb24(84, 0, 112),
            new Rgb24(112, 0, 112),
            new Rgb24(112, 0, 84),
            new Rgb24(112, 0, 56),
            new Rgb24(112, 0, 28),
            new Rgb24(112, 0, 0),
            new Rgb24(112, 28, 0),
            new Rgb24(112, 56, 0),
            new Rgb24(112, 84, 0),
            new Rgb24(112, 112, 0),
            new Rgb24(84, 112, 0),
            new Rgb24(56, 112, 0),
            new Rgb24(28, 112, 0),
            new Rgb24(0, 112, 0),
            new Rgb24(0, 112, 28),
            new Rgb24(0, 112, 56),
            new Rgb24(0, 112, 84),
            new Rgb24(0, 112, 112),
            new Rgb24(0, 84, 112),
            new Rgb24(0, 56, 112),
            new Rgb24(0, 28, 112),
            new Rgb24(56, 56, 112),
            new Rgb24(68, 56, 112),
            new Rgb24(84, 56, 112),
            new Rgb24(96, 56, 112),
            new Rgb24(112, 56, 112),
            new Rgb24(112, 56, 96),
            new Rgb24(112, 56, 84),
            new Rgb24(112, 56, 68),
            new Rgb24(112, 56, 56),
            new Rgb24(112, 68, 56),
            new Rgb24(112, 84, 56),
            new Rgb24(112, 96, 56),
            new Rgb24(112, 112, 56),
            new Rgb24(96, 112, 56),
            new Rgb24(84, 112, 56),
            new Rgb24(68, 112, 56),
            new Rgb24(56, 112, 56),
            new Rgb24(56, 112, 68),
            new Rgb24(56, 112, 84),
            new Rgb24(56, 112, 96),
            new Rgb24(56, 112, 112),
            new Rgb24(56, 96, 112),
            new Rgb24(56, 84, 112),
            new Rgb24(56, 68, 112),
            new Rgb24(80, 80, 112),
            new Rgb24(88, 80, 112),
            new Rgb24(96, 80, 112),
            new Rgb24(104, 80, 112),
            new Rgb24(112, 80, 112),
            new Rgb24(112, 80, 104),
            new Rgb24(112, 80, 96),
            new Rgb24(112, 80, 88),
            new Rgb24(112, 80, 80),
            new Rgb24(112, 88, 80),
            new Rgb24(112, 96, 80),
            new Rgb24(112, 104, 80),
            new Rgb24(112, 112, 80),
            new Rgb24(104, 112, 80),
            new Rgb24(96, 112, 80),
            new Rgb24(88, 112, 80),
            new Rgb24(80, 112, 80),
            new Rgb24(80, 112, 88),
            new Rgb24(80, 112, 96),
            new Rgb24(80, 112, 104),
            new Rgb24(80, 112, 112),
            new Rgb24(80, 104, 112),
            new Rgb24(80, 96, 112),
            new Rgb24(80, 88, 112),
            new Rgb24(0, 0, 64),
            new Rgb24(16, 0, 64),
            new Rgb24(32, 0, 64),
            new Rgb24(48, 0, 64),
            new Rgb24(64, 0, 64),
            new Rgb24(64, 0, 48),
            new Rgb24(64, 0, 32),
            new Rgb24(64, 0, 16),
            new Rgb24(64, 0, 0),
            new Rgb24(64, 16, 0),
            new Rgb24(64, 32, 0),
            new Rgb24(64, 48, 0),
            new Rgb24(64, 64, 0),
            new Rgb24(48, 64, 0),
            new Rgb24(32, 64, 0),
            new Rgb24(16, 64, 0),
            new Rgb24(0, 64, 0),
            new Rgb24(0, 64, 16),
            new Rgb24(0, 64, 32),
            new Rgb24(0, 64, 48),
            new Rgb24(0, 64, 64),
            new Rgb24(0, 48, 64),
            new Rgb24(0, 32, 64),
            new Rgb24(0, 16, 64),
            new Rgb24(32, 32, 64),
            new Rgb24(40, 32, 64),
            new Rgb24(48, 32, 64),
            new Rgb24(56, 32, 64),
            new Rgb24(64, 32, 64),
            new Rgb24(64, 32, 56),
            new Rgb24(64, 32, 48),
            new Rgb24(64, 32, 40),
            new Rgb24(64, 32, 32),
            new Rgb24(64, 40, 32),
            new Rgb24(64, 48, 32),
            new Rgb24(64, 56, 32),
            new Rgb24(64, 64, 32),
            new Rgb24(56, 64, 32),
            new Rgb24(48, 64, 32),
            new Rgb24(40, 64, 32),
            new Rgb24(32, 64, 32),
            new Rgb24(32, 64, 40),
            new Rgb24(32, 64, 48),
            new Rgb24(32, 64, 56),
            new Rgb24(32, 64, 64),
            new Rgb24(32, 56, 64),
            new Rgb24(32, 48, 64),
            new Rgb24(32, 40, 64),
            new Rgb24(44, 44, 64),
            new Rgb24(48, 44, 64),
            new Rgb24(52, 44, 64),
            new Rgb24(60, 44, 64),
            new Rgb24(64, 44, 64),
            new Rgb24(64, 44, 60),
            new Rgb24(64, 44, 52),
            new Rgb24(64, 44, 48),
            new Rgb24(64, 44, 44),
            new Rgb24(64, 48, 44),
            new Rgb24(64, 52, 44),
            new Rgb24(64, 60, 44),
            new Rgb24(64, 64, 44),
            new Rgb24(60, 64, 44),
            new Rgb24(52, 64, 44),
            new Rgb24(48, 64, 44),
            new Rgb24(44, 64, 44),
            new Rgb24(44, 64, 48),
            new Rgb24(44, 64, 52),
            new Rgb24(44, 64, 60),
            new Rgb24(44, 64, 64),
            new Rgb24(44, 60, 64),
            new Rgb24(44, 52, 64),
            new Rgb24(44, 48, 64),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0),
            new Rgb24(0, 0, 0)
        };

        foreach (var color in colors)
        {
            yield return color;
        }
    }
}

/// <summary>
/// 8-bit RGB color.
/// </summary>
public readonly record struct Rgb24(byte R, byte G, byte B)
{
    public static Rgb24 Black { get; } = new Rgb24(0, 0, 0);
}

/// <summary>
/// Raw 768-byte palette payload before interpretation.
/// </summary>
public sealed class RawPalette
{
    public const int BytesPerColor = 3;
    public const int ByteLength = Palette256.ColorCount * BytesPerColor;

    public static RawPalette Empty { get; } = new RawPalette(new byte[ByteLength]);

    private RawPalette(byte[] colors)
    {
        Colors = colors;
    }

    public byte[] Colors { get; }

    public static RawPalette FromRgbTriplets(byte[] colors)
    {
        if (colors is null)
        {
            throw new ArgumentNullException(nameof(colors));
        }

        if (colors.Length != ByteLength)
        {
            throw new ArgumentException($"A Darklands 256-color palette must contain {ByteLength} bytes.", nameof(colors));
        }

        return new RawPalette((byte[])colors.Clone());
    }
}

