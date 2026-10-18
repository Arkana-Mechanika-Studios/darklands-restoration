using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Graphics;
using Darklands.Core.Battles;

namespace Darklands.Battles.Tests;

public sealed class BattlefieldTilesTests
{
    [Fact]
    public void TerrainDescriptionHasTheLoadersDefaults()
    {
        var terrain = new TerrainDescription();
        terrain.SetSetName(TileKind.Floor, "city");
        terrain.SetTile(TileKind.Floor, 1, 6);
        Assert.Equal("city", terrain.SetName(TileKind.Floor));
        Assert.Equal(6, terrain.Tile(TileKind.Floor, 1));
        Assert.Equal(-1, terrain.Tile(TileKind.Floor, 2));
        Assert.Equal(2, terrain.AnimatedBelow(TileKind.FloorDecoration));
        Assert.Equal(8, terrain.AnimatedBelow(TileKind.Furniture));
        Assert.Equal(-1, terrain.AnimatedBelow(TileKind.NorthWall));
    }

    [Fact]
    public void CityTilesLoadFromTheTileCatalog()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var data = new byte[TerrainDescription.Size];
        data[0] = 3;
        "city"u8.CopyTo(data.AsSpan(0x01));
        "citynw15"u8.CopyTo(data.AsSpan(0x34));
        "cityww15"u8.CopyTo(data.AsSpan(0x67));
        "city"u8.CopyTo(data.AsSpan(0x9A));
        foreach (var at in new[] { 0x0A, 0x24, 0x3D, 0x57, 0x70, 0x8A, 0xA4 })
        {
            Array.Fill(data, (byte)0xFF, at, 16);
        }
        data[0x0B] = 6;
        data[0x3E] = 2;
        data[0x71] = 2;
        data[0xA5] = 0;
        var tiles = BattlefieldTiles.Load(CatArchive.Decode(files.Read("BC")), new TerrainDescription(data));
        Assert.Equal(16, tiles.Floors[1]!.Height);
        Assert.Equal(48, tiles.Floors[1]!.Width);
        Assert.NotNull(tiles.NorthWalls[1]);
        Assert.NotNull(tiles.WestWalls[1]);
        Assert.NotNull(tiles.Furniture[1]);
        Assert.Null(tiles.Floors[2]);
    }
}
