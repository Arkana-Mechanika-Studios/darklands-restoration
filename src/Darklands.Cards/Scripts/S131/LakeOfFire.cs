namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC04": the second gate. While the Second Seal holds (no fact 1/0x18) it stays shut.
/// Beyond it a great lake burns with running flames: the party wades across on strength, swims
/// under the flames on endurance, uses a Firewall potion, prays, or goes back. Getting across
/// leads into the steaming hall beyond and its fight (enemy 0x59); failing turns the party back.
/// </summary>
// orig: R131+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R131+0x01EC; hint R131+0x0362
//   option 1: action R131+0x038A; hint R131+0x04E0
//   option 2: action R131+0x0508; hint R131+0x05BA
//   option 3: action R131+0x05F2; hint R131+0x0740
//   option 4: action R131+0x0778; hint R131+0x0786
//   item pop-up: R131+0x0874
//   saint pop-up: R131+0x0964, R131+0x09EE, R131+0x0A78,
//     R131+0x0B02
//   other: R131+0x07BE, R131+0x083C, R131+0x08B0,
//     R131+0x08EC, R131+0x0928
// DORMANT (generated): option slots 10..19 keep the default handler R131+0x083C ("screwed up.") and are never shown.
// C (generated): card-option-flag-5/6/7/8/9 are never assigned or read here: the card's init zeroes card-option-enabled[0..19], so those lines stay hidden, as in the C#.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class LakeOfFire : Card
{
    public override int Number => 348;

    private const int FirewallPotion = 0x74;
    private const int LakeEnemy = 0x59;

    private static readonly int Endurance = (int)Core.Characters.Attribute.Endurance;
    private static readonly int Strength = (int)Core.Characters.Attribute.Strength;

    public override int Run(CardContext c)
    {
        if (!c.Events.FactAnywhere(1, 0x18))
        {
            // C: the closed gate is drawn from image-buffer-two (image-buffer keeps the last card's picture); this early return runs
            //   before the table set-up, so card-item-lines / card-saint-lines keep the last card's values (unread).
            Show(c, 231);
            c.Screen.Text(12);
            c.BattleMode = 1;
            return c.Next = -2;
        }
        Show(c, 285);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        c.SetCardItems(FirewallPotion);
        c.SetCardSaints(0x12, 0x14, 0x5A, 0x6E);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        // C: every handler of this card redraws with card-run (the whole card, Show), not card-run-near.
        return menu.Run(
            // orig: R131+0x01EC / R131+0x0362 matched
            new MenuLine(chance => WadeAcross(c, chance), () => WeakestTimesThree(c, Strength)),
            // orig: R131+0x038A / R131+0x04E0 matched
            new MenuLine(chance => SwimUnderTheFlames(c, chance), () => WeakestTimesThree(c, Endurance)),
            // orig: R131+0x0508 / R131+0x05BA matched; pop-up R131+0x0874 (/ _08b0 / _08ec / _0928) matched
            new MenuLine(_ => UseTheFirewallPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.SuccessAssured)),
            // orig: R131+0x05F2 / R131+0x0740 matched; pop-up R131+0x0964 / _09ee / _0a78 / _0b02 matched
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), () => LastCastle.SaintHintForChosen(c)),
            // orig: R131+0x0778 / R131+0x0786 matched
            new MenuLine(_ => LastCastle.TurnBack(c), OptionHint.Saying(CardTexts.SuccessAssured)));
    }

    /// <summary>Three times the lowest value of the attribute; that member becomes $ChosenOneName.</summary>
    // orig: R131+0x0362 matched (also _04e0)
    // C: the hint itself sets text-slot-chosen-one to top-scoring-member (unsigned char); the product stays below 32767.
    private static OptionHint WeakestTimesThree(CardContext c, int attribute)
    {
        var chance = c.PartyMinAttribute(attribute) * 3;
        c.Variables.Chosen[0] = c.BestMember;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    /// <summary>Within the chance all get across scorched (text 1); within three times it the weakest is burned badly too (text 2); else everyone is burned and turns back (text 3).</summary>
    // orig: R131+0x01EC matched
    // C: party-stat-low runs before the roll; chance × 3 cannot exceed 16 bits (chance ≤ 300).
    private void WadeAcross(CardContext c, int chance)
    {
        c.PartyMinAttribute(Strength);
        c.Variables.Chosen[0] = c.BestMember;
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.Injure(-2, 1, 1, 10);
            Show(c, 329);
            c.Screen.Text(1);
            IntoTheSteamingHall(c);
        }
        else if (chance * 3 >= roll)
        {
            c.Injure(-2, 1, 1, 10);
            c.Injure(c.Variables.Chosen[0], 1, 1, 10);
            Show(c, 328);
            c.Screen.Text(2);
            IntoTheSteamingHall(c);
        }
        else
        {
            Show(c, 327);
            c.Screen.Text(3);
            c.Injure(-2, 1, 1, 20);
            c.BattleMode = 1;
        }
        c.Next = -2;
    }

    /// <summary>Within the chance all swim through unhurt (text 4); within three times it the weakest is burned (text 5); else everyone is burned and turns back (text 6).</summary>
    // orig: R131+0x038A matched
    private void SwimUnderTheFlames(CardContext c, int chance)
    {
        c.PartyMinAttribute(Endurance);
        c.Variables.Chosen[0] = c.BestMember;
        var roll = c.Random(100);
        if (roll <= chance)
        {
            Show(c, 326);
            c.Screen.Text(4);
            IntoTheSteamingHall(c);
        }
        else if (chance * 3 >= roll)
        {
            c.Injure(c.Variables.Chosen[0], 1, 1, 30);
            Show(c, 326);
            c.Screen.Text(5);
            IntoTheSteamingHall(c);
        }
        else
        {
            c.Injure(-2, 1, 1, 20);
            Show(c, 323);
            c.Screen.Text(6);
            c.BattleMode = 1;
        }
        c.Next = -2;
    }

    // orig: R131+0x0508 matched
    // C: menu-owner-member / menu-choice-index are the globals the pop-up box set last.
    private void UseTheFirewallPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Injure(-2, 1, 1, 10);
        Show(c, 326);
        c.Screen.Text(10);
        IntoTheSteamingHall(c);
        c.Next = -2;
    }

    /// <summary>
    /// The first saint lets them breathe under water (text 7), the others shield them from the
    /// fire but they are singed (text 8); an unanswered prayer leaves them at the shore (text 9).
    /// The original greys out the endurance option here, not the prayer.
    /// </summary>
    // orig: R131+0x05F2 matched
    // C: returns 0 after a cancelled box, otherwise returns without a value (ignored).
    private void Pray(CardContext c, CardMenu menu)
    {
        menu[1] = OptionState.Shown;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    Show(c, 324);
                    c.Screen.Text(7);
                    ShowTheSteamingHall(c);
                    break;
                case 1 or 2 or 3:
                    c.Injure(-2, 1, 1, 10);
                    Show(c, 326);
                    c.Screen.Text(8);
                    ShowTheSteamingHall(c);
                    break;
                // DORMANT: a stale menu-choice-index outside 0..3 skips the texts and goes straight to the fight.
            }
            LastCastle.Fight(c, LakeEnemy);
            return;
        }
        Show(c, 330);
        c.Screen.Text(9);
        c.Next = Number;
    }

    /// <summary>The far shore: steam and bursts of fire from the grates (text 11).</summary>
    // orig: the common tail repeated in R131+0x01EC / _038a / _0508 / _05f2 matched
    private void ShowTheSteamingHall(CardContext c)
    {
        Show(c, 325);
        c.Screen.Text(11);
    }

    /// <summary>The far shore and its fight.</summary>
    private void IntoTheSteamingHall(CardContext c)
    {
        ShowTheSteamingHall(c);
        LastCastle.Fight(c, LakeEnemy);
    }
}
