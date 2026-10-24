using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$meetw00": a wolf pack on the party's trail, by day or by night. Frighten them off with
/// wood lore (or a camp fire at night), outride them, call a saint, throw a potion, or fight.
/// </summary>
// orig: R084+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R084+0x0722 (1838:0722) matched; R084+0x07A0 (1838:07A0) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class WolfPackOnTheTrail : Card
{
    public const int CardNumber = 0xA0;

    private const int WolvesPicture = 126;
    private const int EscapePicture = 139;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var picture = c.IsDaylight ? 286 : 61;
        Show(c, picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 4);
        menu[1] = c.PartyGet(5) == 0 ? OptionState.Hidden : OptionState.Enabled;
        c.PartyMaxSkill(0x12);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        c.SetCardItems(0x5F);
        c.SetCardSaints(3, 0x45, 0x6B, 0x79);
        menu[3] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();
        menu.EachRound = () => menu.Block = c.IsDaylight ? 0 : 1;
        return menu.Run(
            new MenuLine(chance => FrightenThem(c, chance), hint: () => WoodLoreChance(c)),
            new MenuLine(chance => OutrideThem(c, chance), () => RidingChance(c)),
            // orig: R084+0x0572 (line 2); R084+0x0814 (1838:0814) matched; R084+0x089E (1838:089E) matched; R084+0x0928 (1838:0928) matched;
            // R084+0x09B2 (1838:09B2) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c, picture), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R084+0x0654 (1838:0654) matched; R084+0x06AC (1838:06AC) matched; tests: CardSmokeTests
            // orig: R084+0x07D8 (1838:07D8) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // C: no roll: the potion always drives them off.
                c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
                ShowPicture(c, 20);
                c.Screen.Text(9);
                GoOn(c);
            }, OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.PotionWorks)),
            // orig: R084+0x06E4 (1838:06E4) matched; R084+0x06EA (1838:06EA) matched; tests: CardSmokeTests
            new MenuLine(() => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)));
    }

    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    // orig: R084+0x01F2 (1838:01F2) matched; tests: CardSmokeTests
    private static void Fight(CardContext c)
    {
        ShowPicture(c, WolvesPicture);
        c.Screen.Text(6);
        var wolves = c.Random(5) + 3;
        switch (c.RunBattle(0x2F, -1, 0x2A, c.PartyLoreLevel() / 4 + 1, wolves, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, 287);
                c.Screen.Text(10);
                c.PassHours(1);
                GoOn(c);
                break;
            case 1:
                c.DropAway();
                GoOn(c);
                break;
            case 2:
                c.Screen.Text(0x0B);
                c.PassHours(2);
                c.Next = CardNumber;
                break;
            case 3:
            case 4:
                // The wolves kill one member and the horses bolt.
                var victim = c.Random(c.PartySize);
                if (c.Status(victim) == CharacterStatus.Empty)
                {
                    var i = victim;
                    do
                    {
                        i = (i + 1) % 5;
                    }
                    while (c.Status(i) == CharacterStatus.Empty);
                    victim = i;
                }
                c.Variables.Chosen[0] = victim;
                c.SetStatus(victim, CharacterStatus.Dead);
                ShowPicture(c, EscapePicture);
                c.Screen.Text(c.PartyGet(5) != 0 ? 0x0C : 0x0D);
                c.RemoveItemsWithFlags(Who.Everybody, 0x2000);
                GoOn(c);
                c.SetStatus(victim, CharacterStatus.Empty);
                c.DeleteMember(victim);
                c.PassHours(0);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party facing the pack.
                break;
        }
    }

    // orig: R084+0x043C (1838:043C) matched; tests: CardSmokeTests
    // C: (daylight ? -1 : 0) & 0x1E: 30 by day, 0 at night; at night at most 80.
    private static OptionHint WoodLoreChance(CardContext c)
    {
        var chance = (c.IsDaylight ? -1 : 0) & 0x1E;
        chance += c.PartyMaxSkill(0x12);
        if (!c.IsDaylight && chance > 80)
        {
            chance = 80;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Variables.Chosen[0]);
    }

    // orig: R084+0x03CC (1838:03CC) matched; tests: CardSmokeTests
    private static void FrightenThem(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, EscapePicture);
            if (c.IsDaylight)
            {
                c.Screen.Text(2);
            }
            else
            {
                c.PassHours(3);
                c.Screen.Text(3);
            }
            GoOn(c);
            return;
        }
        Fight(c);
    }

    // orig: R084+0x0536 (1838:0536) matched; tests: CardSmokeTests
    // C: item_quality_flags(member, 0x02000000) as the long is pushed (low word 0); BUG: it tests item 0's flags.
    private static OptionHint RidingChance(CardContext c)
    {
        var worst = c.PartyMinSkill(0x11);
        var rider = (byte)c.BestMember;
        return OptionHint.ChanceOf(Math.Clamp(c.ItemQualityByFlags(rider, 0x02000000) + worst, 1, 99), rider);
    }

    // orig: R084+0x048C (1838:048C) matched; tests: CardSmokeTests
    private static void OutrideThem(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(3);
            ShowPicture(c, 286);
            c.Screen.Text(4);
            c.PracticeSkill(Who.Everybody, 0x11, 7, 10, 0);
            GoOn(c);
            return;
        }
        c.PassHours(2);
        ShowPicture(c, WolvesPicture);
        c.Screen.Text(5);
        Fight(c);
    }

    // orig: R084+0x0572 (1838:0572) matched; R084+0x061C (1838:061C) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c, int picture)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.NameToSlot(1, c.CardSaints[c.MenuSlot], 0);
        ShowPicture(c, EscapePicture);
        if (answer != 0)
        {
            c.PassHours(1);
            c.Screen.Text(7);
            GoOn(c);
            return;
        }
        c.Screen.Text(8);
        ShowPicture(c, picture);
        c.Next = CardNumber;
    }
}
