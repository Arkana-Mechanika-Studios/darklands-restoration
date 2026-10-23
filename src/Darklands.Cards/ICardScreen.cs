namespace Darklands.Cards;

/// <summary>How an option line of a card text is offered.</summary>
public enum OptionState : byte
{
    /// <summary>The line is left out.</summary>
    Hidden = 0,

    /// <summary>The line can be chosen.</summary>
    Enabled = 1,

    /// <summary>The line is shown but cannot be chosen.</summary>
    Shown = 2,
}

/// <summary>Where a card text is drawn.</summary>
public enum TextFrame
{
    /// <summary>Over the card picture, inside the card frame.</summary>
    OnPicture = 0,

    /// <summary>On a blank page, with a dark text colour.</summary>
    BlankPage = 1,
}

/// <summary>
/// What an option shows while Shift is held over it: a chance of success (0..100), or a
/// message (Chance = -1), for one member (0..4) or the whole party (-2).
/// </summary>
public readonly record struct OptionHint(int Chance, string Message, int Member)
{
    public const int WholeParty = -2;

    public static OptionHint Say(string message, int member = WholeParty) => new(-1, message, member);

    public static OptionHint ChanceOf(int percent, int member) => new(percent, "", member);

    /// <summary>A hint function that only says something, for the member given now (the handler's <c>r.a = -1</c>).</summary>
    public static Func<OptionHint> Saying(string message, int member = WholeParty)
    {
        var hint = Say(message, member);
        return () => hint;
    }
}

/// <summary>The card display, as card scripts use it. Every call that waits returns once the player has acted.</summary>
public interface ICardScreen
{
    /// <summary>Shows a card: its picture, the party panel, and loads its texts.</summary>
    void Show(string cardName, string picture);

    /// <summary>Changes the picture behind the current card's texts.</summary>
    void ShowPicture(string picture);

    /// <summary>Shows a card with the picture the last <see cref="Show"/> drew (pic_buf left as it was): its texts are loaded.</summary>
    // orig: card_load(card_name, pic_buf) R043+0x0A9A with a pic_buf the card did not fill (R113+0x0656: no case of the picture switch matched)
    // C: a screen that keeps no picture (the scripted test screens) shows the card without one.
    void ShowWithTheLastPicture(string cardName) => Show(cardName, "");

    /// <summary>The card's picture and the party column drawn again after a full screen (the texts stay to be shown again).</summary>
    // orig: thunk 09C0:1EA5 = R043+0x0A9A (150B:000A) with the card's name and picture, as R029+0x2246 calls it after the inn's roster
    // C: a screen that keeps no picture (the scripted test screens) does nothing.
    void Reload()
    {
    }

    /// <summary>Shows a text block and waits for the player to continue.</summary>
    void Text(int block, TextFrame frame = TextFrame.OnPicture);

    /// <summary>Shows a text block with option lines and returns the option chosen.</summary>
    int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null);

    /// <summary>
    /// Shows a text block with option lines and returns the option chosen. An item or saint line opens
    /// its pop-up box (the card's <see cref="CardContext.CardItems"/> or <see cref="CardContext.CardSaints"/>
    /// for each member); a choice made there sets <see cref="CardContext.MenuMember"/>,
    /// <see cref="CardContext.MenuSlot"/> and <see cref="CardContext.MenuPopup"/> and returns the line.
    /// <paramref name="popupHint"/> is the hint of a choice in line k's box, asked with the member and slot set.
    /// </summary>
    // orig: card_options R043+0x0F2A (150B:049A) draft (function matched) + card_menu R043+0x1BD2 (150B:1142) draft (bytes match) with card_mouse R043+0x21D4 (150B:1744) draft (bytes match): the pop-up's handler table is item_fn / saint_fn (DS:EC00 / DS:ECF6), copied into the menu's own with the box layout
    // C: a screen without pop-up boxes (the scripted test screens) chooses as the three-argument Choose does.
    int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint) =>
        Choose(block, options, hint);

    /// <summary>
    /// <see cref="Choose(int, IReadOnlyList{OptionState}, Func{int, OptionHint}?, Func{int, OptionHint}?)"/> with a step the card
    /// takes between card_options (the text drawn, waited out and the lines faded in) and card_menu (the choice).
    /// </summary>
    // orig: R061+0x2278 (1A5F:0008) `card_options(1); advance_hours(1);` before `card_menu(...)`: the hour passes after the text block was shown and continued, not before it
    // C: a screen that does not separate the two (the scripted test screens) takes the step first.
    int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint, Action? afterOptions)
    {
        afterOptions?.Invoke();
        return Choose(block, options, hint, popupHint);
    }

    /// <summary>
    /// Runs the menu without drawing a text: on the option lines of the last text the screen laid out
    /// (any card's, a result text's too), each choosable when its line's state here is enabled. For a
    /// card whose loop calls card_menu without card_options.
    /// </summary>
    // orig: card_menu R043+0x1BD2 on the hotspot list DS:E96E (count, y[], height[], option index[], type[]) that the last
    // draw_text_wrapped left; opt_on[k] sets only the enabled byte of the line whose option index is k
    // C: a screen without a text layout of its own (the scripted test screens) offers block 0.
    int ChooseOnLastLayout(IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint) =>
        Choose(0, options, hint, popupHint);

    /// <summary>Shows a short message beside a member's portrait (or the whole panel) and waits.</summary>
    void Tell(string message, int member = OptionHint.WholeParty);

    /// <summary>A framed message for <paramref name="ticks"/> timer ticks, no key waited for (root 0000:24BC); a screen without one tells it.</summary>
    void ShowMessage(string text, int ticks) => Tell(text);

    /// <summary>Shows the hourglass while time passes.</summary>
    void PassTime(int hours);
}
