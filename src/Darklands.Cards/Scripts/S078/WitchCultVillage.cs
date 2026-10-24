namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa06": a hamlet given over to a witch cult; the villagers pour out against the party.
/// Defend yourselves (and win through to their altar on the hill), surrender, or flee.
/// </summary>
// orig: R078+0x0A6A (18DE:000A) matched; tests: CardSmokeTests
// orig: R078+0x0DDA (18DE:037A) matched; R078+0x0E12 (18DE:03B2) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class WitchCultVillage : Card
{
    public const int CardNumber = 0x8A;

    private const int Picture = 160;
    private const int BoundForSacrificePicture = 144;

    /// <summary>The cult's altar on the hill.</summary>
    private const int TheAltar = 0x8E;

    /// <summary>The midnight sacrifice.</summary>
    private const int TheSacrifice = 0x8F;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        return menu.Run(
            new MenuLine(() => DefendYourselves(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R078+0x0D12 (18DE:02B2) matched; R078+0x0D58 (18DE:02F8) matched; tests: CardSmokeTests
            new MenuLine(() => Surrender(c, 5), OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x0D90 (18DE:0330) matched; R078+0x0DA2 (18DE:0342) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R078+0x0B90 (18DE:0130) matched; R078+0x0CDA (18DE:027A) matched; tests: CardSmokeTests
    private static void DefendYourselves(CardContext c)
    {
        // C: arguments are evaluated right to left: rand_mod(4) (second group) before rand_mod(3).
        var third = c.PartyLoreLevel() / 4 + 1;
        var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 3;
        switch (c.RunBattle(0x18, c.Here, 0x0B, first, second, 4, third, 1, -1, 0, 0))
        {
            case 0:
                c.Screen.Text(1);
                c.PassHours(1);
                c.Next = TheAltar;
                break;
            case 1:
                c.ReportAway();
                c.Screen.Text(2);
                c.PassHours(1);
                c.Next = TheAltar;
                break;
            case 2:
                c.ReportAway();
                c.Screen.Text(3);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
                Surrender(c, 4);
                break;
            case 4:
                Surrender(c, 5);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party facing the mob.
                break;
        }
    }

    /// <summary>Bound and dragged up the hill, to be sacrificed at midnight.</summary>
    private static void Surrender(CardContext c, int text)
    {
        ShowPicture(c, BoundForSacrificePicture);
        c.Screen.Text(text);
        c.WaitUntilHour(0);
        c.Next = TheSacrifice;
    }
}
