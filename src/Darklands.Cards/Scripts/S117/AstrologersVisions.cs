namespace Darklands.Cards.Scripts;

/// <summary>
/// "$wqSta01": the astrologer's visions (from card 292), by how far the quest against the
/// witches and the Templars has come (quest 1 and the facts 1/1, 1/3) and the party's lore:
/// the next step of the road, and sometimes a potion for the leader. Then back on the map.
/// </summary>
// orig: R117+0x066C (189E:000C) matched; tests: CardSmokeTests
// DORMANT: the file's 22 option and pop-up handlers are never called (no menu); text 4 is never shown.
public sealed class AstrologersVisions : Card
{
    private const int TransformationPotion = 0x6E;

    public override int Number => 293;

    public override int Run(CardContext c)
    {
        Show(c, 142);
        c.Variables.Chosen[0] = c.Leader;
        c.Next = CardContext.MapCard;
        // C: pass-hours(card-option-flag-9 = 1): the flag assignment is the argument, so one hour passes.
        c.PassHours(1);
        // C: event-datum-at(0x5C, here) is the value of the visit note card 292 creates (100: no bit 1 or 2 set).
        var seen = c.EventValueHere(0x5C);
        if (c.Events.QuestExists(1) && c.PartyLoreLevel() >= 3)
        {
            var monastery = c.Events.FactAnywhere(1, 3);
            var sabbat = c.Events.FactAnywhere(1, 1);
            if (monastery && sabbat)
            {
                if ((seen & 3) != 0)
                {
                    c.Screen.Text(9);
                }
                else
                {
                    c.Screen.Text(3);
                    if (c.PartyLoreLevel() < 8)
                    {
                        c.AddItem(c.Leader, TransformationPotion);
                        c.Screen.Text(7);
                    }
                }
            }
            else if (monastery)
            {
                if ((seen & 1) != 0)
                {
                    c.Screen.Text(9);
                }
                else
                {
                    c.Screen.Text(2);
                    c.Screen.Text(5);
                }
            }
            else if (sabbat)
            {
                if ((seen & 2) != 0)
                {
                    c.Screen.Text(8);
                }
                else
                {
                    c.Screen.Text(10);
                    if (c.PartyLoreLevel() < 7)
                    {
                        c.AddItem(c.Leader, TransformationPotion);
                        c.Screen.Text(6);
                    }
                    else
                    {
                        c.Screen.Text(5);
                    }
                }
            }
            else if (c.EventAtHere(0x5C))
            {
                c.Screen.Text(8);
            }
            else
            {
                c.Screen.Text(1);
                if (c.PartyLoreLevel() < 6)
                {
                    c.AddItem(c.Leader, TransformationPotion);
                    c.Screen.Text(6);
                }
                else
                {
                    c.Screen.Text(5);
                }
            }
        }
        else
        {
            c.Screen.Text(0);
        }
        // The original returns 0x0C directly (following-card is also 0x0C).
        return CardContext.MapCard;
    }
}
