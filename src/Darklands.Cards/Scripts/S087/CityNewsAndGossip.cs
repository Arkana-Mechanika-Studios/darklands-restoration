namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Situa01": an hour of listening to the talk of the town. There is no menu: the card picks
/// the text that fits the city's situation (war or rebellion, a lord who was overthrown or who
/// survived a plot, a city under a quest, or else idle news by the city's number) and returns
/// to the remembered card.
/// </summary>
// orig: R087+0x13CE (1974:000E) matched; tests: CardSmokeTests
public sealed class CityNewsAndGossip : Card
{
    public const int CardNumber = 0xAE;

    private const int Picture = 0;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // C: the city flags are the low byte of the query, cast to a signed char each time it is read.
        if ((c.HereQuery(0x20) & 1) != 0)
        {
            c.Screen.Text(1, TextFrame.BlankPage);
        }
        else if ((c.HereQuery(0x20) & 2) != 0)
        {
            c.Screen.Text(2, TextFrame.BlankPage);
        }

        if ((c.HereQuery(0x20) & 0x80) != 0)
        {
            c.Screen.Text(9, TextFrame.BlankPage);
        }
        else if (c.EventAtHere(0x42))
        {
            c.Screen.Text(8, TextFrame.BlankPage);
        }
        else if (c.Events.QuestAt(2, c.Here))
        {
            c.Screen.Text(3, TextFrame.BlankPage);
        }
        else
        {
            // C: a signed remainder, so it is below 20 (and negative when the query is).
            var news = c.HereQuery(0x21) % 0x14;
            if (news == 0x14)
            {
                // DORMANT: the remainder of a division by 20 is never 20.
                c.Screen.Text(4, TextFrame.BlankPage);
            }
            else if (news > 0x0E)
            {
                c.Screen.Text(5, TextFrame.BlankPage);
            }
            else if (news > 9)
            {
                c.Screen.Text(6, TextFrame.BlankPage);
            }
            else if (news > 4)
            {
                c.Screen.Text(7, TextFrame.BlankPage);
            }
            else
            {
                c.Screen.Text(0, TextFrame.BlankPage);
            }
        }
        c.PassHours(1);
        c.Next = c.State.SavedCard2;
        return c.Next;
    }
}
