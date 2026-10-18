using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago11": armed peasants of the blighted land demand the party's fairest woman as the
/// dragon's next sacrifice. Talk them out of it, outrun them, give her up, hide, call a saint,
/// or ask about the dragon. Without a woman (or by chance) the party instead comes upon a maiden
/// being tied to the stake. Each maiden saved counts for the dragon quest (fact 4/6).
/// </summary>
// orig: R084+0x0A3C (18DB:000C) matched; tests: CardSmokeTests
// orig: R084+0x13EA (18DB:09BA) matched; R084+0x144C (18DB:0A1C) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class PeasantsDemandAMaiden : Card
{
    public const int CardNumber = 0xA1;

    private const int Picture = 237;
    private const int LostPicture = 121;
    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 5);
        c.Variables.Chosen[0] = c.Leader;
        // C: && short-circuits: the able women are counted only after a non-zero coin.
        if (c.Random(2) != 0 && c.AbleWomen() != 0)
        {
            c.PartyMaxAttributeOfWomen(5);
            c.Variables.Chosen[0] = (byte)c.BestMember;
        }
        else
        {
            ShowPicture(c, 145);
            c.Screen.Text(1);
            c.Next = 0xA2;
        }
        c.SetCardItems();
        c.SetCardSaints(0x15, 0x36, 0x3D);
        menu[4] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            new MenuLine(() =>
            {
                if (!TalkThemOutOfIt(c, PersuasionChance(c).Chance))
                {
                    menu[0] = OptionState.Shown;
                }
            }, () => PersuasionChance(c)),
            new MenuLine(() => OutrunThem(c, RunningChance(c).Chance), hint: () => RunningChance(c)),
            // orig: R084+0x1080 (18DB:0650) matched; R084+0x1134 (18DB:0704) matched; tests: CardSmokeTests
            new MenuLine(() => GiveHerUp(c), OptionHint.Saying("You dog!")),
            new MenuLine(() => Hide(c, HidingChance(c).Chance), hint: () => HidingChance(c)),
            // orig: R084+0x1484 (18DB:0A54) matched; R084+0x150E (18DB:0ADE) matched; R084+0x1598 (18DB:0B68) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R084+0x136C (18DB:093C) matched; R084+0x13B2 (18DB:0982) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PassHours(1);
                ShowPicture(c, Picture);
                c.Screen.Text(0x11);
                c.Next = CardNumber;
            }, OptionHint.Saying(SuccessAssured)));
    }

    private static void AMaidenSaved(CardContext c) =>
        c.Events.Update(-1, -1, 0x1C, 6, 1, DragonQuest, EventUpdate.Add);

    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    /// <summary>The member they demanded is lost (taken for the dragon).</summary>
    private static void SheIsTaken(CardContext c)
    {
        var maiden = c.Variables.Chosen[0];
        c.DeleteMember(maiden);
        if (maiden < c.PartySize)
        {
            c.SetStatus(maiden, CharacterStatus.Empty);
        }
    }

    // orig: R084+0x0C54 (18DB:0224) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int ambush)
    {
        // C: arguments are evaluated right to left: rand_mod(3) + 2 (second group) first.
        var second = c.Random(3) + 2;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x42, -1, 0x35, first, second, -1, 0, 0, -1, ambush, 0))
        {
            case 0:
                ShowPicture(c, Picture);
                c.Screen.Text(7);
                c.PassHours(1);
                AMaidenSaved(c);
                GoOn(c);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                ShowPicture(c, 224);
                c.Screen.Text(8);
                ShowPicture(c, LostPicture);
                c.Screen.Text(9);
                SheIsTaken(c);
                c.PassHours(2);
                GoOn(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, 224);
                c.Screen.Text(0x0A);
                ShowPicture(c, LostPicture);
                c.Screen.Text(9);
                SheIsTaken(c);
                c.PassHours(2);
                c.LoseItems(Who.Everybody);
                GoOn(c);
                break;
            default:
                // DORMANT: other battle results leave the party before the peasants.
                break;
        }
    }

    // orig: R084+0x0F14 (18DB:04E4) matched; tests: CardSmokeTests
    private static OptionHint PersuasionChance(CardContext c) =>
        OptionHint.ChanceOf((c.Skill(Who.Leader, 0x0A) + c.Attribute(Who.Leader, 5)) / 3, c.Leader);

    /// <summary>Returns false when they will not listen (the attempt is not repeated).</summary>
    // orig: R084+0x0E4A (18DB:041A) matched; tests: CardSmokeTests
    private static bool TalkThemOutOfIt(CardContext c, int chance)
    {
        ShowPicture(c, Picture);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(2);
            c.PassHours(1);
            AMaidenSaved(c);
            GoOn(c);
            return true;
        }
        c.Screen.Text(3);
        c.Events.Create(-2, c.Here, 0, 0x1C, 0x0B, 0, 0, 26280, 0, DragonQuest);
        c.Next = CardNumber;
        return false;
    }

    // orig: R084+0x1054 (18DB:0624) matched; tests: CardSmokeTests
    private static OptionHint RunningChance(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(c.PartyMaxMountedAgility() + c.PartyMinMountedAgility(), 1, 99), OptionHint.WholeParty);

    // orig: R084+0x0F50 (18DB:0520) matched; tests: CardSmokeTests
    private static void OutrunThem(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (c.PartyGet(5) != 0)
            {
                ShowPicture(c, 286);
                c.Screen.Text(4);
                AMaidenSaved(c);
                c.PracticeSkill(Who.Everybody, 0x11, 1, 10, 0);
                c.PassHours(1);
            }
            else
            {
                // C: on foot the escape does not count as a maiden saved.
                ShowPicture(c, 287);
                c.Screen.Text(5);
                c.PassHours(3);
            }
            GoOn(c);
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(6);
        Fight(c, 0);
    }

    private static void GiveHerUp(CardContext c)
    {
        ShowPicture(c, Picture);
        c.Screen.Text(0x0B);
        ShowPicture(c, LostPicture);
        c.Screen.Text(9);
        var maiden = c.Variables.Chosen[0];
        if (maiden < c.PartySize)
        {
            c.SetStatus(maiden, CharacterStatus.Empty);
        }
        c.DeleteMember(maiden);
        c.PassHours(0);
        c.AddSkill(Who.Everybody, 9, -(c.Random(6) + 2));
        GoOn(c);
    }

    // orig: R084+0x1218 (18DB:07E8) matched; tests: CardSmokeTests
    private static OptionHint HidingChance(CardContext c)
    {
        var chance = c.Skill(Who.Leader, 0x12);
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R084+0x115E (18DB:072E) matched; tests: CardSmokeTests
    private static void Hide(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 145);
            c.Screen.Text(0x0C);
            c.PassHours(6);
            AMaidenSaved(c);
            c.PracticeSkill(Who.Leader, 0x12, 1, 10, 0);
            GoOn(c);
            return;
        }
        ShowPicture(c, LostPicture);
        c.Screen.Text(0x0D);
        Fight(c, 1);
    }

    // orig: R084+0x123A (18DB:080A) matched; R084+0x1334 (18DB:0904) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    ShowPicture(c, 286);
                    c.Screen.Text(0x0F);
                    break;
                case 1:
                case 2:
                    ShowPicture(c, 287);
                    c.Screen.Text(0x10);
                    break;
                default:
                    // DORMANT: slot 3 holds no saint: no text.
                    break;
            }
            AMaidenSaved(c);
            GoOn(c);
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(0x0E);
        c.Next = CardNumber;
    }
}
