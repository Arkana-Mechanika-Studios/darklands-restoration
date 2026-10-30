namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC01": past the first gate lies a stinking cavern of blood, with hail and fire falling
/// from the roof. The party may run across (the slowest member's agility decides how badly it
/// goes), creep across under their armour, pray, share a Firewall potion or turn back; every
/// crossing ends in the fight beyond (enemy 0x58).
/// </summary>
// orig: R130+0x0D48 (190C:0008) matched; tests: CardSmokeTests
//   option 0: action R130+0x0EDA; hint R130+0x106A
//   option 1: action R130+0x107E; hint R130+0x1128
//   option 2: action R130+0x1160; hint R130+0x129A
//   option 3: action R130+0x12D2; hint R130+0x1372
//   option 4: action R130+0x13AA; hint R130+0x13B8
//   item pop-up: R130+0x149A
//   saint pop-up: R130+0x14D6, R130+0x1560, R130+0x15EA
//   other: R130+0x13F0, R130+0x1462
// DORMANT (generated): option slots 10..19 keep the default handler R130+0x1462 ("screwed up.") and are never shown.
// C (generated): card-option-flag-5/6/7/8/9 are never assigned or read here: the card's init zeroes card-option-enabled[0..19], so those lines stay hidden, as in the C#.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class CavernOfFireAndHail : Card
{
    public override int Number => 345;

    private const int FirewallPotion = 0x74;
    private const int CavernEnemy = 0x58;
    private const int CavernPicture = 308;

    private static readonly int Endurance = (int)Core.Characters.Attribute.Endurance;
    private static readonly int Strength = (int)Core.Characters.Attribute.Strength;
    private static readonly int Agility = (int)Core.Characters.Attribute.Agility;

    public override int Run(CardContext c)
    {
        Show(c, CavernPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        c.SetCardItems(FirewallPotion);
        c.SetCardSaints(0x0F, 0x2B, 0x6F);
        menu[3] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();

        // C: every handler of this card redraws with card-run (the whole card, Show), not card-run-near.
        return menu.Run(
            // orig: R130+0x0EDA / R130+0x106A matched
            // C: the hint's party-effective-stat-two-low also sets top-scoring-member (unused by the hint, r.b = −2).
            new MenuLine(chance => RunAcross(c, chance), () => OptionHint.ChanceOf(c.PartyMinEffectiveAgility(), OptionHint.WholeParty)),
            // orig: R130+0x107E / R130+0x1128 matched
            new MenuLine(_ => CreepAcrossUnderArmour(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R130+0x1160 / R130+0x129A matched; pop-up R130+0x14D6 / _0820 / _08aa matched
            new MenuLine(_ => PrayForShelter(c, menu), OptionHint.Saying(CardTexts.SelectSaint), () => LastCastle.SaintHintForChosen(c)),
            // orig: R130+0x12D2 / R130+0x1372 matched; pop-up R130+0x149A matched
            new MenuLine(_ => ShareTheFirewallPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.SuccessAssured)),
            // orig: R130+0x13AA / R130+0x13B8 matched
            new MenuLine(_ => LastCastle.TurnBack(c), OptionHint.Saying(CardTexts.SuccessAssured)));
    }

    /// <summary>
    /// One roll: within the chance everyone gets across (harm ×1); else, within twice the best
    /// agility, the slowest member falls (×2); otherwise everyone slides and falls (×3).
    /// </summary>
    // orig: R130+0x0EDA matched
    // C: the roll is drawn before party-effective-stat-two-low, whose top-scoring-member (unsigned char) becomes $ChosenOneName; the
    //   ×2 test's party-effective-stat-two-top moves top-scoring-member again but nothing reads it. Each harm is one draw per
    //   statement, in the order agility, endurance, strength, then the battle seed; products stay below 32767.
    private void RunAcross(CardContext c, int chance)
    {
        Show(c, 306);
        c.Screen.Text(1);
        var roll = c.Random(100);
        c.PartyMinEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        int harm;
        if (chance >= roll)
        {
            Show(c, 307);
            c.Screen.Text(2);
            harm = 1;
        }
        else if (c.PartyMaxEffectiveAgility() * 2 >= roll)
        {
            Show(c, 332);
            c.Screen.Text(3);
            harm = 2;
        }
        else
        {
            Show(c, CavernPicture);
            c.Screen.Text(4);
            harm = 3;
        }
        c.RaiseAttributeTemporarily(-2, Agility, -(c.Random(harm + 4) * harm), 24);
        c.AddAttribute(-2, Endurance, -(c.Random(harm + 4) * harm));
        c.AddAttribute(-2, Strength, -(c.Random(harm + 2) * harm));
        LastCastle.Fight(c, CavernEnemy);
    }

    /// <summary>Picking the way across under the armour (the original adds, not takes, endurance and strength here).</summary>
    // orig: R130+0x107E matched
    // BUG: the original adds rand(10)+8 Endurance and rand(6)+6 Strength although text 6 says everyone is hurt (the
    //   prayer's path subtracts the same kind of amounts); kept as the original does it (no OriginalBehaviour flag yet).
    private void CreepAcrossUnderArmour(CardContext c)
    {
        Show(c, CavernPicture);
        c.Screen.Text(6);
        c.AddAttribute(-2, Endurance, c.Random(10) + 8);
        c.AddAttribute(-2, Strength, c.Random(6) + 6);
        BreakTheSecondSeal(c);
        LastCastle.Fight(c, CavernEnemy);
    }

    /// <summary>A saint stops the rain for a while (text 7) or stays silent (text 8, the prayer is greyed out).</summary>
    // orig: R130+0x1160 matched
    // C: returns 0 after a cancelled box (the line stays greyed out), otherwise returns without a value (ignored).
    private void PrayForShelter(CardContext c, CardMenu menu)
    {
        menu[2] = OptionState.Shown;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            Show(c, 307);
            c.Screen.Text(7);
            c.RaiseAttributeTemporarily(-2, Agility, -(c.Random(6) + 6), 24);
            c.AddAttribute(-2, Endurance, -(c.Random(8) + 8));
            c.AddAttribute(-2, Strength, -(c.Random(4) + 4));
            BreakTheSecondSeal(c);
            LastCastle.Fight(c, CavernEnemy);
            return;
        }
        Show(c, CavernPicture);
        c.Screen.Text(8);
        c.Next = 345; // stays
    }

    // orig: R130+0x12D2 matched
    private void ShareTheFirewallPotion(CardContext c)
    {
        c.TakeItem(-2, FirewallPotion);
        Show(c, 332);
        c.Screen.Text(9);
        c.RaiseAttributeTemporarily(-2, Agility, -(c.Random(6) + 6), 24);
        BreakTheSecondSeal(c);
        LastCastle.Fight(c, CavernEnemy);
    }

    /// <summary>Fact 1/0x18, kept for good: the Second Seal is broken and the gate of the burning lake opens.</summary>
    // orig: the event-create repeated inline in R130+0x107E / _0420 / _0592 matched
    private static void BreakTheSecondSeal(CardContext c) =>
        c.Events.Create(-2, c.Here, 0, 0x1C, 0x18, 0, 0, Core.Events.EventLog.Forever, 0, 1);
}
