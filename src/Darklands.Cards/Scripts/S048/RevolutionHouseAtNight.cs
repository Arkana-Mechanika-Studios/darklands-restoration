namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol01": at night, at the house of the leader of one side of the city's revolt. The party
/// watches the house through the night, knocks at the back door, picks a lock, uses a potion,
/// storms it (when it opposes the house) or leaves.
/// </summary>
// orig: R048+0x0D6E (190E:000E) matched; tests: CardSmokeTests
public sealed class RevolutionHouseAtNight : Card
{
    public const int CardNumber = 0x11;

    private const int Picture = 0;

    private const int ByDay = 0x10, Outskirts = 0x01, SideStreet = 0x09, SideStreetAtNight = 0x0A, Fight = 0x5F,
        RevoltAnswers = 0x74, RevoltAnswersSkipped = 0x79, Watch = 0x3C;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var states = new OptionState[10];
        states[0] = states[1] = states[2] = states[3] = states[9] = OptionState.Enabled;
        var firstSide = RevolutionSupport.AtFirstSidesHouse(c);
        var events = c.Events;
        if (firstSide ? events.Fact(RevolutionSupport.Politics, 0x21, c.Here) : events.Fact(RevolutionSupport.Politics, 0x22, c.Here))
        {
            states[2] = states[3] = OptionState.Shown;
        }
        if (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == -1 && firstSide)
        {
            states[2] = states[3] = states[4] = OptionState.Hidden;
        }
        else if (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == 1 && !firstSide)
        {
            states[2] = states[3] = states[4] = OptionState.Hidden;
        }
        else if ((events.FactValue(RevolutionSupport.Politics, 0, c.Here) == 1 && firstSide)
            || (events.FactValue(RevolutionSupport.Politics, 0, c.Here) == -1 && !firstSide))
        {
            states[1] = OptionState.Hidden;
            states[4] = OptionState.Enabled;
        }
        // DORMANT: R048+0x1726 (190E:09C6) only resets the option flags and the default handlers (screwed up., success
        // assured for the pop-ups) and clears card_saints/card_items; the loop below overwrites the flags.
        c.SetCardItems(0x5F, 0x65, 0x66);
        // C: the potion option's state is replaced by items_usable() whatever the branches above decided.
        states[3] = c.ItemsUsable();

        _menu = Menu(c, 0);
        _menu.Enable(0, 1, 2, 3, 4, 9);
        _menu[0] = states[0];
        _menu[1] = states[1];
        _menu[2] = states[2];
        _menu[3] = states[3];
        _menu[4] = states[4];
        _menu[9] = states[9];
        var lines = new MenuLine[]
        {
            new MenuLine(chance => WatchTheHouse(c, chance), () => WatchChance(c)),
            new MenuLine(chance => KnockAtTheBackDoor(c, firstSide, chance), () => KnockChance(c, firstSide)),
            new MenuLine(chance => PickTheLock(c, firstSide, chance), () => LockChance(c)),
            new MenuLine(chance => UseAPotion(c, firstSide, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionPopupHint(c)),
            // orig: R048+0x1574 (190E:0814) matched
            // orig: R048+0x1588 (190E:0828) matched
            new MenuLine(() =>
                {
                    c.Screen.Text(9);
                    c.Next = Fight;
                },
                OptionHint.Saying(CardTexts.Unknown)),
            // DORMANT: options 5..8 have no text line and are never enabled; they would only let 0 hours pass.
            // orig: R048+0x15C0 (190E:0860) matched
            // orig: R048+0x15CC (190E:086C) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x1604 (190E:08A4) matched
            // orig: R048+0x1610 (190E:08B0) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x1648 (190E:08E8) matched
            // orig: R048+0x1654 (190E:08F4) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x168C (190E:092C) matched
            // orig: R048+0x1698 (190E:0938) matched
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R048+0x16D0 (190E:0970) matched
            // orig: R048+0x16EE (190E:098E) matched
            new MenuLine(() =>
                {
                    c.PassHours(1);
                    c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                },
                OptionHint.Saying(SuccessAssured)),
        };
        // DORMANT: R048+0x17C8 (190E:0A68) (option lines 10..19, "screwed up.") and the saint pop-up's hints
        // R048+0x18F0 (190E:0B90), R048+0x197A (190E:0C1A), R048+0x1A04 (190E:0CA4), R048+0x1A8E (190E:0D2E) are unreachable:
        // the card has no more than ten option lines and card_saints stays empty (the hint would show the saint chance).
        c.Next = Number;
        return _menu.Run(lines);
    }

    private int NightOrDay(CardContext c) => c.IsDaylight ? ByDay : CardNumber;

    private static void WhisperEvent(CardContext c, bool firstSide, int firstType, int hours) =>
        RevolutionSupport.NewRevoltEvent(c, firstSide ? firstType : firstType + 1, hours);

    // ---- option 0: watch the house until dawn --------------------------------------------

    /// <summary>The party's best Streetwise and Stealth, averaged.</summary>
    // orig: R048+0x1104 (190E:03A4) matched
    private static OptionHint WatchChance(CardContext c)
    {
        // C: Streetwise (0x10) is asked first, then Stealth (0x0F).
        var streetwise = c.PartyMaxSkill(0x10);
        var stealth = c.PartyMaxSkill(0x0F);
        return OptionHint.ChanceOf(Math.Clamp((streetwise + stealth) / 2, 0, 99), OptionHint.WholeParty);
    }

    // orig: R048+0x104C (190E:02EC) matched
    private void WatchTheHouse(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.WaitUntilHour(5);
            c.Screen.Text(1, TextFrame.BlankPage);
            c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 1, 8, 0);
            c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x10, 1, 7, -1);
            c.Next = NightOrDay(c);
            return;
        }
        c.WaitUntilHour(5);
        c.AdjustReputation(c.Here, -8, -8);
        c.Screen.Text(2, TextFrame.BlankPage);
        c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 0, 8, 0);
        c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x10, 0, 7, -1);
        c.Next = Watch;
    }

    // ---- option 1: knock at the back door ------------------------------------------------

    /// <summary>
    /// Fame over 20 less the strength of the party's side, plus half of the standing's size, Charisma
    /// and Speak Common.
    /// </summary>
    // orig: R048+0x124E (190E:04EE) matched (checked against asm)
    private static OptionHint KnockChance(CardContext c, bool firstSide)
    {
        var holder = c.Events.FactValue(RevolutionSupport.Politics, 0, c.Here);
        if ((holder == -1 && firstSide) || (holder == 1 && !firstSide))
        {
            // BUG: the original copies "Success is assured." but returns 0 instead of -1, so the
            // hint shows a chance of 0 (and the member is whatever the register held).
            // C: the member of the hint (stale register) is shown as the whole party.
            return OptionHint.ChanceOf(0, OptionHint.WholeParty);
        }
        var strength = firstSide ? c.SideStrength(-1) : c.SideStrength(1);
        // C: Charisma (-1 = leader), then the standing, then Speak Common.
        var bonus = (Math.Abs(c.HereQuery(0)) + c.Attribute(c.Leader, 5) + c.Skill(c.Leader, 0x0A)) / 2;
        var raw = c.PartyGet(4) / 0x14 - strength + bonus;
        // C: the handler is declared int (it returns only r.a in AX): the hint's member is DX as clamp_range(0, 0x63, v) leaves it,
        // its hi (0x63) when v is above it, else its lo (0) (checked against asm R029 1367:0066; card oracle: CardOracleTests).
        return OptionHint.ChanceOf(Math.Clamp(raw, 0, 99), raw > 0x63 ? 0x63 : 0);
    }

    // orig: R048+0x113E (190E:03DE) matched
    private void KnockAtTheBackDoor(CardContext c, bool firstSide, int chance)
    {
        var holder = c.Events.FactValue(RevolutionSupport.Politics, 0, c.Here);
        if (holder == -1 && firstSide)
        {
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = RevoltAnswersSkipped;
            return;
        }
        if (holder == 1 && !firstSide)
        {
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = RevoltAnswersSkipped;
            return;
        }
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 1, 1);
            c.Screen.Text(3);
            c.PracticeSkill(c.Leader, 0x0A, 1, 7, 0);
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = RevoltAnswers;
            return;
        }
        // BUG(?): the standing rises by one on a failure too, as in the original.
        c.AdjustReputation(c.Here, 1, 1);
        WhisperEvent(c, firstSide, 0x1D, 0x1E);
        c.PracticeSkill(c.Leader, 0x0A, 0, 7, -1);
        c.Screen.Text(4);
        c.Next = CardNumber;
    }

    // ---- option 2: pick the lock ---------------------------------------------------------

    /// <summary>The party's best Streetwise and Artifice, less what its Stealth lacks of 125.</summary>
    // orig: R048+0x1400 (190E:06A0) matched (checked against asm)
    private static OptionHint LockChance(CardContext c)
    {
        var lacking = 0x7D;
        lacking -= c.PartyAverageSkill(0x0F);
        // C: the asm asks for the best Artifice (0x0E) first and the best Streetwise (0x10) second, so
        // the member shown is the best at Streetwise.
        var artifice = c.PartyMaxSkill(0x0E);
        var streetwise = c.PartyMaxSkill(0x10);
        return OptionHint.ChanceOf(Math.Clamp((streetwise + artifice) / 2 - lacking, 0, 99), c.BestMember);
    }

    // orig: R048+0x1320 (190E:05C0) matched
    private void PickTheLock(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(1);
        c.PartyMaxSkill(0x0E);
        c.Variables.Chosen[0] = c.BestMember;
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(c.Variables.Chosen[0], 0x0E, 1, 0x0A, 0);
            c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 1, 0x0A, -1);
            c.Screen.Text(5);
            c.Next = Fight;
            return;
        }
        c.PracticeSkill(c.Variables.Chosen[0], 0x0E, 0, 0x0A, 0);
        c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 0, 0x0A, -1);
        c.AdjustReputation(c.Here, -4, -4);
        _menu[3] = OptionState.Shown;
        _menu[4] = OptionState.Shown;
        WhisperEvent(c, firstSide, 0x21, 0x1A);
        c.Screen.Text(6);
        c.Next = Outskirts;
    }

    // ---- option 3: a potion as a diversion -----------------------------------------------

    /// <summary>The potion pop-up's hint for every slot: success assured for the chosen member (it also names the member in the texts).</summary>
    // orig: R048+0x1800 (190E:0AA0) matched
    // orig: R048+0x183C (190E:0ADC) matched
    // orig: R048+0x1878 (190E:0B18) matched
    // orig: R048+0x18B4 (190E:0B54) matched
    private static OptionHint PotionPopupHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.Say(SuccessAssured, c.MenuMember);
    }

    // orig: R048+0x153C (190E:07DC) matched
    // orig: R048+0x1456 (190E:06F6) matched
    // BUG: the pop-up handler returns -1 as the "chance", and the draw (0..99) is never at most -1:
    // the potion always fails (and, as twice the chance is below the draw, the party then goes to card 1).
    private void UseAPotion(CardContext c, bool firstSide, int chance)
    {
        c.PassHours(1);
        var roll = c.Random(100);
        if (roll <= chance)
        {
            // DORMANT: reached only with a chance of 0 or more, which the pop-up never gives.
            c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 1, 0x0A, -1);
            c.Screen.Text(7);
            c.Next = Fight;
            return;
        }
        WhisperEvent(c, firstSide, 0x21, 0x1A);
        _menu[3] = OptionState.Shown;
        _menu[4] = OptionState.Shown;
        c.Screen.Text(8);
        c.PracticeSkill(Darklands.Core.Characters.Who.Everybody, 0x0F, 0, 0x0A, 0);
        // C: twice the chance is a 16-bit signed value compared with the draw.
        var doubled = (short)(chance << 1);
        if (doubled < roll)
        {
            c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.Next = Outskirts;
            return;
        }
        // DORMANT: needs a chance of at least half the draw, which the pop-up never gives.
        c.State.SavedCard = c.IsDaylight ? SideStreet : SideStreetAtNight;
        c.Next = Watch;
    }
}
