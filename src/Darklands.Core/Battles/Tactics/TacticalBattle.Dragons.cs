using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The dragon fights (DS:C21A set by loading a file of the seven-headed dragon, M73, or of the cave dragon, M90): combatant 5,
/// the first enemy placed, is the dragon. It picks its victims itself, breathes fire every few blows (spell type 8 at power 50,
/// with its breathing animation), and the usual enemy turn leaves the field to it.
/// </summary>
public sealed partial class TacticalBattle
{
    /// <summary>The breathing animation's weapon code by dragon (DS:17D6 / DS:17D9).</summary>
    private static string BreathCode(byte fight) => fight == Battle.HydraFight ? "P7" : "PC";

    /// <summary>The fighting animation's weapon code by dragon (DS:17DC / DS:17DF, the same as DS:216E / DS:2171).</summary>
    private static string ClawCode(byte fight) => fight == Battle.HydraFight ? "D7" : "CD";

    /// <summary>The dragon fight's part of the battle's reset, which also runs for a battle taken up from a saved game.</summary>
    // orig: R002+0x1830 (0E76:1830) draft, after the part DS:9081 bit 0 skips (checked against the asm): DS:C26E = 1 (0x18FA),
    // DS:C21F = 3 (0x18FF), DS:C21A = 0 (0x1936)
    private void ResetDragonFight()
    {
        Battle.DemoAttackCount = 1;
        Battle.DemoAttackInterval = 3;
        Battle.DemoMode = 0;
    }

    /// <summary>
    /// enemies_place for the first enemy (record 5) of a dragon fight: weapon type 40 takes the dragon's animation code for the rest
    /// of the battle.
    /// </summary>
    // orig: enemies_place R025+0x0000 (1EF8:0000) draft, 0x0779..0x07AA (checked against the asm): `if (DS:C21A != 0 && si == 5)`
    // 20A5:763B..763C = "D7" when DS:C21A == 'I', else "CD"; after the slot's files were loaded (0x0733) and the weapon readied (0x042B)
    private void DragonWeaponForFight(int si)
    {
        if (Battle.DemoMode != 0 && si == 5)
        {
            Battle.DragonWeaponCode = ClawCode(Battle.DemoMode);
        }
    }

    /// <summary>
    /// melee_exchanges, for the dragon about to strike while the battle runs: when it is breathing (action 0x100) any party slot will
    /// do, else a member fighting hand to hand (11 draws at most; one found makes it breathe only every 6th blow from now on);
    /// either way until the one drawn is able or none was found. Then it turns to one of facings 1..4.
    /// </summary>
    // orig: melee_exchanges R014+0x2A4A (0E76:2A4A) draft, 0x2A98..0x2B36 (checked against the asm)
    // C: with every party slot unable a breathing dragon draws for ever (the battle would have ended before)
    private void DragonPicksVictim(Combatant c)
    {
        do
        {
            if (c.Action == CombatAction.UseItem)
            {
                c.Target = (sbyte)Scaled(5);
            }
            else
            {
                c.Target = -1;
                var tries = 0;
                do
                {
                    var k = Scaled(5);
                    if (Battle[k].Status == CharacterStatus.Able && (Battle[k].Action & CombatAction.Melee) != 0)
                    {
                        c.Target = (sbyte)k;
                        Battle.DemoAttackInterval = 6;
                        break;
                    }
                }
                while (tries++ < 10);
            }
        }
        while (c.Target != -1 && Battle[c.Target].Status != CharacterStatus.Able);
        c.SpriteFacing = Scaled(4) + 1;
    }

    /// <summary>
    /// melee_frame, the dragon's blow landing while the battle runs: every DS:C21F-th blow (counted by cbt_attack) it breathes next:
    /// the breathing animation, the use-item action and spell type 8 at power 50.
    /// </summary>
    // orig: melee_frame R014+0x199C (0E76:199C) draft, 0x19F8..0x1A47 (checked against the asm): DS:C26E (unsigned) idiv DS:C21F
    // (signed), the code copied to anim_file[5] (*DS:117E), DS:9EE5 = 0x100, DS:CA11 = 8, DS:C97F = 50
    private void DragonMayBreathe(Combatant c)
    {
        if (Battle.DemoAttackCount % Battle.DemoAttackInterval != 0 || Battle.Result != BattleResult.Running)
        {
            return;
        }
        SlotWeapons[5] = BreathCode(Battle.DemoMode);
        c.Action = CombatAction.UseItem;
        c.Spell = 8;
        c.SpellPower = 50;
    }

    /// <summary>
    /// enemy_turn in a dragon fight: a dragon doing nothing is set to attack at once (and the fight made real, every combatant
    /// joining it); either way the usual enemy turn does not run.
    /// </summary>
    // orig: enemy_turn R018+0x0706 (1F43:0256) draft, 0x0729..0x079D (checked against the asm): `if ((DS:9EE5 & 0x1BE) == 0)`
    // DS:9EE5 = 0x12, DS:D9A0 (engage time of 5) = clock, DS:CA69 = 7, anim_file[5] = DS:216E / DS:2171, DS:A6C1 = DS:9721 = 1,
    // DS:DA49 = clock + 300, R018+0x1472(i) for i < 13; then (and otherwise) the turn ends
    private void DragonTurn()
    {
        var c = Battle[5];
        if ((c.Action & AttackOrders) != 0)
        {
            return;
        }
        c.Action = CombatAction.StandardAttack;
        c.EngageAt = Clock;
        c.State = ActionState.Ready;
        SlotWeapons[5] = ClawCode(Battle.DemoMode);
        Battle.RealCombat = true;
        Menus.Set(OrdersItem.Surrender, 1);
        CombatBoxRetryAt = (ushort)(Clock + 300);
        for (var i = 0; i < Combatant.Count; i++)
        {
            JoinFight(i);
        }
    }
}
