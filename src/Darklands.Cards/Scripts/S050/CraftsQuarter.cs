namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CivCr00": the civilian crafts quarter. The party visits the physician, the apothecary
/// (card 0x59), the jeweller, the artificer, the clothmaker, the military crafts quarter, or
/// goes back to the side or main street. By night anyone abroad may meet the night encounter
/// (and the shops take an hour to rouse). Small towns lack some shops in some years.
/// </summary>
// orig: R050+0x14A0 (1982:0000) matched; tests: CardSmokeTests
// DORMANT: R050+0x1CB4 (1982:0814) is an empty init; R050+0x1CB6 (1982:0816)
//   a "screwed up." handler nothing installs. Lines 10..19 keep the card before's option table.
public sealed class CraftsQuarter : Card
{
    public const int CardNumber = 0x1A;

    private const int Picture = 34;

    private const int MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        MilitaryCrafts = 0x17, Physician = 0x18, Clothmaker = 0x25, ClothmakerAtNight = 0x26, Artificer = 0x57,
        ArtificerAtNight = 0x58, Apothecary = 0x59, Jeweller = 0x5D, JewellerAtNight = 0x5E, NightEncounter = 0x3C, NoGo = 0x62;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x0C);
        Show(c, Picture);
        var physician = OptionState.Enabled;
        var apothecary = OptionState.Enabled;
        // C: loc_query 33 is the location's own random seed; with the year it decides, in small towns, which shops are there.
        if (c.HereQuery(2) <= 3 && (c.HereQuery(0x21) + c.State.Date.Year) % 3 == 0)
        {
            physician = OptionState.Hidden;
        }
        if (c.HereQuery(2) <= 3 && c.HereQuery(0x21) % 2 == 0)
        {
            apothecary = OptionState.Hidden;
        }
        if (c.HereQuery(2) == 4 && c.HereQuery(0x21) % 3 == 0)
        {
            apothecary = OptionState.Hidden;
        }
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 3, 4, 7, 8, 9);
        menu[0] = physician;
        menu[1] = apothecary;
        c.Next = Number;
        return menu.Run(
            // orig: R050+0x1776 (1982:02D6) + R050+0x173A (1982:029A) R050 seg 1982 matched
            Visit(c, () => Physician, roused: false),
            // orig: R050+0x1830 (1982:0390) + R050+0x17F4 (1982:0354) R050 seg 1982 matched
            Visit(c, () => Apothecary, roused: false),
            // orig: R050+0x1904 (1982:0464) + R050+0x18AE (1982:040E) R050 seg 1982 matched
            // DORMANT: opt_flag_2 is 0: the jeweller is never offered here.
            Visit(c, () => c.IsDaylight ? Jeweller : JewellerAtNight, roused: true),
            // orig: R050+0x19D8 (1982:0538) + R050+0x1982 (1982:04E2) R050 seg 1982 matched
            Visit(c, () => c.IsDaylight ? Artificer : ArtificerAtNight, roused: true),
            // orig: R050+0x1AAC (1982:060C) + R050+0x1A56 (1982:05B6) R050 seg 1982 matched
            Visit(c, () => c.IsDaylight ? Clothmaker : ClothmakerAtNight, roused: true),
            // orig: R050+0x1B3C (1982:069C) + R050+0x1B2A (1982:068A) R050 seg 1982 matched
            // DORMANT: opt_flag_5 is 0 (it would lead to card 0x62, "no go").
            new MenuLine(() => { c.PassHours(0); c.Next = NoGo; }, OptionHint.Saying(SuccessAssured)),
            // orig: R050+0x1B86 (1982:06E6) + R050+0x1B74 (1982:06D4) R050 seg 1982 matched
            // DORMANT: opt_flag_6 is 0 (card 0x62 as well).
            new MenuLine(() => { c.PassHours(0); c.Next = NoGo; }, OptionHint.Saying(SuccessAssured)),
            // orig: R050+0x1BD0 (1982:0730) + R050+0x1BBE (1982:071E) R050 seg 1982 matched
            new MenuLine(() => { c.PassHours(1); c.Next = MilitaryCrafts; }, OptionHint.Saying(SuccessAssured)),
            // orig: R050+0x1C26 (1982:0786) + R050+0x1C08 (1982:0768) R050 seg 1982 matched
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight; }, OptionHint.Saying(SuccessAssured)),
            // orig: R050+0x1C7C (1982:07DC) + R050+0x1C5E (1982:07BE) R050 seg 1982 matched
            new MenuLine(() => { c.PassHours(0); c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>By day certain; at night the party's average Stealth plus its best Streetwise, less a local danger (1..99).</summary>
    private static OptionHint Safety(CardContext c)
    {
        if (c.IsDaylight)
        {
            return OptionHint.Say(SuccessAssured);
        }
        // C: thunk 09C0:20E9(0x0F, 1, 5): LocalPrice's middle argument is unused; its draw is made at every hint and choice.
        var chance = c.PartyAverageSkill(0x0F) + (c.PartyMaxSkill(0x10) - c.LocalPrice(0x0F, 5));
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), OptionHint.WholeParty);
    }

    /// <summary>
    /// Goes to a shop; a roll above the chance meets the night encounter (never when the chance
    /// is "assured", −1). The later shops are roused at night: an hour first.
    /// </summary>
    // C: the first two actions test "chance + 1 != 0" (p_6++ then p_6), the others "chance != -1": the same condition.
    private static MenuLine Visit(CardContext c, Func<int> shop, bool roused) =>
        new(chance =>
            {
                if (roused && !c.IsDaylight)
                {
                    c.PassHours(1);
                }
                if (c.Random(100) > chance && chance != -1)
                {
                    c.State.SavedCard = CardNumber;
                    c.Next = NightEncounter;
                    return;
                }
                c.PassHours(1);
                c.Next = shop();
            },
            () => Safety(c));
}
