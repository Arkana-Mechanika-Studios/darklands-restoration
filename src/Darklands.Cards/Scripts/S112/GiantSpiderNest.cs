using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetG02": dark sticky vines by the path — a nest of giant spiders. The best scout may
/// sense the ambush; then the party can press on, hail, use a potion, pray, sneak around or
/// scout it out. Otherwise the spiders drop from the trees: potion, prayer, fight or flight.
/// </summary>
// orig: R112+0x165C (199D:000C) matched; tests: CardSmokeTests
// orig: R112+0x23DE (199D:0D8E) matched (registration: item pop-ups R112+0x24B8/0eaa name the member, slots 2..3 empty; saint pop-ups do not)
public sealed class GiantSpiderNest : Card
{
    private const int SpidersPicture = 359;
    private const int RoadPicture = 286;
    private const int CampPicture = 61;

    // C: the card's picture buffer (pic_buf), set by the card load.
    private int _cardPicture;

    public override int Number => 274;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x20);
        c.Next = Number;
        var menu = Menu(c, 0);
        c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        c.Variables.Chosen[0] = c.BestMember;
        var watchfulness = c.Attribute(c.BestMember, (int)Attribute.Perception) + c.Random(10) + c.Skill(c.BestMember, (int)Skill.Woodwise) / 2;
        // C: the roll is made before the event test, every time.
        if (c.Random(100) > watchfulness && !c.Events.Started(0x4F))
        {
            menu.Set(OptionState.Enabled, 2, 3, 6, 8);
            c.PartyMinMountedAgility();
            c.Variables.Chosen[0] = c.BestMember;
            _cardPicture = SpidersPicture;
            Show(c, SpidersPicture);
            menu.Block = 1;
        }
        else
        {
            _cardPicture = RoadPicture;
            Show(c, RoadPicture);
            menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 7);
            menu.Block = 0;
        }
        // C: the saints were set just before (0x4A/0x67/0x6B/0x79 for the ambush, else 0x36/0x3D/0x83 and
        // 0x45 or 3 by the terrain DS:D843 in 4..14), but the registration's memset(card_saints, -1, 8) that
        // follows erases them: no saint is available here until the spiders strike.
        c.SetCardSaints();
        c.SetCardItems(0x65, 0x66);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R112+0x1B5C (199D:050C) matched; hint R112+0x1BE8
            new MenuLine(_ => { TheSpidersStrike(c, menu, c.ItemsUsable()); c.Next = 274; }, Unknown),
            // orig: R112+0x1C20 (199D:05D0) matched; hint R112+0x1CAC
            new MenuLine(_ => { TheSpidersStrike(c, menu, c.ItemsUsable()); c.Next = 274; }, Unknown),
            // orig: R112+0x1CE4 (line 2); hint R112+0x1DEE (199D:079E) matched
            new MenuLine(p => ThrowAPotion(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R112+0x1E26 (line 3); hint R112+0x1F22 (199D:08D2) matched
            new MenuLine(_ => PrayForDeliverance(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R112+0x1F5A (199D:090A) matched; hint R112+0x1F96
            new MenuLine(_ => { ShowPicture(c, CampPicture); c.Screen.Text(8); c.Next = 274; }, () => OptionHint.Say(SuccessAssured)),
            // orig: R112+0x1FCE (line 5); hint R112+0x20E4 (199D:0A94) matched
            new MenuLine(p => SneakAround(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageSkill((int)Skill.Woodwise) + c.PartyAverageEffectiveAgility() * 2 / 3 + 5, 10, 85), OptionHint.WholeParty)),
            // orig: R112+0x2120 (199D:0AD0) matched; hint R112+0x2136
            new MenuLine(_ => FightTheSpiders(c, menu.Block == 1), () => OptionHint.Say("Enter Combat")),
            // orig: R112+0x216E (line 7); hint R112+0x2294 (199D:0C44) matched
            new MenuLine(p => ScoutItOut(c, menu, p), Unknown),
            // orig: R112+0x22CC (line 8); hint R112+0x239C (199D:0D4C) matched
            new MenuLine(p => RunAway(c, p), () => RunHint(c)),
            // orig: R112+0x23DA (199D:0D8A) matched (empty); hint R112+0x23DC (empty)
            // DORMANT: line 9 is never offered. UNCERTAIN: its hint returns AX/DX; a neutral hint is used.
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>Hurling a potion: Perception plus the potion's quality, plus 4 (slot 0) or 19 (slot 1); unclamped.</summary>
    // orig: R112+0x24B8 (199D:0E68) matched; slot 1 R112+0x24FA
    // C: slots 2 and 3 have empty pop-up handlers (R112+0x253C/0eee); only two potions, so they never show.
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var aim = c.Attribute(m, (int)Attribute.Perception) + c.ItemQuality(m, c.CardItems[c.MenuSlot]);
        return OptionHint.ChanceOf(aim + (c.MenuSlot == 0 ? 4 : 19), m);
    }

    // C: unmounted, the "chance" is the least nimble rider's agility itself.
    private static OptionHint RunHint(CardContext c) =>
        OptionHint.ChanceOf(c.PartyGet(5) != 0 ? Math.Clamp(c.PartyMinSkill((int)Skill.Riding) * 2, 25, 99) : c.PartyMinMountedAgility(), OptionHint.WholeParty);

    /// <summary>The spiders drop from the trees: potion (<paramref name="potionLine"/>), prayer, fight or flight.</summary>
    private static void TheSpidersStrike(CardContext c, CardMenu menu, OptionState potionLine)
    {
        menu.Set(OptionState.Hidden, 0, 1, 4, 5, 7, 9);
        menu[2] = potionLine;
        menu.Set(OptionState.Enabled, 3, 6, 8);
        c.PartyMinMountedAgility();
        c.Variables.Chosen[0] = c.BestMember;
        ShowPicture(c, SpidersPicture);
        menu.Block = 1;
        c.SetCardSaints(0x4A, 0x67, 0x6B, 0x79);
        menu[3] = c.SaintsUsable();
    }

    // orig: R112+0x1CE4 (199D:0694) matched; tests: CardSmokeTests
    private static void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 189);
            c.Screen.Text(3);
            c.LeaveForTheMap();
            return;
        }
        if (menu.Block == 1)
        {
            c.Screen.Text(14, TextFrame.BlankPage);
            FightTheSpiders(c, true);
            return;
        }
        TheSpidersStrike(c, menu, OptionState.Shown);
        c.Next = 274;
    }

    // orig: R112+0x1E26 (199D:07D6) matched; tests: CardSmokeTests
    private void PrayForDeliverance(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, 289);
            c.Screen.Text(4);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, SpidersPicture);
        c.Screen.Text(5);
        if (menu.Block == 1)
        {
            FightTheSpiders(c, true);
            return;
        }
        ShowPicture(c, _cardPicture);
        c.Next = 274;
    }

    // orig: R112+0x1FCE (199D:097E) matched; tests: CardSmokeTests
    private static void SneakAround(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.HoursUntil(18));
            ShowPicture(c, CampPicture);
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        c.PassHours(1);
        TheSpidersStrike(c, menu, c.ItemsUsable());
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        c.Next = 274;
    }

    /// <summary>Scouting shows the nest (potion, prayer, sneaking, fighting or flight, the prayer line without a saint check); failing, they strike.</summary>
    // orig: R112+0x216E (199D:0B1E) matched; tests: CardSmokeTests
    private static void ScoutItOut(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.PartyMaxSkill((int)Skill.Woodwise);
            c.Variables.Chosen[0] = c.BestMember;
            ShowPicture(c, 145);
            menu.Block = 2;
            menu.Set(OptionState.Hidden, 0, 1, 4, 7, 9);
            menu[2] = c.ItemsUsable();
            menu.Set(OptionState.Enabled, 3, 5, 6, 8);
        }
        else
        {
            TheSpidersStrike(c, menu, c.ItemsUsable());
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        }
        c.Next = 274;
    }

    /// <summary>Running from the spiders; mounted parties practise Riding either way.</summary>
    // orig: R112+0x22CC (199D:0C7C) matched; tests: CardSmokeTests
    private static void RunAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (c.PartyGet(5) != 0)
            {
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 7, 10, 0);
            }
            c.PassHours(3);
            ShowPicture(c, 287);
            c.Screen.Text(9);
            c.LeaveForTheMap();
            return;
        }
        if (c.PartyGet(5) != 0)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
        }
        ShowPicture(c, SpidersPicture);
        c.Screen.Text(7);
        FightTheSpiders(c, true);
    }

    /// <summary>The spiders: won, fame and goodwill; fled, those away are lost; beaten, robbed and the weakest member dragged off.</summary>
    // orig: R112+0x19B0 (199D:0360) matched; tests: CardSmokeTests
    // C: right-to-left arguments: the spider count is drawn before run_battle draws its seed (-1).
    // C: party_x is declared `extern long` in the original, so the call pushes party_y and party_x: nearest_city(party_x) receives the party's (x, y); nothing is undefined (checked against the asm).
    // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): result 5 takes the default arm and returns without leaving.
    // DORMANT: the switch's explicit cases -0x40..-1 return without leaving, and results 6 and above (pit, timed exit) cannot occur on this object-less battlefield; run_battle never gives them.
    private static void FightTheSpiders(CardContext c, bool ambushed)
    {
        var spiders = c.Random(3) + c.PartyLoreLevel() / 5 * 3 + 1;
        switch (c.RunBattle(ambushed ? 0x30 : 0x2F, -1, 0x39, 1, spiders, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, 331);
                c.Screen.Text(11);
                c.PassHours(1);
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 1, 1);
                c.GiveReward(1);
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, CampPicture);
                c.Screen.Text(6);
                c.PassHours(c.HoursUntil(5));
                break;
            case 3:
            case 4:
                c.LoseItems(Who.Everybody);
                c.PartyMinAttribute((int)Attribute.Strength);
                var victim = c.BestMember;
                c.SetStatus(victim, CharacterStatus.Empty);
                c.Variables.Chosen[0] = victim;
                c.DeleteMember(victim);
                ShowPicture(c, SpidersPicture);
                c.Screen.Text(10);
                c.PassHours(c.Random(3) + 3);
                break;
            default:
                return;
        }
        c.LeaveForTheMap();
    }
}
