using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$SlumD00": the slum of a city, full of paupers, drifters and thieves. The party rests an
/// hour, wanders about listening to rumours (the rumour mill, card 0x66), finds a cheap room to
/// live in until morning (where trouble may find it), or leaves via a side street.
/// </summary>
// orig: R052+0x16A8 (19A2:0008) matched; tests: CardSmokeTests
public sealed class SlumDistrict_SlumD00 : Card
{
    public const int CardNumber = 0x23;

    private const int Picture = 273;

    private const int CityOutskirts = 0x04, SideStreet = 0x09, SideStreetAtNight = 0x0A, NightEncounter = 0x24, Rumours = 0x66,
        Ambush = 0xB5;

    /// <summary>Events at a city: a lodging disturbance (0x52) and the brew mishap (0x51) that sends the party to card 0xB5.</summary>
    private const int Disturbance = 0x52, Mishap = 0x51;

    private const int Dawn = 5, Stealth = 0x0F, Streetwise = 0x10;

    public override int Number => CardNumber;

    // orig: R052+0x16A8 (19A2:0008) matched (entry: lodging price, option states, menu loop)
    // orig: R052+0x1DB8 (19A2:0718) matched (init helper: enables all 20 option lines, installs the "screwed up." default and the pop-up handlers, clears the item and saint slots)
    // DORMANT: R052+0x1E5A (19A2:07BA): the "screwed up." default hint for opt_fn[0..19]; lines 0..9 have their own hints and lines 10..19 have no text line.
    // DORMANT: R052+0x1E92 (19A2:07F2), R052+0x1ECE (19A2:082E), R052+0x1F0A (19A2:086A), R052+0x1F46 (19A2:08A6): item pop-up handlers (success assured for the chosen member); card_items stay empty, so no item pop-up exists.
    // DORMANT: R052+0x1F82 (19A2:08E2), R052+0x200C (19A2:096C), R052+0x2096 (19A2:09F6), R052+0x2120 (19A2:0A80): saint pop-up handlers (chance text of the chosen saint); card_saints stay empty, so no saint pop-up exists.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Variables.Money[0] = Money.From(0, 0, c.HereQuery(2) <= 5 ? 1 : 2);

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R052+0x18E4 (19A2:0244) matched (option 0: rest for an hour); hint R052+0x1902 (19A2:0262) (success assured)
            new MenuLine(() =>
            {
                c.Screen.Text(5, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = CardNumber;
            }),
            // orig: R052+0x193A (19A2:029A) matched (option 1: listen to the rumours); hint R052+0x1960 (19A2:02C0) (success assured)
            new MenuLine(() =>
            {
                c.PassHours(c.HereQuery(2) / 2);
                c.State.SavedCard = CardNumber;
                c.Next = Rumours;
            }),
            // DORMANT: options 2..6 are switched off by the entry (the MSG lines are placeholders "2".."6"); each shows text 0 and goes to the outskirts (card 4).
            // option 2 R052+0x1998 (19A2:02F8) (hint R052+0x19B6 (19A2:0316)), option 3 R052+0x19EE (19A2:034E) (hint R052+0x1A0C (19A2:036C)), option 4 R052+0x1A44 (19A2:03A4) (hint R052+0x1A62 (19A2:03C2)),
            // option 5 R052+0x1A9A (19A2:03FA) (hint R052+0x1AB8 (19A2:0418)), option 6 R052+0x1AF0 (19A2:0450) (hint R052+0x1B0E (19A2:046E)); all hints: success assured.
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)),
            // DORMANT: option 7 is switched off by the entry. A night-long sneak through the slum; see SneakThroughTheSlum.
            // orig: R052+0x1B46 (19A2:04A6) matched (option 7 action); hint R052+0x1C62 (19A2:05C2) (see SneakChance)
            new MenuLine(chance => SneakThroughTheSlum(c, chance), () => SneakChance(c)),
            // orig: R052+0x1C7E (19A2:05DE) matched (option 8: live cheaply until morning, see LiveCheaply); hint R052+0x1D34 (19A2:0694) ("You can always encounter the unknown")
            new MenuLine(() => LiveCheaply(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R052+0x1D6C (19A2:06CC) matched (option 9: leave via a side street); hint R052+0x1D80 (19A2:06E0) (success assured)
            new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight));
    }

    // DORMANT: reached only by the hidden options 2..6.
    private static void LeaveForOutskirts(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityOutskirts;
    }

    private static int HoursUntilDawn(CardContext c)
    {
        var hours = Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, Dawn);
        return hours <= 11 ? hours : 8;
    }

    /// <summary>Half the sum of the best Stealth and Streetwise in the party.</summary>
    // orig: R052+0x1C62 (19A2:05C2) matched
    private static OptionHint SneakChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxSkillSum(Stealth, Streetwise) / 2, OptionHint.WholeParty);

    /// <summary>
    /// DORMANT. Sneaks about the slum until morning: a good roll passes the night safely; a bad one
    /// is worse by day (Streetwise counts half) and may end in a night encounter, or costs some standing.
    /// </summary>
    // orig: R052+0x1B46 (19A2:04A6) matched
    private static void SneakThroughTheSlum(CardContext c, int chance)
    {
        var divisor = 1;
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
            c.PassHours(HoursUntilDawn(c));
            c.PracticeSkill(Who.Everybody, Streetwise, 7, 10, 0);
        }
        else
        {
            if (c.IsDaylight)
            {
                divisor = 2;
            }
            if (-(c.PartyMaxSkill(Streetwise) / divisor - 100) >= c.Random(100))
            {
                c.PassHours(c.Random(3));
                c.State.SavedCard = CardNumber;
                c.PracticeSkill(Who.Everybody, Streetwise, 0, 10, -1);
                c.Next = NightEncounter;
                return;
            }
            c.AdjustReputation(c.Here, -1, -2);
            c.Screen.Text(6, TextFrame.BlankPage);
            c.PassHours(HoursUntilDawn(c));
            c.PracticeSkill(Who.Everybody, Streetwise, 0, 10, 0);
        }
        c.Next = CardNumber;
    }

    /// <summary>Lives in a cheap room, camping there; the night may bring a disturbance (an encounter) or a brew mishap.</summary>
    // orig: R052+0x1C7E (19A2:05DE) matched
    private static void LiveCheaply(CardContext c)
    {
        c.PassHours(c.HereQuery(2) / 3);
        c.Screen.Text(c.HereQuery(0x21) % 2 != 0 ? 2 : 3);
        c.PracticeSkill(Who.Everybody, Streetwise, 7, 10, 0);
        c.Camp(3);
        if (c.EventAtHere(Disturbance))
        {
            c.PassHours(c.Random(2) + 2);
            c.Screen.Text(7);
            c.State.SavedCard = CardNumber;
            c.Next = NightEncounter;
            return;
        }
        c.Next = c.EventAtHere(Mishap) ? Ambush : CardNumber;
    }
}
