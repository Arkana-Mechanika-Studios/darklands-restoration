// The launcher's card papers (tools/launcher-art/ASSETS.md), run by hand:
//   card: the left card's paper lifted from the art direction's mock-up (a PNG of it), its text, drop caps and rules
//     painted out (a smooth fill plus the paper's grain from nearby); writes card-left.png (828 x 1016) and its slice.
//     The crop and the drop caps' places are those of the 2026-11-08 mock-up.
//   settings: the settings panel's art without its dark header band (the bottom row, flipped, becomes the top row).
//   tone: the settings panel's paper brought to the card's colour (each channel's mean and spread), its gold frame kept.
//   PaperTool card <mockup.png> <out dir> [x0 y0 x1 y1]
//   PaperTool settings <card-settings.png> <slice.txt> <out dir>
//   PaperTool tone <card-settings.png> <card-left-mockup-size.png> <out.png>
using Darklands.Rendering;

static int L(uint c) => (int)((((c >> 16) & 255) * 30 + ((c >> 8) & 255) * 59 + (c & 255) * 11) / 100);
static int Sat(uint c) { int r = (int)(c >> 16 & 255), g = (int)(c >> 8 & 255), b = (int)(c & 255); return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)); }
static uint Rgb(double r, double g, double b, int a = 255) => (uint)a << 24 | (uint)Math.Clamp((int)Math.Round(r), 0, 255) << 16 | (uint)Math.Clamp((int)Math.Round(g), 0, 255) << 8 | (uint)Math.Clamp((int)Math.Round(b), 0, 255);

if (args[0] == "settings")
{
    var art = PngCodec.Read(args[1]);
    var s = File.ReadAllText(args[2]).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    int top = s[1], bottom = s[3];
    var h = art.Height - top + bottom;
    var o = new RgbaImage(art.Width, h);
    for (var y = 0; y < bottom; y++)
        Array.Copy(art.Argb, (art.Height - 1 - y) * art.Width, o.Argb, y * art.Width, art.Width);
    Array.Copy(art.Argb, top * art.Width, o.Argb, bottom * art.Width, (art.Height - top) * art.Width);
    Directory.CreateDirectory(args[3]);
    PngCodec.Write(o, Path.Combine(args[3], "card-settings.png"));
    File.WriteAllText(Path.Combine(args[3], "card-settings.slice.txt"), $"{s[0]} {bottom} {s[2]} {bottom}\n");
    Console.WriteLine($"settings {art.Width}x{art.Height} -> {o.Width}x{o.Height}, slice {s[0]} {bottom} {s[2]} {bottom}");
    return 0;
}

if (args[0] == "tone")
{
    // the settings panel's paper brought to the card's: each channel's mean and spread over the paper, the frame kept
    var art = PngCodec.Read(args[1]);
    var reference = PngCodec.Read(args[2]);
    (double[] Mean, double[] Sd) Stats(RgbaImage im, Func<int, int, bool> use)
    {
        var s = new double[3]; var s2 = new double[3]; var n = 0;
        for (var y = 0; y < im.Height; y++) for (var x = 0; x < im.Width; x++)
        {
            if (!use(x, y)) continue; var c = im.Argb[y * im.Width + x];
            for (var k = 0; k < 3; k++) { double v = c >> (16 - 8 * k) & 255; s[k] += v; s2[k] += v * v; }
            n++;
        }
        var m = s.Select(v => v / n).ToArray();
        return (m, s2.Select((v, k) => Math.Sqrt(v / n - m[k] * m[k])).ToArray());
    }
    var a = Stats(art, (x, y) => x > art.Width / 4 && x < art.Width * 3 / 4 && y > art.Height / 4 && y < art.Height * 3 / 4);
    var r = Stats(reference, (x, y) => x > reference.Width / 4 && x < reference.Width * 3 / 4 && y > reference.Height / 6 && y < reference.Height * 5 / 6);
    Console.WriteLine($"settings paper {string.Join(",", a.Mean.Select(v => v.ToString("F0")))} sd {string.Join(",", a.Sd.Select(v => v.ToString("F1")))}; card {string.Join(",", r.Mean.Select(v => v.ToString("F0")))} sd {string.Join(",", r.Sd.Select(v => v.ToString("F1")))}");
    var o = new RgbaImage(art.Width, art.Height);
    for (var i = 0; i < art.Argb.Length; i++)
    {
        var c = art.Argb[i];
        double[] v = [c >> 16 & 255, c >> 8 & 255, c & 255];
        var d = Math.Abs(v[0] - a.Mean[0]) + Math.Abs(v[1] - a.Mean[1]) + Math.Abs(v[2] - a.Mean[2]);
        var w = Math.Clamp((140 - d) / 70, 0, 1); // paper: near its mean; the gold frame and its shadows: far
        var t = new double[3];
        for (var k = 0; k < 3; k++) t[k] = v[k] + w * ((v[k] - a.Mean[k]) * Math.Min(1.6, r.Sd[k] / a.Sd[k]) + r.Mean[k] - v[k]);
        o.Argb[i] = c & 0xFF000000 | Rgb(t[0], t[1], t[2], 0) & 0xFFFFFF;
    }
    PngCodec.Write(o, args[3]);
    return 0;
}

var img = PngCodec.Read(args[1]);
var outDir = args[2];
Directory.CreateDirectory(outDir);
int X0 = 180, Y0 = 262, X1 = 700, Y1 = 918;
if (args.Length >= 7) { X0 = int.Parse(args[3]); Y0 = int.Parse(args[4]); X1 = int.Parse(args[5]); Y1 = int.Parse(args[6]); }
int W = X1 - X0, H = Y1 - Y0;
var px = new uint[W * H];
for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) px[y * W + x] = img.Argb[(Y0 + y) * img.Width + X0 + x];

// 1. the background: reached from the crop's border through dark pixels
const int Dark = 118;
var bg = new bool[W * H];
var q = new Queue<int>();
void Seed(int i) { if (!bg[i] && L(px[i]) < Dark) { bg[i] = true; q.Enqueue(i); } }
for (var x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
for (var y = 0; y < H; y++) { Seed(y * W); Seed(y * W + W - 1); }
while (q.Count > 0)
{
    var i = q.Dequeue(); int x = i % W, y = i / W;
    if (x > 0) Seed(i - 1); if (x < W - 1) Seed(i + 1); if (y > 0) Seed(i - W); if (y < H - 1) Seed(i + W);
}
// only the card: the largest light piece (light bits of the painting at the crop's border are not paper)
{
    var label = new int[W * H];
    int best = 0, bestSize = 0, next = 0;
    for (var s0 = 0; s0 < W * H; s0++)
    {
        if (bg[s0] || label[s0] != 0) continue;
        next++; var size = 0; label[s0] = next; q.Enqueue(s0);
        while (q.Count > 0)
        {
            var i = q.Dequeue(); size++; int x = i % W, y = i / W;
            foreach (var j in new[] { x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < H - 1 ? i + W : -1 })
                if (j >= 0 && !bg[j] && label[j] == 0) { label[j] = next; q.Enqueue(j); }
        }
        if (size > bestSize) { bestSize = size; best = next; }
    }
    for (var i = 0; i < W * H; i++) if (label[i] != best) bg[i] = true;
}
int bx0 = W, by0 = H, bx1 = 0, by1 = 0;
for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) if (!bg[y * W + x]) { bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y); }
Console.WriteLine($"card in the crop: {bx0},{by0} .. {bx1},{by1} ({bx1 - bx0 + 1}x{by1 - by0 + 1}); in the mock-up {X0 + bx0},{Y0 + by0}");

// 2. the paper's local brightness (75th percentile per 20 px block), then ink: darker than it, or coloured
const int Block = 20;
int gw = (W + Block - 1) / Block, gh = (H + Block - 1) / Block;
var level = new double[gw * gh];
for (var gy = 0; gy < gh; gy++)
    for (var gx = 0; gx < gw; gx++)
    {
        var v = new List<int>();
        for (var y = gy * Block; y < Math.Min(H, gy * Block + Block); y++)
            for (var x = gx * Block; x < Math.Min(W, gx * Block + Block); x++)
                if (!bg[y * W + x]) v.Add(L(px[y * W + x]));
        v.Sort();
        level[gy * gw + gx] = v.Count < 20 ? -1 : v[v.Count * 3 / 4];
    }
double Level(int x, int y)
{
    double fx = Math.Clamp((x - Block / 2.0) / Block, 0, gw - 1), fy = Math.Clamp((y - Block / 2.0) / Block, 0, gh - 1);
    int ix = (int)fx, iy = (int)fy; double ax = fx - ix, ay = fy - iy;
    double sum = 0, wsum = 0;
    for (var j = 0; j < 2; j++) for (var k = 0; k < 2; k++)
    {
        int cx = Math.Min(gw - 1, ix + k), cy = Math.Min(gh - 1, iy + j);
        var l = level[cy * gw + cx]; if (l < 0) continue;
        var w = (k == 0 ? 1 - ax : ax) * (j == 0 ? 1 - ay : ay) + 1e-6;
        sum += l * w; wsum += w;
    }
    return wsum > 0 ? sum / wsum : 200;
}
var ink = new bool[W * H];
for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        var i = y * W + x; if (bg[i]) continue;
        var c = px[i];
        int r = (int)(c >> 16 & 255), g = (int)(c >> 8 & 255);
        ink[i] = L(c) < Level(x, y) - 24 || r - g > 42 || Sat(c) > 85;
    }
// ink near the card's edge is its burnt rim, not text: keep it
var edgeDist = new int[W * H];
Array.Fill(edgeDist, int.MaxValue);
for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) if (bg[y * W + x]) edgeDist[y * W + x] = 0;
for (var pass = 0; pass < 2; pass++)
    for (var y = 0; y < H; y++) for (var x = 0; x < W; x++)
    {
        int xx = pass == 0 ? x : W - 1 - x, yy = pass == 0 ? y : H - 1 - y, i = yy * W + xx, d = edgeDist[i];
        int step = pass == 0 ? -1 : 1;
        if (xx + step >= 0 && xx + step < W && edgeDist[i + step] != int.MaxValue) d = Math.Min(d, edgeDist[i + step] + 1);
        if (yy + step >= 0 && yy + step < H && edgeDist[i + step * W] != int.MaxValue) d = Math.Min(d, edgeDist[i + step * W] + 1);
        edgeDist[i] = d;
    }
const int Rim = 16;
var hole = new bool[W * H];
for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        if (!ink[y * W + x] || edgeDist[y * W + x] <= Rim) continue;
        for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++)
        {
            int xx = x + dx, yy = y + dy;
            if (xx >= 0 && yy >= 0 && xx < W && yy < H && !bg[yy * W + xx] && edgeDist[yy * W + xx] > Rim / 2) hole[yy * W + xx] = true;
        }
    }
// the two drop caps, whole (their light rims are not ink by colour)
foreach (var (rx, ry) in new[] { (53, 45), (53, 230) })
    for (var y = ry - 7; y < ry + 79; y++) for (var x = rx - 7; x < rx + 79; x++) if (!bg[y * W + x]) hole[y * W + x] = true;
Console.WriteLine($"ink painted out: {hole.Count(b => b) * 100.0 / bg.Count(b => !b):F1}% of the card");

// 3. a smooth fill of the holes (push-pull over a pyramid of the known pixels)
double[][] Fill(double[] val, bool[] known, int w, int h)
{
    if (w <= 2 || h <= 2) { var m = 0.0; var n = 0; for (var i = 0; i < val.Length; i++) if (known[i]) { m += val[i]; n++; } var o = new double[val.Length]; Array.Fill(o, n > 0 ? m / n : 200); return [o]; }
    int w2 = (w + 1) / 2, h2 = (h + 1) / 2;
    var v2 = new double[w2 * h2]; var k2 = new bool[w2 * h2];
    for (var y = 0; y < h2; y++) for (var x = 0; x < w2; x++)
    {
        double s = 0; var n = 0;
        for (var j = 0; j < 2; j++) for (var k = 0; k < 2; k++) { int xx = 2 * x + k, yy = 2 * y + j; if (xx < w && yy < h && known[yy * w + xx]) { s += val[yy * w + xx]; n++; } }
        if (n > 0) { v2[y * w2 + x] = s / n; k2[y * w2 + x] = true; }
    }
    var coarse = Fill(v2, k2, w2, h2)[0];
    var outv = new double[w * h];
    for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
    {
        var i = y * w + x;
        if (known[i]) { outv[i] = val[i]; continue; }
        double fx = Math.Clamp((x - 0.5) / 2, 0, w2 - 1), fy = Math.Clamp((y - 0.5) / 2, 0, h2 - 1);
        int ix = (int)fx, iy = (int)fy; double ax = fx - ix, ay = fy - iy;
        int ix1 = Math.Min(w2 - 1, ix + 1), iy1 = Math.Min(h2 - 1, iy + 1);
        outv[i] = coarse[iy * w2 + ix] * (1 - ax) * (1 - ay) + coarse[iy * w2 + ix1] * ax * (1 - ay) + coarse[iy1 * w2 + ix] * (1 - ax) * ay + coarse[iy1 * w2 + ix1] * ax * ay;
    }
    return [outv];
}
var knownMask = new bool[W * H];
for (var i = 0; i < W * H; i++) knownMask[i] = !bg[i] && !hole[i];
var ch = new double[3][];
for (var c = 0; c < 3; c++)
{
    var vals = new double[W * H];
    for (var i = 0; i < W * H; i++) vals[i] = px[i] >> (16 - 8 * c) & 255;
    ch[c] = Fill(vals, knownMask, W, H)[0];
}
// 4. the paper's grain in the holes: the fine detail (pixel minus its 7 x 7 mean) of a known pixel nearby
var blur = new double[3][];
for (var c = 0; c < 3; c++)
{
    blur[c] = new double[W * H];
    for (var y = 0; y < H; y++) for (var x = 0; x < W; x++)
    {
        double s = 0; var n = 0;
        for (var dy = -3; dy <= 3; dy++) for (var dx = -3; dx <= 3; dx++) { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < W && yy < H) { s += ch[c][yy * W + xx]; n++; } }
        blur[c][y * W + x] = s / n;
    }
}
(int, int)[] offsets = [(0, -26), (0, 26), (0, -52), (0, 52), (-37, 0), (37, 0), (-37, -26), (37, 26), (0, -78), (0, 78), (-74, 0), (74, 0), (0, -104), (0, 104)];
var result = new uint[W * H];
var rng = new Random(7);
for (var y = 0; y < H; y++)
    for (var x = 0; x < W; x++)
    {
        var i = y * W + x;
        if (bg[i]) { result[i] = 0; continue; }
        if (!hole[i]) { result[i] = px[i] | 0xFF000000; continue; }
        double gr = 0, gg = 0, gb = 0;
        foreach (var (dx, dy) in offsets)
        {
            int xx = x + dx, yy = y + dy;
            if (xx < 0 || yy < 0 || xx >= W || yy >= H || !knownMask[yy * W + xx]) continue;
            var j = yy * W + xx;
            gr = Math.Clamp(ch[0][j] - blur[0][j], -14, 14); gg = Math.Clamp(ch[1][j] - blur[1][j], -14, 14); gb = Math.Clamp(ch[2][j] - blur[2][j], -14, 14);
            break;
        }
        result[i] = Rgb(blur[0][i] + gr, blur[1][i] + gg, blur[2][i] + gb);
    }
// soft edge: the outermost paper pixels half see-through
for (var y = 1; y < H - 1; y++)
    for (var x = 1; x < W - 1; x++)
    {
        var i = y * W + x;
        if (bg[i]) continue;
        var n = (bg[i - 1] ? 1 : 0) + (bg[i + 1] ? 1 : 0) + (bg[i - W] ? 1 : 0) + (bg[i + W] ? 1 : 0);
        if (n > 0) result[i] = result[i] & 0xFFFFFF | (uint)(255 - 50 * n) << 24;
    }
// 5. cropped to the card, written at the mock-up's size, and enlarged to twice the design (414 x 508 -> 828 x 1016)
int cw = bx1 - bx0 + 1, chh = by1 - by0 + 1;
var card = new RgbaImage(cw, chh);
for (var y = 0; y < chh; y++) Array.Copy(result, (by0 + y) * W + bx0, card.Argb, y * cw, cw);
PngCodec.Write(card, Path.Combine(outDir, "card-left-mockup-size.png"));
var mask = new RgbaImage(W, H);
for (var i = 0; i < W * H; i++) mask.Argb[i] = bg[i] ? 0xFF000000 : hole[i] ? 0xFFFF0000 : px[i] | 0xFF000000;
PngCodec.Write(mask, Path.Combine(outDir, "card-left-mask.png"));
const int TW = 828, TH = 1016;
var big = new RgbaImage(TW, TH);
for (var y = 0; y < TH; y++)
    for (var x = 0; x < TW; x++)
    {
        double fx = (x + 0.5) * cw / TW - 0.5, fy = (y + 0.5) * chh / TH - 0.5;
        int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy); double ax = fx - ix, ay = fy - iy;
        double a = 0, r = 0, g = 0, b = 0;
        for (var j = 0; j < 2; j++) for (var k = 0; k < 2; k++)
        {
            int xx = Math.Clamp(ix + k, 0, cw - 1), yy = Math.Clamp(iy + j, 0, chh - 1);
            var c = card.Argb[yy * cw + xx]; var w = (k == 0 ? 1 - ax : ax) * (j == 0 ? 1 - ay : ay);
            var al = (c >> 24) * w; a += al; r += (c >> 16 & 255) * al; g += (c >> 8 & 255) * al; b += (c & 255) * al;
        }
        big.Argb[y * TW + x] = a < 0.5 ? 0 : Rgb(r / a, g / a, b / a, (int)Math.Round(a));
    }
PngCodec.Write(big, Path.Combine(outDir, "card-left.png"));
File.WriteAllText(Path.Combine(outDir, "card-left.slice.txt"), "120 120 120 120\n");
Console.WriteLine($"card-left.png {TW}x{TH} from {cw}x{chh}");
return 0;
