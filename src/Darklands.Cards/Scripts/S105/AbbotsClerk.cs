using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count04": inside a monastery, before the abbot's clerk. The party can ask for news,
/// trade, use the library, meet the alchemists, pay for prayers, store goods, sleep in a cell,
/// or leave. What is offered depends on whether the monastery is devout, businesslike or
/// corrupt (by the place's number, as at its gate, card 245).
/// </summary>
// orig: R105+0x1DA6 (1A12:0006) matched; tests: CardSmokeTests
// orig: R105+0x273A (1A12:099A) matched (registration: no item or saint lines; the pop-up call of this card reads item_fn[menu_slot], never used)
public sealed class AbbotsClerk : Card
{
    private const int ClerkPicture = 262;

    public override int Number => 246;

    public override int Run(CardContext c)
    {
        Show(c, ClerkPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 8, 9);
        // C: the pop-up branch of the loop calls (*item_fn[menu_slot])() — a row of the 2-D table, i.e. its first entry, left over from an earlier card. This card has no pop-up lines, so it never runs.
        var donation = Donation(c);
        c.Variables.Money[0] = donation;
        if (c.State.Party.Purse < donation)
        {
            menu[4] = OptionState.Shown;
        }
        if (c.Here % 10 > 4 && c.Here % 10 < 9)
        {
            menu[1] = OptionState.Hidden;
        }
        if (c.Here % 4 != 0)
        {
            menu[3] = OptionState.Hidden;
        }
        if (c.EventAtHere(0x62))
        {
            menu[3] = OptionState.Shown;
        }
        if (c.EventAtHere(0x34))
        {
            menu[2] = OptionState.Shown;
        }

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R105+0x2086 (1A12:02E6) matched; hint R105+0x2096
            new MenuLine(_ => { c.State.SavedCard = 246; c.State.SavedCard2 = 246; c.Next = 0x6E; }, NoRiskForTheLeader(c)),
            // orig: R105+0x212A (1A12:038A) matched (hint)
            new MenuLine(_ => Trade(c), Assured),
            // orig: R105+0x21C6 (1A12:0426) matched (hint)
            new MenuLine(p => UseTheLibrary(c, p), () => LibraryHint(c)),
            // orig: R105+0x22E2 (1A12:0542) matched (hint)
            new MenuLine(p => MeetTheAlchemists(c, menu, p), () => AlchemistsHint(c)),
            // orig: R105+0x234C (1A12:05AC) matched; hint R105+0x2472
            new MenuLine(_ => PayForPrayers(c, donation), NoRiskForTheLeader(c)),
            // orig: R105+0x24AC (1A12:070C) matched; hint R105+0x24FE
            // DORMANT: line 5 (store goods) is never offered (opt_flag_5 = 0).
            new MenuLine(_ => { ShowPicture(c, 290); c.Screen.Text(5); c.Screens.ItemStore(); c.PassHours(1); c.Next = 246; }, NoRiskForTheLeader(c)),
            // DORMANT: lines 6 and 7 are never offered; each lets no time pass (R105+0x2538, R105+0x257C).
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x25C0 (1A12:0820) matched; hint R105+0x2688
            new MenuLine(_ => SleepInACell(c), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R105+0x26C0 (1A12:0920) matched; hint R105+0x2702 (leaves without clearing cur_loc)
            new MenuLine(_ => { c.Variables.Chosen[0] = c.Leader; ShowPicture(c, 179); c.Screen.Text(8); c.Next = CardContext.MapCard; }, Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    private static Func<OptionHint> NoRiskForTheLeader(CardContext c) => () => OptionHint.Say(NoRisk, c.Leader);

    private static bool Devout(CardContext c) => c.Here % 10 < 5;

    /// <summary>A hundredth of the purse and of the bank deposit (each taken as a 16-bit count of pfennigs) plus 20 pfennigs a head.</summary>
    // C: (unsigned) of the long pfennig counts keeps their low 16 bits, unsigned, before the division.
    private static Money Donation(CardContext c)
    {
        var purse = (ushort)c.State.Party.Purse.TotalPfennigs / 100;
        var bank = (ushort)c.State.Party.BankNotes.TotalPfennigs / 100;
        return Money.From(0, 0, purse + bank + c.PresentCount() * 20);
    }

    /// <summary>Devout and businesslike monasteries trade different goods; corrupt ones (9) have nothing for sale.</summary>
    // orig: R105+0x20D0 (1A12:0330) matched; tests: CardSmokeTests
    private static void Trade(CardContext c)
    {
        c.PassHours(1);
        if (c.Here % 10 < 5)
        {
            c.OpenShop(-1, "monk", 0x40C, 0x400);
        }
        else if (c.Here % 10 < 9)
        {
            c.OpenShop(-1, "monk", 0x2D1E, 0x400);
        }
        else
        {
            c.Screen.Text(9, TextFrame.BlankPage);
        }
        c.Next = 246;
    }

    private static OptionHint LibraryHint(CardContext c)
    {
        c.PartyMaxSkillSum((int)Skill.ReadWrite, (int)Skill.SpeakLatin);
        var scholar = c.BestMember;
        var chance = c.Skill(scholar, (int)Skill.ReadWrite) + c.Skill(scholar, (int)Skill.SpeakLatin) + c.Attribute(scholar, (int)Attribute.Intelligence);
        if (Devout(c))
        {
            chance += 20;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), scholar);
    }

    /// <summary>On to the librarian (card 57, then back here); a failed request simply changes nothing.</summary>
    // orig: R105+0x2162 (1A12:03C2) matched; tests: CardSmokeTests
    private static void UseTheLibrary(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, 155);
            c.Screen.Text(1);
            c.EventHere(0x34, 168);
            c.State.SavedCard = 246;
            c.Next = 0x39;
        }
    }

    private static OptionHint AlchemistsHint(CardContext c)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.ReadWrite) + c.Skill(Who.Leader, (int)Skill.Alchemy) + c.Attribute(Who.Leader, (int)Attribute.Intelligence) / 3;
        if (Devout(c))
        {
            chance += 10;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Leader);
    }

    /// <summary>The alchemists' tower (card 83, then back here) is open only to those who answer their questions.</summary>
    // orig: R105+0x2230 (1A12:0490) matched; tests: CardSmokeTests
    private static void MeetTheAlchemists(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 197);
            c.Screen.Text(2);
            c.EventHere(0x62, 168);
            c.State.SavedCard = 246;
            c.Next = 0x53;
            return;
        }
        ShowPicture(c, 302);
        c.Screen.Text(6);
        c.EventHere(0x62, 168);
        menu[3] = OptionState.Shown;
        ShowPicture(c, ClerkPicture);
        c.Next = 246;
    }

    /// <summary>
    /// The donation buys Divine Favour: much in a devout house, by each member's Religion; a
    /// point each elsewhere.
    /// </summary>
    // orig: R105+0x234C (1A12:05AC) matched; tests: CardSmokeTests
    private static void PayForPrayers(CardContext c, Money donation)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(donation);
        ShowPicture(c, 290);
        if (c.Here % 10 < 5)
        {
            c.Screen.Text(3);
            for (var i = 0; i < Party.MaxMembers; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty)
                {
                    c.AddAttribute(i, (int)Attribute.DivineFavor, c.Skill(i, (int)Skill.Religion) / 5 + 3);
                }
            }
        }
        else if (c.Here % 10 < 5)
        {
            // DORMANT: the original repeats the devout test here (it surely meant < 9), so this
            // middle blessing for businesslike houses (text 4) is never given.
            c.Screen.Text(4);
            for (var i = 0; i < Party.MaxMembers; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty)
                {
                    c.AddAttribute(i, (int)Attribute.DivineFavor, c.Skill(i, (int)Skill.Religion) / 6 + 2);
                }
            }
        }
        else
        {
            c.Screen.Text(5);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 1);
        }
        c.PassHours(Math.Min(5, c.HoursUntil(18)));
        ShowPicture(c, ClerkPicture);
        c.Next = 246;
    }

    /// <summary>A night in a cell restores Endurance and a point of Strength.</summary>
    // orig: R105+0x25C0 (1A12:0820) matched; tests: CardSmokeTests
    private static void SleepInACell(CardContext c)
    {
        ShowPicture(c, 353);
        c.Screen.Text(7);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.SetAttribute(i, (int)Attribute.Endurance, c.AttributeMaximum(i, (int)Attribute.Endurance));
                if (c.AttributeMaximum(i, (int)Attribute.Strength) > c.Attribute(i, (int)Attribute.Strength))
                {
                    c.AddAttribute(i, (int)Attribute.Strength, 1);
                }
            }
        }
        c.PassHours(c.HoursUntil(5) + 1);
        ShowPicture(c, ClerkPicture);
        c.Next = 246;
    }
}
