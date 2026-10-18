namespace Darklands.Platform;

/// <summary>
/// The game's 320×200 indexed-colour screen and its 256-colour palette. Presentation draws
/// into it; a host shows it.
/// </summary>
public sealed class Screen
{
    public const int Width = 320;
    public const int Height = 200;

    /// <summary>One palette index per pixel, row by row.</summary>
    public byte[] Pixels { get; } = new byte[Width * Height];

    /// <summary>The 256 colours as 0xAARRGGBB.</summary>
    public uint[] Palette { get; } = new uint[256];
}
