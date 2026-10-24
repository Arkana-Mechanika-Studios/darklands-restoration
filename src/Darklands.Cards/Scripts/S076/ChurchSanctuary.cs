namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Sanct00": the party has fled into a church and claims sanctuary while the city guard
/// watches outside. Rest until night or day, give up, ask the captain of the guard how
/// things stand, or try to sneak out.
/// </summary>
// orig: R076+0x0430 (187B:0000) matched; tests: CardSmokeTests
// orig: R076+0x098E (187B:055E) matched; R076+0x09C6 (187B:0596) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
// C: card_load with another picture reloads the same texts; ShowPicture shows the picture only.
// C: texts 4..8 (a second menu, the priest's warnings and the forced departure) and 12 (a failed escape
// ending in capture) are never shown by this card, the only one that loads $Sanct00; an indirect loader is not excluded.
public sealed class ChurchSanctuary : Card
{
    public const int CardNumber = 0x81;

    private const int Picture = 271;
    private const int GuardsPicture = 160;
    private const int SneakingOutPicture = 132;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // In daylight the party can rest until nightfall, at night until daybreak.
        var menu = Menu(c);
        menu.Enable(2, 3, 4);
        menu[0] = c.IsDaylight ? OptionState.Enabled : OptionState.Hidden;
        menu[1] = c.IsDaylight ? OptionState.Hidden : OptionState.Enabled;
        c.RandomName(c.City.Ordinal + 0x156, 0, 0);
        var standing = c.ReputationHere;
        c.SetCardSaints();
        c.SetCardItems();

        return menu.Run(
            // orig: R076+0x05D8 (187B:01A8) matched; R076+0x05FE (187B:01CE) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.WaitUntilHour(19);
                menu[0] = OptionState.Hidden;
                menu[1] = OptionState.Enabled;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R076+0x0636 (187B:0206) matched; R076+0x065C (187B:022C) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.WaitUntilHour(6);
                menu[1] = OptionState.Hidden;
                menu[0] = OptionState.Enabled;
            }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => GiveUp(c, standing), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => AskTheCaptain(c, standing), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => SneakOut(c), () => ChanceToSneakOut(c)));
    }

    /// <summary>
    /// Giving up: a party not hated too much is led to the dungeon. A hated one is attacked by
    /// the guard; if it wins, prices rise against it; either way the city stays hostile.
    /// </summary>
    // orig: R076+0x0694 (187B:0264) matched; R076+0x07EA (187B:03BA) matched; tests: CardSmokeTests
    private static void GiveUp(CardContext c, int standing)
    {
        // The original tests the two bands separately, with the same outcome.
        if (standing > -10)
        {
            ShowPicture(c, GuardsPicture);
            c.Screen.Text(0x0B);
            c.Next = 0x0D;
            return;
        }
        if (standing > -75)
        {
            ShowPicture(c, GuardsPicture);
            c.Screen.Text(0x0B);
            c.Next = 0x0D;
            return;
        }
        // C: arguments are evaluated right to left (party_lore_level draws nothing).
        var third = c.Random(3) + 1;
        var second = c.Random(5) + 4;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        var result = c.RunBattle(0x18, c.Here + 0x6F, 3, first, second, 0, third, 1, -1, 3, 0);
        c.AdjustReputation(c.Here, -1, -5);
        // C: int division; a place of size 0 would divide by zero as in the original.
        var hours = 2000 / c.LocationQuery(c.Here, 2);
        switch (result)
        {
            case 0:
            case 1:
                c.Events.Create(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(1);
                c.Next = 0x13;
                break;
            case 2:
                c.ReportAway();
                c.PassHours(1);
                break;
            case 3:
            case 4:
                c.PassHours(3);
                c.Next = 0x0D;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party in the sanctuary.
                break;
        }
        // DORMANT: the 120-hour arm: only a party at -75 or worse gets here.
        c.EventHere(0x11, standing <= -75 ? 240 : 120);
    }

    // orig: R076+0x0822 (187B:03F2) matched; R076+0x0874 (187B:0444) matched; tests: CardSmokeTests
    /// <summary>The captain's answer depends on the party's standing in the city.</summary>
    private static void AskTheCaptain(CardContext c, int standing)
    {
        c.Screen.Text(standing > -10 ? 1 : standing > -75 ? 2 : 3, TextFrame.BlankPage);
        c.PassHours(c.Random(3));
        ShowPicture(c, Picture);
    }

    // orig: R076+0x095E (187B:052E) matched; tests: CardSmokeTests
    private static OptionHint ChanceToSneakOut(CardContext c)
    {
        var chance = Math.Clamp(c.PartyMinSkillSum(0x0F, 0x10), 10, 99);
        // C: best_member is read as an unsigned char.
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R076+0x08AC (187B:047C) matched; tests: CardSmokeTests
    private static void SneakOut(CardContext c)
    {
        var chance = ChanceToSneakOut(c).Chance;
        if (c.Random(100) > chance)
        {
            ShowPicture(c, GuardsPicture);
            c.Screen.Text(9);
            c.PassHours(1);
            ShowPicture(c, Picture);
            return;
        }
        ShowPicture(c, SneakingOutPicture);
        c.Screen.Text(0x0A);
        c.PassHours(2);
        c.Next = c.IsDaylight ? 9 : 0x0A;
    }
}
