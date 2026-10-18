namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Busin00": the business district of a city. Through narrow streets the party seeks the
/// arms and armor guilds, the other crafts, the inn, a physician, a grove to wait in, the
/// slum, the main street, a side street, the city wall or a gate.
/// </summary>
// orig: R049+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class BusinessDistrict : Card
{
    public const int CardNumber = 0x14;

    private const int DayPicture = 78, NightPicture = 49;

    private const int MilitaryCrafts = 0x17, Crafts = 0x1A, Inn = 0x1D, InnAtNight = 0x1E, Physician = 0x18, Grove = 0x21,
        GroveAtNight = 0x22, Slum = 0x23, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        CityWall = 0x3B, Gate = 0x3A;

    private const int ClosedPlace = 0x51;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        if (c.IsDaylight)
        {
            c.PlayMusic(0x0C);
        }
        Show(c, c.IsDaylight ? DayPicture : NightPicture);
        var states = new OptionState[10];
        states[0] = states[1] = states[2] = states[4] = states[5] = states[6] = states[7] = states[8] = states[9] = OptionState.Enabled;
        if (c.HereQuery(0x14) == 0)
        {
            states[5] = OptionState.Hidden;
        }
        // C: this test hides the physician (option 3), which started hidden anyway: it is never offered.
        if (c.HereQuery(2) <= 3 && (c.HereQuery(0x21) + c.State.Date.Year) % 3 == 0)
        {
            states[3] = OptionState.Hidden;
        }
        if (c.EventTypeAtHere(ClosedPlace, 0))
        {
            states[2] = OptionState.Shown;
        }
        // DORMANT: R049+0x05AE (1838:05AE) and R049+0x05B0 (1838:05B0) are empty.

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k <= 9; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        menu.Run(
            // orig: R049+0x0298 (1838:0298) matched
            // orig: R049+0x02AA (1838:02AA) matched
            Go(c, () => MilitaryCrafts),
            // orig: R049+0x02E2 (1838:02E2) matched
            // orig: R049+0x02F4 (1838:02F4) matched
            Go(c, () => Crafts),
            // orig: R049+0x032C (1838:032C) matched
            // orig: R049+0x034A (1838:034A) matched
            Go(c, () => c.IsDaylight ? Inn : InnAtNight),
            // DORMANT: option 3 (the physician) is never enabled: it starts hidden and nothing sets it.
            // orig: R049+0x0382 (1838:0382) matched
            // orig: R049+0x0394 (1838:0394) matched
            Go(c, () => Physician),
            // orig: R049+0x03CC (1838:03CC) matched
            // orig: R049+0x03EA (1838:03EA) matched
            Go(c, () => c.IsDaylight ? Grove : GroveAtNight),
            // orig: R049+0x0422 (1838:0422) matched
            // orig: R049+0x0434 (1838:0434) matched
            Go(c, () => Slum),
            // orig: R049+0x046C (1838:046C) matched
            // orig: R049+0x048A (1838:048A) matched
            Go(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight),
            // orig: R049+0x04C2 (1838:04C2) matched
            // orig: R049+0x04E0 (1838:04E0) matched
            Go(c, () => c.IsDaylight ? SideStreet : SideStreetAtNight),
            // orig: R049+0x0518 (1838:0518) matched
            // orig: R049+0x052A (1838:052A) matched
            Go(c, () => CityWall, hours: 1),
            // orig: R049+0x0562 (1838:0562) matched
            // orig: R049+0x0574 (1838:0574) matched
            new MenuLine(() => { c.PassHours(1); c.Next = Gate; }, () => OptionHint.ChanceOf(100, c.Leader)));
        return c.Next;
    }

    /// <summary>An option that walks to a card after <paramref name="hours"/> hours and shows "Success is assured".</summary>
    private static MenuLine Go(CardContext c, Func<int> destination, int hours = 0) =>
        new(() =>
            {
                c.PassHours(hours);
                c.Next = destination();
            },
            OptionHint.Saying(SuccessAssured));
}
