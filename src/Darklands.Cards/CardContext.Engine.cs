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

    // ---- item and saint pop-ups (card_items, card_saints) ----------------------------

    /// <summary>Item codes usable on this card (<c>card_items</c>), −1 = none.</summary>
    public int[] CardItems { get; } = [-1, -1, -1, -1];

    /// <summary>Saints that can be invoked on this card (<c>card_saints</c>), −1 = none.</summary>
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
    /// The "use an item" option's state (<c>items_usable</c>): enabled at Basic, or when an able
    /// member carries (or knows the formula of) one of <see cref="CardItems"/>; else shown only.
    /// </summary>
    // orig: items_usable R043+0x2178 (150B:16E8) matched
    // C: loops i < party_size over status-1 members; has_item(i, code) != 0 (carried or formula known).
    public OptionState ItemsUsable() => Usable(CardItems, (i, item) => Inventory.Has(State, i, item) != 0);

    /// <summary>The "call a saint" option's state (<c>saints_usable</c>): an able member knows one of <see cref="CardSaints"/>.</summary>
    // orig: saints_usable R043+0x211C (150B:168C) matched
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

    /// <summary>The member chosen in the item/saint pop-up (<c>menu_member</c>); the screen sets it.</summary>
    public int MenuMember { get; set; }

    /// <summary>
    /// The engine's one table of option handlers (<c>opt_fn[0..19]</c>): each card's reset points them
    /// at its "screwed up." handler and its loop installs its own every round; a line a card does not
    /// install keeps the handler an earlier card left (null: none since start-up).
    /// </summary>
    // orig: opt_fn DS:EA14 (20 far pointers, BSS)
    public Func<OptionHint>?[] OptionHandlers { get; } = new Func<OptionHint>?[CardMenu.MaxLines];

    /// <summary>The item/saint slot chosen in the pop-up (<c>menu_slot</c>).</summary>
    public int MenuSlot { get; set; }

    /// <summary>The choice came from the pop-up, not from an option line (<c>menu_popup</c>).</summary>
    public bool MenuPopup { get; set; }

    /// <summary>
    /// The pop-up boxes list every item and saint of the card for every member who can act, as if
    /// each had them, all choosable (a library, an alchemist or a city lord teaching them).
    /// </summary>
    // orig: DS:5204, read by the pop-up text R043+0x2CE2; set to 1 around the menu loop by R058+0x0928, R064+0x2D38 and R089+0x0968 and back to 0 when it ends
    public bool PopupListsAll { get; set; }

    /// <summary>
    /// Prays to a saint for a member and redraws the card (<c>invoke_saint</c>): the prayer
    /// screen is the presentation's; until it is set, the prayer fails (returns 0).
    /// </summary>
    // orig: invoke_saint R029+0x2180 (0E76:2180) matched
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
    /// "Chance of success is X% to Y%" (<c>chance_text</c>): numbers at Basic, quality words
    /// (per 20 points) at Standard, nothing at Expert; the upper chance is at most 100.
    /// </summary>
    // orig: chance_text R029+0x5B62 (1367:0C52) matched
    // C: quality_words[lo / 20] (DS:4FCE, 7 near pointers) is not range-checked: the words next to it are pointers to empty strings in
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
    /// Puts a name into $Named1..5 (<c>name_to_slot</c>): 0 an item, 1 a saint, 2 a formula (the
    /// card passes an item code 0x5F and up), 3/4 the bell of an hour, 5 the next bell, 6 a dragon,
    /// 7 a dwarf, 8 a merchant trade or house.
    /// </summary>
    // orig: name_to_slot R029+0x1FB8 (0E76:1FB8) matched (checked against asm: kind 2 reads es:[bx+15BA] with bx = 4 x index)
    // C: kind 2 reads the far pointer at 2E38:15BA + 4 x index. Nothing writes that address: it is the formula-name table
    // virtue_names (2E38:1736, 66 far pointers filled from DARKLAND.LST by world_load) seen 0x5F entries early (1736 - 15BA = 0x17C = 95 x 4),
    // so index 0x5F + n is the full name of formula n; R089 passes 0x64..0x74 (formulas 5..21) and the table goes on into saint_names (183E) after formula 65.
    // C: kind 6, 7 and 8 read the far pointers at 290E:1FA7, 1FE7 and 2107 (16, 38 and 31 strings, see PersonalNamesDecoder).
    // DIVERGENCE: for kinds 0..2 an index past its table (or a kind-2 index below 0x5F, which reads the unwritten bytes before virtue_names) keeps the slot's text; kinds 6..8 read the static pointers next to their tables as the original does (TablePersonalNames.Name); without the EXE's tables kinds 6..8 keep the slot's text.
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
            // C: bell_name(hour_band(index), slot) (R029+0x5756 matched: _fstrcpy(dst, bell_names[i])).
            3 or 4 => bells[TimeOfDay.Band(index)],
            5 => bells[TimeOfDay.Band(index) + 1 > 7 ? 0 : TimeOfDay.Band(index) + 1],
            6 => Table(NameTable.Dragons),
            7 => Table(NameTable.Dwarfs),
            8 => Table(NameTable.Titles),
            _ => Variables.Named[slot],
        };
    }

    /// <summary>2E38:1736 (virtue_names) is 0x5F far pointers after 2E38:15BA, the base <c>name_to_slot</c> kind 2 addresses.</summary>
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

    /// <summary>Lets days pass (<c>advance_days</c>).</summary>
    // orig: advance_days R029+0x5590 (1367:0680) matched
    public void PassDays(int days) => TimePassage.AdvanceDays(State, days);

    /// <summary>Waits until the given hour (<c>wait_until_hour</c>).</summary>
    // orig: wait_until_hour R029+0x5626 (1367:0716) matched; hours_until R029+0x577A (1367:086A) matched
    public void WaitUntilHour(int hour) => PassHours(TimeOfDay.HoursUntil(State.Date.Hour, hour));

    /// <summary>05:00 to 18:59 (<c>is_daylight</c>).</summary>
    // orig: is_daylight R029+0x563A (1367:072A) matched
    public bool IsDaylight => State.Date.IsDaylight;

    /// <summary>0..n−1 by scaling (<c>random_n</c>).</summary>
    // orig: random_n root 0410:0008 (0410:0008) matched
    public int RandomScaled(int below) => State.Random.Scaled(below);
}
