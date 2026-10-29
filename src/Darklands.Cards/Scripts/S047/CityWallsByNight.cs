using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW01": along the city walls by night. The best climber goes up with a grapple, or
/// everyone climbs, or the strongest forces a sewer grating; a potion, a saint, or the walk
/// back to the outskirts.
/// </summary>
// orig: R047+0x20C0 (1A44:0000) matched; tests: CardSmokeTests
// DORMANT: R047+0x2BDA (1A44:0B1A), the init, enables option lines 10..19 with the
//   "screwed up." handler R047+0x2C58; they have no action and no text line.
// C: options 6..9 (text 0 again, then the outskirts) may have no line in the card's text.
public sealed class CityWallsByNight : Card
{
    public const int CardNumber = 0x0F;

    private const int Picture = 0, TopPicture = 273, FallPicture = 66, SewerPicture = 42, GratingPicture = 43,
        PotionPicture = 275, EludePicture = 59, StairsPicture = 13;

    private const int CityOutskirts = 0x04, SideStreet = 0x09, SideStreetAtNight = 0x0A, WallsByDay = 0x0E;

    /// <summary>A grapple and rope; the event of a city whose sewer grating has been tried.</summary>
    private const int Grapple = 0x3B, SewerWatched = 0x10;

    /// <summary>The climber (the original's module word, DS:5828; 1 at the start in the EXE's data).</summary>
    private int _climber = 1;

    /// <summary>
    /// The climbing value the options compare with (DS:582A, 0 at the start). The arrival does
    /// not set it: only the everybody-climbs action does, leaving the last member's value.
    /// </summary>
    // BUG: the arrival finds the best climber with a local maximum but never stores his value, so the
    //   grapple option's chance is a stale value from the last time everybody climbed (0 at first).
    private int _climbing;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var states = new OptionState[5];
        Array.Fill(states, OptionState.Enabled);
        if (c.CountItems(Who.Everybody, Grapple) == 0)
        {
            states[0] = OptionState.Shown;
        }
        if (!c.PartyAllWell())
        {
            states[1] = OptionState.Shown;
        }
        var best = 0;
        // C: the search starts at member 1: the first member is never the climber.
        for (var i = 1; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                var value = ClimbingValue(c, i);
                if (value > best)
                {
                    _climber = i;
                    best = value;
                }
            }
        }
        c.Variables.Chosen[0] = _climber;
        if (c.EventAtHere(SewerWatched))
        {
            states[2] = OptionState.Shown;
        }
        c.StandingSeen = c.Reputation(c.Here);
        c.SetCardItems(0x63, 0x66);
        c.SetCardSaints(0x15, 0x59, 0x61);
        states[3] = c.ItemsUsable();
        states[4] = c.SaintsUsable();

        var menu = Menu(c, 0);
        void Close()
        {
            menu[0] = OptionState.Shown;
            menu[1] = OptionState.Shown;
        }
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k < states.Length; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R047+0x24A2 (1A44:03E2) + R047+0x23A4 (1A44:02E4) R047 seg 1A44 matched
            new MenuLine(chance => SendUpTheClimber(c, chance, Close), ClimberChance),
            // orig: R047+0x26B0 (1A44:05F0) + R047+0x24B4 (1A44:03F4) R047 seg 1A44 matched
            // (R047+0x24B4 read from the disassembly)
            new MenuLine(chance => EveryoneClimbs(c, chance, Close), () => EveryoneChance(c)),
            // orig: R047+0x2838 (1A44:0778) + R047+0x271A (1A44:065A) R047 seg 1A44 matched
            new MenuLine(chance => ForceTheGrating(c, chance), () => GratingChance(c)),
            // orig: R047+0x296C (1A44:08AC) + R047+0x288E (1A44:07CE) R047 seg 1A44 matched
            new MenuLine(_ => UseAPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            // orig: R047+0x2A7E (1A44:09BE) + R047+0x29A4 (1A44:08E4) R047 seg 1A44 matched
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R047+0x2AC8 (1A44:0A08) + R047+0x2AB6 (1A44:09F6) R047 seg 1A44 matched
            new MenuLine(() => { c.PassHours(0); c.Next = CityOutskirts; }, () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R047+0x2B20 (1A44:0A60) + R047+0x2B02 (1A44:0A42) R047 seg 1A44 matched
            BackOutside(c),
            // orig: R047+0x2B56 (1A44:0A96) + R047+0x2B38 (1A44:0A78) R047 seg 1A44 matched
            BackOutside(c),
            // orig: R047+0x2B8C (1A44:0ACC) + R047+0x2B6E (1A44:0AAE) R047 seg 1A44 matched
            BackOutside(c),
            // orig: R047+0x2BC2 (1A44:0B02) + R047+0x2BA4 (1A44:0AE4) R047 seg 1A44 matched
            BackOutside(c));
    }

    /// <summary>Twice the burdened Agility plus Stealth, halved.</summary>
    private static int ClimbingValue(CardContext c, int member) => (c.EffectiveAgility(member) * 2 + c.Skill(member, 0x0F)) / 2;

    private static int WallsForTheHour(CardContext c) => c.IsDaylight ? WallsByDay : CardNumber;

    private OptionHint ClimberChance() => OptionHint.ChanceOf(_climbing, _climber);

    private void SendUpTheClimber(CardContext c, int chance, Action close)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, TopPicture);
            c.Screen.Text(1);
            c.PracticeSkill(_climber, 0x0F, 7, 10, 0);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        ShowPicture(c, FallPicture);
        c.Screen.Text(2, TextFrame.BlankPage);
        Show(c, Picture);
        c.Injure(_climber, 0, 1, 10);
        c.PracticeSkill(_climber, 0x0F, 0, 10, 0);
        c.PassHours(1);
        close();
        c.Next = WallsForTheHour(c);
    }

    /// <summary>
    /// The weakest climber below the stored climbing value (999 when none is): the comparison
    /// is with the stored value, not with the lowest found so far (original bug), so the last
    /// able member below it wins. It also names him in the texts.
    /// </summary>
    // BUG: compares each member with DS:582A instead of the running minimum.
    private OptionHint EveryoneChance(CardContext c)
    {
        var chance = 999;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                var value = (c.Skill(i, 0x0F) + (c.EffectiveAgility(i) << 1)) / 2;
                if (_climbing > value)
                {
                    c.Variables.Chosen[0] = i;
                    chance = value;
                }
            }
        }
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>One roll for everybody; on a miss each member whose value is not above the roll falls, the others make it alone.</summary>
    private void EveryoneClimbs(CardContext c, int chance, Action close)
    {
        var roll = c.Random(100);
        if (roll < chance)
        {
            ShowPicture(c, TopPicture);
            c.Screen.Text(3);
            Show(c, Picture);
            c.PracticeSkill(Who.Everybody, 0x0F, 7, 10, 0);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        var named = c.Variables.Chosen[0];
        var madeIt = new bool[5];
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                // C: the module word keeps each member's value in turn (the last one stays).
                _climbing = ClimbingValue(c, i);
                if (roll >= _climbing)
                {
                    c.Variables.Chosen[0] = i;
                    ShowPicture(c, FallPicture);
                    c.Screen.Text(0x0B);
                    Show(c, Picture);
                    c.Injure(i, 0, 1, 10);
                }
                else
                {
                    madeIt[i] = true;
                }
                c.PracticeSkill(i, 0x0F, 0, 5, 0);
            }
        }
        for (var i = 0; i < c.PartySize; i++)
        {
            if (madeIt[i])
            {
                c.Variables.Chosen[0] = i;
                ShowPicture(c, FallPicture);
                c.Screen.Text(0x0C);
                Show(c, Picture);
            }
        }
        c.Variables.Chosen[0] = named;
        c.PassHours(1);
        close();
        c.Next = WallsForTheHour(c);
    }

    /// <summary>The highest Strength of any slot (status not checked), times 1.6; it names that member.</summary>
    private static OptionHint GratingChance(CardContext c)
    {
        var strongest = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Attribute(i, 1) > strongest)
            {
                strongest = c.Attribute(i, 1);
                c.Variables.Chosen[0] = i;
            }
        }
        return OptionHint.ChanceOf((strongest << 4) / 10, c.Variables.Chosen[0]);
    }

    /// <summary>The strongest strains at the grating (losing 3 Endurance); through it into the sewers, or caught at it.</summary>
    private static void ForceTheGrating(CardContext c, int chance)
    {
        var member = c.Variables.Chosen[0];
        c.AddAttribute(member, 0, -3);
        if (c.Random(100) < chance)
        {
            ShowPicture(c, SewerPicture);
            c.Screen.Text(4);
            if (c.Reputation(c.Here) > 0)
            {
                c.AdjustReputation(c.Here, -2, -4);
            }
            c.PassHours(c.HereQuery(2) / 3);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        c.Events.Renew(member, c.Here, 0, SewerWatched, 3, 0x63, 0, 500, 0, 0);
        ShowPicture(c, GratingPicture);
        c.Screen.Text(5);
        c.PassHours(1);
        c.Next = WallsForTheHour(c);
    }

    // orig: R047+0x2C90 (1A44:0BD0), R047+0x2CCC (1A44:0C0C) R047 seg 1A44 matched
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.Say(SuccessAssured, c.MenuMember);
    }

    /// <summary>The potions always work: the first gets the party over, the second lets it slip in at the cost of standing.</summary>
    private static void UseAPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.MenuSlot == 0)
        {
            ShowPicture(c, PotionPicture);
            c.Screen.Text(6);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        ShowPicture(c, EludePicture);
        c.Screen.Text(7);
        if (c.Reputation(c.Here) > -10)
        {
            c.AdjustReputation(c.Here, -1, -3);
        }
        else
        {
            c.AdjustReputation(c.Here, -2, -4);
        }
        c.PassHours(0);
        c.Next = SideStreetAtNight;
    }

    // orig: R047+0x2D08 (1A44:0C48), R047+0x2D92 (1A44:0CD2),
    //   R047+0x2E1C (1A44:0D5C) R047 seg 1A44 matched
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)), c.MenuMember);
    }

    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            if (c.MenuSlot == 0)
            {
                ShowPicture(c, StairsPicture);
                c.Screen.Text(8);
            }
            else
            {
                ShowPicture(c, TopPicture);
                c.Screen.Text(9);
            }
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        c.Screen.Text(0x0A);
        c.PassHours(1);
        c.Next = WallsForTheHour(c);
    }

    private static MenuLine BackOutside(CardContext c) =>
        new(() =>
            {
                c.Screen.Text(0);
                c.Next = CityOutskirts;
                c.PassHours(0);
            },
            () => OptionHint.ChanceOf(100, c.Leader));
}
