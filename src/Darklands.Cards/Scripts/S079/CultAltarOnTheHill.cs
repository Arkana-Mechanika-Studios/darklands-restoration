using Darklands.Core.Characters;
using Darklands.Core.World;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Destr01": the blackened altar of a witch cult on the hill above its village. Deface its
/// images, burn out the taint with alchemy, call a saint, lay a holy relic on it, call forth
/// its demon and fight it, or leave. A purged altar leaves the village an empty ruin.
/// </summary>
// orig: R079+0x168A (19A0:000A) matched; tests: CardSmokeTests
// orig: R079+0x20F2 (19A0:0A72) matched; R079+0x2170 (19A0:0AF0) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class CultAltarOnTheHill : Card
{
    public const int CardNumber = 0x8E;

    private const int Picture = 176;
    private const int PurgedPicture = 189;
    private const int FailurePicture = 16;
    private const int BoundPicture = 144;

    /// <summary>The abandoned hamlet the village becomes.</summary>
    private const int AbandonedHamlet = 0x88;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(4, 5);
        menu[0] = c.EventAtHere(0x58) ? OptionState.Shown : OptionState.Enabled;
        // A relic (item flag 0x1000) is needed for line 3.
        menu[3] = c.CountItemsWithFlags(Who.Everybody, 0x1000) != 0 ? OptionState.Enabled : OptionState.Shown;
        c.SetCardItems(0x66, 0x67, 0x6E);
        c.SetCardSaints(0x0F, 0x6F);
        menu[1] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(() =>
            {
                if (!DefaceTheAltar(c, DefaceChance(c).Chance))
                {
                    menu[0] = OptionState.Shown;
                }
            }, () => DefaceChance(c)),
            new MenuLine(() => BurnOutTheTaint(c), OptionHint.Saying(CardTexts.SelectPotion),
                () => PotionHint(c, c.MenuMember, c.MenuSlot)),
            // orig: R079+0x224C (19A0:0BCC) matched; R079+0x22D6 (19A0:0C56) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() =>
            {
                menu[3] = OptionState.Shown;
                LayARelic(c, RelicChance(c).Chance);
            }, () => RelicChance(c)),
            new MenuLine(() =>
            {
                if (!CallForthTheDemon(c, DemonChance(c).Chance))
                {
                    menu[4] = OptionState.Shown;
                }
            }, () => DemonChance(c)),
            // orig: R079+0x20B2 (19A0:0A32) matched; R079+0x20BA (19A0:0A3A) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x0C, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The altar is broken: the village is empty from now on and counts as destroyed.</summary>
    private static void ThePlaceIsCleansed(CardContext c)
    {
        var village = c.State.Locations[c.Here];
        village.ArrivalCard = AbandonedHamlet;
        village.Flags |= Location.DestroyedFlag;
        c.Next = AbandonedHamlet;
    }

    // orig: R079+0x187C (19A0:01FC) matched; tests: CardSmokeTests
    private static void FightTheDemon(CardContext c)
    {
        // C: arguments are evaluated right to left: rand_mod(3) + 1 (third group), rand_mod(5), then the first group's rand_mod(3).
        var third = c.Random(3) + 1;
        var second = c.Random(5) + 4;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x36, c.Here + 0x6F, 0x0B, first, second, 0x35, third, 1, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, PurgedPicture);
                c.Screen.Text(5);
                c.PracticeSkill(Who.Everybody, 9, 1, 20, 1);
                ThePlaceIsCleansed(c);
                // The map shows the village as a ruin (icon 9).
                c.State.Locations[c.Here].Icon = 9;
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, 286);
                c.Screen.Text(6);
                c.Next = 0x0C;
                break;
            case 3:
                ShowPicture(c, BoundPicture);
                c.Screen.Text(7);
                c.WaitUntilHour(0);
                c.Next = 0x8F;
                break;
            case 4:
                ShowPicture(c, BoundPicture);
                c.Screen.Text(8);
                // C: a negative practice percentage (0..-6) as the original passes it.
                c.PracticeSkill(Who.Everybody, 9, 0, -c.Random(7), 0);
                c.WaitUntilHour(0);
                c.Next = 0x8F;
                break;
            default:
                // DORMANT: other battle results leave the party at the altar.
                break;
        }
    }

    // orig: R079+0x1BAA (19A0:052A) matched; tests: CardSmokeTests
    private static OptionHint DefaceChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(8);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    /// <summary>The demon may rise for a famous party (fame above 200: a fame-in-1200 chance). Returns false when the altar stands.</summary>
    // orig: R079+0x1A3A (19A0:03BA) matched; tests: CardSmokeTests
    private static bool DefaceTheAltar(CardContext c, int chance)
    {
        c.PassHours(8);
        var fame = c.State.Party.Fame;
        if (c.Random(100) > chance)
        {
            // C: && short-circuits: the 1200-roll is drawn only for fame above 200.
            if (fame > 200 && c.Random(1200) <= fame)
            {
                ShowPicture(c, 235);
                c.Screen.Text(1);
                FightTheDemon(c);
            }
            else
            {
                c.Screen.Text(2);
            }
            // C: noted for four days even after the demon fight.
            c.Events.Create(-2, c.Here, 0, 0x58, 1, 0x63, 0, 0x60, 0, 0);
            return false;
        }
        if (fame > 200 && c.Random(1200) <= fame)
        {
            ShowPicture(c, 232);
            c.Screen.Text(3);
            FightTheDemon(c);
            return true;
        }
        ShowPicture(c, PurgedPicture);
        c.Screen.Text(4);
        c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
        ThePlaceIsCleansed(c);
        return true;
    }

    // orig: R079+0x21A8 (19A0:0B28) matched; R079+0x21E0 (19A0:0B60) matched; R079+0x2210 (19A0:0B90) matched; tests: CardSmokeTests
    private static OptionHint PotionHint(CardContext c, int member, int slot)
    {
        c.Variables.Chosen[0] = member;
        return slot switch
        {
            0 => OptionHint.ChanceOf(Math.Clamp(c.Skill(member, 7) / 2 + 15, 1, 99), member),
            1 => OptionHint.ChanceOf(Math.Clamp(c.Skill(member, 7), 1, 99), member),
            _ => OptionHint.Say(SuccessAssured, member),
        };
    }

    // orig: R079+0x1BCC (19A0:054C) matched; R079+0x1D4A (19A0:06CA) matched; tests: CardSmokeTests
    private static void BurnOutTheTaint(CardContext c)
    {
        // C: the option line itself gives chance -1 (and member and slot whatever the pop-up left).
        var chance = c.MenuPopup ? PotionHint(c, c.MenuMember, c.MenuSlot).Chance : -1;
        c.PassHours(1);
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        switch (c.MenuSlot)
        {
            case 0:
            case 1:
                if (c.Random(100) <= chance)
                {
                    ShowPicture(c, PurgedPicture);
                    c.Screen.Text(0x0B);
                    c.PracticeSkill(c.Variables.Chosen[0], 9, 1, 10, 0);
                    // C: the original writes the arrival card twice (0x8A, then 0x88).
                    c.State.Locations[c.Here].ArrivalCard = 0x8A;
                    ThePlaceIsCleansed(c);
                }
                else
                {
                    ShowPicture(c, 20);
                    c.Screen.Text(0x0D);
                    ShowPicture(c, Picture);
                    c.Next = CardNumber;
                }
                break;
            case 2:
                c.NameToSlot(0, 0x6E, 0);
                ShowPicture(c, 197);
                c.Screen.Text(0x0C);
                c.PracticeSkill(c.Variables.Chosen[0], 9, 1, 10, 0);
                ThePlaceIsCleansed(c);
                break;
            default:
                // DORMANT: slot 3 holds no item: an hour passes and nothing happens.
                break;
        }
    }

    // orig: R079+0x1D82 (19A0:0702) matched; R079+0x1EA0 (19A0:0820) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.Variables.Chosen[0] = c.MenuMember;
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 0x0F, 0);
                break;
            case 1:
                c.NameToSlot(1, 0x6F, 0);
                break;
            default:
                // DORMANT: slots 2, 3 hold no saint: no name.
                break;
        }
        if (answer != 0)
        {
            ShowPicture(c, PurgedPicture);
            c.Screen.Text(9);
            c.PassHours(0);
            c.PracticeSkill(c.Variables.Chosen[0], 9, 1, 10, 0);
            ThePlaceIsCleansed(c);
            return;
        }
        ShowPicture(c, FailurePicture);
        c.Screen.Text(0x0A);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = CardNumber;
    }

    // orig: R079+0x1FC4 (19A0:0944) matched; tests: CardSmokeTests
    private static OptionHint RelicChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(9);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    // orig: R079+0x1ED8 (19A0:0858) matched; tests: CardSmokeTests
    private static void LayARelic(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, PurgedPicture);
            c.Screen.Text(0x10);
            c.PassHours(1);
            c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
            ThePlaceIsCleansed(c);
            return;
        }
        ShowPicture(c, FailurePicture);
        c.Screen.Text(0x11);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = CardNumber;
    }

    // orig: R079+0x2082 (19A0:0A02) matched; tests: CardSmokeTests
    private static OptionHint DemonChance(CardContext c)
    {
        var chance = Math.Clamp(c.PartyMaxSkillSum(8, 9), 0, 99);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    /// <summary>Returns false when the demon does not come.</summary>
    // orig: R079+0x1FE6 (19A0:0966) matched; tests: CardSmokeTests
    private static bool CallForthTheDemon(CardContext c, int chance)
    {
        c.PassHours(4);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 234);
            c.Screen.Text(0x0E);
            FightTheDemon(c);
            return true;
        }
        ShowPicture(c, FailurePicture);
        c.Screen.Text(0x0F);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return false;
    }
}
