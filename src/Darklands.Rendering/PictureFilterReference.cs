using Darklands.Platform;

namespace Darklands.Rendering;

/// <summary>
/// The picture filters drawn on the CPU: what each <see cref="PictureFilter"/> must look like, independent of any graphics
/// library. The host's renderers (the GPU renderer's pixel shader, Shaders/picture.frag.hlsl, which follows this code step by
/// step; and render targets with blend modes elsewhere) are checked against it (Darklands.Host.Sdl.Tests.PictureFilterTests),
/// and its renders let a person judge and tune the looks without a window. Slow (seconds a frame at 1920x1080): for tests and
/// reference pictures, never for playing. New code (presentation only, outside the game); the filters are this project's own.
/// </summary>
public static class PictureFilterReference
{
    /// <summary>
    /// The picture <paramref name="argb"/> (<paramref name="sourceWidth"/> x <paramref name="sourceHeight"/>, opaque) shown at
    /// <paramref name="width"/> x <paramref name="height"/> output pixels through <paramref name="filter"/>, sampled at each
    /// output pixel's centre. <paramref name="curvature"/> (the CRT kinds only; 0: none) bends the picture: the point seen at an
    /// output pixel moves out from the middle by d |d|² x curvature (d: the pixel's place from the middle, in picture widths
    /// and heights); what falls outside is black.
    /// </summary>
    public static uint[] Render(ReadOnlySpan<uint> argb, int sourceWidth, int sourceHeight, PictureFilter filter, int width, int height,
        double curvature = 0)
    {
        var picture = new Picture(argb.ToArray(), sourceWidth, sourceHeight, width, height);
        var bend = PictureFilters.IsCrt(filter) ? curvature : 0;
        var output = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                if (bend > 0)
                {
                    var dx = px / width - 0.5;
                    var dy = py / height - 0.5;
                    var r2 = dx * dx + dy * dy;
                    px = (px / width + dx * r2 * bend) * width;
                    py = (py / height + dy * r2 * bend) * height;
                    if (px < 0 || py < 0 || px > width || py > height)
                    {
                        output[y * width + x] = 0xFF000000;
                        continue;
                    }
                }
                var colour = filter switch
                {
                    PictureFilter.Sharp => picture.Nearest(px, py),
                    PictureFilter.Even => picture.Even(px, py),
                    PictureFilter.Soft => picture.Soft(px, py),
                    PictureFilter.SoftCrt => picture.Mask(picture.Soft(px, py), py, x),
                    PictureFilter.Hq => picture.Hq(px, py),
                    PictureFilter.Crt => picture.Mask(picture.Even(px, py), py, x),
                    PictureFilter.CrtPlus => picture.Plus(px, py, x),
                    _ => picture.Nearest(px, py),
                };
                output[y * width + x] = 0xFF000000u
                    | (uint)Math.Clamp(Math.Round(colour.R * 255), 0, 255) << 16
                    | (uint)Math.Clamp(Math.Round(colour.G * 255), 0, 255) << 8
                    | (uint)Math.Clamp(Math.Round(colour.B * 255), 0, 255);
            }
        }
        return output;
    }

    /// <summary>A colour, channels 0..1 (alpha premultiplied where pictures with transparent parts are blended).</summary>
    internal readonly record struct Rgba(double R, double G, double B, double A)
    {
        public static Rgba operator +(Rgba a, Rgba b) => new(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);

        public static Rgba operator *(Rgba a, double k) => new(a.R * k, a.G * k, a.B * k, a.A * k);

        public static Rgba Lerp(Rgba a, Rgba b, double t) => a * (1 - t) + b * t;

        public double Luma => 0.299 * R + 0.587 * G + 0.114 * B;

        public bool Same(Rgba o) => Math.Abs(R - o.R) < PictureFilterMath.HqSameColour && Math.Abs(G - o.G) < PictureFilterMath.HqSameColour
            && Math.Abs(B - o.B) < PictureFilterMath.HqSameColour && Math.Abs(A - o.A) < PictureFilterMath.HqSameColour;
    }

    /// <summary>The game's picture and where it is shown; the filters' sampling (the shader's functions of the same names).</summary>
    internal sealed class Picture(uint[] argb, int sourceWidth, int sourceHeight, int width, int height)
    {
        private readonly (int X, int Y) _k = PictureFilterMath.Prescale(width, height, sourceWidth, sourceHeight);
        private readonly double _rowHeight = height / (double)sourceHeight;

        /// <summary>A texel, the picture's edge repeated outside it.</summary>
        public Rgba Texel(int x, int y)
        {
            var c = argb[Math.Clamp(y, 0, sourceHeight - 1) * sourceWidth + Math.Clamp(x, 0, sourceWidth - 1)];
            return new Rgba((c >> 16 & 0xFF) / 255.0, (c >> 8 & 0xFF) / 255.0, (c & 0xFF) / 255.0, (c >> 24 & 0xFF) / 255.0);
        }

        public Rgba Nearest(double px, double py) =>
            Texel((int)Math.Floor(px * sourceWidth / width), (int)Math.Floor(py * sourceHeight / height));

        /// <summary>Even (sharp bilinear): the picture enlarged by whole numbers with hard pixels, sampled bilinearly.</summary>
        public Rgba Even(double px, double py)
        {
            var (c0, c1, fx) = Tap(px, width, sourceWidth, _k.X);
            var (r0, r1, fy) = Tap(py, height, sourceHeight, _k.Y);
            return Rgba.Lerp(Rgba.Lerp(Texel(c0, r0), Texel(c1, r0), fx), Rgba.Lerp(Texel(c0, r1), Texel(c1, r1), fx), fy);
        }

        private static (int First, int Second, double Weight) Tap(double p, int size, int source, int k)
        {
            var enlarged = source * k;
            var t = p * enlarged / size - 0.5;
            var t0 = Math.Floor(t);
            var weight = t - t0;
            var a = (int)Math.Clamp(t0, 0, enlarged - 1) / k;
            var b = (int)Math.Clamp(t0 + 1, 0, enlarged - 1) / k;
            return (a, b, a == b ? 0 : weight);
        }

        /// <summary>Soft: a Gaussian beam over the 5 x 5 texels around the point, normalised.</summary>
        public Rgba Soft(double px, double py) =>
            Gauss(px * sourceWidth / width - 0.5, py * sourceHeight / height - 0.5, PictureFilterMath.SoftSpreadX, PictureFilterMath.SoftSpreadY);

        /// <summary>The texels around (u, v) (texel centres at whole numbers) weighted by a Gaussian of those spreads, normalised.</summary>
        public Rgba Gauss(double u, double v, double spreadX, double spreadY)
        {
            var cx = (int)Math.Floor(u + 0.5);
            var cy = (int)Math.Floor(v + 0.5);
            var sum = new Rgba(0, 0, 0, 0);
            var total = 0.0;
            for (var j = -2; j <= 2; j++)
            {
                var dy = (cy + j - v) / spreadY;
                for (var i = -2; i <= 2; i++)
                {
                    var dx = (cx + i - u) / spreadX;
                    var w = Math.Exp(-0.5 * (dx * dx + dy * dy));
                    sum += Premultiplied(Texel(cx + i, cy + j)) * w;
                    total += w;
                }
            }
            return Straight(sum * (1 / total));
        }

        private static Rgba Premultiplied(Rgba c) => new(c.R * c.A, c.G * c.A, c.B * c.A, c.A);

        private static Rgba Straight(Rgba c) => c.A > 0 ? new Rgba(c.R / c.A, c.G / c.A, c.B / c.A, c.A) : c;

        /// <summary>The CRT's scanline (across the picture's row at py) and aperture column (output column x) on a colour.</summary>
        public Rgba Mask(Rgba c, double py, int x)
        {
            var depth = PictureFilterMath.ScanlineDepthFor(_rowHeight);
            var gain = PictureFilterMath.CrtGain(depth);
            var v = py / _rowHeight;
            var scanline = 1 - depth * (1 + Math.Cos(2 * Math.PI * (v - Math.Floor(v)))) / 2;
            double M(int channel) => gain * scanline * (x % 3 == channel ? 1 : 1 - PictureFilterMath.MaskStrength);
            return new Rgba(c.R * M(0), c.G * M(1), c.B * M(2), c.A);
        }

        /// <summary>
        /// HQ: Even, with the corner of a texel cut where a staircase edge passes it: the two texels beside the corner and the one
        /// across it share a colour other than this texel's, and the staircase goes on (the texel diagonally beside it on the other
        /// side has this texel's colour). Checkerboard dithering (the texel across the corner is this one's colour), straight
        /// edges, rectangle corners and lone pixels are left as they are. The cut: the line through the middles of the corner's
        /// two sides, blended over about one output pixel.
        /// </summary>
        public Rgba Hq(double px, double py)
        {
            var baseColour = Even(px, py);
            var sx = px * sourceWidth / width;
            var sy = py * sourceHeight / height;
            var tx = (int)Math.Floor(sx);
            var ty = (int)Math.Floor(sy);
            var fx = sx - tx;
            var fy = sy - ty;
            var dx = fx >= 0.5 ? 1 : -1;
            var dy = fy >= 0.5 ? 1 : -1;
            var u = dx > 0 ? fx : 1 - fx;
            var v = dy > 0 ? fy : 1 - fy;
            var p = Texel(tx, ty);
            var a = Texel(tx + dx, ty);
            var b = Texel(tx, ty + dy);
            var q = Texel(tx + dx, ty + dy);
            var cut = a.Same(b) && !p.Same(a) && q.Same(a) && (Texel(tx - dx, ty + dy).Same(p) || Texel(tx + dx, ty - dy).Same(p));
            if (!cut)
            {
                return baseColour;
            }
            var ramp = (double)sourceWidth / width + (double)sourceHeight / height;
            var alpha = Math.Clamp((u + v - 1.5) / ramp + 0.5, 0, 1);
            return Rgba.Lerp(baseColour, a, alpha);
        }

        /// <summary>A row of the picture at u (texel centres at whole numbers): a narrow Gaussian across (CRT+'s spread), the beam's colour.</summary>
        public Rgba Row(int row, double u)
        {
            var cx = (int)Math.Floor(u + 0.5);
            var sum = new Rgba(0, 0, 0, 0);
            var total = 0.0;
            for (var i = -2; i <= 2; i++)
            {
                var dx = (cx + i - u) / PictureFilterMath.PlusSpreadX;
                var w = Math.Exp(-0.5 * dx * dx);
                sum += Texel(cx + i, row) * w;
                total += w;
            }
            return sum * (1 / total);
        }

        /// <summary>
        /// CRT+: the beams of the rows near the point, each as tall as its brightness makes it (a Gaussian across the row), summed;
        /// where the rows are too thin to show scanlines, the rows blended instead; the stronger aperture mask; a glow (the picture
        /// blurred wide) added over it.
        /// </summary>
        public Rgba Plus(double px, double py, int x)
        {
            var u = px * sourceWidth / width - 0.5;
            var v = py * sourceHeight / height;
            var r0 = (int)Math.Floor(v - 0.5);
            var beams = new Rgba(0, 0, 0, 0);
            for (var r = r0 - 1; r <= r0 + 2; r++)
            {
                var row = Row(Math.Clamp(r, 0, sourceHeight - 1), u);
                var spread = PictureFilterMath.PlusBeamDark + (PictureFilterMath.PlusBeamBright - PictureFilterMath.PlusBeamDark) * Math.Clamp(row.Luma, 0, 1);
                var d = (v - (r + 0.5)) / spread;
                beams += row * Math.Exp(-0.5 * d * d);
            }
            beams *= PictureFilterMath.PlusBeamGain;
            var t = v - 0.5 - r0;
            var flat = Rgba.Lerp(Row(Math.Clamp(r0, 0, sourceHeight - 1), u), Row(Math.Clamp(r0 + 1, 0, sourceHeight - 1), u), t);
            var scan = Rgba.Lerp(flat, beams, PictureFilterMath.ScanlineDepthFor(_rowHeight) / PictureFilterMath.ScanlineDepth);
            var glow = Gauss(u, v - 0.5, PictureFilterMath.PlusGlowSpread, PictureFilterMath.PlusGlowSpread);
            double Channel(double s, double g, int channel) =>
                s * PictureFilterMath.PlusMaskGain * (x % 3 == channel ? 1 : 1 - PictureFilterMath.PlusMaskStrength) + PictureFilterMath.PlusGlowAmount * g;
            return new Rgba(Channel(scan.R, glow.R, 0), Channel(scan.G, glow.G, 1), Channel(scan.B, glow.B, 2), 1);
        }
    }
}
