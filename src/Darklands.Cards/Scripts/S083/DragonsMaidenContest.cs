using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago07": a village asks the party to choose the fairest of its tearful girls, the
/// "queen" it gives to the dragon. Choose a local girl, choose a woman of the party (and fight
/// to save her), try to leave, or pray for help.
/// </summary>
// orig: R083+0x0C34 (18FB:0004) matched; tests: CardSmokeTests
// orig: R083+0x12CA (18FB:069A) matched; R083+0x1322 (18FB:06F2) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class DragonsMaidenContest : Card
{
    public const int CardNumber = 0x9D;

    private const int Picture = 237;
    private const int StakePicture = 53;
    private const int DragonQuest = 4;

    /// <summary>The sacrifice at the stake.</summary>
    private const int TheSacrifice = 0xA2;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        // C: the growth is drawn as an argument of event_update.
        var growth = c.Random(2);
        c.Events.Update(-1, -1, 0x1C, 1, growth, DragonQuest, EventUpdate.Add);
        if (c.Events.FactAnywhere(DragonQuest, 5))
        {
            // The contest was held before.
            c.Next = 0xA1;
        }
        else
        {
            c.Events.Create(-2, c.Here, 0, 0x1C, 5, 0, 0, 26280, 0, DragonQuest);
        }
        // C: the reference's party_count_able counts able women; one alone is not offered either.
        if (c.AbleWomen() <= 1)
        {
            menu[1] = OptionState.Hidden;
        }
        else
        {
            c.PartyMaxAttributeOfWomen(5);
            c.Variables.Chosen[0] = (byte)c.BestMember;
        }
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 0);
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.SetCardItems();
        c.SetCardSaints(0x25, 0x42);
        menu[3] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            // orig: R083+0x0E30 (18FB:0200) matched; R083+0x0E76 (18FB:0246) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                ShowPicture(c, StakePicture);
                c.Screen.Text(1);
                c.PassHours(3);
                c.Next = TheSacrifice;
            }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => ChooseOurOwn(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R083+0x1136 (18FB:0506) matched; R083+0x11BA (18FB:058A) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.LoseItems(Who.Everybody);
                ShowPicture(c, 177);
                c.Screen.Text(8);
                ShowPicture(c, Picture);
                c.Screen.Text(9);
                c.PassHours(2);
                c.Next = 0x0C;
            }, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R083+0x11F2 (line 3); R083+0x135A (18FB:072A) matched; R083+0x13E4 (18FB:07B4) matched; tests: CardSmokeTests
            new MenuLine(() => PrayForHelp(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint));
    }

    /// <summary>The party's fairest is crowned, then seized for the dragon: the party fights for her.</summary>
    // orig: R083+0x0EAE (18FB:027E) matched; R083+0x10FE (18FB:04CE) matched; tests: CardSmokeTests
    private static void ChooseOurOwn(CardContext c)
    {
        var queen = c.Variables.Chosen[0];
        ShowPicture(c, StakePicture);
        c.Screen.Text(2);
        c.SetAttribute(queen, 0, 1);
        ShowPicture(c, Picture);
        c.Screen.Text(3);
        // C: arguments are evaluated right to left: rand_mod(4) (second group) before rand_mod(5).
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        switch (c.RunBattle(0x63, -1, 0x0B, 2, first, 4, 2, second, -1, 0, 0))
        {
            case 0:
                c.PartyMaxAttribute(4);
                c.Variables.Chosen[0] = (byte)c.BestMember;
                c.PassHours(1);
                ShowPicture(c, 21);
                c.Screen.Text(4);
                c.Events.Update(-1, -1, 0x1C, 6, 1, DragonQuest, EventUpdate.Add);
                c.Next = 0x0C;
                break;
            case 1:
            case 2:
                // C: card_load (not card_load_near) here: the same texts are loaded again.
                ShowPicture(c, 224);
                c.Screen.Text(5);
                ShowPicture(c, 121);
                c.Screen.Text(6);
                c.DeleteMember(c.Variables.Chosen[0]);
                c.OneOfTheAwayLeaves();
                c.PassHours(2);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                ShowPicture(c, 224);
                c.Screen.Text(7);
                ShowPicture(c, 177);
                c.Screen.Text(6);
                c.PassHours(2);
                c.DeleteMember(c.Variables.Chosen[0]);
                c.PassHours(0);
                c.LoseItems(Who.Everybody);
                c.Next = 0x0C;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party in the village.
                break;
        }
    }

    // orig: R083+0x11F2 (18FB:05C2) matched; R083+0x1292 (18FB:0662) matched; tests: CardSmokeTests
    private static void PrayForHelp(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 0x25, 2);
                break;
            case 1:
                c.NameToSlot(1, 0x42, 2);
                break;
            default:
                // DORMANT: slots 2, 3 hold no saint.
                break;
        }
        if (answer != 0)
        {
            ShowPicture(c, StakePicture);
            c.Screen.Text(0x0A);
            c.Next = TheSacrifice;
            return;
        }
        c.Screen.Text(0x0B);
        c.PassHours(0);
        c.Next = CardNumber;
    }
}
