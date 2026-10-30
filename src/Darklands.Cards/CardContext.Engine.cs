using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;
using Darklands.Core.Time;

namespace Darklands.Cards;

/// <summary>Card-engine helpers: item and saint pop-ups, chance texts, time.</summary>
public sealed partial class CardContext
{
    /// <summary>The rules use the world's item list and city records and report through the card screen.</summary>
    public bool RulesConnected => _rulesConnected;

    private static bool ConnectRules(GameState state, WorldData world, ICardScreen screen)
    {
        if (state.Data is Core.World.NoGameData)
        {
            state.Data = world;
        }
        if (state.Messages is GameMessageLog)
        {
            state.Messages = new ScreenMessages(screen);
        }
        return true;
    }

    /// <summary>Rule messages shown on the card screen.</summary>
    private sealed class ScreenMessages(ICardScreen screen) : IGameMessages
    {
        public void Show(string text, int ticks) => screen.ShowMessage(text, ticks);

        public void Report(int member, string text, bool good) => screen.Tell(text, member);
    }

    // ---- item and saint pop-ups (card-item-lines, card-saint-lines) ----------------------------

    /// <summary>Item codes usable on this card (<c>card-item-lines</c>), −1 = none.</summary>
    public int[] CardItems { get; } = [-1, -1, -1, -1];

    /// <summary>Saints that can be invoked on this card (<c>card-saint-lines</c>), −1 = none.</summary>
    public int[] CardSaints { get; } = [-1, -1, -1, -1];

    /// <summary>Sets the card's usable items (up to four; the rest become −1).</summary>
    public void SetCardItems(params int[] items) => Fill(CardItems, items);

    /// <summary>Sets the card's invokable saints (up to four; the rest become −1).</summary>
    public void SetCardSaints(params int[] saints) => Fill(CardSaints, saints);

    private static void Fill(int[] slots, int[] values)
    {
        for (var k = 0; k < slots.Length; k++)
        {
            slots[k] = k < values.Length ? values[k] : -1;
        }
    }

    /// <summary>
    /// The "use an item" option's state (<c>usable-item-list</c>): enabled at Basic, or when an able
    /// member carries (or knows the formula of) one of <see cref="CardItems"/>; else shown only.
    /// </summary>
    // orig: usable-item-list R043+0x2178 (150B:16E8) matched
    // C: loops i < party-headcount over status-1 members; carries-item(i, code) != 0 (carried or formula known).
    public OptionState ItemsUsable() => Usable(CardItems, (i, item) => Inventory.Has(State, i, item) != 0);

    /// <summary>The "call a saint" option's state (<c>saints-available</c>): an able member knows one of <see cref="CardSaints"/>.</summary>
    // orig: saints-available R043+0x211C (150B:168C) matched
    public OptionState SaintsUsable() => Usable(CardSaints, (i, saint) => Saints.Knows(State, i, saint));

    private OptionState Usable(int[] list, Func<int, int, bool> has)
    {
        if (State.Difficulty == Difficulty.Basic)
        {
            return OptionState.Enabled;
        }
        for (var i = 0; i < State.Party.Members.Count; i++)
        {
            if (State.Party.Members[i].Status == CharacterStatus.Able && list.Any(x => x != -1 && has(i, x)))
            {
                return OptionState.Enabled;
            }
        }
        return OptionState.Shown;
    }

    /// <summary>The member chosen in the item/saint pop-up (<c>menu-owner-member</c>); the screen sets it.</summary>
    public int MenuMember { get; set; }

    /// <summary>
    /// The engine's one table of option handlers (<c>card-option-handler[0..19]</c>): each card's reset points them
    /// at its "screwed up." handler and its loop installs its own every round; a line a card does not
    /// install keeps the handler an earlier card left (null: none since start-up).
    /// </summary>
    // orig: card-option-handler DS:EA14 (20 far pointers, BSS)
    public Func<OptionHint>?[] OptionHandlers { get; } = new Func<OptionHint>?[CardMenu.MaxLines];

    /// <summary>The item/saint slot chosen in the pop-up (<c>menu-choice-index</c>).</summary>
    public int MenuSlot { get; set; }

    /// <summary>
    /// <c>menu-choice-index</c> as the box left it: the same as <see cref="MenuSlot"/>, except while the menu asks the box's handler for another
    /// slot (Enter in a box rates line k with the handler for slot k % 4, <see cref="CardMenu"/>): <see cref="MenuSlot"/> then names that
    /// handler, and a handler that reads <c>menu-choice-index</c> (most saint handlers and some item handlers) reads this.
    /// </summary>
    // orig: menu-choice-index DS:EE6A, set from the box's entry by card-choice-menu R043+0x1CAE on Enter; the handler card-option-handler[k] reads it as it stands
    public int MenuEntrySlot
    {
        get => _menuEntrySlot ?? MenuSlot;
        internal set => _menuEntrySlot = value;
    }

    private int? _menuEntrySlot;

    /// <summary>Ends <see cref="MenuEntrySlot"/>'s difference from <see cref="MenuSlot"/> (CardMenu, after asking another slot's handler).</summary>
    internal void ClearMenuEntrySlot() => _menuEntrySlot = null;

    /// <summary>
    /// A box handler that is a bare <c>retf</c> (installed for a slot the card lists no item or saint in): its chance is what AX holds at
    /// the call, which pointer-hide (root 047C:12C3) leaves, and it names no member. Only Enter in a box reaches such a slot (line k rated
    /// with the handler for slot k % 4); the box is reached with the pointer, so pointer-hide then runs the display driver's hide.
    /// </summary>
    // orig: e.g. R112+0x1438 / R112+0x143A / R108+0x0D30 retf; the loop: si = card-choice-menu(..); pointer-hide(); local-minus-0a = (*card-option-handler[si])() (R112+0x06ED..0x0700)
    // DIVERGENCE (undefined-register): AX after pointer-hide with the pointer drawn is whatever the display driver's hide routine (reached through
    // root 047C:052E) returns (with no pointer drawn it would be the line k). 0 is used, as the card oracle's pointer-hide returns.
    public OptionHint EmptyBoxHandler() => OptionHint.ChanceOf(0, OptionHint.WholeParty);

    /// <summary>The choice came from the pop-up, not from an option line (<c>popup-menu-box</c>).</summary>
    public bool MenuPopup { get; set; }

    /// <summary>
    /// The item or saint box whose list the menu was left in (<see cref="PopupBox.None"/>: the main
    /// lines). The screen sets it while the pointer is in a box (a Shift hint there asks the box's
    /// handlers) and leaves it set when the choice is made there, by a click or by Enter.
    /// </summary>
    // orig: DS:EE42 (1 while the box's layout DS:EA64 is the menu's layout DS:E96E), with the kind of the box copied into DS:EA64 (DS:EB5A items, DS:EC50 saints) by card-mouse-handler R043+0x239C
    public PopupBox MenuBox { get; set; }

    /// <summary>The states of the box's 20 entries (member × 4 + slot) as the box was opened (<see cref="PopupStates"/>).</summary>
    // orig: DS:EA92 (the box layout's card-option-enabled[20], written by R043+0x2CE2)
    public OptionState[] MenuBoxStates { get; } = new OptionState[CardMenu.MaxLines];

    /// <summary>
    /// The states of an item (<paramref name="saints"/> false) or saint box's entries: entry
    /// member × 4 + slot is enabled when the member has the item or knows the saint (every one while
    /// <see cref="PopupListsAll"/>), shown greyed for the leader at Basic who lacks it and for a
    /// known formula (not at Expert), hidden otherwise.
    /// </summary>
    // orig: the stores into DS:EA92 + i * 4 + j of R043+0x2CE2 (150B:2252) draft (bytes match): all 20 cleared, then for i < party-headcount of a status other than 0 and 5 and j with card-item-lines/card-saint-lines[j] != -1
    public OptionState[] PopupStates(bool saints)
    {
        var states = new OptionState[CardMenu.MaxLines];
        var list = saints ? CardSaints : CardItems;
        var basic = State.Difficulty == Difficulty.Basic;
        var members = State.Party.Members;
        for (var i = 0; i < members.Count && i < 5; i++)
        {
            if (members[i].Status is CharacterStatus.Empty or CharacterStatus.Away)
            {
                continue;
            }
            for (var j = 0; j < 4; j++)
            {
                var entry = list[j];
                if (entry == -1)
                {
                    continue;
                }
                var has = saints ? (Saints.Knows(State, i, entry) ? 1 : 0) : Inventory.Has(State, i, entry);
                var leaderAtBasic = basic && State.Party.LeaderIndex == i;
                if (has == 0 && !leaderAtBasic && !PopupListsAll)
                {
                    continue;
                }
                states[i * 4 + j] = leaderAtBasic && has == 0 && !PopupListsAll ? OptionState.Shown
                    : !saints && State.Difficulty != Difficulty.Expert && has == 2 && !PopupListsAll ? OptionState.Shown
                    : saints || has == 1 || PopupListsAll ? OptionState.Enabled
                    : OptionState.Hidden;
            }
        }
        return states;
    }

    /// <summary>
    /// The pop-up boxes list every item and saint of the card for every member who can act, as if
    /// each had them, all choosable (a library, an alchemist or a city lord teaching them).
    /// </summary>
    // orig: DS:5204, read by the pop-up text R043+0x2CE2; set to 1 around the menu loop by R058+0x0928, R064+0x2D38 and R089+0x0968 and back to 0 when it ends
    public bool PopupListsAll { get; set; }

    /// <summary>
    /// Prays to a saint for a member and redraws the card (<c>call-on-saint</c>): the prayer
    /// screen is the presentation's; until it is set, the prayer fails (returns 0).
    /// </summary>
    // orig: call-on-saint R029+0x2180 (0E76:2180) matched
    // DIVERGENCE: the prayer screen (thunk 09C0:1EAF, then the card redraw thunk 09C0:1EA5) is the presentation's SaintPrayer; without it the prayer fails (0). In play DarklandsGame attaches SaintPrayerScreen to every card context it makes (DarklandsGame.cs, right after the CardContext is created), so no card runs without it.
    public int InvokeSaint(int who, int saint) => SaintPrayer?.Invoke(who, saint) ?? 0;

    /// <summary>The prayer screen (member, saint → result), supplied by the presentation.</summary>
    public Func<int, int, int>? SaintPrayer { get; set; }

    /// <summary>The saints' help, used by a successful prayer.</summary>
    public ISaintEffects SaintEffects { get; set; } = NoSaintEffects.Instance;

    // ---- texts ----------------------------------------------------------------------

    /// <summary>The quality words of chance texts at Standard difficulty.</summary>
    public static readonly string[] QualityWords = ["very poor", "poor", "fair", "good", "very good", "excellent", "superb"];

    /// <summary>
    /// "Chance of success is X% to Y%" (<c>chance-line</c>): numbers at Basic, quality words
    /// (per 20 points) at Standard, nothing at Expert; the upper chance is at most 100.
    /// </summary>
    // orig: chance-line R029+0x5B62 (1367:0C52) matched
    // C: grade-words[lo / 20] (DS:4FCE, 7 near pointers) is not range-checked: the words next to it are pointers to empty strings in
    // the data image (index 7: DS:6F43, −1: DS:0000, −2: DS:7461, all zero bytes), so a chance of 140..159 or −40..−20 says nothing.
    // DIVERGENCE: an index beyond 7 or below −2 (a chance of 160 and up, or below −40; callers pass clamped chances) is clamped to 0..6.
    // C: only hi is limited to 100; the text is appended to the caller's buffer there.
    public string ChanceText(int low, int high)
    {
        if (State.Difficulty == Difficulty.Expert)
        {
            return "";
        }
        high = Math.Min(high, 100);
        string Word(int k) => k switch
        {
            >= 0 and <= 6 => QualityWords[k],
            7 or -1 or -2 => "",
            _ => QualityWords[Math.Clamp(k, 0, 6)],
        };
        string Say(int v) => State.Difficulty == Difficulty.Basic ? $"{v}%" : Word(v / 20);
        return $"Chance of success is {Say(low)} to {Say(high)}";
    }

    /// <summary>
    /// Puts a name into $Named1..5 (<c>label-to-slot</c>): 0 an item, 1 a saint, 2 a formula (the
    /// card passes an item code 0x5F and up), 3/4 the bell of an hour, 5 the next bell, 6 a dragon,
    /// 7 a dwarf, 8 a merchant trade or house.
    /// </summary>
    // orig: label-to-slot R029+0x1FB8 (0E76:1FB8) matched (checked against asm: kind 2 reads es:[bx+15BA] with bx = 4 x index)
    // C: kind 2 reads the far pointer at 2E38:15BA + 4 x index. Nothing writes that address: it is the formula-name table
    // virtue-label-table (2E38:1736, 66 far pointers filled from DARKLAND.LST by world-read) seen 0x5F entries early (1736 - 15BA = 0x17C = 95 x 4),
    // so index 0x5F + n is the full name of formula n; R089 passes 0x64..0x74 (formulas 5..21) and the table goes on into saint-label-table (183E) after formula 65.
    // C: kind 6, 7 and 8 read the far pointers at 290E:1FA7, 1FE7 and 2107 (16, 38 and 31 strings, see PersonalNamesDecoder).
    // DIVERGENCE: for kinds 0..2 an index past its table (or a kind-2 index below 0x5F, which reads the unwritten bytes before virtue-label-table) keeps the slot's text; kinds 6..8 read the static pointers next to their tables as the original does (TablePersonalNames.Name); without the EXE's tables kinds 6..8 keep the slot's text.
    // DORMANT: kinds above 8 copy nothing in the original either.
    public void NameToSlot(int kind, int index, int slot)
    {
        var bells = TextVariables.BellNames;
        // C: the tables are read unchecked (IPersonalNames.Name reads the neighbouring pointers); without the EXE's tables the slot keeps its text
        string Table(NameTable table) => PersonalNames.Count(table) > 0 ? PersonalNames.Name(table, index) : Variables.Named[slot];
        Variables.Named[slot] = kind switch
        {
            0 => World.ItemName(index),
            1 => index >= 0 && index < World.Items.Saints.Count ? World.Items.Saints[index].FullName : "",
            2 => FormulaOrSaintName(index - FormulaTableBase) ?? Variables.Named[slot],
            // C: bell-time-label(time-of-day-band(index), slot) (R029+0x5756 matched: _fstrcpy(dst, bell-time-labels[i])).
            3 or 4 => bells[TimeOfDay.Band(index)],
            5 => bells[TimeOfDay.Band(index) + 1 > 7 ? 0 : TimeOfDay.Band(index) + 1],
            6 => Table(NameTable.Dragons),
            7 => Table(NameTable.Dwarfs),
            8 => Table(NameTable.Titles),
            _ => Variables.Named[slot],
        };
    }

    /// <summary>2E38:1736 (virtue-label-table) is 0x5F far pointers after 2E38:15BA, the base <c>label-to-slot</c> kind 2 addresses.</summary>
    private const int FormulaTableBase = 0x5F;

    private string? FormulaOrSaintName(int n)
    {
        var formulae = World.Items.Formulae;
        var saints = World.Items.Saints;
        if (n >= 0 && n < formulae.Count)
        {
            return formulae[n].FullName;
        }
        n -= formulae.Count;
        return n >= 0 && n < saints.Count ? saints[n].FullName : null;
    }

    // ---- time -----------------------------------------------------------------------

    /// <summary>Lets days pass (<c>pass-days</c>).</summary>
    // orig: pass-days R029+0x5590 (1367:0680) matched
    public void PassDays(int days) => TimePassage.AdvanceDays(State, days);

    /// <summary>Waits until the given hour (<c>wait-for-hour</c>).</summary>
    // orig: wait-for-hour R029+0x5626 (1367:0716) matched; hours-remaining-until R029+0x577A (1367:086A) matched
    public void WaitUntilHour(int hour) => PassHours(TimeOfDay.HoursUntil(State.Date.Hour, hour));

    /// <summary>05:00 to 18:59 (<c>daylight-test</c>).</summary>
    // orig: daylight-test R029+0x563A (1367:072A) matched
    public bool IsDaylight => State.Date.IsDaylight;

    /// <summary>0..n−1 by scaling (<c>roll-n</c>).</summary>
    // orig: roll-n root 0410:0008 (0410:0008) matched
    public int RandomScaled(int below) => State.Random.Scaled(below);
}
