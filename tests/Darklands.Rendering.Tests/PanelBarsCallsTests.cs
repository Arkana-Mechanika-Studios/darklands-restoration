using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Darklands.Content;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The party panel's bars against the original's: the jobs of tools/oracle/panel_calls.py (member-gauge-bars root 0000:1A9C called directly in
/// the emulated original with generated member values, its line and text entries recorded). The recorded calls are drawn on one screen,
/// PartyPanel's bars (or the "???? ???? ????" of an away member) for the same member on another, and the row is compared pixel by pixel.
/// </summary>
public sealed class PanelBarsCallsTests
{
    private const byte Background = 0x10;

    /// <summary>DARKLANDS_PANEL_CALLS = jobs.json.gz; DARKLANDS_PANEL_CALLS_OUT = a folder for results.tsv and the side-by-side pictures.</summary>
    [Fact]
    public void TheRewriteDrawsTheBarsAsTheOriginalDid()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_PANEL_CALLS");
        if (string.IsNullOrEmpty(path) || GameFolder.Files is not { } files)
        {
            return;
        }
        var outFolder = Environment.GetEnvironmentVariable("DARKLANDS_PANEL_CALLS_OUT");
        var (agree, differ) = Run(path, files, outFolder);
        Assert.True(differ == 0, $"{differ} of {agree + differ} jobs differ (see {outFolder}/results.tsv)");
    }

    /// <summary>The jobs kept in the repository (batch 34): the bars must stay as the original's.</summary>
    [Fact]
    public void TheKeptSampleStillAgrees()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TextCalls", "bars.json.gz");
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
        var panel = new PartyPanel(assets);
        var drawBar = typeof(PartyPanel).GetMethod("DrawBar", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lines = new List<string> { "id\tverdict\tdetail" };
        int agree = 0, differ = 0;
        if (outFolder is not null)
        {
            Directory.CreateDirectory(outFolder);
        }
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = job.GetProperty("id").GetString()!;
            if (job.GetProperty("error").ValueKind != JsonValueKind.Null)
            {
                lines.Add($"{id}\tSKIP\tthe original's call stopped: {job.GetProperty("error")}");
                continue;
            }
            var m = job.GetProperty("member");
            var row = m.GetProperty("row").GetInt32();
            var colour = (byte)m.GetProperty("colour").GetInt32();

            var original = NewCanvas();
            foreach (var call in job.GetProperty("calls").EnumerateArray())
            {
                if (call[0].GetString() == "line")
                {
                    Line(original, call[1].GetInt32(), call[2].GetInt32(), call[3].GetInt32(), call[4].GetInt32(), (byte)call[5].GetInt32());
                }
                else
                {
                    var window = call[4];
                    new TextPainter(assets.Font(window[8].GetInt32())).Draw(original, call[1].GetInt32(), call[2].GetInt32(),
                        Convert.FromHexString(call[3].GetString()!), (byte)window[6].GetInt32(), (TextMode)((window[6].GetInt32() >> 8) & 3), window[3].GetInt32());
                }
            }

            var member = new Character { Status = (CharacterStatus)m.GetProperty("status").GetInt32() };
            var attributes = new[] { Attribute.Endurance, Attribute.Strength, Attribute.DivineFavor };
            for (var k = 0; k < 3; k++)
            {
                member.Attributes[(int)attributes[k]] = m.GetProperty("current")[k].GetInt32();
                member.AttributeMaximums[(int)attributes[k]] = m.GetProperty("maximum")[k].GetInt32();
            }
            var rewrite = NewCanvas();
            if (member.Status == CharacterStatus.Away)
            {
                PartyPanel.DrawUnknownBars(rewrite, new TextPainter(assets.Font(1)), row);
            }
            else
            {
                // as PartyPanel.DrawRow: endurance at 0x15 (value at 0x12), strength at 0x23 (0x20), divine favour at 0x30 (0x2D)
                drawBar.Invoke(panel, [rewrite, member, Attribute.Endurance, 0x15, 0x12, row, (byte)9, colour]);
                drawBar.Invoke(panel, [rewrite, member, Attribute.Strength, 0x23, 0x20, row, (byte)2, colour]);
                drawBar.Invoke(panel, [rewrite, member, Attribute.DivineFavor, 0x30, 0x2D, row, (byte)0x0E, colour]);
            }

            var pixels = 0;
            for (var y = 0; y < Canvas.Height; y++)
            {
                for (var x = 0; x < Canvas.Width; x++)
                {
                    pixels += original[x, y] != rewrite[x, y] ? 1 : 0;
                }
            }
            if (pixels == 0)
            {
                agree++;
                lines.Add($"{id}\tAGREE\t");
                continue;
            }
            differ++;
            lines.Add($"{id}\tDIFFER\t{pixels} pixels; member {m}");
            if (outFolder is not null)
            {
                Directory.CreateDirectory(Path.Combine(outFolder, "unexplained"));
                TextLayoutCallsTests.WriteSide(original, rewrite, Path.Combine(outFolder, "unexplained", id + ".png"));
            }
        }
        if (outFolder is not null)
        {
            lines.Add($"# agree {agree}, differ {differ}");
            File.WriteAllLines(Path.Combine(outFolder, "results.tsv"), lines);
        }
        return (agree, differ);
    }

    // the driver's line from (x0, y0) to (x1, y1), both ends drawn (the bars only use vertical ones)
    private static void Line(Canvas canvas, int x0, int y0, int x1, int y1, byte colour)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            canvas[x0, y0] = colour;
            if (x0 == x1 && y0 == y1)
            {
                return;
            }
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static Canvas NewCanvas()
    {
        var canvas = new Canvas(new Screen());
        canvas.Clear(Background);
        return canvas;
    }
}
