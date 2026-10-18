using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol17": the guard has arrested the rebel leaders. If the party works for the city
/// (the rebels' location fact 0 has value 1), a messenger-girl brings word (block 0, or 1 when
/// coming from the main street): the party may go to help (0x54) or betray its employer.
/// Otherwise the party only watches the march go by.
/// </summary>
// orig: R091+0x05C4 (1894:0004) matched; tests: CardSmokeTests
public sealed class RebelLeadersArrested : Card
{
    private const int Picture = 0;
    private const int RebelsTag = 2;

    public override int Number => 0xBD;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // The rebels are up for a month.
        c.EventHere(0x3E, 0x2D0);
        c.RandomName(c.City.Ordinal + 0x16, 2, 0);
        c.RandomName(c.City.Ordinal + 0x23, 1, 1);
        // C: the block local is set only on this branch; the other skips the menu.
        var menu = Menu(c);
        menu.Enable(0, 1);
        if (c.Events.FactValue(RebelsTag, 0, c.Here) == 1)
        {
            var before = c.State.PreviousCard;
            menu.Block = before is 6 or 8 ? 1 : 0;
        }
        else
        {
            if (c.Events.Fact(RebelsTag, 0x24, c.Here))
            {
                c.Screen.Text(4, TextFrame.BlankPage);
            }
            else if (c.Events.Fact(RebelsTag, 0x25, c.Here))
            {
                c.Screen.Text(3, TextFrame.BlankPage);
            }
            else
            {
                c.Screen.Text(5, TextFrame.BlankPage);
            }
            c.Next = c.IsDaylight ? 9 : 10;
        }

        // orig: R091+0x0838 (1894:0278) matched (resets the handlers only)
        return menu.Run(
            // orig: R091+0x0776 (1894:01B6); hint R091+0x077E matched
            new MenuLine(() => c.Next = 0x54, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R091+0x07B6 (1894:01F6); hint R091+0x0810 matched
            new MenuLine(() => Betray(c), OptionHint.Saying("Easy")));
    }

    private static void Betray(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, 0x24, 0, 0, 0x5A0, 0, RebelsTag);
        c.AdjustReputation(c.Here, -15, -40);
        c.Screen.Text(2, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
