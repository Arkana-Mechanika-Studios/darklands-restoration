namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG05": a small grove of trees in a quiet corner of the city, by day. The party rests
/// an hour, strolls for a bell, waits for nightfall (and sleeps if that is long), or leaves
/// down the main street or a side street. The grove at night is card 0x22.
/// </summary>
// orig: R052+0x0AC6 (18E4:0006) matched; tests: CardSmokeTests
public sealed class GroveByDay_CityG05 : Card
{
    public const int CardNumber = 0x21;

    private const int Picture = 47, Music = 0x17;

    private const int GroveAtNight = 0x22, MainStreet = 0x06, MainStreetAtNight = 0x08,
        SideStreet = 0x09, SideStreetAtNight = 0x0A, CannotEnter = 0x62;

    private const int Nightfall = 18;

    public override int Number => CardNumber;

    // orig: R052+0x0AC6 (18E4:0006) matched (entry: music, picture, option states, menu loop)
    // orig: R052+0x104C (18E4:058C) matched (init helper: switches all 20 option lines off and installs the "screwed up." default; the entry then enables lines 0..2, 8 and 9)
    // DORMANT: R052+0x106E (18E4:05AE): the "screwed up." default hint for card-option-handler[0..19]; every line the card can show has its own hint, the others are never selectable.
    public override int Run(CardContext c)
    {
        c.PlayMusic(Music);
        Show(c, Picture);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R052+0x0CEA (18E4:022A) matched (option 0: enjoy the view for an hour); hint R052+0x0D14 (18E4:0254) (success assured)
            new MenuLine(() =>
            {
                c.Screen.Text(1);
                c.PassHours(1);
                c.Next = ThisGrove(c);
            }),
            // orig: R052+0x0D4C (18E4:028C) matched (option 1: stroll for a bell); hint R052+0x0D76 (18E4:02B6) (success assured)
            new MenuLine(() =>
            {
                c.Screen.Text(2);
                c.PassHours(3);
                c.Next = ThisGrove(c);
            }),
            // orig: R052+0x0DAE (18E4:02EE) matched (option 2: wait until nightfall); hint R052+0x0DFC (18E4:033C) (success assured)
            new MenuLine(() =>
            {
                var hours = HoursUntilNightfall(c) + 1;
                c.Screen.Text(3);
                c.PassHours(hours);
                if (hours >= 8)
                {
                    c.Variables.Numbers[0] = hours;
                    c.Screen.Text(4);
                }
                c.Next = ThisGrove(c);
            }),
            // DORMANT: options 3..7 are switched off by the entry (the MSG lines are the placeholders "3".."7").
            // orig: R052+0x0E34 (18E4:0374) matched (option 3); hint R052+0x0E40 (18E4:0380) matched: passes no time and names no next card, so it would stay on the card.
            new MenuLine(() => c.PassHours(0)),
            // orig: R052+0x0E78 (18E4:03B8) matched (option 4); hint R052+0x0E84 (18E4:03C4) matched (success assured for the leader): the same.
            new MenuLine(() => c.PassHours(0), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R052+0x0EBE (18E4:03FE) matched (option 5); hint R052+0x0ED0 (18E4:0410) matched: next card 0x62.
            new MenuLine(() => { c.PassHours(0); c.Next = CannotEnter; }),
            // orig: R052+0x0F08 (18E4:0448) matched (option 6); hint R052+0x0F1A (18E4:045A) matched (100% for the leader): next card 0x62.
            new MenuLine(() => { c.PassHours(0); c.Next = CannotEnter; }, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x0F54 (18E4:0494) matched (option 7); hint R052+0x0F66 (18E4:04A6) matched (100% for the leader): next card 0x62.
            new MenuLine(() => { c.PassHours(0); c.Next = CannotEnter; }, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R052+0x0FA0 (18E4:04E0) matched (option 8: leave down the main street); hint R052+0x0FBE (18E4:04FE) (success assured)
            new MenuLine(() =>
            {
                c.PassHours(1);
                c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight;
            }),
            // orig: R052+0x0FF6 (18E4:0536) matched (option 9: leave down a side street); hint R052+0x1014 (18E4:0554) (success assured)
            new MenuLine(() =>
            {
                c.PassHours(1);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            }));
    }

    /// <summary>After the time has passed the party is in the grove of the day or the night.</summary>
    private static int ThisGrove(CardContext c) => c.IsDaylight ? CardNumber : GroveAtNight;

    private static int HoursUntilNightfall(CardContext c) => Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, Nightfall);
}
