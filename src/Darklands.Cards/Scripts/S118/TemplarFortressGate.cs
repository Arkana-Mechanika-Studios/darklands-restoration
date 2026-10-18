namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd01": before the Templars' fortress monastery. The party can enter the graveyard (a
/// battle the main loop starts, card returns −2), leave, or call on a saint: the fourth saint
/// reveals the one dark gravestone (fact 1/0x12, which card 297 reads).
/// </summary>
// orig: R118+0x14C2 (1984:0002) matched; tests: CardSmokeTests
// orig: R118+0x1B52 (1984:0692) matched (registration: item pop-ups name the member, saint pop-ups do not)
public sealed class TemplarFortressGate : Card
{
    private const int CardPicture = 179;

    public override int Number => 298;

    public override int Run(CardContext c)
    {
        Show(c, CardPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x35, 0x44, 0x0F, 0x6F);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R118+0x16FA (1984:023A) matched; hint R118+0x1742
            new MenuLine(_ => EnterTheGraveyard(c), Unknown),
            // orig: R118+0x177A (1984:02BA) matched; hint R118+0x1782
            // C: next_card = 0x0C with no time passing.
            new MenuLine(_ => c.Next = CardContext.MapCard, Unknown),
            // orig: R118+0x17BA (1984:02FA) matched; hint R118+0x18A4
            new MenuLine(_ => CallOnASaint(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 3..9 are never enabled. Each (R118+0x18DC, R118+0x1936, R118+0x1990,
            // R118+0x19EA, R118+0x1A44, R118+0x1A9E, R118+0x1AF8) would show text 0,
            // pass no time and close the card (0x62); hints 043a..0656 say "unknown" for no one.
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown),
            new MenuLine(_ => CloseTheCard(c), Unknown));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The graveyard: a visit note here for an hour, then battle 0x5E (mode 0).</summary>
    // orig: R118+0x16FA (1984:023A) matched; tests: CardSmokeTests
    private static void EnterTheGraveyard(CardContext c)
    {
        c.EventHere(0x64, 1);
        c.CardBattle.Enemy = 0x5E;
        c.CardBattle.Mode = 0;
        c.CardBattle.Parameters[0] = 0;
        c.CardBattle.Parameters[1] = 0;
        c.CardBattle.Parameters[2] = c.Events.FactAnywhere(1, 0x12) ? 1 : 0;
        c.CardBattle.Seed = c.Random(1000);
        c.Next = -2;
    }

    /// <summary>
    /// Saints 1..3 send visions (texts 1, 3, 4); the fourth shows the dark stone (fact 1/0x12, forever).
    /// Unanswered, the darkness reaches Heaven. The saint line is spent either way.
    /// </summary>
    // orig: R118+0x17BA (1984:02FA) matched; tests: CardSmokeTests
    // C: the card is reloaded (card_load_near) only after the fourth saint's vision or no answer.
    private void CallOnASaint(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        menu[2] = OptionState.Shown;
        if (answer != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(1);
                    return;
                case 1:
                    c.Screen.Text(3);
                    return;
                case 2:
                    c.Screen.Text(4);
                    return;
                case 3:
                    // C: event_new(-2, cur_loc, 0, 0x1C, 0x12, 0, 0, DS:270F, 0, 1): the address 0x270F = 9999, forever.
                    c.Events.Create(-2, c.Here, 0, 0x1C, 0x12, 0, 0, 9999, 0, 1);
                    ShowPicture(c, 181);
                    c.Screen.Text(5);
                    break;
                default:
                    return;
            }
        }
        else
        {
            ShowPicture(c, 182);
            c.Screen.Text(6);
        }
        Show(c, CardPicture);
    }

    // orig: R118+0x18DC (1984:041C) matched (dormant; twins 0476, 04d0, 052a, 0584, 05de, 0638)
    private static void CloseTheCard(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CardContext.ClosedCard;
    }
}
