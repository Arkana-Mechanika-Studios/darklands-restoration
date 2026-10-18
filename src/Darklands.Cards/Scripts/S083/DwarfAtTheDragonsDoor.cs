namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago09": a red-hot boulder blocks the dragon's lair, and a dwarf explains that a dragon
/// grows less hungry when it is denied maidens. Once three maidens are saved (the dragon quest's
/// fact 4/6), he opens the lair; otherwise the party goes back to the land.
/// </summary>
// orig: R083+0x1F82 (1A30:0002) matched; tests: CardSmokeTests
// orig: R083+0x2034 (1A30:00B4) matched; R083+0x206C (1A30:00EC) matched; tests: CardSmokeTests
// DORMANT: the registration routine and its "screwed up." default are never called (the card has no menu).
public sealed class DwarfAtTheDragonsDoor : Card
{
    public const int CardNumber = 0x9F;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 237);
        c.Next = CardNumber;
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.Screen.Text(0);
        if (c.Events.FactValueAnywhere(4, 6) >= 3)
        {
            c.Screen.Text(2);
            c.Next = 0x99;
        }
        else
        {
            c.Screen.Text(1);
            c.State.CurrentLocation = -1;
            c.Next = 0x0C;
        }
        return c.Next;
    }
}
