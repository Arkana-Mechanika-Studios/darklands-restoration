namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol03": the party sits down with its contact in a city's political struggle to weigh
/// the situation: the factions' attitudes, posting handbills, influencing people, secrets,
/// raids, and (when the balance allows) marching on the council hall or the fortress, or
/// arresting the rebels.
/// </summary>
// orig: R074+0x0BEA (18F6:000A) matched; tests: CardSmokeTests
// orig: R074+0x1370 (18F6:0790) matched; R074+0x1372 (18F6:0792) matched; R074+0x13AA (18F6:07CA) matched; R074+0x1434 (18F6:0854) matched; R074+0x14BE (18F6:08DE) matched; R074+0x1548 (18F6:0968) matched; tests: CardSmokeTests
// DORMANT: the registration routine is empty, so the "screwed up." default and the four saint
// pop-up hints are never installed, and card_items/card_saints keep an earlier card's values
// (no line of this card uses them).
// UNCERTAIN: party_x (DS:E3D0, the map X) is read as the side taken in the city: see CardContext.CityMark.
public sealed class FactionPlanningSession : Card
{
    public const int CardNumber = 0x79;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 4, 9);
        var withTheGovernment = c.CityMark != 0;
        if (!withTheGovernment)
        {
            c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
            if (c.HereQuery(0x17) != 0)
            {
                menu[5] = OptionState.Enabled;
                menu[6] = OptionState.Enabled;
            }
        }
        else
        {
            c.RandomName(c.City.Ordinal + 0x16, 2, 0);
            menu[7] = OptionState.Enabled;
        }

        // orig: R074+0x10FC (18F6:051C) matched; R074+0x1110 (18F6:0530) matched; R074+0x1148 (18F6:0568) matched; R074+0x115C (18F6:057C) matched; R074+0x1194 (18F6:05B4) matched; R074+0x11A8 (18F6:05C8) matched; R074+0x11E0 (18F6:0600) matched; R074+0x11F4 (18F6:0614) matched; tests: CardSmokeTests
        MenuLine Talk(int text) => new(() => c.Screen.Text(text), OptionHint.Saying(NoRisk));
        return menu.Run(
            new MenuLine(() => DiscussTheFactions(c, withTheGovernment), OptionHint.Saying(NoRisk)),
            Talk(5),
            Talk(4),
            Talk(6),
            Talk(7),
            // orig: R074+0x122C (18F6:064C) matched; R074+0x1234 (18F6:0654) matched; R074+0x126C (18F6:068C) matched; R074+0x1274 (18F6:0694) matched; R074+0x12AC (18F6:06CC) matched; R074+0x12B4 (18F6:06D4) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x70, OptionHint.Saying(NoRisk)),
            new MenuLine(() => c.Next = 0x70, OptionHint.Saying(NoRisk)),
            new MenuLine(() => c.Next = 0x70, OptionHint.Saying(NoRisk)),
            // orig: R074+0x12EC (18F6:070C) matched; R074+0x12F8 (18F6:0718) matched; tests: CardSmokeTests
            // DORMANT: a placeholder line ("8") that is never offered (flag 0); it would only let no time pass.
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R074+0x1330 (18F6:0750) matched; R074+0x1338 (18F6:0758) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = c.State.SavedCard, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R074+0x0E74 (18F6:0294) matched; R074+0x10C4 (18F6:04E4) matched; tests: CardSmokeTests
    /// <summary>Who supports change: merchants, clergy and people, the guilds; then whether the time has come.</summary>
    private static void DiscussTheFactions(CardContext c, bool withTheGovernment)
    {
        var shown = c.Variables.Conditions;
        Array.Clear(shown);
        c.Variables.Support[1] = c.SideOf(0x0001);
        c.Variables.Support[2] = c.SideOf(0x2000);
        c.Variables.Support[3] = c.SideOf(0x4000);
        c.Variables.Support[4] = c.SideOf(0x8000);
        c.Variables.Support[5] = c.SideOf(0x1000);
        shown[0] = c.HereQuery(0x0E) != 0;
        shown[1] = c.HereQuery(0x0C) != 0;
        shown[2] = c.HereQuery(0x0D) != 0;
        c.Screen.Text(1);

        Array.Clear(shown);
        c.Variables.Support[2] = c.SideOf(0x0800);
        c.Variables.Support[3] = c.SideOf(0x0400);
        shown[0] = c.HereQuery(0x1C) != 0;
        c.Screen.Text(2);

        Array.Clear(shown);
        c.Variables.Support[0] = c.SideOf(0x0002);
        c.Variables.Support[1] = c.SideOf(0x0004);
        c.Variables.Support[2] = c.SideOf(0x0010);
        c.Variables.Support[3] = c.SideOf(0x0008);
        c.Variables.Support[4] = c.SideOf(0x0020);
        c.Variables.Support[5] = c.SideOf(0x0080);
        c.Variables.Support[6] = c.SideOf(0x0040);
        shown[0] = c.HereQuery(4) != 0;
        shown[1] = c.HereQuery(5) != 0;
        shown[2] = c.HereQuery(6) != 0;
        shown[3] = c.HereQuery(8) != 0;
        c.Screen.Text(3);

        if (!c.Events.Fact(2, 0x16, c.Here) && !c.Events.Fact(2, 0x15, c.Here))
        {
            Array.Clear(shown);
            var balance = withTheGovernment ? c.SidePercent(1) : c.SidePercent(-1);
            if (balance >= 200)
            {
                if (!withTheGovernment && c.HereQuery(0x18) != 0)
                {
                    shown[0] = true;
                }
                else if (!withTheGovernment)
                {
                    shown[1] = true;
                }
                else
                {
                    shown[2] = true;
                }
                c.Screen.Text(8);
            }
        }
        else
        {
            c.Screen.Text(withTheGovernment ? 9 : 0x0A);
        }
        c.Next = CardNumber;
    }
}
