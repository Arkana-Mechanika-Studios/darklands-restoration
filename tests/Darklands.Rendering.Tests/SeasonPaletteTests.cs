using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Simulation.Overland;

namespace Darklands.Rendering.Tests;

public sealed class SeasonPaletteTests
{
    [Fact]
    public void EveryMonthAndDayReadsAndWritesTheCanvasPalette()
    {
        for (var month = 0; month < 12; month++)
        {
            for (var day = 0; day < 31; day++)
            {
                var canvas = new Canvas(new Screen());
                SeasonPalette.Apply(new OverlandScreen.CanvasPaletteBytes(canvas), month, day);
            }
        }
        // winter writes colour 74 (offset 0xDE) as 6-bit 0x2E: 8-bit (0x2E << 2) | (0x2E >> 4) in the red byte
        var winter = new Canvas(new Screen());
        SeasonPalette.Apply(new OverlandScreen.CanvasPaletteBytes(winter), 0, 10);
        Assert.Equal(0x2Eu << 2 | 0x2Eu >> 4, winter.Screen.Palette[0xDE / 3] >> 16 & 0xFF);
    }

    [Fact]
    public void TheSeasonOfTheSaveDKSAVE99CanBeApplied()
    {
        var game = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        var path = Path.Combine(game, "SAVES", "DKSAVE99.SAV");
        if (!File.Exists(path))
        {
            return;
        }
        var bsvPath = Path.ChangeExtension(path, ".BSV");
        var bsv = File.Exists(bsvPath) ? File.ReadAllBytes(bsvPath) : [];
        var state = OriginalSaves.Read(File.ReadAllBytes(path), bsv, "DKSAVE99.SAV", Darklands.Core.World.NoGameData.Instance).State;
        SeasonPalette.Apply(new OverlandScreen.CanvasPaletteBytes(new Canvas(new Screen())), state.Date.Month, state.Date.Day);
    }
}
