namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol04": listening to the town talk about its politics. The party may ask how the
/// merchants and leagues (text 1), the clergy (text 2) or the guilds (text 3) stand to a
/// change of government, or leave.
/// </summary>
/// <remarks>
/// The original never sets the first line's flag: it shows as the card before left opt_flag_0
/// (normally a city hub card, which leaves it offered or greyed).
/// </remarks>
// orig: R091+0x0BA8 (18F2:0008) matched; tests: CardSmokeTests
public sealed class CityPoliticsGossip : Card
{
    private const int Picture = 0;

    public override int Number => 0xBF;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // (The original takes its own copy of the reputation here, which nothing reads.)
        var menu = Menu(c);
        // C: opt_flag_0 is never assigned: line 0 is what the card before left (OptionFlagCopies stale bit 0).
        menu.Enable(1, 2, 3, 4, 5, 6, 7, 8, 9);
        // orig: R091+0x11F8 (18F2:0658) matched (resets the handlers only)
        return menu.Run(
            // orig: R091+0x0DCA (18F2:022A); hint R091+0x0E9A matched
            new MenuLine(() => Ask(c, 3, [(0, 2), (1, 4), (2, 0x10), (3, 8), (4, 0x20), (5, 0x40)], [4, 5, 7, 6, 8])),
            // orig: R091+0x0ED2 (18F2:0332); hint R091+0x0F74 matched
            // C: flag −0x8000 is bit 15 (0x8000 as a signed int).
            new MenuLine(() => Ask(c, 1, [(1, 1), (2, 0x2000), (3, 0x4000), (4, -0x8000), (5, 0x1000)], [0x0E, 0x0C, 0x0D])),
            // orig: R091+0x0FAC (18F2:040C); hint R091+0x0FFE matched
            new MenuLine(() => Ask(c, 2, [(2, 0x800), (3, 0x400)], [0x1C])),
            // orig: R091+0x1036; R091+0x1076 (stay); R091+0x10B6; R091+0x10F4;
            // R091+0x1132; R091+0x1170 (empty) matched: lines 3..8 do nothing.
            new MenuLine(() => c.Next = 0xBF),
            new MenuLine(() => c.Next = 0xBF),
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            // orig: R091+0x11AE (18F2:060E); hint R091+0x11C0 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.ReturnCard; }));
    }

    /// <summary>
    /// Fills $Support… with the sides of some groups (the city's political flags) and shows the
    /// passages about the buildings the city has (location values), then the answer.
    /// </summary>
    private static void Ask(CardContext c, int text, (int Slot, int Flag)[] groups, int[] buildings)
    {
        Array.Clear(c.Variables.Conditions);
        foreach (var (slot, flag) in groups)
        {
            c.Support[slot] = c.SideOf(flag);
        }
        for (var k = 0; k < buildings.Length; k++)
        {
            if (c.HereQuery(buildings[k]) != 0)
            {
                c.Variables.Conditions[k] = true;
            }
        }
        c.Screen.Text(text);
        c.Next = 0xBF;
    }
}
