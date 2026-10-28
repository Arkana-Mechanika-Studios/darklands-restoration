using System.Text;
using Darklands.Core.Characters;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The card screen of the card oracle: the same input as the original under tools/oracle/card_oracle.py
/// and a trace in the same words. Texts are recorded as the bytes the player would read (the block
/// after the $variables are expanded); a menu with its option states (the block's option lines), and
/// probing, every offered line's hint as Shift over the line asks it (the original's card_mouse sets
/// menu_member = line / 4 and menu_slot = line % 4 before it calls the line's handler); an item or
/// saint line is chosen in its pop-up box the way <see cref="TracingScreen"/> does.
/// </summary>
public sealed class OracleScreen(int option, bool probe, HandCall? hand = null) : ICardScreen
{
    private static readonly object MessagesLock = new();
    private int _menus;
    private string _card = "";

    public CardContext? Context { get; set; }

    /// <summary>The line (block, line) to choose whatever its state, in the first menu of that block (once; see CardOracleStates.ForceOf).</summary>
    public (int Block, int Line)? Force { get; set; }

    /// <summary>The choices of the first menus (the variant part <c>P2_0_1</c>, see CardOracleStates.PathOf): the n-th menu chooses the (value modulo the count)-th enabled line; later menus follow the rotation policy.</summary>
    public int[]? Path { get; set; }

    public List<string> Log { get; } = [];

    /// <summary>"show" when the texts are another card's than the ones shown, else "picture" (as the oracle words card_load and card_load_near).</summary>
    public void Show(string cardName, string picture)
    {
        Log.Add(cardName == _card ? $"picture {picture.ToLowerInvariant()}" : $"show {cardName} {picture.ToLowerInvariant()}");
        // card_load takes pic_buf, the picture the rules redraw after a battle or a prayer ("redraw" in the original's
        // trace); the comparison reads this marker and drops it
        Log.Add($"main {picture.ToLowerInvariant()}");
        _card = cardName;
        _main = picture;
    }

    /// <summary>pic_buf: empty in the oracle's start states (the original's card runs first after the load).</summary>
    private string _main = "";

    public void ShowWithTheLastPicture(string cardName) => Show(cardName, _main);

    public void ShowPicture(string picture) => Log.Add($"picture {picture.ToLowerInvariant()}");

    public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Log.Add($"text {block} {(int)frame} {Expanded(block)}");

    private IReadOnlyList<Content.MessageBlock> Blocks()
    {
        if (CardHarness.Assets is not { } assets || _card.Length == 0)
        {
            return [];
        }
        // GameAssets caches the message files in a plain dictionary: the cards run in parallel
        lock (MessagesLock)
        {
            try
            {
                return assets.Messages(_card).Blocks;
            }
            catch (KeyNotFoundException)
            {
                return [];
            }
        }
    }

    private string Expanded(int block)
    {
        var blocks = Blocks();
        if (Context is null || block < 0 || block >= blocks.Count)
        {
            return "";
        }
        // msg_file: the card's .MSG as the oracle loads it (the text variables can read it through party slots past the fifth)
        lock (MessagesLock)
        {
            Context.MessageFileBytes = CardHarness.Assets!.Messages(_card).Bytes;
        }
        return Convert.ToHexString(TextVariables.Expand(blocks[block].Text, Context)).ToLowerInvariant();
    }

    /// <summary>The option lines of a block (its option markers), at most 20.</summary>
    private int Lines(int block)
    {
        var blocks = Blocks();
        return block < 0 || block >= blocks.Count ? 20 : Math.Min(20, blocks[block].Text.Count(b => b is 0x15 or 0x10 or 0x16 or 0x06));
    }

    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
    {
        if (++_menus > 50)
        {
            throw new InvalidOperationException("The card keeps asking: it never moves on.");
        }
        var lines = Lines(block);
        var states = options.Take(lines).ToList();
        Log.Add($"menu {block} [{string.Concat(states.Select(o => (int)o))}] {Expanded(block)}");
        if (hand is { } call && _menus == call.Menu)
        {
            return Call(block, call, hint);
        }
        var enabled = Enumerable.Range(0, states.Count).Where(i => states[i] == OptionState.Enabled).ToList();
        int choice;
        var forced = Force is { } f && f.Block == block && f.Line < states.Count;
        if (forced)
        {
            choice = Force!.Value.Line;
            Force = null;
        }
        else
        {
            if (enabled.Count == 0)
            {
                throw new InvalidOperationException($"Menu {block} offers no option.");
            }
            if (Path is { } path && _menus - 1 < path.Length)
            {
                choice = enabled[path[_menus - 1] % enabled.Count];
            }
            else
            {
                var first = option >= 0 && enabled.Contains(option) ? enabled.IndexOf(option) : 0;
                choice = enabled[(first + _menus - 1) % enabled.Count];
            }
        }
        if (probe && hint is not null)
        {
            for (var i = 0; i < states.Count; i++)
            {
                if (states[i] != OptionState.Hidden || (forced && i == choice))
                {
                    if (Context is { } cx)
                    {
                        cx.MenuMember = i / 4;
                        cx.MenuSlot = i % 4;
                    }
                    Log.Add($"hint {i}: {Say(hint(i))}");
                }
            }
        }
        if (BoxProbe)
        {
            ProbeBoxes(block);
        }
        if (Context is { } c)
        {
            c.MenuPopup = false;
            if (probe && PopupKind(block, choice) is { } saints && PopupChoice(c, saints) is var (member, slot))
            {
                BoxChoice(c, saints, member, slot);
            }
        }
        Log.Add($"choose {choice}");
        return choice;
    }

    /// <summary>
    /// A handler call (tools/oracle/card_oracle.py hand_menu): the card gets the line whatever its state, as its loop's
    /// <c>k = card_menu(..)</c> would return it, and then calls the line's handler and runs its action. With
    /// <see cref="HandCall.Hints"/> every line's hint (0..19) is asked first; with <see cref="HandCall.Slot"/> the choice is made in
    /// the line's item or saint pop-up box on that slot, whoever holds the item.
    /// </summary>
    private int Call(int block, HandCall call, Func<int, OptionHint>? hint)
    {
        if (Context is { } c)
        {
            if (call.Hints && hint is not null)
            {
                for (var i = 0; i < CardMenu.MaxLines; i++)
                {
                    c.MenuMember = i / 4;
                    c.MenuSlot = i % 4;
                    Log.Add($"hint {i}: {Say(hint(i))}");
                }
            }
            c.MenuPopup = false;
            if (call.Slot is { } slot && PopupKind(block, call.Line) is { } saints)
            {
                BoxChoice(c, saints, PopupMember(c, saints, slot), slot);
            }
        }
        Log.Add($"choose {call.Line}");
        return call.Line;
    }

    /// <summary>DARKLANDS_CARD_ORACLE_BOXKEY=1: a choice in a pop-up box is made with Enter (menu_popup stays 0), as card_oracle.py.</summary>
    private static readonly bool BoxKey = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_BOXKEY") == "1";

    /// <summary>DARKLANDS_CARD_ORACLE_BOXES=1: every menu asks each item/saint line's box handlers (trace lines "box &lt;line&gt; &lt;member&gt; &lt;slot&gt;"), as card_oracle.py.</summary>
    private static readonly bool BoxProbe = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_BOXES") == "1";

    private Func<int, OptionHint>? _popupHint;

    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint)
    {
        _popupHint = popupHint;
        return Choose(block, options, hint);
    }

    /// <summary>
    /// A choice in the line's box as card_mouse leaves it (card_oracle.py box_choice): the pointer is in the box, so the menu's
    /// layout is the box's (its handler table and its entries' states), and the entry was clicked (menu_popup 1) or, with
    /// <see cref="BoxKey"/>, chosen with Enter.
    /// </summary>
    private static void BoxChoice(CardContext c, bool saints, int member, int slot)
    {
        c.MenuBox = saints ? PopupBox.Saints : PopupBox.Items;
        c.PopupStates(saints).CopyTo(c.MenuBoxStates, 0);
        c.MenuPopup = !BoxKey;
        c.MenuMember = member;
        c.MenuSlot = slot;
    }

    /// <summary><see cref="BoxProbe"/>: for each item/saint line of the block, its box's hint for every present member and listed slot, and $ChosenOneName after it.</summary>
    private void ProbeBoxes(int block)
    {
        if (Context is not { } c || _popupHint is not { } popup)
        {
            return;
        }
        var blocks = Blocks();
        if (block < 0 || block >= blocks.Count)
        {
            return;
        }
        var markers = blocks[block].Text.Where(b => b is 0x15 or 0x10 or 0x16 or 0x06).Take(CardMenu.MaxLines).ToList();
        for (var k = 0; k < markers.Count; k++)
        {
            if (markers[k] is not (0x10 or 0x16))
            {
                continue;
            }
            var saints = markers[k] == 0x16;
            var list = saints ? c.CardSaints : c.CardItems;
            for (var i = 0; i < c.State.Party.Members.Count; i++)
            {
                if (c.State.Party.Members[i].Status is CharacterStatus.Empty or CharacterStatus.Away)
                {
                    continue;
                }
                for (var j = 0; j < 4; j++)
                {
                    if (list[j] == -1)
                    {
                        continue;
                    }
                    c.MenuBox = saints ? PopupBox.Saints : PopupBox.Items;
                    c.MenuMember = i;
                    c.MenuSlot = j;
                    var r = popup(k);
                    c.MenuBox = PopupBox.None;
                    Log.Add($"box {k} {i} {j}: {Say(r)} c{c.Variables.Chosen[0]}");
                }
            }
        }
    }

    /// <summary>The member a pop-up box gives a slot: the last who has the item / knows the saint, else the last present member.</summary>
    private static int PopupMember(CardContext c, bool saints, int slot)
    {
        var list = saints ? c.CardSaints : c.CardItems;
        var present = Enumerable.Range(0, c.State.Party.Members.Count).Reverse()
            .Where(i => c.State.Party.Members[i].Status is not (CharacterStatus.Empty or CharacterStatus.Away)).ToList();
        if (list[slot] != -1)
        {
            foreach (var i in present)
            {
                if (c.PopupListsAll || (saints ? Core.Rules.Saints.Knows(c.State, i, list[slot])
                    : Core.Rules.Inventory.Has(c.State, i, list[slot]) == 1))
                {
                    return i;
                }
            }
        }
        return present.Count > 0 ? present[0] : 0;
    }

    private bool? PopupKind(int block, int line)
    {
        var blocks = Blocks();
        if (block < 0 || block >= blocks.Count)
        {
            return null;
        }
        var markers = blocks[block].Text.Where(b => b is 0x15 or 0x10 or 0x16 or 0x06).ToList();
        return line < markers.Count ? markers[line] switch { 0x10 => false, 0x16 => true, _ => null } : null;
    }

    private static (int Member, int Slot)? PopupChoice(CardContext c, bool saints)
    {
        var list = saints ? c.CardSaints : c.CardItems;
        for (var i = c.State.Party.Members.Count - 1; i >= 0; i--)
        {
            if (c.State.Party.Members[i].Status is CharacterStatus.Empty or CharacterStatus.Away)
            {
                continue;
            }
            for (var j = 0; j < 4; j++)
            {
                if (list[j] != -1 && (c.PopupListsAll || (saints
                        ? Core.Rules.Saints.Knows(c.State, i, list[j])
                        : Core.Rules.Inventory.Has(c.State, i, list[j]) == 1)))
                {
                    return (i, j);
                }
            }
        }
        return null;
    }

    /// <summary>The texts are a card's, without a picture being shown (a direct handler call, see <see cref="CardOracleTests.Run"/>).</summary>
    public void UseTexts(string cardName) => _card = cardName;

    public static string Describe(OptionHint h) => Say(h);

    private static string Say(OptionHint h) => $"{h.Chance} '{(h.Chance == -1 ? h.Message : "")}' {h.Member}";

    public void Tell(string message, int member = OptionHint.WholeParty) => Log.Add($"tell {member} {message}");

    public void PassTime(int hours) => Log.Add($"time {hours}");
}

/// <summary>A handler call (run name <c>hand &lt;key&gt; M1L7[H][S2]</c>): at menu number <see cref="Menu"/> the card gets line <see cref="Line"/>.</summary>
public sealed record HandCall(int Menu, int Line, bool Hints, int? Slot)
{
    public static HandCall Parse(string spec)
    {
        var m = System.Text.RegularExpressions.Regex.Match(spec, @"^M(\d+)L(\d+)(H)?(?:S(\d))?$");
        if (!m.Success)
        {
            throw new ArgumentException(spec);
        }
        return new HandCall(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success,
            m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : null);
    }
}

/// <summary>The full screens a card opens, recorded (the oracle stubs the original's).</summary>
public sealed class OracleScreens(List<string> log) : IGameScreens
{
    public void Barter(int shop, string name, long categories) => log.Add($"screen barter {shop} {categories:X} {name}");

    public void Camp(int mode) => log.Add($"screen camp {mode}");

    public void Alchemy(int member, int place) => log.Add($"screen alchemy {member} {place}");

    public void ItemStore() => log.Add("screen itemstore");

    public void CharacterSheet(int member) => log.Add($"screen sheet {member}");

    public void PartyInfo() => log.Add("screen partyinfo");

    public void Loot(IList<ItemStack> items, Core.Economy.Money money) =>
        log.Add($"screen loot {string.Join(",", items.Select(i => $"{i.Code}x{i.Count}"))} {money}");

    public int Battle(BattleRequest request)
    {
        log.Add($"screen battle {request.Battlefield} {request.Seed}");
        return 0;
    }
}
