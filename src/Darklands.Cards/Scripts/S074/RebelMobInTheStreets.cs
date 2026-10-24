using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol06": the city lord's men call the party, who works for him, to stop a rebel mob
/// in the streets. Fight it for a purse, or cravenly refuse. When the party meets the mob
/// itself (coming from $MilCr00 or $CivCr00 with the rebels' plans known, or from $Other00),
/// it may also try to talk the crowd into going home: a line the
/// original never wired up.
/// </summary>
// orig: R074+0x20A6 (1A42:0006) matched; tests: CardSmokeTests
// orig: R074+0x246E (1A42:03CE) matched; R074+0x2490 (1A42:03F0) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class RebelMobInTheStreets : Card
{
    public const int CardNumber = 0x7B;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        var rebelPlans = c.Events.FactValue(2, 0x17, c.Here);
        c.RandomName(c.City.Ordinal + 0x16, 2, 0);
        c.RandomName(c.City.Ordinal + 0x23, 1, 1);
        c.Variables.Money[0] = Money.From(0, c.HereQuery(2) * 10, 0);

        var previous = c.State.PreviousCard;
        var facingTheMob = (previous == 0x17 && (rebelPlans & 0x1E) != 0)
            || (previous == 0x1A && (rebelPlans & 0xE0) != 0)
            || previous == 0x7C;
        var menu = Menu(c, facingTheMob ? 0x0B : 0);
        menu.Enable(0);
        // C: opt_flag_1 = 0 and nothing sets it again: the back-off line is never offered.
        menu[1] = OptionState.Hidden;
        // C: opt_on[2] = opt_flag_2 (never assigned: what the card before left), then opt_on[2] = 1 when facing the mob (opt_flag_2 stays).
        if (facingTheMob)
        {
            menu.Override(2, OptionState.Enabled);
        }
        return menu.Run(
            new MenuLine(() => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(() => BackOff(c), OptionHint.Saying("Cowards!")),
            // orig: R074+0x2442 (1A42:03A2) matched; R074+0x2444 (1A42:03A4) matched; tests: CardSmokeTests
            // DORMANT: talking to the crowd: the original has no code behind this line (it does nothing and the menu returns).
            // C: away from the mob, line 2 takes opt_flag_2, which this card never sets (a stale
            // global of an earlier card); block 0 has no third line, so it is taken as hidden.
            // C: if a stale value 1 left the line enabled in block 0, choosing it would also do nothing.
            new MenuLine(() => { }, OptionHint.Saying("No Code!")));
    }

    // orig: R074+0x224A (1A42:01AA) matched; R074+0x23AE (1A42:030E) matched; tests: CardSmokeTests
    private static void Fight(CardContext c)
    {
        // C: arguments are evaluated right to left: the second group's size is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        switch (c.RunBattle(0x25, c.Here + 0x70, 3, 2, first, 0, 2, second, -1, 0, 0))
        {
            case 0:
                c.ConflictOutcome(-1, 1, 1);
                c.State.Party.Purse = c.State.Party.Purse.Add(c.Variables.Money[0]);
                c.Screen.Text(7);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 6 : 8;
                break;
            case 1:
            case 2:
                c.ReportAway();
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(-1, 1, -1);
                c.Screen.Text(8);
                c.PassHours(1);
                c.Next = 0x82;
                break;
            case 3:
                c.PassHours(2);
                c.ConflictOutcome(-1, 1, -1);
                c.LoseItems(Core.Characters.Who.Everybody);
                c.Screen.Text(9);
                c.Next = c.IsDaylight ? 9 : 0x0A;
                break;
            case 4:
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(-1, 1, -1);
                c.LoseItems(Core.Characters.Who.Everybody);
                c.Screen.Text(0x0A);
                c.PassHours(2);
                c.Next = 4;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results (5, negative codes) leave the card as it is: the menu comes back.
                break;
        }
    }

    // orig: R074+0x23E6 (1A42:0346) matched; R074+0x2418 (1A42:0378) matched; tests: CardSmokeTests
    // DORMANT: line 1 is never offered (opt_flag_1 = 0), so the party never backs off here.
    /// <summary>The city remembers the party's cowardice for 60 days.</summary>
    private static void BackOff(CardContext c)
    {
        c.Screen.Text(1);
        c.Events.Create(-2, c.Here, 0x5A, 0x1C, 0x24, 0, 0, 0x5A0, 0, 2);
        c.Next = 0x82;
    }
}
