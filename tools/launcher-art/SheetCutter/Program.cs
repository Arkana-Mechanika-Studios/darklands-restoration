// Cuts the artist's launcher sheets into the launcher's pieces, by their alpha, checks them and writes them for
// Convert-LauncherArt.ps1 (tools/launcher-art/ASSETS.md):
//   <folder>/rgba/*.png  the sheets as 8-bit RGBA PNG (WebP and the like converted first, e.g. by Windows' WIC)
//   <folder>/out/        the pieces (<name>.png, and <name>.slice.txt for the sliced ones), then <folder>/report.txt
// Checks: transparency painted as a checkerboard (keyed out), white halos, the 9-slices' inner edges and seams, the sizes.
//   dotnet run --project tools/launcher-art/SheetCutter -- <folder>
using System.Text;
using Darklands.Rendering;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: SheetCutter <folder with rgba/>");
    return 1;
}
var root = args[0];
var input = Path.Combine(root, "rgba");
var output = Path.Combine(root, "out");
Directory.CreateDirectory(output);
var report = new StringBuilder();
void Say(string s) { report.AppendLine(s); Console.WriteLine(s); }

static (int A, int R, int G, int B) Px(uint c) => ((int)(c >> 24), (int)(c >> 16 & 255), (int)(c >> 8 & 255), (int)(c & 255));
static bool Greyish(uint c, int light)
{
    var (a, r, g, b) = Px(c);
    var max = Math.Max(r, Math.Max(g, b));
    var min = Math.Min(r, Math.Min(g, b));
    return a > 0 && max - min < 22 && min >= light;
}

RgbaImage Load(string name) => PngCodec.Read(Path.Combine(input, name + ".png"));

// alpha statistics, and whether the "transparent" parts are a painted checkerboard
(bool Fake, string Text) Alpha(RgbaImage img)
{
    long clear = 0, partial = 0, opaqueGrey = 0, total = img.Argb.Length;
    foreach (var c in img.Argb)
    {
        var a = (int)(c >> 24);
        if (a < 16) clear++;
        else if (a < 240) partial++;
        if (a >= 160 && Greyish(c, 215)) opaqueGrey++;
    }
    var fake = opaqueGrey > total / 50;
    return (fake, $"alpha: {100.0 * clear / total:F1}% clear, {100.0 * partial / total:F1}% partial, opaque light-grey {100.0 * opaqueGrey / total:F1}%{(fake ? "  => PAINTED CHECKERBOARD (fake transparency)" : "")}");
}

// a painted light-grey / white background keyed out: low-saturation light pixels become transparent, their edge softened
RgbaImage KeyOut(RgbaImage img)
{
    var o = new RgbaImage(img.Width, img.Height);
    for (var i = 0; i < img.Argb.Length; i++)
    {
        var c = img.Argb[i];
        var (a, r, g, b) = Px(c);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var sat = max - min;
        // parchment is saturated tan (sat > 30); the checkerboard is grey-white (sat < 10, light > 200)
        // fully keyed at min >= 222 and sat < 22; a soft ramp down to min 200 / sat 34 for the edges
        var light = Math.Clamp((min - 190) / 20.0, 0, 1);
        var grey = Math.Clamp((42 - sat) / 12.0, 0, 1);
        var keyed = (int)Math.Round(a * (1 - light * grey));
        o.Argb[i] = (uint)Math.Clamp(Math.Min(a, keyed), 0, 255) << 24 | (c & 0xFFFFFF);
    }
    return o;
}

bool Solid(RgbaImage img, int x, int y) => img.Argb[y * img.Width + x] >> 24 >= 128;

// pieces: bands of rows holding something, then runs of columns in each band, each tightened
List<(int X, int Y, int W, int H)> Pieces(RgbaImage img, int minGap = 6)
{
    List<(int, int)> Runs(int n, Func<int, bool> has)
    {
        var runs = new List<(int, int)>();
        int start = -1, empty = 0;
        for (var i = 0; i <= n; i++)
        {
            var h = i < n && has(i);
            if (h) { if (start < 0) start = i; empty = 0; }
            else if (start >= 0 && (++empty >= minGap || i == n)) { runs.Add((start, i - empty + 1)); start = -1; empty = 0; }
        }
        return runs;
    }
    var result = new List<(int, int, int, int)>();
    foreach (var (y0, y1) in Runs(img.Height, y => Enumerable.Range(0, img.Width).Count(x => Solid(img, x, y)) >= 3))
    {
        foreach (var (x0, x1) in Runs(img.Width, x => Enumerable.Range(y0, y1 - y0).Count(y => Solid(img, x, y)) >= 3))
        {
            var rows = Runs(y1 - y0, dy => Enumerable.Range(x0, x1 - x0).Count(x => Solid(img, x, y0 + dy)) >= 2);
            if (rows.Count == 0) continue;
            var ty0 = y0 + rows[0].Item1;
            var ty1 = y0 + rows[^1].Item2;
            result.Add((x0, ty0, x1 - x0, ty1 - ty0));
        }
    }
    return result;
}

RgbaImage Crop(RgbaImage img, (int X, int Y, int W, int H) r)
{
    var o = new RgbaImage(r.W, r.H);
    for (var y = 0; y < r.H; y++)
        Array.Copy(img.Argb, (r.Y + y) * img.Width + r.X, o.Argb, y * r.W, r.W);
    return o;
}

// white halo: semi-transparent edge pixels that are much lighter than the opaque pixels next to them
string Halo(RgbaImage img)
{
    long edge = 0, light = 0;
    for (var y = 1; y < img.Height - 1; y++)
        for (var x = 1; x < img.Width - 1; x++)
        {
            var c = img.Argb[y * img.Width + x];
            var a = (int)(c >> 24);
            if (a < 20 || a > 230) continue;
            edge++;
            var (_, r, g, b) = Px(c);
            if (Math.Min(r, Math.Min(g, b)) > 205 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 30) light++;
        }
    return edge == 0 ? "halo: no soft edge" : $"halo: {light} of {edge} soft-edge pixels near-white ({100.0 * light / edge:F1}%){(light > edge / 10 ? "  => WHITE HALO" : "")}";
}

(int R, int G, int B) Mean(RgbaImage img, int x0, int y0, int w, int h)
{
    long r = 0, g = 0, b = 0, n = 0;
    for (var y = y0; y < y0 + h; y++)
        for (var x = x0; x < x0 + w; x++)
        {
            var c = img.Argb[y * img.Width + x];
            if (c >> 24 < 200) continue;
            r += c >> 16 & 255; g += c >> 8 & 255; b += c & 255; n++;
        }
    return n == 0 ? (0, 0, 0) : ((int)(r / n), (int)(g / n), (int)(b / n));
}

static int Dist((int R, int G, int B) a, (int R, int G, int B) b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

void Write(RgbaImage img, string name, string? slice = null)
{
    PngCodec.Write(img, Path.Combine(output, name + ".png"));
    if (slice is not null) File.WriteAllText(Path.Combine(output, name + ".slice.txt"), slice + "\n");
    Say($"  -> {name}.png {img.Width}x{img.Height}{(slice is null ? "" : "  slice " + slice)}");
}

// nine pieces in a 3 x 3 grid -> one image, corners kept, with "left top right bottom" borders
void NineSlice(string file, string name)
{
    Say($"\n== {file}");
    var img = Load(file);
    var (fake, text) = Alpha(img);
    Say("  " + text);
    var original = img;
    if (fake) { img = KeyOut(img); Say("  keyed out: " + Alpha(img).Text); }
    var p = Pieces(img);
    Say($"  pieces: {p.Count}: " + string.Join(" ", p.Select(q => $"({q.X},{q.Y} {q.W}x{q.H})")));
    if (p.Count != 9) { Say("  => NOT 9 PIECES, not assembled"); return; }
    if (fake)
    {
        // the key only near the pieces' edges and outside them: inside, the parchment's own light spots keep their alpha
        var keep = new RgbaImage(img.Width, img.Height, (uint[])img.Argb.Clone());
        foreach (var q in p)
            for (var y = q.Y + 14; y < q.Y + q.H - 14; y++)
                Array.Copy(original.Argb, y * img.Width + q.X + 14, keep.Argb, y * img.Width + q.X + 14, Math.Max(0, q.W - 28));
        img = keep;
        PngCodec.Write(img, Path.Combine(output, file + "-keyed.png"));
    }
    var c = p.Select(q => Crop(img, q)).ToList();
    // border sizes: the rows' heights and the columns' widths must agree
    int[] rowH = [p[0].H, p[1].H, p[2].H, p[6].H, p[7].H, p[8].H];
    int[] colW = [p[0].W, p[3].W, p[6].W, p[2].W, p[5].W, p[8].W];
    Say($"  top row heights {p[0].H}/{p[1].H}/{p[2].H}, bottom {p[6].H}/{p[7].H}/{p[8].H}; left col widths {p[0].W}/{p[3].W}/{p[6].W}, right {p[2].W}/{p[5].W}/{p[8].W}; centre {p[4].W}x{p[4].H}");
    Say($"  middle row heights {p[3].H}/{p[4].H}/{p[5].H}; middle col widths {p[1].W}/{p[4].W}/{p[7].W}");
    var spread = Math.Max(rowH[..3].Max() - rowH[..3].Min(), Math.Max(rowH[3..].Max() - rowH[3..].Min(), Math.Max(colW[..3].Max() - colW[..3].Min(), colW[3..].Max() - colW[3..].Min())));
    Say(spread > 4 ? $"  => BORDER SIZES DIFFER by up to {spread}px (pieces do not line up)" : $"  border sizes agree (within {spread}px)");
    // seams: the colour just inside each junction, on both sides (8px strips, middle half of the shared side)
    var seams = new List<string>();
    void Seam(int a, int b, bool horizontal)
    {
        var A = c[a]; var B = c[b];
        (int, int, int) ma, mb;
        if (horizontal)
        {
            var h = Math.Min(A.Height, B.Height);
            ma = Mean(A, A.Width - 8, h / 4, 8, h / 2);
            mb = Mean(B, 0, h / 4, 8, h / 2);
        }
        else
        {
            var w = Math.Min(A.Width, B.Width);
            ma = Mean(A, w / 4, A.Height - 8, w / 2, 8);
            mb = Mean(B, w / 4, 0, w / 2, 8);
        }
        seams.Add($"{a}-{b}:{Dist(ma, mb)}");
    }
    foreach (var (a, b) in new[] { (0, 1), (1, 2), (3, 4), (4, 5), (6, 7), (7, 8) }) Seam(a, b, true);
    foreach (var (a, b) in new[] { (0, 3), (3, 6), (1, 4), (4, 7), (2, 5), (5, 8) }) Seam(a, b, false);
    Say("  seam colour difference (sum |dR|+|dG|+|dB|, > 30 visible): " + string.Join(" ", seams));
    Say("  " + Halo(img));
    // assembled from one grid: the inner cut lines are the median of the pieces' inner edges (a piece reaching past it is cut
    // back to it), the outer lines the pieces' farthest extent; every cell is cut from the sheet at its grid rectangle
    static int Median(params int[] v) { Array.Sort(v); return v[v.Length / 2]; }
    var L = new[] { p[0].X, p[3].X, p[6].X }.Min();
    var A = Median(p[0].X + p[0].W, p[3].X + p[3].W, p[6].X + p[6].W);
    var B = Median(p[1].X, p[4].X, p[7].X);
    var C = Median(p[1].X + p[1].W, p[4].X + p[4].W, p[7].X + p[7].W);
    var D = Median(p[2].X, p[5].X, p[8].X);
    var R = new[] { p[2].X + p[2].W, p[5].X + p[5].W, p[8].X + p[8].W }.Max();
    var T = new[] { p[0].Y, p[1].Y, p[2].Y }.Min();
    var E = Median(p[0].Y + p[0].H, p[1].Y + p[1].H, p[2].Y + p[2].H);
    var F = Median(p[3].Y, p[4].Y, p[5].Y);
    var G = Median(p[3].Y + p[3].H, p[4].Y + p[4].H, p[5].Y + p[5].H);
    var H = Median(p[6].Y, p[7].Y, p[8].Y);
    var Bo = new[] { p[6].Y + p[6].H, p[7].Y + p[7].H, p[8].Y + p[8].H }.Max();
    var off = new List<string>();
    void Off(string what, int line, params int[] edges) { var d = edges.Max(e => Math.Abs(e - line)); if (d > 3) off.Add($"{what} off by {d}px"); }
    Off("left column's right edge", A, p[0].X + p[0].W, p[3].X + p[3].W, p[6].X + p[6].W);
    Off("middle column's left edge", B, p[1].X, p[4].X, p[7].X);
    Off("middle column's right edge", C, p[1].X + p[1].W, p[4].X + p[4].W, p[7].X + p[7].W);
    Off("right column's left edge", D, p[2].X, p[5].X, p[8].X);
    Off("top row's bottom edge", E, p[0].Y + p[0].H, p[1].Y + p[1].H, p[2].Y + p[2].H);
    Off("middle row's top edge", F, p[3].Y, p[4].Y, p[5].Y);
    Off("middle row's bottom edge", G, p[3].Y + p[3].H, p[4].Y + p[4].H, p[5].Y + p[5].H);
    Off("bottom row's top edge", H, p[6].Y, p[7].Y, p[8].Y);
    Say(off.Count == 0 ? "  inner cut edges line up" : "  => INNER EDGES MISALIGNED: " + string.Join("; ", off));
    int[] xs0 = [L, B, D], xs1 = [A, C, R], ys0 = [T, F, H], ys1 = [E, G, Bo];
    var cw = new[] { A - L, C - B, R - D };
    var ch = new[] { E - T, G - F, Bo - H };
    var o = new RgbaImage(cw.Sum(), ch.Sum());
    var cells = new RgbaImage[9];
    for (var i = 0; i < 9; i++)
    {
        var (col, row) = (i % 3, i / 3);
        cells[i] = Crop(img, (xs0[col], ys0[row], cw[col], ch[row]));
        // a piece that stops short of a shared cut line leaves a see-through sliver: its last solid pixels are carried to the line
        if (fake) Extend(cells[i], right: col < 2, left: col > 0, bottom: row < 2, top: row > 0);
        // every cut edge between two cells: its last pixels (often half transparent, from the export) replaced by the ones just inside
        Seal(cells[i], right: col < 2, left: col > 0, bottom: row < 2, top: row > 0);
        var ox = cw.Take(col).Sum();
        var oy = ch.Take(row).Sum();
        for (var y = 0; y < ch[row]; y++)
            Array.Copy(cells[i].Argb, y * cw[col], o.Argb, (oy + y) * o.Width + ox, cw[col]);
    }
    // seams on the assembled cells: 8px strips on each side of a join, over the middle half of the shared side
    var joins = new List<string>();
    foreach (var (a2, b2, horizontal) in new[] { (0, 1, true), (1, 2, true), (3, 4, true), (4, 5, true), (6, 7, true), (7, 8, true), (0, 3, false), (3, 6, false), (1, 4, false), (4, 7, false), (2, 5, false), (5, 8, false) })
    {
        var P = cells[a2]; var Q = cells[b2];
        var d = horizontal
            ? Dist(Mean(P, P.Width - 8, P.Height / 4, 8, P.Height / 2), Mean(Q, 0, Q.Height / 4, 8, Q.Height / 2))
            : Dist(Mean(P, P.Width / 4, P.Height - 8, P.Width / 2, 8), Mean(Q, Q.Width / 4, 0, Q.Width / 2, 8));
        joins.Add($"{a2}-{b2}:{d}");
    }
    Say("  seams after assembly (> 30 visible): " + string.Join(" ", joins));
    // nearly opaque is opaque (the parchment came at about 98%: the painting would show through it)
    for (var i = 0; i < o.Argb.Length; i++)
        if (o.Argb[i] >> 24 >= 235) o.Argb[i] |= 0xFF000000;
    int l = cw[0], t = ch[0], r = cw[2], bt = ch[2];
    Write(o, name, $"{l} {t} {r} {bt}");
}

// carries each inner edge's last solid pixel (alpha >= 200, found within 12px) out to the cell's edge, where 12px further in is solid
static void Extend(RgbaImage c, bool right, bool left, bool bottom, bool top)
{
    const int Reach = 18, Trim = 10;
    uint At(int x, int y) => c.Argb[y * c.Width + x];
    void Set(int x, int y, uint v) => c.Argb[y * c.Width + x] = v;
    // real parchment or frame: opaque and coloured (the checkerboard left over is grey)
    bool Good(uint p) { int r = (int)(p >> 16 & 255), g = (int)(p >> 8 & 255), b = (int)(p & 255); return p >> 24 >= 240 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= 24; }
    for (var y = 0; y < c.Height; y++)
    {
        if (right && Good(At(c.Width - 1 - Reach, y)))
        {
            var x = c.Width - 1 - Trim;
            while (x > c.Width - 1 - Reach && !Good(At(x, y))) x--;
            for (var f = x + 1; f < c.Width; f++) Set(f, y, At(x, y));
        }
        if (left && Good(At(Reach, y)))
        {
            var x = Trim;
            while (x < Reach && !Good(At(x, y))) x++;
            for (var f = 0; f < x; f++) Set(f, y, At(x, y));
        }
    }
    for (var x = 0; x < c.Width; x++)
    {
        if (bottom && Good(At(x, c.Height - 1 - Reach)))
        {
            var y = c.Height - 1 - Trim;
            while (y > c.Height - 1 - Reach && !Good(At(x, y))) y--;
            for (var f = y + 1; f < c.Height; f++) Set(x, f, At(x, y));
        }
        if (top && Good(At(x, Reach)))
        {
            var y = Trim;
            while (y < Reach && !Good(At(x, y))) y++;
            for (var f = 0; f < y; f++) Set(x, f, At(x, y));
        }
    }
}

// the outer 3 pixels of each inner edge copied from the 4th pixel in
static void Seal(RgbaImage c, bool right, bool left, bool bottom, bool top)
{
    const int Edge = 3;
    uint At(int x, int y) => c.Argb[y * c.Width + x];
    void Set(int x, int y, uint v) => c.Argb[y * c.Width + x] = v;
    for (var y = 0; y < c.Height; y++)
    {
        for (var e = 0; e < Edge; e++)
        {
            if (right) Set(c.Width - 1 - e, y, At(c.Width - 1 - Edge, y));
            if (left) Set(e, y, At(Edge, y));
        }
    }
    for (var x = 0; x < c.Width; x++)
    {
        for (var e = 0; e < Edge; e++)
        {
            if (bottom) Set(x, c.Height - 1 - e, At(x, c.Height - 1 - Edge));
            if (top) Set(x, e, At(x, Edge));
        }
    }
}

// a nearest-neighbour fit of a piece into its cell (the pieces differ by a few pixels at most)
static RgbaImage Resize(RgbaImage s, int w, int h)
{
    if (s.Width == w && s.Height == h) return s;
    var o = new RgbaImage(w, h);
    for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            o.Argb[y * w + x] = s.Argb[Math.Min(s.Height - 1, y * s.Height / h) * s.Width + Math.Min(s.Width - 1, x * s.Width / w)];
    return o;
}

// three pieces in a row -> one image with "left right" cap widths
void ThreeSlice(RgbaImage img, List<(int X, int Y, int W, int H)> p, string name)
{
    var c = p.Select(q => Crop(img, q)).ToList();
    var h = c.Max(q => q.Height);
    Say($"  heights {c[0].Height}/{c[1].Height}/{c[2].Height}{(c.Max(q => q.Height) - c.Min(q => q.Height) > 3 ? "  => HEIGHTS DIFFER" : "")}");
    var ml = Mean(c[0], c[0].Width - 8, h / 4, 8, h / 2); var mm1 = Mean(c[1], 0, h / 4, 8, h / 2);
    var mm2 = Mean(c[1], c[1].Width - 8, h / 4, 8, h / 2); var mr = Mean(c[2], 0, h / 4, 8, h / 2);
    Say($"  seams left-middle {Dist(ml, mm1)}, middle-right {Dist(mm2, mr)} (> 30 visible)");
    var o = new RgbaImage(c.Sum(q => q.Width), h);
    var x = 0;
    foreach (var piece in c)
    {
        var cell = Resize(piece, piece.Width, h);
        for (var y = 0; y < h; y++) Array.Copy(cell.Argb, y * cell.Width, o.Argb, y * o.Width + x, cell.Width);
        x += piece.Width;
    }
    Write(o, name, $"{c[0].Width} 0 {c[2].Width} 0");
}

void Sheet(string file, Action<RgbaImage, List<(int X, int Y, int W, int H)>> use)
{
    Say($"\n== {file}");
    var img = Load(file);
    var (fake, text) = Alpha(img);
    Say("  " + text);
    if (fake) { img = KeyOut(img); Say("  keyed out: " + Alpha(img).Text); }
    Say("  " + Halo(img));
    var p = Pieces(img);
    Say($"  pieces: {p.Count}: " + string.Join(" ", p.Select(q => $"({q.X},{q.Y} {q.W}x{q.H})")));
    use(img, p);
}

NineSlice("card-parchment-9slice", "card-left");
NineSlice("settings-panel-9slice", "card-settings");
Sheet("fleuron-rule-and-dropcap", (img, p) =>
{
    if (p.Count != 4) { Say("  => expected 4 pieces"); return; }
    Write(Crop(img, p[0]), "rule-left");
    Write(Crop(img, p[1]), "fleuron");
    Write(Crop(img, p[2]), "rule-right");
    Write(Crop(img, p[3]), "dropcap");
});
Sheet("section-icons", (img, p) =>
{
    if (p.Count != 5) { Say("  => expected 5 pieces"); return; }
    string[] names = ["icon-game", "icon-display", "icon-sound", "icon-startup", "icon-link"];
    for (var i = 0; i < 5; i++) Write(Crop(img, p[i]), names[i]);
});
Sheet("plate-dark-3states", (img, p) =>
{
    if (p.Count != 9) { Say("  => expected 9 pieces"); return; }
    string[] names = ["plate-small", "plate-small-hover", "plate-small-pressed"];
    for (var row = 0; row < 3; row++) { Say($"  row {row}:"); ThreeSlice(img, p.Skip(row * 3).Take(3).ToList(), names[row]); }
});
Sheet("plate-red-play", (img, p) => { if (p.Count == 3) ThreeSlice(img, p, "plate-red"); else Say("  => expected 3 pieces"); });
Sheet("plate-parchment", (img, p) => { if (p.Count == 3) ThreeSlice(img, p, "plate-stone"); else Say("  => expected 3 pieces"); });
Sheet("corner-ornament", (img, p) => { if (p.Count >= 1) Write(Crop(img, p[0]), "corner"); });
{
    Say("\n== background-v2");
    var img = Load("background-v2");
    Say($"  {img.Width}x{img.Height}: {(img.Width >= 2560 ? "enough for 2560-wide windows" : $"enough up to about {img.Width}-wide windows; larger ones are enlarged (2560x1440 wanted)")}");
    Write(img, "background");
}
File.WriteAllText(Path.Combine(root, "report.txt"), report.ToString());
return 0;
