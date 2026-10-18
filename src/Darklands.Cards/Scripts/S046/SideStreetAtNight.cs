namespace Darklands.Cards.Scripts;

/// <summary>
/// "$SideS01": a side street at night. The party slips through the dark lanes to the main
/// street, the fortress, the square, the market, the church, the business district, the docks,
/// a gate, the other quarters or the city's places; trouble may meet it on the way (the night
/// encounter, which remembers where the party was going). A party poor in Stealth and
/// Streetwise is waylaid by thieves when it leaves.
/// </summary>
// orig: R046+0x1132 (194B:0002) matched; tests: CardSmokeTests
// DORMANT: R046+0x1A12 (194B:08E2): an empty function nothing calls.
// C: the option lines are copied from opt_flag_0..9 only; the option table has no init loop here,
//   so lines 10..19 keep whatever the card before left (stale globals): the text has 10 lines,
//   so they cannot be picked.
public sealed class SideStreetAtNight : Card
{
    public const int CardNumber = 0x0A;

    private const int Picture = 273;

    private const int MainStreetAtNight = 0x08, CitySquare = 0x12, Church = 0x13, Business = 0x14, Market = 0x15,
        MarketAtNight = 0x16, CitySquareAtNight = 0x19, Fortress = 0x1B, FortressAtNight = 0x1C, Docks = 0x1F,
        DocksAtNight = 0x20, OtherGate = 0x21, OtherGateAtNight = 0x22, Thieves = 0x24, Places = 0x3B,
        NightEncounter = 0x3C, OtherQuarters = 0x7C;

    private const int Politics = 2;

    /// <summary>The chance of trouble on the way, drawn on arrival (the original's module word).</summary>
    private int _danger;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        // C: sound_play(6) (root 047C:078A) directly, not through the music setting (music_play): the silence plays even with music off.
        c.PlaySound(6);
        Show(c, Picture);
        OptionState IfThere(int building) => c.HereQuery(building) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var square = IfThere(0x10);
        var fortress = IfThere(0x0F);
        var market = IfThere(0x11);
        var docks = IfThere(0x12);
        c.StandingSeen = c.Reputation(c.Here);
        // C: thunk 09C0:20E9(8, 1, 1): LocalPrice's middle argument is unused.
        _danger = c.LocalPrice(8, 1);
        if (c.Events.FactValue(Politics, 0, c.Here) == -1)
        {
            c.Screen.Text(2, TextFrame.BlankPage);
        }

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        menu[1] = fortress;
        menu[2] = square;
        menu[3] = market;
        menu[6] = docks;
        c.Next = Number;
        var next = menu.Run(
            // orig: R046+0x1460 (194B:0330) + R046+0x1432 (194B:0302) R046 seg 194B matched
            new MenuLine(() =>
                {
                    if (TroubleOnTheWay(c))
                    {
                        MeetTrouble(c, MainStreetAtNight);
                        return;
                    }
                    c.Next = MainStreetAtNight;
                },
                Safety),
            // orig: R046+0x1510 (194B:03E0) + R046+0x14AA (194B:037A) R046 seg 194B matched
            new MenuLine(() =>
                {
                    if (TroubleOnTheWay(c))
                    {
                        // C: here the hour passes before the time of day is read.
                        c.PassHours(1);
                        MeetTrouble(c, c.IsDaylight ? Fortress : FortressAtNight);
                        return;
                    }
                    Arrive(c, () => c.IsDaylight ? Fortress : FortressAtNight);
                },
                Safety),
            // orig: R046+0x15B8 (194B:0488) + R046+0x155A (194B:042A) R046 seg 194B matched
            Slip(c, () => c.IsDaylight ? CitySquare : CitySquareAtNight),
            // orig: R046+0x1660 (194B:0530) + R046+0x1602 (194B:04D2) R046 seg 194B matched
            Slip(c, () => c.IsDaylight ? Market : MarketAtNight),
            // orig: R046+0x16EA (194B:05BA) + R046+0x16AA (194B:057A) R046 seg 194B matched
            Slip(c, () => Church),
            // orig: R046+0x1774 (194B:0644) + R046+0x1734 (194B:0604) R046 seg 194B matched
            Slip(c, () => Business),
            // orig: R046+0x181C (194B:06EC) + R046+0x17BE (194B:068E) R046 seg 194B matched
            Slip(c, () => c.IsDaylight ? Docks : DocksAtNight),
            // orig: R046+0x18B4 (194B:0784) + R046+0x1866 (194B:0736) R046 seg 194B matched
            new MenuLine(() =>
                {
                    var trouble = TroubleOnTheWay(c);
                    c.PassHours(1);
                    if (trouble)
                    {
                        // The night encounter always leads on to the night gate here, even by day.
                        MeetTrouble(c, OtherGateAtNight);
                        return;
                    }
                    c.Next = c.IsDaylight ? OtherGate : OtherGateAtNight;
                },
                Safety),
            // orig: R046+0x193E (194B:080E) + R046+0x18FE (194B:07CE) R046 seg 194B matched
            Slip(c, () => OtherQuarters),
            // orig: R046+0x19C8 (194B:0898) + R046+0x1988 (194B:0858) R046 seg 194B matched
            Slip(c, () => Places));
        return ThievesMayWaylay(c, next);
    }

    /// <summary>Safe when nothing threatens (drawn as 0); else the chance to get through.</summary>
    private OptionHint Safety() =>
        _danger == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(100 - _danger, OptionHint.WholeParty);

    /// <summary>The roll against the option's chance (the hint handler runs first, as in the original).</summary>
    // C: rand_mod(100) is drawn even when nothing threatens: the && tests the roll first.
    // (the line's chance is asked here, not rated by the menu: the draw comes first and the lines have no pop-up.)
    private bool TroubleOnTheWay(CardContext c) => c.Random(100) >= Safety().Chance && _danger != 0;

    private static void MeetTrouble(CardContext c, int place)
    {
        c.State.SavedCard = place;
        c.Next = NightEncounter;
    }

    /// <summary>Arrives quietly: the walk text, an hour, then the place for the time it is now.</summary>
    private static void Arrive(CardContext c, Func<int> place)
    {
        c.Screen.Text(1, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = place();
    }

    private MenuLine Slip(CardContext c, Func<int> place) =>
        new(() =>
            {
                if (TroubleOnTheWay(c))
                {
                    MeetTrouble(c, place());
                    return;
                }
                Arrive(c, place);
            },
            Safety);

    /// <summary>
    /// On leaving (orig: the return expression of R046+0x1132): unless a roll falls
    /// under the party's average Stealth and Streetwise, or the night encounter comes anyway,
    /// thieves waylay the party, which then goes on to the place it chose.
    /// </summary>
    private static int ThievesMayWaylay(CardContext c, int next)
    {
        // C: the operands of '>' are unsequenced; only rand_mod draws, so the order does not matter.
        var stealth = Math.Clamp((c.PartyAverageSkill(0x0F) + c.PartyAverageSkill(0x10)) / 2, 0, 99);
        if (!(stealth > c.Random(100)) && next != NightEncounter)
        {
            c.State.SavedCard = next;
            next = Thieves;
        }
        c.Next = next;
        return next;
    }
}
