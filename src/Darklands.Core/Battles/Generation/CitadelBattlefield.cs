namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Baphomet's citadel (overlay R013, battlefield types 0x57..0x5D and 0x6E): one fixed battlefield
/// per room of the citadel, prepared in LCASTLE as a level, a tile description (".fil") and a list of
/// enemy groups (".atv"). The generator loads one, gives its doors their ids and the level its
/// exit cards, saves the level and sets how many of which monster each group holds.
/// </summary>
// orig: R013 1432 (10 functions; only the dispatcher matched, the room loaders differ in register use only)
public static class CitadelBattlefield
{
    private const int GroupRecordSize = 0x1E;
    private const int GroupCountAt = BattlefieldLevel.ImageSize;

    /// <summary>The first exit card of the citadel's rooms.</summary>
    private const int OuterExitCard = 0x158;

    /// <summary>The exit card of the other four ways out of a room.</summary>
    private const int InnerExitCard = 0x16D;

    /// <summary>The spot records of a level (+0x3386, 10 bytes: x, y, height) that the court's doors point at.</summary>
    private const int SpotsAt = 0x3386;

    /// <summary>Builds the room for a battlefield type; returns the number of levels (0 for a type that is not one).</summary>
    // orig: R013+0x0000 (1432:0000) draft (matched per the draft notes); jump table and the test of the first spec checked against asm; tests: Oracle:special.json
    public static int Build(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        switch (battlefield)
        {
            case 0x57:
                BuildCourt(g);
                break;
            case 0x58:
                BuildRoom(g, "lc_g1", 0x161, quality: 1, count: 4);
                break;
            case 0x59:
                BuildRoom(g, "lc_g2", 0x15A, quality: 1, count: 2);
                break;
            case 0x5A:
                BuildRoom(g, "lc_g3", 0x15D, quality: 2, count: 1);
                break;
            case 0x5B when specs[0].Type != 0:
                // C: the argument tested is the first spec's type (p_a): the room with the stairs (two of its eight groups are dropped).
                BuildRoom(g, "lc_g4s", 0x160, quality: 2, count: 3, droppedGroups: 2);
                break;
            case 0x5B:
                BuildRoom(g, "lc_g4ns", 0x160, quality: 1, count: 1);
                break;
            case 0x5C:
                BuildRoom(g, "lc_g5", 0x164, quality: 1, count: 1, fixTiles: FixGuardRoomTiles);
                break;
            case 0x5D:
                BuildRoom(g, "lc_g7a", 0x15F, quality: 1, count: 1);
                break;
            case 0x6E:
                BuildRoom(g, "lc_g7b", 0x167, quality: 1, count: 1, firstExitCard: 0x167, lastKindEndsBattle: true);
                break;
            default:
                // DORMANT: the dispatcher only sends the eight citadel types here.
                return 0;
        }
        // C: combat_flags |= 1; unlike the other persistent battlefields no cleared-level memory is touched.
        g.Persistent = true;
        return 1;
    }

    /// <summary>
    /// The court: seven doors (type 8 objects) along a row, each given an id and a spot. With a nonzero
    /// <see cref="BattlefieldContext.CourtProgress"/> the party starts on one of the spots and meets no one,
    /// and the doors already opened are marked on the map.
    /// </summary>
    // orig: R013+0x0098 (1432:0098) draft (the draft does not match); checked against asm; tests: Oracle:special.json
    // DORMANT: CourtProgress (DS:160C) is 0 in the shipped game, so the party start, the opened doors and the empty court never occur.
    private static void BuildCourt(GenerationState g)
    {
        int[] doorIds = [0x159, 0x15C, 0x15E, 0x15B, 0x162, 0x165, 0x166, 0];
        // C: the original's table of (column, row) spots sits on the stack right below the door ids, so a spot index of 7
        // or more reads door ids as positions; the table here keeps that layout.
        int[] spots = [2, 5, 7, 5, 12, 5, 17, 5, 22, 5, 27, 5, 32, 5, .. doorIds];
        var room = LoadLevel(g, "lc_court");
        var level = room.Level;
        var groupCount = room.GroupCount;
        var progress = g.Context.CourtProgress;
        var startBits = progress & 0xFF;
        if (startBits != 0)
        {
            groupCount = 0;
            var spot = 0;
            while ((startBits >>= 1) != 0)
            {
                spot++;
            }
            level.PartyStart = new MapPoint((spots[spot * 2] << 5) + 0x10, (spots[spot * 2 + 1] << 4) + 8);
            level.PartyFacing = 6;
        }
        var doorsOpened = progress >> 8 != 0;
        if (doorsOpened)
        {
            for (var i = 0; i < 7; i++)
            {
                if ((progress >> 8 & 1 << i) != 0)
                {
                    var cell = level[spots[i * 2], 3];
                    level[spots[i * 2], 3] = new BattlefieldCell(cell.Bits & ~0x50000u | 0xA0000u);
                }
            }
            // DIVERGENCE: the loop of the original runs over 9 bits; bits 7 and 8 take the door ids 0x159 and 0x15E as rows
            // and write far outside the level (into unrelated memory), which is not reproduced.
        }
        level.ExitCards = new LevelExitCards(OuterExitCard, 12, 12, 12, 12);
        var nextDoor = 0;
        for (var i = 0; i < (sbyte)level.ObjectCount; i++)
        {
            var o = level.Object(i);
            if (o.Type is 7 or 8)
            {
                // C: the spots advance with every object, the door ids only with the doors.
                level.SetObject(i, o with { Room = doorIds[nextDoor++] });
                var record = SpotsAt + 10 * (o.Id & 0xFF);
                PutWord(level, record, (spots[i * 2] << 5) + 0x10);
                PutWord(level, record + 2, (spots[i * 2 + 1] << 4) + 8);
                level.Image[record + 4] = 4;
            }
        }
        g.Save(level);
        ReadTerrain(g, "lc_court.fil");
        if (doorsOpened)
        {
            g.Terrain[0x47] = 9;
        }
        MapGroups(g, ReadGroups(g, "lc_court.atv", groupCount), quality: 1, count: 2);
    }

    /// <summary>One of the citadel's other rooms: one door, the same exit cards, a tile description and groups of one kind each.</summary>
    // orig: R013+0x0338; R013+0x047A; R013+0x05BC; R013+0x06FE;
    // R013+0x0848; R013+0x098A; R013+0x0ADC; R013+0x0C1E
    // (1432:xxxx) draft (eight copies of one template; the draft's differences are register use); checked against asm; tests: Oracle:special.json
    private static void BuildRoom(GenerationState g, string member, int doorId, int quality, int count, int firstExitCard = OuterExitCard,
        int droppedGroups = 0, Action<TerrainDescription>? fixTiles = null, bool lastKindEndsBattle = false)
    {
        var room = LoadLevel(g, member);
        var level = room.Level;
        level.ExitCards = new LevelExitCards(firstExitCard, InnerExitCard, InnerExitCard, InnerExitCard, InnerExitCard);
        var doors = 0;
        for (var i = 0; i < (sbyte)level.ObjectCount; i++)
        {
            var o = level.Object(i);
            if (o.Type is 7 or 8)
            {
                // DORMANT: the id array has one entry and a second door reads the word after it (a zero); no room has two doors.
                level.SetObject(i, o with { Room = doors++ == 0 ? doorId : 0 });
            }
        }
        g.Save(level);
        ReadTerrain(g, member + ".fil");
        fixTiles?.Invoke(g.Terrain);
        MapGroups(g, ReadGroups(g, member + ".atv", room.GroupCount - droppedGroups), quality, count, lastKindEndsBattle);
    }

    /// <summary>The guards' room has two tiles of its own.</summary>
    // orig: R013+0x098A (1432:098A) draft; checked against asm (the two stores into the tile description)
    private static void FixGuardRoomTiles(TerrainDescription terrain)
    {
        terrain[0x44] = 8;
        terrain[0x77] = 6;
    }

    private sealed record Room(BattlefieldLevel Level, int GroupCount);

    /// <summary>Reads a level file: the floor image and, after it, the number of groups.</summary>
    private static Room LoadLevel(GenerationState g, string member)
    {
        var bytes = g.Context.Citadel!.Read(member);
        var image = new byte[BattlefieldLevel.ImageSize];
        bytes.AsSpan(0, Math.Min(bytes.Length, image.Length)).CopyTo(image);
        var count = bytes.Length >= GroupCountAt + 2 ? (short)(bytes[GroupCountAt] | bytes[GroupCountAt + 1] << 8) : (short)0;
        return new Room(new BattlefieldLevel(image), count);
    }

    /// <summary>Reads a tile description file over the generator's own.</summary>
    private static void ReadTerrain(GenerationState g, string member)
    {
        var bytes = g.Context.Citadel!.Read(member);
        bytes.AsSpan(0, Math.Min(bytes.Length, TerrainDescription.Size)).CopyTo(g.Terrain.Bytes);
    }

    /// <summary>Reads a groups file (30 bytes per group, then two more); a group the file lacks stays as the cleared buffer has it.</summary>
    private static List<EnemyGroup> ReadGroups(GenerationState g, string member, int count)
    {
        var bytes = g.Context.Citadel!.Read(member);
        var groups = new List<EnemyGroup>();
        for (var i = 0; i < count; i++)
        {
            var at = i * GroupRecordSize;
            groups.Add(at + GroupRecordSize <= bytes.Length ? EnemyGroup.FromRecord(bytes.AsSpan(at, GroupRecordSize)) : new EnemyGroup());
        }
        return groups;
    }

    /// <summary>Turns each group's monster type into a kind of the level and sets how many of it there are.</summary>
    private static void MapGroups(GenerationState g, List<EnemyGroup> groups, int quality, int count, bool kindEndsBattle = false)
    {
        foreach (var group in groups)
        {
            var kind = g.Forces.AddKind(0, group.Kinds[0], quality);
            group.Kinds[0] = (sbyte)kind;
            group.Counts[0] = (sbyte)count;
            if (kindEndsBattle && kind >= 0)
            {
                // C: the kind index is used unchecked.
                // DIVERGENCE: an unknown type (−1) would flag the bytes before the first kind record, which nothing reads.
                g.Forces.Kinds[0][kind].EndsBattleWhenKilled = 1;
            }
            g.Forces.Groups[0].Add(group);
        }
    }

    private static void PutWord(BattlefieldLevel level, int at, int value)
    {
        level.Image[at] = (byte)value;
        level.Image[at + 1] = (byte)(value >> 8);
    }
}
