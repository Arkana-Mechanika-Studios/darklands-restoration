using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetM00": a small merchant caravan on the road. The party can trade, ask to travel with
/// it to the nearest city (days pass safely and the party gains Divine Favour), pray, or avoid
/// it. One caravan in eleven is a band of disguised man-wolf bandits: a perceptive party may
/// notice before they strike.
/// </summary>
// orig: R109+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R109+0x0CB0 (1838:0CB0) matched (registration: saint pop-ups only, they do not name the member)
public sealed class MerchantCaravanOnTheRoad : Card
{
    private const int CaravanPicture = 357;
    private const int BanditLeaderPicture = 81;
    private const int FleePicture = 145;

    public override int Number => 260;

    public override int Run(CardContext c)
    {
        Show(c, CaravanPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 1, 2, 3, 4);
        // The disguise is kept in the game's saved-card word, as the original does.
        c.State.SavedCard = c.Random(100) <= 8 ? 1 : 0;
        // C: rand_mod(4) + 1 << 1: the shift binds looser than +, so 2, 4, 6 or 8 days.
        c.Variables.Numbers[0] = (c.Random(4) + 1) * 2;
        c.PartyMaxAttribute((int)Attribute.Perception);
        c.Variables.Chosen[0] = c.BestMember;
        if (c.Events.Started(0x57))
        {
            TalkingWithTheMerchants(c, menu);
            menu[2] = OptionState.Enabled;
        }
        c.SetCardSaints(0x11, 0x25, 0x43, 0x5D);
        if (menu[3] == OptionState.Enabled)
        {
            menu[3] = c.SaintsUsable();
        }
        menu.Popup = () => c.SaintPopupChanceHint();

        // DORMANT: in text block 0 the option lines 5..9 are never offered (the first menu on this block never enables them); they can be offered in other blocks only.
        // DORMANT: in text block 1 the option lines 1..4, 7..9 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 12 the option lines 1..6, 8 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 13 the option lines 3, 5..9 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            // orig: R109+0x0614 (1838:0614) matched; hint R109+0x0666
            // DORMANT: line 0 (news, on to card 0x6E) is never offered: opt_flag_0 is 0 on every path.
            new MenuLine(_ => ExchangeNews(c, menu), Unknown),
            // orig: R109+0x069E (1838:069E) matched; hint R109+0x076C
            new MenuLine(_ => Trade(c, menu), Unknown),
            // orig: R109+0x07A4 (line 2); hint R109+0x0820 (1838:0820) matched
            new MenuLine(p => AskToTravelAlong(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                (c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2 + c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.PartyAverageSkill((int)Skill.Virtue)) / 2,
                1, 99), c.Leader)),
            // orig: R109+0x0880 (line 3); hint R109+0x0980 (1838:0980) matched (the placeholder str_saint_xxx)
            new MenuLine(_ => PrayForInsight(c, menu), () => OptionHint.Say("XXX Saint % success msg XXX.")),
            // orig: R109+0x09B8 (1838:09B8) matched; hint R109+0x0A04
            new MenuLine(_ => LetThemPass(c), () => OptionHint.Say(SuccessAssured)),
            // orig: R109+0x0A3C (1838:0A3C) matched; hint R109+0x0AC6
            new MenuLine(_ => TravelWithTheCaravan(c), Unknown),
            // orig: R109+0x0AFE (1838:0AFE) matched; hint R109+0x0B18
            new MenuLine(_ => { c.Screen.Text(15); c.LeaveForTheMap(); }, Unknown),
            // orig: R109+0x0B50 (1838:0B50) matched; hint R109+0x0B5A
            new MenuLine(_ => FightTheBandits(c, false), () => OptionHint.Say("Enter Combat")),
            // orig: R109+0x0B92 (1838:0B92) matched; hint R109+0x0BDE
            new MenuLine(_ => LetThemPass(c), Unknown),
            // orig: R109+0x0C16 (line 9); hint R109+0x0C80 (1838:0C80) matched
            new MenuLine(p => RunAway(c, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageEffectiveAgility() + c.PartyMaxSkill((int)Skill.Woodwise), 1, 99), OptionHint.WholeParty)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private static bool Disguised(CardContext c) => c.State.SavedCard != 0;

    /// <summary>"Continuing your discussions": trade, travel along, or go separate ways.</summary>
    private static void TalkingWithTheMerchants(CardContext c, CardMenu menu)
    {
        menu.Block = 13;
        menu.Set(OptionState.Enabled, 1, 4);
        menu.Set(OptionState.Hidden, 0, 2, 3, 5, 6, 7, 8, 9);
    }

    /// <summary>
    /// Approaching disguised bandits: a perceptive party (best Perception plus 0..19 reaching 60)
    /// sees the leader's face in time; otherwise the ambush is sprung.
    /// </summary>
    // orig: R109+0x02B6 (1838:02B6) matched; tests: CardSmokeTests
    private static void FaceTheDisguisedBandits(CardContext c, CardMenu menu)
    {
        var spotted = c.PartyMaxAttribute((int)Attribute.Perception) + c.Random(20);
        if (spotted >= 60)
        {
            ShowPicture(c, BanditLeaderPicture);
            menu.Block = 12;
            menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5, 6, 8);
            menu.Set(OptionState.Enabled, 7, 9);
            c.Next = 260;
            return;
        }
        ShowPicture(c, FleePicture);
        c.Screen.Text(7);
        FightTheBandits(c, true);
    }

    // orig: R109+0x0614 (1838:0614) matched; tests: CardSmokeTests
    private static void ExchangeNews(CardContext c, CardMenu menu)
    {
        if (Disguised(c))
        {
            FaceTheDisguisedBandits(c, menu);
            return;
        }
        TalkingWithTheMerchants(c, menu);
        c.EventHere(0x57, 3);
        c.State.SavedCard = 260;
        c.State.SavedCard2 = 260;
        c.Next = 0x6E;
    }

    /// <summary>The merchants' wares depend on the party's map row (y modulo 10).</summary>
    // orig: R109+0x069E (1838:069E) matched; tests: CardSmokeTests
    // C: party_y % 10 is C's signed remainder (y is never negative). cur_loc is cleared before the shop opens.
    private static void Trade(CardContext c, CardMenu menu)
    {
        if (Disguised(c))
        {
            FaceTheDisguisedBandits(c, menu);
            return;
        }
        c.State.CurrentLocation = Core.GameState.Wilderness;
        const string merchant = "merchant";
        switch (c.State.MapPosition.Y % 10)
        {
            case 0: c.OpenShop(-1, merchant, 1, 0); break;
            case 1: c.OpenShop(-1, merchant, 2, 0); break;
            case 2: c.OpenShop(-1, merchant, 4, 0); break;
            case 3: c.OpenShop(-1, merchant, 0x20, 4); break;
            case 4: c.OpenShop(-1, merchant, 0, 0x830); break;
            case 5: c.OpenShop(-1, merchant, 0, 0x400); break;
            case 6: c.OpenShop(-1, merchant, 0x40, 0); break;
            case 7: c.OpenShop(-1, merchant, 0x82, 0); break;
            case 8: c.OpenShop(-1, merchant, -0x8000, 0x1001); break;
            case 9: c.OpenShop(-1, merchant, 0x400, 0); break;
        }
        menu.Block = 13;
        menu[0] = OptionState.Hidden;
        menu[1] = OptionState.Enabled;
        if (menu[2] != OptionState.Shown)
        {
            menu[2] = OptionState.Enabled;
        }
        menu[4] = OptionState.Enabled;
        menu.Set(OptionState.Hidden, 3, 5, 6, 7, 8, 9);
        c.Next = 260;
    }

    // orig: R109+0x07A4 (1838:07A4) matched; tests: CardSmokeTests
    private static void AskToTravelAlong(CardContext c, CardMenu menu, int chance)
    {
        if (Disguised(c))
        {
            FaceTheDisguisedBandits(c, menu);
            return;
        }
        if (c.Random(100) <= chance)
        {
            menu.Block = 1;
            menu.Set(OptionState.Enabled, 5, 6);
            menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 7, 8, 9);
            c.Next = 260;
            return;
        }
        c.Screen.Text(2);
        TalkingWithTheMerchants(c, menu);
        menu[2] = OptionState.Shown;
        c.Next = 260;
    }

    /// <summary>A saint reveals the bandits for what they are, or vouches for honest merchants.</summary>
    // orig: R109+0x0880 (1838:0880) matched; tests: CardSmokeTests
    private static void PrayForInsight(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0 && Disguised(c))
        {
            ShowPicture(c, BanditLeaderPicture);
            menu.Block = 6;
            menu.Set(OptionState.Enabled, 7, 8);
            menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5, 6, 9);
            ShowPicture(c, CaravanPicture);
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 289);
            c.Screen.Text(4);
        }
        else
        {
            ShowPicture(c, 16);
            c.Screen.Text(3);
        }
        c.Next = 260;
        ShowPicture(c, CaravanPicture);
    }

    // orig: R109+0x09B8 (1838:09B8) matched (twin R109+0x0B92); tests: CardSmokeTests
    private static void LetThemPass(CardContext c)
    {
        ShowPicture(c, FleePicture);
        c.Screen.Text(5);
        c.PassHours(3);
        c.LeaveForTheMap();
    }

    /// <summary>
    /// Days with the caravan (two to eight): the party gains Divine Favour by its Religion and
    /// arrives at the nearest city.
    /// </summary>
    // orig: R109+0x0A3C (1838:0A3C) matched; tests: CardSmokeTests
    // C: load_city(cur_loc = nearest_city(party_x, party_y)); the party's map position is not moved.
    private static void TravelWithTheCaravan(CardContext c)
    {
        c.Screen.Text(14);
        c.PassHours(c.Variables.Numbers[0] * 24);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, 0);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddAttribute(i, (int)Attribute.DivineFavor, c.Skill(i, (int)Skill.Religion) / 5 + c.Variables.Numbers[0]);
            }
        }
        var city = c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);
        c.State.CurrentLocation = city;
        c.LoadCity(city);
        c.Next = CardContext.MapCard;
    }

    // orig: R109+0x0C16 (1838:0C16) matched; tests: CardSmokeTests
    private static void RunAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, FleePicture);
            c.Screen.Text(9);
            c.PassHours(c.HoursUntil(18));
            c.LeaveForTheMap();
            return;
        }
        FightTheBandits(c, false);
    }

    /// <summary>
    /// The man-wolf bandits (of two kinds, by a coin). Won, a lesson in Virtue; fled, a night in
    /// hiding; beaten, the weakest member dies and the rest wake robbed and half dead.
    /// </summary>
    // orig: R109+0x036E (1838:036E) matched; tests: CardSmokeTests
    // C: the kind coin is drawn first; then, arguments right to left, rand_mod(5) + 3 before rand_mod(3);
    // then run_battle draws its seed (-1).
    private static void FightTheBandits(CardContext c, bool ambushed)
    {
        var kind = c.Random(2);
        var lore = c.PartyLoreLevel();
        var count = c.Random(5) + 3;
        var groups = c.Random(3) + lore / 4 + 1;
        switch (c.RunBattle(ambushed ? 0x30 : 0x2F, -1, kind != 0 ? 2 : 0x0F, groups, count, kind != 0 ? 2 : 0x16, lore % 3 + 1, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 3);
                c.Screen.Text(8);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 15, 0);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                c.PassHours(c.HoursUntil(18));
                ShowPicture(c, FleePicture);
                c.Screen.Text(9);
                break;
            case 3:
                LeftForDead(c);
                ShowPicture(c, 286);
                c.Screen.Text(11);
                break;
            case 4:
                LeftForDead(c);
                ShowPicture(c, BanditLeaderPicture);
                c.Screen.Text(10);
                break;
        }
        c.LeaveForTheMap();
    }

    private static void LeftForDead(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        c.PartyMinAttribute((int)Attribute.Strength);
        var dead = c.BestMember;
        c.Variables.Chosen[0] = dead;
        c.SetStatus(dead, CharacterStatus.Empty);
        c.DeleteMember(dead);
        c.SetAttribute(Who.Everybody, (int)Attribute.Endurance, 1);
        c.SetAttribute(Who.Everybody, (int)Attribute.Strength, c.Random(10) + 1);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, 0);
        c.PassHours(c.HoursUntil(18));
    }
}
