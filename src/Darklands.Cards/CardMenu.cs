namespace Darklands.Cards;

/// <summary>
/// The short texts of the card engine that option hints show (the strings of R043 1462 and the
/// far texts the card scripts share).
/// </summary>
// orig: text-success-certain DS:4F48, text-no-danger DS:4F02, text-botched DS:4EC4, text-sleep-till-dawn DS:4ED0, text-wait-for-night DS:4EEA, text-pick-potion DS:4F0C, text-pick-saint DS:4F2A, text-potion-effective DS:4F7A, text-not-known DS:4F9A, text-enter-fight DS:4FC0; R136+0x8690+0x1E57 / +0x1E5B
public static class CardTexts
{
    /// <summary>The hint of most option lines.</summary>
    public const string SuccessAssured = "Success is assured.";

    public const string NoRisk = "No risk.";

    /// <summary>The hint of an option line a card has no handler for (the handler its reset installs).</summary>
    public const string ScrewedUp = "screwed up.";

    public const string SleepUntilDawn = "You'll sleep until dawn.";

    public const string WaitForNight = "You'll wait for night.";

    /// <summary>The hint of a "use an item" option line.</summary>
    public const string SelectPotion = "Select a person and a potion.";

    /// <summary>The hint of a "call upon a saint" option line.</summary>
    public const string SelectSaint = "Select a person and a saint.";

    public const string PotionWorks = "Of course the potion will work!";

    public const string Unknown = "You can always encounter the unknown";

    /// <summary>A hint when nothing can be promised (R136+0x8690+0x1E57).</summary>
    public const string HopeForTheBest = "You can hope for the best";

    /// <summary>A hint when the party cannot pay (R136+0x8690+0x1E5B).</summary>
    public const string NotEnoughMoney = "Not enough money";

    /// <summary>The hint of an option that starts a fight.</summary>
    public const string EnterCombat = "Enter Combat";
}

/// <summary>
/// One option line of a <see cref="CardMenu"/>: what choosing it does, its hint (the original's
/// option handler, <c>card-option-handler[k]</c>: chance or message and who acts; "Success is assured." when
/// there is none) and, for item and saint lines, the hint of a choice made in the pop-up box
/// (<c>item-handler-table</c> / <c>saint-handler-table</c>, reading <see cref="CardContext.MenuMember"/> and
/// <see cref="CardContext.MenuSlot"/>).
/// </summary>
/// <remarks>
/// As in the original the chosen line's handler is called once the line is chosen (the pop-up's
/// handler for a pop-up choice), with its side effects, and an action that takes the chance is
/// handed its chance (−1 when it only says something). The handlers live in the engine's one
/// table (<see cref="CardContext.OptionHandlers"/>, <c>card-option-handler</c>): the lines a card lists install
/// theirs every round; a line it does not list has the handler its reset installed ("screwed up."),
/// or, for a card with an empty reset, whatever an earlier card left there.
/// </remarks>
// orig: card-option-handler[] DS:EA14, item-handler-table[][] DS:EC00, saint-handler-table[][] DS:ECF6 and the per-option action every card script calls after card-choice-menu with the handler's r.a (e.g. R044+0x1A50); tests: GoldenTraceTests, CardSmokeTests
// C: the original always calls the chosen line's handler after the menu (local-minus-0a = card-option-handler[k]() or DS:EB0A[menu-choice-index]()), whether the action uses local-minus-0a or not.
public sealed class MenuLine
{
    private readonly Action<int> _choose;

    /// <summary>A line whose action works without the chance (its handler is still called on the choice).</summary>
    public MenuLine(Action choose, Func<OptionHint>? hint = null, Func<OptionHint>? popup = null)
        : this(_ => choose(), hint, popup, takesTheChance: false)
    {
    }

    /// <summary>A line whose action is handed the chance of its handler, called on the choice.</summary>
    public MenuLine(Action<int> choose, Func<OptionHint>? hint = null, Func<OptionHint>? popup = null)
        : this(choose, hint, popup, takesTheChance: true)
    {
    }

    private MenuLine(Action<int> choose, Func<OptionHint>? hint, Func<OptionHint>? popup, bool takesTheChance)
    {
        _choose = choose;
        Hint = hint;
        Popup = popup;
        TakesTheChance = takesTheChance;
    }

    /// <summary>The line's hint (<c>card-option-handler[k]</c>); none is "Success is assured.".</summary>
    public Func<OptionHint>? Hint { get; }

    /// <summary>The hint of a choice made in the line's item or saint pop-up box; none: the menu's <see cref="CardMenu.Popup"/>, else <see cref="Hint"/>.</summary>
    public Func<OptionHint>? Popup { get; }

    /// <summary>The action is handed the chance of the handler called on the choice.</summary>
    public bool TakesTheChance { get; }

    /// <summary>The handler of lines a card has none for: "screwed up.", and choosing it does nothing.</summary>
    // orig: the "screwed up." handler each card's reset function installs for all 20 lines (e.g. R044+0x25BA)
    internal static readonly MenuLine Missing = new(_ => { }, ScrewedUp);

    internal static OptionHint ScrewedUp() => OptionHint.Say(CardTexts.ScrewedUp);

    internal OptionHint AskHint() => Hint?.Invoke() ?? OptionHint.Say(CardTexts.SuccessAssured);

    internal void Choose(int chance) => _choose(chance);
}

/// <summary>
/// A card's option menu: the text block that lists the option lines (<c>settings-block</c>), each
/// line's state (<c>opt_flag_k</c>: hidden, enabled, shown greyed), and the lines' handlers. A card
/// sets the states, then <see cref="Run"/> offers the lines until a choice moves the game away
/// from the card. Choices may change the states and the block; the next round shows the change.
/// </summary>
/// <remarks>
/// Each round: <see cref="EachRound"/> (it may move the card on: then nothing is offered), the
/// block and states are offered, the screen returns the choice (asking hints while Shift is held),
/// <see cref="OnChoice"/> runs, the chosen line's handler is called and its action runs (handed
/// the handler's chance if it takes one). A line with a state but no handler shows "screwed up."
/// and does nothing.
/// </remarks>
// orig: card-option-lines R043+0x0F2A (150B:049A) draft (function matched) + card-choice-menu R043+0x1BD2 (150B:1142) draft; the loop is the do { card-option-enabled[k] = opt_flag_k; card-option-handler[k] = ...; card-option-lines(settings-block); k = card-choice-menu(card-text-window, card-second-window); local-minus-0a = popup-menu-box ? DS:EB0A[menu-choice-index]() : card-option-handler[k](); switch (k) ... } while (following-card == N) every card script wraps around them (e.g. R044+0x1A50 (19DD:0000) matched); tests: GoldenTraceTests, CardSmokeTests
// C: a card script sets following-card to its own number before its start-up (which may already leave) and loops while it is unchanged; the menu loops while CardContext.Next is the number given (the card's own, or the marker it uses).
// C: before the first menu the script's reset clears card-option-enabled[0..19], points card-option-handler[0..19] at its "screwed up." handler and fills card-item-lines/card-saint-lines with -1; a new menu has every line hidden, lines without a handler are MenuLine.Missing, the pop-up lists are reset with SetCardItems/SetCardSaints.
// C: the original keeps one global set of flags (card-option-flag-0..9, GameState.OptionFlags) for all cards and each card copies some of them
// C: (OptionFlagCopies) into its lines every round: those lines are the global flags here, so a card that leaves one unassigned offers the
// C: previous card's value; the other lines start hidden (the card's reset cleared card-option-enabled) and are the menu's own.
// C: the mouse/keyboard behaviour of card-choice-menu (hover highlight, Shift hint, the item and saint pop-up boxes of option kinds 1 and 2, party-portrait clicks, F1..F6) is the screen's (CardScreen.Choose, see there); the menu hands it the pop-up hints.
public sealed class CardMenu
{
    /// <summary>A card text has at most 20 option lines.</summary>
    public const int MaxLines = 20;

    private readonly CardContext _c;
    private readonly int _stayOn;
    private readonly int _copied;
    private readonly OptionState[] _states = new OptionState[MaxLines];

    /// <param name="c">The card's context.</param>
    /// <param name="stayOn">The menu is offered while <see cref="CardContext.Next"/> is this (the card's own number).</param>
    /// <param name="block">The text block with the option lines.</param>
    /// <param name="copiedFlags">The lines (bit k: line k, k &lt; 10) whose state is the global <c>opt_flag_k</c>.</param>
    /// <param name="staleFlags">The copied lines the card never assigns: they keep what the card before left; the other copied flags start at 0.</param>
    /// <param name="resetsHandlers">The card's reset points all 20 handlers at its "screwed up." handler (false: its reset is empty).</param>
    /// <param name="onOptionsBlock">The block is the game's <see cref="CardContext.OptionsBlock"/> (the card shows <c>card-option-lines(settings-block)</c>);
    /// <paramref name="block"/> null then leaves the value the card before left (the card has not written it yet).</param>
    // C: the card function assigns every copied flag it ever assigns before its loop (OptionFlagCopies); the ports state only the non-zero ones.
    public CardMenu(CardContext c, int stayOn, int? block = 0, int copiedFlags = 0, int staleFlags = 0, bool resetsHandlers = true,
        bool onOptionsBlock = false)
    {
        _c = c;
        if (resetsHandlers)
        {
            Array.Fill(c.OptionHandlers, MenuLine.ScrewedUp);
        }
        _stayOn = stayOn;
        _copied = copiedFlags & 0x3FF;
        _onOptionsBlock = onOptionsBlock;
        if (block is { } b)
        {
            Block = b;
        }
        for (var k = 0; k < 10; k++)
        {
            if (((_copied & ~staleFlags) >> k & 1) != 0)
            {
                c.State.OptionFlags[k] = 0;
            }
        }
    }

    private readonly bool _onOptionsBlock;
    private int _block;

    /// <summary>
    /// The text block with the option lines: for a card that shows <c>card-option-lines(settings-block)</c> the game's
    /// <see cref="CardContext.OptionsBlock"/> (a later card can read what this one left), else the card's own.
    /// </summary>
    // orig: settings-block DS:EE53 (OptionsBlockCards)
    public int Block
    {
        get => _onOptionsBlock ? _c.OptionsBlock : _block;
        set
        {
            if (_onOptionsBlock)
            {
                _c.OptionsBlock = value;
            }
            else
            {
                _block = value;
            }
        }
    }

    /// <summary>
    /// The card's loop draws its option text (<c>card-option-lines</c>); false: it calls only
    /// <c>card-choice-menu</c>, which works on the option lines the screen last laid out (<see cref="ICardScreen.ChooseOnLastLayout"/>).
    /// </summary>
    public bool DrawsOptions { get; set; } = true;

    /// <summary>A line's state: <c>opt_flag_k</c> for a line the card copies from the global flags, else the line's own (starting hidden).</summary>
    public OptionState this[int line]
    {
        get => IsGlobal(line) ? (OptionState)(byte)_c.State.OptionFlags[line] : _states[line];
        set
        {
            if (IsGlobal(line))
            {
                _c.State.OptionFlags[line] = (int)value;
            }
            else
            {
                _states[line] = value;
            }
        }
    }

    private bool IsGlobal(int line) => (_copied >> line & 1) != 0;

    private readonly OptionState?[] _overrides = new OptionState?[MaxLines];

    /// <summary>
    /// Sets the state the screen gets for a line every round without touching <c>opt_flag_k</c>
    /// (a card that writes <c>card-option-enabled[k]</c> after the copy).
    /// </summary>
    public void Override(int line, OptionState state) => _overrides[line] = state;

    /// <summary>Gives several lines one state.</summary>
    public void Set(OptionState state, params int[] lines)
    {
        foreach (var line in lines)
        {
            this[line] = state;
        }
    }

    /// <summary>Enables the given lines.</summary>
    public void Enable(params int[] lines) => Set(OptionState.Enabled, lines);

    /// <summary>Hides every line.</summary>
    public void HideAll()
    {
        for (var k = 0; k < MaxLines; k++)
        {
            this[k] = OptionState.Hidden;
        }
    }

    /// <summary>Moves to another option text: its block, the given lines enabled and all others hidden.</summary>
    public void Offer(int block, params int[] enabled)
    {
        Block = block;
        HideAll();
        Enable(enabled);
    }

    /// <summary>
    /// The hint of a pop-up choice on lines that have no <see cref="MenuLine.Popup"/> of their own
    /// (none: the line's own hint).
    /// </summary>
    public Func<OptionHint>? Popup { get; set; }

    /// <summary>Runs at the start of every round, before the lines are offered; when it moves the card on, nothing is offered.</summary>
    public Action? EachRound { get; set; }

    /// <summary>Run each round between the text block and its lines being shown and the choice (the card script's calls between card-option-lines and card-choice-menu).</summary>
    // orig: e.g. R061+0x2278 `card-option-lines(1); pass-hours(1);`
    public Action? AfterOptions { get; set; }

    /// <summary>Runs once the player has chosen, before the choice is rated.</summary>
    public Action? OnChoice { get; set; }

    /// <summary>
    /// The item box's handler (the card's <c>item-handler-table</c> table): the hint of a choice in the item box,
    /// whatever line opened it, reading <see cref="CardContext.MenuMember"/> and <see cref="CardContext.MenuSlot"/>
    /// (a card's handler for slot j is this handler with <see cref="CardContext.MenuSlot"/> = j). None: the
    /// pop-up hint of the line the box belongs to (<see cref="MenuLine.Popup"/>, else <see cref="Popup"/>, else its hint).
    /// </summary>
    // orig: item-handler-table DS:EC00 (5 x 4 far pointers; every card fills the five rows alike), the handler table of the item box's layout DS:EB5A (+0xA6)
    public Func<OptionHint>? ItemBox { get; set; }

    /// <summary>The saint box's handler (the card's <c>saint-handler-table</c> table); see <see cref="ItemBox"/>.</summary>
    // orig: saint-handler-table DS:ECF6, the handler table of the saint box's layout DS:EC50 (+0xA6)
    public Func<OptionHint>? SaintBox { get; set; }

    /// <summary>
    /// The card's loop rates a pop-up choice with the item table (<c>item-handler-table[0][menu-choice-index]</c>) whichever box
    /// it was made in, not with the box's own table (<c>DS:EB0A[menu-choice-index]</c>).
    /// </summary>
    // orig: the loops of $Count04 (R105), $Balti01 (R111), $Witcd35 (R126+0x2C0E), $Witcd37 (R127+0x07C6), $Witcd39 (R127), $Witcd40 (R128), $MeetH02 (R132+0x034E), $DPost00 (R134); only $Witcd35, $Witcd37 and $MeetH02 have box lines
    public bool PopupRatedByItems { get; set; }

    /// <summary>
    /// Offers the lines (line k is <paramref name="lines"/>[k]) while <see cref="CardContext.Next"/>
    /// stays on the card; nothing is offered when the card has already moved on. Returns the next card.
    /// </summary>
    // C: a box's layout (DS:EA64, copied from DS:EB5A or DS:EC50 when the box opens) holds the box's handler table at +0xA6 (DS:EB0A) and its
    // C: entries' states at +0x2E (DS:EA92). When the pointer enters the box card-mouse-handler copies the whole layout (0xF6 bytes) over the menu's
    // C: (DS:E96E, R043+0x246B), whose +0xA6 is card-option-handler (DS:EA14) and +0x2E card-option-enabled (DS:E99C); a choice in the box (a click, R043+0x27A6, or Enter,
    // C: R043+0x1C8A) returns with the box's layout still there. So after it card-option-handler[k] is the box's handler for slot k % 4 and card-option-enabled[k] the
    // C: state of the box's entry k, for every k, until the card's loop installs its own; Enter leaves popup-menu-box 0, and the loop then rates
    // C: the line with card-option-handler[k], the box's handler for slot k % 4.
    public int Run(params MenuLine[] lines)
    {
        // C: a line the card does not list has no action (its switch has no case) and the handler left in card-option-handler[k].
        MenuLine Line(int k) => k < lines.Length ? lines[k] : MenuLine.Missing;
        // C: a null card-option-handler entry (no card installed one since start-up) would be called through a null pointer; "screwed up." here.
        OptionHint Handler(int k) => (_c.OptionHandlers[k] ?? MenuLine.ScrewedUp)();

        while (_c.Next == _stayOn)
        {
            EachRound?.Invoke();
            if (_c.Next != _stayOn)
            {
                break;
            }
            // C: card-option-enabled[k] = (char)opt_flag_k for the copied lines and card-option-handler[k] = the card's handler for the lines it installs, every round.
            var states = Enumerable.Range(0, MaxLines).Select(k => _overrides[k] ?? this[k]).ToArray();
            for (var k = 0; k < lines.Length && k < MaxLines; k++)
            {
                _c.OptionHandlers[k] = lines[k].AskHint;
            }
            // C: a Shift hint in a box asks card-option-handler[member * 4 + slot], which is then the box's table: the box's handler for the slot.
            _c.MenuBox = PopupBox.None;
            var choice = DrawsOptions
                ? _c.Screen.Choose(Block, states, Handler, k => BoxHint(_c.MenuBox, Line(k)), AfterOptions)
                : _c.Screen.ChooseOnLastLayout(states, Handler, k => BoxHint(_c.MenuBox, Line(k)));
            var line = Line(choice);
            if (_c.MenuBox != PopupBox.None)
            {
                var box = _c.MenuBox;
                var boxLine = line;
                for (var k = 0; k < MaxLines; k++)
                {
                    var slot = k % 4;
                    _c.OptionHandlers[k] = () => SlotHint(box, boxLine, slot);
                    if (!IsGlobal(k))
                    {
                        _states[k] = _c.MenuBoxStates[k];
                    }
                }
            }
            OnChoice?.Invoke();
            // C: local-minus-0a = popup-menu-box ? (*DS:EB0A[menu-choice-index])() : (*card-option-handler[k])(): always called; the action gets local-minus-0a.a.
            var hint = !_c.MenuPopup ? Handler(choice)
                : PopupRatedByItems ? SlotHint(PopupBox.Items, line, _c.MenuSlot)
                : BoxHint(_c.MenuBox, line);
            line.Choose(line.TakesTheChance ? hint.Chance : -1);
        }
        _c.MenuBox = PopupBox.None;
        return _c.Next;
    }

    /// <summary>
    /// The hint of a choice in a pop-up box: the box's handler (<see cref="ItemBox"/> / <see cref="SaintBox"/>),
    /// else the pop-up hint of the line the box belongs to (its own, else the menu's, else the line's hint).
    /// </summary>
    private OptionHint BoxHint(PopupBox box, MenuLine line)
    {
        var rate = (box == PopupBox.Saints ? SaintBox : box == PopupBox.Items ? ItemBox : null) ?? line.Popup ?? Popup;
        return rate is not null ? rate() : line.AskHint();
    }

    /// <summary>The box's handler for one slot (the card's function for that slot): <see cref="BoxHint"/> asked with <see cref="CardContext.MenuSlot"/> = the slot.</summary>
    // orig: card-mouse-handler R043+0x21D4 (150B:1744) draft: the box's layout, handler table included, becomes the menu's (R043+0x246B)
    // C: every card that installs item-handler-table / saint-handler-table fills the five member rows alike (the loops for loop-counter-two < 5), so the row the box's entry or the
    // C: click uses (DS:EB0A[menu-choice-index] is row 0, a Shift hint row menu-owner-member, Enter row k / 4) does not matter.
    // DIVERGENCE (undefined-memory): a slot the card's reset installs no handler for keeps an earlier card's (null since start-up: the original then calls
    // 0000:0000); a click or a Shift hint only reaches the slots the card lists items or saints in, but Enter rates line k with slot k % 4,
    // which can be such a slot. The card's box handler (or the line's pop-up) answers for every slot here.
    private OptionHint SlotHint(PopupBox box, MenuLine line, int slot)
    {
        // C: the handler for slot j is that slot's function; menu-choice-index stays the entry's (MenuEntrySlot), which handlers that read it see.
        var chosen = _c.MenuSlot;
        _c.MenuEntrySlot = chosen;
        _c.MenuSlot = slot;
        try
        {
            return BoxHint(box, line);
        }
        finally
        {
            _c.MenuSlot = chosen;
            _c.ClearMenuEntrySlot();
        }
    }

    /// <summary>
    /// The handler most card files keep for option lines their text does not have: the card's first text
    /// again (unless <paramref name="showFirstText"/> is false), then the end-of-game marker 0x62.
    /// </summary>
    // orig: e.g. R119+0x10C8 (18D0:0748) matched (the repeat-the-opening handler of the R119..R135 card files)
    public static void ShowFirstTextAndEnd(CardContext c, bool showFirstText = true)
    {
        if (showFirstText)
        {
            c.Screen.Text(0);
        }
        c.PassHours(0);
        c.Next = 0x62;
    }

    /// <summary>
    /// The ten-line handler table of a card file whose card function never runs a menu: every line is
    /// <see cref="ShowFirstTextAndEnd"/> with the given hint, and the usual item pop-up, which says the same
    /// message. Cards keep it to preserve the original's dormant handlers.
    /// </summary>
    // C: the file's item pop-up handlers copy the message its line hints copy: DS:4F9A (e.g. R119+0x0670) or DS:4F48 (e.g. R125+0x2B58).
    public static MenuLine[] FirstTextAndEndTable(CardContext c, string hint = CardTexts.Unknown, bool showFirstText = true)
    {
        var lines = new MenuLine[10];
        for (var k = 0; k < lines.Length; k++)
        {
            lines[k] = new MenuLine(_ => ShowFirstTextAndEnd(c, showFirstText), OptionHint.Saying(hint),
                () => c.MemberPopupHint(hint));
        }
        return lines;
    }
}

/// <summary>The hints of the item and saint pop-up boxes most cards share.</summary>
public sealed partial class CardContext
{
    /// <summary>
    /// The usual item pop-up handler: the member picked in the box becomes $ChosenOneName and the
    /// message (by default "Success is assured.") is shown beside them, with no chance.
    /// </summary>
    // orig: the item-handler-table handlers most card files install (e.g. R059+0x0BFC, R119+0x0670 matched): strcpy(text-bank-buffer, ...); r.a = -1; text-slot-chosen-one = r.b = menu-owner-member
    public OptionHint MemberPopupHint(string message = CardTexts.SuccessAssured)
    {
        Variables.Chosen[0] = MenuMember;
        return OptionHint.Say(message, MenuMember);
    }

    /// <summary>
    /// The usual saint pop-up handler: the member who prays becomes $ChosenOneName and the chance
    /// of the prayer is told for the party (<c>chance-line</c> of <c>saint-odds-low/hi</c>).
    /// </summary>
    // orig: the saint-handler-table handlers most cards install (e.g. R059+0x0CEC, R074+0x0AD6 (1838:0AD6) matched): text-slot-chosen-one = menu-owner-member; saint-odds-low, then saint-odds-high inside chance-line; r.a = -1, r.b = -2
    public OptionHint SaintPopupHint()
    {
        Variables.Chosen[0] = MenuMember;
        var saint = CardSaints[MenuEntrySlot];
        var low = SaintChanceLow(MenuMember, saint);
        return OptionHint.Say(ChanceText(low, SaintChanceHigh(MenuMember, saint)));
    }

    /// <summary>
    /// The saint pop-up handler of the cards whose box only tells the chance of the prayer, beside
    /// the member (it does not make the member $ChosenOneName).
    /// </summary>
    // orig: e.g. R119+0x0760 matched: saint-odds-low, saint-odds-high, chance-line; r.a = -1, r.b = menu-owner-member
    public OptionHint SaintPopupChanceHint()
    {
        var saint = CardSaints[MenuEntrySlot];
        var low = SaintChanceLow(MenuMember, saint);
        return OptionHint.Say(ChanceText(low, SaintChanceHigh(MenuMember, saint)), MenuMember);
    }

    /// <summary>
    /// The saint pop-up handler of the cards whose box makes the member who prays $ChosenOneName and
    /// tells the chance of the prayer beside that member.
    /// </summary>
    // orig: e.g. R090+0x13F6 (18E0:0976) matched: text-slot-chosen-one = menu-owner-member; saint-odds-low, saint-odds-high, chance-line; r.a = -1, r.b = menu-owner-member
    public OptionHint SaintPopupMemberHint()
    {
        Variables.Chosen[0] = MenuMember;
        var saint = CardSaints[MenuEntrySlot];
        var low = SaintChanceLow(MenuMember, saint);
        return OptionHint.Say(ChanceText(low, SaintChanceHigh(MenuMember, saint)), MenuMember);
    }
}
