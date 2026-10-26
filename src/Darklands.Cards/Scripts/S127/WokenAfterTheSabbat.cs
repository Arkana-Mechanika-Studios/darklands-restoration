using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd38": the witches overwhelmed the party. In a dark dream they are carried off and
/// robbed; four days later they wake in the empty wilderness. The party loses most of what it
/// carried and, unless the innocents were saved (fact 9), Virtue and Divine Favour. The site is
/// abandoned from now on. The card has no menu: it goes straight back to the map.
/// </summary>
// orig: R127+0x19A2 (19D2:0002) matched; tests: CardSmokeTests
//   item pop-up: R127+0x1EB0, R127+0x1EEC, R127+0x1F28,
//     R127+0x1F64
//   saint pop-up: R127+0x1FA0, R127+0x2028, R127+0x20B0,
//     R127+0x2138
//   other: R127+0x1A52, R127+0x1A70, R127+0x1AAC,
//     R127+0x1ACA, R127+0x1B06, R127+0x1B24,
//     R127+0x1B60, R127+0x1B7E, R127+0x1BBA,
//     R127+0x1BD8, R127+0x1C14, R127+0x1C32,
//     R127+0x1C6E, R127+0x1C8C, R127+0x1CC8,
//     R127+0x1CE6, R127+0x1D22, R127+0x1D40,
//     R127+0x1D7C, R127+0x1D9A, R127+0x1DD6,
//     R127+0x1E78
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class WokenAfterTheSabbat : Card
{
    public override int Number => 334;

    private const int Picture = 143;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Screen.Text(0);
        // C: cur_day += 4 writes the day of the month directly (it may pass the month's end); the hour that passes
        //   next runs the calendar carry.
        // C: advance_hours (R029+0x54D8) takes an overlong day off the month's length in a loop whether or not the hour crossed midnight
        //   (R029+0x5526..0x5572, the same loop as Calendar.Finish), so the card's day + 4 is carried as here.
        c.State.Date = c.State.Date with { Day = (short)(c.State.Date.Day + 4) };
        c.PassHours(1);
        c.LoseItems(Who.Everybody);
        if (!c.Events.FactAnywhere(1, 9))
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        }
        SabbatAftermath.MarkTheSiteAbandoned(c);
        // The original's option handlers (text 0, then card 0x62) are never offered: see DormantOptions.
        return SabbatAftermath.WorldMap;
    }

    /// <summary>The option table the card file also holds (ten lines: the first text again, then 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R127+0x1A52 R127+0x1A70 R127+0x1AAC R127+0x1ACA R127+0x1B06
    //   R127+0x1B24 R127+0x1B60 R127+0x1B7E R127+0x1BBA R127+0x1BD8
    //   R127+0x1C14 R127+0x1C32 R127+0x1C6E R127+0x1C8C R127+0x1CC8
    //   R127+0x1CE6 R127+0x1D22 R127+0x1D40 R127+0x1D7C R127+0x1D9A
    //   R127+0x1DD6 R127+0x1E78 R127+0x1EB0 R127+0x1EEC R127+0x1F28
    //   R127+0x1F64 R127+0x1FA0 R127+0x2028 R127+0x20B0 R127+0x2138
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c, CardTexts.SuccessAssured);
}
