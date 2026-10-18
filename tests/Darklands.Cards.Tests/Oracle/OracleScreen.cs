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
public sealed class OracleScreen(int option, bool probe) : ICardScreen
{
    private static readonly object MessagesLock = new();
    private int _menus;
    private string _card = "";

    public CardContext? Context { get; set; }

    public List<string> Log { get; } = [];

    /// <summary>"show" when the texts are another card's than the ones shown, else "picture" (as the oracle words card_load and card_load_near).</summary>
    public void Show(string cardName, string picture)
    {
        Log.Add(cardName == _card ? $"picture {picture.ToLowerInvariant()}" : $"show {cardName} {picture.ToLowerInvariant()}");
        // card_load takes pic_buf, the picture the rules redraw after a battle or a prayer ("redraw" in the original's
        // trace); the comparison reads this marker and drops it
        Log.Add($"main {picture.ToLowerInvariant()}");
        _card = cardName;
    }

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
        var enabled = Enumerable.Range(0, states.Count).Where(i => states[i] == OptionState.Enabled).ToList();
        if (enabled.Count == 0)
        {
            throw new InvalidOperationException($"Menu {block} offers no option.");
        }
        var first = option >= 0 && enabled.Contains(option) ? enabled.IndexOf(option) : 0;
        var choice = enabled[(first + _menus - 1) % enabled.Count];
        if (probe && hint is not null)
        {
            for (var i = 0; i < states.Count; i++)
            {
                if (states[i] != OptionState.Hidden)
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
        if (Context is { } c)
        {
            c.MenuPopup = false;
            if (probe && PopupKind(block, choice) is { } saints && PopupChoice(c, saints) is var (member, slot))
            {
                c.MenuPopup = true;
                c.MenuMember = member;
                c.MenuSlot = slot;
            }
        }
        Log.Add($"choose {choice}");
        return choice;
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

    private static string Say(OptionHint h) => $"{h.Chance} '{(h.Chance == -1 ? h.Message : "")}' {h.Member}";

    public void Tell(string message, int member = OptionHint.WholeParty) => Log.Add($"tell {member} {message}");

    public void PassTime(int hours) => Log.Add($"time {hours}");
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
