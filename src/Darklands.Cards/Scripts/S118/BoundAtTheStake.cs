using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$wqSta04": captured by the Templars (card 295), the party is bound to the stake as a
/// sacrifice to Baphomet. It can bear its fate (the end of the game), buy them off with all
/// its money (at least 1000 pfennigs), break the ropes, slip loose a potion, or call on a saint.
/// </summary>
// orig: R118+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R118+0x0548 (1838:0548) matched (registration: item pop-ups R118+0x0616/063c/0662 name the member; saint pop-ups do not)
// DORMANT: texts 11 and 12 are never shown.
public sealed class BoundAtTheStake : Card
{
    public override int Number => 296;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.Variables.Chosen[0] = c.Leader;
        // C: party-map-column is declared `extern long`, so the call pushes party-map-row and party-map-column (and 0x24): closest-place-of-kind gets (x, y, 0x24); nothing is undefined (checked against the asm).
        c.Variables.Location = c.NearestLocationOfType(c.State.MapPosition.X, c.State.MapPosition.Y, 0x24);
        c.Variables.Location2 = c.SabbatSite();
        // C: the slots are assigned out of order: items 0, 2, saints 0, 1, then item 1 and saint 2 (0x67 both).
        c.SetCardItems(0x63, 0x67, 0x74);
        c.SetCardSaints(0x14, 0x5A, 0x67, 0x6E);
        menu[3] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R118+0x0270 (1838:0270) matched; hint R118+0x0284
            // The pyre: the game ends (-0x63).
            new MenuLine(_ => { c.Screen.Text(1); c.Next = -0x63; }, () => OptionHint.Say(SuccessAssured)),
            // orig: R118+0x02BC (1838:02BC) matched; hint R118+0x0316
            new MenuLine(_ => BuyThemOff(c, menu), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R118+0x034E (line 2); hint R118+0x0390 (1838:0390) matched
            new MenuLine(p => BreakTheRopes(c, menu, p), () => StrengthHint(c)),
            // orig: R118+0x03B8 (line 3); hint R118+0x0444 (1838:0444) matched
            new MenuLine(p => SlipLooseAPotion(c, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R118+0x047C (line 4); hint R118+0x04FC (1838:04FC) matched
            new MenuLine(_ => CallOnASaint(c), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 5..9 are never offered; actions R118+0x0534.. and hints R118+0x0536.. are empty
            // C: the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    /// <summary>The potion's chance: the member's Artifice plus 50, 30 or 50 by slot.</summary>
    // orig: R118+0x0616 (1838:0616) matched; slots 1, 2 R118+0x063C, R118+0x0662
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        return OptionHint.ChanceOf(c.Skill(m, (int)Skill.Artifice) + (c.MenuSlot == 1 ? 30 : 50), m);
    }

    // C: (party-stat-top(1) - 0x23) * 5, unclamped (negative for a weak party); the strongest member is shown.
    private static OptionHint StrengthHint(CardContext c)
    {
        var chance = (c.PartyMaxAttribute((int)Attribute.Strength) - 35) * 5;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R118+0x02BC (1838:02BC) matched; tests: CardSmokeTests
    private static void BuyThemOff(CardContext c, CardMenu menu)
    {
        if (c.State.Party.Purse.TotalPfennigs >= 1000)
        {
            c.State.Party.Purse = Money.Zero;
            c.Screen.Text(2);
            c.LeaveForTheMap();
            return;
        }
        menu[1] = OptionState.Shown;
        c.Screen.Text(13);
        c.Next = 296;
    }

    // orig: R118+0x034E (1838:034E) matched; tests: CardSmokeTests
    private static void BreakTheRopes(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(3);
            c.LeaveForTheMap();
            return;
        }
        menu[2] = OptionState.Shown;
        c.Screen.Text(4);
        c.Next = 296;
    }

    /// <summary>
    /// The potion burns the ropes (slots 0, 1) or protects from the flames (slot 2, and the
    /// friar is freed too). The vial is not taken, though a failure's text says it is lost.
    /// </summary>
    // orig: R118+0x03B8 (1838:03B8) matched; tests: CardSmokeTests
    // C: a slot 3 success matches no case: nothing happens and the card asks again.
    private static void SlipLooseAPotion(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        if (c.Random(100) <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    c.Screen.Text(5);
                    c.PracticeSkill(c.MenuMember, (int)Skill.Artifice, 1, 10, 0);
                    c.LeaveForTheMap();
                    break;
                case 2:
                    c.Screen.Text(7);
                    c.Screen.Text(8);
                    c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, 0);
                    c.PracticeSkill(c.MenuMember, (int)Skill.Artifice, 1, 10, 0);
                    c.LeaveForTheMap();
                    break;
            }
            return;
        }
        c.Screen.Text(6);
        c.Next = 296;
    }

    /// <summary>Any prayer that is made frees the party: the answered one through the fire (text 10), the other by the saint's name alone (text 9).</summary>
    // orig: R118+0x047C (1838:047C) matched; tests: CardSmokeTests
    private static void CallOnASaint(CardContext c)
    {
        var saint = c.CardSaints[c.MenuEntrySlot];
        c.Variables.Chosen[0] = c.MenuMember;
        c.NameToSlot(1, saint, 0);
        var answer = c.InvokeSaint(c.MenuMember, saint);
        if (answer == -1)
        {
            return;
        }
        c.Screen.Text(answer == 1 ? 10 : 9);
        c.Screen.Text(8);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, 0);
        c.LeaveForTheMap();
    }
}
