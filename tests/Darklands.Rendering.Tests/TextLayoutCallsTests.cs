using System.IO.Compression;
using System.Text.Json;
using Darklands.Cards;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The card text layout against the original's own: the jobs of tools/oracle/text_calls.py (text-paint-wrapped root 0265:0134 and
/// wrapped-text-size root 0265:0654 called directly in the emulated original from a card-screen state, with the driver's text and picture
/// entries recorded). Each job's recorded calls are drawn with the rewrite's glyph painter on one screen, the same input goes through
/// <see cref="CardTextLayout"/> on another, and the two are compared pixel by pixel, with the hot-spot table and the measured height.
/// </summary>
// C: the glyphs are drawn by the same painter on both sides (the emulator has no graphics driver), so what is compared is the layout: where
//   each piece of text goes, which bytes it holds, its colour and mode, the drop cap, the option areas, the height; not the glyph shapes.
public sealed class TextLayoutCallsTests
{
    private const byte Background = 0x10;

    /// <summary>DARKLANDS_TEXT_CALLS = jobs.json.gz; DARKLANDS_TEXT_CALLS_OUT = a folder for results.tsv and the side-by-side pictures.</summary>
    [Fact]
    public void TheRewriteLaysOutTheTextsAsTheOriginalDid()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_TEXT_CALLS");
        if (string.IsNullOrEmpty(path) || GameFolder.Files is not { } files)
        {
            return;
        }
        var outFolder = Environment.GetEnvironmentVariable("DARKLANDS_TEXT_CALLS_OUT");
        var (agree, differ) = Run(path, files, outFolder);
        Assert.True(differ == 0, $"{differ} of {agree + differ} jobs differ (see {outFolder}/results.tsv)");
    }

    /// <summary>The jobs kept in the repository (a sample that agreed, batch 34): the layout must stay as the original's.</summary>
    [Fact]
    public void TheKeptSampleStillAgrees()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TextCalls", "sample.json.gz");
        if (!File.Exists(path) || GameFolder.Files is not { } files)
        {
            return;
        }
        var (agree, differ) = Run(path, files, null);
        Assert.True(agree > 0);
        Assert.Equal(0, differ);
    }

    internal static (int Agree, int Differ) Run(string path, GameFiles files, string? outFolder)
    {
        using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(stream);
        var assets = new GameAssets(files);
        var lines = new List<string> { "id\tname\tverdict\tdetail" };
        int agree = 0, differ = 0, explained = 0;
        if (outFolder is not null)
        {
            Directory.CreateDirectory(outFolder);
        }
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            var name = job.GetProperty("name").GetString()!;
            var paint = job.GetProperty("paint");
            if (paint.GetProperty("error").ValueKind != JsonValueKind.Null || job.GetProperty("size").GetProperty("error").ValueKind != JsonValueKind.Null)
            {
                lines.Add($"{id}\t{name}\tSKIP\tthe original's call stopped: {paint.GetProperty("error")} {job.GetProperty("size").GetProperty("error")}");
                continue;
            }
            var result = Compare(job, assets);
            if (result.Problems.Count == 0)
            {
                agree++;
                lines.Add($"{id}\t{name}\tAGREE\t");
                continue;
            }
            if (result.Explained is { } divergence)
            {
                explained++;
                lines.Add($"{id}\t{name}\tEXPLAINED {divergence}\t{string.Join("; ", result.Problems)}");
                if (outFolder is not null && result.Pixels > 0)
                {
                    Directory.CreateDirectory(Path.Combine(outFolder, "explained"));
                    WriteSide(result.Original, result.Rewrite, Path.Combine(outFolder, "explained", $"{divergence}-{id}.png"));
                }
                continue;
            }
            differ++;
            lines.Add($"{id}\t{name}\tDIFFER\t{string.Join("; ", result.Problems)}");
            if (outFolder is not null && result.Pixels > 0)
            {
                Directory.CreateDirectory(Path.Combine(outFolder, "unexplained"));
                WriteSide(result.Original, result.Rewrite, Path.Combine(outFolder, "unexplained", id + ".png"));
            }
        }
        if (outFolder is not null)
        {
            lines.Add($"# agree {agree}, explained by a registered divergence {explained}, differ {differ}");
            File.WriteAllLines(Path.Combine(outFolder, "results.tsv"), lines);
        }
        return (agree, differ);
    }

    internal sealed record Outcome(Canvas Original, Canvas Rewrite, int Pixels, List<string> Problems, string? Explained);

    internal static Outcome Compare(JsonElement job, GameAssets assets)
    {
        var text = Convert.FromHexString(job.GetProperty("text").GetString()!);
        var s = job.GetProperty("settings");
        int I(string k) => s.GetProperty(k).GetInt32();
        var font = assets.Font(I("font"));
        var painter = new TextPainter(font);
        var problems = new List<string>();

        // the original's recorded calls, drawn
        var original = NewCanvas();
        var paint = job.GetProperty("paint");
        foreach (var call in paint.GetProperty("calls").EnumerateArray())
        {
            var kind = call[0].GetString();
            var x = call[1].GetInt32();
            var y = call[2].GetInt32();
            var window = call[4];
            if (kind == "text")
            {
                var colour = window[6].GetInt32();
                var callFont = assets.Font(window[8].GetInt32());
                new TextPainter(callFont).Draw(original, x, y, Convert.FromHexString(call[3].GetString()!), (byte)colour, (TextMode)((colour >> 8) & 3));
            }
            else if (call[3].GetInt32() is var letter and >= 'A' and <= 'Z' && DropCaps.Get(assets, (byte)letter) is { } cap)
            {
                original.Draw(cap, x, y, transparent: true);
            }
            else
            {
                problems.Add($"picture {call[3]} not a drop cap");
            }
        }

        // the same input through the rewrite
        var settings = new CardTextLayout.Settings
        {
            Options = s.GetProperty("options").EnumerateArray().Select(o => (OptionState)o.GetInt32()).ToList(),
            Conditions = s.GetProperty("conditions").EnumerateArray().Select(o => o.GetInt32() != 0).ToList(),
            StopAtFirstOption = I("stop") != 0,
            Leading = I("leading"),
            Spacing = I("spacing"),
            Alignment = (TextAlignment)I("align"),
            DropCap = I("icon") == 1 ? b => DropCaps.Get(assets, b) : null,
            Color = (byte)I("colour"),
            OptionColor = I("option_colour") == 1 ? (byte)0x80 : null,
            OptionMode = TextMode.Xor,
        };
        var rewrite = NewCanvas();
        var layout = new CardTextLayout(painter);
        var areas = layout.Draw(rewrite, text, I("x"), I("y"), I("width"), settings);

        var pixels = 0;
        for (var y = 0; y < Canvas.Height; y++)
        {
            for (var x = 0; x < Canvas.Width; x++)
            {
                if (original[x, y] != rewrite[x, y])
                {
                    pixels++;
                }
            }
        }
        if (pixels > 0)
        {
            problems.Add($"{pixels} pixels");
        }

        // the hot-spot table (one entry per choosable option line; y and height are bytes there)
        var hs = paint.GetProperty("hotspots");
        var count = hs.GetProperty("count").GetInt32();
        var mine = areas.Select(a => $"{a.Option}:{(int)a.Kind}:{a.Y & 0xFF}:{a.Height & 0xFF}").ToList();
        var theirs = Enumerable.Range(0, count).Select(k => $"{hs.GetProperty("index")[k].GetInt32()}:{hs.GetProperty("type")[k].GetInt32()}:{hs.GetProperty("y")[k].GetInt32()}:{hs.GetProperty("height")[k].GetInt32()}").ToList();
        if (!mine.SequenceEqual(theirs))
        {
            problems.Add($"hotspots original [{string.Join(' ', theirs)}] rewrite [{string.Join(' ', mine)}]");
        }

        // wrapped-text-size, made as its callers make it (no stop, leading or drop cap, the font's line height, from y 0 here: the job's y is
        //   added): the high word is the end y minus one. With size_init the call had the table DS:EA64 with the job's option states; without,
        //   the table 0, so enabled[n] is DS:002E + n (hooked at root 0265:07C8: the run-time library's copyright string and what follows it;
        //   a word wider than the column makes the loop read the same marker again and again, one entry further each time).
        var size = job.GetProperty("size");
        var measure = new CardTextLayout(painter);
        var options = I("size_init") != 0 ? settings.Options : NullTable;
        var end = I("y") + measure.MeasureHeight([.. text, .. new byte[64]], I("width"), 0, options);
        if (size.GetProperty("dx").GetInt32() != ((end - 1) & 0xFFFF))
        {
            problems.Add($"height original dx {size.GetProperty("dx").GetInt32()} rewrite end y {end} - 1");
        }
        return new Outcome(original, rewrite, pixels, problems, null);
    }

    /// <summary>DS:002E.. in the captured state (the bytes wrapped-text-size reads as enabled[] when its caller passes no table).</summary>
    private static readonly OptionState[] NullTable =
        [.. System.Text.Encoding.Latin1.GetBytes("90, Microsoft Corp\x18\0dk.tmp\0near a door\0near the st").Select(b => (OptionState)b)];

    private static Canvas NewCanvas()
    {
        var canvas = new Canvas(new Screen());
        canvas.Clear(Background);
        return canvas;
    }

    /// <summary>original | rewrite | the differences in magenta, at twice the size (grey levels of the palette index, which is all a layout needs).</summary>
    internal static void WriteSide(Canvas a, Canvas b, string path)
    {
        const int W = Canvas.Width, H = Canvas.Height;
        var argb = new uint[W * 3 * 2 * H * 2];
        uint Grey(byte c) => c == Background ? 0xFF202830u : 0xFF000000u | (uint)(0x60 + (c * 0x9F / 255)) * 0x010101u;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var pa = a[x, y];
                var pb = b[x, y];
                var cells = new[] { Grey(pa), Grey(pb), pa != pb ? 0xFFFF00FFu : 0xFF000000u };
                for (var k = 0; k < 3; k++)
                {
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            argb[(y * 2 + dy) * W * 6 + k * W * 2 + x * 2 + dx] = cells[k];
                        }
                    }
                }
            }
        }
        PngWriter.Write(argb, W * 6, H * 2, path);
    }
}
