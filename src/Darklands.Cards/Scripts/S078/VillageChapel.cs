using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa07": the village chapel and its graveyard. Attend Mass (at Prime), confess, talk to
/// the priest, give a tithe, visit the graveyard (when a quest leads there), or leave. In a
/// village under evil the priest is one of the cult.
/// </summary>
// orig: R078+0x0E4A (191C:000A) matched; tests: CardSmokeTests
// orig: R078+0x1514 (191C:06D4) matched; R078+0x154C (191C:070C) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class VillageChapel : Card
{
    public const int CardNumber = 0x8B;

    private const int Picture = 27;
    private const int ChapelPicture = 290;
    private const int PriestPicture = 298;
    private const int VillageEvil = 0x3D;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Variables.Chosen[0] = c.Leader;
        c.Variables.Location = c.Here;
        c.Next = CardNumber;
        // C: money_pfennigs is a long, kept in a 16-bit int: large purses wrap here.
        var pfennigs = (short)c.State.Party.Purse.TotalPfennigs;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 5);
        if ((ushort)pfennigs < 10)
        {
            menu[3] = OptionState.Shown;
        }
        // C: the tithe is a tenth taken unsigned; the rest ($Money2, written through the address
        // DS:EEA2) is the 16-bit difference.
        var tenth = (ushort)pfennigs / 10;
        c.Variables.Money[0] = Money.FromPfennigs(tenth);
        c.Variables.Money[1] = Money.FromPfennigs((short)(pfennigs - tenth));
        menu[4] = c.Events.QuestAt(0x0A, c.Here) ? OptionState.Enabled : OptionState.Hidden;
        c.SetCardSaints();
        c.SetCardItems();

        // The original names the leader $ChosenOneName again before each choice.
        MenuLine Line(Action act, Func<OptionHint> hint) =>
            new(() => { c.Variables.Chosen[0] = c.Leader; act(); }, hint);
        return menu.Run(
            Line(() => AttendMass(c), OptionHint.Saying(NoRisk)),
            Line(() => Confess(c), () => OptionHint.Say(SuccessAssured, c.Leader)),
            Line(() => TalkToThePriest(c), OptionHint.Saying(NoRisk)),
            Line(() => GiveATithe(c), OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x1494 (191C:0654) matched; R078+0x149C (191C:065C) matched; tests: CardSmokeTests
            Line(() => c.Next = 0x2A, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x14D4 (191C:0694) matched; R078+0x14DC (191C:069C) matched; tests: CardSmokeTests
            Line(() => c.Next = 0x85, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>Mass is held at Prime: it lifts the spirit (and in a cursed village, it is a black mass).</summary>
    // orig: R078+0x104C (191C:020C) matched; R078+0x1152 (191C:0312) matched; tests: CardSmokeTests
    private static void AttendMass(CardContext c)
    {
        // C: the original asks cur_band() twice; the first answer is discarded.
        if (TimeOfDay.Band(c.State.Date.Hour) == 1)
        {
            if (c.EventAtHere(VillageEvil))
            {
                var favour = c.Skill(Who.Leader, 0x0B) / 40 + c.Skill(Who.Leader, 8) / 60 + 1;
                c.AddAttribute(Who.Everybody, 6, favour);
            }
            else
            {
                c.AddAttribute(Who.Everybody, 6, -1);
            }
            c.WaitUntilHour(9);
            ShowPicture(c, ChapelPicture);
            // C: && short-circuits: the coin is tossed only in a cursed village.
            if (c.EventAtHere(VillageEvil) && c.Random(2) != 0)
            {
                c.Screen.Text(4);
            }
            else
            {
                c.Screen.Text(2);
            }
        }
        else
        {
            ShowPicture(c, 295);
            c.Screen.Text(1);
        }
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    // orig: R078+0x118A (191C:034A) matched; R078+0x1232 (191C:03F2) matched; tests: CardSmokeTests
    private static void Confess(CardContext c)
    {
        ShowPicture(c, ChapelPicture);
        if (c.EventAtHere(VillageEvil))
        {
            c.PassHours(1);
            c.Variables.Chosen[0] = c.Leader;
            c.Screen.Text(8);
        }
        else
        {
            c.AddAttribute(c.Leader, 6, c.Random(50));
            c.PassHours(c.Random(8));
            c.Variables.Chosen[0] = c.Leader;
            c.Screen.Text(3);
        }
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    // orig: R078+0x126C (191C:042C) matched; R078+0x1340 (191C:0500) matched; tests: CardSmokeTests
    private static void TalkToThePriest(CardContext c)
    {
        if (c.EventAtHere(VillageEvil))
        {
            ShowPicture(c, PriestPicture);
            c.Screen.Text(0x0B);
        }
        else if (c.Events.QuestAt(4, c.Here))
        {
            ShowPicture(c, 297);
            c.Screen.Text(0x0A);
        }
        else
        {
            ShowPicture(c, 299);
            c.Screen.Text(9);
        }
        c.PassHours(1);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    /// <summary>The tithe ($Money1, set on arrival) earns divine favour by the size of the purse.</summary>
    // orig: R078+0x1378 (191C:0538) matched; R078+0x145C (191C:061C) matched; tests: CardSmokeTests
    private static void GiveATithe(CardContext c)
    {
        // C: a long division, then kept in a 16-bit int.
        var tenth = (short)(c.State.Party.Purse.TotalPfennigs / 10);
        c.AddAttribute(c.Leader, 6, Math.Clamp(tenth / 240, 1, 10));
        if (tenth < 240)
        {
            c.Screen.Text(5);
        }
        else
        {
            ShowPicture(c, PriestPicture);
            c.Screen.Text(tenth < 1440 ? 6 : 7);
            ShowPicture(c, Picture);
        }
        // C: the option stays offered: a second tithe takes the same $Money1 again.
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.PassHours(1);
        c.Next = CardNumber;
    }
}
