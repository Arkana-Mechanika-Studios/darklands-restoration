using Darklands.Core.Items;

namespace Darklands.Persistence;

/// <summary>
/// CACHE.TMP (the end of a saved game) ↔ <see cref="ItemStores"/>, which keeps the file as its bytes:
/// the loader copies a saved game's tail into the file, the writer copies the file back.
/// </summary>
// orig: the cache.tmp copy of the loader R037 (1462:1C7D) and of the writer R037+0x2ACE (1462:2ACE); the file's layout and its reads and
// writes are ItemStores' (stash-load R035+0x1350, stash-store R035+0x1468); tests: OriginalSaveTests
public static class CacheFile
{
    /// <summary>The item stores held by a CACHE.TMP image.</summary>
    public static ItemStores Read(ReadOnlySpan<byte> file)
    {
        var stores = new ItemStores();
        stores.Load(file);
        return stores;
    }

    /// <summary>The stores' file image, as <see cref="ItemStores.File"/> holds it.</summary>
    public static byte[] Write(ItemStores stores) => stores.File;
}
