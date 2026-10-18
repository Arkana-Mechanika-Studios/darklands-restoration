namespace Darklands.Core.Battles;

/// <summary>
/// What the original keeps of its last battle between battles and writes into every saved game's battle file
/// (DKSAVEn.BSV): the battle's variables as the battle-state save lays them out, the last battlefield's level files
/// (level%d.flr, .atv, .enm), terrain.fil and the loot file, and the header words of the battle file last written or
/// read. A load replaces it with the battle file's content; a battle fought here writes it at its end (and a save
/// inside a battle writes it from the battle running).
/// </summary>
// orig: the globals and work files the battle-state save R003+0x00DC (0E76:00DC) reads and the battle-state restore R003+0x0CE8 (0E76:0CE8) writes
// draft; the header DS:D91B (0x2A bytes); tests: BattleSaveTests, OriginalSaveTests
public sealed class BattleFile
{
    /// <summary>The variables' size (the first block of the battle file).</summary>
    public const int VariablesSize = 0x1978;

    /// <summary>The header's words: [0] the variables' packed size, [1..16] each level's, [17..18] the loot file's raw and packed size, [19..20] terrain.fil's.</summary>
    public const int HeaderWords = 21;

    /// <summary>The battle variables (<see cref="BattleVariables"/>).</summary>
    public byte[] Variables { get; set; } = new byte[VariablesSize];

    /// <summary>The last battlefield's levels by number: the floor image (object functions as numbers), the .atv and the .enm data.</summary>
    public List<LevelFiles> Levels { get; } = [];

    /// <summary>terrain.fil (DS:1164, 0xC4 bytes), null when the file does not exist.</summary>
    public byte[]? Terrain { get; set; }

    /// <summary>The loot file (DS:1162), null when it does not exist (an empty file is an empty array).</summary>
    public byte[]? Loot { get; set; }

    /// <summary>DS:D91B: the header as last written or read (the words a save does not rewrite keep their values).</summary>
    public ushort[] Header { get; } = new ushort[HeaderWords];

    /// <summary>The compressor's work area (the persistence layer's; it outlives each save, as the original's buffer).</summary>
    public object? Compressor { get; set; }

    /// <summary>DS:907A at +0: the battle file was written inside a battle.</summary>
    public bool InBattle => Variables[BattleVariables.InBattle] == 1;

    /// <summary>The number of levels of the last battlefield (DS:D661).</summary>
    public int LevelCount => Variables[BattleVariables.LevelCount];

    /// <summary>A copy (the compressor and the header are shared state of the program and are carried, not copied).</summary>
    public BattleFile Clone()
    {
        var copy = new BattleFile
        {
            Variables = (byte[])Variables.Clone(),
            Terrain = (byte[]?)Terrain?.Clone(),
            Loot = (byte[]?)Loot?.Clone(),
            Compressor = Compressor,
        };
        Header.CopyTo(copy.Header, 0);
        copy.Levels.AddRange(Levels.Select(l => l with { Floor = (byte[])l.Floor.Clone(), Activations = (byte[])l.Activations.Clone(), Enemies = (byte[])l.Enemies.Clone() }));
        return copy;
    }
}

/// <summary>One level's files: the floor image (0x33FC bytes), the .atv data (count + 30 bytes each), the .enm data (count + 0x122 bytes each).</summary>
public sealed record LevelFiles(byte[] Floor, byte[] Activations, byte[] Enemies);

/// <summary>
/// The layout of the battle variables (the first block of the battle file): the globals in the order the battle-state
/// save writes them. Per-combatant tables have 13 entries (5 party, 8 enemies), per-member tables 5.
/// </summary>
// orig: the battle-state save R003+0x00DC (0E76:00DC) draft (the serialisation order matches the bytes)
public static class BattleVariables
{
    /// <summary>DS:907A: 1 inside a battle.</summary>
    public const int InBattle = 0x0000;

    /// <summary>20A5:6238: 13 × 250 path bytes.</summary>
    public const int Paths = 0x0001;

    /// <summary>DS:D6FB: the next path byte of each combatant.</summary>
    public const int PathIndex = 0x0CB3;

    /// <summary>DS:C3CD: the stairs record the enemies already in records 5..12 are placed around (14 bytes).</summary>
    public const int InitialSpot = 0x0CC0;

    /// <summary>DS:C600: the collision test ignores the combatants.</summary>
    public const int IgnorePeople = 0x0CCE;

    /// <summary>DS:C7A4: 20 words, the chests' offsets in the loot file.</summary>
    public const int ChestOffsets = 0x0CCF;

    /// <summary>20A5:59C8: the loot piles, 180 records of 12 bytes.</summary>
    public const int Piles = 0x0CF7;

    public const int PileSize = 12;

    public const int MaxPiles = 180;

    /// <summary>DS:C80E the pile count, then DS:C83E (the first pile not gathered).</summary>
    public const int PileCount = 0x1567;

    public const int Gathered = 0x1568;

    /// <summary>DS:D6F0: hidden encounters are checked.</summary>
    public const int ActivationsOn = 0x1569;

    /// <summary>DS:D728, DS:D73B, DS:D7FE, DS:D947 (bytes) and DS:D945 (word): the cursor keys' state.</summary>
    public const int CursorKeys = 0x156A;

    /// <summary>DS:D842: the enemies wait for the delayed start.</summary>
    public const int DelayedStart = 0x1570;

    /// <summary>DS:C1F0: the battlefield type of the battle.</summary>
    public const int BattlefieldType = 0x1571;

    /// <summary>DS:C624: the marching order, five slots and the length.</summary>
    public const int MarchingOrder = 0x1573;

    /// <summary>DS:C64C, DS:C678, DS:C6EF, DS:C77E, DS:C784, DS:C7CD, DS:C7FE, DS:C8B2: five bytes each (per member).</summary>
    public const int MemberDoor = 0x1579;

    public const int MemberDoorOpens = 0x157E;

    public const int MemberDoorSide = 0x1583;

    public const int MemberStairs = 0x1588;

    public const int MemberChest = 0x158D;

    public const int MemberDissolveSlot = 0x1592;

    public const int MemberExitZone = 0x1597;

    public const int MemberStairsBack = 0x159C;

    /// <summary>DS:D610: the party may move as a group.</summary>
    public const int GroupMove = 0x15A1;

    /// <summary>DS:D6F4: the alarm.</summary>
    public const int Alarm = 0x15A2;

    /// <summary>The pointer shape in use (a near pointer).</summary>
    public const int Cursor = 0x15A3;

    /// <summary>The per-combatant byte tables (13 each): DS:A6D3 moving, DS:CA57 exchange, DS:CA64 action state, DS:D3FE, DS:D4C7 damage shown, DS:D4E5, DS:D4F2 was hit, DS:A6B4 floor, DS:D5E9 gave up, DS:DA36.</summary>
    public const int Moving = 0x15A5;

    public const int Exchange = 0x15B2;

    public const int State = 0x15BF;

    public const int AttackFacing = 0x15CC;

    public const int DamageShown = 0x15D9;

    public const int DamageNumber = 0x15E6;

    public const int WasHit = 0x15F3;

    public const int Floor = 0x1600;

    public const int GaveUp = 0x160D;

    public const int EngagedByAi = 0x161A;

    /// <summary>The per-combatant word tables: DS:D5F6 (what blocked the step), DS:D62B (only saved), DS:A696 the flags.</summary>
    public const int BlockedBy = 0x1627;

    public const int Unused1641 = 0x1641;

    public const int Flags = 0x165B;

    /// <summary>DS:A6C4 the place actions (13 bytes), DS:C1C1 (13 bytes: an enemy kind's base load at its placement).</summary>
    public const int Place = 0x1675;

    public const int PlacedBaseLoad = 0x1682;

    /// <summary>2E38:0BB6: the wall groups opened, 16 longs.</summary>
    public const int DoorsOpened = 0x168F;

    public const int RealCombat = 0x16CF;

    /// <summary>DS:C4FD: an enemy has seen a member.</summary>
    public const int EnemySaw = 0x16D0;

    public const int FloorShown = 0x16D1;

    public const int PartyFloor = 0x16D2;

    /// <summary>DS:C651: the enemies' floor as the end-of-battle test last found it.</summary>
    public const int EnemyFloor = 0x16D3;

    /// <summary>DS:C925, DS:C978 (words), DS:C989, DS:CA1B (bytes): the alarm trigger.</summary>
    public const int AlarmTrigger = 0x16D4;

    /// <summary>DS:CA29: a pit or an alarm stopped the battle.</summary>
    public const int Stopped = 0x16DA;

    /// <summary>DS:07E6, DS:CA3F: words only saved and restored.</summary>
    public const int Unused16DB = 0x16DB;

    /// <summary>DS:CA99: the lock entries are about a door.</summary>
    public const int LockIsDoor = 0x16DF;

    public const int Result = 0x16E0;

    public const int ResultCard = 0x16E2;

    /// <summary>DS:D661: the number of levels.</summary>
    public const int LevelCount = 0x16E4;

    /// <summary>DS:D662: the level the battlefield starts on.</summary>
    public const int StartLevel = 0x16E5;

    /// <summary>DS:D68D: the skills each member used (5 words).</summary>
    public const int SkillsUsed = 0x16E7;

    /// <summary>DS:D6CE: the battle seed (the restore reads it into DS:DAD7).</summary>
    public const int Seed = 0x16F1;

    /// <summary>DS:DA34: the cleared levels.</summary>
    public const int ClearedLevels = 0x16F3;

    /// <summary>DS:D9E6: the order queues, 13 × 3 words.</summary>
    public const int Orders = 0x16F5;

    public const int NextTickAt = 0x1743;

    /// <summary>The per-combatant clock words: DS:D9CA, DS:D9B0, DS:D996, DS:D97C, DS:D962, DS:D948, DS:DABD, DS:DAA3, DS:DA89, DS:DA6F, DS:DA55.</summary>
    public const int NextStepAt = 0x1745;

    public const int NextFrameAt = 0x175F;

    public const int EngageAt = 0x1779;

    public const int RepathAt = 0x1793;

    public const int GiveUpUntil = 0x17AD;

    public const int ReloadStartAt = 0x17C7;

    public const int AimStartAt = 0x17E1;

    public const int AimEndAt = 0x17FB;

    public const int FlashUntil = 0x1815;

    public const int DamageNumberUntil = 0x182F;

    public const int RecoverAt = 0x1849;

    /// <summary>DS:DA53, DS:DA51, DS:DA4F, DS:DA4D, DS:DA4B, DS:DA49, DS:DA47, DS:C2BB, DS:DA45: the battle's clock words.</summary>
    public const int PartyRethinkAt = 0x1863;

    public const int EnemyRethinkAt = 0x1865;

    public const int ActivationsAt = 0x1867;

    public const int TimedExitAt = 0x1869;

    public const int GroupCursorUntil = 0x186B;

    public const int CombatBoxRetryAt = 0x186D;

    public const int AlarmAt = 0x186F;

    public const int DelayedStartAt = 0x1871;

    public const int EndAt = 0x1873;

    /// <summary>DS:DA43 and 2E38:0C64: the saved path's length and its 250 bytes.</summary>
    public const int SavedPathLength = 0x1875;

    public const int Clock = 0x1877;

    public const int SavedPath = 0x1879;

    /// <summary>DS:07D8: the battle screen is up.</summary>
    public const int ScreenUp = 0x1973;

    /// <summary>DS:160C: the last castle's progress.</summary>
    public const int LastCastleFlags = 0x1974;

    /// <summary>DS:160E: only saved and restored.</summary>
    public const int Unused1976 = 0x1976;
}
