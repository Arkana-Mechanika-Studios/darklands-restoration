using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetP01": a band of sick refugees on the road. The party can give food and clothing, pray
/// for their cure, or avoid them. Some such bands (about one in seven) are bandits in
/// disguise: a perceptive member may sense the ambush in time.
/// </summary>
// orig: R109+0x21A2 (1A52:0002) matched; tests: CardSmokeTests
// orig: R109+0x2DB8 (1A52:0C18) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: R109+0x2E52, R109+0x2E54, R109+0x2E56, R109+0x2E58 are never called; text 14 is never shown.
public sealed class RefugeesFromThePlague : Card
{
    private const int RefugeesPicture = 160;
    private const int RoadPicture = 286;
    private const int AmbushPicture = 33;
    private const int PrayerPicture = 289;

    public override int Number => 263;

    public override int Run(CardContext c)
    {
        Show(c, RefugeesPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 9);
        // C: (unsigned)money_pfennigs(purse): the low 16 bits of the long, unsigned, then / 20 + 1.
        var gift = Money.From(0, 0, Math.Clamp((ushort)c.State.Party.Purse.TotalPfennigs / 20 + 1, 1, 480));
        c.Variables.Money[0] = gift;
        if (c.State.Party.Purse < gift)
        {
            menu[0] = OptionState.Shown;
        }
        c.SetCardSaints(0x73, 0x75, 0x55);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // DORMANT: in text block 0 the option lines 5..8 are never offered (the switch to this block hides them, the first menu on this block never enables them); they can be offered in other blocks only.
        // DORMANT: in text block 5 the option lines 0, 1 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            // orig: R109+0x259C (line 0); hint R109+0x2716 (1A52:0576) matched
            new MenuLine(_ => GiveFoodAndClothing(c, menu, gift), Unknown),
            // orig: R109+0x274E (line 1); hint R109+0x28E6 (1A52:0746) matched
            new MenuLine(_ => PrayForTheirCure(c, menu), SelectASaint),
            // orig: R109+0x291E (1A52:077E) matched; hint R109+0x2990
            new MenuLine(_ => SpringTheAmbush(c, menu), Unknown),
            // orig: R109+0x29C8 (1A52:0828) matched; hint R109+0x29E0
            new MenuLine(_ => { c.PassHours(1); c.LeaveForTheMap(); }, () => OptionHint.Say(NoRisk)),
            // orig: R109+0x2A18 (1A52:0878) matched; hint R109+0x2ADE
            new MenuLine(_ => PrayForGuidance(c, menu), SelectASaint),
            // orig: R109+0x2B16 (line 5); hint R109+0x2BB0 (1A52:0A10) matched
            new MenuLine(p => Bargain(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.Skill(Who.Leader, (int)Skill.SpeakCommon) + c.Attribute(Who.Leader, (int)Attribute.Charisma), 1, 99), c.Leader)),
            // orig: R109+0x2BF2 (line 6); hint R109+0x2C64 (1A52:0AC4) matched
            new MenuLine(_ => PrayToStopTheAttack(c), SelectASaint),
            // orig: R109+0x2C9C (1A52:0AFC) matched; hint R109+0x2CA2
            new MenuLine(_ => FightTheBandits(c), () => OptionHint.Say(SuccessAssured)),
            // orig: R109+0x2CDA (1A52:0B3A) matched; hint R109+0x2D30
            new MenuLine(_ => Surrender(c), () => OptionHint.Say(SuccessAssured)),
            // orig: R109+0x2D68 (1A52:0BC8) matched; hint R109+0x2D80
            new MenuLine(_ => { c.PassHours(1); c.LeaveForTheMap(); }, () => OptionHint.Say(NoRisk)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private static OptionHint SelectASaint() => OptionHint.Say("Select a person and a saint.");

    /// <summary>"I think this is an ambush": go on, avoid them, or pray (lines 2, 3 and 4 of <paramref name="block"/>).</summary>
    private static void SenseTheAmbush(CardContext c, CardMenu menu, int block, bool withPrayer)
    {
        menu.Block = block;
        menu.Set(OptionState.Enabled, 2, 3);
        menu.Set(OptionState.Hidden, 0, 1, 4, 5, 6, 7, 8, 9);
        if (withPrayer)
        {
            c.SetCardSaints(0x5D, 0x25, 0x11, 0x43);
            menu[4] = c.SaintsUsable();
        }
    }

    /// <summary>The "refugees" draw weapons: bargain, pray, fight or surrender.</summary>
    // orig: R109+0x291E (1A52:077E) matched (also inlined in 03fc and 05ae)
    private static void SpringTheAmbush(CardContext c, CardMenu menu)
    {
        ShowPicture(c, AmbushPicture);
        menu.Block = 5;
        menu.Set(OptionState.Enabled, 5, 6, 7, 8);
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 9);
        c.SetCardSaints(0x36, 0x3D, 0x83);
        menu[6] = c.SaintsUsable();
    }

    /// <summary>Most such bands (86 in 100) are true refugees; otherwise they are bandits, sensed or not.</summary>
    // orig: R109+0x259C (1A52:03FC) matched; tests: CardSmokeTests
    // C: rand_mod(100) <= party_max_attr(3): the query draws nothing.
    private static void GiveFoodAndClothing(CardContext c, CardMenu menu, Money gift)
    {
        if (c.Random(100) <= 85)
        {
            c.PassHours(1);
            c.State.Party.Purse = c.State.Party.Purse.Subtract(gift);
            ShowPicture(c, RefugeesPicture);
            c.Screen.Text(1);
            c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, -1);
            c.LeaveForTheMap();
            return;
        }
        if (c.Random(100) <= c.PartyMaxAttribute((int)Attribute.Perception))
        {
            ShowPicture(c, RoadPicture);
            SenseTheAmbush(c, menu, 4, withPrayer: true);
            return;
        }
        SpringTheAmbush(c, menu);
    }

    /// <summary>For true refugees the prayer cures them (or at least comforts them); for bandits, an answered prayer unmasks them.</summary>
    // orig: R109+0x274E (1A52:05AE) matched; tests: CardSmokeTests
    // C: the band's nature is rolled after the prayer.
    private static void PrayForTheirCure(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (c.Random(100) <= 85)
        {
            if (answer != 0)
            {
                ShowPicture(c, PrayerPicture);
                c.Screen.Text(3);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 25, 0);
            }
            else
            {
                ShowPicture(c, PrayerPicture);
                c.Screen.Text(2);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, -1);
            }
            c.LeaveForTheMap();
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 16);
            SenseTheAmbush(c, menu, 12, withPrayer: false);
            return;
        }
        SpringTheAmbush(c, menu);
    }

    // orig: R109+0x2A18 (1A52:0878) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, 16);
            SenseTheAmbush(c, menu, 12, withPrayer: false);
        }
        else
        {
            ShowPicture(c, PrayerPicture);
            c.Screen.Text(2);
        }
        ShowPicture(c, RefugeesPicture);
        c.Next = 263;
    }

    // orig: R109+0x2B16 (1A52:0976) matched; tests: CardSmokeTests
    private static void Bargain(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, AmbushPicture);
            menu.Block = 6;
            menu.Set(OptionState.Enabled, 7, 8);
            menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5, 6, 9);
            return;
        }
        ShowPicture(c, AmbushPicture);
        c.Screen.Text(13);
        FightTheBandits(c);
    }

    // orig: R109+0x2BF2 (1A52:0A52) matched; tests: CardSmokeTests
    private static void PrayToStopTheAttack(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, PrayerPicture);
            c.Screen.Text(7);
            c.LeaveForTheMap();
            return;
        }
        FightTheBandits(c);
    }

    // orig: R109+0x2CDA (1A52:0B3A) matched; tests: CardSmokeTests
    private static void Surrender(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        c.PassHours(1);
        ShowPicture(c, RoadPicture);
        c.Screen.Text(9);
        c.LeaveForTheMap();
    }

    /// <summary>The bandits; whatever happens, the party moves on.</summary>
    // orig: R109+0x2424 (1A52:0284) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(5) + 3 is drawn before rand_mod(3); then run_battle draws its seed (-1).
    private static void FightTheBandits(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var count = c.Random(5) + 3;
        var groups = c.Random(3) + lore / 4 + 1;
        var result = c.RunBattle(0x30, -1, 7, groups, count, lore > 5 ? 0x16 : 0x12, lore % 5, 1, -1, 0, 0);
        ShowPicture(c, RoadPicture);
        switch (result)
        {
            case 0:
                c.PassHours(1);
                c.Screen.Text(8);
                break;
            case 1:
            case 2:
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                        c.LoseItems(i);
                        c.Variables.Chosen[0] = i;
                    }
                }
                c.PassHours(c.HoursUntil(18));
                c.Screen.Text(11);
                c.PassHours(c.HoursUntil(5));
                break;
            case 3:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                c.Variables.Chosen[0] = c.Leader;
                c.Screen.Text(10);
                break;
            case 4:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                c.Variables.Chosen[0] = c.Leader;
                c.Screen.Text(9);
                break;
        }
        c.LeaveForTheMap();
    }
}
