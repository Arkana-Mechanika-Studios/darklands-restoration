namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge04": an imprisoned companion (kind 0x4D event here) is released and rejoins the
/// party, which is back in the city's street.
/// </summary>
// orig: R116+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: the file's 30 other functions (a registration and option handlers) are never called: the card has no menu.
public sealed class CompanionReleased : Card
{
    public override int Number => 288;

    public override int Run(CardContext c)
    {
        // The name is copied from a data block (DS:66A6, "$Dunge04"). The companion joins before the card is drawn.
        var note = c.Events.Find(0x4D, -1, c.Here, -1, -1, -1);
        // C: var_chosen1 = party_add(*events[v]): the event's first word (its type) is the character.
        // DIVERGENCE: without such an event the original reads and frees events[-1]; here nobody joins.
        c.Variables.Chosen[0] = c.Events[note] is { } e ? c.CharacterJoins(e.Type) : -1;
        if (note != -1)
        {
            c.Events.Free(note);
        }
        Show(c, 310);
        c.Screen.Text(0);
        // The original returns the card without setting next_card.
        return c.IsDaylight ? 0x12 : 0x19;
    }
}
