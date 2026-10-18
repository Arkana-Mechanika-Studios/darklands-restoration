namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol00": by day, at the house of the leader of one side of the city's revolt (the rebels
/// or the government, depending on whom the party came for). The party gathers gossip, asks
/// politely for an interview, brashly offers its services, picks the lock, uses a potion or
/// leaves; a failure makes the house wary for a long time.
/// </summary>
// orig: R048+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RevolutionHouseByDay : Card
{
    public const int CardNumber = 0x10;

    private const int Picture = 0;

    private const int Night = 0x11, Outskirts = 0x01, SideStreet = 0x09, SideStreetAtNight = 0x0A, Fight = 0x5F,
        RevoltAnswers = 0x74, RevoltAnswersSkipped = 0x79;

    /// <summary>
    /// The module flag that picks the option block 5 instead of 0. Nothing in the overlay sets it
    /// (it starts as 0 in the EXE), so block 0 is always used.
    /// </summary>
    // C: DS:5835 = 0 in the EXE's initial data; never written by the overlay.
    private readonly bool _alternateOptionBlock = false;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var states = new OptionState[10];
        states[0] = states[1] = states[2] = states[3] = states[4] = states[9] = OptionState.Enabled;
        c.StandingSeen = c.HereQuery(0);
        // C: loc_query(cur_loc, 0x21) is called once here and its result dropped.
        var firstSide = RevolutionSupport.AtFirstSidesHouse(c);
        var events = c.Events;
        if (firstSide)
        {
            if (events.Fact(RevolutionSupport.Politics, 0x1B, c.Here))
            {
                states[0] = OptionState.Shown;
            }
            if (events.Fact(RevolutionSupport.Politics, 0x1F, c.Here))
            {
                states[2] = OptionState.Shown;
            }
            if (events.Fact(RevolutionSupport.Politics, 0x21, c.Here))
            {
                states[3] = states[4] = OptionState.Shown;
            }
            // orig: R029+0x5CC4 (1367:0DB4) matched (random_name via the shared c.RandomName)
            c.RandomName(c.City.Ordinal + 0x0C, c.HereQuery(0x21) % 5 == 0 ? 1 : 0, 0);
        }
        else
        {
            if (events.Fact(RevolutionSupport.Politics, 0x1C, c.Here))
            {
                states[0] = OptionState.Shown;
            }
            if (events.Fact(RevolutionSupport.Politics, 0x20, c.Here))
            {
                states[2] = OptionState.Shown;
            }
            if (events.Fact(RevolutionSupport.Politics, 0x22, c.Here))
            {
                states[3] = states[4] = OptionState.Shown;
            }
            // orig: R029+0x5CC4 (1367:0DB4) matched (random_name via the shared c.RandomName)
            c.RandomName(c.City.Ordinal + 0x16, 2, 0);
        }
        // The side that holds the city: the house is closed to the party's own kind.
        if (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == -1 && firstSide)
        {
            states[2] = states[3] = states[4] = states[5] = OptionState.Hidden;
        }
        else if (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == 1 && !firstSide)
        {
            states[2] = states[3] = states[4] = states[5] = OptionState.Hidden;
        }
        else if ((events.FactValue(RevolutionSupport.Politics, 0, c.Here) == 1 && firstSide)
            || (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == -1 && !firstSide))
        {
            states[0] = states[1] = states[2] = OptionState.Hidden;
        }
        // DORMANT: R048+0x0B5A (1838:0B5A) only resets the option flags and the default handlers (screwed up.,
        // success assured for the potion pop-up's four slots); the loop below overwrites them all.
        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        // C: the potion option's state is replaced by items_usable() whatever the branches above decided.
        states[4] = c.ItemsUsable();

        _menu = Menu(c, _alternateOptionBlock ? 5 : 0);
        _menu.Enable(0, 1, 2, 3, 4, 9);
        _menu[0] = states[0];
        _menu[1] = states[1];
        _menu[2] = states[2];
        _menu[3] = states[3];
        _menu[4] = states[4];
        _menu[9] = states[9];
        var lines = new MenuLine[]
        {
            new MenuLine(chance => GatherGossip(c, firstSide, chance), () => GossipChance(c)),
            new MenuLine(chance => AskForAnInterview(c, firstSide, chance), () => InterviewChance(c, firstSide)),
            new MenuLine(chance => OfferYourServices(c, firstSide, chance), () => ServicesChance(c)),
            new MenuLine(chance => PickTheLock(c, firstSide, chance), () => LockChance(c)),
            new MenuLine(chance => UseAPotion(c, firstSide, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionPopupHint(c)),
            // DORMANT: option 5 (draw weapons and storm the house) is never enabled; the hint says "Enter Combat".
            // orig: R048+0x09F8 (1838:09F8) matched
            // orig: R048+0x0A00 (1838:0A00) matched
            new MenuLine(() => c.Next = Fight, OptionHint.Saying(CardTexts.EnterCombat)),
            // DORMANT: options 6..8 have no text line and are never enabled; they would only let 0 hours pass.
            // orig: R048+0x0A38 (1838:0A38) matched
            // orig: R048+0x0A44 (1838:0A44) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x0A7C (1838:0A7C) matched
            // orig: R048+0x0A88 (1838:0A88) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x0AC0 (1838:0AC0) matched
            // orig: R048+0x0ACC (1838:0ACC) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x0B04 (1838:0B04) matched
            // orig: R048+0x0B22 (1838:0B22) matched
            new MenuLine(() =>
                {
                    c.PassHours(0);
                    c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                },
                OptionHint.Saying(SuccessAssured)),
        };
        // DORMANT: R048+0x0BBC (1838:0BBC) (option lines 10..19, "screwed up.") and R048+0x0CE4 (1838:0CE4)
        // (the saint pop-up's hint) are unreachable: the card has no more than ten option lines and no saints.
        c.Next = Number;
        return _menu.Run(lines);
    }

    private int NightOrDay(CardContext c) => c.IsDaylight ? CardNumber : Night;

    private static void WhisperEvent(CardContext c, bool firstSide, int firstType, int hours) =>
        RevolutionSupport.NewRevoltEvent(c, firstSide ? firstType : firstType + 1, hours);

    // ---- option 0: gather gossip about the house's owner ---------------------------------

    /// <summary>The leader's Charisma, Speak Common and the party's best Streetwise, with the standing's size, over three.</summary>
    // orig: R048+0x04A2 (1838:04A2) matched
    private static OptionHint GossipChance(CardContext c) =>
        OptionHint.ChanceOf(
            Math.Clamp((c.Attribute(c.Leader, 5) + (c.Skill(c.Leader, 0x0A) + c.PartyMaxSkill(0x10)) + Math.Abs(c.StandingSeen)) / 3, 0, 99),
            c.Leader);

    // orig: R048+0x03D6 (1838:03D6) matched
    private void GatherGossip(CardContext c, bool firstSide, int chance)
    {
        // C: advance_hours(rand_mod(2) + 1) runs before the success draw.
        c.PassHours(c.Random(2) + 1);
        if (c.Random(100) <= chance)
        {
            if (!firstSide)
            {
                c.Screen.Text(2, TextFrame.BlankPage);
                c.Screen.Text(0x0C, TextFrame.BlankPage);
            }
            // BUG: no else: the second house's text 2 is then shown a second time, as in the original.
            if (c.HereQuery(0x29) == 2)
            {
                c.Screen.Text(2, TextFrame.BlankPage);
                c.Screen.Text(3, TextFrame.BlankPage);
            }
            else
            {
                c.Screen.Text(2, TextFrame.BlankPage);
                c.Screen.Text(4, TextFrame.BlankPage);
            }
        }
        else
        {
            c.Screen.Text(1, TextFrame.BlankPage);
        }
        _menu[0] = OptionState.Shown;
        WhisperEvent(c, firstSide, 0x1B, 0x5A0);
        c.Next = NightOrDay(c);
    }

    // ---- option 1: ask politely for an interview -----------------------------------------

    /// <summary>Certain for the side that holds the city; hopeless for the other; else Charisma and Speak Common with the standing's size, over two.</summary>
    // orig: R048+0x05F0 (1838:05F0) matched
    private static OptionHint InterviewChance(CardContext c, bool firstSide)
    {
        var holder = c.Events.FactValue(RevolutionSupport.Politics, 0, c.Here);
        if (holder == -1 && firstSide)
        {
            return OptionHint.Say(SuccessAssured);
        }
        if (holder == 1 && !firstSide)
        {
            return OptionHint.Say(SuccessAssured);
        }
        if (holder == 1 && firstSide)
        {
            return OptionHint.ChanceOf(0, OptionHint.WholeParty);
        }
        if (holder == -1 && !firstSide)
        {
            return OptionHint.ChanceOf(0, OptionHint.WholeParty);
        }
        return OptionHint.ChanceOf(
            Math.Clamp((c.Attribute(c.Leader, 5) + c.Skill(c.Leader, 0x0A) + Math.Abs(c.StandingSeen)) / 2, 0, 99), c.Leader);
    }

    // orig: R048+0x0506 (1838:0506) matched
    private void AskForAnInterview(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(1);
        if (c.Random(100) <= chance)
        {
            var holder = c.Events.FactValue(RevolutionSupport.Politics, 0, c.Here);
            if ((holder == -1 && firstSide) || (holder == 1 && !firstSide))
            {
                c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
                c.Next = RevoltAnswersSkipped;
                return;
            }
            c.Screen.Text(5, TextFrame.BlankPage);
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = RevoltAnswers;
            return;
        }
        WhisperEvent(c, firstSide, 0x1D, 0x0C);
        c.Screen.Text(6);
        c.Next = NightOrDay(c);
        _menu[1] = OptionState.Shown;
    }

    // ---- option 2: brashly offer the party's services ------------------------------------

    /// <summary>Fame, Strength and the best weapon skill of the leader, with the standing's size, over three.</summary>
    // orig: R048+0x077A (1838:077A) matched
    private static OptionHint ServicesChance(CardContext c) =>
        OptionHint.ChanceOf(
            Math.Clamp((c.PartyGet(4) + (c.Attribute(c.Leader, 1) + c.BestWeaponSkill(c.Leader)) + Math.Abs(c.StandingSeen)) / 3, 0, 99),
            c.Leader);

    // orig: R048+0x06DE (1838:06DE) matched
    private void OfferYourServices(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(0);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(5, TextFrame.BlankPage);
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = RevoltAnswers;
            return;
        }
        _menu[2] = OptionState.Shown;
        WhisperEvent(c, firstSide, 0x1F, 0x5A0);
        c.Screen.Text(0x0B);
        c.Next = NightOrDay(c);
    }

    // ---- option 3: pick the lock and sneak in --------------------------------------------

    /// <summary>The party's best Artifice and Streetwise, less what its Stealth lacks of 100.</summary>
    // orig: R048+0x08C0 (1838:08C0) matched
    private static OptionHint LockChance(CardContext c)
    {
        var lacking = 100;
        lacking -= c.PartyAverageSkill(0x0F);
        // C: the asm asks for the best Artifice (0x0E) first and the best Streetwise (0x10) second, so
        // the member shown is the best at Streetwise.
        var artifice = c.PartyMaxSkill(0x0E);
        var streetwise = c.PartyMaxSkill(0x10);
        return OptionHint.ChanceOf(Math.Clamp((artifice + streetwise) / 2 - lacking, 0, 99), c.BestMember);
    }

    // orig: R048+0x07E0 (1838:07E0) matched
    private void PickTheLock(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(1);
        c.PartyMaxSkill(0x0E);
        c.Variables.Chosen[0] = c.BestMember;
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(c.Variables.Chosen[0], 0x0E, 1, 0x0A, 0);
            c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 1, 0x0A, 0);
            c.Screen.Text(7);
            c.Next = Fight;
            return;
        }
        c.PracticeSkill(c.Variables.Chosen[0], 0x0E, 0, 0x0A, 0);
        c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 0, 0x0A, 0);
        c.AdjustReputation(c.Here, -8, -8);
        _menu[3] = OptionState.Shown;
        _menu[4] = OptionState.Shown;
        WhisperEvent(c, firstSide, 0x21, 0x1A);
        c.Screen.Text(8);
        c.Next = Outskirts;
    }

    // ---- option 4: a potion to distract the doormen --------------------------------------

    /// <summary>The potion pop-up's hint for every slot: success assured for the chosen member (it also names the member in the texts).</summary>
    // orig: R048+0x0BF4 (1838:0BF4) matched
    // orig: R048+0x0C30 (1838:0C30) matched
    // orig: R048+0x0C6C (1838:0C6C) matched
    // orig: R048+0x0CA8 (1838:0CA8) matched
    private static OptionHint PotionPopupHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.Say(SuccessAssured, c.MenuMember);
    }

    // orig: R048+0x09C0 (1838:09C0) matched
    // orig: R048+0x0916 (1838:0916) matched
    // BUG: the pop-up handler returns -1 as the "chance", and the draw (0..99) is never at most -1:
    // the potion always fails, the success branch below never runs.
    private void UseAPotion(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(1);
        if (c.Random(100) <= chance)
        {
            // DORMANT: reached only with a chance of 0 or more, which the pop-up never gives.
            c.PartyMinSkill(0x0F);
            c.PracticeSkill(c.BestMember, 0x0F, 1, 0x0A, -1);
            c.Screen.Text(9);
            c.Next = Fight;
            return;
        }
        WhisperEvent(c, firstSide, 0x21, 0x1A);
        _menu[3] = OptionState.Shown;
        _menu[4] = OptionState.Shown;
        c.Screen.Text(0x0A);
        c.Next = NightOrDay(c);
    }
}
