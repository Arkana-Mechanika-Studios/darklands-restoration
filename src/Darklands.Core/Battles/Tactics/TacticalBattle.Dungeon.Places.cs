using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

// The dungeon of the battle: where each member stands (doors, stairs, chest regions, exit zones, the pile beside him) and the Dissolve Lock slot. The level files and the wall groups are in TacticalBattle.Dungeon.cs.
public sealed partial class TacticalBattle
{
    /// <summary>
    /// Each tick: for every able member which door, stairs, chest region and exit zone it stands
    /// in (the flags, the "place changed" bit, the leader's menu entries), then the triggers it
    /// stands in; the enemies spring the trap triggers; enemies open the unlocked doors they walk into.
    /// </summary>
    // orig: party-place-table R014+0xDC14 (19CD:26A4) draft
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
            // BUG: OriginalBehaviour.FixPlaceActionsOnOwnLevel - the place tests read the level SHOWN for every member: one on another level
            //   standing at a door, stairs or chest box of the shown level's map takes its index (split audit row 1)
            if (Battle.Behaviour.FixPlaceActionsOnOwnLevel && !Levels.IsShown(c))
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
        for (var k = 0; k < Levels.Shown.GetByte(DoorCountAt); k++)
        {
            // BUG: the enemies open the unlocked doors of the level SHOWN that they walk into, from any level (party-place-table
            //   R014+0xDC14, the door-assign at R014+0xDDE1). Kept as the original (party-split audit row 24). Decision: later, an
            //   optional fixed rule after 1.0.
            var at = DoorsAt + 14 * k;
            if (Levels.Shown.GetByte(DoorOpenAt + k) != 0 || Levels.Shown.GetByte(at + 7) != 0 || Levels.Shown.GetWord(at) <= 0)
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
            // C: the row redraw (0000:1716, R014+0xDD09) and the triggers' path both fall through to R014+0xDD17 `and byte [si],0xF7`.
            Battle[i].Flags &= ~TacticalFlags.PlaceChanged;
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
        for (var k = 0; k < Levels.Shown.GetByte(DoorCountAt); k++)
        {
            var at = DoorsAt + 14 * k;
            var (x0, y0, w, h) = Box(at);
            if (x0 <= 0)
            {
                continue;
            }
            if (Inside(c.Position, x0, y0, w, h))
            {
                int b8 = Levels.Shown.GetByte(at + 8), b9 = Levels.Shown.GetByte(at + 9);
                var cell = Levels.Shown[b8 & 0x3F, b9 & 0x3F].Bits;
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
        if (Battle.Leader != i || !Running)
        {
            return;
        }
        if ((c.Flags & TacticalFlags.InDoorway) != 0)
        {
            var locked = (sbyte)Levels.Shown.GetByte(DoorsAt + 14 * c.Door + 7);
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

    /// <summary>The weakest Eater-Water (item 99) in member i's pack (pick-finder-macro).</summary>
    // orig: the pick-finder-macro / pick-finder-macro-two macros of (checked against the asm of 19CD:1E92)
    private void FindEaterWater(int i) => Battle[i].DissolveSlot = WeakestEaterWater(Battle[i]);

    private const int EaterWater = 99;

    /// <summary>The stack of the weakest Eater-Water in the pack (the first of the lowest quality), −1 none.</summary>
    private static int WeakestEaterWater(Combatant c)
    {
        var slot = -1;
        var best = 100;
        for (var n = 0; n < c.Items.Count; n++)
        {
            if (c.Items[n].Code == EaterWater && (byte)c.Items[n].Quality < best)
            {
                slot = n;
                best = (byte)c.Items[n].Quality;
            }
        }
        return slot;
    }

    /// <summary>The Eater-Water of a Dissolve Lock order is used up: use-up-slot(m, DS:C7CD[m]).</summary>
    // orig: fighter-goto-action R014+0x2256..0x2263 (checked against the asm): use-up-slot 18BF:0FA4 (R014+0xB434) with the byte
    //   DS:C7CD[m] and no test of it; the slot is written only by FindEaterWater (command-menu-build R014+0xDF28 when order mode opens, and
    //   R014+0xD04E / 0xD402 for the leader at each running tick), and the character sheet works in order mode (R022+0x02F7 tests
    //   DS:A6B3 and DS:A6B2) without building the menu again, so an item given or dropped there moves the pack under the slot.
    private void UseUpDissolveSlot(int i)
    {
        var c = Battle[i];
        // C: the slot is a byte: no Eater-Water found (−1) is 255.
        var slot = c.DissolveSlot & 0xFF;
        // BUG: the order is carried out without a look at the pack: when the Eater-Water has been moved away since the order was given
        //   (given or dropped on the character sheet while the order waits), the lock opens all the same and the stack that now lies at
        //   the old slot pays for it (lockstep sessions dissolve-free-control / dissolve-free-moved: the stack that slid into the slot is
        //   used up, the chest opens). With OriginalBehaviour.FixStaleDissolveSlot nothing is used up and the lock opens for free; the
        //   switch of the rule is planned as FixDissolveWithoutEaterWater (fix.battle.dissolve-lock-without-eater-water, switches.csv).
        //   Decision: later, in the Fixed rule set the order is cancelled and the lock stays shut (1.1 step 7, after the battle seam); Classic keeps the original.
        if (Battle.Behaviour.FixStaleDissolveSlot)
        {
            // DIVERGENCE: with the fix (data safety, switchable; on in the player's game) only an Eater-Water is used up: the slot's stack
            //   when it still is one, else the weakest the pack holds (none: nothing; the lock opens as in the original).
            if (slot >= c.Items.Count || c.Items[slot].Code != EaterWater)
            {
                slot = WeakestEaterWater(c);
            }
            if (slot >= 0)
            {
                Attacks.ConsumeSlot(Battle, i, slot);
            }
            return;
        }
        // BUG: OriginalBehaviour.FixStaleDissolveSlot - the stack at the slot is used up whatever it is now; past the last stack it is the
        //   stale entry there (its count byte decremented, and when that was 0 or 1 the pack's count goes down: the last stack is lost, an
        //   empty pack's count becomes −1).
        ConsumeSlotAsOriginal(i, slot);
    }

    /// <summary>use-up-slot(m, k) of a party member m (−1..4) with a slot byte k (0..255), with no test of either, as the original.</summary>
    // orig: use-up-slot R014+0xB434 (checked against the asm), the party branch (m < 5, signed): entry m × 64 + k of the five packs
    //   (2E38:003E, 6 bytes, count at +4) decremented in place; old count − 1 > 0: done; else member m's count byte (DS:9CD3 + 0x80m,
    //   signed) shifts the entries above k down only when count − 1 > k, then goes down by one.
    private void ConsumeSlotAsOriginal(int m, int k)
    {
        if (m >= Combatant.PartySlots)
        {
            return;
        }
        if (m >= 0 && k < Pack.Size)
        {
            Attacks.ConsumeSlot(Battle, m, k);
            return;
        }
        // BUG: OriginalBehaviour.FixStaleDissolveSlot - slot 255 is entry m × 64 + 255 of the five packs (2E38:003E): for members 0 and 1
        //   entry 63 of member m + 3's pack, for member −1 (nobody selected) member 2's entry 63, whose count byte is decremented; when it
        //   was 0 or 1, use-up-slot's removal (no shift, k past the count) lowers member m's own count.
        var entry = m * Pack.Size + k;
        if (entry < 0 || entry >= Combatant.PartySlots * Pack.Size)
        {
            // DIVERGENCE: entry m × 64 + k outside the packs (member −1 with a slot below 64: before them, from 2E38:FEBE after the
            //   wrap; members 2..4 with slot 255: past them, in the formula and saint tables and the item-definition far pointers,
            //   2E38:07BE..), which the battle does not hold: nothing is changed (the original's write there is not modelled).
            return;
        }
        var other = Battle[entry / Pack.Size].Items;
        var stale = other[entry % Pack.Size];
        other[entry % Pack.Size] = stale with { Count = (byte)(stale.Count - 1) };
        if (stale.Count - 1 <= 0)
        {
            if (m < 0)
            {
                // DIVERGENCE: member −1's count byte DS:9C53 (before the party's records) is not held by the battle: not modelled.
                return;
            }
            Battle[m].Items.Stacks = (sbyte)(Battle[m].Items.Stacks - 1);
        }
    }

    /// <summary>The stairs combatant i stands on; the leader's Use Stairs entry (always on while the party is split).</summary>
    // orig: R014+0xD328 (19CD:1DB8) matched
    private void OnStairsAt(int i)
    {
        var c = Battle[i];
        for (var k = 0; k < Levels.Shown.GetByte(StairsCountAt); k++)
        {
            var (x, y, w, h) = Box(StairsAt + 14 * k);
            if (Inside(c.Position, x, y, w, h))
            {
                c.StairsToTake = k;
                c.Flags |= TacticalFlags.OnStairs;
                break;
            }
            c.Flags &= ~TacticalFlags.OnStairs;
        }
        if (Battle.Leader == i && Running)
        {
            if (Levels.PartyIndex != Levels.ShownIndex)
            {
                Menus.Set(OrdersItem.UseStairs, 1);
                c.StairsToTake = c.StairsBack;
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
        for (var k = 0; k < Levels.Shown.GetByte(RegionCountAt); k++)
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
        if (Battle.Leader != i || !Running)
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
            int locked = Levels.Shown.GetByte(at + 8), trap = Levels.Shown.GetByte(at + 9);
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
        for (var k = 0; k < Levels.Shown.GetByte(ZoneCountAt); k++)
        {
            var at = ZonesAt + 10 * k;
            int x0 = Levels.Shown.GetWord(at), y0 = Levels.Shown.GetWord(at + 2), w = Levels.Shown.GetWord(at + 4), h = Levels.Shown.GetWord(at + 6);
            if (x0 <= 0)
            {
                continue;
            }
            if (c.Level == Levels.ShownIndex && Inside(c.Position, x0, y0, w, h))
            {
                c.ExitZone = k;
                c.Flags |= TacticalFlags.InExitZone;
                break;
            }
            c.Flags &= ~TacticalFlags.InExitZone;
        }
        if (Battle.Leader == i && Running)
        {
            Menus.Set(OrdersItem.Exit, (byte)((c.Flags & TacticalFlags.InExitZone) != 0 ? 1 : 2));
        }
    }

    /// <summary>The loot pile next to member i (the selected member's, when order mode opens).</summary>
    // orig: R014+0xD7A2 (19CD:2232) draft
    private void PileNextTo(int i)
    {
        var c = Battle[i];
        c.LootPile = LootPiles.FindIndex(p => p.Level == Levels.ShownIndex && p.Count != 0
            && c.Position.X - 16 <= p.Position.X && p.Position.X < c.Position.X + 16
            && c.Position.Y - 16 <= p.Position.Y && p.Position.Y < c.Position.Y + 16);
        NextToPile = c.LootPile != -1;
        if (Battle.Leader == i && Running)
        {
            Menus.Set(OrdersItem.LootBodies, (byte)(NextToPile && !Battle.RealCombat ? 1 : 2));
        }
    }
}
