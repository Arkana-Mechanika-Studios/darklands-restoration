namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Overl00": the road leads out of the Empire, toward Burgundy and France, Switzerland and
/// Italy, Hungary or Poland. None of those roads is open in the game: only turning back is offered.
/// </summary>
// orig: R111+0x1066 (193E:0006) matched; tests: CardSmokeTests
// orig: R111+0x14F2 (193E:0492) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
// DIVERGENCE: the original offers the option lines of an uninitialised local (card_options(v_di)); text block 0 is offered here.
// DORMANT: texts 1 and 2 (the road or the pass is blocked) are never shown.
public sealed class RoadOutOfTheEmpire : Card
{
    public override int Number => 271;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu[9] = OptionState.Enabled;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // DORMANT: lines 0..8 are never offered (opt_flag = 0). Actions R111+0x1280, R111+0x12BA,
            // R111+0x12F4, R111+0x132E, R111+0x1368 are empty; 0342..040e let no time pass.
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            new MenuLine(_ => { }, Unknown),
            // orig: R111+0x13A2 (193E:0342) matched; hint R111+0x13AE
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            // orig: R111+0x14B2 (193E:0452) matched; hint R111+0x14BA (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");
}
