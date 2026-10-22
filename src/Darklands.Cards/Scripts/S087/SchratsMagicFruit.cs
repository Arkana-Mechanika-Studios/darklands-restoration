using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$WildH03": a schrat (a woodland spirit of the Wild Hunt) rewards the party with a
/// glowing fruit. One member may eat it and gain a permanent point of strength, or the party
/// refuses it. Each visit notes the fact in the world (fact 4 of quest 7).
/// </summary>
// orig: R087+0x14EE (1986:000E) matched; tests: CardSmokeTests
public sealed class SchratsMagicFruit : Card
{
    public const int CardNumber = 0xAF;

    private const int Picture = 125;

    /// <summary>The attribute raised (strength).</summary>
    private const int Strength = 1;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x25);
        Show(c, Picture);
        c.Next = CardNumber;
        c.Variables.Chosen[0] = 0;
        // One line for each of the first four slots, for those that are occupied; the fifth line refuses.
        // UNCERTAIN: the original's bare 'party' (first test) is read as the status byte of slot 0.
        var menu = Menu(c);
        menu.Enable(4);
        menu[0] = c.Status(0) == CharacterStatus.Empty ? OptionState.Hidden : OptionState.Enabled;
        menu[1] = c.Status(1) == CharacterStatus.Empty ? OptionState.Hidden : OptionState.Enabled;
        menu[2] = c.Status(2) == CharacterStatus.Empty ? OptionState.Hidden : OptionState.Enabled;
        menu[3] = c.Status(3) == CharacterStatus.Empty ? OptionState.Hidden : OptionState.Enabled;
        c.Variables.Chosen[1] = 1;
        c.Variables.Chosen[2] = 2;
        c.Variables.Chosen[3] = 3;
        c.Events.Create(-2, c.Here, 0, 0x1C, 4, 0, 0, EventLog.Forever, 0, 7);
        // orig: R087+0x18E0 (1986:0400) matched; R087+0x1918 (1986:0438) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints();

        // C: the start-up cannot leave this card, so the menu loop test is always true.
        return menu.Run(
            // orig: R087+0x16C6 (1986:01E6) matched; R087+0x1700 (1986:0220) matched; tests: CardSmokeTests
            new MenuLine(() => EatTheFruit(c, 0), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x1738 (1986:0258) matched; R087+0x1772 (1986:0292) matched; tests: CardSmokeTests
            new MenuLine(() => EatTheFruit(c, 1), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x17AA (1986:02CA) matched; R087+0x17E4 (1986:0304) matched; tests: CardSmokeTests
            new MenuLine(() => EatTheFruit(c, 2), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x181C (1986:033C) matched; R087+0x1856 (1986:0376) matched; tests: CardSmokeTests
            new MenuLine(() => EatTheFruit(c, 3), OptionHint.Saying(SuccessAssured)),
            // orig: R087+0x188E (1986:03AE) matched; R087+0x18A8 (1986:03C8) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.Screen.Text(2);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
            }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The member eats the fruit: strength and its maximum grow by 4, for good.</summary>
    // BUG: the strength is given before the last text; a game saved at that text holds only the card (DS:A772 = 0xAF), so
    // loading it runs this card again from its menu with the strength kept: the fruit can be eaten again and again
    // (lockstep oracle: save at the text, load, eat: +4 then +8).
    private static void EatTheFruit(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        c.AddAttributeMaximum(member, Strength, 4);
        c.AddAttribute(member, Strength, 4);
        c.Screen.Text(1);
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }
}
