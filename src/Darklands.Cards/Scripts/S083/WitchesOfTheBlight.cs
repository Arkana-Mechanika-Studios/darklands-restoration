using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago06": the dragon's worshippers in the blighted land (the dragon quest's fact 4/4
/// counts these meetings): a stone hailing the Dragon King, hags raising the dead, witches
/// sowing demons from the storm, and their sabbat on a hill. Run, pray, fight, grovel,
/// surrender, or parley with the demons in Latin.
/// </summary>
// orig: R083+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R083+0x0968 (1838:0968) matched; R083+0x09D4 (1838:09D4) matched; tests: CardSmokeTests
// DORMANT: lines 6..9 are enabled but have no lines in the texts, nor handlers (the "screwed up." default).
public sealed class WitchesOfTheBlight : Card
{
    public const int CardNumber = 0x9C;

    private const int Picture = 286;
    private const int SabbatPicture = 144;
    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.State.CurrentLocation = -1;
        // C: the picture buffer is the card's: these scenes change it for later redraws.
        var picture = Picture;
        // settings-block keeps an earlier card's value when the card leaves at once (case 0): the sabbat test below reads it.
        int? block = null;
        switch (c.Events.FactValueAnywhere(DragonQuest, 4))
        {
            case 0:
                c.Screen.Text(0);
                c.Next = 0x0C;
                break;
            case 1:
                ShowPicture(c, 182);
                c.Screen.Text(1);
                ShowPicture(c, picture);
                block = 0;
                Fight(c, 0);
                c.Next = 0x0C;
                break;
            case 2:
                ShowPicture(c, 237);
                c.Screen.Text(2);
                picture = 242;
                ShowPicture(c, picture);
                block = 3;
                break;
            default:
                picture = SabbatPicture;
                ShowPicture(c, picture);
                block = 4;
                break;
        }
        // C: the growth is drawn as an argument of event-advance.
        var growth = c.Random(2);
        c.Events.Update(-1, -1, 0x1C, 1, growth, DragonQuest, EventUpdate.Add);
        c.Events.Update(-1, -1, 0x1C, 4, 1, DragonQuest, EventUpdate.Add);
        var menu = Menu(c, block);
        menu.Enable(0, 2, 4, 5);
        // C: at the sabbat (block 4) line 3 (grovel) is left out, surrender stays (R083+0x01CD: DS:EE7C = 0); in case 0 the test
        // C: reads the block the card before left, and the flag (a global) stays for the next card.
        menu[3] = menu.Block == 4 ? OptionState.Hidden : OptionState.Enabled;
        c.SetCardItems();
        c.SetCardSaints(9, 0x17, 0x1D, 0x2B);
        menu[1] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            new MenuLine(chance => Run(c, menu.Block, chance), hint: () => RunningChance(c)),
            // orig: R083+0x05DC (line 1); R083+0x0A0C (1838:0A0C) matched; R083+0x0A96 (1838:0A96) matched; R083+0x0B20 (1838:0B20) matched;
            // R083+0x0BAA (1838:0BAA) matched; tests: CardSmokeTests
            new MenuLine(() => Pray(c, picture), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R083+0x06DA (1838:06DA) matched; R083+0x06E0 (1838:06E0) matched; tests: CardSmokeTests
            new MenuLine(() => Fight(c, menu.Block), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R083+0x0718 (1838:0718) matched; R083+0x0798 (1838:0798) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PassHours(2);
                ShowPicture(c, 177);
                c.Screen.Text(0x0D);
                c.AddSkill(Who.Everybody, 9, -(c.Random(3) + 3));
                c.LoseItems(Who.Everybody);
                c.SetAttribute(Who.Everybody, 6, 0);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R083+0x07D0 (1838:07D0) matched; R083+0x0824 (1838:0824) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                ShowPicture(c, SabbatPicture);
                c.Screen.Text(0x0E);
                c.AddSkill(Who.Everybody, 9, -c.Random(3));
                c.Next = 0x8F;
            }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => ParleyInLatin(c, chance, menu.Block), hint: () => LatinChance(c)));
    }

    // orig: R083+0x0328 (1838:0328) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int block)
    {
        int result;
        // C: arguments are evaluated right to left in each call.
        if (block == 3)
        {
            var second = c.Random(3) + 2;
            var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
            result = c.RunBattle(0x70, -1, 0x35, first, second, -1, 0, 0, -1, 0, 0);
        }
        else if (block == 4)
        {
            var third = c.Random(3) + 1;
            var second = c.Random(5) + 4;
            var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
            result = c.RunBattle(0x71, -1, 0x27, first, second, 0x29, third, 1, -1, 0, 0);
        }
        else
        {
            var third = c.Random(3) + 1;
            var second = c.Random(3) + 2;
            var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
            result = c.RunBattle(0x6F, -1, 0x27, first, second, 0x2D, third, 1, -1, 0, 0);
        }
        switch (result)
        {
            case 0:
                c.PassHours(0);
                if (block == 3)
                {
                    ShowPicture(c, 287);
                    c.Screen.Text(5);
                }
                else
                {
                    c.Screen.Text(0x11);
                }
                c.Next = 0x0C;
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, Picture);
                c.Screen.Text(6);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                ShowPicture(c, SabbatPicture);
                c.Screen.Text(7);
                c.WaitUntilHour(0);
                c.Next = 0x8F;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party where it was.
                break;
        }
    }

    // orig: R083+0x05BC (1838:05BC) matched; tests: CardSmokeTests
    private static OptionHint RunningChance(CardContext c)
    {
        var chance = c.PartyMinMountedAgility();
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R083+0x0518 (1838:0518) matched; tests: CardSmokeTests
    private static void Run(CardContext c, int block, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(2);
            ShowPicture(c, Picture);
            c.Screen.Text(8);
            if (c.PartyGet(5) != 0)
            {
                c.PracticeSkill(Who.Everybody, 0x11, 1, 10, 0);
            }
            // C: following-card is not changed: the menu comes back.
            return;
        }
        ShowPicture(c, SabbatPicture);
        c.Screen.Text(9);
        Fight(c, block);
    }

    // orig: R083+0x05DC (1838:05DC) matched; R083+0x06A2 (1838:06A2) matched; tests: CardSmokeTests
    private static void Pray(CardContext c, int picture)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 232);
            c.Screen.Text(c.CardSaints[c.MenuEntrySlot] == 0x2B ? 0x0A : 0x0B);
            c.State.CurrentLocation = -1;
            c.Next = 0x0C;
            return;
        }
        ShowPicture(c, SabbatPicture);
        c.Screen.Text(0x0C);
        ShowPicture(c, picture);
        c.Next = CardNumber;
    }

    // orig: R083+0x092A (1838:092A) matched; tests: CardSmokeTests
    private static OptionHint LatinChance(CardContext c)
    {
        var chance = (c.Skill(Who.Leader, 0x0B) + c.Attribute(Who.Leader, 5)) / 2;
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R083+0x085C (1838:085C) matched; tests: CardSmokeTests
    private static void ParleyInLatin(CardContext c, int chance, int block)
    {
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, SabbatPicture);
            c.Screen.Text(0x0F);
            c.PracticeSkill(Who.Leader, 0x0B, 1, 10, 0);
            c.State.CurrentLocation = -1;
            c.Next = 0x0C;
            return;
        }
        var latin = c.Skill(c.Leader, 0x0B);
        ShowPicture(c, SabbatPicture);
        c.Screen.Text(latin >= roll ? 0x12 : 0x10);
        Fight(c, block);
    }
}
