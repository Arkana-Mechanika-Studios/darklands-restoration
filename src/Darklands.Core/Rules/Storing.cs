using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.World;

namespace Darklands.Core.Rules;

/// <summary>
/// Putting items into a place's store and taking them out (the item store screen's rules),
/// one item at a time.
/// </summary>
// orig: item cache overlay R035 = R035 (linked paragraph 1462, payload = offset) matched (11/11 file match); tests: TradingAndLootTests
public static class Storing
{
    /// <summary>
    /// Adds one item to a store: onto the entry of the same code and quality, else as a new
    /// entry. Faithful quirk: the count is a byte, so the 256th item makes it 0.
    /// </summary>
    // orig: stash-put R035+0x10D2 (1462:10D2) matched; tests: TradingAndLootTests
    public static void AddOne(StoreTable store, int code, int quality)
    {
        // C: the entry's qual is a char compared with the int argument: for qualities above 127 the signed char never equals it and a new entry is made each time (qualities are 1..99 in play; not reproduced).
        var at = store.FindIndex(e => e.Code == code && e.Quality == (byte)quality);
        if (at >= 0)
        {
            // C: cnt++ on a char: 255 + 1 wraps to 0.
            // BUG: the count has no limit: the 256th item of one kind makes it 0, and the stored items are lost.
            store[at] = store[at] with { Count = (byte)(store[at].Count + 1) };
            return;
        }
        store.Add(new StoredItem(code, (byte)quality, 1));
    }

    /// <summary>Takes one item off entry <paramref name="index"/> (in use or not); an entry reaching 0 is removed (one at 0 wraps to 255).</summary>
    // orig: stash-take R035+0x114A (1462:114A) matched; tests: TradingAndLootTests
    // C: --cnt == 0 on a char; the shifting loop copies the entry after the last one down (StoreTable.RemoveAt), even for an index past the count.
    public static void TakeOne(StoreTable store, int index)
    {
        var count = (byte)(store[index].Count - 1);
        store[index] = store[index] with { Count = count };
        if (count == 0)
        {
            store.RemoveAt(index);
        }
    }

    /// <summary>
    /// Puts one item of the member's pack entry <paramref name="index"/> into the store; returns
    /// the member's stack count. As in the original the entry may be past the last stack (a lit
    /// row past the end): its stale code and quality go into the store and it loses an item.
    /// </summary>
    // orig: stash-screen-keys R035+0x0A46 (1462:0A46) matched, case P (0x1950/0x1970): stash-put then item-take-single-overlay-entry; tests: TradingAndLootTests
    // C: the screen checks only that party[who].nitems is not 0 (a count of -1 passes); the entry is read whatever the count (Pack).
    /// <summary>The message of a refused Put (a place cannot hold more stores or entries).</summary>
    // DIVERGENCE: own text (the original has none, its Put never refuses): shown by the item store screen when OriginalBehaviour.FixCacheStoreLimit (data safety) refuses a Put; the item stays in the pack.
    public const string NoRoomMessage = "The innkeeper has no room for more.";

    /// <summary>Whether Put may go on (always, unless the compact CACHE.TMP layout is in use and the store or the file is full).</summary>
    public static bool CanPut(GameState s, Location place, StoreTable store, int member, int index)
    {
        var item = s.Party.Slot(member).Pack[index];
        return s.ItemStores.CanPut(place, s.Locations, store, item.Code & 0xFFF, item.Quality, s.Behaviour);
    }

    public static int Put(GameState s, StoreTable store, int member, int index)
    {
        var pack = s.Party.Slot(member).Pack;
        // C: stash-put(id & 0xfff, who, qual) evaluates its arguments right to left (no side effects), then item-take-single.
        AddOne(store, pack[index].Code & 0xFFF, pack[index].Quality);
        return Inventory.TakeAt(s, member, index);
    }

    /// <summary>
    /// Gives the member one item of store entry <paramref name="index"/> (type and weight from
    /// the item list); returns the member's stack count. The item leaves the store even when the
    /// member's pack is full. As in the original the entry may be past the last one (a lit row
    /// past the end): the member gets the stale entry's item, which loses one (0 wraps to 255).
    /// </summary>
    // orig: stash-screen-keys R035+0x0A46 (1462:0A46) matched, case G (0x2247/0x2267): give-item-record-form {id, type byte +0x1E, qual, 1, weight +0x25} then stash-take; tests: TradingAndLootTests
    // C: the screen checks only that the store is not empty (DS:89F0); entries past the count are what earlier stores left (StoreTable).
    public static int Get(GameState s, StoreTable store, int member, int index)
    {
        var entry = store[index];
        var d = s.Item(entry.Code);
        // BUG: taking from an inn store into a full pack loses the item: the store entry gives it up whatever AddStack
        // did, and with 64 stacks and none that the item joins, AddStack adds nothing.
        var count = Inventory.AddStack(s, member, new ItemStack(entry.Code, (byte)d.Type, entry.Quality, 1, (byte)d.Weight));
        TakeOne(store, index);
        return count;
    }
}
