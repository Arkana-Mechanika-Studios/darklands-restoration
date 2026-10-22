using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetA01": the pavilion of a wicked traveling alchemist whose guards demand all the
/// party's alchemical goods for a sham price. Hand them over (or explain there are none),
/// have the leader plead, or fight.
/// </summary>
// orig: R085+0x13B2 (1973:0002) matched; tests: CardSmokeTests
// orig: R085+0x1800 (1973:0450) matched; R085+0x1838 (1973:0488) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
// DORMANT: text block 6 (the alchemist breaks camp) is never shown by this card.
public sealed class AlchemistsRobberPavilion : Card
{
    public const int CardNumber = 0xA7;

    private const int Picture = 73;

    /// <summary>Item flags of the alchemical components and potions.</summary>
    private const uint AlchemyFlags = 0xC00;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // Line 0 hands over the goods, line 1 says there are none.
        var menu = Menu(c);
        menu.Enable(0, 2, 3);
        if (c.CountItemsWithFlags(Who.Everybody, AlchemyFlags) == 0)
        {
            menu[0] = OptionState.Hidden;
            menu[1] = OptionState.Enabled;
        }
        c.Variables.Chosen[0] = c.Leader;
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            // DORMANT: the start-up never leaves this card.
            return c.Next;
        }
        // C: $ChosenOneName is the leader again after every choice, before the handler runs.
        return menu.Run(
            // orig: R085+0x162C (1973:027C) matched; R085+0x167C (1973:02CC) matched; tests: CardSmokeTests
            new MenuLine(() => { c.Variables.Chosen[0] = c.Leader; HandOverTheGoods(c); }, OptionHint.Saying(SuccessAssured)),
            // orig: R085+0x16B4 (1973:0304) matched; R085+0x16C2 (1973:0312) matched; tests: CardSmokeTests
            new MenuLine(() => { c.Variables.Chosen[0] = c.Leader; LeaveTheScene(c); }, OptionHint.Saying(SuccessAssured)),
            // orig: R085+0x16FA (1973:034A) matched; R085+0x1782 (1973:03D2) matched; tests: CardSmokeTests
            new MenuLine(chance => { c.Variables.Chosen[0] = c.Leader; PleadForMercy(c, chance); }, () => PleaChance(c)),
            // orig: R085+0x17C2 (1973:0412) matched; R085+0x17C8 (1973:0418) matched; tests: CardSmokeTests
            new MenuLine(() => { c.Variables.Chosen[0] = c.Leader; FightTheAlchemist(c); }, OptionHint.Saying(CardTexts.EnterCombat)));
    }

    private static void LeaveTheScene(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    private static void HandOverTheGoods(CardContext c)
    {
        c.RemoveItemsWithFlags(Who.Everybody, AlchemyFlags);
        ShowPicture(c, 106);
        // UNCERTAIN: the text pays out $Money1, which this card never sets (a stale value from an earlier card).
        c.Screen.Text(1);
        LeaveTheScene(c);
    }

    private static OptionHint PleaChance(CardContext c)
    {
        var skill = c.Skill(c.Leader, 0x0A);
        return OptionHint.ChanceOf((skill + c.Attribute(c.Leader, 5)) / 2, c.Leader);
    }

    private static void PleadForMercy(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 71);
            c.Screen.Text(2);
            LeaveTheScene(c);
            return;
        }
        ShowPicture(c, 72);
        c.Screen.Text(3);
        FightTheAlchemist(c);
    }

    // orig: R085+0x1524 (1973:0174) matched; tests: CardSmokeTests
    private static void FightTheAlchemist(CardContext c)
    {
        // C: arguments are evaluated right to left: the guards' count (rand_mod(5)) is drawn before the first group's.
        var second = c.Random(5) + 3;
        var first = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
        var third = c.PartyLoreLevel() / 5 != 0 ? 1 : 2;
        switch (c.RunBattle(0x2F, -1, 3, first, second, 0x18, third, 1, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, 103);
                c.Screen.Text(4);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                break;
            case 3:
            case 4:
                c.RejoinAway();
                ShowPicture(c, 104);
                c.Screen.Text(5, TextFrame.BlankPage);
                c.RemoveItemsWithFlags(Who.Everybody, AlchemyFlags);
                break;
            default:
                // DORMANT: other battle results leave the party at the pavilion.
                return;
        }
        LeaveTheScene(c);
    }
}
