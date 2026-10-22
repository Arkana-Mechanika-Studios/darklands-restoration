using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$meetb00": a sounder of wild boars bursts from the undergrowth of the wilderness. Dodge
/// aside, outrun them on horseback (when the whole party is mounted), call a saint to calm
/// the animals, or attack at once.
/// </summary>
// orig: R085+0x071A (18A9:000A) matched; tests: CardSmokeTests
// orig: R085+0x0D16 (18A9:0606) matched; R085+0x0D82 (18A9:0672) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class WildBoarsCharge : Card
{
    public const int CardNumber = 0xA5;

    private const int Picture = 123;
    private const int FightPicture = 124;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 3);
        // Only a party that is entirely mounted can ride away.
        if (c.PartyGet(5) == 0)
        {
            menu[1] = OptionState.Hidden;
        }
        c.SetCardItems();
        c.SetCardSaints(3, 0x45, 0x6B, 0x79);
        menu[2] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            // DORMANT: the start-up never leaves this card.
            return c.Next;
        }
        return menu.Run(
            // orig: R085+0x09E8 (18A9:02D8) matched; R085+0x0AAA (18A9:039A) matched; tests: CardSmokeTests
            new MenuLine(() => DodgeAside(c), OptionHint.Saying("It's every man for himself")),
            // orig: R085+0x0AD8 (18A9:03C8) matched; R085+0x0B94 (18A9:0484) matched; tests: CardSmokeTests
            new MenuLine(chance => OutrunThem(c, chance), () => OutrunChance(c)),
            // orig: R085+0x0BC0 (18A9:04B0) matched; R085+0x0CA0 (18A9:0590) matched; tests: CardSmokeTests
            // orig: R085+0x0DBA (18A9:06AA) matched; R085+0x0E44 (18A9:0734) matched; R085+0x0ECE (18A9:07BE) matched; R085+0x0F58 (18A9:0848) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R085+0x0CD8 (18A9:05C8) matched; R085+0x0CDE (18A9:05CE) matched; tests: CardSmokeTests
            new MenuLine(() => FightTheBoars(c), OptionHint.Saying(CardTexts.EnterCombat)));
    }

    /// <summary>The boars charge through the party: each member may be trampled; several hits make them turn and fight.</summary>
    private static void DodgeAside(CardContext c)
    {
        var hit = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty && c.Random(100) > (c.Attribute(i, 2) + 5) * 2)
            {
                c.Injure(i, 1, 1, 0x0A);
                hit++;
            }
        }
        ShowPicture(c, FightPicture);
        if (hit == 0)
        {
            c.Screen.Text(9);
            LeaveTheScene(c);
        }
        else if (hit == 1)
        {
            c.Screen.Text(0x0A);
            LeaveTheScene(c);
        }
        else
        {
            c.Screen.Text(0x0B);
            FightTheBoars(c);
        }
    }

    private static void LeaveTheScene(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    /// <summary>The riding hint: the best rider, at a chance of 1%.</summary>
    // UNCERTAIN: the original asks the party's best riding skill (skill 0x11) but then passes the
    // constant 0 to clamp_range(1, 99, .) (checked against the assembly): the chance is always 1.
    private static OptionHint OutrunChance(CardContext c)
    {
        c.PartyMaxSkill(0x11);
        // C: the member is the low byte of the best member.
        return OptionHint.ChanceOf(CardContext.ClampRange(1, 0x63, 0), (byte)c.BestMember);
    }

    private static void OutrunThem(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(3);
            ShowPicture(c, 286);
            c.Screen.Text(1);
            c.PracticeSkill(Who.Everybody, 0x11, 7, 10, 0);
            LeaveTheScene(c);
            return;
        }
        c.PassHours(2);
        c.PracticeSkill(Who.Everybody, 0x11, 0, 10, 0);
        ShowPicture(c, Picture);
        c.Screen.Text(2);
        FightTheBoars(c);
    }

    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 3, 0);
                break;
            case 1:
                c.NameToSlot(1, 0x45, 0);
                break;
            case 2:
                c.NameToSlot(1, 0x6B, 0);
                break;
            case 3:
                c.NameToSlot(1, 0x79, 0);
                break;
            default:
                // DORMANT: no further saint slot exists.
                break;
        }
        if (answer != 0)
        {
            c.PassHours(1);
            ShowPicture(c, 289);
            c.Screen.Text(4);
            LeaveTheScene(c);
            return;
        }
        ShowPicture(c, FightPicture);
        c.Screen.Text(5);
        c.Next = CardNumber;
    }

    // orig: R085+0x0894 (18A9:0184) matched; tests: CardSmokeTests
    private static void FightTheBoars(CardContext c)
    {
        ShowPicture(c, FightPicture);
        c.Screen.Text(3);
        var boars = Math.Clamp(c.Random(3) + c.PartyLoreLevel() / 2 + 1, 3, 8);
        switch (c.RunBattle(0x2F, -1, 0x2B, 1, boars, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, 332);
                c.Screen.Text(6);
                c.PassHours(1);
                c.AddItem(c.Leader, 0x92);
                LeaveTheScene(c);
                break;
            case 1:
            case 2:
                c.DropAway();
                LeaveTheScene(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, FightPicture);
                // UNCERTAIN: items with flag 0x2000 are taken to be the horses (text 7 drives them away).
                c.Screen.Text(c.CountItemsWithFlags(Who.Everybody, 0x2000) != 0 ? 7 : 8);
                c.Injure(Who.Everybody, 1, 1, 0x0A);
                LeaveTheScene(c);
                break;
            default:
                // DORMANT: other battle results leave the party where it is.
                break;
        }
    }
}
