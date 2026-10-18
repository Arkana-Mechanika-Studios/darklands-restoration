namespace Darklands.Cards;

/// <summary>A card: a scene with a picture, texts and choices. Running it returns the next card.</summary>
// orig: the card functions of table R136+0xD090[] (one per card number); the engine they share is (R043, image 'card engine'), see Show and Menu; the table dispatcher is root 03A1:000A (CardDispatcher)
public abstract class Card
{
    public abstract int Number { get; }

    public abstract int Run(CardContext c);

    /// <summary>The card's MSG name from the card table.</summary>
    // orig: strcpy(card_name, "$Xxxxnn") at the top of every card function (DS:E3D4); the table is derived from those calls, see CardTable
    public string Name => CardTable.Find(Number)?.Name ?? $"card {Number}";

    /// <summary>Shows the card with one of the numbered card pictures.</summary>
    // orig: R043+0x0A9A (150B:000A) draft (bytes match; the byte-match summary counts 22 of the file's 28 functions); tests: CardSmokeTests (card_load: pics\<pic>.pic, text colour 15 for pictures starting with 'x', msgfiles\<name>.msg)
    // C: the card scripts pass pic_names[n] (a far string) in pic_buf; the number-to-name table is PictureNames.
    protected void Show(CardContext c, int picture) => c.Screen.Show(Name, PictureNames.Of(picture));

    // orig: R043+0x0BA0 (150B:0110) draft (matches); tests: CardSmokeTests (card_load_near: the same without the MEM.LOG line and the DS:E957 card-name store)
    // DIVERGENCE: card_load also appends a MEM.LOG line (R043+0x0000, a debug memory report) and clears the card sound; card_load_near does neither; both are not reproduced (a debug log and a sound reset).
    protected static void ShowPicture(CardContext c, int picture) => c.Screen.ShowPicture(PictureNames.Of(picture));

    /// <summary>A new option menu of this card on text block <paramref name="block"/>, offered while the card stays on its own number.</summary>
    // orig: see CardMenu (card_options, card_menu and the do/while loop every card script runs)
    // C: the lines the card's function copies from opt_flag_k every round are the global flags (OptionFlagCopies, generated from the C).
    /// <param name="c">The card's context.</param>
    /// <param name="block">The text block with the option lines.</param>
    /// <param name="resetsHandlers">False for a card whose reset installs no "screwed up." handlers (its unlisted lines keep the handlers an earlier card left).</param>
    protected CardMenu Menu(CardContext c, int block = 0, bool resetsHandlers = true)
    {
        var (copied, stale) = OptionFlagCopies.Of(GetType().Name);
        return new(c, Number, block, copied, stale, resetsHandlers);
    }
}
