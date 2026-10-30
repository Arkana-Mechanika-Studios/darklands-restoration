using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago13": in the scorched lair a mad priest stands amid the dragon's hoard by his cross,
/// which keeps the dragon away. Attack him and his demons, apologise and leave, or offer to be
/// his acolytes (with enough Latin and religion he removes the cross, and the dragon returns).
/// </summary>
// orig: R084+0x1FD8 (1A35:0008) matched; tests: CardSmokeTests
// orig: R084+0x253E (1A35:056E) matched; R084+0x2576 (1A35:05A6) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
// C: the texts name $NamedThreeName, which this card never sets (an earlier card's name shows).
public sealed class MadPriestOfTheHoard : Card
{
    public const int CardNumber = 0xA3;

    private const int Picture = 306;
    private const int TheLair = 0x99;
    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        if (c.Events.FactAnywhere(DragonQuest, 9))
        {
            // The cross is gone: the dragon is back in its lair.
            c.Next = TheLair;
        }
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            // orig: R084+0x2126 (1A35:0156) matched; R084+0x2304 (1A35:0334) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                if (AttackHim(c))
                {
                    menu[0] = OptionState.Shown;
                }
            }, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R084+0x233C (1A35:036C) matched; R084+0x234A (1A35:037A) matched; tests: CardSmokeTests
            new MenuLine(() => { c.State.CurrentLocation = -1; c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)),
            // orig: R084+0x2382 (1A35:03B2) matched; R084+0x2510 (1A35:0540) matched; tests: CardSmokeTests
            new MenuLine(() => BecomeHisAcolytes(c), OptionHint.Saying("Latin and Religious Training")));
    }

    /// <summary>The cross is destroyed and the dragon returns to its lair (fact 4/9; the rival knight's count +4).</summary>
    private static void TheDragonReturns(CardContext c)
    {
        ShowPicture(c, 110);
        c.Screen.Text(6);
        c.Events.Create(-2, c.Here, 0, 0x1C, 9, 0, 0, 26280, 0, DragonQuest);
        c.Events.Update(-1, -1, 0x1C, 3, 4, DragonQuest, EventUpdate.Add);
        c.Next = TheLair;
    }

    /// <summary>Returns true when the party won (the attack line is then shown only).</summary>
    private static bool AttackHim(CardContext c)
    {
        ShowPicture(c, 307);
        c.Screen.Text(1);
        // C: arguments are evaluated right to left: roll-below(4) (second group) before roll-below(5).
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        var result = c.RunBattle(0x64, -1, 0x21, 2, first, 0, 2, second, -1, 0, 0);
        // C: a signed test: 0, 1 and any negative code count as a win.
        if (result < 2)
        {
            ShowPicture(c, 189);
            c.Screen.Text(7);
            TheDragonReturns(c);
            return true;
        }
        switch (result)
        {
            case 2:
                c.DropAway();
                ShowPicture(c, 287);
                c.Screen.Text(8);
                break;
            case 3:
                ShowPicture(c, 110);
                c.Screen.Text(9);
                break;
            case 4:
                c.SetAttribute(Who.Everybody, 6, 0);
                ShowPicture(c, 13);
                c.Screen.Text(0x0A);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): battle results above 4 leave the party before the priest.
                return false;
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
        return false;
    }

    private static void BecomeHisAcolytes(CardContext c)
    {
        var latin = c.PartyMaxSkill(0x0B);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        var religion = c.PartyMaxSkill(8);
        c.Variables.Chosen[1] = (byte)c.BestMember;
        ShowPicture(c, 331);
        c.Screen.Text(2);
        if (latin < 60)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(3);
            ShowPicture(c, Picture);
            c.Next = CardNumber;
        }
        else if (religion < 60)
        {
            ShowPicture(c, 307);
            c.Screen.Text(4);
            ShowPicture(c, Picture);
            c.Next = CardNumber;
        }
        else
        {
            ShowPicture(c, 332);
            c.Screen.Text(5);
            TheDragonReturns(c);
        }
    }
}
