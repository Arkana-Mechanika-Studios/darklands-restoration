namespace Darklands.Core.Battles.Generation;

/// <summary>
/// The empty field used for battlefield types no generator handles: a 20 × 20 open square,
/// the party three cells from its centre on a random side, the enemies three cells beyond.
/// </summary>
// orig: empty_battlefield R008+0x2D62 (1432:2D62), R008+0x2E76 (1432:2E76),
// R008+0x3002 (1432:3002) draft (all three byte-matched per the draft notes; spec A §12)
// C: the original works in the battle's static floor image, so fields it does not write keep the previous battle's values (exit
// cards, door and exit records ...): GenerationProcessMemory.FloorImage, zeros in a fresh program.
public static class EmptyBattlefield
{
    public static int Build(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        var level = new BattlefieldLevel(g.Context.Process.FloorImage is { Length: BattlefieldLevel.ImageSize } previous
            ? (byte[])previous.Clone()
            : new byte[BattlefieldLevel.ImageSize]);
        for (var x = 0; x < BattlefieldLevel.Size; x++)
        {
            for (var y = 0; y < BattlefieldLevel.Size; y++)
            {
                level[x, y] = BattlefieldCell.Void;
                level.SetFloorByte(x, y, 0);
            }
        }
        var gateSide = EmptyField(g, level);
        g.Save(level);
        int Map((int Type, int Quality, int Count) s) => s.Type >= 0 && s.Count > 0 ? g.Forces.AddKind(0, s.Type, s.Quality) : -1;
        var k1 = Map(specs[0]);
        var k2 = Map(specs[1]);
        var k3 = Map(specs[2]);
        EmptyGroup(g, gateSide, (k1, specs[0].Count), (k2, specs[1].Count), (k3, specs[2].Count));
        _ = battlefield;
        return 1;
    }

    // orig: R008+0x2E76 (1432:2E76) draft (byte-matched per the draft notes)
    private static int EmptyField(GenerationState g, BattlefieldLevel level)
    {
        g.Terrain.SetSetName(TileKind.Floor, "out");
        g.Terrain.SetTile(TileKind.Floor, 1, 0);
        level.PartListCount = 0;
        var image = level.Image;
        image[0x248A] = image[0x248B] = 0;
        var k = 0;
        for (var x = 1; x <= 20; x++)
        {
            for (var y = 1; y <= 20; y++)
            {
                var value = (x << 8) + y;
                image[0x24C6 + 2 * k] = (byte)value;
                image[0x24C6 + 2 * k + 1] = (byte)(value >> 8);
                k++;
                level[x, y] = new BattlefieldCell(0);
                level.SetFloorByte(x, y, (byte)(level.FloorByte(x, y) | 0x10));
            }
        }
        image[0x24C6 + 2 * 400] = image[0x24C6 + 2 * 400 + 1] = 0xFF;
        image[0x3326] = 4;
        short[][] rectangles = [[32, 16, 640, 16], [640, 32, 32, 288], [32, 320, 640, 16], [32, 32, 32, 288]];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                image[0x3328 + 10 * i + 2 * j] = (byte)rectangles[i][j];
                image[0x3328 + 10 * i + 2 * j + 1] = (byte)(rectangles[i][j] >> 8);
            }
        }
        level.PartyRoom = -1;
        var gateSide = g.Roll(4) - 1;
        level.PartyFacing = ((byte)gateSide ^ 2) << 1;
        var (dx, dy) = gateSide switch { 0 => (0, -3), 1 => (3, 0), 2 => (0, 3), 3 => (-3, 0), _ => (0, 0) };
        level.PartyStart = new MapPoint((dx + 10) << 5, (dy + 10) << 4);
        return gateSide;
    }

    // orig: R008+0x3002 (1432:3002) draft (byte-matched per the draft notes)
    private static void EmptyGroup(GenerationState g, int gateSide, params (int Kind, int Count)[] kinds)
    {
        var group = new EnemyGroup();
        (group.X, group.Y) = gateSide switch { 0 => ((short)320, (short)208), 1 => ((short)224, (short)160), 2 => ((short)320, (short)112), _ => ((short)416, (short)160) };
        group.Spread = 16;
        group.Room = 0;
        var j = 0;
        foreach (var (kind, count) in kinds)
        {
            if (kind is >= 0 and < 12 && count > 0)
            {
                group.Kinds[j] = (sbyte)kind;
                group.Counts[j] = (sbyte)count;
                j++;
            }
        }
        if (j > 0)
        {
            g.Forces.Groups[0].Add(group);
        }
    }
}
