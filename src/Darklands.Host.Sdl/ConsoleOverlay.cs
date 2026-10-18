using Darklands.DevConsole;
using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// Draws the developer console over the window's top part, at the window's own resolution (never into the game's
/// picture): the scrollback, then the input line with its cursor. The font is rasterised again when the window's size
/// changes the text height.
/// </summary>
internal sealed class ConsoleOverlay(GameConsole console, string? fontFile, int fontSize, Action<string> report) : IDisposable
{
    private static readonly (byte, byte, byte) OutputColour = (215, 215, 215);
    private static readonly (byte, byte, byte) CommandColour = (130, 200, 255);
    private static readonly (byte, byte, byte) ErrorColour = (255, 130, 110);
    private static readonly (byte, byte, byte) HintColour = (150, 150, 150);

    private ConsoleFont? _font;
    private readonly long _start = Environment.TickCount64;

    public GameConsole Console => console;

    public void Draw(IntPtr renderer, int width, int height)
    {
        var pixels = Math.Max(8, fontSize * height / DisplayLayout.BaseHeight);
        var font = _font;
        if (font is null || font.PixelHeight != pixels)
        {
            font?.Dispose();
            _font = font = ConsoleFont.Load(renderer, fontFile, pixels, report);
        }
        var margin = Math.Max(4, font.CharacterWidth / 2);
        var panelHeight = height * 3 / 5;
        SdlNative.SDL_SetRenderDrawBlendMode(renderer, SdlNative.BlendModeBlend);
        SdlNative.SDL_SetRenderDrawColor(renderer, 12, 12, 22, 225);
        var panel = new SdlNative.SdlRect { X = 0, Y = 0, Width = width, Height = panelHeight };
        SdlNative.SDL_RenderFillRect(renderer, ref panel);
        SdlNative.SDL_SetRenderDrawColor(renderer, 90, 110, 160, 255);
        var edge = new SdlNative.SdlRect { X = 0, Y = panelHeight, Width = width, Height = Math.Max(1, pixels / 8) };
        SdlNative.SDL_RenderFillRect(renderer, ref edge);

        // the input line at the bottom of the panel, the cursor blinking
        var inputY = panelHeight - margin - font.LineHeight;
        var prompt = "> ";
        var x = font.Draw(renderer, prompt, margin, inputY, CommandColour);
        font.Draw(renderer, console.Input, x, inputY, OutputColour);
        if ((Environment.TickCount64 - _start) / 500 % 2 == 0)
        {
            SdlNative.SDL_SetRenderDrawColor(renderer, 230, 230, 230, 255);
            var cursor = new SdlNative.SdlRect
            {
                X = x + font.Measure(console.Input.AsSpan(0, console.Cursor)),
                Y = inputY,
                Width = Math.Max(1, pixels / 10),
                Height = font.LineHeight,
            };
            SdlNative.SDL_RenderFillRect(renderer, ref cursor);
        }

        // the scrollback above it, newest at the bottom, long lines wrapped
        var columns = Math.Max(10, (width - 2 * margin) / Math.Max(1, font.CharacterWidth));
        var rows = Wrapped(columns);
        var y = inputY - font.LineHeight - margin / 2;
        var skip = console.ScrollOffset;
        for (var i = rows.Count - 1 - skip; i >= 0 && y > -font.LineHeight; i--, y -= font.LineHeight)
        {
            var (text, kind) = rows[i];
            font.Draw(renderer, text, margin, y, kind switch
            {
                ConsoleLineKind.Command => CommandColour,
                ConsoleLineKind.Error => ErrorColour,
                _ => OutputColour,
            });
        }
        var hint = skip > 0 ? $"-- scrolled back {skip} lines (PgDn) --" : "help: commands   F11/Esc: close   PgUp/PgDn: scroll";
        font.Draw(renderer, hint, width - margin - font.Measure(hint), inputY, HintColour);
    }

    private List<(string Text, ConsoleLineKind Kind)> Wrapped(int columns)
    {
        var rows = new List<(string, ConsoleLineKind)>();
        foreach (var line in console.Lines)
        {
            if (line.Text.Length == 0)
            {
                rows.Add(("", line.Kind));
            }
            for (var start = 0; start < line.Text.Length; start += columns)
            {
                rows.Add((line.Text.Substring(start, Math.Min(columns, line.Text.Length - start)), line.Kind));
            }
        }
        return rows;
    }

    public void Dispose() => _font?.Dispose();
}
