namespace Darklands.Cards.Scripts;

/// <summary>
/// The scene the game's unfinished shop and service cards share ($Merch01, $Trade00,
/// $Pharm01 ...): the plain "common" picture, ten option lines, and each of them shows the
/// card's first text and goes on to the city entry card.
/// </summary>
// orig: R059+0x1896 (19C1:0006) matched; tests: CardSmokeTests
// The ten cards are byte-for-byte the same code except for the card number and MSG name (each subclass cites its own
// main function): R059+0x1896, R059+0x220E, R060+0x0000, R060+0x1674, R061+0x0000,
// R061+0x1900, R062+0x0000, R062+0x0978, R062+0x12F0, R063+0x16BA.
// Offsets below are relative to the main function (identical in all ten); absolute values are those of 19c1.
// orig: handler setup R059+0x1E1C (+0x586): card-option-enabled[0..19]=1, card-option-handler[0..19]=screwed-up handler
// R059+0x1EBE (+0x628, "screwed up."), the four item-handler-table[] 0666/06a2/06de/071a (menu-owner-member, success assured) and
// the four saint-handler-table[] 0756/07e0/086a/08f4 (saint chance text); card-item-lines/card-saint-lines then cleared to -1 before the
// card fills them in. Item and saint pop-ups only change the handler result, never the action, so they are not modelled.
public abstract class PlaceholderScene : Card
{
    private const int CommonPicture = 0;
    private const int CityEntry = 4;

    /// <summary>The card's MSG name (the card table has none for these cards).</summary>
    protected abstract string MessageFile { get; }

    public override int Run(CardContext c)
    {
        // C: card-run(msg, image-file-names.common); DS:EE70 = 0 and DS:EE72 = 7 are set before the menu (text window state, not modelled).
        c.Screen.Show(MessageFile, PictureNames.Of(CommonPicture));
        // The items and saints are listed, but these cards never offer the pop-ups.
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        // All ten opt_flag_k are 1 (every line enabled); card-option-enabled[10..19] are never copied and the text has ten lines.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var lines = new MenuLine[10];
        for (var k = 0; k < lines.Length; k++)
        {
            // orig: action k=0 R059+0x1AB6 (+0x220) card-text-block(0,1); k=1..9 R059+0x1B14/02da/0330/0386/03dc/0432/0488/04e0/0536 card-text-block(0,0)
            var frame = k == 0 ? TextFrame.BlankPage : TextFrame.OnPicture;
            // orig: handler k=0 R059+0x1ADA (+0x244) and k=7 R059+0x1D36 (+0x4A0): "success assured", chance 100 for the leader;
            // k=1..6, 8, 9 R059+0x1B32/02f8/034e/03a4/03fa/0450/04fe/0554: the same message with chance -1 for the whole party
            var leaderAssured = k is 0 or 7;
            lines[k] = new MenuLine(() => ShowFirstTextAndLeave(c, frame),
                () => leaderAssured
                    ? OptionHint.ChanceOf(100, c.Leader) with { Message = SuccessAssured }
                    : OptionHint.Say(SuccessAssured));
        }
        c.Next = Number;
        return menu.Run(lines);
    }

    private static void ShowFirstTextAndLeave(CardContext c, TextFrame frame)
    {
        c.Screen.Text(0, frame);
        c.PassHours(0);
        // C: action 0 writes following-card = daylight-test() ? 4 : 4 (both arms are the city entry card).
        c.Next = CityEntry;
    }
}
