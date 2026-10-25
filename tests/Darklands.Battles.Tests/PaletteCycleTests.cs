using System.IO.Compression;
using System.Text.Json;
using Darklands.Content.Formats.Battles;
using Darklands.Platform;

namespace Darklands.Battles.Tests;

/// <summary>
/// The palette cycles of the timer's frame work (root 047C:0362 / 03DA / 040B / 042A): the battlefield's three and the enemies' schemes.
/// With DARKLANDS_PALCYCLE_JOBS (the output of tools/oracle/palette_cycle.py: the original's routines called directly with a model of the
/// VGA DAC) every frame's DAC is compared with <see cref="PaletteCycles"/>.
/// </summary>
public sealed class PaletteCycleTests
{
    private static byte[] Pattern() => Enumerable.Range(0, 768).Select(i => (byte)((i * 7 + 3) & 0x3F)).ToArray();

    private static PaletteCycles Battlefield(byte[] dac)
    {
        // R002+0x04D2..0x04FA and R014+0x02D5..0x02EB
        var cycles = new PaletteCycles { Running = true };
        cycles.Take(1, 4, 0x10, 0x15, dac);
        cycles.Take(2, 6, 0x18, 0x1E, dac);
        cycles.Take(4, 5, 0x16, 0x17, dac);
        cycles.Start(1);
        cycles.Start(-2);
        cycles.Start(4);
        return cycles;
    }

    [Fact]
    public void TheBattlefieldsCyclesTurnAsTheOriginalsDid()
    {
        // the original's DAC after its 4th, 6th and 24th call of 047C:042A (palette_cycle.py, job "battlefield"): slot 1 (colours 16..21,
        // every 4 frames) moves up by one colour, slot 2 (24..30, every 6) down, slot 4 (22..23, every 5) swaps
        var dac = Pattern();
        var cycles = Battlefield(dac);
        var expected = new Dictionary<int, (byte[] Slot1, byte[] Slot2, byte[] Slot4)>
        {
            [4] = ([60, 3, 10, 19, 26, 33, 40, 47, 54, 61, 4, 11, 18, 25, 32, 39, 46, 53], [59, 2, 9, 16, 23, 30, 37, 44, 51, 58, 1, 8, 15, 22, 29, 36, 43, 50, 57, 0, 7], [17, 24, 31, 38, 45, 52]),
            [6] = ([60, 3, 10, 19, 26, 33, 40, 47, 54, 61, 4, 11, 18, 25, 32, 39, 46, 53], [16, 23, 30, 37, 44, 51, 58, 1, 8, 15, 22, 29, 36, 43, 50, 57, 0, 7, 59, 2, 9], [38, 45, 52, 17, 24, 31]),
            [24] = ([19, 26, 33, 40, 47, 54, 61, 4, 11, 18, 25, 32, 39, 46, 53, 60, 3, 10], [15, 22, 29, 36, 43, 50, 57, 0, 7, 59, 2, 9, 16, 23, 30, 37, 44, 51, 58, 1, 8], [17, 24, 31, 38, 45, 52]),
        };
        for (var frame = 1; frame <= 24; frame++)
        {
            cycles.Frame(dac);
            if (expected.TryGetValue(frame, out var e))
            {
                Assert.Equal(e.Slot1, dac[48..66]);
                Assert.Equal(e.Slot2, dac[72..93]);
                Assert.Equal(e.Slot4, dac[66..72]);
            }
        }
    }

    [Fact]
    public void ACycleTakesItsColoursOnceStopsWhereItIsAndHoldsWhileNotRunning()
    {
        var dac = Pattern();
        var cycles = new PaletteCycles { Running = true };
        cycles.Take(3, 3, 0x40, 0x44, dac);
        // taken, not started: nothing moves
        cycles.Frame(dac);
        cycles.Frame(dac);
        cycles.Frame(dac);
        Assert.Equal(Pattern()[192..207], dac[192..207]);
        cycles.Start(3);
        for (var i = 0; i < 3; i++)
        {
            cycles.Frame(dac);
        }
        // after three frames colour 0x40 shows colour 0x44's
        Assert.Equal(Pattern()[204..207], dac[192..195]);
        cycles.Stop(3);
        var stopped = dac.ToArray();
        for (var i = 0; i < 30; i++)
        {
            cycles.Frame(dac);
        }
        Assert.Equal(stopped, dac);
        // a range of one colour (last <= first) or a slot out of 1..8 is not taken
        cycles.Take(5, 3, 0x50, 0x50, dac);
        cycles.Take(9, 3, 0x50, 0x55, dac);
        cycles.Start(5);
        cycles.Start(9);
        Assert.False(cycles.Turning(5));
        Assert.Null(cycles.Written(5));
    }

    [Fact]
    public void FourteenSchemesCycleAllInSlotThree()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var schemes = EnemyPalettes.Load(files);
        var cycling = Enumerable.Range(0, schemes.Count).Select(schemes.Cycle).Where(c => c.First != 0xFF).ToArray();
        Assert.Equal(71, schemes.Count);
        Assert.Equal(14, cycling.Length);
        Assert.All(cycling, c => Assert.Equal(3, c.Slot));
        Assert.Equal([3, 5, 9], cycling.Select(c => c.Reload).Distinct().Order().ToArray());
    }

    [Fact]
    public void TheOriginalsCyclesGiveTheSameDac()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_PALCYCLE_JOBS") is not { Length: > 0 } path)
        {
            return;
        }
        using var doc = JsonDocument.Parse(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        var frames = 0;
        var failures = new List<string>();
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var dac = job.GetProperty("dac").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
            var after = job.GetProperty("after").EnumerateArray().ToArray();
            var cycles = new PaletteCycles { Running = true };
            var frame = 0;
            foreach (var call in job.GetProperty("calls").EnumerateArray())
            {
                int I(int k) => call[k].GetInt32();
                switch (call[0].GetString())
                {
                    case "take":
                        cycles.Take(I(1), I(2), I(3), I(4), dac);
                        break;
                    case "start":
                        cycles.Start(I(1));
                        break;
                    case "stop":
                        cycles.Stop(I(1));
                        break;
                    case "frame":
                        cycles.Frame(dac);
                        var original = after[frame].EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                        if (!original.AsSpan().SequenceEqual(dac))
                        {
                            failures.Add($"{job.GetProperty("name").GetString()} frame {frame}");
                        }
                        frame++;
                        frames++;
                        break;
                }
            }
        }
        Console.WriteLine($"palcycle: {frames} frames compared, {failures.Count} differ");
        Assert.True(frames > 0, "no frame");
        Assert.True(failures.Count == 0, $"{failures.Count} of {frames} frames differ: " + string.Join(", ", failures.Take(20)));
    }
}
