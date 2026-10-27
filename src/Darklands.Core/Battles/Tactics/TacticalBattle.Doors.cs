using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>
    /// Combatant <paramref name="who"/> uses door n: it opens (or shuts) with its linked twin and
    /// its lock is gone; the wall group behind it is uncovered, and an encounter waiting in that
    /// room comes out (and raises the alarm).
    /// </summary>
    // orig: door_toggle R023+0x00B8 (1EF8:00B8) draft, checked against the asm
    // (REVEAL, the open flags, the lock byte, the twin); R023+0x03CA (1EF8:03CA) draft
    private void ToggleDoor(int n, int who)
    {
        LevelChanged = true;
        var c = Battle[who];
        var at = DoorsAt + 14 * n;
        var wall = (sbyte)Level.GetByte(at + 6);
        if (wall >= 0)
        {
            RevealGroup(wall);
        }
        Level.SetByte(DoorOpenAt + n, c.DoorOpens ? 1 : 0);
        Level.SetByte(at + 7, 0);
        var link = (sbyte)Level.GetByte(at + 0x0C);
        if (link != -1)
        {
            Level.SetByte(DoorsAt + 14 * link + 7, 0);
            Level.SetByte(DoorOpenAt + link, c.DoorOpens ? 1 : 0);
        }
        DoorBits(at + 8, c.DoorSide, c.DoorOpens);
        if ((Level.GetByte(at + 10) & 0x3F) != 0)
        {
            DoorBits(at + 10, c.DoorSide, c.DoorOpens);
        }
        if (wall != -1 && ActivationsEnabled)
        {
            var list = Activations[Battle.FloorShown];
            for (var k = 0; k < list.Count; k++)
            {
                // C: R023+0x0362..0x0368: the encounter's +0x00 word (X; negative while disabled) must be > 0.
                if (list[k].Room == wall && list[k].X > 0)
                {
                    if (PlaceEnemies(0, list[k], who))
                    {
                        Battle.Alarm = 1;
                        AlarmAt = (ushort)(Scaled(300) + Clock + 300);
                    }
                    list.RemoveAt(k);
                    break;
                }
            }
        }
    }

    /// <summary>The door bit (8) of the wall on the door's side of its cell and of the neighbour across: set when it opens, cleared when it shuts.</summary>
    /// <param name="cellAt">The record's cell bytes (+8/+9 or +10/+11).</param>
    private void DoorBits(int cellAt, int side, bool open)
    {
        int x = Level.GetByte(cellAt) & 0x3F, y = Level.GetByte(cellAt + 1) & 0x3F;
        switch (side)
        {
            case 0:
                DoorBit(x, y, 0, open);
                DoorBit(x, y - 1, 2, open);
                break;
            case 1:
                DoorBit(x, y, 1, open);
                DoorBit(x + 1, y, 3, open);
                break;
            case 2:
                DoorBit(x, y, 2, open);
                DoorBit(x, y + 1, 0, open);
                break;
            case 3:
                DoorBit(x, y, 3, open);
                DoorBit(x - 1, y, 1, open);
                break;
        }
    }

    /// <summary>Bit 3 of the wall nibble on side dir (0 north: byte 2; 1 east: byte 0; 2 south: byte 3; 3 west: byte 1).</summary>
    // orig: R023+0x03CA / R023+0x0734 (1EF8:0734) draft
    private void DoorBit(int x, int y, int dir, bool open)
    {
        var k = dir switch { 0 => 2, 1 => 0, 2 => 3, _ => 1 };
        var b = Level.CellByte(x, y, k);
        Level.SetCellByte(x, y, k, open ? b | 8 : b & ~8);
    }

    /// <summary>Door n opens or shuts by itself (an enemy walks into it, or a path search tries it), its group uncovered when <paramref name="reveal"/>.</summary>
    // orig: door_set R023+0x04AC (1EF8:04AC) draft
    // DIVERGENCE: a door record with none of the four side flags (+8 and +9, bits 0x80 / 0x40) leaves the direction local bp-6 uninitialised in the original
    //   (R023+0x04C8 .. +0x050B), so the door bits go to the side the stale stack word names, or to no side when it is not 0..3 (the two switches
    //   R023+0x0620 / +0x06B8 have no default); north (0) is used here. Not reached in play: the three callers (R014+0x4924, +0x4B95, +0xDDE1, the only
    //   calls of the vector 09C0:1D6F) take only records whose word +0 is above 0; the flagless records of IMAPS.CAT (IFORTMON.009 doors 0 and 1) and
    //   LCASTLE (door 0 of the nine levels) are all-zero, word +0 = 0; the castle generator's records always carry a flag and the map turn moves the flags.
    private void SetDoor(int n, bool open, bool reveal)
    {
        var at = DoorsAt + 14 * n;
        int b8 = Level.GetByte(at + 8), b9 = Level.GetByte(at + 9);
        var dir = (b8 & 0x80) != 0 ? 0 : (b8 & 0x40) != 0 ? 1 : (b9 & 0x80) != 0 ? 2 : (b9 & 0x40) != 0 ? 3 : 0;
        Level.SetByte(DoorOpenAt + n, open ? 1 : 0);
        if (reveal)
        {
            LevelChanged = true;
            var wall = (sbyte)Level.GetByte(at + 6);
            if (wall >= 0)
            {
                RevealGroup(wall);
            }
        }
        var link = (sbyte)Level.GetByte(at + 0x0C);
        if (link != -1)
        {
            Level.SetByte(DoorOpenAt + link, open ? 1 : 0);
        }
        DoorBits(at + 8, dir, open);
        if ((Level.GetByte(at + 10) & 0x3F) != 0)
        {
            DoorBits(at + 10, dir, open);
        }
    }

    /// <summary>For an enemy's path search: the unlocked doors that are no twin are opened, and afterwards put back as they were.</summary>
    // orig: path_find R014+0x481A (12F7:000A) draft (the DS:E0C0 block) and R014+0x4B5A (12F7:034A)
    // matched
    private void OpenDoorsForSearch(bool open)
    {
        if (open)
        {
            Level.Image.AsSpan(DoorOpenAt, 40).CopyTo(_doorsBeforeSearch);
            for (var k = 0; k < Level.GetByte(DoorCountAt); k++)
            {
                var at = DoorsAt + 14 * k;
                if (!_twinDoor[k] && Level.GetByte(at + 7) == 0 && Level.GetWord(at) > 0)
                {
                    SetDoor(k, true, false);
                }
            }
            return;
        }
        for (var k = 0; k < Level.GetByte(DoorCountAt); k++)
        {
            if (!_twinDoor[k] && Level.GetWord(DoorsAt + 14 * k) > 0)
            {
                SetDoor(k, _doorsBeforeSearch[k] == 1, false);
            }
        }
        _doorsBeforeSearch.CopyTo(Level.Image.AsSpan(DoorOpenAt, 40));
    }

    /// <summary>DS:E0C0: the open flags before an enemy's path search.</summary>
    // DIVERGENCE: the original never clears DS:E0C0 (nor saves it with the battle): a battle starts with what the last enemy search of an
    // earlier battle of the session kept there, which matters only when an enemy's first search fails at its goal (see PathFinder.Find).
    // Here each battle starts with every flag 0.
    private readonly byte[] _doorsBeforeSearch = new byte[40];

    /// <summary>The picker tries the lock at image offset <paramref name="lockAt"/> with his best… first lock picks (item 0x40): skill + quality − 25 + 1d10 against the lock.</summary>
    // orig: lock_pick R023+0x12C0 (1EF8:12C0) draft, checked against the asm
    // C: without lock picks the quality is read from the slot after the last item (or item 0 of an empty pack), but it is only used
    // when lock picks were found, so nothing depends on it.
    private bool PickLock(int lockAt)
    {
        var c = Battle[_picker];
        var k = c.Items.FindIndex(s => s.Code == 0x40);
        if (k < 0)
        {
            _view.Message(NoLockPicksText, 150);
            return false;
        }
        var s = c.Skills[14] + (byte)c.Items[k].Quality - 25;
        var locked = Level.GetByte(lockAt) & 0x7F;
        if (locked < Battle.RollOneTo(10) + s)
        {
            _view.Message(LockYieldsText, 150);
            Level.SetByte(lockAt, 0);
            c.SkillsUsed |= SkillUse.ArtificeSucceeded;
            return true;
        }
        _view.Message(locked < s + 10 ? RefusesText : BeyondSkillText, 150);
        c.SkillsUsed |= SkillUse.ArtificeTried;
        return false;
    }

    /// <summary>Member who opens the chest of his region: the lid art turns open, the party takes what it wants of the contents and the money.</summary>
    // orig: chest_open R023+0x13D0 (1EF8:13D0) draft, checked against the asm
    private void OpenChest(int who)
    {
        var at = RegionsAt + RegionSize * Battle[who].Chest;
        int cx = Level.GetByte(at + 6), cy = Level.GetByte(at + 7);
        if ((Level.CellByte(cx, cy, 0) & 0xF0) == 0x50)
        {
            // C: the cell's long += 0x10: furniture 5 (a shut chest) becomes 6 (open).
            Level[cx, cy] = new BattlefieldCell(Level[cx, cy].Bits + 0x10);
        }
        var idx = Level.GetByte(at + 0x0C);
        var loot = Store.Loot;
        int[] offset = [idx < loot.ChestOffsets.Length ? loot.ChestOffsets[idx] : -1];
        int[] count = [Level.GetByte(at + 0x0B)];
        var contents = new List<ItemStack>();
        loot.Take(contents, 0, count, offset, 1);
        var money = new Money(Level.GetWord(at + 0x0E), Level.GetWord(at + 0x10), Level.GetWord(at + 0x12));
        _view.OpenLoot(who, contents, money);
        Level.SetWord(at + 0x0E, 0);
        Level.SetWord(at + 0x10, 0);
        Level.SetWord(at + 0x12, 0);
        Level.SetByte(at + 0x0B, contents.Count);
        LevelChanged = true;
        if (contents.Count != 0 && idx < loot.ChestOffsets.Length)
        {
            loot.ChestOffsets[idx] = loot.Append(contents, contents.Count);
        }
        _view.MapChanged();
        // C: then combat_draw R014+0x6818, as each tick ends: the draw list (its random draws) and the target cursor.
        Effects.Drawn(_view);
        RemoveArrivedMissiles();
        RefreshTargetCursor();
    }

    /// <summary>Member who disarms the trap of his chest: Artifice − 10 + 1d20 against its difficulty; failing, it goes off at him.</summary>
    // orig: chest_disarm R023+0x153E (1EF8:153E) matched (checked against the asm:
    // +9 is the difficulty, +0x0A the trap's type and level)
    private bool DisarmTrap(int who)
    {
        var c = Battle[who];
        var at = RegionsAt + RegionSize * c.Chest;
        var s = c.Skills[14] - 10;
        var r = Battle.RollOneTo(20);
        if (Level.GetByte(at + 9) < s + r)
        {
            _view.Message(TrapDisarmedText, 150);
            c.SkillsUsed |= SkillUse.ArtificeSucceeded;
            Level.SetByte(at + 9, 0);
            return true;
        }
        _view.Message(OopsText, 60);
        c.SkillsUsed |= SkillUse.ArtificeTried;
        var trap = Level.GetByte(at + 0x0A);
        Effects.Lands(c.Position.X, c.Position.Y, who, trap & 0x0F, (trap >> 4) * 10, who, Battle.FloorShown);
        return false;
    }

    /// <summary>The action at a place ordered from the menu.</summary>
    // orig: cbt_location_action R014+0x2170 (0E76:2170) matched
    // C: every case is a separate if (the action is cancelled at the end of each), so only the first that applies acts.
    private void DoPlaceAction(int i)
    {
        var c = Battle[i];
        if (c.Place == PlaceAction.UseDoor)
        {
            ToggleDoor(c.Door, i);
            _redraw = true;
            CancelAction(i);
        }
        if (c.Place == PlaceAction.PickDoorLock)
        {
            if (PickLock(DoorsAt + 14 * c.Door + 7))
            {
                ToggleDoor(c.Door, i);
                _redraw = true;
            }
            CancelAction(i);
        }
        if (c.Place == PlaceAction.PickChestLock)
        {
            if (PickLock(RegionsAt + RegionSize * c.Chest + 8))
            {
                OpenChest(i);
                _redraw = true;
            }
            CancelAction(i);
        }
        if (c.Place == PlaceAction.DisarmChestTrap)
        {
            if (DisarmTrap(i))
            {
                OpenChest(i);
                _redraw = true;
            }
            CancelAction(i);
        }
        if (c.Place == PlaceAction.DissolveLock)
        {
            _view.Message(EaterWaterText, 150);
            UseUpDissolveSlot(i);
            if (!LockIsDoor)
            {
                OpenChest(i);
            }
            else
            {
                ToggleDoor(c.Door, i);
            }
            _redraw = true;
            CancelAction(i);
        }
        if (c.Place == PlaceAction.OpenChest)
        {
            OpenChest(i);
            _redraw = true;
            CancelAction(i);
        }
        if (c.Place == PlaceAction.Stairs)
        {
            TakeExit(c.StairsExit, i);
            CancelAction(i);
        }
        if (c.Place == PlaceAction.Leave)
        {
            c.Floor = BattleEnd.LeftThroughExit;
            _redraw = true;
            c.Flags = (c.Flags & ~TacticalFlags.InExitZone) | TacticalFlags.LeftField;
            ForgetTarget(i);
            CancelAction(i);
            _view.RowChanged(i);
        }
        if (c.Place == PlaceAction.TakeLoot)
        {
            OpenLootPile(i);
            CancelAction(i);
        }
    }

    /// <summary>DS:C77D: the byte before the members' stairs-exit table (DS:C77E, <see cref="Combatant.StairsExit"/>), which nothing reads.</summary>
    public byte StairsExitBeforeTable { get; set; }

    /// <summary>DS:C8B1: the byte before the members' stairs-back table (DS:C8B2, <see cref="Combatant.StairsBack"/>).</summary>
    public byte StairsBackBeforeTable { get; set; }

    // ---- the keys of the place actions (not in order mode) ------------------------------------

    /// <summary>
    /// U outside order mode: the leader (or, travelling together, everybody) takes the stairs, or
    /// the leader uses the door; in order mode the selected member is ordered to.
    /// </summary>
    // orig: R016+0x1182 (1EF8:1182) matched; the U case of combat_keys for a split party (R016+0x014E)
    public void UseKey(int selected)
    {
        if (Battle.PartyFloor != Battle.FloorShown)
        {
            // combat_keys with the party split: only U (and a few keys) work.
            if (Paused)
            {
                // R016+0x014E..0x016E: no test of DS:A6B2 (the order is chosen with nobody selected too)
                if (Menus.On(OrdersItem.UseStairs))
                {
                    if (selected >= 0)
                    {
                        Battle[selected].StairsExit = Battle[selected].StairsBack;
                    }
                    else if (!Battle.Behaviour.FixStairsKeyWithoutSelection)
                    {
                        // BUG: OriginalBehaviour.FixStairsKeyWithoutSelection - R016+0x0165..0x0169 copies entry [DS:A6B2] of the two member
                        // tables without testing it: with nobody selected (0xFF) entry -1, DS:C77D = DS:C8B1 (the bytes before the tables).
                        StairsExitBeforeTable = StairsBackBeforeTable;
                    }
                    ChoosePlace(PlaceAction.Stairs);
                }
                return;
            }
            var leader = Battle[Battle.Leader];
            if (leader.StairsBack != -1)
            {
                TakeExit(leader.StairsBack, Battle.Leader);
            }
            return;
        }
        if (Paused)
        {
            if (selected != -1)
            {
                if (Menus.On(OrdersItem.UseStairs))
                {
                    // orig: R016+0x1384 (1EF8:1384) matched: R016+0x1590(2)
                    ChoosePlace(PlaceAction.Stairs);
                }
                else if (Menus.On(OrdersItem.UseDoor))
                {
                    // orig: R016+0x137A (1EF8:137A) matched: R016+0x1590(1)
                    ChoosePlace(PlaceAction.UseDoor);
                }
            }
            return;
        }
        var l = Battle.Leader;
        if (Menus.On(OrdersItem.UseStairs))
        {
            if (MoveMode == 0)
            {
                TakeExit(Battle[l].StairsExit, l);
            }
            else
            {
                for (var i = 0; i < Combatant.PartySlots; i++)
                {
                    if (Battle[i].IsAble)
                    {
                        // C: every member takes the LEADER's stairs record.
                        TakeExit(Battle[l].StairsExit, i);
                    }
                }
            }
        }
        else if (Menus.On(OrdersItem.UseDoor))
        {
            ToggleDoor(Battle[l].Door, l);
            _view.MapChanged();
        }
        CancelAction(l);
    }

    /// <summary>O: the leader opens the chest (in order mode the selected member is ordered to).</summary>
    // orig: R016+0x1072 (1EF8:1072) matched
    public void OpenKey(int selected)
    {
        if (!Menus.On(OrdersItem.OpenChest))
        {
            return;
        }
        if (Paused)
        {
            if (selected != -1)
            {
                // orig: R016+0x13A2 (1EF8:13A2) matched: R016+0x1590(5)
                ChoosePlace(PlaceAction.OpenChest);
            }
            return;
        }
        OpenChest(Battle.Leader);
        _view.MapChanged();
        CancelAction(Battle.Leader);
    }

    /// <summary>P: Parry in order mode when the Attack entry is on; else the leader picks the lock (door or chest).</summary>
    // orig: R016+0x10B2 (1EF8:10B2) matched
    public bool PickKey(int selected)
    {
        if (Paused)
        {
            if (Menus.On(AttackItem.Parry) && selected != -1)
            {
                return true;
            }
            // R016+0x10CC: the pick has no test of DS:A6B2 (Parry has, R016+0x10C0)
            if (Menus.On(OrdersItem.PickLock))
            {
                // orig: R016+0x1398 (1EF8:1398) matched (R016+0x1590(3), a door) and R016+0x13AC
                // (1EF8:13AC) matched (R016+0x1590(6), a chest),
                ChoosePlace(LockIsDoor ? PlaceAction.PickDoorLock : PlaceAction.PickChestLock);
            }
            return false;
        }
        if (!Menus.On(OrdersItem.PickLock))
        {
            return false;
        }
        var l = Battle.Leader;
        _picker = l;
        if (!LockIsDoor)
        {
            if (PickLock(RegionsAt + RegionSize * Battle[l].Chest + 8))
            {
                OpenChest(l);
                _view.MapChanged();
            }
        }
        else if (PickLock(DoorsAt + 14 * Battle[l].Door + 7))
        {
            ToggleDoor(Battle[l].Door, l);
            _view.MapChanged();
        }
        CancelAction(l);
        return false;
    }

    /// <summary>D: in order mode Dissolve Lock or Disarm is ordered; else the leader dissolves the lock with Eater-Water or disarms the chest's trap.</summary>
    // orig: R016+0x0F76 (1EF8:0F76) matched
    public void DissolveKey(int selected)
    {
        if (Paused)
        {
            // R016+0x0F76..0x0F95: no test of DS:A6B2
            if (Menus.On(OrdersItem.DissolveLock))
            {
                // orig: R016+0x13C0 (1EF8:13C0) matched: R016+0x1590(7)
                ChoosePlace(PlaceAction.DissolveLock);
                return;
            }
            if (Menus.On(OrdersItem.DisarmTrap))
            {
                // orig: R016+0x13B6 (1EF8:13B6) matched: R016+0x1590(4)
                ChoosePlace(PlaceAction.DisarmChestTrap);
            }
            return;
        }
        var l = Battle.Leader;
        if (Menus.On(OrdersItem.DissolveLock))
        {
            _view.Message(EaterWaterText, 150);
            // C: the slot of the SELECTED member (−1 outside order mode reads before the table) is used up.
            if (selected >= 0 && Battle[selected].DissolveSlot >= 0 && Battle[selected].DissolveSlot < Battle[selected].Items.Count)
            {
                Attacks.ConsumeSlot(Battle, selected, Battle[selected].DissolveSlot);
            }
            if (LockIsDoor)
            {
                ToggleDoor(Battle[l].Door, l);
                _view.MapChanged();
                CancelAction(l);
                return;
            }
            OpenChest(l);
            _view.MapChanged();
        }
        else if (Menus.On(OrdersItem.DisarmTrap) && DisarmTrap(l))
        {
            OpenChest(l);
            _view.MapChanged();
        }
        CancelAction(l);
    }

    // ---- stairs and levels ------------------------------------------------------------------

    /// <summary>
    /// Combatant <paramref name="who"/> takes stairs record n to another level: when no able member
    /// is left behind the party moves (the enemies follow or stay as an encounter); it is placed
    /// round the stairs' spot there.
    /// </summary>
    // orig: take_exit R023+0x1618 (1EF8:1618) draft
    // C: the wall group of the stairs is uncovered on the NEW level (doors_opened of that level; the list read from the new image).
    private void TakeExit(int n, int who)
    {
        if (n < 0 || n >= Level.GetByte(StairsCountAt))
        {
            return;
        }
        var old = Battle.FloorShown;
        var at = StairsAt + 14 * n;
        var record = Level.Image.AsSpan(at, 14).ToArray();
        var wall = (sbyte)Level.GetByte(at + 7);
        // BUG: OriginalBehaviour.FixStairsWallMarkLost - the write below goes to the level's buffer, and level_switch (R024+0x05B4, called
        // at R023+0x16C7) saves the level it leaves only when DS:D4E4 is set (LevelChanged: any change of the level sets it; level_switch
        // and the battle's end R014+0x069A clear it), which this write does not do: after a switch with DS:D4E4 clear the original
        // reloads the old level without the 0xFF (the levels stay in memory here, the mark is taken back by SaveLevelOrLoseUnsaved).
        if (wall != -1 && !Battle.Behaviour.FixStairsWallMarkLost)
        {
            _unsavedLevelBytes.Add((at + 7, (byte)wall));
        }
        Level.SetByte(at + 7, 0xFF);
        int lvl = (sbyte)Level.GetByte(at + 6);
        Battle[who].Floor = lvl;
        var alone = true;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (Battle[i].IsAble && Battle[i].Floor != lvl)
            {
                alone = false;
            }
        }
        if (alone)
        {
            Battle.PartyFloor = lvl;
            LeaveLevel(who, record);
            Battle.RealCombat = false;
        }
        SwitchLevel(lvl);
        if (wall != -1)
        {
            // orig: R023+0x1764: the wall group opened on the new level, DS:D4E4 = 1
            LevelChanged = true;
            OpenGroupBit(lvl, wall);
            foreach (var (x, y) in WallGroup(wall))
            {
                // C: this copy reads the list words as (y, x) bytes: c[1] is the column, c[0] the row (the same cells).
                Level.SetCellByte(x, y, 3, Level.CellByte(x, y, 3) & 0x7F);
            }
        }
        var spot = new MapPoint((short)(record[8] | record[9] << 8), (short)(record[10] | record[11] << 8));
        PlaceAround(who, spot, record[12]);
        if (Battle.PartyFloor == lvl)
        {
            ViewOrigin = null;
            _view.MapChanged();
        }
        else
        {
            SwitchLevel(old);
        }
        Battle[who].Action = CombatAction.None;
        ForgetTarget(who);
        // C: the stairs record n of the level shown NOW (after switching back when the party stays): its +0x0D.
        Battle[who].StairsBack = (sbyte)Level.GetByte(StairsAt + 14 * n + 0x0D);
    }

    /// <summary>
    /// The party left the level through stairs record <paramref name="exit"/>: in real combat the
    /// enemy kinds that saw the party follow after a delay; otherwise the weakest are dropped and the
    /// rest wait at the stairs as an encounter.
    /// </summary>
    // orig: leave_level R023+0x17D2 (1EF8:17D2) draft
    // DIVERGENCE: the encounter record it adds keeps undefined memory, not modelled. Its kinds (+0x08..+0x0A) are the local words of slots 4..6
    //   (bp-0x24), written only for a kind with an able enemy (R023+0x1A04..0x1A1A): the first (no kind 4 exists) and a kind with none left hold
    //   the stack's leftovers, with a count of 0 (here -1 for the first, 0 for the others); the reinforcement part +0x14..+0x1D is not written
    //   (R023+0x1A27..0x1A9D write +0x00..+0x13 only), so the table slot keeps what it held (here the new group's defaults). These bytes go into
    //   a battle file saved after the change; the battle-call adapter of R023+0x17D2 takes the original's bytes there (local/batch/b2).
    private void LeaveLevel(int who, byte[] exit)
    {
        var seen = new bool[7];
        var cnt = 0;
        var any = false;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var p = Battle[i];
            if (p.IsAble)
            {
                cnt++;
                if (p.AnimationSlot is >= 0 and < 7 && (EnemyFollows(p.AnimationSlot) & (1 << Battle.PartyFloor)) != 0)
                {
                    seen[p.AnimationSlot] = true;
                    any = true;
                }
            }
        }
        if (cnt <= 0)
        {
            return;
        }
        var me = Battle[who].Position;
        if (any && Battle.RealCombat)
        {
            for (var k = 0; k < 3; k++)
            {
                if (!seen[k + 4])
                {
                    for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
                    {
                        if (Battle[i].AnimationSlot - k == 4)
                        {
                            Battle[i].Status = CharacterStatus.Empty;
                        }
                    }
                }
            }
            var best = 9999;
            for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
            {
                var p = Battle[i];
                if (p.IsAble)
                {
                    best = Math.Min(best, Distance((me.X - p.Position.X) / 2, me.Y - p.Position.Y));
                }
            }
            InitialEnemySpot = (new MapPoint((short)(exit[8] | exit[9] << 8), (short)(exit[10] | exit[11] << 8)), exit[12]);
            // C: DS:C3CD is the whole 14-byte stairs record (the battle file keeps it).
            exit.AsSpan(0, Math.Min(exit.Length, InitialSpotRecord.Length)).CopyTo(InitialSpotRecord);
            DelayedStartAt = (ushort)(Math.Clamp(best / 32, 1, 4) * 60 + Clock);
            WavesEnabled = false;
            DelayedStart = true;
            ActivationsEnabled = false;
            GatherLoot();
            Battle.RealCombat = false;
            return;
        }
        var counts = new int[7];
        var types = new int[7];
        types[4] = -1;
        int csum = 0, wsum = 0, nearest = -1, near = 9999;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var p = Battle[i];
            if (!p.IsAble)
            {
                continue;
            }
            csum += p.Endurance;
            wsum += p.MaximumEndurance;
            var d = Distance((me.X - p.Position.X) / 2, me.Y - p.Position.Y);
            if (d < near)
            {
                near = d;
                nearest = i;
            }
        }
        var k2 = wsum / cnt;
        // C: integer arithmetic; a kind whose endurances sum to 0 divides by 0 in the original (cannot happen with able enemies).
        var kill = k2 == 0 ? 0 : cnt - (k2 / 2 + csum) / k2;
        if (kill > 0)
        {
            cnt -= kill;
            do
            {
                int r = 999, weakest = 5;
                for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
                {
                    var p = Battle[i];
                    if (!p.IsAble)
                    {
                        continue;
                    }
                    // orig: R023+0x19BD..0x19C2 `mov al,0x64; imul byte [si]; cwd; idiv cx`: the whole 16-bit product 100 × endurance over the
                    // sign-extended maximum (no cut to a byte). A maximum of 0 is a divide error in the original (an able enemy has one).
                    var d = p.MaximumEndurance == 0 ? 0 : 100 * p.Endurance / p.MaximumEndurance;
                    if (d < r && !p.EndsBattleWhenKilled)
                    {
                        r = d;
                        weakest = i;
                    }
                }
                Battle[weakest].Status = CharacterStatus.Empty;
            }
            while (--kill > 0);
        }
        if (cnt <= 0)
        {
            return;
        }
        // C: R023+0x1A04..0x1A1A keeps the kinds as words at bp−0x24 + 2 × (slot − 4) and the counts at bp−0x1E + 2 × (slot − 4): an able enemy
        // in slot 1..3 would add 1 to the kind word of slot 4..6. Enemies only take slots 0 (its words are not read), 5 and 6: not modelled.
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var p = Battle[i];
            if (p.IsAble && p.AnimationSlot is >= 0 and < 7)
            {
                types[p.AnimationSlot] = EnemyKindOfSlot(p.AnimationSlot);
                counts[p.AnimationSlot]++;
            }
        }
        var group = new EnemyGroup
        {
            X = (short)Battle[nearest].Position.X, Y = (short)Battle[nearest].Position.Y, Spread = 0x10, HasReinforcements = 0, Room = -1,
            Florins = Purse.Florins, Groschen = Purse.Groschen, Pfennigs = Purse.Pfennigs,
        };
        for (var k = 0; k < 3; k++)
        {
            group.Kinds[k] = (sbyte)types[k + 4];
            group.Counts[k] = (sbyte)counts[k + 4];
        }
        Activations[Battle.FloorShown].Add(group);
        LevelChanged = true;
        WavesEnabled = false;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            Battle[i].Status = CharacterStatus.Empty;
        }
        FreeEnemySlots();
    }
}
