namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>The menus at the start of a battle: Attack off, Walk and Halt on, the place entries off, the "(Ldr)" / "(All)" suffixes.</summary>
    // orig: battle_menus_enable R000+0x0F04 (0E76:0F04) draft; battle_run sets
    // DS:9429 (Orders) 1, DS:9433 (Attack) 2 and the other enables 2 (R002+0x0000)
    private void InitMenus()
    {
        var m = Menus;
        m.OrdersMenu = 1;
        m.AttackMenu = 2;
        Array.Fill(m.Orders, (byte)2);
        Array.Fill(m.Attack, (byte)2);
        m.Set(OrdersItem.Walk, 1);
        m.Set(OrdersItem.Halt, 1);
        m.DoorSuffix = BattleMenus.Leader;
        m.StairsSuffix = MoveMode == 0 ? BattleMenus.Leader : BattleMenus.All;
        m.ChestSuffix = m.PickSuffix = m.DissolveSuffix = BattleMenus.Leader;
        m.DisarmText = 0x60C;
        m.ExitSuffix = MoveMode == 0 ? BattleMenus.Leader : BattleMenus.All;
    }

    /// <summary>The order menu back to its state outside order mode.</summary>
    // orig: order_menu_reset R014+0xDE12 (19CD:28A2) matched
    public void ResetOrderMenu()
    {
        var m = Menus;
        m.AttackMenu = 2;
        for (var k = 0; k < 6; k++)
        {
            m.Attack[k] = 2;
        }
        m.Set(OrdersItem.Resume, 2);
        m.Set(OrdersItem.Finished, 2);
        m.Set(OrdersItem.Walk, (byte)(Battle.PartyFloor != Battle.FloorShown ? 2 : 1));
        m.DoorSuffix = BattleMenus.Leader;
        m.Set(OrdersItem.Flee, 2);
        m.Set(OrdersItem.UseDoor, 2);
        m.Set(OrdersItem.UseStairs, 2);
        m.StairsSuffix = MoveMode == 0 ? BattleMenus.Leader : BattleMenus.All;
        m.ChestSuffix = m.PickSuffix = m.DissolveSuffix = BattleMenus.Leader;
        m.DisarmText = 0x60C;
        m.Set(OrdersItem.OpenChest, 2);
        m.Set(OrdersItem.PickLock, 2);
        m.Set(OrdersItem.DissolveLock, 2);
        m.Set(OrdersItem.DisarmTrap, 2);
        m.Set(OrdersItem.LootBodies, 2);
        m.Set(OrdersItem.Exit, 2);
        m.ExitSuffix = MoveMode == 0 ? BattleMenus.Leader : BattleMenus.All;
        m.Set(OrdersItem.Cancel, 2);
    }

    /// <summary>
    /// The order menu of the selected member: Resume, "finished", Flee, Halt and Cancel on; in real
    /// combat the Attack menu (and its Throw list; a held member cannot attack, a member without a
    /// missile weapon or ammunition cannot shoot); the place entries of where it stands.
    /// </summary>
    // orig: order_menu_build R014+0xDF28 (19CD:29B8) draft
    private void BuildOrderMenu(int a)
    {
        ResetOrderMenu();
        var m = Menus;
        m.DoorSuffix = m.StairsSuffix = m.ChestSuffix = m.PickSuffix = m.DissolveSuffix = m.ExitSuffix = "";
        m.DisarmText = 0x5C6;
        m.Set(OrdersItem.Flee, 1);
        m.Set(OrdersItem.Halt, 1);
        m.Set(OrdersItem.Resume, 1);
        m.Set(OrdersItem.Cancel, 1);
        var c = Battle[a];
        if (Battle.PartyFloor != Battle.FloorShown)
        {
            m.Set(OrdersItem.UseStairs, 1);
            m.Set(OrdersItem.Walk, 2);
            m.Set(OrdersItem.Flee, 2);
            m.Set(OrdersItem.Halt, 2);
            c.StairsExit = c.StairsBack;
            return;
        }
        m.Set(OrdersItem.Walk, 1);
        m.Giver = c.Member?.ShortName ?? $"#{a}";
        m.Set(OrdersItem.Finished, 1);
        m.Set(OrdersItem.LootBodies, (byte)(NextToPile && !Battle.RealCombat ? 1 : 2));
        if (Battle.RealCombat)
        {
            m.AttackMenu = 1;
            for (var k = 0; k < 6; k++)
            {
                m.Attack[k] = 1;
            }
            m.ThrowLines.Clear();
            m.ThrowLines.AddRange(Effects.ThrowList(a));
            // DS:97DD: 1 with lines, 2 without (R019 0C80 writes it).
            m.Attack[(int)AttackItem.Throw] = (byte)(m.ThrowLines.Count != 0 ? 1 : 2);
            if (c.Held)
            {
                m.Set(OrdersItem.Walk, 2);
                for (var k = 0; k < 6; k++)
                {
                    m.Attack[k] = 2;
                }
                return;
            }
            if (c.ReadiedWeapon == -1 || !Attacks.CanFire(Battle, a))
            {
                m.Attack[(int)AttackItem.Missile] = 2;
            }
        }
        var fl = c.Flags;
        if ((fl & TacticalFlags.InDoorway) != 0)
        {
            int locked = Level.GetByte(DoorsAt + 14 * c.Door + 7);
            if (locked == 0 || Battle.RealCombat)
            {
                m.Set(OrdersItem.UseDoor, 1);
            }
            else
            {
                LockIsDoor = true;
                m.Set(OrdersItem.PickLock, 1);
                if (locked > 0x7F)
                {
                    FindEaterWater(a);
                    if (c.DissolveSlot != -1)
                    {
                        m.Set(OrdersItem.DissolveLock, 1);
                    }
                }
            }
        }
        if ((fl & TacticalFlags.OnStairs) != 0)
        {
            m.Set(OrdersItem.UseStairs, 1);
        }
        if ((fl & TacticalFlags.InExitZone) != 0)
        {
            m.Set(OrdersItem.Exit, 1);
        }
        if ((fl & TacticalFlags.AtChest) != 0 && !Battle.RealCombat)
        {
            var at = RegionsAt + RegionSize * c.Chest;
            int locked = Level.GetByte(at + 8), trap = Level.GetByte(at + 9);
            if ((locked & 0x7F) == 0)
            {
                if (trap == 0)
                {
                    m.Set(OrdersItem.OpenChest, 1);
                }
                else
                {
                    m.Set(OrdersItem.DisarmTrap, 1);
                    switch (trap)
                    {
                        case 0x50:
                            m.DisarmText = 0x5DC;
                            break;
                        case 0x14:
                            m.DisarmText = 0x5B2;
                            break;
                        case 0x32:
                            m.DisarmText = 0x5C6;
                            break;
                    }
                }
            }
            else
            {
                m.Set(OrdersItem.PickLock, 1);
                LockIsDoor = false;
                if (locked > 0x7F)
                {
                    FindEaterWater(a);
                    if (c.DissolveSlot != -1)
                    {
                        m.Set(OrdersItem.DissolveLock, 1);
                    }
                }
            }
        }
    }

    /// <summary>The per-tick enables of Travel As Group / Single File (only while the party may move together).</summary>
    // orig: combat_tick R014+0x0F58 (0E76:0F58) draft (DS:9681 / DS:9695)
    private void GroupEntries()
    {
        if (GroupMoveAllowed)
        {
            Menus.Set(OrdersItem.Group, 1);
            Menus.Set(OrdersItem.SingleFile, 1);
        }
        else if (MoveMode == 1)
        {
            Menus.Set(OrdersItem.Group, 1);
            Menus.Set(OrdersItem.SingleFile, 2);
        }
        else if (MoveMode == 2)
        {
            Menus.Set(OrdersItem.Group, 2);
            Menus.Set(OrdersItem.SingleFile, 1);
        }
        else
        {
            Menus.Set(OrdersItem.Group, 2);
            Menus.Set(OrdersItem.SingleFile, 2);
        }
    }
}
