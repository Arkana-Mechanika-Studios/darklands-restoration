using System.Text;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Xunit.Abstractions;

namespace Darklands.Battles.Tests;

/// <summary>A diagnostic dump of the doors, triggers, stairs and chests the generators leave (DUNGEON_DUMP=1).</summary>
public sealed class DungeonDumpTests(ITestOutputHelper output)
{
    [Fact]
    public void Dump()
    {
        if (GameFolder.Files is not { } files || Environment.GetEnvironmentVariable("DUNGEON_DUMP") is null)
        {
            return;
        }
        foreach (var (bf, seed) in new[] { (0x00, 1234), (0x0A, 777), (0x0B, 4242), (0x5E, 99), (0x11, 60256) })
        {
            var field = BattleScreenTests.Build(files, bf, seed);
            var text = new StringBuilder($"battlefield {bf:X2} seed {seed}: {field.Levels.Count} levels, start {field.StartLevel}\n");
            foreach (var level in field.Levels)
            {
                var im = level.Image;
                short W(int at) => (short)(im[at] | im[at + 1] << 8);
                text.Append($" level {level.LevelNumber}: doors {im[0x1F52]} triggers {im[0x2184]} lists {im[0x2488]} stairs {im[0x2FB6]} regions {im[0x300C]} zones {im[0x3326]}\n");
                for (var k = 0; k < im[0x1F52]; k++)
                {
                    var a = 0x1F54 + 14 * k;
                    text.Append($"  door {k}: {W(a)},{W(a + 2)} {im[a + 4]}x{im[a + 5]} wall {(sbyte)im[a + 6]} lock {im[a + 7]:X2} cell {im[a + 8]:X2},{im[a + 9]:X2} cell2 {im[a + 10]:X2},{im[a + 11]:X2} link {(sbyte)im[a + 12]} open {im[0x33D3 + k]}\n");
                }
                for (var k = 0; k < im[0x2184]; k++)
                {
                    var a = 0x2186 + 14 * k;
                    text.Append($"  trigger {k}: {W(a)},{W(a + 2)} {im[a + 4]}x{im[a + 5]} type {W(a + 6)} seg {W(a + 8):X4} a {W(a + 10)} b {W(a + 12)}\n");
                }
                for (var k = 0; k < im[0x2488]; k++)
                {
                    text.Append($"  list {k}: offset {W(0x248A + 2 * k):X4}\n");
                }
                for (var k = 0; k < im[0x2FB6]; k++)
                {
                    var a = 0x2FB8 + 14 * k;
                    text.Append($"  stairs {k}: {W(a)},{W(a + 2)} {im[a + 4]}x{im[a + 5]} level {(sbyte)im[a + 6]} wall {(sbyte)im[a + 7]} spot {W(a + 8)},{W(a + 10)} dir {im[a + 12]} back {(sbyte)im[a + 13]}\n");
                }
                for (var k = 0; k < im[0x300C]; k++)
                {
                    var a = 0x300E + 0x2C * k;
                    text.Append($"  region {k}: {W(a)},{W(a + 2)} {im[a + 4]}x{im[a + 5]} cell {im[a + 6]},{im[a + 7]} b8 {im[a + 8]:X2} b9 {im[a + 9]:X2} bA {im[a + 10]:X2} n {im[a + 11]} idx {im[a + 12]} bytes {Convert.ToHexString(im, a + 13, 31)}\n");
                }
            }
            output.WriteLine(text.ToString());
        }
    }
}
