using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Polit01": thugs catch the party posting handbills in a city in political trouble. The party
/// can eat the handbills, talk its way out, bribe them, run, use a potion, call a saint, or
/// fight; a lost fight costs the party its goods and standing.
/// </summary>
// orig: R057+0x1CA0 (1A02:0000) matched; tests: CardSmokeTests
// orig: R057+0x27AC (1A02:0B0C) matched (init helper: enables all 20 option lines, installs the 4 item and 4 saint pop-up handlers per member row and empties card-item-lines and card-saint-lines; the card then fills 4 items and 3 saints)
// DORMANT: R057+0x284E (1A02:0BAE) the init helper points card-option-handler[0..19] at this "screwed up." default; lines 0..9 get their own handlers and lines 10..19 stay enabled but the MSG has only 7 lines, so it never runs.
// DORMANT: option lines 7, 8 and 9 (R057+0x26DC (1A02:0A3C) / R057+0x26E8 (1A02:0A48), R057+0x2720 (1A02:0A80) / R057+0x272C (1A02:0A8C), R057+0x2764 (1A02:0AC4) / R057+0x2770 (1A02:0AD0)): the entry enables them, but the card's text has no line for them, so the menu cannot select them. Each only lets no time pass (the hints say "success is assured"; the last one leaves a dead store of the leader in r.a).
public sealed class HandbillThugs_Polit01 : Card
{
    public const int CardNumber = 0x37;

    private const int Picture = 33;

    private const int SideStreet = 9, SideStreetAtNight = 0x0A;

    /// <summary>The location facts (kind 0x1C, tag 2) about the thugs: the truce running for a day or two, and that they have been beaten.</summary>
    // C: the names of types 0x12 and 0x13 are guessed from how they are used.
    private const int PoliticsTag = 2, ThugsBeaten = 0x12, ThugsTruce = 0x13;

    /// <summary>
    /// The chance a potion gets from its pop-up handler: the original handlers never set it, so the
    /// action receives whatever the stack held (the caller's frame pointer, an address in the upper half
    /// of the data segment: negative as a signed word). The roll therefore never succeeds.
    /// </summary>
    // UNCERTAIN: derived from the stack layout (card-choice-menu's saved BP lies in the handler's unset result word).
    private const int StalePopupChance = -2;

    private const int TalkOption = 1, RunOption = 3;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    // orig: R057+0x1CA0 (1A02:0000) matched (entry)
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.StandingSeen = c.HereQuery(0);
        // The bribe needs at least 120 pfennigs (10 groschen) in the purse.
        var bribe = c.State.Party.Purse.TotalPfennigs < 0x78 ? OptionState.Shown : OptionState.Enabled;
        c.SetCardItems(0x68, 0x60, 0x65, 0x61);
        c.SetCardSaints(0x0C, 0x36, 0x3D);
        var potion = c.ItemsUsable();
        var saint = c.SaintsUsable();

        _menu = Menu(c, 0);
        _menu.Enable(0, 1, 2, 3, 4, 5, 6);
        _menu[2] = bribe;
        _menu[4] = potion;
        _menu[5] = saint;
        c.Next = Number;
        return _menu.Run(
            // orig: R057+0x21DC (1A02:053C) matched (option 0 action, see EatTheHandbills); hint R057+0x2232 "No problem, feeling hungry?"
            new MenuLine(() => EatTheHandbills(c), OptionHint.Saying("No problem, feeling hungry?")),
            // orig: R057+0x225C (1A02:05BC) matched (option 1 action, see TalkThemOut); chance handler R057+0x2312 (see TalkChance)
            new MenuLine(chance => TalkThemOut(c, chance), () => TalkChance(c)),
            // orig: R057+0x2380 (1A02:06E0) matched (option 2 action, see Bribe); chance handler R057+0x2450 (see BribeChance)
            new MenuLine(chance => Bribe(c, chance), () => BribeChance(c)),
            // orig: R057+0x2490 (1A02:07F0) matched (option 3 action, see RunAway); chance handler R057+0x2504 (see RunChance)
            new MenuLine(chance => RunAway(c, chance), () => RunChance(c)),
            // orig: R057+0x2522 (1A02:0882) matched (option 4 action, see UsePotion); hint R057+0x25C6 "select a potion"; pop-up handlers 0be6/0bfc/0c12/0c28 (see PotionHint)
            new MenuLine(chance => UsePotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            // orig: R057+0x25FE (1A02:095E) matched (option 5 action, see CallUponASaint); hint R057+0x265C "success is assured"; pop-up handlers 0c3e/0cc8/0d58/0de8 (see SaintHint)
            // C: the pop-up's handler runs before the action and names the member in the text.
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(SuccessAssured), () => SaintHint(c)),
            // orig: R057+0x2694 (1A02:09F4) matched (option 6: draw weapons and attack); hint R057+0x26A4 "success is assured"
            new MenuLine(() =>
                {
                    Fight(c);
                    c.PassHours(1);
                },
                OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 7 (R057+0x26DC (1A02:0A3C)): enabled by the entry, but the text has no line for it. No time passes; hint "success is assured".
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 8 (R057+0x2720 (1A02:0A80)): as line 7.
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 9 (R057+0x2764 (1A02:0AC4)): as line 7.
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)));
    }

    private void CloseOption(int option) => _menu[option] = OptionState.Shown;

    private static int StreetForTheHour(CardContext c) => c.IsDaylight ? SideStreet : SideStreetAtNight;

    /// <summary>The thugs leave the party alone for a while (renews the truce event for the hours).</summary>
    private static void MakeTruce(CardContext c, int hours) =>
        c.Events.Renew(-2, c.Here, 0, EventKinds.LocationFact, ThugsTruce, 0, 0, hours, 0, PoliticsTag);

    /// <summary>Eats the handbills: a beating, a day's truce, a loss of standing and an hour; the card stays.</summary>
    // orig: R057+0x21DC (1A02:053C) matched
    // BUG: R057+0x21DC..0x2230 never writes following-card (DS:E48A): after the truce the card is shown again and the line can be chosen again,
    //   each time injuring the party, lowering the standing and passing an hour.
    private static void EatTheHandbills(CardContext c)
    {
        c.Screen.Text(0x12, TextFrame.BlankPage);
        MakeTruce(c, 0x18);
        c.Injure(Who.Everybody, 5, 10, 1);
        c.AdjustReputation(c.Here, -0x18, -0x0E);
        c.PassHours(1);
    }

    /// <summary>The leader's Intelligence plus half Streetwise, and a random bonus once the thugs have been beaten before.</summary>
    // orig: R057+0x2312 (1A02:0672) matched
    // C: the random bonus is drawn by the hint handler itself, so it is drawn again by the original every time the handler runs (hover and choice).
    private static OptionHint TalkChance(CardContext c)
    {
        var leader = c.Leader;
        var chance = c.Attribute(leader, 4) + c.Skill(leader, (int)Skill.Streetwise) / 2;
        if (c.Events.Fact(PoliticsTag, ThugsBeaten, c.Here))
        {
            chance += c.Random(11) + 9;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), leader);
    }

    // orig: R057+0x225C (1A02:05BC) matched
    private void TalkThemOut(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(5);
            MakeTruce(c, 0x18);
            if (c.Attribute(c.Leader, 5) * 2 < c.Skill(c.Leader, (int)Skill.SpeakCommon))
            {
                c.PracticeSkill(c.Leader, (int)Skill.Streetwise, 1, 10, 0);
            }
            c.Next = c.State.PreviousCard;
        }
        else
        {
            c.Screen.Text(6);
            CloseOption(TalkOption);
            CloseOption(RunOption);
            c.PracticeSkill(c.Leader, (int)Skill.Streetwise, 0, 10, 0);
        }
        c.PassHours(1);
    }

    /// <summary>The leader's Streetwise against the standing in the city.</summary>
    // orig: R057+0x2450 (1A02:07B0) matched
    private static OptionHint BribeChance(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(c.Skill(c.Leader, (int)Skill.Streetwise) - c.HereQuery(0) + 100, 0, 99), OptionHint.WholeParty);

    /// <summary>The thugs take nine tenths of the purse (counted in 16-bit pfennigs, as the original does).</summary>
    // C: (unsigned)purse-in-pfennigs(purse) * 9 / 10 is done on the low 16 bits only, so a purse above 7281 pfennigs wraps.
    private static void PayTheThugs(CardContext c)
    {
        var pfennigs = (ushort)c.State.Party.Purse.TotalPfennigs;
        c.State.Party.Purse = Money.From(0, 0, (ushort)(pfennigs * 9) / 10);
    }

    // orig: R057+0x2380 (1A02:06E0) matched
    private void Bribe(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            PayTheThugs(c);
            MakeTruce(c, 0x18);
            c.Screen.Text(7);
            c.PracticeSkill(c.Leader, (int)Skill.Streetwise, 7, 5, -1);
            c.Next = c.State.PreviousCard;
        }
        else
        {
            PayTheThugs(c);
            c.Screen.Text(8);
            c.PracticeSkill(c.Leader, (int)Skill.Streetwise, 0, 5, -1);
            Fight(c);
        }
        c.PassHours(1);
    }

    /// <summary>The party's slowest member (the lowest effective agility) decides.</summary>
    // orig: R057+0x2504 (1A02:0864) matched
    private static OptionHint RunChance(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(c.PartyMinEffectiveAgility(), 0, 99), OptionHint.WholeParty);

    // orig: R057+0x2490 (1A02:07F0) matched
    private void RunAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(9);
            MakeTruce(c, 3);
            c.Next = StreetForTheHour(c);
        }
        else
        {
            _ = c.PartyMinEffectiveAgility();
            // The slowest member is the one named in the text.
            c.Variables.Chosen[0] = c.BestMember;
            c.Screen.Text(0x0A);
            Fight(c);
        }
        c.PassHours(1);
    }

    /// <summary>The potion pop-up's hint (all four slots are the same handler): it names the member but sets no chance.</summary>
    // orig: R057+0x2886 (1A02:0BE6) matched (item slot 0)
    // orig: R057+0x289C (1A02:0BFC) matched (item slot 1; same body)
    // orig: R057+0x28B2 (1A02:0C12) matched (item slot 2; same body)
    // orig: R057+0x28C8 (1A02:0C28) matched (item slot 3; same body)
    // C: r.a is never assigned in these handlers (see StalePopupChance).
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.ChanceOf(StalePopupChance, c.MenuMember);
    }

    // orig: R057+0x2522 (1A02:0882) matched
    private void UsePotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(0x0B);
                    break;
                case 1:
                    c.Screen.Text(0x0C);
                    break;
                case 2:
                    c.Screen.Text(0x0D);
                    break;
                case 3:
                    c.Screen.Text(0x0E);
                    break;
            }
            MakeTruce(c, 0x18);
            c.Next = StreetForTheHour(c);
        }
        else
        {
            c.Screen.Text(0x0F);
            Fight(c);
        }
        c.PassHours(1);
    }

    /// <summary>The saint pop-up's hint (all four slots are the same handler): the member's chance with the saint.</summary>
    // orig: R057+0x28DE (1A02:0C3E) matched (saint slot 0)
    // orig: R057+0x2968 (1A02:0CC8) matched (saint slot 1; same body)
    // orig: R057+0x29F8 (1A02:0D58) matched (saint slot 2; same body)
    // DORMANT: R057+0x2A88 (1A02:0DE8) (saint slot 3; same body): the card has only three saints (card-saint-lines[3] stays empty).
    private static OptionHint SaintHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)), c.MenuMember);
    }

    /// <summary>The saint always helps (the original does not even ask for the prayer to be said): the thugs leave for two days.</summary>
    // orig: R057+0x25FE (1A02:095E) matched
    private static void CallUponASaint(CardContext c)
    {
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(0x10);
                break;
            case 1 or 2:
                c.Screen.Text(0x11);
                break;
        }
        MakeTruce(c, 0x30);
        c.PassHours(1);
        c.Next = StreetForTheHour(c);
    }

    /// <summary>
    /// The fight with the thugs: how it goes depends on how learned the party is; the battle is
    /// against a different group by the street the party came from. Winning makes the thugs wary
    /// (and brings standing); losing costs half or most of the goods and all the florins.
    /// </summary>
    // orig: R057+0x1F22 (1A02:0282) matched (checked against asm)
    private void Fight(CardContext c)
    {
        int Brawl(int group)
        {
            // C: the arguments are evaluated right to left, so the second number of thugs is drawn before the first.
            var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
            var first = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
            return c.RunBattle(group, -1, 0x0C, first, second, 0x16, c.PartyLoreLevel() % 3 + 1, 1, -1, 0, 0);
        }

        var previous = c.State.PreviousCard;
        int result;
        switch (previous)
        {
            case 6 or 8:
                // BUG: the case has no break; it fights the group of the main street and then, falling through, the group of the side street too, and only the second result counts.
                _ = Brawl(0x13);
                result = Brawl(0x11);
                break;
            case 9 or 0x0A:
                result = Brawl(0x11);
                break;
            default:
                result = Brawl(0x56);
                break;
        }
        switch (result)
        {
            case 0:
                c.Screen.Text(1);
                MakeTruce(c, 0x18);
                c.Events.Create(-2, c.Here, 0, EventKinds.LocationFact, ThugsBeaten, 0x63, 0, 0x1E0, 0, PoliticsTag);
                c.LocationSet(c.Here, 0, c.Random(6) + c.StandingSeen + 4);
                c.Next = previous;
                break;
            case 1 or 2:
                c.ReportAway();
                c.Screen.Text(2);
                c.Events.Create(-2, c.Here, 0, EventKinds.LocationFact, ThugsTruce, 0, 0, 0x18, 0, PoliticsTag);
                c.Next = StreetForTheHour(c);
                break;
            case 3:
                c.Screen.Text(3);
                c.Events.Create(-2, c.Here, 0, EventKinds.LocationFact, ThugsTruce, 0, 0, 0x18, 0, PoliticsTag);
                c.KeepItems(Who.Everybody, 50);
                // C: party-purse[0] = 0 clears the florins only.
                c.State.Party.Purse = c.State.Party.Purse with { Florins = 0 };
                c.LocationSet(c.Here, 0, -(c.Random(5) - c.StandingSeen) - 2);
                c.Next = StreetForTheHour(c);
                break;
            case 4:
                c.Screen.Text(4);
                c.Events.Create(-2, c.Here, 0, EventKinds.LocationFact, ThugsTruce, 0, 0, 0x18, 0, PoliticsTag);
                c.KeepItems(Who.Everybody, 70);
                c.State.Party.Purse = c.State.Party.Purse with { Florins = 0 };
                c.LocationSet(c.Here, 0, -(c.Random(6) - c.StandingSeen) - 4);
                c.Next = StreetForTheHour(c);
                break;
        }
    }
}
