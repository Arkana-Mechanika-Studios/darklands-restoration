using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol05": the city guard comes to arrest the rebel leader the party works for, either
/// at his house while the party is there (coming from his door, the planning talk or the
/// march) or with a messenger-girl bursting in to fetch the party. Fight the guards, or
/// cravenly abandon him.
/// </summary>
// orig: R076+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R076+0x03D6 (1838:03D6) matched; R076+0x03F8 (1838:03F8) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 2..19 (never offered).
public sealed class GuardsArrestTheRebelLeader : Card
{
    public const int CardNumber = 0x80;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = CardNumber;
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.RandomName(c.City.Ordinal + 0x23, 1, 1);
        c.Variables.Money[0] = Money.From(0, c.HereQuery(2) * 10, 0);
        var previous = c.State.PreviousCard;
        // C: the original keeps this in a module static (DS:8E12), but sets it on every visit.
        var messengerComes = previous is 0x10 or 0x11 or 0x79 or 0x70;

        var menu = Menu(c, messengerComes ? 0 : 4);
        menu.Enable(0, 1);
        return menu.Run(
            new MenuLine(() => Fight(c, messengerComes), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(() => AbandonHim(c), OptionHint.Saying("Coward!")));
    }

    // orig: R076+0x01A6 (1838:01A6) matched; R076+0x032C (1838:032C) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, bool messengerComes)
    {
        // C: arguments are evaluated right to left: the third group, then the second, then the
        // first (party-lore-rating draws nothing, so only the two roll-below calls matter).
        var third = c.PartyLoreLevel() % 3 + 1;
        var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var first = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x1E, -1, 8, first, second, 0x21, third, 1, -1, 0, 0))
        {
            case 0:
                c.ConflictOutcome(1, -1, -1);
                c.Screen.Text(messengerComes ? 7 : 6);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 9 : 0x0A;
                break;
            case 1:
            case 2:
                c.ReportAway();
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(1, -1, 0);
                c.Screen.Text(8);
                c.PassHours(1);
                c.Next = 0x82;
                break;
            case 3:
                c.PassHours(2);
                c.ConflictOutcome(1, -1, 0);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(9);
                c.Next = c.IsDaylight ? 9 : 0x0A;
                break;
            case 4:
                c.PassHours(2);
                c.AdjustReputation(c.Here, -5, -5);
                c.ConflictOutcome(1, -1, 0);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(0x0A);
                c.Next = 0x0D;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the card as it is.
                break;
        }
    }

    // orig: R076+0x0364 (1838:0364) matched; R076+0x03AE (1838:03AE) matched; tests: CardSmokeTests
    /// <summary>The city remembers the party's cowardice for 60 days.</summary>
    private static void AbandonHim(CardContext c)
    {
        c.PassHours(1);
        c.Events.Create(-2, c.Here, 0x5A, 0x1C, 0x24, 0, 0, 0x5A0, 0, 2);
        c.ConflictOutcome(1, -1, 0);
        c.Screen.Text(2);
        c.Next = 0x82;
    }
}
