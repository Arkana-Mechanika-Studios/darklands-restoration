using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW00": along the city walls by day. The party bribes a sentry to look away, sends its
/// best climber up with a grapple, has everyone climb, uses a potion, calls upon a saint, or
/// goes back to the outskirts.
/// </summary>
// orig: R047+0x13CC (1974:000C) matched; tests: CardSmokeTests
// DORMANT: R047+0x1E9A (1974:0ADA), the init, enables option lines 10..19 with the
//   "screwed up." handler R047+0x1F0C; they have no action and no text line.
// UNCERTAIN: options 6..9 (all: text 0 again, then the outskirts) may have no line in the card's text.
public sealed class CityWallsByDay : Card
{
    public const int CardNumber = 0x0E;

    private const int Picture = 280, BribePicture = 274, TopPicture = 248, FallPicture = 65, CaughtPicture = 296, SaintPicture = 273;

    private const int CityOutskirts = 0x04, SideStreet = 0x09, SideStreetAtNight = 0x0A, WallsByNight = 0x0F, PotionSuccess = 0x63;

    /// <summary>A grapple and rope; the event of a city whose walls are watched after an attempt.</summary>
    private const int Grapple = 0x3B, WallsWatched = 0x0F;

    /// <summary>
    /// The best climber found so far and his climbing value (the original's module words): they
    /// are never reset, so a better climber of an earlier visit stays the measure.
    /// </summary>
    // C: DS:5818 starts as 1 (member 1) and DS:581A as 0 in the EXE's data.
    private int _climber = 1;

    private int _climbing;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var states = new OptionState[5];
        Array.Fill(states, OptionState.Enabled);
        if (c.CountItems(Who.Everybody, Grapple) == 0)
        {
            states[1] = OptionState.Shown;
            states[3] = OptionState.Shown;
        }
        else
        {
            states[2] = OptionState.Shown;
        }
        if (c.EventAtHere(WallsWatched))
        {
            states[1] = OptionState.Shown;
            states[2] = OptionState.Shown;
            states[3] = OptionState.Shown;
        }
        if (!c.PartyAllWell())
        {
            states[2] = OptionState.Shown;
        }
        _ = c.HereQuery(2);
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                var value = ClimbingValue(c, i);
                if (_climbing < value)
                {
                    _climber = i;
                    _climbing = value;
                }
            }
        }
        c.Variables.Chosen[0] = _climber;
        c.StandingSeen = c.Reputation(c.Here);
        var bribeValue = (c.HereQuery(2) / 3 + 1) * c.PartySize * 14 / 10;
        if (c.StandingSeen < 0)
        {
            bribeValue = (100 - c.StandingSeen) / 33;
        }
        var bribe = Money.FromPfennigs(bribeValue);
        c.Variables.Money[0] = bribe;
        if (c.State.Party.Purse < bribe)
        {
            states[0] = OptionState.Shown;
        }
        c.SetCardItems(0x64, 0x5F);
        c.SetCardSaints(0x59, 0x61);
        // C: these overwrite the "shown" set above for the potion line.
        states[3] = c.ItemsUsable();
        states[4] = c.SaintsUsable();

        var menu = Menu(c, 0);
        void Close()
        {
            menu[1] = OptionState.Shown;
            menu[2] = OptionState.Shown;
        }
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k < states.Length; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R047+0x17B6 (1974:03F6) + R047+0x1756 (1974:0396) R047 seg 1974 matched
            new MenuLine(() => BribeTheSentry(c, bribe), OptionHint.Saying(NoRisk)),
            // orig: R047+0x18F2 (1974:0532) + R047+0x17EE (1974:042E) R047 seg 1974 matched
            new MenuLine(chance => SendUpTheClimber(c, chance, Close), ClimberChance),
            // orig: R047+0x1AF0 (1974:0730) + R047+0x1904 (1974:0544) R047 seg 1974 matched
            new MenuLine(chance => EveryoneClimbs(c, chance, Close), () => WeakestClimber(c)),
            // orig: R047+0x1C3A (1974:087A) + R047+0x1B52 (1974:0792) R047 seg 1974 matched
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.PotionWorks), () => PotionChance(c, c.MenuSlot)),
            // orig: R047+0x1D1E (1974:095E) + R047+0x1C72 (1974:08B2) R047 seg 1974 matched
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R047+0x1D68 (1974:09A8) + R047+0x1D56 (1974:0996) R047 seg 1974 matched
            new MenuLine(() => { c.Next = CityOutskirts; c.PassHours(0); }, OptionHint.Saying(NoRisk)),
            // orig: R047+0x1DBE (1974:09FE) + R047+0x1DA0 (1974:09E0) R047 seg 1974 matched
            BackOutside(c, TextFrame.BlankPage, () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R047+0x1E16 (1974:0A56) + R047+0x1DF8 (1974:0A38) R047 seg 1974 matched
            BackOutside(c, TextFrame.BlankPage, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R047+0x1E4C (1974:0A8C) + R047+0x1E2E (1974:0A6E) R047 seg 1974 matched
            BackOutside(c, TextFrame.BlankPage, () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R047+0x1E82 (1974:0AC2) + R047+0x1E64 (1974:0AA4) R047 seg 1974 matched
            BackOutside(c, TextFrame.OnPicture, () => OptionHint.ChanceOf(100, c.Leader)));
    }

    /// <summary>A member's climbing value: twice the burdened Agility plus Stealth, halved.</summary>
    private static int ClimbingValue(CardContext c, int member) => (c.EffectiveAgility(member) * 2 + c.Skill(member, 0x0F)) / 2;

    private static int WallsForTheHour(CardContext c) => c.IsDaylight ? CardNumber : WallsByNight;

    private static void BribeTheSentry(CardContext c, Money bribe)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(bribe);
        ShowPicture(c, BribePicture);
        c.Screen.Text(1);
        c.PassHours(1);
        c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
    }

    private OptionHint ClimberChance() => OptionHint.ChanceOf(_climbing, _climber);

    private void SendUpTheClimber(CardContext c, int chance, Action close)
    {
        c.Variables.Chosen[0] = _climber;
        if (c.Random(100) < chance)
        {
            ShowPicture(c, TopPicture);
            c.Screen.Text(3);
            c.PracticeSkill(_climber, 0x0F, 7, 10, 0);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        ShowPicture(c, FallPicture);
        c.Screen.Text(4, TextFrame.OnPicture);
        Show(c, Picture);
        c.Injure(_climber, 0, 1, 10);
        c.PracticeSkill(_climber, 0x0F, 0, 10, 0);
        c.PassHours(1);
        close();
        c.Next = WallsForTheHour(c);
    }

    /// <summary>The weakest able climber's value (999 with nobody able); it also names him in the texts.</summary>
    private static OptionHint WeakestClimber(CardContext c)
    {
        var lowest = 999;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                var value = ClimbingValue(c, i);
                if (value < lowest)
                {
                    c.Variables.Chosen[0] = i;
                    lowest = value;
                }
            }
        }
        return OptionHint.ChanceOf(lowest, OptionHint.WholeParty);
    }

    /// <summary>
    /// Everyone climbs: one roll for the party; if it fails, every member whose climbing value
    /// is not above the roll falls (and is hurt), the others reach the top alone.
    /// </summary>
    private void EveryoneClimbs(CardContext c, int chance, Action close)
    {
        var roll = c.Random(100);
        if (roll < chance)
        {
            ShowPicture(c, TopPicture);
            c.Screen.Text(0x0B);
            c.PracticeSkill(Who.Everybody, 0x0F, 7, 10, 0);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        var named = c.Variables.Chosen[0];
        var madeIt = new bool[5];
        for (var i = 0; i < c.PartySize; i++)
        {
            // C: here every member present counts (status not 0), not only the able ones.
            if (c.Status(i) != CharacterStatus.Empty)
            {
                if (ClimbingValue(c, i) <= roll)
                {
                    c.Variables.Chosen[0] = i;
                    ShowPicture(c, FallPicture);
                    c.Screen.Text(0x0C);
                    Show(c, Picture);
                    c.Injure(i, 0, 1, 10);
                    c.PracticeSkill(i, 0x0F, 7, 2, 0);
                }
                else
                {
                    madeIt[i] = true;
                    c.PracticeSkill(i, 0x0F, 7, 5, 0);
                }
            }
        }
        for (var i = 0; i < c.PartySize; i++)
        {
            if (madeIt[i])
            {
                c.Variables.Chosen[0] = i;
                ShowPicture(c, TopPicture);
                c.Screen.Text(0x0D);
                Show(c, Picture);
            }
        }
        close();
        c.Variables.Chosen[0] = named;
        c.PassHours(1);
        c.Next = WallsForTheHour(c);
    }

    /// <summary>The member's Thrown Weapons + 40 for the first potion, four fifths of it for the second.</summary>
    // orig: R047+0x1F44 (1974:0B84), R047+0x1F74 (1974:0BB4) R047 seg 1974 matched
    private static OptionHint PotionChance(CardContext c, int slot)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var skill = c.Skill(c.MenuMember, 4) + 40;
        // C: (skill + 0x28 << 3) / 10: '+' binds tighter than '<<'.
        var chance = slot == 0 ? skill : (skill << 3) / 10;
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), c.MenuMember);
    }

    private static void UseAPotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) < chance)
        {
            c.Screen.Text(c.MenuSlot == 0 ? 5 : 6);
            c.PracticeSkill(c.MenuMember, 4, 7, 10, 0);
            c.PassHours(0);
            c.Next = PotionSuccess;
        }
        else
        {
            ShowPicture(c, CaughtPicture);
            c.Screen.Text(7);
            c.PracticeSkill(c.MenuMember, 4, 0, 10, 0);
            c.PassHours(1);
            c.Next = WallsForTheHour(c);
        }
        c.Events.Renew(-2, c.Here, 0, WallsWatched, 1, 0x63, 0, 30, 0, 0);
    }

    // orig: R047+0x1FAC (1974:0BEC), R047+0x2036 (1974:0C76) R047 seg 1974 matched
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)), c.MenuMember);
    }

    /// <summary>
    /// A saint helps the party over the wall. Only an answer of exactly 1 counts as help; the
    /// member named in the texts is the one praying, and afterwards the earlier one again.
    /// </summary>
    private void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        // C: the module word of the best climber is reused to keep $ChosenOneName: the climber is lost.
        _climber = c.Variables.Chosen[0];
        c.Variables.Chosen[0] = c.MenuMember;
        if (answer - 1 == 0)
        {
            ShowPicture(c, SaintPicture);
            c.Screen.Text(9);
            c.AdjustReputation(c.Here, 1, 4);
            c.PassHours(0);
            c.Next = SideStreet;
        }
        else
        {
            c.Screen.Text(0x0A);
            c.PassHours(0);
            c.Next = CardNumber;
        }
        c.Variables.Chosen[0] = _climber;
    }

    private static MenuLine BackOutside(CardContext c, TextFrame frame, Func<OptionHint> hint) =>
        new(() =>
            {
                c.Screen.Text(0, frame);
                c.Next = CityOutskirts;
                c.PassHours(0);
            },
            hint);
}
