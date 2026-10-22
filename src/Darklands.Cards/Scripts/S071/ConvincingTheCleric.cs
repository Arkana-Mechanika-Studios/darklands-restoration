using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Cleri00": while the city's politics quest runs, the party talks to the cleric (the
/// church's side, bit 0x400) to win him over: with charisma and reputation, with virtue and
/// religion, with a donation, with a threat, or by prayer. The guild number that brought the
/// party here (party_x) is the fact the success is booked under.
/// </summary>
// orig: R071+0x1FB6 (1A33:0006) matched; tests: CardSmokeTests
// orig: handler setup R071+0x26AC (standard saint pop-ups 0796/081e/08a6/092e, unused-line handler
// 075e "screwed up."; the card lists no items)
// C: the card sets the window globals DS:EE72 = 7 and DS:EE70 = 0; they are not modelled (nothing reconstructed reads them).
public sealed class ConvincingTheCleric : Card
{
    private const int ClergySide = 0x400;
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int WildernessCard = 0x62;

    public override int Number => 0x6F;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        // Lines 0..5 are offered; lines 6..9 are switched off (opt_flag_6..9 = 0).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5);
        c.Support[0] = c.SideOf(ClergySide);
        var standing = c.LocationQuery(c.Here, 0);
        c.SetCardSaints(0x3D, 0x24, 0x70, 0x7D);
        menu[4] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(chance => Persuade(c, chance), () => new OptionHint(100, "", c.Leader)),
            new MenuLine(chance => Reason(c, chance), () => new OptionHint(100, "", 0)),
            new MenuLine(chance => Donate(c, chance), () => new OptionHint(100, "", OptionHint.WholeParty)),
            new MenuLine(_ => Threaten(c), () => new OptionHint(-1, SuccessAssured, c.Leader)),
            new MenuLine(_ => Pray(c, ref standing), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(_ => Leave(c), OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 6..9 (opt_flag_6..9 = 0, and the text has only six option lines): the first text,
            // time passes, back to the wilderness card. orig: R071+0x2544/05ee/0648/06a2, hints 05b2/060c/0666/06c0
            new MenuLine(_ => Dormant(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => Dormant(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => Dormant(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => Dormant(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// Charisma and reputation: success books a gain (or a loss) in the guild's fact; failure costs
    /// standing in the city, a lot if the chance was thin.
    /// </summary>
    // orig: R071+0x220A (hint R071+0x22D4: 100%, the leader)
    // C: the handler always offers 100, so the roll (0..99) always succeeds; the failure branches are kept.
    private void Persuade(CardContext c, int chance)
    {
        c.PassHours(1);
        var roll = c.Random(100);
        if (roll <= chance)
        {
            if (c.Events.FactValue(PoliticsQuest, 0, c.Here) == 1)
            {
                c.Events.Update(-2, c.Here, FactKind, c.GuildCode, 1, PoliticsQuest, EventUpdate.Add);
            }
            else
            {
                c.Events.Update(-2, c.Here, FactKind, c.GuildCode, -1, PoliticsQuest, EventUpdate.Add);
            }
            c.Screen.Text(1);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 7, -1);
        }
        else
        {
            if (chance / 3 >= roll)
            {
                c.AdjustReputation(c.Here, -5, -5);
                c.Screen.Text(3);
            }
            else
            {
                c.AdjustReputation(c.Here, -1, -1);
                c.Screen.Text(2);
            }
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 7, -1);
        }
        c.Next = Number;
    }

    /// <summary>Virtue, religion and Latin of the first member (slot 0).</summary>
    // orig: R071+0x22EC (hint R071+0x23A6: 100%, member 0)
    // C: as above the handler always offers 100.
    private void Reason(CardContext c, int chance)
    {
        c.PassHours(1);
        const int practice = 3;
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.Screen.Text(1);
            PracticeAll(c, 7, practice);
        }
        else
        {
            c.Screen.Text(chance / practice >= roll ? practice : 2);
            PracticeAll(c, 0, practice);
        }
        c.Next = Number;
    }

    private static void PracticeAll(CardContext c, int mode, int percent)
    {
        c.PracticeSkill(0, (int)Skill.Virtue, mode, percent, -1);
        c.PracticeSkill(0, (int)Skill.Religion, mode, percent, -1);
        c.PracticeSkill(0, (int)Skill.SpeakLatin, mode, percent, -1);
    }

    /// <summary>The donation: the roll decides between the grateful and the hopeless text; no money is taken.</summary>
    // orig: R071+0x23BC (hint R071+0x23EE: 100%, whole party)
    // C: the purse is not touched and $Money1 is not set by this card (the text shows whatever it held); the
    // handler offers 100, so the roll always succeeds.
    private void Donate(CardContext c, int chance)
    {
        c.PassHours(1);
        c.Screen.Text(c.Random(100) <= chance ? 4 : 5, TextFrame.BlankPage);
        c.Next = Number;
    }

    /// <summary>The threat: time passes, and the cleric sneers (text 2); the card the party came from comes back.</summary>
    // orig: R071+0x2404 (hint R071+0x2422)
    // C: text 6 ("terrible secret") is never shown by this card.
    private void Threaten(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(2);
        c.Next = c.SavedCard;
    }

    /// <summary>A saint wins the cleric over; only an answer of exactly 1 helps.</summary>
    // orig: R071+0x245C (hint R071+0x24C2 "Select a person and a saint.")
    // C: time passes before the saint is called, even if the prayer is cancelled (answer -1 returns at once);
    // answer - 1 == 0: only an answer of exactly 1 works.
    // C: cur_rep (the card's copy of the reputation, not written back) rises by 5; nothing reads it afterwards.
    private void Pray(CardContext c, ref int standing)
    {
        c.PassHours(1);
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            standing += 5;
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    c.Screen.Text(7, TextFrame.BlankPage);
                    break;
                case 2:
                case 3:
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
            }
        }
        else
        {
            c.Screen.Text(9, TextFrame.BlankPage);
        }
        c.Next = c.SavedCard;
    }

    // orig: R071+0x24FA (hint R071+0x250C)
    private void Leave(CardContext c)
    {
        c.PassHours(0);
        c.Next = c.SavedCard;
    }

    private static void Dormant(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = WildernessCard;
    }
}
