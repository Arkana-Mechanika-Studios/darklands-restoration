using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetB02": unemployed mercenaries turned bandits lie in ambush by the road — a harder twin
/// of card 258. The best scout may notice them; then the party can press on, talk, flush them
/// out with a potion, pray, surrender or slip away. Unnoticed, they spring the ambush.
/// </summary>
// orig: R108+0x3028 (1B3A:0008) matched; tests: CardSmokeTests
// orig: R108+0x3B30 (1B3A:0B10) matched (registration: item pop-ups R108+0x3BF2/0bf8 name the member; saint pop-ups do not)
// DORMANT: R108+0x3C40 and R108+0x3C7C are never called.
public sealed class MercenaryBanditAmbush : Card
{
    private const int AmbushPicture = 33;
    private const int RoadPicture = 286;
    private const int BanditsPicture = 287;
    private const int CampPicture = 61;

    public override int Number => 259;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x20);
        // The name is copied from a data block (DS:6578, "$MeetB02", checked against the asm).
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5);
        c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        c.Variables.Chosen[0] = c.BestMember;
        var watchfulness = c.Attribute(c.BestMember, (int)Attribute.Perception) + c.Random(10) + c.Skill(c.BestMember, (int)Skill.Woodwise) / 2;
        // C: the roll is made before the event test, every time.
        if (c.Random(100) > watchfulness && !c.Events.Started(0x4F))
        {
            Show(c, AmbushPicture);
            menu.Set(OptionState.Hidden, 0, 2, 5);
            menu.Block = 1;
            menu[6] = OptionState.Enabled;
        }
        else
        {
            Show(c, RoadPicture);
            menu.Block = 0;
        }
        c.SetCardItems(0x65, 0x66);
        c.SetCardSaints(0x36, 0x3D, 0x83, 0x45);
        if (menu.Block == 0)
        {
            menu[2] = c.ItemsUsable();
        }
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R108+0x359A (1B3A:057A) matched; hint R108+0x35D8
            new MenuLine(_ => { ShowPicture(c, BanditsPicture); c.Screen.Text(17); Fight(c, true); }, Unknown),
            // orig: R108+0x3610 (line 1); hint R108+0x36BE (1B3A:069E) matched
            new MenuLine(p => TalkYourWayPast(c, p), () => TalkHint(c)),
            // orig: R108+0x3712 (line 2); hint R108+0x37B2 (1B3A:0792) matched
            new MenuLine(p => FlushThemOut(c, p), () => OptionHint.Say("Of course the potion will work!"), () => PotionHint(c)),
            // orig: R108+0x37EA (line 3); hint R108+0x3908 (1B3A:08E8) matched
            new MenuLine(_ => PrayForPeacefulPassage(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R108+0x3940 (1B3A:0920) matched; hint R108+0x3996
            new MenuLine(_ => Surrender(c), () => OptionHint.Say(SuccessAssured)),
            // orig: R108+0x39CE (line 5); hint R108+0x3AB0 (1B3A:0A90) matched
            new MenuLine(p => SneakAway(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageEffectiveAgility() + c.PartyAverageSkill((int)Skill.Woodwise) * 2, 1, 99), OptionHint.WholeParty)),
            // orig: R108+0x3AE2 (1B3A:0AC2) matched; hint R108+0x3AEC
            new MenuLine(_ => Fight(c, false), () => OptionHint.Say(SuccessAssured)),
            // DORMANT: line 7 ("sneak up and see who they are", in text block 0) is never offered (opt_flag_7 = 0);
            // lines 7..9 have empty actions R108+0x3B24/0b08/0b0c and hints R108+0x3B26/0b0a/0b0e
            // (UNCERTAIN: the hints return AX/DX; neutral hints are used).
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>Throwing the potion: twice Perception plus 1 (slot 0) or plus 7 (slot 1), unclamped.</summary>
    // orig: R108+0x3BF2 (1B3A:0BD2) matched; slot 1 R108+0x3C18
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var perception = c.Attribute(m, (int)Attribute.Perception) * 2;
        return OptionHint.ChanceOf(c.MenuSlot == 0 ? perception + 1 : perception + 7, m);
    }

    // C: member_skill(var_chosen1 = best_member, 0xA): the most charming member is named inside the argument.
    private static OptionHint TalkHint(CardContext c)
    {
        var charm = c.PartyMaxAttribute((int)Attribute.Charisma) * 4;
        c.Variables.Chosen[0] = c.BestMember;
        var chance = Math.Clamp((charm + c.Skill(c.BestMember, (int)Skill.SpeakCommon)) / 4, 0, 99);
        return OptionHint.ChanceOf(chance, c.Variables.Chosen[0]);
    }

    // orig: R108+0x3610 (1B3A:05F0) matched; tests: CardSmokeTests
    private static void TalkYourWayPast(CardContext c, int chance)
    {
        c.PartyMaxAttribute((int)Attribute.Charisma);
        c.Variables.Chosen[0] = c.BestMember;
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, RoadPicture);
            c.Screen.Text(2);
            c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.SpeakCommon, 1, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, AmbushPicture);
        c.Screen.Text(3);
        Fight(c, true);
    }

    // orig: R108+0x3712 (1B3A:06F2) matched; tests: CardSmokeTests
    private static void FlushThemOut(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 189);
            c.Screen.Text(4);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, AmbushPicture);
        c.Screen.Text(5);
        Fight(c, true);
    }

    // orig: R108+0x37EA (1B3A:07CA) matched; tests: CardSmokeTests
    private static void PrayForPeacefulPassage(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Variables.Chosen[0] = c.MenuMember;
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    ShowPicture(c, 289);
                    c.Screen.Text(7, TextFrame.BlankPage);
                    break;
                case 2:
                    ShowPicture(c, 189);
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
                case 3:
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(9, TextFrame.BlankPage);
                    break;
            }
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(6);
        c.Next = 259;
    }

    // orig: R108+0x3940 (1B3A:0920) matched; tests: CardSmokeTests
    private static void Surrender(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        c.PassHours(1);
        ShowPicture(c, BanditsPicture);
        c.Screen.Text(10);
        c.LeaveForTheMap();
    }

    // orig: R108+0x39CE (1B3A:09AE) matched; tests: CardSmokeTests
    private void SneakAway(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.HoursUntil(18));
            ShowPicture(c, CampPicture);
            c.Screen.Text(12);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        Show(c, AmbushPicture);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        menu[1] = OptionState.Enabled;
        menu.Set(OptionState.Hidden, 0, 2);
        menu[3] = c.SaintsUsable();
        menu.Set(OptionState.Hidden, 5, 7, 8, 9);
        menu.Set(OptionState.Enabled, 4, 6);
        menu.Block = 1;
        c.Next = 259;
    }

    /// <summary>The mercenaries, more and better than card 258's bandits as the party's lore grows; then the party moves on.</summary>
    // orig: R108+0x332C (1B3A:030C) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(4) (inside clamp_range) is drawn before rand_mod(3), then run_battle draws its seed.
    private static void Fight(CardContext c, bool warned)
    {
        int NearestCity() => c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);

        var lore = c.PartyLoreLevel();
        var count = Math.Clamp(c.Random(4) + lore / 3 + 1, c.PresentCount(), 7);
        var groups = c.Random(3) + lore / 4 + 1;
        switch (c.RunBattle(warned ? 0x30 : 0x2F, -1, 0x0F, groups, count, lore > 6 ? 0x25 : 0x16, lore / 4 + 1, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, BanditsPicture);
                c.Screen.Text(11);
                c.AdjustReputation(NearestCity(), 0, 5);
                break;
            case 1:
            case 2:
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.Variables.Chosen[0] = i;
                    }
                }
                c.RejoinAway();
                c.PassHours(c.HoursUntil(18));
                ShowPicture(c, CampPicture);
                c.Screen.Text(12);
                c.PassHours(c.HoursUntil(5));
                break;
            case 3:
                c.AdjustReputation(NearestCity(), -1, -2);
                c.Variables.Chosen[0] = c.Leader;
                c.LoseItems(Who.Everybody);
                c.PassHours(4);
                ShowPicture(c, RoadPicture);
                c.Screen.Text(13);
                break;
            case 4:
                c.AdjustReputation(NearestCity(), -1, -2);
                c.LoseItems(Who.Everybody);
                c.PassHours(1);
                ShowPicture(c, BanditsPicture);
                c.Screen.Text(10);
                break;
        }
        c.LeaveForTheMap();
    }
}
