using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago04": in a cave witches dance and chant, thanking their masters for the dragon.
/// Attack them, have the sneakiest member join their dance and spoil the spell, call a saint,
/// or sneak out. Thwarting them opens the way to the dragon's lair.
/// </summary>
// orig: R082+0x071C (18A9:000C) matched; tests: CardSmokeTests
// orig: R082+0x0C68 (18A9:0558) matched; R082+0x0CCA (18A9:05BA) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class WitchesSummoningTheDragon : Card
{
    public const int CardNumber = 0x9A;

    private const int ThwartedPicture = 110;
    private const int DefeatPicture = 237;
    private const int TheLair = 0x99;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 323);
        c.Next = CardNumber;
        c.PartyMaxSkill(0x0F);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        c.SetCardItems();
        c.SetCardSaints(0x0F, 0x2B, 0x6F);
        var menu = Menu(c);
        menu.Enable(0, 1, 3);
        menu[2] = c.SaintsUsable();
        return menu.Run(
            // orig: R082+0x0A2E (18A9:031E) matched; R082+0x0A34 (18A9:0324) matched; tests: CardSmokeTests
            new MenuLine(() => Fight(c), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(chance => JoinTheDance(c, chance), hint: () => DisguiseChance(c)),
            // orig: R082+0x0B40 (line 2); R082+0x0D02 (18A9:05F2) matched; R082+0x0D8C (18A9:067C) matched; R082+0x0E16 (18A9:0706) matched;
            // tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R082+0x0C22 (18A9:0512) matched; R082+0x0C30 (18A9:0520) matched; tests: CardSmokeTests
            new MenuLine(() => { c.State.CurrentLocation = -1; c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R082+0x0890 (18A9:0180) matched; tests: CardSmokeTests
    private static void Fight(CardContext c)
    {
        // C: arguments are evaluated right to left: roll-below(4) (third group) before roll-below(5).
        var third = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        switch (c.RunBattle(0x65, -1, 0x29, 3, first, 0x27, 4, third, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, ThwartedPicture);
                c.Screen.Text(1);
                c.Events.Create(-2, c.Here, 0x5F, 2, 0, 0x63, 0, 0x150, 0, 4);
                c.Next = TheLair;
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, DefeatPicture);
                c.Screen.Text(2);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 3:
                c.DropAway();
                c.LoseItems(Who.Everybody);
                ShowPicture(c, DefeatPicture);
                c.Screen.Text(3);
                c.PassHours(1);
                // C: this case skips the move to the wilderness.
                c.Next = 0x0C;
                break;
            case 4:
                c.DropAway();
                c.SetAttribute(Who.Everybody, 6, 0);
                c.LoseItems(Who.Everybody);
                ShowPicture(c, DefeatPicture);
                c.Screen.Text(4);
                c.PassHours(1);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party in the cave.
                break;
        }
    }

    // orig: R082+0x0B20 (18A9:0410) matched; tests: CardSmokeTests
    private static OptionHint DisguiseChance(CardContext c)
    {
        var sneak = c.Variables.Chosen[0];
        return OptionHint.ChanceOf(c.Skill(sneak, 0x0F), sneak);
    }

    // orig: R082+0x0A6C (18A9:035C) matched; tests: CardSmokeTests
    private static void JoinTheDance(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 306);
            c.Screen.Text(5);
            ShowPicture(c, ThwartedPicture);
            c.Screen.Text(1);
            // C: here no event is recorded, unlike the won battle.
            c.Next = TheLair;
            return;
        }
        ShowPicture(c, 307);
        c.Screen.Text(6);
        Fight(c);
    }

    // orig: R082+0x0B40 (18A9:0430) matched; R082+0x0BEA (18A9:04DA) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, ThwartedPicture);
            c.Screen.Text(c.MenuSlot == 1 ? 7 : 8);
            c.Next = TheLair;
            return;
        }
        ShowPicture(c, DefeatPicture);
        c.Screen.Text(9);
        Fight(c);
    }
}
