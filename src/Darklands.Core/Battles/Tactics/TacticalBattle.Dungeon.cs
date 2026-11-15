using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

/// <summary>What a level's trigger record calls (its handler word +6 before R024 relocates it to a far pointer).</summary>
public enum TriggerHandler
{
    /// <summary>0: hidden-door-search (R023 0812).</summary>
    SecretDoor = 0,

    /// <summary>1: a hidden trap is discovered (R014 19CD:255A, thunk 09C0:17F7).</summary>
    HiddenTrap = 1,

    /// <summary>2: trap-spell-effect (R023 0D88).</summary>
    SpellTrap = 2,

    /// <summary>4: trap-shot (R023 0E30).</summary>
    MissileTrap = 4,

    /// <summary>5: a pit (R014 19CD:2312, thunk 09C0:181F).</summary>
    Pit = 5,

    /// <summary>6: a wall group opens (R023 0000, thunk 09C0:1815).</summary>
    OpenWalls = 6,

    /// <summary>7: an alarm removes furniture (R014 19CD:23F6, thunk 09C0:1833).</summary>
    FurnitureAlarm = 7,

    /// <summary>8: an alarm removes a wall (R014 19CD:24A8, thunk 09C0:1829).</summary>
    WallAlarm = 8,
}

/// <summary>
/// The dungeon of the battle: doors (used, picked, dissolved, opened by enemies), secret doors,
/// traps (hidden, spell and dart traps, pits, alarms), chests (picked, disarmed, opened), wall
/// groups that open, stairs between levels and leaving a level; where each member stands and the
/// menu entries that follow from it.
/// </summary>
// orig: R023 draft (3 of 16 functions byte-matched; offsets checked against the asm where they mattered);
// party-place-table and its helpers R014 19CD:1ADE..2232, the trigger handlers 19CD:2312..255A, fighter-goto-action 0E76:2170; level-change
// R024 059A and the level preparation R024 0000
public sealed partial class TacticalBattle
{
    private const int DoorCountAt = 0x1F52, DoorsAt = 0x1F54, TriggerCountAt = 0x2184, TriggersAt = 0x2186;
    private const int ListCountAt = 0x2488, ListsAt = 0x248A, ListBase = 0x24C6;
    private const int StairsCountAt = 0x2FB6, StairsAt = 0x2FB8, RegionCountAt = 0x300C, RegionsAt = 0x300E, RegionSize = 0x2C;
    private const int TrapPatchesAt = 0x3236, ZoneCountAt = 0x3326, ZonesAt = 0x3328;
    private const int FurnitureRemovalAt = 0x3350, WallRemovalAt = 0x3382, MissileTrapsAt = 0x316E, DoorOpenAt = 0x33D3;

    /// <summary>The DS strings of these messages (the screen reads them from the EXE).</summary>
    public const int NoLockPicksText = 0x04C8, LockYieldsText = 0x04F8, BeyondSkillText = 0x0518, RefusesText = 0x0540,
        FindsSecretDoorText = 0x055A, DiscoversTrapText = 0x0570, TrapDisarmedText = 0x0582, EaterWaterText = 0x0590, OopsText = 0x0644;

    /// <summary>R136+0xE530[k]: door k is the linked twin of an earlier door (enemies do not open it for a path).</summary>
    private readonly bool[] _twinDoor = new bool[40];

    /// <summary>DS:E1CA: the member whose order (or key) picks a lock.</summary>
    private int _picker;

    /// <summary>DS:CA99: the pick/dissolve entries are about a door (1) or a chest (0).</summary>
    // BUG: DS:CA99 follows the leader's place tests, so a member's Dissolve Lock order dissolves a door or a chest by the leader's
    //   last place, not his own (fighter-goto-action R014+0x2170 case 7). Kept as the original (party-split audit row 20). Decision:
    //   later, an optional fixed rule after 1.0.
    public bool LockIsDoor { get; private set; }

    /// <summary>DS:C865: the selected member stands next to a loot pile.</summary>
    public bool NextToPile { get; private set; }

    /// <summary>The menus of the battle (enable bytes and label parts).</summary>
    public BattleMenus Menus { get; } = new();

    /// <summary>DS:C925 / DS:C978 / DS:C989 / DS:CA1B: the trigger whose alarm ended the last battle (it is applied when the fight starts again);
    /// program globals, kept in <see cref="Generation.GenerationProcessMemory"/>.</summary>
    public (int X, int Y, int Width, int Height) AlarmTrigger
    {
        get => ProgramMemory.AlarmTrigger;
        set => ProgramMemory.AlarmTrigger = value;
    }

    /// <summary>The program's globals the battles share (BattleScreens keeps one for the whole program).</summary>
    public Generation.GenerationProcessMemory ProgramMemory { get; set; } = new();

    /// <summary>The flags table (DS:A696) as the program holds it when the battle starts (the last battle's, or the last load's battle file); null: zeros.</summary>
    public IReadOnlyList<ushort>? StaleFlags { get; set; }

    /// <summary>The path bytes (fighter-path-table, 13 × 250) and path indices (DS:D6FB) as the program holds them when the battle starts (the last
    /// battle's, or the last load's battle file); null: what the combatants hold (zeros).</summary>
    public (byte[] Paths, byte[] Indices)? StalePaths { get; set; }

    /// <summary>The battle variables as the program holds them when the battle starts (the program's battle file: the last battle's, or the
    /// last load's); null: a fresh program's zeros. A new battle takes from them the tables no battle's start resets: the attack facings
    /// (DS:D3FE), the placed base loads (DS:C1C1) and the saved path with its length (2E38:0C64, DS:DA43).</summary>
    public byte[]? StaleVariables { get; set; }

    /// <summary>DS:07E6: the timer's rate word, which the timer's set-up (root 047C:0066) makes 1 whenever it is installed, as at every
    /// battle's start (R002+0x0093); a battle taken up from a saved game reads the file's (R003+0x0CE8). Only the battle file shows it.</summary>
    public int TimerRate { get; set; } = 1;

    private Generation.BattlefieldStore Store => Field.Store;

    private static bool Inside(MapPoint p, int x, int y, int w, int h) => x <= p.X && p.X < x + w && y <= p.Y && p.Y < y + h;

    private (int X, int Y, int W, int H) Box(int at) => (LevelShown.GetWord(at), LevelShown.GetWord(at + 2), LevelShown.GetByte(at + 4), LevelShown.GetByte(at + 5));

    // ---- the level files ----------------------------------------------------------------

    /// <summary>
    /// The levels of a new battlefield are prepared once: the wall lists' offsets become offsets in
    /// the image and the chests' contents go to the loot store. The handlers keep their type numbers here
    /// (the original turns them into far pointers).
    /// </summary>
    // orig: R024+0x0000 (1EF8:0000) draft (the tac.txt dump is the debug print's: not kept)
    private void PrepareLevels()
    {
        if (Store.Prepared)
        {
            return;
        }
        Store.Prepared = true;
        foreach (var level in Levels.All)
        {
            for (var j = 0; j < level.GetByte(RegionCountAt); j++)
            {
                var at = RegionsAt + RegionSize * j;
                var n = level.GetByte(at + 0x0B);
                var items = new List<ItemStack>();
                // C: n records are copied from +0x14 whatever n is (past the fourth they come from the next record).
                for (var i = 0; i < n && at + 0x14 + 6 * i + 6 <= BattlefieldLevel.ImageSize; i++)
                {
                    var r = at + 0x14 + 6 * i;
                    items.Add(new ItemStack((ushort)level.GetWord(r), level.GetByte(r + 2), level.GetByte(r + 3), level.GetByte(r + 4), level.GetByte(r + 5)));
                }
                var idx = level.GetByte(at + 0x0C);
                var o = Store.Loot.Append(items, items.Count);
                if (idx < Store.Loot.ChestOffsets.Length)
                {
                    Store.Loot.ChestOffsets[idx] = o;
                }
            }
            for (var i = 0; i < level.GetByte(ListCountAt); i++)
            {
                level.SetWord(ListsAt + 2 * i, level.GetWord(ListsAt + 2 * i) + ListBase);
            }
            if (level.LevelNumber == Battle.LevelShown && (sbyte)level.Image[0x1F51] >= 0)
            {
                OpenGroupBit(level.LevelNumber, (sbyte)level.Image[0x1F51]);
            }
        }
    }

    private void OpenGroupBit(int level, int n)
    {
        if (level is >= 0 and < 16)
        {
            // C: 1L << n through the run-time's long shift: from 32 on nothing is set.
            Store.DoorsOpened[level] |= n is >= 0 and < 32 ? 1u << n : 0;
        }
    }

    /// <summary>The cells of wall group n (x &lt;&lt; 8 | y words, −1 ended) in the shown level.</summary>
    private IEnumerable<(int X, int Y)> WallGroup(int n)
    {
        if (n < 0 || n >= 64)
        {
            yield break;
        }
        var at = (ushort)LevelShown.GetWord(ListsAt + 2 * n);
        for (var j = 0; at + j + 1 < BattlefieldLevel.ImageSize; j += 2)
        {
            var w = (ushort)LevelShown.GetWord(at + j);
            if (w == 0xFFFF)
            {
                yield break;
            }
            yield return (w >> 8, w & 0xFF);
        }
    }

    /// <summary>REVEAL: wall group n is remembered as open and its cells are no longer hidden.</summary>
    // orig: the REVEAL block inlined in R023 0000, 00B8, 04AC, 0812, 0B6E (draft)
    private void RevealGroup(int n)
    {
        OpenGroupBit(Battle.LevelShown, n);
        foreach (var (x, y) in WallGroup(n))
        {
            LevelShown.SetCellByte(x, y, 3, LevelShown.CellByte(x, y, 3) & 0x7F);
        }
    }

    /// <summary>After a level is shown: the twins of linked doors, and the cells of the wall groups not opened yet are hidden.</summary>
    // orig: level-change R024+0x059A (1EF8:059A) draft (after loading the files)
    private void AfterSwitchLevel()
    {
        Array.Clear(_twinDoor);
        for (var i = 0; i < 40; i++)
        {
            var link = (sbyte)LevelShown.GetByte(DoorsAt + 14 * i + 0x0C);
            if (!_twinDoor[i] && link != -1 && link is >= 0 and < 40)
            {
                _twinDoor[link] = true;
            }
        }
        var mask = Battle.LevelShown is >= 0 and < 16 ? Store.DoorsOpened[Battle.LevelShown] : 0;
        for (var k = 0; k < LevelShown.GetByte(ListCountAt); k++)
        {
            if ((mask & 1) == 0)
            {
                foreach (var (x, y) in WallGroup(k))
                {
                    LevelShown.SetCellByte(x, y, 3, LevelShown.CellByte(x, y, 3) | 0x80);
                }
            }
            mask >>= 1;
        }
    }
}
