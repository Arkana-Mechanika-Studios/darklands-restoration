using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

/// <summary>What a level's trigger record calls (its handler word +6 before R024 relocates it to a far pointer).</summary>
public enum TriggerHandler
{
    /// <summary>0: secret_door_search (R023 0812).</summary>
    SecretDoor = 0,

    /// <summary>1: a hidden trap is discovered (R014 19CD:255A, thunk 09C0:17F7).</summary>
    HiddenTrap = 1,

    /// <summary>2: trap_spell (R023 0D88).</summary>
    SpellTrap = 2,

    /// <summary>4: trap_missile (R023 0E30).</summary>
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
// party_locations and its helpers R014 19CD:1ADE..2232, the trigger handlers 19CD:2312..255A, cbt_location_action 0E76:2170; level_switch
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

    private Generation.BattlefieldStore Store => Field.Store;

    private static bool Inside(MapPoint p, int x, int y, int w, int h) => x <= p.X && p.X < x + w && y <= p.Y && p.Y < y + h;

    private (int X, int Y, int W, int H) Box(int at) => (Level.GetWord(at), Level.GetWord(at + 2), Level.GetByte(at + 4), Level.GetByte(at + 5));

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
        foreach (var level in _levels)
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
            if (level.LevelNumber == Battle.FloorShown && (sbyte)level.Image[0x1F51] >= 0)
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
        var at = (ushort)Level.GetWord(ListsAt + 2 * n);
        for (var j = 0; at + j + 1 < BattlefieldLevel.ImageSize; j += 2)
        {
            var w = (ushort)Level.GetWord(at + j);
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
        OpenGroupBit(Battle.FloorShown, n);
        foreach (var (x, y) in WallGroup(n))
        {
            Level.SetCellByte(x, y, 3, Level.CellByte(x, y, 3) & 0x7F);
        }
    }

    /// <summary>After a level is shown: the twins of linked doors, and the cells of the wall groups not opened yet are hidden.</summary>
    // orig: level_switch R024+0x059A (1EF8:059A) draft (after loading the files)
    private void LevelShown()
    {
        Array.Clear(_twinDoor);
        for (var i = 0; i < 40; i++)
        {
            var link = (sbyte)Level.GetByte(DoorsAt + 14 * i + 0x0C);
            if (!_twinDoor[i] && link != -1 && link is >= 0 and < 40)
            {
                _twinDoor[link] = true;
            }
        }
        var mask = Battle.FloorShown is >= 0 and < 16 ? Store.DoorsOpened[Battle.FloorShown] : 0;
        for (var k = 0; k < Level.GetByte(ListCountAt); k++)
        {
            if ((mask & 1) == 0)
            {
                foreach (var (x, y) in WallGroup(k))
                {
                    Level.SetCellByte(x, y, 3, Level.CellByte(x, y, 3) | 0x80);
                }
            }
            mask >>= 1;
        }
    }

    // ---- where each member stands -------------------------------------------------------

    /// <summary>
    /// Each tick: for every able member which door, stairs, chest region and exit zone it stands
    /// in (the flags, the "place changed" bit, the leader's menu entries), then the triggers it
    /// stands in; the enemies spring the trap triggers; enemies open the unlocked doors they walk into.
    /// </summary>
    // orig: party_locations R014+0xDC14 (19CD:26A4) draft
    // C: entering or leaving a place skips the member's triggers this tick (goto entered/left).
    private void PartyLocations()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (!c.IsAble)
            {
                continue;
            }
            var was = ((int)c.Flags & 2) >> 1;
            DoorAt(i);
            if (Changed(was, 2, 0xDA) is { } door)
            {
                Moved(i, door);
                continue;
            }
            var old = (int)c.Flags;
            OnStairsAt(i);
            if (Changed(old & 1, 1, 0xD9) is { } stairs)
            {
                Moved(i, stairs);
                continue;
            }
            was = ((int)c.Flags & 4) >> 2;
            RegionAt(i);
            if (Changed(was, 4, 0xDC) is { } region)
            {
                Moved(i, region);
                continue;
            }
            was = ((int)c.Flags & 0x20) >> 5;
            ZoneAt(i);
            if (Changed(was, 0x20, 0xF8) is { } zone)
            {
                Moved(i, zone);
                continue;
            }
            Triggers(i);
            c.Flags &= ~TacticalFlags.PlaceChanged;

            bool? Changed(int before, int bit, int keep)
            {
                var now = ((int)c.Flags & bit) != 0;
                if (before == 0 && now || before != 0 && !now)
                {
                    c.Flags = (TacticalFlags)((int)c.Flags & ~(byte)~keep);
                    return now;
                }
                return null;
            }
        }
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            if (Battle[i].IsAble)
            {
                Triggers(i);
            }
        }
        for (var k = 0; k < Level.GetByte(DoorCountAt); k++)
        {
            var at = DoorsAt + 14 * k;
            if (Level.GetByte(DoorOpenAt + k) != 0 || Level.GetByte(at + 7) != 0 || Level.GetWord(at) <= 0)
            {
                continue;
            }
            var (x, y, w, h) = Box(at);
            for (var m = Combatant.PartySlots; m < Combatant.Count; m++)
            {
                if (Battle[m].IsAble && Inside(Battle[m].Position, x, y, w, h))
                {
                    SetDoor(k, true, true);
                    _view.MapChanged();
                    break;
                }
            }
        }

        void Moved(int i, bool entered)
        {
            if (entered)
            {
                Battle[i].Flags |= TacticalFlags.PlaceChanged;
            }
            _view.RowChanged(i);
        }
    }

    /// <summary>The door combatant i stands in: its side, whether using it opens it; the leader's Use Door / Pick Lock / Dissolve Lock entries.</summary>
    // orig: R014+0xD04E (19CD:1ADE) draft
    // C: a door record with x ≤ 0 is skipped without clearing the flag; without doors the flag stays as it was. A side flag missing
    // from the record keeps the last side (and with a side above 3 the wall nibble is whatever v held).
    private void DoorAt(int i)
    {
        var c = Battle[i];
        var v = 0;
        for (var k = 0; k < Level.GetByte(DoorCountAt); k++)
        {
            var at = DoorsAt + 14 * k;
            var (x0, y0, w, h) = Box(at);
            if (x0 <= 0)
            {
                continue;
            }
            if (Inside(c.Position, x0, y0, w, h))
            {
                int b8 = Level.GetByte(at + 8), b9 = Level.GetByte(at + 9);
                var cell = Level[b8 & 0x3F, b9 & 0x3F].Bits;
                if ((b8 & 0x80) != 0)
                {
                    c.DoorSide = 0;
                }
                else if ((b8 & 0x40) != 0)
                {
                    c.DoorSide = 1;
                }
                else if ((b9 & 0x80) != 0)
                {
                    c.DoorSide = 2;
                }
                else if ((b9 & 0x40) != 0)
                {
                    c.DoorSide = 3;
                }
                v = c.DoorSide switch
                {
                    0 => (int)(cell >> 16) & 0xF,
                    1 => (int)cell & 0xF,
                    2 => (int)(cell >> 24) & 0xF,
                    3 => (int)(cell >> 8) & 0xF,
                    _ => v,
                };
                c.Flags |= TacticalFlags.InDoorway;
                c.DoorOpens = v < 8;
                c.Door = k;
                break;
            }
            c.Flags &= ~TacticalFlags.InDoorway;
        }
        if (Battle.Leader != i || Paused)
        {
            return;
        }
        if ((c.Flags & TacticalFlags.InDoorway) != 0)
        {
            var locked = (sbyte)Level.GetByte(DoorsAt + 14 * c.Door + 7);
            if (locked != 0 && !Battle.RealCombat)
            {
                Menus.Set(OrdersItem.UseDoor, 2);
                Menus.Set(OrdersItem.DissolveLock, 2);
                Menus.Set(OrdersItem.PickLock, 1);
                LockIsDoor = true;
                FindEaterWater(i);
                if (c.DissolveSlot == -1)
                {
                    return;
                }
                Menus.Set(OrdersItem.DissolveLock, 1);
                return;
            }
            if (locked != 0)
            {
                return;
            }
            Menus.Set(OrdersItem.UseDoor, 1);
        }
        else
        {
            Menus.Set(OrdersItem.UseDoor, 2);
            if ((c.Flags & TacticalFlags.AtChest) != 0)
            {
                return;
            }
        }
        Menus.Set(OrdersItem.DissolveLock, 2);
        Menus.Set(OrdersItem.PickLock, 2);
    }

    /// <summary>The weakest Eater-Water (item 99) in member i's pack (FIND_PICK).</summary>
    // orig: the FIND_PICK / FIND_PICK2 macros of (checked against the asm of 19CD:1E92)
    private void FindEaterWater(int i)
    {
        var c = Battle[i];
        c.DissolveSlot = -1;
        var best = 100;
        for (var n = 0; n < c.Items.Count; n++)
        {
            if (c.Items[n].Code == 99 && (byte)c.Items[n].Quality < best)
            {
                c.DissolveSlot = n;
                best = (byte)c.Items[n].Quality;
            }
        }
    }

    /// <summary>The stairs combatant i stands on; the leader's Use Stairs entry (always on while the party is split).</summary>
    // orig: R014+0xD328 (19CD:1DB8) matched
    private void OnStairsAt(int i)
    {
        var c = Battle[i];
        for (var k = 0; k < Level.GetByte(StairsCountAt); k++)
        {
            var (x, y, w, h) = Box(StairsAt + 14 * k);
            if (Inside(c.Position, x, y, w, h))
            {
                c.StairsExit = k;
                c.Flags |= TacticalFlags.OnStairs;
                break;
            }
            c.Flags &= ~TacticalFlags.OnStairs;
        }
        if (Battle.Leader == i && !Paused)
        {
            if (Battle.PartyFloor != Battle.FloorShown)
            {
                Menus.Set(OrdersItem.UseStairs, 1);
                c.StairsExit = c.StairsBack;
                return;
            }
            Menus.Set(OrdersItem.UseStairs, (byte)((c.Flags & TacticalFlags.OnStairs) != 0 ? 1 : 2));
        }
    }

    /// <summary>The chest region combatant i stands in; the leader's Open Chest / Pick Lock / Dissolve Lock / Disarm entries.</summary>
    // orig: R014+0xD402 (19CD:1E92) draft, checked against the asm (the region's
    // +8 is the lock, +9 the trap's difficulty: the draft's "acid")
    private void RegionAt(int i)
    {
        var c = Battle[i];
        for (var k = 0; k < Level.GetByte(RegionCountAt); k++)
        {
            var at = RegionsAt + RegionSize * k;
            var (x, y, w, h) = Box(at);
            if (x <= 0)
            {
                continue;
            }
            if (Inside(c.Position, x, y, w, h))
            {
                c.Chest = k;
                c.Flags |= TacticalFlags.AtChest;
                break;
            }
            c.Flags &= ~TacticalFlags.AtChest;
        }
        if (Battle.Leader != i || Paused)
        {
            return;
        }
        if ((c.Flags & TacticalFlags.AtChest) != 0)
        {
            Menus.Set(OrdersItem.OpenChest, 2);
            Menus.Set(OrdersItem.PickLock, 2);
            Menus.Set(OrdersItem.DissolveLock, 2);
            Menus.Set(OrdersItem.DisarmTrap, 2);
            var at = RegionsAt + RegionSize * c.Chest;
            int locked = Level.GetByte(at + 8), trap = Level.GetByte(at + 9);
            if ((locked & 0x7F) != 0 && !Battle.RealCombat)
            {
                Menus.Set(OrdersItem.PickLock, 1);
                LockIsDoor = false;
                FindEaterWater(i);
                if (c.DissolveSlot != -1)
                {
                    Menus.Set(OrdersItem.DissolveLock, 1);
                }
                return;
            }
            if (trap != 0 && !Battle.RealCombat)
            {
                Menus.Set(OrdersItem.DisarmTrap, 1);
                switch (trap)
                {
                    case 0x50:
                        Menus.DisarmText = 0x628;
                        break;
                    case 0x14:
                        Menus.DisarmText = 0x5F2;
                        break;
                    case 0x32:
                        Menus.DisarmText = 0x60C;
                        break;
                }
                return;
            }
            if ((locked & 0x7F) == 0 && trap == 0 && !Battle.RealCombat)
            {
                Menus.Set(OrdersItem.OpenChest, 1);
            }
            return;
        }
        Menus.Set(OrdersItem.OpenChest, 2);
        if ((c.Flags & TacticalFlags.InDoorway) == 0)
        {
            Menus.Set(OrdersItem.PickLock, 2);
            Menus.Set(OrdersItem.DissolveLock, 2);
            Menus.Set(OrdersItem.DisarmTrap, 2);
        }
    }

    /// <summary>The exit zone combatant i stands in (on the shown level); the leader's Exit Battlefield entry.</summary>
    // orig: R014+0xD60A (19CD:209A) matched
    private void ZoneAt(int i)
    {
        var c = Battle[i];
        for (var k = 0; k < Level.GetByte(ZoneCountAt); k++)
        {
            var at = ZonesAt + 10 * k;
            int x0 = Level.GetWord(at), y0 = Level.GetWord(at + 2), w = Level.GetWord(at + 4), h = Level.GetWord(at + 6);
            if (x0 <= 0)
            {
                continue;
            }
            if (c.Floor == Battle.FloorShown && Inside(c.Position, x0, y0, w, h))
            {
                c.ExitZone = k;
                c.Flags |= TacticalFlags.InExitZone;
                break;
            }
            c.Flags &= ~TacticalFlags.InExitZone;
        }
        if (Battle.Leader == i && !Paused)
        {
            Menus.Set(OrdersItem.Exit, (byte)((c.Flags & TacticalFlags.InExitZone) != 0 ? 1 : 2));
        }
    }

    /// <summary>The triggers combatant i stands in call their handlers (enemies only spring the dart and spell traps).</summary>
    // orig: R014+0xD6DA (19CD:216A) matched
    // DIVERGENCE: a record whose handler word is not one of the eight types (a marker the generator left) would be called
    // through its raw words in the original; here nothing happens.
    private void Triggers(int i)
    {
        var c = Battle[i];
        for (var k = 0; k < Level.GetByte(TriggerCountAt); k++)
        {
            var at = TriggersAt + 14 * k;
            var (x0, y0, w, h) = Box(at);
            var handler = (TriggerHandler)Level.GetWord(at + 6);
            if (x0 > 0 && Inside(c.Position, x0, y0, w, h)
                && (i < Combatant.PartySlots || handler is TriggerHandler.MissileTrap or TriggerHandler.SpellTrap))
            {
                int a = Level.GetWord(at + 10), b = Level.GetWord(at + 12);
                switch (handler)
                {
                    case TriggerHandler.SecretDoor:
                        SearchSecretDoor(i, k, (ushort)a, b);
                        break;
                    case TriggerHandler.HiddenTrap:
                        DiscoverTrap(i, k, a, b);
                        break;
                    case TriggerHandler.SpellTrap:
                        SpellTrap(i, k, a);
                        break;
                    case TriggerHandler.MissileTrap:
                        MissileTrap(i, k, a, b);
                        break;
                    case TriggerHandler.Pit:
                        Pit(i, k, a, b);
                        break;
                    case TriggerHandler.OpenWalls:
                        OpenWalls(i, k, a);
                        break;
                    case TriggerHandler.FurnitureAlarm:
                        FurnitureAlarm(i, k, (byte)a, b);
                        break;
                    case TriggerHandler.WallAlarm:
                        WallAlarm(i, k, (byte)a, b);
                        break;
                }
            }
        }
    }

    /// <summary>The trigger record's x changes sign (on ⇄ off).</summary>
    private void ToggleTrigger(int h)
    {
        if (h is < 0 or >= 50)
        {
            return;
        }
        var at = TriggersAt + 14 * h;
        Level.SetWord(at, -Level.GetWord(at));
    }

    /// <summary>The loot pile next to member i (the selected member's, when order mode opens).</summary>
    // orig: R014+0xD7A2 (19CD:2232) draft
    private void PileNextTo(int i)
    {
        var c = Battle[i];
        c.LootPile = LootPiles.FindIndex(p => p.Level == Battle.FloorShown && p.Count != 0
            && c.Position.X - 16 <= p.Position.X && p.Position.X < c.Position.X + 16
            && c.Position.Y - 16 <= p.Position.Y && p.Position.Y < c.Position.Y + 16);
        NextToPile = c.LootPile != -1;
        if (Battle.Leader == i && !Paused)
        {
            Menus.Set(OrdersItem.LootBodies, (byte)(NextToPile && !Battle.RealCombat ? 1 : 2));
        }
    }

    // ---- the trigger handlers ------------------------------------------------------------

    /// <summary>A pit: when the whole party stands in it, they fall ("Aaaahh!"), the battle ends with result 6.</summary>
    // orig: R014+0xD882 (19CD:2312) draft
    private void Pit(int i, int k, int n, int card)
    {
        _ = i;
        var (x, y, w, h) = Box(TriggersAt + 14 * k);
        for (var m = 0; m < Combatant.PartySlots; m++)
        {
            if (Battle[m].IsAble && !Inside(Battle[m].Position, x, y, w, h))
            {
                return;
            }
        }
        Level.SetWord(TriggersAt + 14 * k, -1);
        var at = FurnitureRemovalAt + 10 * (n & 0xFF);
        // C: the direction byte is pushed zero-extended (checked against the asm).
        PlaceParty(Level.GetWord(at + 6), Level.GetWord(at + 8), Level.GetByte(at + 5));
        _stopped = true;
        Battle.Result = BattleResult.FellIntoPit;
        Battle.ResultCard = card;
        _view.Message(PitText, 60);
    }

    /// <summary>DS:1BCA "Aaaahh!" (a literal of 19CD:2312).</summary>
    public const int PitText = 0x1BCA;

    /// <summary>DS:CA29: the battle is stopped (a pit or a timed exit): the ticks do nothing more.</summary>
    private bool _stopped;

    /// <summary>An alarm outside real combat: furniture n is removed, the battle ends after a moment with result 7 and the trigger is remembered.</summary>
    // orig: R014+0xD966 (19CD:23F6) draft
    private void FurnitureAlarm(int i, int k, byte n, int card) => Alarm(i, k, n, card, FurnitureRemovalAt, true);

    // orig: R014+0xDA18 (19CD:24A8) draft
    private void WallAlarm(int i, int k, byte n, int card) => Alarm(i, k, n, card, WallRemovalAt, false);

    private void Alarm(int i, int k, byte n, int card, int records, bool furniture)
    {
        _ = i;
        if (Battle.RealCombat)
        {
            return;
        }
        Battle.ResultCard = card;
        var at = records + 10 * n;
        if (Level.GetByte(at) == 1)
        {
            if (furniture)
            {
                RemoveFurniture(n, k);
            }
            else
            {
                RemoveWall(n, k);
            }
        }
        Battle.Result = BattleResult.TimedExit;
        if (!_stopped)
        {
            Battle.TimedExitTime = Clock;
            NextTickAt = (ushort)(Clock + 1000);
            _stopped = true;
        }
        // C: effect_start(a, b, c): a furniture record holds them at +6, +8 and +5, a wall record at +4, +6 and +8; c zero-extended
        // (checked against the asm of both handlers).
        if (furniture)
        {
            PlaceParty(Level.GetWord(at + 6), Level.GetWord(at + 8), Level.GetByte(at + 5));
        }
        else
        {
            PlaceParty(Level.GetWord(at + 4), Level.GetWord(at + 6), Level.GetByte(at + 8));
        }
        var (x, y, w, h) = Box(TriggersAt + 14 * k);
        AlarmTrigger = (x, y, w, h);
    }

    /// <summary>A hidden trap: a member whose perception is above the skill finds it; it is drawn into the floor and everybody stops.</summary>
    // orig: R014+0xDACA (19CD:255A) matched
    private void DiscoverTrap(int i, int k, int n, int skill)
    {
        var c = Battle[i];
        if (c.Attributes[3] <= skill)
        {
            return;
        }
        ToggleTrigger(k);
        var at = TrapPatchesAt + 12 * n;
        for (var m = 0; m < 4; m++)
        {
            int v = Level.GetByte(at + m), row = Level.GetByte(at + 4 + m), f = Level.GetByte(at + 8 + m);
            if (v == 0)
            {
                continue;
            }
            if (f < 0x80)
            {
                Level.SetFloorByte(v, row, (byte)(Level.FloorByte(v, row) | f));
            }
            else
            {
                // C: the shift is 12 (the furniture decoration nibble) or 20 (the north wall's) by bit 7 of the column.
                var shift = (v & 0x80) == 0 ? 0x0C : 0x14;
                var x = v & 0x3F;
                Level[x, row] = new BattlefieldCell(Level[x, row].Bits | (uint)((f & 0x7F) << shift));
            }
        }
        _view.Message(DiscoversTrapText, 150, c.Index);
        _view.MapChanged();
        for (var m = 0; m < Combatant.PartySlots; m++)
        {
            Battle[m].Moving = false;
        }
    }

    /// <summary>A wall group opens (a room's walls are uncovered) and the trigger goes off.</summary>
    // orig: R023+0x0000 (1EF8:0000) draft
    private void OpenWalls(int who, int h, int n)
    {
        _ = who;
        if (n >= 0)
        {
            RevealGroup(n);
        }
        ToggleTrigger(h);
        _view.MapChanged();
    }

    /// <summary>A member searches: with perception above the roll the secret door is found (its wall cleared, its group uncovered).</summary>
    /// <param name="record">Low byte the door, high byte a second trigger to switch (0xFF none).</param>
    // orig: secret_door_search R023+0x0812 (1EF8:0812) draft
    private void SearchSecretDoor(int who, int h, ushort record, int roll)
    {
        var c = Battle[who];
        int n = record & 0xFF, h2 = record >> 8;
        if (c.Attributes[3] <= roll)
        {
            return;
        }
        ToggleTrigger(h);
        if (h2 != 0xFF)
        {
            ToggleTrigger(h2);
        }
        var at = DoorsAt + 14 * n;
        int b8 = Level.GetByte(at + 8), b9 = Level.GetByte(at + 9);
        if ((b8 & 0x80) != 0)
        {
            c.DoorSide = 0;
        }
        else if ((b8 & 0x40) != 0)
        {
            c.DoorSide = 1;
        }
        else if ((b9 & 0x80) != 0)
        {
            c.DoorSide = 2;
        }
        else if ((b9 & 0x40) != 0)
        {
            c.DoorSide = 3;
        }
        int x = b8 & 0x3F, y = b9 & 0x3F;
        switch (c.DoorSide)
        {
            case 0:
                Level.SetCellByte(x, y, 2, Level.CellByte(x, y, 2) & 0xF0);
                Level.SetCellByte(x, y - 1, 3, Level.CellByte(x, y - 1, 3) & 0xF0);
                break;
            case 1:
                Level.SetCellByte(x, y, 0, Level.CellByte(x, y, 0) & 0xF0);
                Level.SetCellByte(x + 1, y, 1, Level.CellByte(x + 1, y, 1) & 0xF0);
                break;
            case 2:
                Level.SetCellByte(x, y, 3, Level.CellByte(x, y, 3) & 0xF0);
                Level.SetCellByte(x, y + 1, 2, Level.CellByte(x, y + 1, 2) & 0xF0);
                break;
            case 3:
                Level.SetCellByte(x, y, 1, Level.CellByte(x, y, 1) & 0xF0);
                Level.SetCellByte(x - 1, y, 0, Level.CellByte(x - 1, y, 0) & 0xF0);
                break;
        }
        _view.Message(FindsSecretDoorText, 150, who);
        var wall = (sbyte)Level.GetByte(at + 6);
        if (wall >= 0)
        {
            RevealGroup(wall);
        }
        _view.MapChanged();
    }

    /// <summary>Furniture n goes (a trigger's alarm): the trigger is switched and the two cells' furniture removed.</summary>
    // orig: R023+0x0AEA (1EF8:0AEA) draft
    private void RemoveFurniture(int n, int h)
    {
        ToggleTrigger(h);
        var at = FurnitureRemovalAt + 10 * n;
        int x = Level.GetByte(at + 1), y = Level.GetByte(at + 2), x2 = Level.GetByte(at + 3), y2 = Level.GetByte(at + 4);
        if (x != 0)
        {
            Level.SetCellByte(x, y, 0, Level.CellByte(x, y, 0) & 0x0F);
        }
        if (x2 != 0)
        {
            Level.SetCellByte(x2, y2, 0, Level.CellByte(x2, y2, 0) & 0x0F);
        }
    }

    /// <summary>Wall n goes (a trigger's alarm): its group is uncovered and the wall between the two cells removed.</summary>
    // orig: R023+0x0B6E (1EF8:0B6E) draft; R023+0x0D1A (1EF8:0D1A) matched
    // C: the wall byte is signed: a group of 0 is not uncovered (q->wall > 0).
    private void RemoveWall(int n, int h)
    {
        ToggleTrigger(h);
        var at = WallRemovalAt + 10 * n;
        var wall = (sbyte)Level.GetByte(at + 3);
        if (wall > 0)
        {
            RevealGroup(wall);
        }
        int bx = Level.GetByte(at + 1), by = Level.GetByte(at + 2);
        var dir = (bx & 0x80) != 0 ? 0 : (bx & 0x40) != 0 ? 1 : (by & 0x80) != 0 ? 2 : (by & 0x40) != 0 ? 3 : 0;
        int x = bx & 0x3F, y = by & 0x3F;
        switch (dir)
        {
            case 0:
                ClearWall(x, y, 0);
                ClearWall(x, y - 1, 2);
                break;
            case 1:
                ClearWall(x, y, 1);
                ClearWall(x + 1, y, 3);
                break;
            case 2:
                ClearWall(x, y, 2);
                ClearWall(x, y + 1, 0);
                break;
            case 3:
                ClearWall(x, y, 3);
                ClearWall(x - 1, y, 1);
                break;
        }
    }

    /// <summary>The wall on side dir of a cell: north and west bytes cleared whole, east and south only their nibble.</summary>
    private void ClearWall(int x, int y, int dir)
    {
        switch (dir)
        {
            case 0:
                Level.SetCellByte(x, y, 2, 0);
                break;
            case 1:
                Level.SetCellByte(x, y, 0, Level.CellByte(x, y, 0) & 0xF0);
                break;
            case 2:
                Level.SetCellByte(x, y, 3, Level.CellByte(x, y, 3) & 0xF0);
                break;
            case 3:
                Level.SetCellByte(x, y, 1, 0);
                break;
        }
    }

    /// <summary>A spell trap goes off at the one who stepped on it ("Oops!"); it and its twin are switched off.</summary>
    // orig: trap_spell R023+0x0D88 (1EF8:0D88) matched
    private void SpellTrap(int who, int h, int t)
    {
        _view.Message(OopsText, 60);
        var at = MissileTrapsAt + 10 * t;
        var c = Battle[who];
        Effects.Lands(c.Position.X, c.Position.Y, who, Level.GetByte(at + 2), Level.GetByte(at + 3), who, Battle.FloorShown);
        ToggleTrigger(h);
        var link = Level.GetWord(at);
        if (link is >= 0 and < 50 && Level.GetWord(TriggersAt + 14 * link) >= 0)
        {
            ToggleTrigger(link);
        }
    }

    /// <summary>
    /// A dart trap (and its twin t2) shoots its volley from a wall along a line: who stands in a
    /// dart's line dodges by agility or is hit by the dart weapon against armour.
    /// </summary>
    // orig: trap_missile R023+0x0E30 (1EF8:0E30) draft
    // C: the dart lines use the walking half sizes DS:119A / DS:11A6 of the party for the members (the combat boxes are put back after).
    private void MissileTrap(int who, int h, int t, int t2)
    {
        _ = who;
        _view.Message(OopsText, 60);
        ToggleTrigger(h);
        var cur = t;
        var link = Level.GetWord(MissileTrapsAt + 10 * cur);
        if (link is >= 0 and < 50 && Level.GetWord(TriggersAt + 14 * link) >= 0)
        {
            ToggleTrigger(link);
        }
        var saved = new (int W, int H)[Combatant.PartySlots];
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            saved[i] = (Battle[i].HalfWidth, Battle[i].HalfHeight);
            Battle[i].HalfWidth = Slots[i].HalfWidth;
            Battle[i].HalfHeight = Slots[i].HalfHeight;
        }
        while (true)
        {
            var at = MissileTrapsAt + 10 * cur;
            int x = Level.GetWord(at + 2), y = Level.GetWord(at + 4), cnt = (sbyte)Level.GetByte(at + 6), dir = Level.GetByte(at + 7);
            int weapon = Level.GetByte(at + 8), skill = Level.GetByte(at + 9);
            int cx = x >> 5, cy = y >> 4;
            for (var i = 0; i < cnt; i++)
            {
                MapPoint from, to;
                int k;
                switch (dir)
                {
                    case 0:
                        from = new MapPoint(DartOffset(DartAlongX, cnt, i) + x, y - 3);
                        to = new MapPoint(from.X, (cy << 4) - 0xEC);
                        for (k = 0; k < 16; k++)
                        {
                            if ((Level.CellByte(cx, cy - k, 2) & 0x0F) != 0)
                            {
                                to = new MapPoint(from.X, ((cy - k) << 4) + 4);
                                break;
                            }
                        }
                        break;
                    case 2:
                        from = new MapPoint(x + 3, DartOffset(DartAlongY, cnt, i) + y);
                        to = new MapPoint((cx << 5) + 0x1FA, from.Y);
                        for (k = 0; k < 16; k++)
                        {
                            if ((Level.CellByte(cx + k, cy, 0) & 0x0F) != 0)
                            {
                                to = new MapPoint(((cx + k) << 5) + 0x1A, from.Y);
                                break;
                            }
                        }
                        break;
                    case 4:
                        from = new MapPoint(DartOffset(DartAlongX, cnt, i) + x, y + 3);
                        to = new MapPoint(from.X, (cy << 4) + 0xFB);
                        for (k = 0; k < 16; k++)
                        {
                            // C: the word at byte 2 of the cell masked with 0xF00: the low nibble of byte 3 (the south wall).
                            if ((Level.CellByte(cx, cy + k, 3) & 0x0F) != 0)
                            {
                                to = new MapPoint(from.X, ((cy + k) << 4) + 0x0B);
                                break;
                            }
                        }
                        break;
                    case 6:
                        from = new MapPoint(x - 3, DartOffset(DartAlongY, cnt, i) + y);
                        to = new MapPoint((cx << 5) - 0x1DA, from.Y);
                        for (k = 0; k < 16; k++)
                        {
                            if ((Level.CellByte(cx - k, cy, 1) & 0x0F) != 0)
                            {
                                to = new MapPoint(((cx - k) << 5) + 6, from.Y);
                                break;
                            }
                        }
                        break;
                    default:
                        // DIVERGENCE: an odd direction leaves the line's globals as the last test left them in the original.
                        from = to = new MapPoint(x, y);
                        break;
                }
                var target = Map.LineBlocked(from, to, -2, -2, 0);
                if (target < 50)
                {
                    LaunchTrapMissile(-2, -1, dir, weapon, from, to);
                    continue;
                }
                target -= 50;
                var tc = Battle[target];
                if (tc.Agility >= Scaled(100))
                {
                    LaunchTrapMissile(-2, target, dir, weapon, from, to);
                    continue;
                }
                var dmg = Scaled(6) + Battle.Tables.WeaponDamage(weapon) - 2;
                var cls = Battle.Tables.WeaponClass(weapon) & 0x0F;
                int armour = tc.ArmourValue[0];
                var d = cls > armour ? dmg : cls == armour ? dmg / 2 : dmg / 8;
                d += (skill - tc.ArmourQuality[0]) / 10;
                Attacks.QueueDamage(Battle, d, target, cls, armour);
                LaunchTrapMissile(-1, target, dir, weapon, from, to);
            }
            if (t == cur && t2 != 0xFF)
            {
                cur = t2;
                continue;
            }
            break;
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            (Battle[i].HalfWidth, Battle[i].HalfHeight) = saved[i];
        }
    }

    /// <summary>DS:23E5..: DS:23E5[n][3] and DS:23EF[n][3], where the darts of a volley of n leave the wall along x (directions 0, 4) and
    /// along y (2, 6): one dart 0; two −8, 8 (−4, 4); three −12, 0, 12 (−6, 0, 6). Kept with the bytes after them (a bigger volley reads on).</summary>
    // orig: DS:23E5 (data of R023; the first byte is the last letter of "Select %Fs")
    private static readonly byte[] DartData =
    [
        0x73, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x08, 0x00, 0xF4, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00,
        0xFC, 0x04, 0x00, 0xFA, 0x00, 0x06, 0x00, 0x61, 0x00, 0x74, 0x61, 0x63, 0x2E, 0x74, 0x78, 0x74,
        0x00, 0x45, 0x6E, 0x65, 0x6D, 0x79, 0x20, 0x41, 0x63, 0x74, 0x69, 0x76, 0x61, 0x74, 0x69, 0x6F,
        0x6E, 0x20, 0x52, 0x65, 0x63, 0x6F, 0x72, 0x64, 0x20, 0x66, 0x6F, 0x72, 0x20, 0x6C, 0x65, 0x76,
    ];

    private const int DartAlongX = 0, DartAlongY = 10;

    private static int DartOffset(int table, int cnt, int i) => table + cnt * 3 + i is var at && at >= 0 && at < DartData.Length ? (sbyte)DartData[at] : 0;
}
