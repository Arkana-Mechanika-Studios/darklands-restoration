using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// A small grey line in the window's bottom-right corner (the program's version on the start screen), drawn over the window at
/// its own resolution like the console, never into the game's picture.
/// </summary>
internal sealed class CornerText(Func<string?> text) : IDisposable
{
    private const int FontSize = 9;   // pixels at the 720x400 window
    private static readonly (byte, byte, byte) Colour = (140, 140, 140);

    private ConsoleFont? _font;

    public void Draw(IntPtr renderer, int width, int height)
    {
        if (text() is not { Length: > 0 } line)
        {
            return;
        }
        var pixels = Math.Max(7, FontSize * height / DisplayLayout.BaseHeight);
        var font = _font;
        if (font is null || font.PixelHeight != pixels)
        {
            font?.Dispose();
            _font = font = ConsoleFont.Load(renderer, null, pixels, _ => { });
        }
        var margin = Math.Max(3, pixels / 2);
        font.Draw(renderer, line, width - margin - font.Measure(line), height - margin - font.LineHeight, Colour);
    }

    public void Dispose() => _font?.Dispose();
}
