namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Shell00": a swindler's shell game in the market: three walnut shells, a dried pea, a
/// groschen a try (two groschen if the pea is found). The party pays and picks a shell, or
/// walks away. The pea is never where it looks; only once in a while, once per game, the
/// swindler lets the party win.
/// </summary>
// orig: R088+0x0B20 (18EA:0000) matched; tests: CardSmokeTests
public sealed class ShellGameSwindler : Card
{
    public const int CardNumber = 0xB2;

    private const int Picture = 238;

    /// <summary>The swindler's lively music.</summary>
    private const int ShellGameMusic = 0x10;

    // C: DS:8E1C, a module static of the original, kept between visits: the swindler has let
    // the party win once (from then on never again).
    private bool _hasLetThePartyWin;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        // The music playing now is remembered (driver command 5) and brought back on leaving.
        // orig: R088+0x0B20: DS:9C3D = music-start(5); music-start(0x10)
        c.SavedMusic.Value = c.CurrentMusic;
        c.PlayMusic(ShellGameMusic);
        Show(c, Picture);
        c.Next = CardNumber;
        // Option flags: lines 0 (pay and play) and 1 (walk away) first; the three pick lines after paying.
        var menu = Menu(c);
        menu.Enable(0, 1);
        // The pay line's own flag; the menu offers it greyed while the party cannot pay (PayState).
        var pay = OptionState.Enabled;
        // The text block shown: 0 the offer; 1..3 the pea was right/middle/left; 4..6 the swindler's shuffle; 7 a win.
        var block = 0;
        // orig: R088+0x0F46 (18EA:0426) matched; R088+0x0F7E (18EA:045E) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints();

        // The start of an offer or a lost round: only paying or leaving.
        void OfferAgain()
        {
            pay = OptionState.Enabled;
            menu[1] = OptionState.Enabled;
            menu[2] = OptionState.Hidden;
            menu[3] = OptionState.Hidden;
            menu[4] = OptionState.Hidden;
        }

        // The swindler lets the party win once (the first pick that wins his coin toss): a groschen back with two more.
        bool LetThePartyWin(int oneIn)
        {
            if (!_hasLetThePartyWin && c.Random(oneIn) == 1)
            {
                c.State.Party.Purse = c.State.Party.Purse.Add(0, 3, 0);
                _hasLetThePartyWin = true;
                block = 7;
                return true;
            }
            return false;
        }

        OptionState PayState()
        {
            // The pay line is only shown (not choosable) when the party cannot pay a groschen (12 pfennigs or less).
            if (pay == OptionState.Enabled && c.State.Party.Purse.TotalPfennigs <= 12)
            {
                return OptionState.Shown;
            }
            return pay;
        }

        // C: the C# state functions below stand for the copy of the option flags into the menu before each choice.
        // C: the start-up cannot leave this card, so the menu loop test is always true.
        menu.EachRound = () =>
        {
            menu[0] = PayState();
            menu.Block = block;
        };
        var result = menu.Run(
            // orig: R088+0x0CCA (18EA:01AA) matched; R088+0x0D1A (18EA:01FA) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.State.Party.Purse = c.State.Party.Purse.Subtract(0, 1, 0);
                pay = OptionState.Hidden;
                menu[1] = OptionState.Hidden;
                menu[2] = OptionState.Enabled;
                menu[3] = OptionState.Enabled;
                menu[4] = OptionState.Enabled;
                switch (c.Random(3))
                {
                    case 0:
                        block = 4;
                        break;
                    case 1:
                        block = 5;
                        break;
                    case 2:
                        block = 6;
                        break;
                    default:
                        // DORMANT: roll-below(3) is 0..2.
                        block = 4;
                        break;
                }
            }, OptionHint.Saying("It's only a groschen")),
            // orig: R088+0x0D48 (18EA:0228) matched; R088+0x0D5A (18EA:023A) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PassHours(0);
                c.Next = c.State.PreviousCard;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R088+0x0D92 (18EA:0272) matched; R088+0x0DF6 (18EA:02D6) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // Picking the right-hand shell.
                OfferAgain();
                if (LetThePartyWin(3))
                {
                    return;
                }
                block = c.Random(100) % 2 != 0 ? 2 : 3;
            }, OptionHint.Saying("The hand is quicker than the eye")),
            // orig: R088+0x0E24 (18EA:0304) matched; R088+0x0E86 (18EA:0366) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // Picking the centre shell.
                OfferAgain();
                if (LetThePartyWin(4))
                {
                    return;
                }
                block = c.Random(100) % 2 != 0 ? 1 : 3;
            }, OptionHint.Saying("The hand is quicker than the eye")),
            // orig: R088+0x0EB4 (18EA:0394) matched; R088+0x0F18 (18EA:03F8) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // Picking the left-hand shell.
                OfferAgain();
                if (LetThePartyWin(4))
                {
                    return;
                }
                block = c.Random(100) % 2 != 0 ? 1 : 2;
            }, OptionHint.Saying("The hand is quicker than the eye")));

        // orig: R088+0x0B20: music-start(DS:9C3D) after the loop
        c.PlayMusic(c.SavedMusic.Value);
        return result;
    }
}
