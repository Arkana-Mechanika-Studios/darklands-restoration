using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher;

// The launcher screen: the stateless drawing primitives (painting, parchment, shadows, ornaments, icons, text wrapping). The state and the input are in LauncherScreen.cs.
internal sealed partial class LauncherScreen
{
    /// <summary>The painting (or a dusk gradient without it), enlarged around its focus to cover the window, darkened at the edges.</summary>
    private static void DrawPainting(Canvas canvas, View v)
    {
        var painting = Assets.Art("background");
        if (painting is null)
        {
            canvas.Paint(new Rect(0, 0, canvas.Width, canvas.Height), (x, y) =>
                Colours.Mix(0x6E6258, 0x1E1A15, (int)(255 * Math.Clamp((double)y / canvas.Height * 1.3, 0, 1))));
        }
        else
        {
            // the focus's place on the canvas, and the scale that keeps it there and still covers every edge
            var cx = v.OffsetX + FocusAtX * v.Scale;
            var cy = v.OffsetY + FocusAtY * v.Scale;
            double fx = FocusX * painting.Width, fy = FocusY * painting.Height;
            var scale = Math.Max(PaintingScale * DesignWidth / painting.Width * v.Scale,
                Math.Max(Math.Max(cx / fx, (canvas.Width - cx) / (painting.Width - fx)), Math.Max(cy / fy, (canvas.Height - cy) / (painting.Height - fy))));
            var left = fx - cx / scale;
            var top = fy - cy / scale;
            var part = ImageOps.Resized(painting, left, top, canvas.Width / scale, canvas.Height / scale, canvas.Width, canvas.Height);
            canvas.Image(part, new Rect(0, 0, canvas.Width, canvas.Height));
            if (scale > 1.05)
            {
                // enlarged: sharpened a little, so the brushwork still reads (a larger painting is the real cure)
                Sharpen(canvas, Math.Min(0.8, (scale - 1) * 1.6));
            }
        }
        // a little darker overall, more at the edges and behind the title, for the text on it
        double w = canvas.Width, h = canvas.Height;
        var titleX = v.OffsetX + 350 * v.Scale;
        var titleY = v.OffsetY + 110 * v.Scale;
        var titleR = 330 * v.Scale;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                double dx = (x - w / 2) / (w / 2), dy = (y - h / 2) / (h / 2);
                var edge = Math.Clamp((dx * dx + dy * dy - 0.35) / 1.4, 0, 1);
                double tx = (x - titleX) / titleR, ty = (y - titleY) / (titleR * 0.45);
                var title = Math.Clamp(1 - (tx * tx + ty * ty), 0, 1);
                canvas.Blend(x, y, Black, (int)(55 * edge + 40 * title));
            }
        }
    }

    /// <summary>An unsharp mask over the whole canvas: each channel pushed away from its 3 x 3 average by <paramref name="amount"/>.</summary>
    private static void Sharpen(Canvas canvas, double amount)
    {
        var source = (uint[])canvas.Pixels.Clone();
        int w = canvas.Width, h = canvas.Height;
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var q = source[(y + dy) * w + x + dx];
                        r += (int)(q >> 16 & 255);
                        g += (int)(q >> 8 & 255);
                        b += (int)(q & 255);
                    }
                }
                var c = source[y * w + x];
                int Push(int value, int sum) => Math.Clamp((int)Math.Round(value + amount * (value - sum / 9.0)), 0, 255);
                canvas.Pixels[y * w + x] = 0xFF000000u | (uint)Push((int)(c >> 16 & 255), r) << 16 | (uint)Push((int)(c >> 8 & 255), g) << 8 | (uint)Push((int)(c & 255), b);
            }
        }
    }

    /// <summary>
    /// A plate lit: a warm glow around it fading out over a few pixels, and its face a little brighter: fully under the pointer,
    /// at about half for the keyboard's choice.
    /// </summary>
    private static void Glow(Canvas canvas, View v, Rect area, double strength)
    {
        var reach = Math.Max(4, v.Len(13));
        for (var y = area.Y - reach; y < area.Bottom + reach; y++)
        {
            for (var x = area.X - reach; x < area.Right + reach; x++)
            {
                if (x < 0 || y < 0 || x >= canvas.Width || y >= canvas.Height)
                {
                    continue;
                }
                var dx = Math.Max(Math.Max(area.X - x, x - (area.Right - 1)), 0);
                var dy = Math.Max(Math.Max(area.Y - y, y - (area.Bottom - 1)), 0);
                if (dx == 0 && dy == 0)
                {
                    continue;
                }
                var d = Math.Sqrt(dx * dx + dy * dy) / reach;
                if (d < 1)
                {
                    canvas.Blend(x, y, GlowLight, (int)(235 * strength * (1 - d) * (1 - d)));
                }
            }
        }
        Shade(canvas, area.Inset(Math.Max(2, v.Len(4))), LampLight, (int)(75 * strength));
    }

    /// <summary>A message or status colour (chosen for the dark hint bar) as ink on parchment.</summary>
    private static uint OnParchment(uint colour) => colour switch
    {
        PaleRed => InkRed,
        Green => InkGreen,
        _ => InkSoft,
    };

    // ---- the stand-ins for the art, and small ornaments ----

    /// <summary>A parchment card: its art scaled to <paramref name="area"/>, or parchment drawn with ragged, burnt edges.</summary>
    private static void Parchment(Canvas canvas, Rect area, View v, string art, int seed)
    {
        if (Assets.Art(art) is { } image)
        {
            Sliced(canvas, v, image, art, area);
            return;
        }
        var scale = Math.Max(0.01, v.Scale);
        for (var y = Math.Max(0, area.Y); y < Math.Min(canvas.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(canvas.Width, area.Right); x++)
            {
                double dx = (x - area.X) / scale, dy = (y - area.Y) / scale;
                double w = area.Width / scale, h = area.Height / scale;
                var edge = Math.Min(Math.Min(dx, w - dx), Math.Min(dy, h - dy));
                // the ragged edge: up to 5 design pixels eaten, varying along the border
                var along = dx < 8 || w - dx < 8 ? dy : dx;
                var ragged = 1.5 + 3.5 * Noise(along / 9, (dx < 8 ? 1 : w - dx < 8 ? 2 : dy < 8 ? 3 : 4) * 17, seed);
                if (edge < ragged)
                {
                    continue;
                }
                var grain = 0.55 * Noise(dx / 45, dy / 45, seed) + 0.3 * Noise(dx / 11, dy / 11, seed + 1) + 0.15 * Noise(dx / 3, dy / 3, seed + 2);
                var colour = Colours.Mix(ParchmentLight, ParchmentDark, (int)(255 * Math.Clamp(grain * 1.1 - 0.15, 0, 1)));
                var burn = Math.Clamp(1 - (edge - ragged) / 26, 0, 1);
                colour = Colours.Mix(colour, ParchmentBurn, (int)(170 * burn * burn));
                canvas.Blend(x, y, colour, 255);
            }
        }
    }

    /// <summary>A soft shadow under a card, on the painting.</summary>
    private static readonly Dictionary<string, Image> Silhouettes = [];

    /// <summary>
    /// A soft shadow under a panel, the shape of its art (ragged edges, curled corners): the art's outline, blurred and moved
    /// a little down and right; without the art, the drawn card's rectangular shadow.
    /// </summary>
    private static void PanelShadow(Canvas canvas, Rect area, View v, string art)
    {
        if (Assets.Art(art) is not { } image)
        {
            CardShadow(canvas, area, v);
            return;
        }
        Image silhouette;
        lock (Silhouettes)
        {
            if (!Silhouettes.TryGetValue(art, out silhouette!))
            {
                var white = new uint[image.Width * image.Height];
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        white[y * image.Width + x] = image.At(x, y) & 0xFF000000 | 0xFFFFFF;
                    }
                }
                Silhouettes[art] = silhouette = new Image(image.Width, image.Height, white);
            }
        }
        var blur = Math.Max(1, v.Len(9));
        var margin = 3 * blur;
        var mask = new Canvas(area.Width + 2 * margin, area.Height + 2 * margin);
        mask.Clear(0);
        Sliced(mask, v, silhouette, art, new Rect(margin, margin, area.Width, area.Height));
        // the coverage (0..255), blurred by two box passes each way
        var w = mask.Width;
        var h = mask.Height;
        var a = new int[w * h];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = (int)(mask.Pixels[i] & 0xFF);
        }
        var t = new int[w * h];
        for (var pass = 0; pass < 2; pass++)
        {
            BoxBlur(a, t, w, h, blur, horizontal: true);
            BoxBlur(t, a, w, h, blur, horizontal: false);
        }
        int dx = v.Len(4), dy = v.Len(7);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (a[y * w + x] > 0)
                {
                    canvas.Blend(area.X - margin + x + dx, area.Y - margin + y + dy, Black, a[y * w + x] * 150 / 255);
                }
            }
        }
    }

    /// <summary>One pass of a box blur of <paramref name="radius"/> along rows or columns, from <paramref name="from"/> into <paramref name="to"/>.</summary>
    private static void BoxBlur(int[] from, int[] to, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h, span = 2 * radius + 1;
        for (var line = 0; line < lines; line++)
        {
            int At(int i) => horizontal ? from[line * w + Math.Clamp(i, 0, w - 1)] : from[Math.Clamp(i, 0, h - 1) * w + line];
            var sum = 0;
            for (var i = -radius; i <= radius; i++)
            {
                sum += At(i);
            }
            for (var i = 0; i < length; i++)
            {
                to[horizontal ? line * w + i : i * w + line] = sum / span;
                sum += At(i + radius + 1) - At(i - radius);
            }
        }
    }

    private static void CardShadow(Canvas canvas, Rect area, View v)
    {
        var reach = Math.Max(2, v.Len(14));
        var offset = v.Len(6);
        for (var y = area.Y - reach + offset; y < area.Bottom + reach + offset; y++)
        {
            for (var x = area.X - reach + offset; x < area.Right + reach + offset; x++)
            {
                var dx = Math.Max(Math.Max(area.X + offset - x, x - (area.Right + offset - 1)), 0);
                var dy = Math.Max(Math.Max(area.Y + offset - y, y - (area.Bottom + offset - 1)), 0);
                var d = Math.Sqrt(dx * dx + dy * dy) / reach;
                if (d < 1)
                {
                    canvas.Blend(x, y, Black, (int)(150 * (1 - d) * (1 - d)));
                }
            }
        }
    }

    /// <summary>The settings panel's corner pieces: the corner art mirrored to each corner, or drawn iron brackets.</summary>
    private static void Corners(Canvas canvas, Rect area, View v)
    {
        var size = Assets.Art("corner") is null ? v.Len(34) : v.Len(60);
        var inset = Assets.Art("corner") is null ? v.Len(2) : -v.Len(9);
        if (Assets.Art("corner") is { } corner)
        {
            foreach (var (h, vert) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                var x = h ? area.Right - inset - size : area.X + inset;
                var y = vert ? area.Bottom - inset - size : area.Y + inset;
                ImageOps.Draw(canvas, ImageOps.Mirrored(corner, h, vert), new Rect(x, y, size, size));
            }
            return;
        }
        var bar = Math.Max(2, v.Len(5));
        var length = v.Len(30);
        foreach (var (h, vert) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var cx = h ? area.Right - inset - bar : area.X + inset;
            var cy = vert ? area.Bottom - inset - bar : area.Y + inset;
            var horizontal = new Rect(h ? cx + bar - length : cx, cy, length, bar);
            var upright = new Rect(cx, vert ? cy + bar - length : cy, bar, length);
            foreach (var piece in new[] { horizontal, upright })
            {
                canvas.Fill(piece, 0x2A231C);
                canvas.Frame(piece, Math.Max(1, v.Len(1)), 0x0F0C09);
            }
            var stud = Math.Max(2, v.Len(4));
            canvas.Fill(new Rect(cx + (bar - stud) / 2, cy + (bar - stud) / 2, stud, stud), Gold);
        }
    }

    /// <summary>One of the small icons (art "icon-…", else a drawn sign in <paramref name="ink"/>).</summary>
    private static void Icon(Canvas canvas, View v, string name, Rect area, uint ink)
    {
        if (Assets.Art(name) is { } art)
        {
            ImageOps.Draw(canvas, art, area);
            return;
        }
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var r = area.Width / 2;
        var stroke = Math.Max(1, area.Width / 9);
        switch (name)
        {
            case "icon-sound":
                // a quaver: a head, a stem and a flag
                Disc(canvas, cx - r / 3, cy + r / 2, Math.Max(2, r * 2 / 5), ink);
                canvas.Fill(new Rect(cx - r / 3 + Math.Max(2, r * 2 / 5) - stroke, cy - r, stroke, r * 3 / 2), ink);
                canvas.Fill(new Rect(cx - r / 3 + Math.Max(2, r * 2 / 5) - stroke, cy - r, r * 2 / 3, stroke * 2), ink);
                break;
            case "icon-link":
                // a box with an arrow leaving it
                canvas.Frame(new Rect(area.X, area.Y + area.Height / 3, area.Width * 2 / 3, area.Height * 2 / 3), stroke, ink);
                for (var i = 0; i < area.Width * 2 / 3; i++)
                {
                    canvas.Fill(new Rect(area.X + area.Width / 3 + i, area.Bottom - area.Height / 3 - i - stroke, stroke + 1, stroke), ink);
                }
                canvas.Fill(new Rect(area.Right - area.Width / 2, area.Y, area.Width / 2, stroke), ink);
                canvas.Fill(new Rect(area.Right - stroke, area.Y, stroke, area.Height / 2), ink);
                break;
            case "icon-problem":
                // ⓘ: a ring with an i
                Ring(canvas, cx, cy, r, stroke, ink);
                canvas.Fill(new Rect(cx - stroke / 2, cy - r / 2 + stroke, Math.Max(1, stroke), Math.Max(1, stroke)), ink);
                canvas.Fill(new Rect(cx - stroke / 2, cy - r / 2 + 3 * stroke, Math.Max(1, stroke), Math.Max(1, r - 2 * stroke)), ink);
                break;
            case "icon-found":
                // ✓
                for (var i = 0; i <= r / 2; i++)
                {
                    canvas.Fill(new Rect(cx - r + i, cy + i - stroke, stroke + 1, stroke + 1), ink);
                }
                for (var i = 0; i <= r * 3 / 2; i++)
                {
                    canvas.Fill(new Rect(cx - r / 2 + i, cy + r / 2 - i - stroke, stroke + 1, stroke + 1), ink);
                }
                break;
            default:
                // a cross with pointed arms (the game, the display, the start-up)
                Diamond(canvas, cx, cy - r / 2, r / 2, ink);
                Diamond(canvas, cx, cy + r / 2, r / 2, ink);
                Diamond(canvas, cx - r / 2, cy, r / 2, ink);
                Diamond(canvas, cx + r / 2, cy, r / 2, ink);
                Disc(canvas, cx, cy, Math.Max(1, r / 4), ink);
                break;
        }
    }

    /// <summary>A rule with a fleuron at its middle (art "fleuron", else a drawn diamond between two dots).</summary>
    private static void Fleuron(Canvas canvas, View v, int cx, int cy, int halfWidth, uint ink, bool onDark)
    {
        var thick = Math.Max(1, v.Len(1));
        var gap = v.Len(26);
        if (Assets.Art("fleuron") is { } flower && Assets.Art("rule-left") is { } ruleLeft && Assets.Art("rule-right") is { } ruleRight)
        {
            var fh = v.Len(onDark ? 26 : 20);
            var fw = (int)Math.Round((double)fh * flower.Width / flower.Height);
            ImageOps.Draw(canvas, flower, new Rect(cx - fw / 2, cy - fh / 2, fw, fh));
            var rh = Math.Max(2, v.Len(onDark ? 7 : 5));
            var space = fw / 2 + v.Len(4);
            if (halfWidth > space + rh)
            {
                // the knob end keeps its shape (three times the rule's height), the tapering line stretches
                var knob = ruleLeft.Height * 3;
                ImageOps.ThreeSlice(canvas, ruleLeft, new Rect(cx - halfWidth, cy - rh / 2, halfWidth - space, rh), 0, Math.Min(knob, ruleLeft.Width / 2));
                ImageOps.ThreeSlice(canvas, ruleRight, new Rect(cx + space, cy - rh / 2, halfWidth - space, rh), Math.Min(knob, ruleRight.Width / 2), 0);
            }
            return;
        }
        if (halfWidth > gap)
        {
            canvas.Fill(new Rect(cx - halfWidth, cy, halfWidth - gap, thick), ink);
            canvas.Fill(new Rect(cx + gap, cy, halfWidth - gap, thick), ink);
            Diamond(canvas, cx - halfWidth, cy, v.Len(2.5), ink);
            Diamond(canvas, cx + halfWidth, cy, v.Len(2.5), ink);
        }
        if (Assets.Art("fleuron") is { } art)
        {
            var w = v.Len(44);
            var h = (int)Math.Round((double)w * art.Height / art.Width);
            ImageOps.Draw(canvas, art, new Rect(cx - w / 2, cy - h / 2, w, h));
            return;
        }
        Diamond(canvas, cx, cy, v.Len(6), ink);
        Disc(canvas, cx - v.Len(14), cy, Math.Max(1, v.Len(2)), ink);
        Disc(canvas, cx + v.Len(14), cy, Math.Max(1, v.Len(2)), ink);
        if (onDark)
        {
            Diamond(canvas, cx, cy, v.Len(2.5), 0x3A2E22);
        }
    }

    /// <summary>
    /// A piece of art in its box: in nine slices (its borders at half size: the art is made at twice the design's size), in three
    /// (a plate: its ends kept at the box's height), or stretched whole when it is not sliced.
    /// </summary>
    private static void Sliced(Canvas canvas, View v, Image art, string name, Rect area)
    {
        switch (Assets.Slice(name))
        {
            case { Top: 0, Bottom: 0 } plate:
                ImageOps.ThreeSlice(canvas, art, area, plate.Left, plate.Right);
                break;
            case { } nine:
                ImageOps.NineSlice(canvas, art, area, nine, v.Scale / 2);
                break;
            default:
                ImageOps.Draw(canvas, art, area);
                break;
        }
    }

    private static void Diamond(Canvas canvas, int cx, int cy, int r, uint colour)
    {
        for (var dy = -r; dy <= r; dy++)
        {
            var w = r - Math.Abs(dy);
            canvas.Fill(new Rect(cx - w, cy + dy, 2 * w + 1, 1), colour);
        }
    }

    private static void Disc(Canvas canvas, int cx, int cy, int r, uint colour)
    {
        for (var dy = -r; dy <= r; dy++)
        {
            var w = (int)Math.Round(Math.Sqrt(r * r - dy * dy));
            canvas.Fill(new Rect(cx - w, cy + dy, 2 * w + 1, 1), colour);
        }
    }

    private static void Ring(Canvas canvas, int cx, int cy, int r, int stroke, uint colour)
    {
        for (var y = cy - r; y <= cy + r; y++)
        {
            for (var x = cx - r; x <= cx + r; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= r && d > r - stroke - 0.5)
                {
                    canvas.Blend(x, y, colour, 255);
                }
            }
        }
    }

    /// <summary><paramref name="colour"/> laid over <paramref name="area"/> at <paramref name="alpha"/>.</summary>
    private static void Shade(Canvas canvas, Rect area, uint colour, int alpha)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                canvas.Blend(x, y, colour, alpha);
            }
        }
    }

    /// <summary>Text in the strong face; thickened by a pixel when the SemiBold file is not embedded.</summary>
    private static void StrongText(Canvas canvas, View v, TextFace face, ReadOnlySpan<char> text, int x, int y, uint colour)
    {
        if (!Assets.HasStrong)
        {
            face.Draw(canvas, text, x + Math.Max(1, v.Len(0.6)), y, colour);
        }
        face.Draw(canvas, text, x, y, colour);
    }

    /// <summary>Smooth value noise in 0..1 (a fixed pattern for each seed).</summary>
    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed), c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    /// <summary>The text wrapped at spaces to <paramref name="width"/>; a word longer than a line (a path) is broken where it must.</summary>
    internal static List<string> WrapAll(TextFace face, string text, int width)
    {
        var lines = new List<string>();
        foreach (var line in face.Wrap(text, width))
        {
            var rest = line;
            while (face.Measure(rest) > width && rest.Length > 1)
            {
                // the longest start that fits, broken after a path's separator when there is one in it
                var cut = 1;
                while (cut < rest.Length && face.Measure(rest.AsSpan(0, cut + 1)) <= width)
                {
                    cut++;
                }
                var separator = rest.AsSpan(0, cut).LastIndexOfAny('\\', '/');
                if (separator > cut / 2)
                {
                    cut = separator + 1;
                }
                lines.Add(rest[..cut]);
                rest = rest[cut..];
            }
            lines.Add(rest);
        }
        return lines;
    }
}
