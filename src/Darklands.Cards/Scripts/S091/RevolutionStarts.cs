using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol18": the revolution has started. If the party sides with the rebels (their location
/// fact 0 has value −1), a messenger-girl (block 0) or the rebel leader in the main street
/// (block 1) calls them to the march (0xB9); they may also hang back. Otherwise the party
/// only watches the march go by.
/// </summary>
// orig: R091+0x0892 (18C1:0002) matched; tests: CardSmokeTests
public sealed class RevolutionStarts : Card
{
    private const int Picture = 0;
    private const int RebelsTag = 2;

    public override int Number => 0xBE;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.EventHere(0x3E, 0x2D0);
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.RandomName(c.City.Ordinal + 0x23, 1, 1);
        var fromTheMainStreet = c.State.PreviousCard is 6 or 8;
        // C: text-slot-named[2] (R136+0xF402) gets a copy of name 0 or name 1 (far strcpy, 30-byte slots).
        c.Variables.Named[2] = fromTheMainStreet ? c.Variables.Named[0] : c.Variables.Named[1];
        var menu = Menu(c);
        menu.Enable(0, 1);
        if (c.Events.FactValue(RebelsTag, 0, c.Here) == -1)
        {
            menu.Block = fromTheMainStreet ? 1 : 0;
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

        // orig: R091+0x0B4E (18C1:02BE) matched (resets the handlers only)
        // C: a plain while: the menu is not shown when a text above already chose the next card.
        return menu.Run(
            // orig: R091+0x0A8C (18C1:01FC); hint R091+0x0A94 matched
            new MenuLine(() => c.Next = 0xB9, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R091+0x0ACC (18C1:023C); hint R091+0x0B26 matched
            new MenuLine(() => HangBack(c), OptionHint.Saying("Easy")));
    }

    private static void HangBack(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, 0x25, 0, 0, 0x5A0, 0, RebelsTag);
        c.AdjustReputation(c.Here, -12, -12);
        c.Screen.Text(2, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
