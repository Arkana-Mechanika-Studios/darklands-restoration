using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Core.Battles.Generation;

/// <summary>The prepared maps of IMAPS.CAT (decoded floor images), by member name ("icity.000").</summary>
public interface IBattlefieldMaps
{
    BattlefieldLevel Load(string member);
}

/// <summary>The members of LCASTLE (Baphomet's citadel: level, tile description and groups of each room), decoded.</summary>
public interface ICitadelFiles
{
    /// <summary>The decoded bytes of a member ("lc_court", "lc_g1.fil", "lc_g1.atv").</summary>
    byte[] Read(string member);
}

/// <summary>
/// What persists between battles at one place (saved with the game): the last battle's location
/// and which of its levels were cleared.
/// </summary>
// orig: DS:DB27 (last battle location) and DS:DA34 (cleared levels, bit per level)
public sealed class BattlefieldMemory
{
    // orig: DS:DB27 holds 0 until the first battle (the saved games made before any battle hold 0 there)
    public int LastLocation { get; set; }

    public int ClearedLevels { get; set; }

    /// <summary>How many battles this program has fought with the memory (the saved game's battle file is stale once one has).</summary>
    public int BattlesFought { get; set; }
}

/// <summary>
/// Generator globals that live as long as the program (not saved, not reset by a load): the original's BSS bytes that one
/// battlefield type sets and another reads.
/// </summary>
public sealed class GenerationProcessMemory
{
    /// <summary>DS:E051: "groups one smaller", set by every Templar fortress build (its second spec's quality is 1), read by the tombs.</summary>
    public bool FortressReducedGroups { get; set; }

    /// <summary>
    /// floor_img as the last battle left it (the level shown at its end), or null in a fresh program (zeros): the empty
    /// battlefield writes only its cells and counts over it, so its exit cards and other records are the previous battle's.
    /// </summary>
    public byte[]? FloorImage { get; set; }

    /// <summary>DS:C925 / DS:C978 / DS:C989 / DS:CA1B: the trigger whose alarm ended the last battle (x −1: none).</summary>
    public (int X, int Y, int Width, int Height) AlarmTrigger { get; set; } = (-1, 0, 0, 0);
}

/// <summary>Everything a battlefield generator reads besides the request.</summary>
public sealed class BattlefieldContext
{
    public required EnemyCatalogue Enemies { get; init; }

    public required IReadOnlyList<ItemDefinition> Items { get; init; }

    public required CombatTables Tables { get; init; }

    public required IBattlefieldMaps Maps { get; init; }

    /// <summary>LCASTLE, for Baphomet's citadel; without it the citadel is an empty field.</summary>
    public ICitadelFiles? Citadel { get; init; }

    public required IRandomSource Random { get; init; }

    /// <summary>cur_loc: the location (−1 wilderness).</summary>
    public int Location { get; init; } = -1;

    /// <summary>DS:D843: terrain code of the world map cell under the party.</summary>
    public int WorldTerrain { get; init; }

    /// <summary>DS:00E4: month 0..11.</summary>
    public int Month { get; init; }

    /// <summary>How many party members are able (R008 counts status 1).</summary>
    public int AbleMembers { get; init; } = 4;

    /// <summary>party_lore_level(): 1..10.</summary>
    public Func<int> LoreLevel { get; init; } = () => 1;

    /// <summary>
    /// DS:C100: a debugging override of the wilderness map number (and, from 10, of the thinning of trees) and of the general map
    /// (iminegen.nnn, counted from 1; 0 = the special map 0) a fixed-layout mine loads; −1 in the shipped game.
    /// </summary>
    // DORMANT: the original sets it to −1 at start-up and never writes it again.
    public int ForceMap { get; init; } = -1;

    /// <summary>DS:C0FE: a debugging override of the side a wilderness battle is laid out on and of which of its exits a fixed-layout mine uses; −1 in the shipped game.</summary>
    // DORMANT: the original sets it to −1 at start-up and never writes it again.
    public int ForceSide { get; init; } = -1;

    /// <summary>
    /// DS:160C (R013's own data): how far the party has come through the citadel's court; the low byte has the bit of the
    /// court spot the party starts on (it then meets no one), the high byte the bits of the doors already opened.
    /// </summary>
    // DORMANT: nothing writes it; it is 0 in the shipped game (the court is always fresh).
    public int CourtProgress { get; init; }

    /// <summary>DS:11DD: a debugging switch of the mine generator (plain wall gaps, no door objects for the special exits); off in the shipped game.</summary>
    // DORMANT: the original's data segment holds 0 and nothing writes it.
    public bool MineDebugSwitch { get; init; }

    public BattlefieldMemory Memory { get; init; } = new();

    /// <summary>The generator globals kept for the whole program (BattleScreens keeps one).</summary>
    public GenerationProcessMemory Process { get; init; } = new();

    public OriginalBehaviour Behaviour { get; init; } = OriginalBehaviour.Faithful;
}

/// <summary>A generated battlefield: its levels, look, enemies, where the party starts and the combat flags it sets.</summary>
public sealed class GeneratedBattlefield
{
    public required IReadOnlyList<BattlefieldLevel> Levels { get; init; }

    public required TerrainDescription Terrain { get; init; }

    public required BattlefieldForces Forces { get; init; }

    /// <summary>The level the party starts on.</summary>
    public int StartLevel { get; init; }

    /// <summary>The .enm data of each level as a battle file held it (a battlefield taken from one); null: made from the kinds.</summary>
    public IReadOnlyList<byte[]?>? EnemyFiles { get; init; }

    /// <summary>combat_flags bit 0: the battlefield persists ("battlefield save rules are in effect").</summary>
    public bool Persistent { get; init; }

    /// <summary>combat_flags bit 0x40: a timed battle (the combat code starts a 600-tick timer).</summary>
    public bool Timed { get; init; }

    /// <summary>What the battles leave on this battlefield besides its level images (the original's files and far tables).</summary>
    public BattlefieldStore Store { get; } = new();
}

/// <summary>
/// The state a battlefield keeps between its battles: whether the level files were prepared for
/// battle (handlers and wall lists relocated, chest contents moved to the loot file), the wall
/// groups each level has opened, and the chests' contents.
/// </summary>
// orig: R024+0x0000 (1EF8:0000) prepares the level files once per new battlefield; R136+0xE4E6 (doors_opened, a long
// per level, cleared by R002+0x1A7A for a new battlefield); DS:C7A4[20] (the chests' loot file offsets)
public sealed class BattlefieldStore
{
    /// <summary>The levels were prepared (R024 0000 ran).</summary>
    public bool Prepared { get; set; }

    /// <summary>R136+0xE4E6[level]: the wall groups (bit n = group n) already uncovered.</summary>
    public uint[] DoorsOpened { get; } = new uint[16];

    /// <summary>The loot file with the piles and the chests' offsets (DS:C7A4).</summary>
    public Tactics.LootStore Loot { get; } = new();

    /// <summary>floor_shown (DS:11CA) when the last battle here ended (−1: none yet): modes 1 and 2 show it again.</summary>
    public int FloorShown { get; set; } = -1;

    /// <summary>
    /// The party slots' places when the last battle here ended (x +4, y +6, path facing +0x19, sprite facing +0x1A of each record),
    /// null for a slot that was empty: mode 1 keeps them.
    /// </summary>
    public (MapPoint Position, int Facing, int SpriteFacing)?[] PartyPlaces { get; } = new (MapPoint, int, int)?[5];
}

/// <summary>A generator's working state: the forces, the levels it saves, the flags it sets.</summary>
public sealed class GenerationState
{
    public GenerationState(BattlefieldContext context, BattlefieldForces forces)
    {
        Context = context;
        Forces = forces;
    }

    public BattlefieldContext Context { get; }

    public BattlefieldForces Forces { get; }

    public TerrainDescription Terrain { get; } = new();

    /// <summary>The saved levels by level number (<c>level%d.flr</c>).</summary>
    public SortedDictionary<int, BattlefieldLevel> SavedLevels { get; } = [];

    public bool Persistent { get; set; }

    public bool Timed { get; set; }

    public int Roll(int sides) => Forces.RollDice(1, sides);

    /// <summary>Saves a level under its level number.</summary>
    public void Save(BattlefieldLevel level) => SavedLevels[level.LevelNumber] = level.Clone();

    /// <summary>mark_persistent_battlefield: the battlefield persists; a new location forgets the cleared levels.</summary>
    // orig: R004+0x20DC (0E76:20DC) draft
    public void MarkPersistent()
    {
        Persistent = true;
        var memory = Context.Memory;
        if (memory.LastLocation != Context.Location)
        {
            memory.ClearedLevels = 0;
            memory.LastLocation = Context.Location;
        }
    }

    /// <summary>The direction (0, 2, 4, 6) from one point to another.</summary>
    // orig: dir_between R004+0x2160 (0E76:2160) draft (byte-matched per the draft notes)
    public static int DirectionBetween(int x0, int y0, int x1, int y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        if (dy < dx && -dy > dx)
        {
            return 0;
        }
        if (dx > dy && -dy < dx)
        {
            return 2;
        }
        if (dx < dy && -dy < dx)
        {
            return 4;
        }
        if (dx < dy && dx < -dy)
        {
            return 6;
        }
        return dy < 0 ? 0 : 4;
    }
}

/// <summary>
/// Builds a battlefield as <c>battlefield_build</c> does: the colour seed from the location, the
/// battle seed, the generator chosen by the battlefield type, then the loader's fix-ups of the
/// enemy kinds and groups.
/// </summary>
// orig: battlefield_build R004+0x0000 (0E76:0000) draft (dispatch table checked
// against the old engine's BattlefieldGenerationDispatcher, which pinned the original 138-word table)
public static class BattlefieldBuilder
{
    public static GeneratedBattlefield Build(BattlefieldContext context, int battlefield, int seed, IReadOnlyList<(int Type, int Quality, int Count)> groups)
    {
        var random = context.Random;
        // C: srand(cur_loc); r = rand(); srand(seed): r fixes the enemies' colours for this place.
        random.Seed((uint)(ushort)context.Location);
        var variantSeed = random.NextRaw();
        random.Seed((uint)(ushort)seed);
        var forces = new BattlefieldForces(context.Enemies, context.Items, context.Tables, random) { Behaviour = context.Behaviour };
        var state = new GenerationState(context, forces);
        var specs = Specs(groups);
        int levels;
        var start = 0;
        switch (Generator(battlefield))
        {
            case BattlefieldGenerator.City:
                levels = CityBattlefield.Build(state, battlefield, out start, specs);
                break;
            case BattlefieldGenerator.Wilderness:
                levels = WildBattlefield.Build(state, battlefield, specs);
                start = 0;
                break;
            case BattlefieldGenerator.Castle:
                levels = CastleBattlefield.Build(state, battlefield, out start, specs);
                break;
            case BattlefieldGenerator.Mine:
                levels = MineBattlefield.Build(state, battlefield, specs);
                start = 0;
                break;
            case BattlefieldGenerator.TemplarFortress:
                levels = FortressBattlefield.BuildFortress(state, battlefield, out start, specs);
                break;
            case BattlefieldGenerator.Tombs:
                levels = FortressBattlefield.BuildTombs(state, battlefield, specs);
                start = 0;
                break;
            case BattlefieldGenerator.DragonCave:
                levels = DragonCaveBattlefield.Build(state, battlefield, specs);
                start = 0;
                break;
            case BattlefieldGenerator.Baphomet when context.Citadel is not null:
                levels = CitadelBattlefield.Build(state, battlefield, specs);
                start = 0;
                break;
            default:
                // DIVERGENCE: the citadel needs LCASTLE; without it (and for battlefield types no generator handles, as in the
                // original) the field is empty (empty_battlefield, R008 2D62).
                levels = EmptyBattlefield.Build(state, battlefield, specs);
                start = 0;
                break;
        }
        forces.FixKinds(levels, variantSeed);
        forces.FixGroups(levels);
        return new GeneratedBattlefield
        {
            Levels = [.. state.SavedLevels.Values],
            Terrain = state.Terrain,
            Forces = forces,
            StartLevel = start,
            Persistent = state.Persistent,
            Timed = state.Timed,
        };
    }

    /// <summary>The three enemy specs (type −1 when missing).</summary>
    private static (int Type, int Quality, int Count)[] Specs(IReadOnlyList<(int Type, int Quality, int Count)> groups) =>
        [.. Enumerable.Range(0, 3).Select(i => i < groups.Count ? groups[i] : (-1, 0, 0))];

    /// <summary>Which generator overlay builds a battlefield type.</summary>
    // orig: battlefield_build's switch (R004+0x00E0..0x024F)
    public static BattlefieldGenerator Generator(int battlefield) => battlefield switch
    {
        <= 8 and >= 0 or 0x27 or 0x28 or >= 0x7A and <= 0x7E => BattlefieldGenerator.Castle,
        >= 0x0A and <= 0x10 or >= 0x66 and <= 0x69 or 0x6C or 0x6D or >= 0x7F and <= 0x89 => BattlefieldGenerator.Mine,
        >= 0x11 and <= 0x26 or >= 0x29 and <= 0x2D or 0x56 or 0x6A or 0x6B => BattlefieldGenerator.City,
        >= 0x2E and <= 0x55 or >= 0x61 and <= 0x63 or >= 0x6F and <= 0x79 => BattlefieldGenerator.Wilderness,
        >= 0x57 and <= 0x5D or 0x6E => BattlefieldGenerator.Baphomet,
        0x5E => BattlefieldGenerator.TemplarFortress,
        0x5F => BattlefieldGenerator.Tombs,
        0x60 or 0x64 or 0x65 => BattlefieldGenerator.DragonCave,
        _ => BattlefieldGenerator.Empty,
    };
}

/// <summary>The battlefield generator overlays.</summary>
public enum BattlefieldGenerator
{
    /// <summary>R008 2D62 empty_battlefield.</summary>
    Empty,

    /// <summary>R005..R007 castles and keeps.</summary>
    Castle,

    /// <summary>R009 mines.</summary>
    Mine,

    /// <summary>R010 cities.</summary>
    City,

    /// <summary>R008 wilderness.</summary>
    Wilderness,

    /// <summary>R013 Baphomet's citadel.</summary>
    Baphomet,

    /// <summary>R011 Templar fortress.</summary>
    TemplarFortress,

    /// <summary>R011 tombs.</summary>
    Tombs,

    /// <summary>R012 dragon cave.</summary>
    DragonCave,
}
