using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Magis00": the party kneels before the richter (the magistrate). Say nothing (and be
/// tortured, at most three times), plead innocence, invoke a saint, or confess. The sentence
/// may be death, a flogging, a fine, or release.
/// </summary>
// orig: R079+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R079+0x0860 (1838:0860) matched; R079+0x08B8 (1838:08B8) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class BeforeTheMagistrate : Card
{
    public const int CardNumber = 0x8C;

    private const int Picture = 246;
    private const int TorturePicture = 247;
    private const int SquarePicture = 107;
    private const int SentencePicture = 293;

    /// <summary>The execution.</summary>
    private const int Execution = 0x8D;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var standing = c.LocationQuery(c.Here, 0);
        // C: the torture count is a module static (DS:8E14) reset on every visit.
        var tortured = 0;
        // C: the picture buffer is the card's: torture changes it for later redraws.
        var picture = Picture;
        c.SetCardItems();
        c.SetCardSaints(0x22, 0x54);
        var menu = Menu(c);
        menu.Enable(0, 1, 3);
        menu[2] = c.SaintsUsable();
        menu.EachRound = () => menu.Block = tortured > 0 ? 1 : 0;

        return menu.Run(
            // orig: R079+0x017C (1838:017C) matched; R079+0x0274 (1838:0274) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                if (tortured < 3)
                {
                    picture = TorturePicture;
                    ShowPicture(c, picture);
                    c.PassHours(6);
                    TheBailiffsWhip(c);
                    c.PassHours(0);
                    KeepTheLeaderAlive(c);
                    c.Next = CardNumber;
                    tortured++;
                    return;
                }
                ShowPicture(c, SquarePicture);
                c.Screen.Text(4);
                c.PassHours(1);
                c.Next = TownSquare(c);
                tortured++;
            }, OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(() => PleadInnocence(c, standing), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R079+0x08F0 (1838:08F0) matched; R079+0x097A (1838:097A) matched; tests: CardSmokeTests
            new MenuLine(() => InvokeASaint(c, picture), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => ConfessGuilt(c, standing), OptionHint.Saying(SuccessAssured)));
    }

    private static int TownSquare(CardContext c) => c.IsDaylight ? 0x12 : 0x19;

    /// <summary>The party's influence with the court: always 0 in the original (an unfinished hook).</summary>
    // orig: R079+0x0A04 (1838:0A04) matched; tests: CardSmokeTests
    // DORMANT: its terms in the sentence and the fine never count.
    private static int Influence() => 0;

    /// <summary>Endurance and strength lost to torture or the whip.</summary>
    private static void TheBailiffsWhip(CardContext c)
    {
        // C: each loss is drawn as the argument of its own add_attr call, endurance first.
        c.AddAttribute(Who.Everybody, 0, -c.Random(0x12));
        c.AddAttribute(Who.Everybody, 1, -c.Random(0x0C));
    }

    private static void KeepTheLeaderAlive(CardContext c)
    {
        if (c.Attribute(Who.Leader, 0) < 1)
        {
            c.SetAttribute(Who.Leader, 0, 1);
        }
    }

    // orig: R079+0x02AC (1838:02AC) matched; R079+0x04A0 (1838:04A0) matched; tests: CardSmokeTests
    private static void PleadInnocence(CardContext c, int standing)
    {
        var verdict = c.Random(5) + standing / 40 - 3;
        verdict += Influence() / 2;
        c.Events.Remove(0x11, -1, c.Here, -1, -1);
        switch (verdict)
        {
            case >= -6 and <= -1:
                c.PassHours(3);
                ShowPicture(c, SentencePicture);
                c.Screen.Text(5);
                c.Next = Execution;
                return;
            case 0:
                c.PassHours(3);
                ShowPicture(c, SquarePicture);
                c.Screen.Text(6);
                TheBailiffsWhip(c);
                c.Next = TownSquare(c);
                return;
            case 1:
                Fine(c);
                break;
            default:
                // Released: innocence believed.
                c.PassHours(1);
                ShowPicture(c, SquarePicture);
                c.Screen.Text(9);
                c.Next = TownSquare(c);
                return;
        }
        c.Next = TownSquare(c);
    }

    /// <summary>
    /// A fine of $Money1 florins: a party with that many florins is held a day "until it has
    /// paid", one without is flogged instead. Neither takes the money (as in the original).
    /// </summary>
    private static void Fine(CardContext c)
    {
        var florins = c.Random(3);
        florins += c.LocationQuery(c.Here, 2) / 3 - Influence();
        if (florins < 1)
        {
            florins = 1;
        }
        c.Variables.Money[0] = Money.From(florins, 0, 0);
        // C: party_money is declared int here: the fine is compared with the purse's florins word.
        if (florins <= c.State.Party.Purse.Florins)
        {
            c.PassHours(24);
            c.Screen.Text(7);
            return;
        }
        c.PassHours(3);
        ShowPicture(c, SquarePicture);
        c.Screen.Text(8);
        TheBailiffsWhip(c);
    }

    // orig: R079+0x04D8 (1838:04D8) matched; R079+0x062A (1838:062A) matched; tests: CardSmokeTests
    private static void InvokeASaint(CardContext c, int picture)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            c.NameToSlot(1, c.CardSaints[c.MenuSlot], 0);
            ShowPicture(c, SentencePicture);
            c.Screen.Text(2);
            c.AddAttribute(Who.Everybody, 0, -c.Random(0x14));
            KeepTheLeaderAlive(c);
            c.PassHours(12);
            ShowPicture(c, SquarePicture);
            c.Screen.Text(4);
            c.Next = TownSquare(c);
            return;
        }
        ShowPicture(c, SentencePicture);
        c.Screen.Text(3);
        ShowPicture(c, picture);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    // orig: R079+0x0662 (1838:0662) matched; R079+0x0828 (1838:0828) matched; tests: CardSmokeTests
    private static void ConfessGuilt(CardContext c, int standing)
    {
        var verdict = c.Random(3) + standing / 40 - 2;
        verdict += Influence() / 2;
        c.Events.Remove(0x11, -1, c.Here, -1, -1);
        switch (verdict)
        {
            case >= -5 and <= -1:
                c.PassHours(3);
                ShowPicture(c, SentencePicture);
                c.Screen.Text(5);
                c.Next = Execution;
                break;
            case 0:
                c.PassHours(3);
                ShowPicture(c, SquarePicture);
                c.Screen.Text(6);
                TheBailiffsWhip(c);
                c.Next = TownSquare(c);
                break;
            case >= 1 and <= 5:
                Fine(c);
                c.Next = TownSquare(c);
                break;
            default:
                // DORMANT: verdicts below -5 or above 5 (impossible: -4..2) leave the party standing before the richter.
                break;
        }
    }
}
