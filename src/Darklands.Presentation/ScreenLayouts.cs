using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// A slot a classic screen declares (the 1.2 view registry, step 9, depth A): a list or an area of its layout, by name, that its
/// drawing and its hit tests both read (the L-9 guard: no hit test against a coordinate outside the screen's slots). The numbers are
/// the original's, so without a pack that moves the slot nothing changes; <paramref name="Hook16a"/> says where a pack's line height
/// or a column's move (plan 16a, CJK pixel fonts) would plug in (null: none foreseen).
/// </summary>
/// <param name="Name">The slot's name in its screen (a pack's <c>interface/layout.json</c> addresses it by it).</param>
/// <param name="Layout">The slot as the original lays it out.</param>
/// <param name="Hook16a">Where a 16a layout entry would act on it.</param>
public sealed record DeclaredSlot(string Name, SlotLayout Layout, string? Hook16a = null)
{
    /// <summary>A list of <paramref name="rows"/> rows of <paramref name="rowHeight"/> pixels at (x, y), <paramref name="width"/> wide.</summary>
    public static DeclaredSlot List(string name, int x, int y, int width, int rows, int rowHeight, string? hook16a = null) =>
        new(name, new SlotLayout(X: x, Y: y, Width: width, Kind: SlotKinds.List, Rows: rows, RowHeight: rowHeight), hook16a);

    /// <summary>An area: a rectangle at (x, y), <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static DeclaredSlot Area(string name, int x, int y, int width, int height, string? hook16a = null) =>
        new(name, new SlotLayout(X: x, Y: y, Width: width, Height: height, Kind: SlotKinds.Area), hook16a);
}

/// <summary>
/// The slots the classic screens declare (the 1.2 view registry, step 9): by screen id (the catalogue's), the lists and areas each
/// screen draws and hit-tests with. A screen here keeps its loop as the original's (its polls and waits); only where it draws its
/// rows and buttons and where it takes a click come from its slots. Kept true by tests: every screen here has a catalogue entry, and
/// none of its source files compares the pointer with a number of its own (ScreenSlotGuardTests).
/// </summary>
// Not in the original: the 1.2 view registry (the modular UI's grounds; plan step 9).
public static class ScreenLayouts
{
    /// <summary>The declared slots by screen id.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DeclaredSlot>> ByScreen { get; } = new Dictionary<string, IReadOnlyList<DeclaredSlot>>(StringComparer.Ordinal)
    {
        [MarchingOrderLayout.Screen] = MarchingOrderLayout.Slots,
        [LoadGameLayout.Screen] = LoadGameLayout.Slots,
        [PartyRosterLayout.Screen] = PartyRosterLayout.Slots,
        [SaintPrayerLayout.Screen] = SaintPrayerLayout.Slots,
        ["BarterScreen"] = BuySellLayout.Slots,
        ["ItemStoreScreen"] = BuySellLayout.Slots,
        ["LootScreen"] = BuySellLayout.Slots,
        [CharacterSheetScreen.ScreenId] = CharacterSheetScreen.DeclaredSlots,
    };

    /// <summary>The slots <paramref name="screen"/> declares (none: a screen still on constants, or not a classic screen).</summary>
    public static IReadOnlyList<DeclaredSlot> Of(string screen) => ByScreen.GetValueOrDefault(screen) ?? [];

    /// <summary>
    /// The row of a list at pixel row <paramref name="y"/> by the original's arithmetic: (y - the list's top) / its row height, rounded
    /// toward zero, with no bounds (a screen that reads a row after the button went up, wherever the pointer is then, gets what the
    /// original got; <see cref="SlotLayout.RowAt"/> is the bounded test).
    /// </summary>
    public static int RowOf(this SlotLayout list, int y)
    {
        ArgumentNullException.ThrowIfNull(list);
        return (y - (list.Y ?? 0)) / (list.RowHeight ?? 1);
    }
}

/// <summary>
/// The load screen's slots: its three buttons (an area, rows of 15) and the saved games' list (an area, rows of 16); the comfort
/// screen's list (rows of 10) and its narrowed buttons take their left edge and width from the plates drawn.
/// </summary>
// orig: R037+0x84BA (LoadGameScreen.Handle): a click at 0x44 < x < 0xc6, 0x2d < y < 0x5a is button (y - 0x2d) / 15; a click in the
//   list (0xc3 < x < 0x140, 0x14 < y < 0xa5) moves the cursor to row (y - 0x14) / 16; the highlight 0xc4, row * 16 + 0x16, 0x7b x 0x10
public static class LoadGameLayout
{
    public const string Screen = "LoadGameScreen";

    /// <summary>The buttons' area: 0x44 &lt; x &lt; 0xc6, 0x2d &lt; y &lt; 0x5a.</summary>
    public static readonly DeclaredSlot Buttons = DeclaredSlot.Area("buttons", 0x45, 0x2E, 0xC6 - 0x45, 0x5A - 0x2E,
        "16a: the labels' column (x 0x52) and the plates' width");

    /// <summary>The buttons' rows: 15 pixels from y 0x2d, the hover highlight two pixels into each.</summary>
    public static readonly DeclaredSlot ButtonRows = DeclaredSlot.List("buttons.rows", 0x45, 0x2D, 0xC6 - 0x45, 3, 15,
        "16a: the row height a pack's line height would set");

    /// <summary>The saved games' list's area: 0xc3 &lt; x &lt; 0x140, 0x14 &lt; y &lt; 0xa5.</summary>
    public static readonly DeclaredSlot List = DeclaredSlot.Area("list", 0xC4, 0x15, 0x140 - 0xC4, 0xA5 - 0x15);

    /// <summary>The saved games' rows: nine of 16 pixels from y 0x14, the highlight 0x7b wide two pixels into each.</summary>
    public static readonly DeclaredSlot ListRows = DeclaredSlot.List("list.rows", 0xC4, 0x14, 0x7B, 9, 16,
        "16a: the row height a pack's line height would set (the comment's and the date's columns)");

    /// <summary>The comfort list's area (its left edge the panel's, 0xc3 when the plates are not narrowed).</summary>
    public static readonly DeclaredSlot ComfortList = DeclaredSlot.Area("comfort.list", 0xC4, 0x13, 0x13B - 0xC4, 0xB0 - 0x13);

    /// <summary>The comfort list's rows: fifteen of 10 pixels from y 0x15.</summary>
    public static readonly DeclaredSlot ComfortRows = DeclaredSlot.List("comfort.list.rows", 0xC5, 0x15, 0x13B - 0xC3 - 3, 15, 10,
        "16a: the row height and the date column");

    public static IReadOnlyList<DeclaredSlot> Slots { get; } = [Buttons, ButtonRows, List, ListRows, ComfortList, ComfortRows];
}

/// <summary>The party roster's slots: its buttons (rows of 15) and the inn's names (rows of 8, the highlight two pixels into a row).</summary>
// orig: R038+0x0000: a click at 0x46 < x < 0xc8, 0x2d < y < 0xa5 is button (y - 0x2d) / 15; at 0xc5 < x < 0x138, 0x14 < y < 0xb4
//   name row (y - 0x14) / 8; the highlight (0xc6, row * 8 + 0x16, 0x71 x 8)
public static class PartyRosterLayout
{
    public const string Screen = "PartyRosterScreen";

    /// <summary>The buttons' area: 0x46 &lt; x &lt; 0xc8, 0x2d &lt; y &lt; 0xa5.</summary>
    public static readonly DeclaredSlot Buttons = DeclaredSlot.Area("buttons", 0x47, 0x2E, 0xC8 - 0x47, 0xA5 - 0x2E,
        "16a: the labels' column (x 0x4a) and the buttons' width");

    /// <summary>The buttons' rows: 15 pixels from y 0x2d.</summary>
    public static readonly DeclaredSlot ButtonRows = DeclaredSlot.List("buttons.rows", 0x47, 0x2D, 0xC8 - 0x47, 8, 15,
        "16a: the row height a pack's line height would set");

    /// <summary>The names' area: 0xc5 &lt; x &lt; 0x138, 0x14 &lt; y &lt; 0xb4.</summary>
    public static readonly DeclaredSlot Names = DeclaredSlot.Area("names", 0xC6, 0x15, 0x138 - 0xC6, 0xB4 - 0x15);

    /// <summary>The names' rows: 8 pixels from y 0x14, the highlight 0x71 wide from x 0xc6, two pixels into each.</summary>
    public static readonly DeclaredSlot NameRows = DeclaredSlot.List("names.rows", 0xC6, 0x14, 0x71, 20, 8,
        "16a: the row height (8, the font's) a CJK pack's line height would set; the '+' (x 199) and the name (x 0xd0) move as one column");

    public static IReadOnlyList<DeclaredSlot> Slots { get; } = [Buttons, ButtonRows, Names, NameRows];
}

/// <summary>
/// The saint screen's slots: the member box (rows of 9, the highlight two pixels into a row), the menu (rows of 8) and the party
/// column (the member's own row leaves the screen).
/// </summary>
// orig: R034 saint screen: the box 250 &lt; x &lt; 310, 142 &lt; y &lt; 192, rows (y - 142) / 9, highlighted at (0xfb, (row + 16) * 9),
//   0x3b x 9; the menu 148 &lt; x &lt; 318, 158 &lt; y &lt; 190, rows (y - 158) / 8 from x 0x94, 0xa9 wide; the column x &lt; 60, row y / 40
public static class SaintPrayerLayout
{
    public const string Screen = "SaintPrayerScreen";

    /// <summary>The member box's area: 250 &lt; x &lt; 310, 142 &lt; y &lt; 192.</summary>
    public static readonly DeclaredSlot MemberBox = DeclaredSlot.Area("members", 251, 143, 310 - 251, 192 - 143,
        "16a: the names' column (x 0xfb) and its width");

    /// <summary>The member box's rows: 9 pixels from y 142, the highlight 0x3b wide from x 0xfb, two pixels into a row.</summary>
    public static readonly DeclaredSlot MemberRows = DeclaredSlot.List("members.rows", 0xFB, 142, 0x3B, 5, 9,
        "16a: the row height (9) a pack's line height would set");

    /// <summary>The menu's area: 148 &lt; x &lt; 318, 158 &lt; y &lt; 190.</summary>
    public static readonly DeclaredSlot Menu = DeclaredSlot.Area("menu", 149, 159, 318 - 149, 190 - 159,
        "16a: the menu lines' column (x 0x94)");

    /// <summary>The menu's rows: four of 8 pixels from y 158, the highlight 0xa9 wide from x 0x94.</summary>
    public static readonly DeclaredSlot MenuRows = DeclaredSlot.List("menu.rows", 0x94, 158, 0xA9, 4, 8,
        "16a: the row height (8, the font's) a pack's line height would set");

    /// <summary>The party column: five rows of 40 pixels, 60 wide.</summary>
    public static readonly DeclaredSlot PartyRows = DeclaredSlot.List("party.rows", 0, 0, PartyPanel.Width, 5, PartyPanel.RowHeight);

    public static IReadOnlyList<DeclaredSlot> Slots { get; } = [MemberBox, MemberRows, Menu, MenuRows, PartyRows];
}

/// <summary>
/// The shops' page (barter, the inn's store, loot: BuySellPage): its menu (rows of 8), the upper list (the shop's, the store's, the
/// pile's) and the lower one (the member's pack), each list's two arrows, and the party column. The same slots on the three screens.
/// </summary>
// orig: R032 / R035 / R040 (BuySellPage): the menu 0x50 &lt; x &lt; 0xe6, 0x1b &lt; y &lt; 0x43, row (y - 0x1b) / 8, highlighted from x 0x50,
//   0x96 wide; the upper list 0x52 &lt; x &lt; 0x10b, 0x55 &lt; y &lt; 0x75, row (y - 0x55) &gt;&gt; 3; the lower 0x72 &lt; x &lt; 0x11c, 0x93 &lt; y &lt; 0xb3,
//   row (y - 0x93) &gt;&gt; 3; the arrows' strips above and below each; the column x &lt; 0x3c, row y / 40
public static class BuySellLayout
{
    public static readonly DeclaredSlot Menu = DeclaredSlot.Area("menu", 0x51, 0x1C, 0xE6 - 0x51, 0x43 - 0x1C,
        "16a: with the purse and shop lines above it (barter: five lines that overlap once a pack widens them), one column a pack's line height moves together");

    public static readonly DeclaredSlot MenuRows = DeclaredSlot.List("menu.rows", 0x50, 0x1B, 0x96, 5, 8,
        "16a: the row height (8, the font's) a pack's line height would set");

    public static readonly DeclaredSlot Upper = DeclaredSlot.Area("upper", 0x53, 0x56, 0x10B - 0x53, 0x75 - 0x56);

    public static readonly DeclaredSlot UpperRows = DeclaredSlot.List("upper.rows", 0x53, 0x55, 0x10B - 0x53, 4, 8,
        "16a: the row height and the price / name / quality columns");

    public static readonly DeclaredSlot Lower = DeclaredSlot.Area("lower", 0x73, 0x94, 0x11C - 0x73, 0xB3 - 0x94);

    public static readonly DeclaredSlot LowerRows = DeclaredSlot.List("lower.rows", 0x73, 0x93, 0x11C - 0x73, 4, 8,
        "16a: the row height and the price / name / count columns");

    public static readonly DeclaredSlot UpperUp = DeclaredSlot.Area("upper.up", 0x53, 0x4D, 0x10B - 0x53, 0x55 - 0x4D);

    public static readonly DeclaredSlot UpperDown = DeclaredSlot.Area("upper.down", 0x53, 0x76, 0x10B - 0x53, 0x7E - 0x76);

    public static readonly DeclaredSlot LowerUp = DeclaredSlot.Area("lower.up", 0x73, 0x8B, 0x12B - 0x73, 0x93 - 0x8B);

    public static readonly DeclaredSlot LowerDown = DeclaredSlot.Area("lower.down", 0x73, 0xB4, 0x12B - 0x73, 0xBC - 0xB4);

    public static readonly DeclaredSlot PartyRows = DeclaredSlot.List("party.rows", 0, 0, 0x3C, 5, 40);

    public static IReadOnlyList<DeclaredSlot> Slots { get; } =
        [Menu, MenuRows, Upper, UpperRows, Lower, LowerRows, UpperUp, UpperDown, LowerUp, LowerDown, PartyRows];
}

/// <summary>The marching order's slots: the party column, a member a row (F1..F5 or a click picks).</summary>
// orig: R022+0x051C: a release at x &lt; 0x3C picks row y / 40 (MarchingOrderScreen); the frames at the rows of the party panel
public static class MarchingOrderLayout
{
    public const string Screen = "MarchingOrderScreen";

    /// <summary>The party column's rows: five of 40 pixels from the top, 60 wide (PartyPanel.Width).</summary>
    public static readonly DeclaredSlot PartyRows = DeclaredSlot.List("party.rows", 0, 0, PartyPanel.Width, 5, PartyPanel.RowHeight);

    public static IReadOnlyList<DeclaredSlot> Slots { get; } = [PartyRows];
}
