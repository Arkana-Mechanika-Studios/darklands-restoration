using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Core.Tests;

/// <summary>
/// The compact CACHE.TMP layout of OriginalBehaviour.FixCacheStoreLimit / FixInnCacheRewrite: a 126-byte table, at most 62 live
/// stores, at most 127 entries a store, unreferenced stores dropped and the places' handles renumbered, Put refused beyond the limits.
/// </summary>
public sealed class CacheStoreLimitTests
{
    private static readonly OriginalBehaviour Fix = OriginalBehaviour.DataSafe;

    private static StoredItem Item(int n) => new(1 + n % 30, 1 + n % 99, 1 + n % 200);

    private static List<Location> Places(int n) => [.. Enumerable.Range(0, n).Select(i => new Location { Index = i })];

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ACompactFileMatchesWhatTheOriginalWroteFor60StoresAndAnAppend()
    {
        // Fixtures: the original DARKLAND.EXE (emulated) given a compact 60-store CACHE.TMP (compact60-input), Ulm without a store: it appended store 61
        // when the screen was left (compact60-left) and rewrote it after a second put (compact60-left2). Recorded by the datasafe/cache-limit run compact60-new.
        var stores = new ItemStores();
        stores.Load(Fixture("compact60-input.cache"));
        var places = Places(61);
        for (var i = 0; i < 60; i++)
        {
            places[i].CacheHandle = i + 1;
        }
        var ulm = places[60];
        stores.Close(ulm, [new StoredItem(0x18, 0x19, 1)], Fix, places);
        Assert.Equal(61, ulm.CacheHandle);
        Assert.Equal(Fixture("compact60-left.cache"), stores.File);
        stores.Close(ulm, [new StoredItem(0x18, 0x19, 2)], Fix, places);
        Assert.Equal(Fixture("compact60-left2.cache"), stores.File);
        Assert.Equal(Enumerable.Range(1, 60), places.Take(60).Select(p => p.CacheHandle));
    }

    [Fact]
    public void ThePlayersGameHoldsUpTo62StoresAndRefusesThe63rdPutLeavingEverythingAsItWas()
    {
        var s = TestGame.Create(1, 5);
        s.Behaviour = Fix;
        var places = s.Locations;
        for (var i = 0; i < 62; i++)
        {
            s.ItemStores.Close(places[i], [Item(i)], Fix, places);
        }
        var before = s.ItemStores.File;
        Assert.Equal(126 + 62 * 5, before.Length);
        Assert.Equal(62, before[1]);

        var table = new StoreTable();
        var pack = s.Party.Members[0].Pack;
        var stacks = pack.Stacks;
        Assert.False(Storing.CanPut(s, places[62], table, 0, 0));            // a 63rd store
        Assert.Equal(stacks, pack.Stacks);                                   // the item stays in the pack
        Assert.Equal(before, s.ItemStores.File);
        Assert.True(Storing.CanPut(s, places[3], s.ItemStores.Open(places[3].CacheHandle, new StoreTable()), 0, 0));   // a place with a store always may
        Assert.True(s.ItemStores.CanPut(places[62], places, table, 1, 1, OriginalBehaviour.Faithful));                // the faithful game never refuses

        // a place gives its store up (emptied): the 63rd then fits, the stores are renumbered and the file loses the dropped one
        s.ItemStores.Close(places[3], [], Fix, places);
        Assert.Equal(-1, places[3].CacheHandle);
        Assert.True(Storing.CanPut(s, places[62], table, 0, 0));
        s.ItemStores.Close(places[62], [Item(62)], Fix, places);
        Assert.Equal(62, s.ItemStores.File[1]);
        var handles = places.Where(p => p.CacheHandle != -1).Select(p => p.CacheHandle).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 62), handles);
        for (var i = 0; i <= 62; i++)
        {
            if (i != 3)
            {
                Assert.Equal([Item(i)], s.ItemStores.Open(places[i].CacheHandle, new StoreTable()));
            }
        }
    }

    [Fact]
    public void AStoreHoldsUpTo127EntriesAndTheNextNewOneIsRefused()
    {
        var s = TestGame.Create(1, 5);
        s.Behaviour = Fix;
        var place = s.Locations[0];
        var items = Enumerable.Range(0, 127).Select(i => new StoredItem(i + 1, 10, 1)).ToList();
        s.ItemStores.Close(place, items, Fix, s.Locations);
        var table = s.ItemStores.Open(place.CacheHandle, new StoreTable());
        Assert.Equal(127, table.Count);
        Assert.False(s.ItemStores.CanPut(place, s.Locations, table, 500, 10, Fix));     // a 128th entry
        Assert.True(s.ItemStores.CanPut(place, s.Locations, table, 5, 10, Fix));        // an item that stacks onto an entry
        // the original's own rewrite of the file works on it
        var faithful = new ItemStores();
        faithful.Load(s.ItemStores.File);
        faithful.Close(place, [.. items.Take(126)]);
        Assert.Equal(126, faithful.Open(place.CacheHandle, new StoreTable()).Count);
    }

    [Fact]
    public void AnOriginalFileWith70LiveStoresIsClosedWithoutLoss()
    {
        var stores = new ItemStores();
        var places = Places(71);
        for (var i = 0; i < 70; i++)
        {
            places[i].CacheHandle = stores.Add([Item(i), Item(i + 50)]);
        }
        foreach (var get in new[] { false, true })
        {
            var table = stores.Open(places[5].CacheHandle, new StoreTable());
            var items = get ? [.. table.Skip(1)] : table.ToList();
            stores.Close(places[5], items, Fix, places);
            for (var i = 0; i < 70; i++)
            {
                var expected = i == 5 && get ? [Item(55)] : new List<StoredItem> { Item(i), Item(i + 50) };
                Assert.Equal(expected, stores.Open(places[i].CacheHandle, new StoreTable()));
            }
        }
        var empty = new StoreTable();
        Assert.False(stores.CanPut(places[70], places, empty, 1, 1, Fix));        // a new store is refused
        Assert.True(stores.CanPut(places[5], places, empty, 1, 1, Fix));
    }

    [Fact]
    public void AnOriginalFileWithA200EntryStoreIsClosedWithoutLoss()
    {
        var stores = new ItemStores();
        var places = Places(3);
        var big = Enumerable.Range(0, 200).Select(i => new StoredItem(i + 1, 10, 1)).ToList();
        places[0].CacheHandle = stores.Add(big);
        places[1].CacheHandle = stores.Add([Item(1)]);
        foreach (var get in new[] { false, true })
        {
            var table = stores.Open(places[0].CacheHandle, new StoreTable());
            Assert.Equal(big.Count, table.Count);
            stores.Close(places[0], get ? [.. big.Skip(1)] : big, Fix, places);
            Assert.Equal(get ? 199 : 200, stores.Open(places[0].CacheHandle, new StoreTable()).Count);
            Assert.Equal([Item(1)], stores.Open(places[1].CacheHandle, new StoreTable()));
            big = [.. stores.Open(places[0].CacheHandle, new StoreTable())];
        }
        var table2 = stores.Open(places[0].CacheHandle, new StoreTable());
        Assert.False(stores.CanPut(places[0], places, table2, 900, 10, Fix));      // no new entry past 127
        Assert.True(stores.CanPut(places[0], places, table2, 5, 10, Fix));
    }

    [Fact]
    public void UnreferencedStoresAreDroppedAndThePlacesRenumbered()
    {
        var stores = new ItemStores();
        var places = Places(5);
        for (var i = 0; i < 4; i++)
        {
            stores.Close(places[i], [Item(i)], Fix, places);
        }
        Assert.Equal([1, 2, 3, 4], places.Take(4).Select(p => p.CacheHandle));
        stores.Close(places[1], [], Fix, places);                              // forgets its store
        places[3].CacheHandle = 9;                                             // a handle past the file's stores: no store
        stores.Close(places[4], [Item(4)], Fix, places);
        Assert.Equal([1, -1, 2, -1, 3], places.Select(p => p.CacheHandle));
        Assert.Equal(3, stores.File[1]);
        Assert.Equal([Item(0)], stores.Open(1, new StoreTable()));
        Assert.Equal([Item(2)], stores.Open(2, new StoreTable()));
        Assert.Equal([Item(4)], stores.Open(3, new StoreTable()));
        Assert.Equal(126 + 3 * 5, stores.File.Length);
    }

    [Fact]
    public void ThePlacesSharingAStoreKeepSharingItAfterTheRenumbering()
    {
        var stores = new ItemStores();
        var places = Places(4);
        stores.Close(places[0], [Item(0)], Fix, places);
        stores.Close(places[1], [Item(1)], Fix, places);
        places[2].CacheHandle = 2;
        places[0].CacheHandle = -1;
        stores.Close(places[3], [Item(3)], Fix, places);
        Assert.Equal([-1, 1, 1, 2], places.Select(p => p.CacheHandle));
        Assert.Equal([Item(1)], stores.Open(1, new StoreTable()));
    }

    [Fact]
    public void TheFaithfulGameStillOverwritesTheFirstStoreWithThe99thOffset()
    {
        var stores = new ItemStores();
        for (var i = 0; i < 98; i++)
        {
            stores.Add([new StoredItem(7, 30, 1)]);
        }
        var place = new Location();
        stores.Close(place, [new StoredItem(9, 9, 9)], OriginalBehaviour.Faithful, [place]);
        Assert.Equal(99, place.CacheHandle);
        var at = 198 + 98 * 5;
        Assert.Equal(new byte[] { (byte)at, (byte)(at >> 8) }, stores.File[198..200]);
        Assert.NotEqual([new StoredItem(7, 30, 1)], stores.Open(1, new StoreTable()));
    }

    [Fact]
    public void AFixedFileLoadedFromAFaithfulOneKeepsTheReferencedStores()
    {
        // a game saved by the faithful build (198-byte table, 3 stores) is laid out compactly on the first fixed close
        var faithful = new ItemStores();
        var places = Places(4);
        for (var i = 0; i < 3; i++)
        {
            places[i].CacheHandle = faithful.Add([Item(i), Item(i + 10)]);
        }
        var stores = new ItemStores();
        stores.Load(faithful.File);
        stores.Close(places[3], [Item(3)], Fix, places);
        Assert.Equal(new byte[] { 124, 4 }, stores.File[..2]);
        Assert.Equal(126, BitConverter.ToUInt16(stores.File, 2));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(i + 1, places[i].CacheHandle);
        }
        Assert.Equal([Item(1), Item(11)], stores.Open(2, new StoreTable()));
    }

    [Fact]
    public void TheOriginalsRewriteOfAFixedFileLosesNothing()
    {
        // the original's cache_save reads the compact table whole (README of the compact60 runs); the model of it agrees
        var stores = new ItemStores();
        var places = Places(3);
        for (var i = 0; i < 3; i++)
        {
            stores.Close(places[i], [Item(i), Item(i + 20)], Fix, places);
        }
        stores.Close(places[1], [Item(7)]);                                     // faithful rewrite of store 2
        Assert.Equal([Item(0), Item(20)], stores.Open(1, new StoreTable()));
        Assert.Equal([Item(7)], stores.Open(2, new StoreTable()));
        Assert.Equal([Item(2), Item(22)], stores.Open(3, new StoreTable()));
    }
}
