using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Tacti00": a developer's stand-in for a street battle. "You have now entered combat";
/// the player picks the outcome (victory, breaking through, flight, downed by the guards,
/// surrender), which leads to the challenge, the gates or the city outskirts. No card leads
/// here in the game, and its picture "city3a" is not among the game's pictures.
/// </summary>
// orig: R045+0x0530 (188B:0000) matched; tests: CardSmokeTests
// DORMANT: the whole card: no card and no table code but the screen table's entry 5 leads here.
// DORMANT: R045+0x0976 (init) also sets the pop-up handlers R045+0x0A50, R045+0x0A8E,
//   R045+0x0ACC, R045+0x0B0A (items) and R045+0x0B48, R045+0x0B86,
//   R045+0x0BC4, R045+0x0C02 (saints): each names the pop-up's member ($ChosenOneName)
//   and says "screwed up."; the card offers no item or saint line (usable-item-list/saints-available are
//   never asked), so the pop-up never opens.
// DORMANT: R045+0x0866, R045+0x08BC, R045+0x08F2, R045+0x0928,
//   R045+0x095E: hint handlers written for options 5..9 but never installed (options 5..9
//   keep the init's "screwed up." handler R045+0x0A18).
// DORMANT: option lines 10..19 are enabled by the init loop with handler R045+0x0A18 and have
//   no action; the card's text has 5 option lines, so neither they nor options 5..9 can be picked.
public sealed class BattleStandIn : Card
{
    public const int CardNumber = 0x05;

    private const string MsgName = "$Tacti00", PictureName = "city3a";

    private const int Challenged = 0x01, CityGate = 0x02, CityGateAtNight = 0x03, CityOutskirts = 0x04;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureName);
        c.StandingSeen = c.Reputation(c.Here);
        c.Variables.Money[0] = Money.From(0, 0, 1);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R045+0x0726 (188B:01F6) + R045+0x0714 (188B:01E4) R045 seg 188B matched
            // DIVERGENCE: the original goes to screen 0, which is card 0 ($Initi00, a normal overlay vector that
            //   sets the purse to 200 florins and earlier-screen to 0); that card is not in the C# card table, so the C# goes back
            //   to the card before, as the dispatcher does with unknown cards. No card leads here in play.
            new MenuLine(() => GoTo(c, c.State.PreviousCard), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R045+0x0750 (188B:0220) + R045+0x073E (188B:020E) R045 seg 188B matched
            new MenuLine(() => GoTo(c, Challenged), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R045+0x079C (188B:026C) + R045+0x078A (188B:025A) R045 seg 188B matched
            new MenuLine(() => GoTo(c, CityGate), OptionHint.Saying(NoRisk)),
            // orig: R045+0x07E6 (188B:02B6) + R045+0x07D4 (188B:02A4) R045 seg 188B matched
            new MenuLine(() => GoTo(c, CityGateAtNight), OptionHint.Saying(SuccessAssured)),
            // orig: R045+0x0830 (188B:0300) + R045+0x081E (188B:02EE) R045 seg 188B matched
            new MenuLine(() => GoTo(c, CityOutskirts), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R045+0x0A18 (188B:04E8) + R045+0x0848 (188B:0318) R045 seg 188B matched
            Unlisted(c),
            // orig: R045+0x0A18 + R045+0x089E (188B:036E) R045 seg 188B matched
            Unlisted(c),
            // orig: R045+0x0A18 + R045+0x08D4 (188B:03A4) R045 seg 188B matched
            Unlisted(c),
            // orig: R045+0x0A18 + R045+0x090A (188B:03DA) R045 seg 188B matched
            Unlisted(c),
            // orig: R045+0x0A18 + R045+0x0940 (188B:0410) R045 seg 188B matched
            Unlisted(c));
    }

    private static void GoTo(CardContext c, int card)
    {
        c.PassHours(0);
        c.Next = card;
    }

    /// <summary>Options 5..9 (no line in the text): show the first text again and go to the outskirts.</summary>
    private static MenuLine Unlisted(CardContext c) =>
        new(() =>
            {
                c.Screen.Text(0);
                GoTo(c, CityOutskirts);
            },
            OptionHint.Saying(CardTexts.ScrewedUp));
}
