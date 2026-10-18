namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityE00": before the walls of a city. The party goes to the gate (by day, or waits for
/// dawn), waits for night, walks round the walls to another gate or into the outskirts by
/// night, or goes back to the open road.
/// </summary>
// orig: R045+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class CityOutskirts : Card
{
    public const int CardNumber = 0x04;

    private const int Picture = 4, ApproachPicture = 5, WallsPicture = 6, NightPicture = 63;

    private const int CityGate = 0x02, CityGateAtNight = 0x03, OverlandMap = 0x0C, WallsByDay = 0x0E, WallsByNight = 0x0F;

    public override int Number => CardNumber;

    // orig: R045+0x0000 (1838:0000) matched (entry: music, the numbers for the texts, the text block by city kind (cur_loc_query 0x28 selects card_options 6/5/0) and the menu loop)
    // orig: R045+0x04C0 (1838:04C0) matched (init helper: enables all 20 option lines and clears the item and saint slots; the card then fills lines 0..4)
    // DORMANT: R045+0x04F8 (1838:04F8) matched: the init helper points opt_fn[0..19] at this "screwed up." default; lines 5..19 stay enabled but the card's MSG has no line for them and the menu cannot select them, so it never runs.
    // DORMANT: the card has no items and no saints (card_items/card_saints stay -1), so no item_fn/saint_fn pop-up handler exists or is ever opened.
    public override int Run(CardContext c)
    {
        c.PlayMusic(c.IsDaylight ? 0x1B : 0x1C);
        Show(c, Picture);
        c.StandingSeen = c.Reputation(c.Here);
        c.Variables.Numbers[0] = c.HereQuery(2) / 2 + 1;
        c.Variables.Numbers[1] = c.HereQuery(2) / 2 + 2;
        // The text depends on the city's kind (loc_query 40): 1 the usual one, 0 and others their own.
        var block = c.HereQuery(0x28) switch
        {
            0 => 5,
            1 => 0,
            _ => 6,
        };
        var menu = Menu(c, block);
        menu.Enable(0, 1, 2, 3, 4);
        c.Next = Number;
        return menu.Run(
            // orig: R045+0x023E (1838:023E) matched (option 0 hint: no risk by day, sleep until dawn by night; action R045+0x01D8, see GoToTheGate)
            new MenuLine(() => GoToTheGate(c), () => OptionHint.Say(c.IsDaylight ? NoRisk : CardTexts.SleepUntilDawn)),
            // orig: R045+0x02E0 (1838:02E0) matched (option 1 hint: wait for night by day, no risk by night; action R045+0x0284, see WaitForNight)
            new MenuLine(() => WaitForNight(c), () => OptionHint.Say(c.IsDaylight ? CardTexts.WaitForNight : NoRisk)),
            // orig: R045+0x0382 (1838:0382) matched (option 2 hint "no risk"; action R045+0x0326, see WalkAroundTheWalls)
            new MenuLine(() => WalkAroundTheWalls(c), OptionHint.Saying(NoRisk)),
            // orig: R045+0x043C (1838:043C) matched (option 3 hint "no risk"; action R045+0x03BA, see CreepAlongTheWallsByNight)
            new MenuLine(() => CreepAlongTheWallsByNight(c), OptionHint.Saying(NoRisk)),
            // orig: R045+0x0486 (1838:0486) matched (option 4 hint "no risk" naming the leader; and its action R045+0x0474 (1838:0474): next card 0x0C, no time passes)
            new MenuLine(() => { c.Next = OverlandMap; c.PassHours(0); }, () => OptionHint.Say(NoRisk, c.Leader)));
    }

    /// <summary>By night the party sleeps until seven in the morning first.</summary>
    // orig: R045+0x01D8 (1838:01D8) matched
    private static void GoToTheGate(CardContext c)
    {
        if (!c.IsDaylight)
        {
            ShowPicture(c, ApproachPicture);
            c.Screen.Text(1);
            c.WaitUntilHour(7);
        }
        else
        {
            c.PassHours(0);
        }
        c.Next = CityGate;
        c.PassHours(0);
    }

    /// <summary>By day the party waits until eight in the evening.</summary>
    // orig: R045+0x0284 (1838:0284) matched
    private static void WaitForNight(CardContext c)
    {
        if (c.IsDaylight)
        {
            ShowPicture(c, ApproachPicture);
            c.Screen.Text(2);
            c.WaitUntilHour(20);
        }
        else
        {
            c.PassHours(0);
        }
        c.Next = CityGateAtNight;
    }

    /// <summary>The walk takes longer round a bigger city.</summary>
    // orig: R045+0x0326 (1838:0326) matched
    private static void WalkAroundTheWalls(CardContext c)
    {
        ShowPicture(c, WallsPicture);
        c.Screen.Text(3);
        c.PassHours(c.HereQuery(2) / 2 + 1);
        c.Next = WallsByDay;
    }

    /// <summary>
    /// Goes round the walls in the dark: if the walk would end before midnight the party waits
    /// until midnight, otherwise (or when it takes no time) the walk's hours pass.
    /// </summary>
    // orig: R045+0x03BA (1838:03BA) matched
    private static void CreepAlongTheWallsByNight(CardContext c)
    {
        ShowPicture(c, NightPicture);
        c.Screen.Text(4);
        var hours = c.HereQuery(2) / 2 + 2;
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
        c.Next = WallsByNight;
    }
}
