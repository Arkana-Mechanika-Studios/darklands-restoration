namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG06": the quiet grove of the city by night. The party waits an hour or a bell in the
/// moonlight, camps in the shadows until morning (which costs a little standing with the
/// city), or leaves down the main street or a side street. The grove by day is card 0x21.
/// </summary>
// orig: R052+0x10A6 (1942:0006) matched; tests: CardSmokeTests
public sealed class GroveAtNight_CityG06 : Card
{
    public const int CardNumber = 0x22;

    private const int Picture = 48, Music = 0x1D;

    private const int GroveByDay = 0x21, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A;

    private const int Dawn = 5;

    public override int Number => CardNumber;

    // orig: R052+0x10A6 (1942:0006) matched (entry: music, picture, option states, menu loop)
    // orig: R052+0x164E (1942:05AE) matched (init helper: enables all 20 option lines and installs the "screwed up." default; the entry then sets lines 0..9, so lines 10..19 stay enabled but have no text line)
    // DORMANT: R052+0x1670 (1942:05D0): the "screwed up." default hint for opt_fn[0..19]; lines 0..9 have their own hints and lines 10..19 cannot be chosen.
    // UNCERTAIN: text block 4 ("Food is just as important as sleep ...") is never shown by this card's code.
    public override int Run(CardContext c)
    {
        c.PlayMusic(Music);
        Show(c, Picture);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R052+0x12CA (1942:022A) matched (option 0: pause for an hour); hint R052+0x12F4 (1942:0254) (100% for the leader)
            new MenuLine(() =>
            {
                c.Screen.Text(1);
                c.PassHours(1);
                c.Next = ThisGrove(c);
            }, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x132E (1942:028E) matched (option 1: pause for a bell); hint R052+0x1358 (1942:02B8) (success assured for the leader)
            new MenuLine(() =>
            {
                c.Screen.Text(2);
                c.PassHours(3);
                c.Next = ThisGrove(c);
            }, () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R052+0x1392 (1942:02F2) matched (option 2: camp until morning, at a small cost in standing); hint R052+0x140A (1942:036A) (100% for the leader)
            new MenuLine(() =>
            {
                c.Screen.Text(3);
                c.Variables.Numbers[0] = Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, Dawn);
                c.PassHours(c.Variables.Numbers[0]);
                c.AdjustReputation(c.Here, -1, -1);
                ShowPicture(c, Picture);
                c.Screen.Text(5);
                c.Next = GroveByDay;
            }, () => OptionHint.ChanceOf(100, c.Leader)),
            // DORMANT: options 3..7 are switched off by the entry (the MSG lines are the placeholders "3".."7"); each only lets no time pass and names no next card.
            // orig: R052+0x1444 (1942:03A4) matched (option 3); hint R052+0x1450 (1942:03B0) matched
            new MenuLine(() => c.PassHours(0)),
            // orig: R052+0x1488 (1942:03E8) matched (option 4); hint R052+0x1494 (1942:03F4) matched (success assured for the leader)
            new MenuLine(() => c.PassHours(0), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R052+0x14CE (1942:042E) matched (option 5); hint R052+0x14DA (1942:043A) matched
            new MenuLine(() => c.PassHours(0)),
            // orig: R052+0x1512 (1942:0472) matched (option 6); hint R052+0x151E (1942:047E) matched (100% for the leader)
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x1558 (1942:04B8) matched (option 7); hint R052+0x1564 (1942:04C4) matched (100% for the leader)
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x159E (1942:04FE) matched (option 8: leave by the main street); hint R052+0x15BC (1942:051C) (100% for the leader)
            new MenuLine(() =>
            {
                c.PassHours(1);
                c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight;
            }, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x15F6 (1942:0556) matched (option 9: slip away down a side street); hint R052+0x1614 (1942:0574) (100% for the leader)
            new MenuLine(() =>
            {
                c.PassHours(1);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            }, () => OptionHint.ChanceOf(100, c.Leader)));
    }

    /// <summary>After the time has passed the party is in the grove of the day or the night.</summary>
    private static int ThisGrove(CardContext c) => c.IsDaylight ? GroveByDay : CardNumber;
}
