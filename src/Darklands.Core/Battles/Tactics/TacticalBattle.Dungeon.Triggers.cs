using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

// The dungeon of the battle: the triggers and their handlers (pits, alarms, hidden traps, secret doors, opening walls, spell and dart traps). The level files and the wall groups are in TacticalBattle.Dungeon.cs.
public sealed partial class TacticalBattle
{
    /// <summary>The triggers combatant i stands in call their handlers (enemies only spring the dart and spell traps).</summary>
    // orig: R014+0xD6DA (19CD:216A) matched
    // DIVERGENCE: a record whose handler word is not one of the eight types (a marker the generator left) would be called
    // through its raw words in the original; here nothing happens.
    private void Triggers(int i)
    {
        var c = Battle[i];
        // BUG: OriginalBehaviour.FixTriggersOfOtherLevels - the shown level's triggers are tested at the place of a combatant on any level:
        //   an alarm ends the battle, a dart or spell trap fires, a secret door or trap is found by remote (split audit row 4)
        if (Battle.Behaviour.FixTriggersOfOtherLevels && c.Level != Battle.LevelShown)
        {
            return;
        }
        for (var k = 0; k < LevelShown.GetByte(TriggerCountAt); k++)
        {
            var at = TriggersAt + 14 * k;
            var (x0, y0, w, h) = Box(at);
            var handler = (TriggerHandler)LevelShown.GetWord(at + 6);
            if (x0 > 0 && Inside(c.Position, x0, y0, w, h)
                && (i < Combatant.PartySlots || handler is TriggerHandler.MissileTrap or TriggerHandler.SpellTrap))
            {
                int a = LevelShown.GetWord(at + 10), b = LevelShown.GetWord(at + 12);
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
        LevelShown.SetWord(at, -LevelShown.GetWord(at));
    }

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
        LevelShown.SetWord(TriggersAt + 14 * k, -1);
        LevelChanged = true;
        var at = FurnitureRemovalAt + 10 * (n & 0xFF);
        // C: the direction byte is pushed zero-extended (checked against the asm).
        PlaceParty(LevelShown.GetWord(at + 6), LevelShown.GetWord(at + 8), LevelShown.GetByte(at + 5));
        _stopped = true;
        Battle.Result = BattleResult.FellIntoPit;
        Battle.ResultCard = card;
        _view.Message(PitText, 60);
    }

    /// <summary>DS:1BCA "Aaaahh!" (a literal of 19CD:2312).</summary>
    public const int PitText = 0x1BCA;

    /// <summary>DS:CA29: the battle is stopped (a pit or a timed exit): the ticks do nothing more.</summary>
    private bool _stopped;

    /// <summary>DS:CA29: the battle is stopped (a pit or an alarm): fight-tick returns before its draw from then on.</summary>
    public bool Stopped => _stopped;

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
        if (LevelShown.GetByte(at) == 1)
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
        // C: effect-begin(a, b, c): a furniture record holds them at +6, +8 and +5, a wall record at +4, +6 and +8; c zero-extended
        // (checked against the asm of both handlers).
        if (furniture)
        {
            PlaceParty(LevelShown.GetWord(at + 6), LevelShown.GetWord(at + 8), LevelShown.GetByte(at + 5));
        }
        else
        {
            PlaceParty(LevelShown.GetWord(at + 4), LevelShown.GetWord(at + 6), LevelShown.GetByte(at + 8));
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
            int v = LevelShown.GetByte(at + m), row = LevelShown.GetByte(at + 4 + m), f = LevelShown.GetByte(at + 8 + m);
            if (v == 0)
            {
                continue;
            }
            if (f < 0x80)
            {
                LevelShown.SetFloorByte(v, row, (byte)(LevelShown.FloorByte(v, row) | f));
            }
            else
            {
                // C: the shift is 12 (the furniture decoration nibble) or 20 (the north wall's) by bit 7 of the column.
                var shift = (v & 0x80) == 0 ? 0x0C : 0x14;
                var x = v & 0x3F;
                LevelShown[x, row] = new BattlefieldCell(LevelShown[x, row].Bits | (uint)((f & 0x7F) << shift));
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
        LevelChanged = true;
        if (n >= 0)
        {
            RevealGroup(n);
        }
        ToggleTrigger(h);
        _view.MapChanged();
    }

    /// <summary>A member searches: with perception above the roll the secret door is found (its wall cleared, its group uncovered).</summary>
    /// <param name="record">Low byte the door, high byte a second trigger to switch (0xFF none).</param>
    // orig: hidden-door-search R023+0x0812 (1EF8:0812) draft
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
        int b8 = LevelShown.GetByte(at + 8), b9 = LevelShown.GetByte(at + 9);
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
                LevelShown.SetCellByte(x, y, 2, LevelShown.CellByte(x, y, 2) & 0xF0);
                LevelShown.SetCellByte(x, y - 1, 3, LevelShown.CellByte(x, y - 1, 3) & 0xF0);
                break;
            case 1:
                LevelShown.SetCellByte(x, y, 0, LevelShown.CellByte(x, y, 0) & 0xF0);
                LevelShown.SetCellByte(x + 1, y, 1, LevelShown.CellByte(x + 1, y, 1) & 0xF0);
                break;
            case 2:
                LevelShown.SetCellByte(x, y, 3, LevelShown.CellByte(x, y, 3) & 0xF0);
                LevelShown.SetCellByte(x, y + 1, 2, LevelShown.CellByte(x, y + 1, 2) & 0xF0);
                break;
            case 3:
                LevelShown.SetCellByte(x, y, 1, LevelShown.CellByte(x, y, 1) & 0xF0);
                LevelShown.SetCellByte(x - 1, y, 0, LevelShown.CellByte(x - 1, y, 0) & 0xF0);
                break;
        }
        _view.Message(FindsSecretDoorText, 150, who);
        LevelChanged = true;
        var wall = (sbyte)LevelShown.GetByte(at + 6);
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
        LevelChanged = true;
        var at = FurnitureRemovalAt + 10 * n;
        int x = LevelShown.GetByte(at + 1), y = LevelShown.GetByte(at + 2), x2 = LevelShown.GetByte(at + 3), y2 = LevelShown.GetByte(at + 4);
        if (x != 0)
        {
            LevelShown.SetCellByte(x, y, 0, LevelShown.CellByte(x, y, 0) & 0x0F);
        }
        if (x2 != 0)
        {
            LevelShown.SetCellByte(x2, y2, 0, LevelShown.CellByte(x2, y2, 0) & 0x0F);
        }
    }

    /// <summary>Wall n goes (a trigger's alarm): its group is uncovered and the wall between the two cells removed.</summary>
    // orig: R023+0x0B6E (1EF8:0B6E) draft; R023+0x0D1A (1EF8:0D1A) matched
    // C: the wall byte is signed: a group of 0 is not uncovered (q->wall > 0).
    private void RemoveWall(int n, int h)
    {
        ToggleTrigger(h);
        LevelChanged = true;
        var at = WallRemovalAt + 10 * n;
        var wall = (sbyte)LevelShown.GetByte(at + 3);
        if (wall > 0)
        {
            RevealGroup(wall);
        }
        int bx = LevelShown.GetByte(at + 1), by = LevelShown.GetByte(at + 2);
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
                LevelShown.SetCellByte(x, y, 2, 0);
                break;
            case 1:
                LevelShown.SetCellByte(x, y, 0, LevelShown.CellByte(x, y, 0) & 0xF0);
                break;
            case 2:
                LevelShown.SetCellByte(x, y, 3, LevelShown.CellByte(x, y, 3) & 0xF0);
                break;
            case 3:
                LevelShown.SetCellByte(x, y, 1, 0);
                break;
        }
    }

    /// <summary>A spell trap goes off at the one who stepped on it ("Oops!"); it and its twin are switched off.</summary>
    // orig: trap-spell-effect R023+0x0D88 (1EF8:0D88) matched
    private void SpellTrap(int who, int h, int t)
    {
        _view.Message(OopsText, 60);
        var at = MissileTrapsAt + 10 * t;
        var c = Battle[who];
        Effects.Lands(c.Position.X, c.Position.Y, who, LevelShown.GetByte(at + 2), LevelShown.GetByte(at + 3), who, Battle.LevelShown);
        ToggleTrigger(h);
        var link = LevelShown.GetWord(at);
        if (link is >= 0 and < 50 && LevelShown.GetWord(TriggersAt + 14 * link) >= 0)
        {
            ToggleTrigger(link);
        }
    }

    /// <summary>
    /// A dart trap (and its twin t2) shoots its volley from a wall along a line: who stands in a
    /// dart's line dodges by agility or is hit by the dart weapon against armour.
    /// </summary>
    // orig: trap-shot R023+0x0E30 (1EF8:0E30) draft
    // C: the dart lines use the walking half sizes DS:119A / DS:11A6 of the party for the members (the combat boxes are put back after).
    private void MissileTrap(int who, int h, int t, int t2)
    {
        _ = who;
        _view.Message(OopsText, 60);
        ToggleTrigger(h);
        var cur = t;
        var link = LevelShown.GetWord(MissileTrapsAt + 10 * cur);
        if (link is >= 0 and < 50 && LevelShown.GetWord(TriggersAt + 14 * link) >= 0)
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
            int x = LevelShown.GetWord(at + 2), y = LevelShown.GetWord(at + 4), cnt = (sbyte)LevelShown.GetByte(at + 6), dir = LevelShown.GetByte(at + 7);
            int weapon = LevelShown.GetByte(at + 8), skill = LevelShown.GetByte(at + 9);
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
                            if ((LevelShown.CellByte(cx, cy - k, 2) & 0x0F) != 0)
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
                            if ((LevelShown.CellByte(cx + k, cy, 0) & 0x0F) != 0)
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
                            if ((LevelShown.CellByte(cx, cy + k, 3) & 0x0F) != 0)
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
                            if ((LevelShown.CellByte(cx - k, cy, 1) & 0x0F) != 0)
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
