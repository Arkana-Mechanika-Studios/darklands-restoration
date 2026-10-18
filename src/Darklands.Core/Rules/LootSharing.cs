using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Rules;

/// <summary>
/// Sharing out what the party found (the loot screen's rules): the found items' qualities,
/// the cash, and moving items one at a time between the pile and a member's pack.
/// </summary>
// orig: loot overlay R040 = R040 (linked paragraph 1462, payload = offset) draft (the byte match 9/10: all but 0c22 byte-match; 0c22 differs in cross-jumped tails, checked against asm); tests: TradingAndLootTests
public static class LootSharing
{
    /// <summary>The pile holds at most 45 entries.</summary>
    public const int MaxPileEntries = 45;

    /// <summary>
    /// Before the screen opens: a dead or missing leader is replaced by the first member who
    /// is not dead. Faithful quirk: the test asks whether the <em>leader's</em> slot is empty
    /// instead of the candidate's, so an empty slot can become the leader, and a leader whose
    /// slot is empty is never replaced.
    /// </summary>
    // orig: loot_distribute R040+0x0000 (1462:0000) draft (byte-matched function), the leader block; tests: TradingAndLootTests
    // DORMANT: loot_distribute's "Could not open list file" exit (DARKLAND.LST missing); the item list is loaded with the game here.
    public static void ReplaceFallenLeader(GameState s)
    {
        var party = s.Party;
        var status = party.Slot(party.LeaderIndex).Status;
        if (status is not (CharacterStatus.Dead or CharacterStatus.Empty))
        {
            return;
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            // BUG: the second test reads party[leader] instead of party[i] (as in the source; loot_distribute is byte-matched): reproduced, no fix flag.
            if (party.Slot(i).Status != CharacterStatus.Dead && party.Slot(party.LeaderIndex).Status != CharacterStatus.Empty)
            {
                party.LeaderIndex = i;
                return;
            }
        }
    }

    /// <summary>
    /// The found items as the pile shows them: a count of 0 becomes 1; of the item types up to
    /// 0x61, types 0x17, 0x18 and 0x40..0x42 get quality 25, types 0x19 and 0x1A keep theirs
    /// (1..99), and all others lose 10 quality points (1..99).
    /// </summary>
    // orig: loot_distribute R040+0x0000 (1462:0000) draft (byte-matched function), the quality loop; tests: TradingAndLootTests
    public static void PrepareFoundItems(IList<ItemStack> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Count == 0)
            {
                item = item with { Count = 1 };
            }
            // C: kind is an unsigned char (the stack's type byte); quality is an unsigned char, so quality − 10 is computed as int before the clamp.
            if ((byte)item.Type <= 0x61)
            {
                var quality = (byte)item.Type switch
                {
                    0x40 or 0x41 or 0x42 or 0x17 or 0x18 => 25,
                    // DORMANT: the second arm also lists 0x17 and 0x18, which the first arm already took.
                    0x19 or 0x1A => Math.Clamp(item.Quality, 1, 99),
                    _ => Math.Clamp(item.Quality - 10, 1, 99),
                };
                item = item with { Quality = (byte)quality };
            }
            items[i] = item;
        }
    }

    /// <summary>
    /// The found cash goes into the purse: florins are dropped and groschen and pfennigs are
    /// capped at 10 each, and the caller's amount becomes that (the original writes it back
    /// through the caller's pointer). Returns the amount the screen announces (what was added).
    /// </summary>
    // orig: R040+0x01C8 (1462:01C8) draft (byte-matched function), the cash lines; tests: TradingAndLootTests
    public static Money TakeCash(GameState s, ref Money found)
    {
        // C: cash[0] = 0; cash[1] = clamp_range(0, 10, cash[1]); cash[2] = clamp_range(0, 10, cash[2]) (right-to-left arguments, no draws), then money_add_fgp(party_money, cash[0] re-read = 0, cash[1], cash[2]).
        // C: the three words are written back through the caller's far pointer (the battle's R136+0xE4E0 or a floor tile's cash).
        var cash = new Money(0, (short)Math.Clamp((int)found.Groschen, 0, 10), (short)Math.Clamp((int)found.Pfennigs, 0, 10));
        found = cash;
        s.Party.Purse = s.Party.Purse.Add(cash.Florins, cash.Groschen, cash.Pfennigs);
        return cash;
    }

    /// <summary>
    /// Puts one item of the member's pack entry <paramref name="index"/> on the pile: onto an equal
    /// entry (code and quality) with fewer than 255, else as a new entry while the pile has
    /// fewer than 45. The item leaves the pack even when the pile is full. Returns the
    /// member's stack count. As in the original the entry may be past the last stack (a lit
    /// row past the end): its stale code and quality go on the pile (item 0, quality 0 for an
    /// entry never written) and it loses an item (0 wraps to 255, a 1 deletes it).
    /// </summary>
    // orig: R040+0x0C22 (1462:0C22) draft (unmatched; checked against asm 0C96..0D87), case P: R040+0x1242 then item_take_one_vec; tests: TradingAndLootTests
    // C: the screen checks only n_items[who] != 0; inventories[who][top + sel] is read whatever the count (Pack).
    public static int Put(GameState s, IList<ItemStack> pile, int member, int index)
    {
        var pack = s.Party.Slot(member).Pack;
        AddToPile(s, pile, pack[index].Code & 0xFFF, pack[index].Quality);
        return Inventory.TakeAt(s, member, index);
    }

    // orig: R040+0x1242 (1462:1242) draft (byte-matched function); tests: TradingAndLootTests
    // C: an equal entry needs count < 255; a new one needs fewer than 45 entries, else the item is lost (it has already left the pack).
    private static void AddToPile(GameState s, IList<ItemStack> pile, int code, int quality)
    {
        for (var i = 0; i < pile.Count; i++)
        {
            if (pile[i].Code == code && pile[i].Quality == quality && pile[i].Count < Inventory.MaxCount)
            {
                pile[i] = pile[i] with { Count = pile[i].Count + 1 };
                return;
            }
        }
        if (pile.Count < MaxPileEntries)
        {
            // UNCERTAIN: the original sets only type (code), quality and count of the new entry and leaves its kind and weight bytes stale; nothing reads them afterwards (Get takes both from the item list), so the item list's values are stored.
            var d = s.Item(code);
            pile.Add(new ItemStack(code, (byte)d.Type, quality, 1, (byte)d.Weight));
        }
    }

    /// <summary>
    /// Gives the member one item of pile entry <paramref name="index"/> (type and weight from
    /// the item list); an entry reaching 0 leaves the pile. The item leaves the pile even when
    /// the member's pack is full. Returns the member's stack count, or −1 when there is no such entry.
    /// </summary>
    // orig: R040+0x0C22 draft (unmatched; checked against asm 0DF6..0F07), case G: add_item_rec {type, kind +0x1E, quality, 1, weight +0x25} then R040+0x12EC (byte-matched); tests: TradingAndLootTests
    public static int Get(GameState s, IList<ItemStack> pile, int member, int index)
    {
        // DIVERGENCE: a row past the pile's end hands out a stale entry in the original (it only checks the pile is not empty); nothing happens here.
        if (index < 0 || index >= pile.Count)
        {
            return -1;
        }
        var entry = pile[index];
        var d = s.Item(entry.Code);
        var count = Inventory.AddStack(s, member, new ItemStack(entry.Code, (byte)d.Type, entry.Quality, 1, (byte)d.Weight));
        // C: --count == 0 on an unsigned char; the gap is closed by copying one entry past the end (harmless).
        if ((byte)(entry.Count - 1) == 0)
        {
            pile.RemoveAt(index);
        }
        else
        {
            pile[index] = entry with { Count = (byte)(entry.Count - 1) };
        }
        return count;
    }
}
