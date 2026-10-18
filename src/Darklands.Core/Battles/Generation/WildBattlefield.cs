namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Wilderness battlefields (overlay R008): a prepared open-country map "iwild*" of IMAPS.CAT chosen by
/// the battlefield type and the terrain of the overland map, with random floor and wall variation,
/// trees, bushes and scenery, the party at one start marker and up to five enemy groups, each with
/// a reinforcement spot, at markers chosen by the battlefield type.
/// </summary>
// orig: R008 1432 (16 functions; 7 byte-matched per the draft notes, 4 differ only in jump-table layout,
// 5 "close"); spec: scratchpad A_battlefield_wild.md, which re-read every function against
public static class WildBattlefield
{
    private static readonly string[] FamilyNames = ["---", "gen", "mtn", "msh", "spc", "sab", "gat", "wal"];

    /// <summary>How many maps each family has (the last digit of the member name runs 1..count).</summary>
    private static readonly int[] FamilyMaps = [0, 6, 4, 4, 6, 7, 2, 4];

    private const int GenericFamily = 1;
    private const int MountainFamily = 2;
    private const int MarshFamily = 3;
    private const int SpecialFamily = 4;
    private const int SabbatFamily = 5;
    private const int GateFamily = 6;
    private const int WallFamily = 7;

    /// <summary>What the first steps decide and the later ones read (the original keeps these in globals).</summary>
    private sealed class MapPlan
    {
        /// <summary>DS:DF83: the terrain the battlefield is made for.</summary>
        public int Terrain;

        /// <summary>DS:DFB9: use the furniture tile set "outgrpb".</summary>
        public bool SabbatSet;

        /// <summary>DS:DF87: first digit of the map number (1 open country, 2 with cliffs, 3 with a road).</summary>
        public int Hundreds = 1;

        /// <summary>DS:DF89: second digit (1 = the map has a small building).</summary>
        public int Tens;

        /// <summary>DS:DF8B: last digit.</summary>
        public int Units;

        /// <summary>DS:DFBA: the furniture pass used a special prop.</summary>
        public bool Special;
    }

    /// <summary>The three enemy specs (type, quality, count), which the enemy choice rewrites in place.</summary>
    private sealed class EnemySpecs
    {
        public int Type1, Quality1, Count1, Type2, Quality2, Count2, Type3, Quality3, Count3;

        public EnemySpecs((int Type, int Quality, int Count)[] s)
        {
            (Type1, Quality1, Count1) = s[0];
            (Type2, Quality2, Count2) = s[1];
            (Type3, Quality3, Count3) = s[2];
        }
    }

    /// <summary>Where the party and the enemies are placed: marker ids and how many enemies of each kind stand at each.</summary>
    private sealed class Placement
    {
        /// <summary>DS:DF8D: the party start marker (22, or 23 on the far side).</summary>
        public int PartyMarker = 0x16;

        /// <summary>DS:DF8F: the marker of each of the five groups (−1 none).</summary>
        public int[] GroupMarker = [2, 3, 4, 5, -1];

        /// <summary>DS:DF99: the marker the reinforcements come from (10 pairs with 22, 1 with 23).</summary>
        public int ReinforcementMarker = 10;

        /// <summary>DS:DF9B: the enemies of each kind in each group.</summary>
        public int[,] Count = new int[5, 3];
    }

    /// <summary>Builds the battlefield (always one level, level0.flr) and sets the combat flags it needs.</summary>
    // orig: R008+0x0000 (1432:0000) draft (byte-matched per the draft notes; calls checked against asm)
    public static int Build(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        var plan = new MapPlan();
        ChooseTerrain(g, battlefield, plan);
        var level = LoadMap(g, battlefield, plan);
        WriteOutdoorTiles(g, level, plan);
        ReplaceFurniture(g, battlefield, level, plan);
        PlaceForces(g, battlefield, level, plan, new EnemySpecs(specs));
        // C: all markers and objects are dropped before the level is saved; levelNo (+0x1F40) is not written (0 in every IWILD member).
        level.ObjectCount = 0;
        g.Save(level);
        return 1;
    }

    /// <summary>The terrain the map is chosen for, and whether the sabbat furniture set is used.</summary>
    // orig: R008+0x009C (1432:009C) draft (byte-matched per the draft notes; jump table checked against asm)
    private static void ChooseTerrain(GenerationState g, int bf, MapPlan plan)
    {
        plan.Terrain = g.Context.WorldTerrain;
        plan.SabbatSet = false;
        switch (bf)
        {
            case 0x33 or 0x3F or 0x40 or 0x41 or (>= 0x45 and <= 0x51) or (>= 0x6F and <= 0x73):
                plan.SabbatSet = true;
                break;
            case 0x35:
                plan.Terrain = 0x14;
                break;
            case 0x38:
                if (plan.Terrain == 0x18)
                {
                    plan.Terrain = 0x11;
                }
                break;
            case 0x3B:
                plan.Terrain = 0x0E;
                break;
            case 0x42 or 0x43:
                plan.Terrain = 8;
                break;
        }
    }

    /// <summary>Chooses and loads the prepared map "iwild{family}.{hundreds}{tens}{units}".</summary>
    // orig: R008+0x0172 (1432:0172) draft ("close"; both jump tables and the draws re-read in the asm)
    private static BattlefieldLevel LoadMap(GenerationState g, int bf, MapPlan plan)
    {
        var family = 0;
        switch (bf)
        {
            case 0x32 or 0x55:
                family = SpecialFamily;
                plan.Units = g.Roll(2) + 2;
                break;
            case 0x34 or 0x41 or 0x6F or 0x70 or 0x71:
                plan.Tens = 1;
                break;
            case 0x36:
                family = SpecialFamily;
                plan.Units = 1;
                break;
            case 0x37:
                family = SpecialFamily;
                plan.Units = 2;
                break;
            case 0x3A:
                family = MountainFamily;
                plan.Hundreds = 2;
                break;
            case 0x3E or 0x75 or 0x79:
                family = SpecialFamily;
                plan.Units = g.Roll(2) + 4;
                break;
            case 0x46 or 0x47:
                family = MountainFamily;
                break;
            case 0x48 or 0x72:
                family = SabbatFamily;
                plan.Units = 1;
                break;
            case 0x49 or 0x4F or 0x50:
                family = SabbatFamily;
                plan.Units = 2;
                break;
            case 0x4A:
                family = SabbatFamily;
                plan.Units = 3;
                break;
            case 0x4B:
                family = SabbatFamily;
                plan.Units = 4;
                break;
            case 0x4C:
                family = SabbatFamily;
                plan.Units = 5;
                break;
            case 0x4D:
                family = SabbatFamily;
                plan.Units = 6;
                break;
            case 0x4E or 0x73:
                family = SabbatFamily;
                plan.Units = 7;
                break;
            case 0x52 or 0x54:
                family = WallFamily;
                break;
            case 0x53:
                family = GateFamily;
                break;
            case 0x61 or 0x62 or 0x63 or 0x74:
                plan.Hundreds = 3;
                plan.Tens = 1;
                family = GenericFamily;
                break;
        }
        if (family == 0)
        {
            family = ChooseFamilyByTerrain(g, plan);
        }
        if (plan.Units <= 0 && g.Context.ForceMap > 0)
        {
            // DORMANT: forceMap (DS:C100) is always −1 in the shipped game.
            // C: signed division of a positive value.
            plan.Units = g.Context.ForceMap % 10;
            if (FamilyMaps[family] < plan.Units)
            {
                plan.Units = 0;
            }
        }
        if (plan.Units <= 0)
        {
            plan.Units = g.Roll(FamilyMaps[family]);
        }
        var number = (plan.Hundreds * 10 + plan.Tens) * 10 + plan.Units;
        return g.Context.Maps.Load($"iwild{FamilyNames[family]}.{number:000}");
    }

    /// <summary>The map family for a battlefield type without a fixed one: by the terrain (may also change hundreds, tens and units).</summary>
    private static int ChooseFamilyByTerrain(GenerationState g, MapPlan plan)
    {
        var family = GenericFamily;
        switch (plan.Terrain)
        {
            case 2 or 3 or 0x19:
                if (plan.Tens == 0)
                {
                    family = SpecialFamily;
                    plan.Units = g.Roll(2) + 2;
                }
                break;
            case 4 or 5:
                family = MarshFamily;
                break;
            case 7 or 0x10 or 0x11:
                plan.Hundreds = 2;
                break;
            case 8 or 9:
                plan.Tens = 1;
                break;
            case 0x0A or 0x0B:
                if (plan.Tens == 0 && g.Roll(10) > 5)
                {
                    plan.Tens = 1;
                }
                break;
            case 0x0F:
                if (g.Roll(10) > 5)
                {
                    plan.Hundreds = 2;
                }
                break;
            case 0x12:
                if (plan.Tens == 0)
                {
                    family = MountainFamily;
                }
                else
                {
                    plan.Hundreds = 2;
                }
                break;
            case 0x13 or 0x14:
                if (plan.Tens == 0)
                {
                    family = MountainFamily;
                    if (g.Roll(10) > 5)
                    {
                        plan.Hundreds = 2;
                    }
                }
                else
                {
                    plan.Hundreds = 2;
                }
                break;
            case 0x15 or 0x16 or 0x17:
                if (plan.Tens == 0)
                {
                    plan.Hundreds = 2;
                    family = MountainFamily;
                }
                else
                {
                    plan.Hundreds = 2;
                }
                break;
            case 0x18:
                plan.Hundreds = 3;
                break;
        }
        return family;
    }

    /// <summary>The outdoor tile description (palette by season, tile sets "out") and the random floor and wall variation of the map.</summary>
    // orig: R008+0x04CE (1432:04CE) draft (code matches, only the jump-table layout differs; both tables and the draw order re-read in the asm)
    private static void WriteOutdoorTiles(GenerationState g, BattlefieldLevel level, MapPlan plan)
    {
        var d = g.Terrain;
        d.BackgroundPalette = g.Context.Month switch
        {
            0 or 1 or 2 or 10 or 11 => 9,
            3 or 8 or 9 => 8,
            _ => 1,
        };
        d.SetSetName(TileKind.Floor, "out");
        int style;
        switch (plan.Terrain)
        {
            case 4 or 5:
                SetTiles(d, TileKind.Floor, 1, 3, 4, 5);
                style = 2;
                break;
            case 6 or 7:
                SetTiles(d, TileKind.Floor, 1, 6, 7, 8);
                style = 1;
                break;
            default:
                SetTiles(d, TileKind.Floor, 1, 0, 1, 2);
                style = 0;
                break;
        }
        SetTiles(d, TileKind.Floor, 4, 9, 10);
        d.SetSetName(TileKind.FloorDecoration, "out");
        SetTiles(d, TileKind.FloorDecoration, 2, 0x13, 0x14, 0x15, 0x16, 0x17, 0x12, 8, 9, 10, 11, 12, 13, 0x10, 0x11);
        d.SetSetName(TileKind.NorthWall, "out");
        d.SetTile(TileKind.NorthWall, 1, 0);
        // The one draw of this step before the cells: which of two wall tiles the cliffs use.
        d.SetTile(TileKind.NorthWall, 2, g.Roll(10) > 5 ? 1 : 2);
        SetTiles(d, TileKind.NorthWall, 3, 3, 4, 5);
        d.SetSetName(TileKind.NorthWallDecoration, "out");
        d.SetTile(TileKind.NorthWallDecoration, 1, 0);
        d.SetSetName(TileKind.WestWall, "out");
        d.SetTile(TileKind.WestWall, 1, 0);
        d.SetTile(TileKind.WestWall, 2, d.Tile(TileKind.NorthWall, 2) == 1 ? 1 : 2);
        d.SetTile(TileKind.WestWall, 3, 3);

        for (var x = 1; x <= 38; x++)
        {
            for (var y = 1; y <= 38; y++)
            {
                VaryFloor(g, level, x, y, style);
                VaryWalls(level, x, y);
            }
        }
    }

    /// <summary>The floor of one cell: the road/track marks become floor decorations, empty cells may get grass, the floor variant is random.</summary>
    private static void VaryFloor(GenerationState g, BattlefieldLevel level, int x, int y, int style)
    {
        var h = level.FloorByte(x, y);
        var high = h >> 4;
        var low = (h & 15) switch
        {
            1 => 14,
            2 => 15,
            3 => 8,
            4 => 9,
            5 => 10,
            6 => 11,
            7 => 12,
            8 => 13,
            9 => 7,
            _ => RandomGrass(g, style),
        };
        switch (high)
        {
            case 2:
                high = 5;
                low = 0;
                break;
            case 3:
                high = 4;
                low = 0;
                break;
            default:
                // C: also for cells without a floor (high 0), which still draw.
                high = g.Roll(3);
                break;
        }
        level.SetFloorByte(x, y, (byte)(high << 4 | low & 15));
    }

    /// <summary>A grass decoration on a quarter of the empty floor cells (marsh and the second style have more kinds).</summary>
    private static int RandomGrass(GenerationState g, int style)
    {
        if (g.Roll(4) != 1)
        {
            return 0;
        }
        return style == 0 ? g.Roll(4) + 2 : g.Roll(5) + 1;
    }

    /// <summary>The four wall nibbles of a cell: the map's wall codes become the tile numbers of the description; gate posts decorate the neighbour.</summary>
    private static void VaryWalls(BattlefieldLevel level, int x, int y)
    {
        var cell = level[x, y].Bits;
        for (var shift = 0; shift <= 24; shift += 8)
        {
            // C: the nibble is cut out of the long with a logical shift.
            var wall = (cell >> shift) & 0xF;
            if (wall == 0)
            {
                continue;
            }
            wall = wall switch { 2 => 3, 3 => 2, 4 => 4, 5 => 5, _ => 1 };
            cell = (cell & ~(0xFu << shift)) | wall << shift;
            if (shift == 16)
            {
                if (wall == 4)
                {
                    // C: x − 1 and x + 1 are inside the 40 × 40 cells for x in 1..38. The neighbour may be one the loop has passed or
                    // will pass; its decoration nibbles are not among those the later pass rewrites.
                    level[x - 1, y] = new BattlefieldCell(level[x - 1, y].Bits | 0x0010_0000);
                }
                else if (wall == 5)
                {
                    level[x + 1, y] = new BattlefieldCell(level[x + 1, y].Bits | 0x0000_1000);
                }
            }
        }
        level[x, y] = new BattlefieldCell(cell);
    }

    private static void SetTiles(TerrainDescription d, TileKind kind, int first, params int[] tiles)
    {
        for (var i = 0; i < tiles.Length; i++)
        {
            d.SetTile(kind, first + i, tiles[i]);
        }
    }

    /// <summary>The furniture tile slots the pass hands out: slot numbers per placeholder code, and the free slots split into three random groups.</summary>
    private sealed class FurnitureSlots
    {
        public int Tileset;
        public int Special5, Special6, SpecialBridge, Special9, Special10, Special16, Special17, Special18, Special19;
        public int FirstA, FirstB, FirstC;
        public int CountA, CountB, CountC;
    }

    /// <summary>
    /// Turns the furniture placeholder codes of the map into slots of the furniture tile set (and the tile set into the description),
    /// thinning trees and bushes by terrain and giving every cell a random rotation.
    /// </summary>
    // orig: R008+0x0926 (1432:0926) draft (code matches, jump tables differ; the draft's labels of three
    // thresholds arms were wrong: asm table terrain-4 maps them to 8/9, 0x0A/0x0B, 0x0C/0x0D, see ThinningFor)
    private static void ReplaceFurniture(GenerationState g, int bf, BattlefieldLevel level, MapPlan plan)
    {
        var d = g.Terrain;
        int tileset;
        if (plan.Terrain is 5 or 4)
        {
            d.SetSetName(TileKind.Furniture, "outgrpc");
            tileset = 3;
        }
        else if (plan.SabbatSet)
        {
            d.SetSetName(TileKind.Furniture, "outgrpb");
            tileset = 4;
        }
        else
        {
            d.SetSetName(TileKind.Furniture, "outgrpa");
            tileset = 0;
        }
        var counts = CountPlaceholders(bf, level);
        var slots = AssignSlots(g, d, plan, counts, tileset);
        var thinning = ThinningFor(plan.Terrain, g.Context.ForceMap);
        plan.Special = false;
        for (var x = 1; x <= 38; x++)
        {
            for (var y = 1; y <= 38; y++)
            {
                ReplaceCell(g, bf, level, plan, d, slots, thinning, x, y);
            }
        }
    }

    /// <summary>The first 0x66 "see object" on a cell, whose id stands for the placeholder code of the cell.</summary>
    private static int? SpecialObjectId(BattlefieldLevel level, int x, int y)
    {
        // C: the object count is a signed byte.
        var count = (sbyte)level.ObjectCount;
        for (var i = 0; i < count; i++)
        {
            var o = level.Object(i);
            if (o.Type == 0x66 && o.X == x << 5 && o.Y == y << 4)
            {
                return o.Id;
            }
        }
        return null;
    }

    /// <summary>How many placeholder codes 5, 6, 7/8, 9, 10, 16, 17, 18 and 19 the map uses (some battlefield types reserve a slot).</summary>
    private static Dictionary<int, int> CountPlaceholders(int bf, BattlefieldLevel level)
    {
        var count = new Dictionary<int, int>();
        void Add(int code) => count[code] = count.GetValueOrDefault(code) + 1;
        switch (bf)
        {
            case 0x36:
                Add(17);
                break;
            case 0x39 or 0x41 or 0x6F or 0x70 or 0x71:
                Add(19);
                break;
            case 0x43:
                Add(16);
                break;
        }
        for (var x = 1; x <= 38; x++)
        {
            for (var y = 1; y <= 38; y++)
            {
                var code = level[x, y].Furniture;
                if (code == 15 && SpecialObjectId(level, x, y) is { } id)
                {
                    code = id;
                }
                if (code is 5 or 6 or 9 or 10 or 16 or 17 or 18 or 19)
                {
                    Add(code);
                }
                else if (code is 7 or 8)
                {
                    Add(7);
                }
            }
        }
        return count;
    }

    /// <summary>Gives each used special code its slot, splits the rest into the groups A, B and C and fills them with shuffled tiles.</summary>
    private static FurnitureSlots AssignSlots(GenerationState g, TerrainDescription d, MapPlan plan, Dictionary<int, int> counts, int tileset)
    {
        var s = new FurnitureSlots { Tileset = tileset };
        int Used(int code) => counts.GetValueOrDefault(code);
        void Tile(int slot, int tile) => d.SetTile(TileKind.Furniture, slot, tile);

        for (var i = 1; i <= 4; i++)
        {
            Tile(i, i - 1);
        }
        var low = 5;
        var high = 15;
        if (Used(9) > 0)
        {
            s.Special9 = 15;
            high = 14;
            Tile(15, 15);
        }
        if (Used(10) > 0)
        {
            s.Special10 = high--;
            Tile(s.Special10, 0x10);
        }
        if (Used(16) > 0)
        {
            s.Special16 = high--;
            Tile(s.Special16, 0x0B);
        }
        if (Used(17) > 0)
        {
            s.Special17 = high--;
            Tile(s.Special17, 0x0C);
        }
        if (Used(5) > 0)
        {
            s.Special5 = low++;
            Tile(s.Special5, 0x12);
        }
        if (Used(6) > 0)
        {
            s.Special6 = low++;
            Tile(s.Special6, 0x13);
        }
        if (Used(7) > 0)
        {
            s.SpecialBridge = low++;
            Tile(s.SpecialBridge, 0x0A);
        }
        if (Used(18) > 0)
        {
            s.Special18 = low++;
            Tile(s.Special18, 0x11);
        }
        if (Used(19) > 0)
        {
            s.Special19 = low++;
            Tile(s.Special19, 0x0D);
        }
        // C: a fall-through switch on the number of free slots (re-derived; the most any map needs is 3 special codes).
        (s.CountA, s.CountB, s.CountC) = (high - low + 1) switch
        {
            1 => (1, 0, 0),
            2 => (1, 0, 1),
            3 => (1, 1, 1),
            4 => (1, 1, 2),
            5 => (2, 1, 2),
            6 => (3, 1, 2),
            7 => (3, 1, 3),
            8 => (3, 2, 3),
            9 => (3, 2, 4),
            10 or 11 => (4, 2, 4),
            _ => (0, 0, 0),
        };
        int[] groupA = [5, 6, 7, 8];
        for (var i = 3; i > 0; i--)
        {
            var j = g.Roll(i + 1) - 1;
            if (j != i)
            {
                (groupA[i], groupA[j]) = (groupA[j], groupA[i]);
            }
        }
        int[] groupB = g.Roll(2) == 1 ? [4, 9] : [9, 4];
        int[] groupC = [0, 1, 2, 3];
        for (var i = 3; i > 1; i--)
        {
            // C: the roll is 1..i, so entry 0 never moves: the first tree is always tile cBase + 0.
            var j = g.Roll(i);
            if (j != i)
            {
                (groupC[i], groupC[j]) = (groupC[j], groupC[i]);
            }
        }
        int treeBase;
        if (tileset == 3)
        {
            treeBase = 0x1E;
        }
        else if (tileset == 4)
        {
            treeBase = 0x14;
        }
        else
        {
            treeBase = plan.Terrain switch
            {
                2 or 3 or 0x19 => g.Roll(2) == 1 ? 0x1E : 0x14,
                8 or 10 or 12 or 14 or 18 => 0x1E,
                _ => 0x14,
            };
        }
        s.FirstA = low;
        for (var i = 0; s.CountA > i && high >= low; i++)
        {
            Tile(low++, groupA[i]);
        }
        d.SetAnimatedBelow(TileKind.Furniture, low);
        s.FirstB = low;
        for (var i = 0; s.CountB > i && high >= low; i++)
        {
            Tile(low++, groupB[i]);
        }
        s.FirstC = low;
        for (var i = 0; s.CountC > i && high >= low; i++)
        {
            Tile(low++, groupC[i] + treeBase);
        }
        return s;
    }

    /// <summary>The chance (percent) that a tree, a bush or a marsh plant disappears, and which codes the thinned trees turn into.</summary>
    private readonly record struct Thinning(int Remove12, int Remove13, int Remove11, int Remove14, int Replace12, int Replace13, int Becomes12, int Becomes13);

    // asm table (terrain - 4, 0..0x14) re-read: the draft labelled three arms 0x0C/0x0D, 0x0E/0x0F, 0x10/0x11; they are 8/9, 0x0A/0x0B, 0x0C/0x0D.
    private static Thinning ThinningFor(int terrain, int forceMap)
    {
        var t = new Thinning(10, 10, 10, 10, 0, 0, 12, 13);
        switch (terrain)
        {
            case 4:
                t = t with { Remove13 = 0, Replace12 = 100, Replace13 = 100, Becomes12 = 11, Becomes13 = 14, Remove11 = 25 };
                break;
            case 6 or 7:
                t = t with { Remove13 = 20, Remove11 = 25, Remove12 = 70, Replace12 = 70, Replace13 = 100, Becomes12 = 11, Becomes13 = 11 };
                break;
            case 8 or 9:
                t = t with { Remove12 = 80, Remove13 = 50, Remove11 = 50, Replace13 = 100, Becomes13 = 12 };
                break;
            case 0x0A or 0x0B:
                t = t with { Remove12 = 40, Remove13 = 30, Remove11 = 20 };
                break;
            case 0x0C or 0x0D:
                t = t with { Remove12 = 20, Remove13 = 8, Remove11 = 15, Replace13 = 22, Becomes13 = 12 };
                break;
            case 0x16 or 0x17:
                t = t with { Remove12 = 20, Remove13 = 20, Replace12 = 50, Replace13 = 50, Becomes12 = 11, Becomes13 = 11 };
                break;
            case 0x18:
                t = t with { Remove11 = 20, Remove12 = 20, Replace13 = 10, Becomes13 = 11 };
                break;
        }
        if (forceMap >= 10)
        {
            // DORMANT: forceMap (DS:C100) is always −1 in the shipped game; from 10 on it switches the thinning off.
            t = t with { Remove11 = 0, Remove12 = 0, Remove13 = 0, Remove14 = 0, Replace12 = 0, Replace13 = 0 };
        }
        return t;
    }

    /// <summary>One cell of the furniture pass: thin, pick the rotation, pick the slot, write furniture and rotation.</summary>
    private static void ReplaceCell(GenerationState g, int bf, BattlefieldLevel level, MapPlan plan, TerrainDescription d, FurnitureSlots s, Thinning thin, int x, int y)
    {
        var hgtMark = 0;
        var cell = level[x, y].Bits;
        var code = (int)(cell >> 4) & 0xF;
        switch (code)
        {
            case 12:
                if (g.Roll(100) <= thin.Remove12)
                {
                    code = 0;
                }
                else if (g.Roll(100) <= thin.Replace12)
                {
                    code = thin.Becomes12;
                }
                break;
            case 13:
                if (g.Roll(100) <= thin.Remove13)
                {
                    code = 0;
                }
                else if (g.Roll(100) <= thin.Replace13)
                {
                    code = thin.Becomes13;
                }
                break;
            case 11:
                if (g.Roll(100) <= thin.Remove11)
                {
                    code = 0;
                }
                break;
            case 14:
                if (g.Roll(100) <= thin.Remove14)
                {
                    code = 0;
                }
                break;
            case 15:
                if (SpecialObjectId(level, x, y) is { } id)
                {
                    code = id;
                }
                if (code == 15)
                {
                    switch (bf)
                    {
                        case 0x36:
                            code = 17;
                            break;
                        case 0x39 or 0x41 or 0x6F or 0x70 or 0x71:
                            code = 19;
                            break;
                        case 0x43:
                            code = 16;
                            break;
                        case 0x3D:
                            // The code stays 15 and becomes "nothing"; the floor decoration marks the spot instead.
                            hgtMark = 7;
                            plan.Special = true;
                            break;
                    }
                }
                break;
        }
        // C: the rotation is drawn for every interior cell, before the group pick.
        var rotation = g.Roll(4) - 1;
        int slot;
        switch (code)
        {
            case 1 or 2 or 3 or 4:
                slot = code;
                break;
            case 5:
                slot = s.Special5;
                break;
            case 6:
                slot = s.Special6;
                break;
            case 7:
                slot = s.SpecialBridge;
                rotation = 0;
                break;
            case 8:
                slot = s.SpecialBridge;
                rotation = 1;
                break;
            case 9:
                slot = s.Special9;
                break;
            case 10:
                slot = s.Special10;
                break;
            case 11:
                slot = s.Tileset == 3 ? g.Roll(s.CountB) + s.FirstB - 1 : g.Roll(s.CountB + s.CountA) + s.FirstA - 1;
                break;
            case 12:
                // C: no "− 1": picks the second to the last tree of group C (the first is the dense tree, code 13).
                slot = g.Roll(s.CountC - 1) + s.FirstC;
                break;
            case 13:
                slot = s.FirstC;
                break;
            case 14:
                slot = g.Roll(s.CountA) + s.FirstA - 1;
                break;
            case 16:
                slot = s.Special16;
                plan.Special = true;
                break;
            case 17:
                slot = s.Special17;
                plan.Special = true;
                break;
            case 18:
                slot = s.Special18;
                plan.Special = true;
                break;
            case 19:
                slot = s.Special19;
                plan.Special = true;
                break;
            default:
                slot = 0;
                break;
        }
        // C: an unused slot (tile −1) falls back to the previous one, down to slot 0.
        while (d.Tile(TileKind.Furniture, slot) < 0 && slot != 0)
        {
            slot--;
        }
        // C: a slot above 15 would spill into the next nibble; no shipped map reaches it (it needs all 11 free slots taken by special codes).
        cell = (cell & ~0xF0u) | (uint)slot << 4;
        cell = (cell & ~0x3000_0000u) | (uint)(rotation & 3) << 28;
        level[x, y] = new BattlefieldCell(cell);
        if (hgtMark != 0)
        {
            level.SetFloorByte(x, y, (byte)(level.FloorByte(x, y) & 0xF0 | hgtMark));
        }
    }

    /// <summary>Chooses the enemies and where everybody stands; places the party and the groups.</summary>
    // orig: R008+0x1388 (1432:1388) draft ("close"; asm re-read in full)
    private static void PlaceForces(GenerationState g, int bf, BattlefieldLevel level, MapPlan plan, EnemySpecs e)
    {
        // C: flag is a byte; layout_type may set it.
        var flag = plan.Tens == 1 || plan.Special;
        var layoutType = LayoutType(bf, ref flag, e);
        ChooseEnemies(g, bf, e);
        var placement = PlanPlacement(g, layoutType, flag, e);

        var o = level.Object(CityBattlefield.PickMarker(g, level, placement.PartyMarker, out _));
        level.PartyStart = new MapPoint(o.X + 16, o.Y + 8);
        level.PartyFacing = 0;
        level.PartyRoom = (sbyte)o.Room;
        // C: signed division.
        var px = level.PartyStart.X / 32;
        var py = level.PartyStart.Y / 16;
        var g1 = e.Type1 >= 0 && e.Count1 > 0 ? g.Forces.AddKind(0, e.Type1, e.Quality1) : -1;
        var g2 = e.Type2 >= 0 && e.Count2 > 0 ? g.Forces.AddKind(0, e.Type2, e.Quality2) : -1;
        var g3 = e.Type3 >= 0 && e.Count3 > 0 ? g.Forces.AddKind(0, e.Type3, e.Quality3) : -1;
        var faced = 0;
        for (var i = 0; i < 5; i++)
        {
            var sum = 0;
            for (var j = 0; j < 3; j++)
            {
                sum += Math.Max(placement.Count[i, j], 0);
            }
            if (sum <= 0)
            {
                continue;
            }
            var marker = CityBattlefield.PickMarker(g, level, placement.GroupMarker[i], out _);
            // C: the original also gets a "selected kind" (1..3 → g1..g3) from this call, maps it and never uses it.
            var (batch, pool) = ReinforcementParams(g, bf, e);
            var r = AddGroup(g, bf, level, placement.ReinforcementMarker, marker, batch, pool,
                (g1, placement.Count[i, 0]), (g2, placement.Count[i, 1]), (g3, placement.Count[i, 2]));
            if (r >= 0 && faced == 0)
            {
                var group = g.Forces.Groups[0][r];
                faced = group.X / 32;
                level.PartyFacing = GenerationState.DirectionBetween(px, py, faced, group.Y / 16);
            }
        }
        if (bf is 0x31 or 0x35 or 0x47 or 0x78)
        {
            g.Timed = true;
        }
        if (bf == 0x55)
        {
            // The cell behind the party gets floor decoration 7.
            var (bx, by) = level.PartyFacing switch
            {
                2 => (px - 1, py),
                4 => (px, py - 1),
                6 => (px + 1, py),
                _ => (px, py + 1),
            };
            level.SetFloorByte(bx, by, (byte)(level.FloorByte(bx, by) & 0xF0 | 7));
        }
    }

    /// <summary>The layout type (0x2E..0x30, 0x3B, 0x43 or the battlefield type itself) from the card's specs; may force the "near" flag.</summary>
    // orig: R008+0x1694 (1432:1694) draft ("close"; asm table re-read)
    private static int LayoutType(int bf, ref bool flag, EnemySpecs card)
    {
        switch (bf)
        {
            case 0x32 or 0x33 or 0x38 or 0x3F or 0x44 or 0x45 or 0x55 or 0x6F or 0x70 or 0x76:
                return 0x2F;
            case 0x3A:
                flag = true;
                return 0x2E;
            case 0x3B:
                return card.Type1 switch { 0x2A => 0x2F, 0x2B or 0x3A => 0x3B, _ => 0x2E };
            case 0x41:
                return card.Type2 != 0x29 ? 0x2F : 0x2E;
            case 0x42:
                return card.Quality3 <= 0 ? 0x2F : 0x2E;
            case 0x31 or 0x35 or 0x39 or 0x40 or 0x51 or 0x71 or 0x78:
                return 0x2E;
            case 0x43:
                return card.Quality3 > 0 ? 0x43 : 0x2E;
            case 0x77:
                return 0x30;
            default:
                // Includes battlefield types no wilderness battle uses (0x56..0x60 ...): the draft's explicit arms all return bf.
                return bf;
        }
    }

    /// <summary>Rewrites the enemy specs of the card by battlefield type, the party's lore and its able members.</summary>
    // orig: R008+0x1790 (1432:1790) draft (code matches, jump tables differ; case labels checked against the asm table, every arm re-read)
    private static void ChooseEnemies(GenerationState g, int bf, EnemySpecs e)
    {
        var lore = g.Context.LoreLevel();
        var able = g.Context.AbleMembers;
        int t1 = -1, q1 = 1, n1 = 1, t2 = -1, q2 = 1, n2 = 0, t3 = -1, q3 = 1, n3 = 0;
        int R(int sides) => g.Roll(sides);
        int Clamp(int value, int min, int max) => Math.Max(Math.Min(value, max), min);
        var cardQuality3 = e.Quality3;
        switch (bf)
        {
            case 0x36:
                if (lore >= 8)
                {
                    t1 = 0x37;
                }
                else
                {
                    t1 = 0x35;
                    if (lore > 3)
                    {
                        q1 = 2;
                    }
                }
                break;
            case 0x37:
                t1 = 0x37;
                if (lore >= 7)
                {
                    q1 = 2;
                }
                break;
            case 0x3F:
                {
                    t1 = 9;
                    q1 = Clamp(lore / 8 + 1, 1, 2);
                    // C: the asm draws the roll(1, 2) before the roll(1, able members).
                    var a = R(2);
                    var b = R(able);
                    n1 = Clamp(b + a + 1, 2, 7);
                    break;
                }
            case 0x40:
                t1 = 0x24;
                t2 = 0x22;
                q2 = 1;
                n2 = Clamp(R(2) + able / 5 + 2, 3, 5);
                break;
            case 0x6F:
                t1 = 0x27;
                t2 = 0x2D;
                q2 = 1;
                n1 = 2;
                n2 = R(2) + 1;
                break;
            case 0x70:
                t1 = 0x35;
                n1 = Clamp(R(2) + able - 1, 2, 6);
                break;
            case 0x71:
                t1 = 0x27;
                n1 = 2;
                t2 = 0x29;
                q2 = Clamp(lore / 6 + 1, 1, 2);
                n2 = Clamp(R(3) + able - 1, 2, 6);
                break;
            case 0x42 or 0x43:
                t1 = 4;
                q1 = Clamp(R(2) + lore / 6, 1, 3);
                t2 = 0x0B;
                q2 = R(2);
                n2 = R(3) + able / 5 + 2;
                break;
            case 0x33:
                t1 = 0x2F;
                n1 = Clamp(R(3) + lore / 6 + 3, 4, 7);
                break;
            case 0x39:
                t1 = 0x27;
                t2 = 0x29;
                q2 = Clamp(lore / 3 + 1, 1, 2);
                n2 = R(2) + 5;
                break;
            case 0x45:
                t1 = 0x2A;
                n1 = Clamp(R(3) + lore / 4 + 2, 3, 7);
                break;
            case 0x46 or 0x47:
                t1 = 0x4D;
                q1 = R(2);
                t2 = 0x0D;
                q2 = R(2);
                n2 = 3;
                break;
            case 0x72:
                t1 = 0x2A;
                t2 = 0x0D;
                q2 = R(2);
                n2 = 3;
                break;
            case 0x48:
                t1 = 0x2A;
                n1 = 2;
                break;
            case 0x49:
                t1 = 0x29;
                q1 = R(2) + 1;
                n1 = R(3);
                t2 = 0x0D;
                q2 = R(2);
                n2 = R(2) + 2;
                break;
            case 0x4A:
                t1 = 0x29;
                q1 = 3;
                t2 = 0x0D;
                q2 = 1;
                n2 = R(4) + 3;
                break;
            case 0x4B:
                t1 = 0x29;
                q1 = R(2) + 1;
                n1 = R(2);
                t2 = 0x0D;
                q2 = R(2);
                n2 = R(2) + 2;
                break;
            case 0x4C:
                t1 = 0x29;
                n1 = R(2) + 1;
                t2 = 0x0D;
                q2 = R(2) + 1;
                q1 = 3;
                n2 = R(3) + 2;
                break;
            case 0x4D:
                t1 = 0x1C;
                q1 = 2;
                t2 = 0x29;
                q2 = 3;
                n2 = Clamp(R(3) + lore / 6 + 2, 3, 6);
                break;
            case 0x73:
                t1 = 0x35;
                q1 = 2;
                break;
            case 0x4E:
                t1 = 0x1C;
                q1 = 2;
                t2 = 0x29;
                q2 = 3;
                n2 = 7;
                break;
            case 0x4F:
                t1 = 0x29;
                q1 = 3;
                n1 = R(2) + 1;
                t2 = 0x0D;
                q3 = cardQuality3;
                q2 = q3 > 0 ? R(2) + 1 : 3;
                n2 = R(2) + 3;
                break;
            case 0x50:
                t1 = 0x1C;
                q1 = 2;
                t2 = 0x29;
                q3 = cardQuality3;
                q2 = q3 > 0 ? R(2) + 1 : 3;
                n2 = 7;
                break;
            case 0x51:
                t1 = 0x29;
                q1 = R(2) + 1;
                n1 = R(2) + 1;
                t2 = 0x0D;
                q2 = Clamp(R(2) + lore / 6, 2, 3);
                n2 = Clamp(lore / 6 + R(3) + 2, 3, 5);
                break;
            case 0x63:
                t1 = 4;
                q1 = Clamp(R(2) + lore / 6, 1, 3);
                t2 = 0x0B;
                q2 = R(2);
                n2 = Clamp(lore / 6 + R(2) + 4, 5, 7);
                break;
            case 0x61:
                t2 = 0x0B;
                q2 = R(2);
                n2 = Clamp(R(3) + lore / 6 + 2, 3, 6);
                t1 = 4;
                q1 = Clamp(lore / 6 + q2 + 1, 2, 4);
                break;
            case 0x62:
                t1 = 0x29;
                q1 = Clamp(R(2) + lore / 6, 2, 3);
                n1 = R(2) + 1;
                t2 = 0x0B;
                q2 = R(2);
                n2 = Math.Max(Math.Min(lore / 6 + R(3) + 2, 8 - n1), 3);
                break;
            case 0x74:
                t1 = 0x4C;
                q1 = Clamp(R(2) + lore / 6, 1, 3);
                t2 = 0x0B;
                q2 = R(2);
                n2 = Clamp(lore / 6 + R(3) + 2, 3, 7);
                break;
            case 0x76 or 0x77 or 0x78:
                t2 = t3 = 8;
                if (cardQuality3 <= 0)
                {
                    t1 = 0x16;
                    q1 = lore / 7 + 1;
                    q2 = 2;
                    n2 = able;
                }
                else
                {
                    t1 = 0x12;
                    // C: signed truncating divisions; N2 = 2 − q can be 0 or negative and is stored as is.
                    q1 = Math.Max(cardQuality3 / -5 + 4, 1);
                    q2 = 3;
                    n2 = 2 - cardQuality3;
                    n3 = Math.Max(5 - cardQuality3, 1);
                }
                break;
            case 0x3E or 0x79:
                t2 = t3 = 8;
                if (cardQuality3 <= 0)
                {
                    t1 = 0x16;
                    q1 = lore / 7 + 1;
                    q2 = 2;
                    n2 = R(3);
                    q3 = 3;
                    n3 = able;
                }
                else
                {
                    t1 = 0x12;
                    q2 = 3;
                    n2 = 2 - cardQuality3;
                    n3 = Math.Max(5 - cardQuality3, 1);
                }
                break;
            case 0x75:
                if (e.Type1 is 0x24 or 0x26)
                {
                    t1 = 0x26;
                    q1 = 2;
                }
                else
                {
                    t1 = 0x23;
                    q1 = 3;
                }
                t2 = t3 = 0x11;
                q2 = Math.Max(cardQuality3 / -5 + 4, 1);
                n2 = 2 - cardQuality3 / 2;
                n3 = Math.Max(5 - cardQuality3, 1);
                break;
            default:
                // The card's specs stay as they are.
                return;
        }
        (e.Type1, e.Quality1, e.Count1) = (t1, q1, n1);
        (e.Type2, e.Quality2, e.Count2) = (t2, q2, n2);
        (e.Type3, e.Quality3, e.Count3) = (t3, q3, n3);
    }

    /// <summary>The marker ids of the party, of up to five groups and of the reinforcements, and how the enemies are divided among the groups.</summary>
    // orig: R008+0x20B2 (1432:20B2) draft (code matches, jump tables differ; the three tables re-read in the asm)
    private static Placement PlanPlacement(GenerationState g, int type, bool flag, EnemySpecs e)
    {
        var p = new Placement();
        p.Count[0, 0] = e.Count1;
        p.Count[0, 1] = e.Count2;
        p.Count[0, 2] = e.Count3;
        // DORMANT: forceSide (DS:C0FE) is −1 in the shipped game, so every "forceSide > 0" test below is false and only the
        // "forceSide <= 0" arms, which draw, run. The short-circuit order is kept: a roll is drawn only when the earlier terms are false.
        var fs = g.Context.ForceSide;
        bool Roll() => g.Roll(10) > 5;
        int R(int sides) => g.Roll(sides);
        switch (type)
        {
            case 0x2E:
                if (flag || fs > 0 && fs < 3 || fs <= 0 && Roll())
                {
                    if (fs == 2 || Roll())
                    {
                        p.GroupMarker[0] = 3;
                    }
                    p.ReinforcementMarker = 10;
                }
                else
                {
                    p.PartyMarker = 0x17;
                    p.GroupMarker[0] = fs == 3 || Roll() ? 6 : 7;
                    p.ReinforcementMarker = 1;
                }
                break;
            case 0x30:
                if (flag || fs > 0 && fs < 3 || fs <= 0 && Roll())
                {
                    p.ReinforcementMarker = 10;
                }
                else
                {
                    p.PartyMarker = 0x17;
                    p.GroupMarker[0] = 6;
                    p.GroupMarker[1] = 7;
                    p.ReinforcementMarker = 1;
                }
                for (var j = 0; j < 3; j++)
                {
                    // C: roll(1, count − 1): no draw when count − 1 is 1, and 0 when it is below 1.
                    p.Count[1, j] = R(p.Count[0, j] - 1);
                    p.Count[0, j] -= p.Count[1, j];
                }
                break;
            case 0x2F:
                if (flag || fs > 0 && fs < 3 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = fs == 2 || Roll() ? 4 : 5;
                    p.ReinforcementMarker = 10;
                }
                else
                {
                    p.PartyMarker = 0x17;
                    p.GroupMarker[0] = fs == 3 || Roll() ? 8 : 9;
                    p.ReinforcementMarker = 1;
                }
                break;
            case 0x43:
                if (fs > 0 && fs < 3 || fs <= 0 && Roll())
                {
                    p.PartyMarker = 4;
                    p.GroupMarker[0] = 2;
                    return p;
                }
                p.PartyMarker = 8;
                p.GroupMarker[0] = 6;
                break;
            case 0x3B:
                if (fs > 0 && fs < 3 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = 4;
                    p.GroupMarker[1] = 5;
                    p.ReinforcementMarker = 10;
                }
                else
                {
                    p.PartyMarker = 0x17;
                    p.GroupMarker[0] = 8;
                    p.GroupMarker[1] = 9;
                    p.ReinforcementMarker = 1;
                }
                break;
            case 0x3C:
                p.Count[0, 1] = 0;
                // C: a fall-through switch on n2 (7 down to 1) adding to the kind-2 counts of the listed groups.
                FallThrough(p, 1, e.Count2, [3, 0, 2, 1, 0, 2, 1]);
                break;
            case 0x34:
                for (var i = 0; i < e.Count2; i++)
                {
                    p.Count[R(3), 1]++;
                }
                p.Count[0, 1] = 0;
                break;
            case 0x3E or 0x75:
                if (fs >= 3 || fs <= 0 && Roll())
                {
                    p.PartyMarker = 0x17;
                }
                p.ReinforcementMarker = 10;
                break;
            case 0x79:
                if (fs >= 3 || fs <= 0 && Roll())
                {
                    p.PartyMarker = 0x17;
                }
                p.GroupMarker[0] = 3;
                p.ReinforcementMarker = 10;
                break;
            case 0x46:
                p.PartyMarker = 0x17;
                if (fs == 2 || fs == 4 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = 3;
                }
                break;
            case 0x47:
                p.PartyMarker = 0x16;
                if (fs == 2 || fs == 4 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = 3;
                }
                break;
            case 0x48:
                p.Count[0, 0] = p.Count[1, 0] = 1;
                p.ReinforcementMarker = 10;
                break;
            case 0x49:
                p.Count[0, 1] = 1;
                for (var i = 1; i < e.Count2; i++)
                {
                    p.Count[R(4) - 1, 1]++;
                }
                break;
            case 0x4A or 0x4B:
                p.Count[0, 1] = 1;
                p.Count[1, 1] = 3;
                FallThrough(p, 1, e.Count2, [1, 3, 2]);
                break;
            case 0x4C:
                p.Count[0, 1] = 0;
                p.Count[1, 1] = p.Count[0, 0] = 1;
                for (var i = 1; i < e.Count1; i++)
                {
                    p.Count[R(3), 0]++;
                }
                for (var i = 1; i < e.Count2; i++)
                {
                    p.Count[R(3), 1]++;
                }
                break;
            case 0x4D:
                p.Count[0, 1] = 0;
                p.Count[1, 1] = 1;
                for (var i = 1; i < e.Count2; i++)
                {
                    p.Count[R(2) + 1, 1]++;
                }
                p.ReinforcementMarker = 10;
                break;
            case 0x4E:
                p.Count[0, 1] = 0;
                for (var i = 0; i < e.Count2; i++)
                {
                    // C: groups 2..4; group 4 has no marker (−1), so it stands at object 0.
                    p.Count[R(3) + 1, 1]++;
                }
                p.ReinforcementMarker = 10;
                break;
            case 0x4F:
                p.Count[0, 0] = 1;
                for (var i = 1; i < e.Count1; i++)
                {
                    p.Count[R(4) - 1, 0]++;
                }
                p.Count[0, 1] = 0;
                for (var i = 0; i < e.Count2; i++)
                {
                    p.Count[R(4) - 1, 1]++;
                }
                p.ReinforcementMarker = Roll() ? 10 : 1;
                break;
            case 0x50:
                {
                    p.Count[0, 0] = 0;
                    p.Count[R(2) - 1, 0] = 1;
                    p.Count[0, 1] -= 2;
                    // C: the die count is the assignment (count[2][1] = count[3][1] = 1); the sides are count[0][1] − 1.
                    p.Count[3, 1] = p.Count[2, 1] = 1;
                    p.Count[1, 1] = g.Forces.RollDice(1, p.Count[0, 1] - 1);
                    p.Count[0, 1] -= p.Count[1, 1];
                    p.ReinforcementMarker = Roll() ? 10 : 1;
                    break;
                }
            case 0x72:
                p.ReinforcementMarker = 10;
                break;
            case 0x53:
                if (fs == 2 || fs == 4 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = 3;
                }
                p.ReinforcementMarker = 10;
                break;
            case 0x54:
                p.PartyMarker = 0x17;
                p.GroupMarker[0] = 6;
                break;
            case 0x61 or 0x63:
                if (fs == 3 || fs == 4 || fs <= 0 && Roll())
                {
                    p.PartyMarker = 0x17;
                    // C: only one entry changes.
                    switch (R(4))
                    {
                        case 1:
                            p.GroupMarker[0] = 6;
                            break;
                        case 2:
                            p.GroupMarker[1] = 7;
                            break;
                        case 3:
                            p.GroupMarker[2] = 8;
                            break;
                        case 4:
                            p.GroupMarker[3] = 9;
                            break;
                    }
                }
                p.Count[0, 2] = p.Count[0, 1] = 0;
                FallThrough(p, 1, e.Count2, [1, 0, 3, 2, 1, 1, 0]);
                break;
            case 0x62 or 0x74:
                if (fs == 1 || fs == 2 || fs <= 0 && Roll())
                {
                    p.GroupMarker[0] = R(4) + 1;
                }
                else
                {
                    p.PartyMarker = 0x17;
                    p.GroupMarker[0] = R(4) + 5;
                }
                break;
        }
        return p;
    }

    /// <summary>
    /// A C switch whose cases fall through: for a value <paramref name="n"/> in the listed range, every later case adds one to the count of
    /// <paramref name="kind"/> in the next listed group. <paramref name="groups"/> lists the groups in case order, from the highest case
    /// (<paramref name="top"/>, default 7) down to 1.
    /// </summary>
    private static void FallThrough(Placement p, int kind, int n, int[] groups, int top = 7)
    {
        if (n < 1 || n > top)
        {
            return;
        }
        for (var i = top - n; i < groups.Length; i++)
        {
            p.Count[groups[i], kind]++;
        }
    }

    /// <summary>The reinforcement batch and pool of a group, by battlefield type (the "selected kind" of the original is never used).</summary>
    // orig: R008+0x294E (1432:294E) draft (byte-matched per the draft notes; asm re-read)
    private static (int Batch, int Pool) ReinforcementParams(GenerationState g, int bf, EnemySpecs e)
    {
        int batch = 0, pool = 0;
        switch (bf)
        {
            case 0x3B:
                if (e.Type1 == 0x2A)
                {
                    batch = 2;
                    pool = Math.Max(Math.Min(e.Count1 - 2, 5), 2);
                }
                else if (e.Type1 is 0x2B or 0x3A)
                {
                    batch = 1;
                    pool = 2;
                }
                break;
            case 0x48:
                batch = 2;
                pool = 3;
                break;
            case 0x4D:
                batch = 2;
                pool = 3;
                break;
            case 0x4E:
                batch = 3;
                pool = 14;
                break;
            case 0x4F:
                batch = 3;
                pool = e.Quality3 > 0 ? 4 : 8;
                break;
            case 0x50:
                batch = 3;
                pool = e.Quality3 > 0 ? 7 : 10;
                break;
            case 0x51:
                batch = 3;
                pool = Math.Max(Math.Min(g.Roll(3) + g.Context.LoreLevel() / 4 + 4, 9), 5);
                break;
            case 0x53:
                if (e.Quality3 > 0)
                {
                    batch = pool = e.Quality3;
                }
                break;
            case 0x72:
                batch = 2;
                pool = 2;
                break;
        }
        return (batch, pool);
    }

    /// <summary>An enemy group at a marker with up to three kinds; the second kind brings reinforcements from the reinforcement marker.</summary>
    /// <returns>The group's number on the level, or −1 when none was made.</returns>
    // orig: R008+0x2ADC (1432:2ADC) draft ("close"; asm re-read)
    // DIVERGENCE: the original writes into the next group slot even when it is the last (into the next level's groups); the C# list grows.
    private static int AddGroup(GenerationState g, int bf, BattlefieldLevel level, int reinforcementMarker, int marker, int batch, int pool,
        (int Kind, int Count) first, (int Kind, int Count) second, (int Kind, int Count) third)
    {
        var (k1, n1) = first;
        var (k2, n2) = second;
        var (k3, n3) = third;
        // C: the third kind and count are not tested.
        if (k1 < 0 && k2 < 0)
        {
            return -1;
        }
        if (n1 <= 0 && n2 <= 0)
        {
            return -1;
        }
        if (marker < 0 || marker >= BattlefieldLevel.MaxObjects)
        {
            return -1;
        }
        var reinforcement = reinforcementMarker;
        if (reinforcement >= 0)
        {
            // C: picked before the group is written.
            reinforcement = CityBattlefield.PickMarker(g, level, reinforcement, out _);
        }
        var group = new EnemyGroup();
        var o = level.Object(marker);
        group.X = (short)(o.X + 16);
        group.Y = (short)(o.Y + 8);
        group.Room = (sbyte)o.Room;
        group.Spread = 16;
        group.HasReinforcements = 0;
        var j = 0;
        if (k1 is >= 0 and < 12 && n1 > 0)
        {
            group.Kinds[0] = (sbyte)k1;
            group.Counts[0] = (sbyte)n1;
            j = 1;
            MarkSlot(g, bf, k1);
        }
        // C: batch is never negative, so the second kind is also taken with a count of 0 or below.
        if (k2 is >= 0 and < 12 && (n2 > 0 || batch >= 0))
        {
            group.Kinds[j] = (sbyte)k2;
            group.Counts[j] = (sbyte)n2;
            j++;
            MarkSlot(g, bf, k2);
            if (batch >= 0 && reinforcement >= 0)
            {
                var r = level.Object(reinforcement);
                group.HasReinforcements = 1;
                group.ReinforcementKind = (sbyte)k2;
                group.Byte15 = 16;
                group.ReinforcementX = (short)(r.X + 16);
                group.ReinforcementY = (short)(r.Y + 8);
                group.ReinforcementBatch = (sbyte)batch;
                group.ReinforcementPool = (sbyte)pool;
                // The party's facing at this moment: 0 for the first group, as the party is turned toward it only afterwards.
                group.ReinforcementFacing = (sbyte)(level.PartyFacing ^ 4);
            }
        }
        if (k3 is >= 0 and < 12 && n3 > 0)
        {
            group.Kinds[j] = (sbyte)k3;
            group.Counts[j] = (sbyte)n3;
            j++;
            MarkSlot(g, bf, k3);
        }
        if (j > 0)
        {
            g.Forces.Groups[0].Add(group);
            return g.Forces.Groups[0].Count - 1;
        }
        return -1;
    }

    /// <summary>Marks the kind's record (byte +0x0C) for battlefield type 0x75 when its template is one of 0x1E..0x22.</summary>
    // orig: R008+0x2D34 (1432:2D34) draft (byte-matched per the draft notes)
    // UNCERTAIN: what the mark means (monsters_clear sets it to 0).
    private static void MarkSlot(GenerationState g, int bf, int kind)
    {
        if (bf == 0x75 && g.Forces.Templates[0][kind] is >= 0x1E and <= 0x22)
        {
            g.Forces.Kinds[0][kind].EndsBattleWhenKilled = 1;
        }
    }
}
