using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetB01": bandits lie in ambush by the road. The party's best scout may notice them in
/// time; then the party can press on, talk, flush them out with a potion, pray, surrender,
/// slip away or scout them out first. If nobody notices, the bandits spring the ambush.
/// </summary>
// orig: R108+0x2076 (1A3F:0006) matched; tests: CardSmokeTests
// orig: R108+0x2CD8 (1A3F:0C68) matched (registration: item pop-ups R108+0x2D9A/0d5e name the member; saint pop-ups do not)
// DORMANT: R108+0x2E04 and R108+0x2E06 are never called.
public sealed class BanditAmbushOnTheRoad : Card
{
    private const int AmbushPicture = 33;
    private const int RoadPicture = 286;
    private const int BanditsPicture = 287;
    private const int CampPicture = 61;

    // C: the card's picture buffer (image-buffer) changes when the card is reloaded with the ambush.
    private int _cardPicture;

    public override int Number => 258;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x20);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5);
        c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        var scout = c.BestMember;
        c.Variables.Chosen[0] = scout;
        var watchfulness = c.Attribute(scout, (int)Attribute.Perception) + c.Random(10) + c.Skill(scout, (int)Skill.Woodwise) / 2;
        // C: `roll-below(100) > v && !event-begun-flag(0x4F)`: the roll is made first, every time.
        if (c.Random(100) > watchfulness && !c.Events.Started(0x4F))
        {
            _cardPicture = AmbushPicture;
            Show(c, AmbushPicture);
            Ambushed(c, menu);
        }
        else
        {
            _cardPicture = RoadPicture;
            Show(c, RoadPicture);
            menu.Block = 0;
        }
        c.SetCardItems(0x65, 0x66);
        // C: DS:D843 is the terrain under the party.
        if (c.State.PartyTerrain is >= 12 and <= 17)
        {
            c.SetCardSaints(0x36, 0x3D, 0x83, 0x45);
        }
        else
        {
            c.SetCardSaints(0x36, 0x3D, 0x83);
        }
        if (menu[2] == OptionState.Enabled)
        {
            menu[2] = c.ItemsUsable();
        }
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // DORMANT: option line 7 is never offered: no path of this card enables it (option-flag / card-option-enabled stay 0 in every block), so the action R108+0x2C14 and hint R108+0x2C9A never run in play.
        return menu.Run(
            // orig: R108+0x266C (1A3F:05FC) matched; hint R108+0x26AA
            new MenuLine(_ => { ShowPicture(c, BanditsPicture); c.Screen.Text(17); Fight(c, true); }, Unknown),
            // orig: R108+0x26E2 (line 1); hint R108+0x2790 (1A3F:0720) matched
            new MenuLine(p => TalkYourWayPast(c, p), () => TalkHint(c, menu)),
            // orig: R108+0x27EA (line 2); hint R108+0x288A (1A3F:081A) matched
            new MenuLine(p => FlushThemOut(c, p), () => OptionHint.Say("Of course the potion will work!"), () => PotionHint(c)),
            // orig: R108+0x28C2 (line 3); hint R108+0x29EA (1A3F:097A) matched
            new MenuLine(_ => PrayForPeacefulPassage(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R108+0x2A22 (1A3F:09B2) matched; hint R108+0x2A78
            new MenuLine(_ => Surrender(c), () => OptionHint.Say(SuccessAssured)),
            // orig: R108+0x2AB0 (line 5); hint R108+0x2B9C (1A3F:0B2C) matched
            new MenuLine(p => SneakAway(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageEffectiveAgility() + c.PartyAverageSkill((int)Skill.Woodwise) / 2, 1, 99), OptionHint.WholeParty)),
            // orig: R108+0x2BD2 (1A3F:0B62) matched; hint R108+0x2BDC
            new MenuLine(_ => Fight(c, true), () => OptionHint.Say("Enter Combat")),
            // orig: R108+0x2C14 (line 7); hint R108+0x2C9A (1A3F:0C2A) matched
            new MenuLine(p => ScoutThemOut(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageEffectiveAgility() + c.PartyMaxSkill((int)Skill.Woodwise) / 2, 0, 99), OptionHint.WholeParty)),
            // DORMANT: lines 8 and 9 are never offered; actions R108+0x2CD0/0c64 and hints R108+0x2CD2/0c66
            // C: are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The ambush is sprung: talk, pray, surrender or fight.</summary>
    private static void Ambushed(CardContext c, CardMenu menu)
    {
        menu.Set(OptionState.Hidden, 0, 2, 5, 7, 8, 9);
        menu.Set(OptionState.Enabled, 1, 3, 4, 6);
        menu.Block = 1;
    }

    /// <summary>Throwing the potion: by Perception (slot 0: twice less 1; slot 1: twice plus 7).</summary>
    // orig: R108+0x2D9A (1A3F:0D2A) matched; slot 1 R108+0x2DCE
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var perception = c.Attribute(m, (int)Attribute.Perception) * 2;
        return OptionHint.ChanceOf(Math.Clamp(c.MenuSlot == 0 ? perception - 1 : perception + 7, 0, 99), m);
    }

    // C: a side effect of the hint: the most charming member becomes $ChosenOneName.
    private static OptionHint TalkHint(CardContext c, CardMenu menu)
    {
        var talk = c.PartyMaxAttribute((int)Attribute.Charisma) * 4;
        c.Variables.Chosen[0] = c.BestMember;
        talk += c.Skill(c.BestMember, (int)Skill.SpeakCommon);
        if (menu.Block == 14)
        {
            talk *= 2;
        }
        return OptionHint.ChanceOf(Math.Clamp(talk / 5, 0, 99), c.Variables.Chosen[0]);
    }

    // orig: R108+0x26E2 (1A3F:0672) matched; tests: CardSmokeTests
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

    /// <summary>The potion is used up; a good throw scatters the bandits, a bad one brings them on.</summary>
    // orig: R108+0x27EA (1A3F:077A) matched; tests: CardSmokeTests
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
        Fight(c, false);
    }

    /// <summary>A saint brings peace, light, or (St. Hubert in the forest) the great stag; unanswered, the danger remains.</summary>
    // orig: R108+0x28C2 (1A3F:0852) matched; tests: CardSmokeTests
    // C: unlike most cards the prayer does not make the member $ChosenOneName (the texts name the scout).
    private void PrayForPeacefulPassage(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    ShowPicture(c, 289);
                    c.Screen.Text(7);
                    break;
                case 2:
                    ShowPicture(c, 189);
                    c.Screen.Text(8);
                    break;
                case 3:
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(9);
                    break;
            }
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(6);
        ShowPicture(c, _cardPicture);
        c.Next = 258;
    }

    // orig: R108+0x2A22 (1A3F:09B2) matched; tests: CardSmokeTests
    private static void Surrender(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        ShowPicture(c, BanditsPicture);
        c.Screen.Text(10);
        c.PassHours(2);
        c.LeaveForTheMap();
    }

    /// <summary>Slipping away into the gullies (waiting for evening by day), or being caught in the ambush.</summary>
    // orig: R108+0x2AB0 (1A3F:0A40) matched; tests: CardSmokeTests
    private void SneakAway(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (c.IsDaylight)
            {
                c.PassHours(c.HoursUntil(19));
            }
            ShowPicture(c, CampPicture);
            c.Screen.Text(12);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        _cardPicture = AmbushPicture;
        Show(c, AmbushPicture);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        menu[1] = OptionState.Enabled;
        menu.Set(OptionState.Hidden, 0, 2);
        menu[3] = c.SaintsUsable();
        menu.Set(OptionState.Hidden, 5, 7, 8, 9);
        menu.Set(OptionState.Enabled, 4, 6);
        menu.Block = 1;
        c.Next = 258;
    }

    /// <summary>Scouting shows the bandits (all choices open, the potion line without checking for potions); a failure changes nothing.</summary>
    // orig: R108+0x2C14 (1A3F:0BA4) matched; tests: CardSmokeTests
    private static void ScoutThemOut(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, BanditsPicture);
            menu.Block = 14;
            menu.Set(OptionState.Enabled, 0, 1, 2);
            menu[3] = c.SaintsUsable();
            menu.Set(OptionState.Hidden, 7, 8, 9);
            menu.Set(OptionState.Enabled, 4, 5, 6);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 10, 0);
        }
        else
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        }
        c.Next = 258;
    }

    /// <summary>
    /// The bandits (stronger ones when <paramref name="warned"/> is false: the potion missed);
    /// their number and quality grow with the party's lore. Whatever happens, the party moves on.
    /// </summary>
    // orig: R108+0x239A (1A3F:032A) matched; tests: CardSmokeTests
    // C: arguments right to left: in limit-to-range(present, 7, roll-below(5) + lore/3 + 1) the roll-below(5) is drawn
    // first, then roll-below(2) for the fourth argument, then battle-driver draws its seed (-1).
    // C: party-map-column is declared `extern long` in the original, so the call pushes party-map-row and party-map-column: closest-city(party-map-column) receives the party's (x, y); nothing is undefined (checked against the asm).
    private static void Fight(CardContext c, bool warned)
    {
        int NearestCity() => c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);

        var lore = c.PartyLoreLevel();
        var count = Math.Clamp(c.Random(5) + lore / 3 + 1, c.PresentCount(), 7);
        var groups = c.Random(2) + lore / 4 + 1;
        var result = c.RunBattle(warned ? 0x30 : 0x2F, -1, 7, groups, count, lore > 5 ? 0x16 : 0x12, lore % 3 + 1, 1, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, BanditsPicture);
                c.Screen.Text(11);
                c.AdjustReputation(NearestCity(), 0, 5);
                break;
            case 1:
            case 2:
                var away = 0;
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        away++;
                        c.Variables.Chosen[0] = i;
                    }
                }
                c.RejoinAway();
                c.PassHours(c.HoursUntil(18));
                if (away == 1)
                {
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(15);
                }
                else if (away == 0)
                {
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(12);
                }
                else
                {
                    ShowPicture(c, RoadPicture);
                    c.Screen.Text(16);
                }
                c.PassHours(c.HoursUntil(5));
                break;
            case 3:
                c.PassHours(1);
                c.AdjustReputation(NearestCity(), -1, -2);
                c.LoseItems(Who.Everybody);
                c.Variables.Chosen[0] = c.Leader;
                ShowPicture(c, RoadPicture);
                c.Screen.Text(13);
                break;
            case 4:
                c.PassHours(4);
                c.AdjustReputation(NearestCity(), -1, -2);
                c.LoseItems(Who.Everybody);
                ShowPicture(c, BanditsPicture);
                c.Screen.Text(10);
                break;
        }
        c.LeaveForTheMap();
    }
}
