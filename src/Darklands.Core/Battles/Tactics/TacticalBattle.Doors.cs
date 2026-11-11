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
    // orig: door-flip R023+0x00B8 (1EF8:00B8) draft, checked against the asm
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
    // orig: door-assign R023+0x04AC (1EF8:04AC) draft
    // DORMANT: a door record with none of the four side flags (+8 and +9, bits 0x80 / 0x40) leaves the direction local bp-6 uninitialised in the original
    //   (R023+0x04C8 .. +0x050B), so the door bits would go to the side the stale stack word names, or to no side when it is not 0..3 (the two switches
    //   R023+0x0620 / +0x06B8 have no default); north (0) is used here. Proven unreachable: the three callers (R014+0x4924, +0x4B95, +0xDDE1, the only
    //   calls of the vector 09C0:1D6F) take only records whose word +0 is above 0, and every writer of a door record gives such a record a flag. The
    //   writers: the level files (all 143 levels of IMAPS.CAT and the 9 of LCASTLE scanned: 479 records, the only 11 flagless ones, IFORTMON.009
    //   doors 0 and 1 and door 0 of each LCASTLE level, are all-zero, word +0 = 0); the castle generator (R007+0x1234 writes x | 0x80 or y | 0x40 with
    //   cells below 0x40); the map turn (R004+0x29B0, MapTurn.TurnDoor: it turns only icity and mine sections, whose records all carry flags, and moves
    //   the four flags among themselves; the fortress and citadel files are never turned, so their zero records keep word +0 = 0); the city's unlock and
    //   the fortress's room set-up write the lock byte +7 only; no instruction of the EXE stores to +8 / +9 through a door-table displacement (0x1F5C,
    //   0x1F5D: reads only). A sweep of every battlefield type 0..0x89 with 40 seeds each (5,520 builds, 60,832 door records) found 0 flagless records
    //   with word +0 above 0.
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
    // orig: route-search R014+0x481A (12F7:000A) draft (the DS:E0C0 block) and R014+0x4B5A (12F7:034A)
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
    // orig: lock-picking R023+0x12C0 (1EF8:12C0) draft, checked against the asm
    // C: without lock picks the quality is read from the slot after the last item (or item 0 of an empty pack), but it is only used
    // when lock picks were found, so nothing depends on it.
    private bool PickLock(int lockAt)
    {
        if (_picker is < 0 or >= Combatant.Count)
        {
            // DIVERGENCE: DS:E1CA is set by every place choice (R016+0x1590), 0xFF with nobody selected (H outside order mode, R016+0x101E):
            //   a member's Pick Lock given before and carried out after it reads combatant record -1, its inventory and DS:D68D[-1]
            //   (the bytes before the tables, not modelled) at R023+0x12C0 and ORs the skill-use bits into the word before DS:D68D. Here the
            //   lock is not picked and the no-lock-picks message shows, as when that inventory holds no item 0x40 (split audit row 19).
            _view.Message(NoLockPicksText, 150);
            return false;
        }
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
    // orig: chest-opening R023+0x13D0 (1EF8:13D0) draft, checked against the asm
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
        // C: R023+0x1525..0x152A: floor-view-paint(DS:17B0): the view drawn anew around the active member
        _view.Recentre(Battle.ActiveMember);
        // C: then fight-draw R014+0x6818 (R023+0x1532), as each tick ends: its R014+0x99F6 (called at R014+0x6926) turns the attackers
        //   toward their targets, then the draw list (its random draws) and the target cursor.
        TurnTowardTargets();
        Effects.Drawn(_view);
        RemoveArrivedMissiles();
        RefreshTargetCursor();
    }

    /// <summary>Member who disarms the trap of his chest: Artifice − 10 + 1d20 against its difficulty; failing, it goes off at him.</summary>
    // orig: chest-disarm-trap R023+0x153E (1EF8:153E) matched (checked against the asm:
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
    // orig: fighter-goto-action R014+0x2170 (0E76:2170) matched
    // C: every case is a separate if (the action is cancelled at the end of each), so only the first that applies acts.
    private void DoPlaceAction(int i)
    {
        var c = Battle[i];
        // BUG: OriginalBehaviour.FixPlaceActionsOnOwnLevel - the door or chest index of a member on another level is used on the level SHOWN
        //   (door-flip, lock-picking, chest-opening, R023+0x153E read the image of DS:11CA): another level's door or chest is toggled,
        //   picked, opened or disarmed, and his own is untouched (split audit row 1)
        int? shownBefore = null;
        if (Battle.Behaviour.FixPlaceActionsOnOwnLevel && c.Floor != Battle.FloorShown && c.Floor >= 0 && c.Floor < _levels.Count
            && c.Place is PlaceAction.UseDoor or PlaceAction.PickDoorLock or PlaceAction.PickChestLock or PlaceAction.OpenChest
                or PlaceAction.DissolveLock or PlaceAction.DisarmChestTrap)
        {
            shownBefore = Battle.FloorShown;
            SwitchLevel(c.Floor);
        }
        DoPlaceActionHere(i);
        if (shownBefore is { } before && Battle.FloorShown != before)
        {
            SwitchLevel(before);
        }
    }

    private void DoPlaceActionHere(int i)
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
            TakeStairsOrder(i);
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
    // orig: R016+0x1182 (1EF8:1182) matched; the U case of fight-keys for a split party (R016+0x014E)
    public void UseKey(int selected)
    {
        if (Battle.PartyFloor != Battle.FloorShown)
        {
            // fight-keys with the party split: only U (and a few keys) work.
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
            int? shownBefore = null;
            if (leader.StairsBack != -1 && !StaleStairsRecord(Battle.Leader, leader.StairsBack))
            {
                if (Battle.Behaviour.FixLeaderStairsRecordOfShownLevel && leader.Floor != Battle.FloorShown && leader.Floor >= 0
                    && leader.Floor < _levels.Count)
                {
                    // the fix: the leader's own level shown first, so the record is his own level's
                    shownBefore = Battle.FloorShown;
                    SwitchLevel(leader.Floor);
                }
                // BUG: OriginalBehaviour.FixLeaderStairsRecordOfShownLevel - with the party split and the battle running, U (R016+0x014E,
                //   not paused) sends the LEADER through his stairs-back record number (DS:C8B2[leader]) read on the level SHOWN, not
                //   his own: with the view on the lone member's floor (he was selected, then deselected) and the leader with the party
                //   on another, the leader leaves through a record of a floor he is not on (GitHub issue 3: from Rotenburg level 3 to
                //   level 1; past the count, whatever the generator left there) and the lone member stays. Only paused, with the member
                //   selected, does U or Orders > Use Stairs bring the member back (the menu's Use Stairs is greyed while the battle runs).
                TakeExit(leader.StairsBack, Battle.Leader);
                ShowAgainAfterFix(shownBefore, Battle.Leader);
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
        if (LeaderAway())
        {
            return;
        }
        if (Menus.On(OrdersItem.UseStairs))
        {
            if (MoveMode == 0)
            {
                TakeExit(Battle[l].StairsExit, l);
            }
            else
            {
                var from = Battle[l].Floor;
                for (var i = 0; i < Combatant.PartySlots; i++)
                {
                    // BUG: OriginalBehaviour.FixLeaderKeysOnOtherLevel - travelling together U sends every able member, on whatever level,
                    //   through the leader's stairs record (split audit row 3)
                    if (Battle[i].IsAble && !(Battle.Behaviour.FixLeaderKeysOnOtherLevel && Battle[i].Floor != from))
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
        if (LeaderAway())
        {
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
        if (!Menus.On(OrdersItem.PickLock) || LeaderAway())
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
        if (LeaderAway())
        {
            return;
        }
        var l = Battle.Leader;
        if (Menus.On(OrdersItem.DissolveLock))
        {
            _view.Message(EaterWaterText, 150);
            // orig: R016+0x0F9A (1EF8:0F9A) matched: use-up-slot 18BF:0FA4 (R014+0xB434) of DS:A6B2 with the byte DS:C7CD (R016+0x0FAF..0x0FBA)
            if (Battle.Behaviour.FixDissolveKeyMember)
            {
                // DIVERGENCE: with the fix (data safety, switchable; on in the player's game) the leader's Eater-Water is used up, as by
                //   his Dissolve Lock order (his own slot byte; the stale-slot fix applies as there).
                UseUpDissolveSlot(l);
            }
            else
            {
                // BUG: OriginalBehaviour.FixDissolveKeyMember - R016+0x0FAF..0x0FBA: use-up-slot(DS:A6B2, DS:C7CD): the selected member
                //   (outside order mode the party member under the pointer, R014+0xB785, else −1, R014+0xB7F5) with member 0's slot byte
                //   (absolute, not indexed), no test of either: another member's stack, or the entries before the packs.
                ConsumeSlotAsOriginal((sbyte)selected, Battle[0].DissolveSlot & 0xFF);
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

    /// <summary>The Stairs order of combatant i carried out: use-exit with his stairs record number (DS:C77E[i]).</summary>
    // orig: fighter-goto-action R014+0x22B2..0x22CB and fight-actions R014+0x11CA..0x11E7: use-exit(DS:C77E[i], i)
    private void TakeStairsOrder(int i)
    {
        var c = Battle[i];
        if (StaleStairsRecord(i, c.StairsExit))
        {
            return;
        }
        int? shownBefore = null;
        if (Battle.Behaviour.FixStairsRecordOfShownLevel && c.Floor != Battle.FloorShown && c.Floor >= 0 && c.Floor < _levels.Count)
        {
            // the fix: his own level shown first, so the record is his own level's
            shownBefore = Battle.FloorShown;
            SwitchLevel(c.Floor);
        }
        // BUG: OriginalBehaviour.FixStairsRecordOfShownLevel - use-exit reads record n of the level SHOWN (R023+0x162B, no count test):
        //   a member alone on another level whose order is given with the view back on the party's level (another member selected
        //   after Use Stairs, then Space: GitHub issue 3) takes that level's record n, past its count as a rule, whatever the generator
        //   left there (zeros: level 0 at (0, 0); another level's record; stale bytes): the wrong place or level, or back by chance (the
        //   Rotenburg keep: slot 2 of level 3 holds level 2's record to level 3).
        TakeExit(c.StairsExit, i);
        ShowAgainAfterFix(shownBefore, i);
    }

    /// <summary>The leader is not on the level shown (FixLeaderKeysOnOtherLevel on): his keys outside order mode do nothing.</summary>
    private bool LeaderAway()
    {
        // BUG: OriginalBehaviour.FixLeaderKeysOnOtherLevel - the leader's U, O, P and D use his stairs record, door or chest index from the
        //   place tests of the level SHOWN: alone on another level he acts on the party's level by remote (split audit row 3)
        return Battle.Behaviour.FixLeaderKeysOnOtherLevel && Battle[Battle.Leader].Floor != Battle.FloorShown;
    }

    /// <summary>FixStairsArrivalWithoutPlace: around the shown level's stairs back to <paramref name="from"/>, then any stairs, then around
    /// a member standing on this level.</summary>
    private void PlaceAfterFailedArrival(int who, int from)
    {
        var count = Level.GetByte(StairsCountAt);
        foreach (var back in new[] { true, false })
        {
            for (var k = 0; k < count; k++)
            {
                var at = StairsAt + 14 * k;
                if (back != ((sbyte)Level.GetByte(at + 6) == from))
                {
                    continue;
                }
                var box = new MapPoint(Level.GetWord(at) + Level.GetByte(at + 4) / 2, Level.GetWord(at + 2) + Level.GetByte(at + 5) / 2);
                if (PlaceAround(who, box, Level.GetByte(at + 12) & 7))
                {
                    return;
                }
            }
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (i != who && Battle[i].IsAble && Battle[i].Floor == Battle[who].Floor && PlaceAround(who, Battle[i].Position, Battle[i].Facing & 6))
            {
                return;
            }
        }
    }

    /// <summary>Whether record number n is past the stairs count of combatant who's own level, with FixStaleStairsBackRecord on.</summary>
    private bool StaleStairsRecord(int who, int n)
    {
        var floor = Battle[who].Floor;
        // BUG: OriginalBehaviour.FixStaleStairsBackRecord - the stairs-back record number (DS:C8B2) is written after use-exit's level switch:
        //   the new level's record when the party came together, 0 for a member who never went alone; Use Stairs then takes record n of his
        //   level past its count (a zero record: level 0 at (0, 0), outside the map; split audit row 14)
        return Battle.Behaviour.FixStaleStairsBackRecord && floor >= 0 && floor < _levels.Count
            && (n < 0 || n >= _levels[floor].GetByte(StairsCountAt));
    }

    /// <summary>After a stairs fix showed the actor's own level first (FixStairsRecordOfShownLevel, FixLeaderStairsRecordOfShownLevel):
    /// unless the party came together on the level he went to (the view is the party's level then), the level shown before comes back,
    /// rather than the actor's old level, which use-exit leaves shown and which may now be empty.</summary>
    private void ShowAgainAfterFix(int? shownBefore, int who)
    {
        if (shownBefore is { } before && Battle.PartyFloor != Battle[who].Floor && Battle.FloorShown != before)
        {
            SwitchLevel(before);
        }
    }

    /// <summary>
    /// Combatant <paramref name="who"/> takes stairs record n to another level: when no able member
    /// is left behind the party moves (the enemies follow or stay as an encounter); it is placed
    /// round the stairs' spot there.
    /// </summary>
    // orig: use-exit R023+0x1618 (1EF8:1618) draft
    // C: the wall group of the stairs is uncovered on the NEW level (opened-door-list of that level; the list read from the new image).
    // C: no test of n against the level's stairs count (byte +0x2FB6): R023+0x162B..0x164A copies record n of the level shown, past the
    //   count too. A member alone on another level whose Stairs order is given with the view back on the party's level (another member
    //   selected after U, GitHub issue 3) takes the record of his own level's index from the party's level: in the Rotenburg keep slot 2
    //   past level 3's one record holds the same bytes as level 2's record 2 (to level 3), so he comes back (lockstep
    //   castle-rotenburg-split-reselect: use-exit(2, 3) at clock 9283 with DS:11CA = DS:C623 = 3).
    // DIVERGENCE: a record past the level image (n > 77, 0x2FB8 + 14 n beyond its 0x33FC bytes) would read what follows the image in
    //   segment 20A5; not reproduced (no caller gives such an n: the tables hold record numbers of up to the six slots, and 0xFF is tested
    //   before, R016+0x014E).
    private void TakeExit(int n, int who)
    {
        if (n < 0 || StairsAt + 14 * (n + 1) > Level.Image.Length)
        {
            return;
        }
        var old = Battle.FloorShown;
        var cameFrom = Battle[who].Floor;
        var at = StairsAt + 14 * n;
        var record = Level.Image.AsSpan(at, 14).ToArray();
        // DIVERGENCE: a record whose level (+6) the battle does not have (a slot past the count with other bytes, never seen in the
        //   generated levels: their slots hold zeros or castle and mine records) would make level-change (R024+0x059A) read a level
        //   file the battle never wrote; here the order is ignored (nothing written, nobody moved) rather than the program stopped.
        if ((sbyte)record[6] < 0 || (sbyte)record[6] >= _levels.Count)
        {
            return;
        }
        var wall = (sbyte)Level.GetByte(at + 7);
        // BUG: OriginalBehaviour.FixStairsWallMarkLost - the write below goes to the level's buffer, and level-change (R024+0x05B4, called
        // at R023+0x16C7) saves the level it leaves only when DS:D4E4 is set (LevelChanged: any change of the level sets it; level-change
        // and the battle's end R014+0x069A clear it), which this write does not do: after a switch with DS:D4E4 clear the original
        // reloads the old level without the 0xFF (the levels stay in memory here, the mark is taken back by SaveLevelOrLoseUnsaved).
        if (wall != -1 && !Battle.Behaviour.FixStairsWallMarkLost)
        {
            _unsavedLevelBytes.Add((at + 7, (byte)wall));
        }
        else if (wall != -1)
        {
            // the fix: the level counts as changed, so the mark is saved with it
            LevelChanged = true;
        }
        Level.SetByte(at + 7, 0xFF);
        int lvl = (sbyte)Level.GetByte(at + 6);
        Battle[who].Floor = lvl;
        var alone = true;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            // BUG: OriginalBehaviour.FixExitedMemberStrandsTheRest - a member gone through an exit (status 1, floor -99) counts as one on another
            //   level: once anyone has left, the party never comes together by the stairs (split audit row 10)
            if (Battle[i].IsAble && Battle[i].Floor != lvl
                && !(Battle.Behaviour.FixExitedMemberStrandsTheRest && Battle[i].Floor == BattleEnd.LeftThroughExit))
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
        // BUG: OriginalBehaviour.FixStairsArrivalWithoutPlace - when no place around the arrival spot is free, place-nearby fails and he keeps
        //   the coordinates of his old level (some generated mines: an arrival spot away from its stairs, amid cells that are not ground; he
        //   stands in the void; lockstep of direct use-exit calls, split audit fuzz)
        if (!PlaceAround(who, spot, record[12]) && Battle.Behaviour.FixStairsArrivalWithoutPlace)
        {
            PlaceAfterFailedArrival(who, cameFrom);
        }
        if (Battle.PartyFloor == lvl)
        {
            // orig: R023+0x1618 floor-view-paint(who) when the party has gone over: the view's centre becomes the cell of the member who
            // took the stairs (R017+0x0000 with who, not -1); lockstep dungeon-king-door (local/batch/b32/a7-king)
            _view.Recentre(who);
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
    // orig: level-exit R023+0x17D2 (1EF8:17D2) draft
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
            // orig: R023+0x1995 `mov word [bp-0xe],5` sets the victim index once before the loop; R023+0x19A6 resets only the minimum (bp-4).
            // A pass with no eligible enemy (all ends-battle) therefore kills the previous victim again.
            var weakest = 5;
            do
            {
                var r = 999;
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
