namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd12": the party names a password to the Templar guard. Only "Beelzebub forever" lets
/// them pass; every other word brings armed foes through the door (ambush, −3).
/// </summary>
// orig: R121+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R121+0x0218; hint R121+0x025E
//   option 1: action R121+0x0296; hint R121+0x02B0
//   option 2: action R121+0x02E8; hint R121+0x032E
//   option 3: action R121+0x0366; hint R121+0x03AC
//   option 4: action R121+0x03E4; hint R121+0x042A
//   option 5: action R121+0x0462; hint R121+0x04A8
//   option 6: action R121+0x04E0; hint R121+0x0526
//   option 7: action R121+0x055E; hint R121+0x057C
//   option 8: action R121+0x05B8; hint R121+0x05D6
//   option 9: action R121+0x0612; hint R121+0x0630
//   item pop-up: R121+0x0746, R121+0x0782, R121+0x07BE,
//     R121+0x07FA
//   saint pop-up: R121+0x0836, R121+0x08BE, R121+0x0946,
//     R121+0x09CE
//   other: R121+0x066C, R121+0x070E
// DORMANT (generated): option slots 10..19 keep the default handler R121+0x070E ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card-item-lines stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card-saint-lines stays all -1, so no line opens a saint pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class TemplarPassword : Card
{
    public override int Number => 308;

    private const int Picture = 168;
    private const int FoesPicture = 185;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // C: the card never sets settings-block; its menu always shows block 0 (card-option-lines(0)).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5);
        c.SetCardItems();
        c.SetCardSaints();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            new MenuLine(_ => WrongPassword(c), unknown),
            // orig: R121+0x0296 matched — "Beelzebub forever".
            new MenuLine(_ => { c.Screen.Text(3); Leave(c); }, unknown),
            new MenuLine(_ => WrongPassword(c), unknown),
            new MenuLine(_ => WrongPassword(c), unknown),
            new MenuLine(_ => WrongPassword(c), unknown),
            new MenuLine(_ => WrongPassword(c), unknown),
            // DORMANT: line 6 is never enabled; its handler R121+0x04E0 is another wrong password.
            new MenuLine(_ => WrongPassword(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    // orig: the common tail of R121+0x0218 .. _04e0 matched
    private static void Leave(CardContext c)
    {
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R121+0x0218 / _02e8 / _0366 / _03e4 / _0462 / _04e0 matched (identical)
    private static void WrongPassword(CardContext c)
    {
        ShowPicture(c, FoesPicture);
        c.Screen.Text(2);
        c.CombatFlags |= 8;
        Leave(c);
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R121+0x055E / _05b8 / _0612 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
