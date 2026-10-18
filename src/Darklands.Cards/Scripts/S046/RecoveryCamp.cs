using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Recov00": a developer's placeholder ("This is the RecoveryCamp card."). Every option
/// shows the text again and leads to the city outskirts.
/// </summary>
// orig: R046+0x1A14 (19D9:0004) matched; tests: CardSmokeTests
// DORMANT: the card's text has no option lines, so in the game its menu offers nothing to choose
//   (the player would be stuck); no card leads to card 11 either. The ten options are kept as written.
// DORMANT: R046+0x1FC8 (19D9:05B8), the init, installs pop-up handlers that never run
//   (no item or saint line): R046+0x20A2, R046+0x20DE,
//   R046+0x211A, R046+0x2156 (items: "Success is assured.", name the
//   member) and R046+0x2192, R046+0x221C, R046+0x22A6,
//   R046+0x2330 (saints: the member's chance text); lines 10..19 get the
//   "screwed up." handler R046+0x206A and have no action.
public sealed class RecoveryCamp : Card
{
    public const int CardNumber = 0x0B;

    private const string MsgName = "$Recov00";

    private const int Picture = 0, CityOutskirts = 0x04;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        c.StandingSeen = c.Reputation(c.Here);
        c.Variables.Money[0] = Money.From(0, 0, 1);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        var leader = c.Leader;
        OptionHint Certain() => OptionHint.ChanceOf(100, leader);
        OptionHint Assured(int who) => OptionHint.Say(SuccessAssured, who);
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.Next = Number;
        return menu.Run(
            // orig: R046+0x1C7A (19D9:026A) + R046+0x1C5C (19D9:024C) R046 seg 19D9 matched
            BackOutside(c, Certain),
            // orig: R046+0x1CD2 (19D9:02C2) + R046+0x1CB4 (19D9:02A4) R046 seg 19D9 matched
            BackOutside(c, () => Assured(leader)),
            // orig: R046+0x1D2A (19D9:031A) + R046+0x1D0C (19D9:02FC) R046 seg 19D9 matched
            BackOutside(c, Certain),
            // orig: R046+0x1D82 (19D9:0372) + R046+0x1D64 (19D9:0354) R046 seg 19D9 matched
            BackOutside(c, () => Assured(OptionHint.WholeParty)),
            // orig: R046+0x1DD8 (19D9:03C8) + R046+0x1DBA (19D9:03AA) R046 seg 19D9 matched
            BackOutside(c, () => Assured(leader)),
            // orig: R046+0x1E30 (19D9:0420) + R046+0x1E12 (19D9:0402) R046 seg 19D9 matched
            BackOutside(c, () => Assured(OptionHint.WholeParty)),
            // orig: R046+0x1E86 (19D9:0476) + R046+0x1E68 (19D9:0458) R046 seg 19D9 matched
            BackOutside(c, Certain),
            // orig: R046+0x1EDE (19D9:04CE) + R046+0x1EC0 (19D9:04B0) R046 seg 19D9 matched
            BackOutside(c, Certain),
            // orig: R046+0x1F36 (19D9:0526) + R046+0x1F18 (19D9:0508) R046 seg 19D9 matched
            BackOutside(c, Certain),
            // orig: R046+0x1F8E (19D9:057E) + R046+0x1F70 (19D9:0560) R046 seg 19D9 matched
            BackOutside(c, Certain));
    }

    /// <summary>Shows the text again and goes to the city outskirts.</summary>
    private static MenuLine BackOutside(CardContext c, Func<OptionHint> hint) =>
        new(() =>
            {
                c.Screen.Text(0);
                c.PassHours(0);
                c.Next = CityOutskirts;
            },
            hint);
}
