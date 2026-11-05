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
        // winter writes the block's byte 0xDE (the red of colour 0x48: the block's colours start 6 bytes in) as 6-bit 0x2E: 8-bit
        // (0x2E << 2) | (0x2E >> 4) in the red byte (vis10 session loadfix-winter-map: DKSAVE99's map, the original's DAC colour 0x48 = 2E 34 37)
        var winter = new Canvas(new Screen());
        SeasonPalette.Apply(new OverlandScreen.CanvasPaletteBytes(winter), 0, 10);
        Assert.Equal(0x2Eu << 2 | 0x2Eu >> 4, winter.Screen.Palette[0x48] >> 16 & 0xFF);
        Assert.Equal(0x34u << 2 | 0x34u >> 4, winter.Screen.Palette[0x48] >> 8 & 0xFF);
        Assert.Equal(0x37u << 2 | 0x37u >> 4, winter.Screen.Palette[0x48] & 0xFF);
        // the snow of the fields (bytes 0xE7..0xE9) is colour 0x4B, the pixels' index of the fields on the map
        Assert.Equal(0x37u << 2 | 0x37u >> 4, winter.Screen.Palette[0x4B] >> 16 & 0xFF);
        Assert.Equal(0x3Cu << 2 | 0x3Cu >> 4, winter.Screen.Palette[0x4B] & 0xFF);
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
