using System.IO.Compression;
using System.Text.Json;
using Darklands.Content;
using Darklands.Content.Formats.Graphics;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Simulation;
using Darklands.Simulation.Overland;

namespace Darklands.Battles.Tests;

/// <summary>
/// The travel map's party drawing (R030+0x16C6): the terrain lift, the rows drawn over the sprite, and (with DARKLANDS_MAPDRAW_JOBS, the
/// output of tools/oracle/map_draw.py) the original's drawing calls replayed through a model of the graphics driver and compared pixel for
/// pixel with <see cref="OverlandPage"/>.
/// </summary>
public sealed class OverlandDrawingTests
{
    [Fact]
    public void TheLiftTablesAreTheOriginals()
    {
        // the three 4 x 16 tables lie one after the other in the data (38DB, 391B, 395B)
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var exe = files.Read("DARKLAND.EXE");
        var all = OverlandPage.Lift20To23.Concat(OverlandPage.Lift16To19).Concat(OverlandPage.LiftRiverMarshFord).Select(v => (byte)v).ToArray();
        Assert.True(exe.AsSpan().IndexOf(all) >= 0, "the 192 bytes of the three tables are not in DARKLAND.EXE");
    }

    [Theory]
    // terrain, fy, fx, lift
    [InlineData(16, 1, 4, 3)]       // 16..19: the table's highest point
    [InlineData(19, 0, 0, 0)]
    [InlineData(20, 1, 8, 7)]       // 20..23: up to 7
    [InlineData(23, 2, 7, 4)]
    [InlineData(3, 1, 4, -3)]       // the minor river, the tidal marsh (4) and the ford (25) lower the party
    [InlineData(4, 2, 2, -2)]
    [InlineData(25, 0, 2, -1)]
    [InlineData(1, 1, 4, 0)]        // the ocean and the major river (1, 2) do not
    [InlineData(24, 1, 8, 0)]
    [InlineData(28, 1, 8, 0)]       // a place icon counts as 28
    public void TheLiftFollowsTheTerrainAndThePixelInTheCell(int terrain, int fy, int fx, int lift) =>
        Assert.Equal(lift, OverlandPage.Lift(terrain, fy, fx));

    [Theory]
    // terrain, fy, the rows (relative to the party's) after which the sprite is drawn
    [InlineData(5, 0, new[] { 0 })]         // up to 9: after its own row (the three rows below cover it)
    [InlineData(5, 3, new[] { 0 })]
    [InlineData(9, 1, new[] { -1, 0 })]     // 9: twice
    [InlineData(12, 2, new[] { -1 })]       // 10..14: after the row above (its own row covers it too)
    [InlineData(16, 0, new[] { -1 })]       // 16..19 on the cell's top line: after the row above
    [InlineData(16, 2, new[] { -1, 0 })]    // 16..19 lower in the cell: twice
    [InlineData(21, 0, new[] { -1 })]       // 20 and up on the top line: after the row above
    [InlineData(21, 3, new[] { 0 })]
    [InlineData(28, 1, new[] { 0 })]
    public void TheSpriteIsDrawnAfterTheRowsTheTerrainNames(int terrain, int fy, int[] rows)
    {
        var drawn = Enumerable.Range(-6, 10).Where(r => OverlandPage.DrawnAfter(20, 20 + r, terrain, fy)).ToArray();
        Assert.Equal(rows, drawn);
    }

    [Fact]
    public void AfterTheBottomJumpTheCellsAreTheOldTopsRowsAsTheStepReadThem()
    {
        // the scroll block's bottom jump (R030+0x1133..0x1154) sets DS:286E = 800 without reading the rows: the buffer keeps the rows of the
        // old top 796. The original's step R030+0x1300 called after its own 20d2 for the view (column 200, DS:286C 60) left DS:D843 = 14, 15
        // and 11 at these markers (local/batch/mapedge/step_east.py), where the map's own cells are 15, 16 and 14.
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), []);
        map.Rows.Open(796);
        map.Buffer.Fill(200, 800, 60);
        foreach (var (x, y, original, own) in new[] { (20, 110, 14, 15), (31, 72, 15, 16), (156, 96, 11, 14) })
        {
            var (row, column) = CellBuffer.MarkerCell(x, y, 800, 60);
            Assert.Equal(original, map.Buffer[row, column]);
            var absolute = WorldMap.CellAt(x + 200 * WorldMap.TileWidth, y + (800 + 60) * WorldMap.RowHeight);
            Assert.Equal(own, map.TerrainCell(absolute.Column, absolute.Row));
        }
    }

    [Fact]
    public void TheOriginalsDrawingCallsGiveTheSamePage()
    {
        if (GameFolder.Files is not { } files || Environment.GetEnvironmentVariable("DARKLANDS_MAPDRAW_JOBS") is not { Length: > 0 } path)
        {
            return;
        }
        var assets = new GameAssets(files);
        var world = WorldData.Load(files);
        var terrain = Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP"));
        var tiles = new OverlandTiles(assets.Picture("mapicons").Image, assets.Picture("mapicon2").Image, assets.Picture("charicon").Image);
        // several job files can be given, separated by ';' (the sweep of map_draw.py, and its --border views at the map's west / north border)
        var files_ = path.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var docs = files_.Select(f => JsonDocument.Parse(new GZipStream(File.OpenRead(f), CompressionMode.Decompress))).ToArray();
        // DARKLANDS_MAPDRAW_RESULTS: a file with one line per job, "map_draw|<job file's name up to its first dot>#<job's number in it>" and its verdict
        // (agree / DIFFER / SKIP), the names of the coverage lines map_draw.py --cov writes: only the agreeing jobs count as coverage
        var verdicts = new List<string>();
        // the place icons come from the state's saved game (R030+0x1C76 lists its places); a world map per job (each job starts from the
        // state's machine: the row buffer read afresh, the bytes after the cell buffer as the state had them, zeros in every state)
        var places = new Dictionary<string, IReadOnlyList<Core.World.Location>>();
        foreach (var d in docs)
        {
            foreach (var s in d.RootElement.GetProperty("saves").EnumerateObject())
            {
                places[s.Name] = Persistence.OriginalSaves.Read(Convert.FromBase64String(s.Value.GetString()!), [], "dksave1.sav", world).State.Locations;
            }
        }
        var outFolder = Environment.GetEnvironmentVariable("DARKLANDS_MAPDRAW_OUT");
        var failures = new List<string>();
        var compared = 0;
        var cellsCompared = 0;
        var cellsDiffer = 0;
        var pastBlock = 0;
        var index = 0;
        var jobNames = docs.Select((d, k) => (d, k)).SelectMany(p => Enumerable.Range(0, p.d.RootElement.GetProperty("jobs").GetArrayLength())
            .Select(n => $"map_draw|{Path.GetFileName(files_[p.k]).Split('.')[0]}#{n}")).ToArray();
        foreach (var job in docs.SelectMany(d => d.RootElement.GetProperty("jobs").EnumerateArray()))
        {
            var failuresBefore = failures.Count;
            var calls = job.GetProperty("calls").EnumerateArray().ToArray();
            var marks = job.GetProperty("marks").EnumerateArray().Select(m => m.GetInt32()).ToArray();
            var steps = job.GetProperty("path").EnumerateArray().Select(p => p.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
            var column = job.GetProperty("column").GetInt32();
            var firstRow = job.GetProperty("top").GetInt32() + job.GetProperty("rows").GetInt32();
            var background = (byte)job.GetProperty("background").GetInt32();
            var map = new WorldMap(terrain, places[job.GetProperty("state").GetString()!]);
            var top = job.GetProperty("top").GetInt32();
            // the row buffer as the job read it: the state's own rows (its slot tails: zeros in every state), then the job's
            // R030+0x1DD4(file, new, old) calls (old -1: all rows), else the view's rows afresh when its top is another
            var stateTop = job.TryGetProperty("state_top", out var st) ? st.GetInt32() : top;
            map.Rows.Open(stateTop);
            if (job.TryGetProperty("loads", out var loads))
            {
                foreach (var load in loads.EnumerateArray())
                {
                    map.Rows.Load(load[0].GetInt32(), load[1].GetInt32());
                }
            }
            else if (top != stateTop)
            {
                map.Rows.Load(top, -1);
            }
            // a view whose decoder runs past the row buffer's block reads the memory after it (RowBuffer.Decode's divergence): not compared
            if (job.TryGetProperty("past_block", out var past) && past.GetBoolean())
            {
                pastBlock++;
                verdicts.Add(jobNames[index] + "	SKIP");
                index++;
                continue;
            }
            var driver = new DriverModel(tiles, assets);
            var page = new OverlandPage();
            // the first 16c6 call begins where 20d2's calls end: its fill of the window DS:282A
            var mapCalls = Array.FindIndex(calls, c => c[0].GetString() == "fill" && c[1][1].GetInt32() == 280);
            var clock = calls.Take(mapCalls).Where(c => c[0].GetString() == "picture" && c[4][0].GetString() == "clock").Select(c => c[4][1].GetInt32()).Last();
            page.DrawMap(map, tiles, column, top, job.GetProperty("rows").GetInt32(), background, assets.Picture("mapheada").Image, assets.Picture($"mapbrs{clock * 3:00}").Image);
            // the cell buffer itself (and the 70 bytes after it, when the job has them) against the original's after its 20d2
            var original = job.GetProperty("cells").EnumerateArray().Select(v => (byte)v.GetInt32()).ToList();
            if (job.TryGetProperty("after", out var after))
            {
                original.AddRange(after.EnumerateArray().Select(v => (byte)v.GetInt32()));
            }
            var cellDiffer = Enumerable.Range(0, original.Count).Where(i => map.Buffer[i] != original[i]).ToArray();
            cellsCompared++;
            if (cellDiffer.Length > 0)
            {
                cellsDiffer++;
                failures.Add($"job {index} ({job.GetProperty("state").GetString()} view {column},{firstRow}): {cellDiffer.Length} cells differ, first G[{cellDiffer[0] / 19}][{cellDiffer[0] % 19}] original {original[cellDiffer[0]]} rewrite {map.Buffer[cellDiffer[0]]}");
            }
            var done = 0;
            for (var k = 0; k < steps.Length; k++)
            {
                for (; done < marks[k]; done++)
                {
                    driver.Run(calls[done]);
                }
                var (x, y, frame, direction) = (steps[k][0], steps[k][1], steps[k][2], steps[k][3]);
                page.DrawParty(tiles, firstRow, x, y, direction, frame, background);
                compared++;
                var differ = Differences(driver.Pages[0], page.Screen.Pixels);
                if (differ.Count > 0)
                {
                    failures.Add($"job {index} ({job.GetProperty("state").GetString()} view {column},{firstRow} {job.GetProperty("kind").GetString()} step {k} marker {x},{y} terrain {job.GetProperty("terrain").GetInt32()}): {differ.Count} pixels, first at {differ[0] % Screen.Width},{differ[0] / Screen.Width}");
                }
            }
            if (outFolder is { Length: > 0 } && index < 400)
            {
                Directory.CreateDirectory(outFolder);
                File.WriteAllBytes(Path.Combine(outFolder, $"job{index:000}-orig.bin"), driver.Pages[0]);
                File.WriteAllBytes(Path.Combine(outFolder, $"job{index:000}-cs.bin"), page.Screen.Pixels);
            }
            verdicts.Add(jobNames[index] + (failures.Count == failuresBefore ? "	agree" : "	DIFFER"));
            index++;
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_MAPDRAW_RESULTS") is { Length: > 0 } results)
        {
            File.WriteAllLines(results, verdicts);
        }
        Console.WriteLine($"mapdraw: {compared} frames and {cellsCompared} cell buffers compared, {pastBlock} jobs past the row buffer's block left out; {failures.Count - cellsDiffer} frames and {cellsDiffer} cell buffers differ");
        Assert.True(compared > 0, "no job");
        Assert.True(failures.Count == 0, $"{failures.Count} of {compared} frames / {cellsCompared} cell buffers differ:\n" + string.Join("\n", failures.Take(30)));
    }

    private static List<int> Differences(byte[] original, byte[] rewrite)
    {
        var differ = new List<int>();
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = OverlandPage.Left; x < Screen.Width; x++)
            {
                if (original[y * Screen.Width + x] != rewrite[y * Screen.Width + x])
                {
                    differ.Add(y * Screen.Width + x);
                }
            }
        }
        return differ;
    }

    /// <summary>
    /// The driver's primitives as MGRAPHIC.EXE does them (a tester model, from its code): two pages of 320 x 200 bytes addressed through
    /// the row table at 1816 (rows 200..204 give offsets 0, 1, 0, 0, 0), a window = (page, x, y, last column, last row).
    /// </summary>
    private sealed class DriverModel(OverlandTiles tiles, GameAssets assets)
    {
        public byte[][] Pages { get; } = [new byte[Screen.Width * Screen.Height], new byte[Screen.Width * Screen.Height]];

        private static int Row(int y) => y switch
        {
            >= 0 and < 200 => y * Screen.Width,
            201 => 1,
            >= 200 and <= 204 => 0,
            _ => -1,
        };

        private IndexedImage Picture(JsonElement p) => p[0].GetString() switch
        {
            "tile" => tiles.Tile(p[1].GetInt32(), p[2].GetInt32()),
            "party" => tiles.Party(p[1].GetInt32(), p[2].GetInt32()),
            "header" => assets.Picture("mapheada").Image,
            "clock" => assets.Picture($"mapbrs{p[1].GetInt32() * 3:00}").Image,
            var other => throw new InvalidOperationException("picture " + other),
        };

        public void Run(JsonElement call)
        {
            int I(int k) => call[k].GetInt32();
            int[] W(int k) => call[k].EnumerateArray().Select(v => v.GetInt32()).ToArray();
            switch (call[0].GetString())
            {
                case "fill_page":
                    Array.Fill(Pages[I(1)], (byte)I(2));
                    break;
                case "fill":
                {
                    var w = W(1);
                    for (var j = 0; j < I(5); j++)
                    {
                        Put(w[0], Row(w[2] + I(3) + j), w[1] + I(2), I(4), _ => (byte)I(6));
                    }
                    break;
                }
                case "sprite":
                case "picture":
                {
                    // entry 6 clips to the window's columns 0..last and rows 0..last; entry 32 does not clip
                    var w = W(1);
                    var clip = call[0].GetString() == "sprite";
                    var image = Picture(call[4]);
                    for (var j = 0; j < image.Height; j++)
                    {
                        var y = I(3) + j;
                        if (clip && (y < 0 || y > w[4]))
                        {
                            continue;
                        }
                        for (var i = 0; i < image.Width; i++)
                        {
                            var x = I(2) + i;
                            var color = image.Pixels[j * image.Width + i];
                            if (color == 0 || (clip && (x < 0 || x > w[3])))
                            {
                                continue;
                            }
                            Put(w[0], Row(w[2] + y), w[1] + x, 1, _ => color);
                        }
                    }
                    break;
                }
                case "copy":
                {
                    var (s, d) = (W(1), W(6));
                    for (var j = 0; j < I(5); j++)
                    {
                        var from = Row(s[2] + I(3) + j);
                        var to = Row(d[2] + I(8) + j);
                        if (from < 0 || to < 0)
                        {
                            continue;
                        }
                        var source = Pages[s[0]];
                        var start = from + s[1] + I(2);
                        Put(d[0], to, d[1] + I(7), I(4), i => start + i < source.Length ? source[start + i] : (byte)0);
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException("call " + call[0].GetString());
            }
        }

        private void Put(int page, int row, int x, int count, Func<int, byte> color)
        {
            if (row < 0)
            {
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var at = row + x + i;
                if (at >= 0 && at < Pages[page].Length)
                {
                    Pages[page][at] = color(i);
                }
            }
        }
    }
}
