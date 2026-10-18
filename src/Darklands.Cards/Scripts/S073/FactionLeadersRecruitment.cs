using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol02": the leader of a faction (the rebels when party_x is 0, the old lord's men
/// otherwise) sizes the party up. Offer your swords for pay, argue for the cause, speak of
/// virtue or ask a saint; a party that once betrayed the faction must first talk its way past
/// that.
/// </summary>
// orig: R073+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R073+0x09C4: option handlers (0a26 "screwed up.") and item pop-ups only
public sealed class FactionLeadersRecruitment : Card
{
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int JoinTheFaction = 0x79;

    public override int Number => 0x74;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 7);
        // C: DS:8E0A, a module global: the least agile member's Agility, read by the item pop-ups.
        var leastAgility = c.PartyMinAttribute((int)Attribute.Agility);
        var rebels = c.GuildCode == 0;

        if (c.Events.Fact(PoliticsQuest, 0x24, c.Here) || c.Events.Fact(PoliticsQuest, 0x25, c.Here))
        {
            // The party once betrayed them.
            if (Math.Clamp(c.Attribute(Who.Leader, (int)Attribute.Charisma) - 20, 0, 99) + c.HereQuery(0) / 2 >= c.Random(100))
            {
                c.Events.Remove(FactKind, 0x24, -1, -1, PoliticsQuest);
                c.Events.Remove(FactKind, 0x25, -1, -1, PoliticsQuest);
                c.Screen.Text(0x0E, TextFrame.BlankPage);
                c.Next = JoinTheFaction;
            }
            else
            {
                c.AdjustReputation(c.Here, -1, -2);
                c.Screen.Text(0x0D, TextFrame.BlankPage);
                c.Next = c.IsDaylight ? 9 : 10;
            }
        }
        if (rebels)
        {
            // C: !(x % 5) with a signed x: negative multiples count too.
            c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
            c.Variables.Money[0] = Core.Economy.Money.From(0, 0, (c.HereQuery(2) + 5) * 2);
            menu[2] = OptionState.Hidden;
        }
        else
        {
            c.RandomName(c.City.Ordinal + 0x16, 2, 0);
            c.Variables.Money[0] = Core.Economy.Money.From(0, 0, (c.HereQuery(2) + 4) * 3);
            menu[1] = OptionState.Hidden;
        }
        // C: cur_rep is read and not used. The handler setup clears card_items only; card_saints[3]
        // keeps the previous card's value (written here slot by slot).
        _ = c.LocationQuery(c.Here, 0);
        c.CardSaints[0] = 0x6A;
        c.CardSaints[1] = 0x39;
        c.CardSaints[2] = 0x44;
        menu[4] = c.SaintsUsable();
        // DORMANT: the item pop-ups this card installs (R073+0x0A5E, R073+0x0A8A, R073+0x0AB6, R073+0x0AE2: the least agile member's
        // Agility + 60, 50, 75, 75) belong to no option line. The value is kept for that reading only.
        _ = leastAgility;

        return menu.Run(
            new MenuLine(chance => Persuade(c, rebels, chance, 1, 2), () => MercenaryChance(c, rebels)),
            new MenuLine(chance => Persuade(c, rebels, chance, 3, 4), () => CauseChance(c, 30)),
            new MenuLine(chance => Persuade(c, rebels, chance, 3, 4), () => CauseChance(c, 40)),
            new MenuLine(chance => Persuade(c, rebels, chance, 5, 6), () => VirtueChance(c)),
            // DIVERGENCE: this card installs no saint pop-up handlers (saint_fn keeps the previous card's);
            // the standard saint rating stands in for them.
            new MenuLine(_ => Pray(c, rebels), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // DORMANT: lines 5, 6, 8, 9 (opt_flag = 0): time passes. orig: R073+0x086A/08ae/093c/0980 (hints 0876/08ba/0948/098c)
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R073+0x08F2 (hint 0904 "No risk."): leave.
            new MenuLine(_ => { c.PassHours(1); c.Next = c.PreviousCard; }, OptionHint.Saying(NoRisk)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)));
    }

    // orig: R073+0x046A
    private static OptionHint MercenaryChance(CardContext c, bool rebels)
    {
        var need = 0x85 - (rebels ? c.SidePercent(-1) : c.SidePercent(1));
        var chance = c.PartyGet(4) / 20 + Math.Abs(c.HereQuery(0) / 10) + need;
        return new OptionHint(Math.Clamp(chance, 0, 100), "", OptionHint.WholeParty);
    }

    /// <summary>The cause: Speak Common, Charisma, a little fame (by <paramref name="fameDivisor"/>) and standing.</summary>
    // orig: R073+0x0538 (fame / 30), R073+0x061C (fame / 40)
    private static OptionHint CauseChance(CardContext c, int fameDivisor)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.SpeakCommon)
            + (c.Attribute(Who.Leader, (int)Attribute.Charisma) + (c.PartyGet(4) / fameDivisor + Math.Abs(c.HereQuery(0) / 15)));
        return new OptionHint(Math.Clamp(chance, 0, 100), "", c.Leader);
    }

    // orig: R073+0x0700
    private static OptionHint VirtueChance(CardContext c)
    {
        var chance = c.PartyAverageSkill((int)Skill.Virtue) + (c.Skill(Who.Leader, (int)Skill.Virtue)
            + (c.Attribute(Who.Leader, (int)Attribute.Charisma) + (c.HereQuery(0) + c.PartyGet(4) / 10)));
        return new OptionHint(Math.Clamp(chance, 0, 100), "", c.Leader);
    }

    /// <summary>The party joins the faction: the city's politics fact 0 takes its side.</summary>
    private static void TakeTheirSide(CardContext c, bool rebels) =>
        c.Events.Update(-2, c.Here, FactKind, 0, rebels ? -1 : 1, PoliticsQuest, EventUpdate.Set);

    /// <summary>The leader explains the party's job (rebels: one of two texts by the city).</summary>
    // orig: R073+0x03C0
    private static void Briefing(CardContext c, bool rebels)
    {
        // C: x % 2 of a signed value: −1 counts as odd.
        if (rebels && c.HereQuery(0x21) % 2 != 0)
        {
            c.Screen.Text(7);
            return;
        }
        c.Screen.Text(rebels ? 8 : 9);
    }

    /// <summary>One approach: an hour's talk; won over, the leader takes the party in; else it is shown out.</summary>
    // orig: R073+0x03FE (texts 1/2), R073+0x04CC and R073+0x05B0 (3/4), R073+0x0694 (5/6)
    private static void Persuade(CardContext c, bool rebels, int chance, int wonText, int lostText)
    {
        c.PassHours(1);
        if (c.Succeeds(chance))
        {
            TakeTheirSide(c, rebels);
            c.Screen.Text(wonText);
            Briefing(c, rebels);
            c.Next = JoinTheFaction;
            return;
        }
        c.Screen.Text(lostText);
        c.Next = c.PreviousCard;
    }

    /// <summary>A saint shows what the leader wants; if the faction is too weak (a third of its rivals or less) the party leaves.</summary>
    // orig: R073+0x0778 (hint R073+0x0832)
    private void Pray(CardContext c, bool rebels)
    {
        // C: the hour passes before the prayer, even when it is called off.
        c.PassHours(1);
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            if ((rebels && c.SidePercent(-1) > 0x21) || (!rebels && c.SidePercent(1) > 0x21))
            {
                TakeTheirSide(c, rebels);
                c.Screen.Text(0x0A);
                Briefing(c, rebels);
                c.Next = JoinTheFaction;
                return;
            }
            c.Screen.Text(0x0C);
            c.Next = c.SavedCard;
            return;
        }
        c.Screen.Text(0x0B);
        c.Next = Number;
    }
}
