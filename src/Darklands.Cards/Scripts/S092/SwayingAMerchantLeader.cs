using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Merch02": talking city politics with a leading merchant. The party may try to charm him
/// (the chance is always full), give him a weighty purse, a reliquary or evidence against him,
/// or take its leave. Nothing but the texts and the leader's practice comes of it.
/// </summary>
// orig: R092+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class SwayingAMerchantLeader : Card
{
    private const int Picture = 356;

    public override int Number => 0xC0;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // (The original takes its own copy of the reputation here, and raises it on lines 1 and 2; nothing reads it.)
        // orig: R092+0x0520 (1838:0520) matched (resets the handlers only)
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 6);
        return menu.Run(
            // orig: R092+0x0224 (1838:0224); hint R092+0x0298 (always 100 %) matched
            new MenuLine(() => Charm(c, 100), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R092+0x02B0; R092+0x030C; R092+0x0368 matched
            new MenuLine(() => { c.PassHours(1); c.Screen.Text(4, TextFrame.BlankPage); c.Next = Number; },
                () => OptionHint.Say(SuccessAssured, c.Leader)),
            new MenuLine(() => { c.PassHours(1); c.Screen.Text(5, TextFrame.BlankPage); c.Next = c.ReturnCard; },
                () => OptionHint.Say(SuccessAssured, c.Leader)),
            new MenuLine(() => { c.PassHours(1); c.Screen.Text(2); c.Next = c.ReturnCard; },
                () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R092+0x03C0; R092+0x03C4 (empty) matched
            // DORMANT: lines 4 and 5 are hidden and empty (their hints 03c2, 03c6 are empty functions too).
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            // orig: R092+0x03C8; hint R092+0x03DA matched
            new MenuLine(() => { c.PassHours(0); c.Next = c.ReturnCard; }),
            // orig: R092+0x0412; R092+0x046C; R092+0x04C6 matched
            // DORMANT: lines 7..9 are hidden; they would end the game (0x62).
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)));
    }

    // DORMANT: the chance is always 100, so the else branch (texts 2 and 3) is never reached.
    // C: practice_skill with mode 7 (not 0 or 1), as the original passes it.
    private static void Charm(CardContext c, int chance)
    {
        c.PassHours(1);
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.Screen.Text(1);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 7, -1);
        }
        else
        {
            c.Screen.Text(chance / 3 >= roll ? 3 : 2);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 7, -1);
        }
        c.Next = 0xC0;
    }

    /// <summary>Lines the card never offers: the game ends (0x62).</summary>
    private static void TheEnd(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
