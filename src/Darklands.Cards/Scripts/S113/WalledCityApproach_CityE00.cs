namespace Darklands.Cards.Scripts;

/// <summary>
/// Card 277 ("$CityE00"): before a walled city — go to the main gate by day (card 2) or by
/// night (card 3), try the wall by day (card 14) or by night (card 15), or travel on. The text
/// and the picture depend on how the city is ruled.
/// </summary>
// orig: R113+0x0656 (189D:0006) matched; tests: CardSmokeTests
// orig: R113+0x0B1C (189D:04CC) matched (registration: sets every opt_on to 1; only lines 0..4 exist in the texts)
public sealed class WalledCityApproach_CityE00 : Card
{
    private const int NightPicture = 6;

    public override int Number => 277;

    public override int Run(CardContext c)
    {
        // C: sound_play(6) (root 047C:078A) directly, not through the music setting.
        c.PlaySound(6);
        // C: cur_loc_query(0x21) % 3 is a signed remainder; a negative value would match no case and keep the old picture.
        var picture = (c.HereQuery(0x21) % 3) switch { 0 => 4, 1 => 6, 2 => 7, _ => -1 };
        // UNCERTAIN: for a negative query the original reloads whatever picture pic_buf held; picture 4 is used.
        Show(c, picture == -1 ? 4 : picture);
        c.Next = Number;
        // C: the option text is chosen anew before each menu; every choice leaves the card, so it cannot change.
        var menu = Menu(c, TextForTheRule(c));
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        // C: cur_rep = loc_reputation(cur_loc) is stored for the texts ($PlaceAttitude).
        var size = c.LocationQuery(c.Here, 2);
        c.Variables.Numbers[0] = size / 2 + 1;
        c.Variables.Numbers[1] = size / 2 + 2;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R113+0x0860 (line 0); hint R113+0x08C6 (189D:0276) matched
            new MenuLine(_ => ToTheGateByDay(c),
                () => OptionHint.Say(c.IsDaylight ? NoRisk : "You'll sleep until dawn.")),
            // orig: R113+0x090C (line 1); hint R113+0x093C (189D:02EC) matched
            new MenuLine(_ => ToTheGateByNight(c),
                () => OptionHint.Say(c.IsDaylight ? "You'll wait for night." : NoRisk)),
            // orig: R113+0x0982 (189D:0332) matched; hint R113+0x09DE
            new MenuLine(_ => TryTheWallByDay(c), () => OptionHint.Say(NoRisk)),
            // orig: R113+0x0A16 (189D:03C6) matched; hint R113+0x0A98
            new MenuLine(_ => TryTheWallByNight(c), () => OptionHint.Say(NoRisk)),
            // orig: R113+0x0AD0 (189D:0480) matched; hint R113+0x0AE2
            new MenuLine(_ => { c.Next = CardContext.MapCard; c.PassHours(0); }, () => OptionHint.Say(NoRisk, c.Leader)));
    }

    /// <summary>The option text by the city's rule (query 0x28): 1 a lord's city (block 0), 0 a capital (block 5), else a free city (block 6).</summary>
    private static int TextForTheRule(CardContext c) => c.HereQuery(0x28) switch { 0 => 5, 1 => 0, _ => 6 };

    // orig: R113+0x0860 (189D:0210) matched; tests: CardSmokeTests
    private static void ToTheGateByDay(CardContext c)
    {
        if (!c.IsDaylight)
        {
            ShowPicture(c, NightPicture);
            c.Screen.Text(1);
            c.WaitUntilHour(5);
        }
        else
        {
            c.PassHours(0);
        }
        c.Next = 2;
        c.PassHours(0);
    }

    // orig: R113+0x090C (189D:02BC) matched; tests: CardSmokeTests
    private static void ToTheGateByNight(CardContext c)
    {
        if (c.IsDaylight)
        {
            c.Screen.Text(2);
            c.WaitUntilHour(19);
        }
        else
        {
            c.PassHours(0);
        }
        c.Next = 3;
    }

    // orig: R113+0x0982 (189D:0332) matched; tests: CardSmokeTests
    private static void TryTheWallByDay(CardContext c)
    {
        ShowPicture(c, NightPicture);
        c.Screen.Text(3);
        c.PassHours(c.LocationQuery(c.Here, 2) / 2 + 1);
        c.Next = 0x0E;
    }

    /// <summary>The search takes hours; if it would end after midnight the party waits only until midnight.</summary>
    // orig: R113+0x0A16 (189D:03C6) matched; tests: CardSmokeTests
    // C: `v <= 24 && v != hours` with v = hour + hours: the second test is false only at hour 0.
    private static void TryTheWallByNight(CardContext c)
    {
        ShowPicture(c, NightPicture);
        c.Screen.Text(4);
        var hours = c.LocationQuery(c.Here, 2) / 2 + 2;
        // C: get_hour() (R029+0x5650, matched: returns cur_hour) + hours.
        var end = c.State.Date.Hour + hours;
        if (end <= 24 && end != hours)
        {
            c.WaitUntilHour(0);
        }
        else
        {
            c.PassHours(hours);
        }
        c.Next = 0x0F;
    }
}
