using System.Globalization;
using Darklands.Platform;

namespace Darklands.DevTools;

/// <summary>Where the host is when a frame is inspected: the input step it has reached (the line of a scripted session).</summary>
/// <param name="Step">The number of input steps taken so far (−1: the host does not count them).</param>
/// <param name="Screen">The screen's own lines for the point (the card, its block, the option line under it; none: null).</param>
public readonly record struct InspectedMoment(int Step, IReadOnlyList<string>? Screen = null);

/// <summary>
/// The developer's inspector: for one pixel of a frame, the lines that say what drew it (the asset and its pixel, the palette
/// index) and where the game is (the input step). It only reads the frame; the game's pixels, owners and texels are never
/// written, so a run with the inspector attached draws and plays the same.
/// </summary>
// Not in the original: a development aid (local post-1.0 design note, the dev-only inspector, step I-1).
public static class Inspector
{
    /// <summary>The lines for pixel (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="frame"/>.</summary>
    public static IReadOnlyList<string> Describe(Screen frame, int x, int y, InspectedMoment moment)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (x is < 0 or >= Screen.Width || y is < 0 or >= Screen.Height)
        {
            return [string.Create(CultureInfo.InvariantCulture, $"pixel {x},{y}: outside the {Screen.Width}x{Screen.Height} frame")];
        }
        var at = y * Screen.Width + x;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"pixel {x},{y}: palette index {frame.Pixels[at]}"),
            Asset(frame.Owners[at], frame.Texels[at]),
        };
        if (frame.Owners[at] == 0 && ScreenTextPlane.Of(frame.Texels[at]) is { } text)
        {
            lines.Add(text.Key is null ? "text (no key)" : "text " + text.Key + (text.Arguments is null ? "" : " (" + text.Arguments + ")"));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  \"{text.Text}\", drawn {text.WidestDrawn} px wide at most"));
        }
        if (moment.Screen is { } screen)
        {
            lines.AddRange(screen);
        }
        if (moment.Step >= 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"step {moment.Step}"));
        }
        return lines;
    }

    private static string Asset(int owner, int texel)
    {
        if (owner == 0 || ScreenOwners.NameOf(owner) is not { } name)
        {
            return "asset: none recorded";
        }
        var line = "asset " + name;
        if (texel != ScreenTexel.Unknown)
        {
            var (column, row, flipX, flipY) = ScreenTexel.Unpack(texel);
            line += string.Create(CultureInfo.InvariantCulture, $", its pixel {column},{row}");
            if (flipX || flipY)
            {
                line += flipX && flipY ? " (mirrored both ways)" : flipX ? " (mirrored left to right)" : " (mirrored top to bottom)";
            }
        }
        var (width, height) = ScreenOwners.SizeOf(owner);
        if (width > 0)
        {
            line += string.Create(CultureInfo.InvariantCulture, $", size {width}x{height}");
        }
        return line;
    }
}
