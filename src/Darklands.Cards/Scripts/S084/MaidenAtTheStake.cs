using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago12": villagers leave a girl tied to a stake for the dragon. Talk them out of it,
/// rescue her by force, have the stealthiest member sneak her away, have the fastest cut her
/// loose, distract the villagers with a potion, or do not interfere. Each girl saved counts for
/// the dragon quest (fact 4/6); the dragon's lair now shows the dwarf at its door.
/// </summary>
// Reconstructed from the disassembly (no C):
// orig: R084+0x1622 (199A:0002) asm; tests: CardSmokeTests
// orig: R084+0x1ECC (199A:08AC) asm; R084+0x1F38 (199A:0918) asm; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class MaidenAtTheStake : Card
{
    public const int CardNumber = 0xA2;

    /// <summary>The card's MSG name (the card table has none for this card).</summary>
    private const string MsgName = "$Drago12";

    private const int Picture = 237;
    private const int SpeechPicture = 53;
    private const int SavedPicture = 229;
    private const int LostPicture = 121;
    private const int DragonQuest = 4;

    /// <summary>Each potion's chance (by pop-up slot): noxious aroma, eyeburn, sunburst, black cloud.</summary>
    private static readonly int[] PotionChances = [0x1B, 0x30, 0x36, 0x5A];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        c.Next = CardNumber;
        var lair = c.DragonLair();
        // DIVERGENCE: without a dragon quest (lair -1) the original writes through index -1; skipped here.
        if (lair >= 0 && lair < c.State.Locations.Count)
        {
            c.State.Locations[lair].ArrivalCard = 0x9F;
        }
        c.PartyMaxSkill(0x0F);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        c.PartyMaxAttribute(2);
        c.Variables.Chosen[1] = (byte)c.BestMember;
        var menu = Menu(c);
        menu.Enable(1, 2, 3, 4, 5);
        menu[0] = c.Events.FactAnywhere(DragonQuest, 0x0B) ? OptionState.Shown : OptionState.Enabled;
        c.SetCardSaints();
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        // C: items_usable() is stored in opt_flag_6, but the menu copies only flags 0..5: the
        // potion line (flag 4) is always enabled.
        _ = c.ItemsUsable();
        return menu.Run(
            // orig: R084+0x196C (199A:034C) asm; R084+0x1A6E (199A:044E) asm; tests: CardSmokeTests
            new MenuLine(() =>
            {
                if (!Oratory(c, OratoryChance(c).Chance))
                {
                    menu[0] = OptionState.Shown;
                }
            }, () => OratoryChance(c)),
            // orig: R084+0x1AA2 (199A:0482) asm; R084+0x1AAC (199A:048C) asm; tests: CardSmokeTests
            new MenuLine(() => Fight(c, 1), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R084+0x1AE4 (199A:04C4) asm; R084+0x1BA4 (199A:0584) asm; tests: CardSmokeTests
            new MenuLine(chance => SneakHerAway(c, chance), hint: () => StealthChance(c)),
            // orig: R084+0x1BC6 (199A:05A6) asm; R084+0x1C80 (199A:0660) asm; tests: CardSmokeTests
            new MenuLine(chance => CutHerLoose(c, chance), hint: () => SpeedChance(c)),
// orig: R084+0x1CA2 (199A:0682) asm; R084+0x1E1A (199A:07FA) asm; tests: CardSmokeTests
            // orig: R084+0x1F70 (199A:0950) asm; R084+0x1F8A (199A:096A) asm; R084+0x1FA4 (199A:0984) asm; R084+0x1FBE (199A:099E) asm; tests: CardSmokeTests
            new MenuLine(() => DistractThem(c), OptionHint.Saying(CardTexts.SelectPotion), () =>
            {
                c.Variables.Chosen[0] = c.MenuMember;
                return OptionHint.ChanceOf(c.MenuSlot is >= 0 and < 4 ? PotionChances[c.MenuSlot] : -1, c.MenuMember);
            }),
            // orig: R084+0x1E52 (199A:0832) asm; R084+0x1E9E (199A:087E) asm; tests: CardSmokeTests
            new MenuLine(() =>
            {
                ShowPicture(c, 228);
                c.Screen.Text(0x0F);
                c.PassHours(2);
                GoOn(c);
            }, OptionHint.Saying("Leaving her to die is hardly heroic")));
    }

    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    private static void SheIsSaved(CardContext c) =>
        c.Events.Update(-1, -1, 0x1C, 6, 1, DragonQuest, EventUpdate.Add);

    private static OptionHint OratoryChance(CardContext c)
    {
        var chance = (c.Skill(Who.Leader, 0x0A) + c.Attribute(Who.Leader, 5)) / 3;
        c.Variables.Chosen[0] = (sbyte)c.Leader;
        return OptionHint.ChanceOf(chance, (sbyte)c.Leader);
    }

    /// <summary>Returns false when the villagers will not listen (the speech is not repeated).</summary>
    private static bool Oratory(CardContext c, int chance)
    {
        ShowPicture(c, SpeechPicture);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            c.PracticeSkill(Who.Leader, 0x0A, 1, 10, 0);
            c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
            SheIsSaved(c);
            c.PassHours(3);
            GoOn(c);
            return true;
        }
        c.Screen.Text(2);
        c.Events.Create(-2, c.Here, 0, 0x1C, 0x0B, 0, 0, 26280, 0, DragonQuest);
        c.PassHours(1);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return false;
    }

    // orig: R084+0x1826 (199A:0206) asm; tests: CardSmokeTests
    private static void Fight(CardContext c, int ambush)
    {
        // C: arguments are evaluated right to left: rand_mod(3) + 2 (second group) first.
        var second = c.Random(3) + 2;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x43, -1, 0x35, first, second, -1, 0, 0, -1, ambush, 0))
        {
            case 0:
                c.Screen.Text(3);
                c.PassHours(1);
                SheIsSaved(c);
                c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
                GoOn(c);
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, LostPicture);
                c.Screen.Text(4);
                c.PassHours(2);
                GoOn(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, LostPicture);
                c.Screen.Text(5);
                c.PassHours(2);
                c.LoseItems(Who.Everybody);
                GoOn(c);
                break;
            default:
                // DORMANT: other battle results leave the party watching.
                break;
        }
    }

    private static OptionHint StealthChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(0x0F);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    private static void SneakHerAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            // C: the leader practises stealth, not the member who sneaked.
            c.PracticeSkill(Who.Leader, 0x0F, 1, 10, 0);
            c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
            ShowPicture(c, SavedPicture);
            c.Screen.Text(6);
            SheIsSaved(c);
        }
        else
        {
            ShowPicture(c, Picture);
            c.Screen.Text(7);
        }
        c.PassHours(2);
        GoOn(c);
    }

    private static OptionHint SpeedChance(CardContext c)
    {
        var chance = c.PartyMaxAttribute(2);
        c.Variables.Chosen[1] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    private static void CutHerLoose(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, SavedPicture);
            c.Screen.Text(8);
            SheIsSaved(c);
            c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
            c.PassHours(2);
            GoOn(c);
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(9);
        Fight(c, 0);
    }

    private static void DistractThem(CardContext c)
    {
        // C: the option line itself gives chance -1: the potion then always fails.
        var chance = -1;
        if (c.MenuPopup && c.MenuSlot is >= 0 and < 4)
        {
            c.Variables.Chosen[0] = c.MenuMember;
            chance = PotionChances[c.MenuSlot];
        }
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        // C: here a roll below the chance (<) succeeds.
        if (c.Random(100) < chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    ShowPicture(c, 20);
                    c.Screen.Text(0x0A);
                    break;
                case 1:
                    ShowPicture(c, 189);
                    c.Screen.Text(0x0B);
                    break;
                case 2:
                    ShowPicture(c, 189);
                    c.Screen.Text(0x0C);
                    break;
                case 3:
                    ShowPicture(c, 16);
                    c.Screen.Text(0x0D);
                    break;
                default:
                    // DORMANT: no slot outside 0..3: no text.
                    break;
            }
            SheIsSaved(c);
            c.PracticeSkill(Who.Everybody, 9, 1, 10, 0);
            c.PassHours(2);
            GoOn(c);
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(0x0E);
        c.Next = CardNumber;
    }
}
