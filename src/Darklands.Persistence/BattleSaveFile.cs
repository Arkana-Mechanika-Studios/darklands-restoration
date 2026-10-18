using Darklands.Core.Battles;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>
/// The battle file beside a saved game (SAVES\DKSAVEn.BSV): a 0x2A-byte header of block sizes,
/// then imploded blocks: the battle's variables (0x1978 bytes), for each level of the last
/// battlefield its floor image, activations and enemies, and two optional work files.
/// </summary>
// orig: the battle-state save R003+0x00DC (0E76:00DC) and the battle-state restore R003+0x0CE8 (0E76:0CE8) draft (unmatched: register
// and slot choices; the serialisation order matches the bytes); the block reader R003+0x0000 (0E76:0000) and the file writer R003+0x00A0
// (0E76:00A0) draft; tests: OriginalSaveTests, BattleSaveTests
// C: the header is DS:D91B (packed size of the variables), DS:D91D[16] (packed size of each level), then (raw, packed) sizes of the
// files DS:1162 (the loot file) and DS:1164 (terrain.fil); raw size -1 or 0 means no file. The files follow the levels in the order DS:1164, DS:1162.
// C: the level count is the variables' byte DS:D661 (+0x16E4). A level is the floor image (0x33FC bytes, its function pointers
// replaced by the numbers 0..8), the .atv data (count byte + 30 bytes each) and the .enm data (count byte + 0x122 bytes each).
// C: the save writes the word DS:D6CE (the battle seed) at +0x16F1; the restore reads that word into DS:DAD7 (an asymmetry of the original).
public sealed class BattleSaveFile
{
    public const int HeaderSize = 0x2A;
    public const int VariablesSize = 0x1978;
    public const int MaxLevels = 16;

    /// <summary>DS:907A at +0: 1 when the game was saved in a battle.</summary>
    public const int InBattleAt = 0x0000;

    /// <summary>The marching order at +0x1573 (6 bytes).</summary>
    public const int MarchingOrderAt = 0x1573;

    /// <summary>DS:D661 at +0x16E4: the number of levels of the last battlefield.</summary>
    public const int LevelCountAt = 0x16E4;

    /// <summary>DS:DA34 at +0x16F3: the cleared levels (as the save header holds them).</summary>
    public const int ClearedLevelsAt = 0x16F3;

    /// <summary>DS:160C at +0x1974: the last castle's progress (GameState.LastCastleFlags).</summary>
    public const int LastCastleFlagsAt = 0x1974;

    // The header words of the two files.
    private const int LootSize = 17, LootPacked = 18, TerrainSize = 19, TerrainPacked = 20;

    /// <summary>The header words: [0] variables, [1..16] levels, [17..18] file DS:1162 (raw, packed), [19..20] file DS:1164.</summary>
    public int[] HeaderWords { get; } = new int[HeaderSize / 2];

    /// <summary>The battle variables (0x1978 bytes).</summary>
    public byte[] Variables { get; private set; } = [];

    /// <summary>The levels of the last battlefield.</summary>
    public List<SavedLevel> Levels { get; } = [];

    /// <summary>The work file DS:1164 (terrain.fil), if it was saved.</summary>
    public byte[]? File1164 { get; private set; }

    /// <summary>The work file DS:1162 (the loot file), if it was saved.</summary>
    public byte[]? File1162 { get; private set; }

    public bool SavedInBattle => Variables.Length > InBattleAt && Variables[InBattleAt] == 1;

    public int ClearedLevels => Word(Variables, ClearedLevelsAt);

    public int LastCastleFlags => Word(Variables, LastCastleFlagsAt);

    public ReadOnlySpan<byte> MarchingOrder => Variables.AsSpan(MarchingOrderAt, 6);

    /// <summary>Reads a battle file as the restore does: the header, the variables, DS:D661 levels, then the two files.</summary>
    // orig: the battle-state restore R003+0x0CE8 (0E76:0CE8) draft; the end-of-data exit R003+0x19CA (0E76:19CA) draft ("EOF-%s"); the explode
    // exit R003+0x19E8 (0E76:19E8) draft ("Corrupt file-%s %d")
    // DIVERGENCE: the original ends the program with those messages; here the read fails with them.
    public static BattleSaveFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("EOF-Vars");
        }
        var file = new BattleSaveFile();
        for (var i = 0; i < file.HeaderWords.Length; i++)
        {
            file.HeaderWords[i] = UWord(data, 2 * i);
        }
        var at = HeaderSize;
        file.Variables = Block(data, ref at, file.HeaderWords[0], "Vars", "Vars");
        if (file.Variables.Length != VariablesSize)
        {
            throw new InvalidDataException($"the battle variables are {file.Variables.Length} bytes, not 0x1978");
        }
        var levels = file.Variables[LevelCountAt];
        for (var i = 0; i < levels; i++)
        {
            // C: the level loop runs DS:D661 times reading DS:D91D[i]: more than 16 levels would read past the header (never written).
            var level = Block(data, ref at, i < MaxLevels ? file.HeaderWords[1 + i] : 0, "Lv", $"Lv{i}");
            file.Levels.Add(SavedLevel.Split(level));
        }
        // C: a raw size of -1 or 0 removes the file; otherwise it is written back with the raw size the header gives.
        if (file.HeaderWords[TerrainSize] is not (0 or 0xFFFF))
        {
            file.File1164 = Sized(Block(data, ref at, file.HeaderWords[TerrainPacked], "terrf", "terrnf"), file.HeaderWords[TerrainSize]);
        }
        if (file.HeaderWords[LootSize] is not (0 or 0xFFFF))
        {
            file.File1162 = Sized(Block(data, ref at, file.HeaderWords[LootPacked], "cf", "cache"), file.HeaderWords[LootSize]);
        }
        return file;
    }

    /// <summary>
    /// The battle file as the program keeps it after the restore (the header words become the program's DS:D91B): a work file
    /// saved empty is removed, as the restore removes it (raw size 0 or −1), so the next save marks it missing (−1).
    /// </summary>
    /// <param name="keepEmptyFiles">Keep a file saved empty as an empty file (to write the battle file back as it was read).</param>
    // orig: the battle-state restore R003+0x0CE8 (0E76:0CE8) draft: terrain.fil is removed when its raw size word is -1 or 0, the same for the loot file
    public BattleFile ToBattleFile(bool keepEmptyFiles = false)
    {
        var file = new BattleFile
        {
            Variables = (byte[])Variables.Clone(),
            Terrain = File1164 ?? (keepEmptyFiles && HeaderWords[TerrainSize] == 0 ? [] : null),
            Loot = File1162 ?? (keepEmptyFiles && HeaderWords[LootSize] == 0 ? [] : null),
        };
        for (var i = 0; i < HeaderWords.Length; i++)
        {
            file.Header[i] = (ushort)HeaderWords[i];
        }
        foreach (var l in Levels)
        {
            file.Levels.Add(new LevelFiles(l.Floor.Image, l.Activations, l.Enemies));
        }
        return file;
    }

    /// <summary>
    /// Writes the program's battle file as the save does: the variables, DS:D661 levels, terrain.fil and the loot file, each
    /// imploded (binary literals, a 0x800-byte dictionary for the variables and levels, 0x400 for the files) with the program's
    /// compressor (its work area carries over from save to save); the header words of blocks not written keep their values.
    /// </summary>
    // orig: the battle-state save R003+0x00DC (0E76:00DC) draft; the block reader R003+0x0000 (0E76:0000) and the file writer R003+0x00A0 (0E76:00A0) draft
    // C: the header is written first as it stood, the blocks after it, then the file is reopened (root 06A1:2C9E) and the new header
    // written over the old one.
    // C: a file that exists but is empty sets its raw size to 0 and leaves its packed size word as it was.
    public static byte[] Write(BattleFile file)
    {
        var compressor = file.Compressor as Implode ?? new Implode();
        file.Compressor = compressor;
        var output = new List<byte>(new byte[HeaderSize]);
        var h = file.Header;
        h[0] = Pack(compressor, file.Variables, 0x800, output);
        var count = file.Variables[LevelCountAt];
        for (var i = 0; i < count; i++)
        {
            var l = i < file.Levels.Count ? file.Levels[i] : throw new InvalidOperationException($"the battle file has {file.Levels.Count} levels, not {count}");
            var size = Pack(compressor, [.. l.Floor, .. l.Activations, .. l.Enemies], 0x800, output);
            // C: each level's packed size goes to the next header word: from the 17th level on the files' words would be written (never: 16 levels at most).
            if (1 + i < h.Length)
            {
                h[1 + i] = size;
            }
        }
        h[TerrainSize] = 0xFFFF;
        if (file.Terrain is { } terrain)
        {
            h[TerrainSize] = (ushort)terrain.Length;
            if (terrain.Length != 0)
            {
                h[TerrainPacked] = Pack(compressor, terrain, 0x400, output);
            }
        }
        h[LootSize] = 0xFFFF;
        if (file.Loot is { } loot)
        {
            h[LootSize] = (ushort)loot.Length;
            if (loot.Length != 0)
            {
                h[LootPacked] = Pack(compressor, loot, 0x400, output);
            }
        }
        var bytes = output.ToArray();
        for (var i = 0; i < h.Length; i++)
        {
            bytes[2 * i] = (byte)h[i];
            bytes[2 * i + 1] = (byte)(h[i] >> 8);
        }
        return bytes;
    }

    // C: DS:D90F / DS:D919 the block (the reader hands out at most what is left), DS:D917 counts what the writer took.
    private static ushort Pack(Implode compressor, byte[] data, int dictionary, List<byte> output)
    {
        var read = 0;
        var written = 0;
        compressor.Compress(
            (buffer, at, size) =>
            {
                // orig: R003+0x0000 (0E76:0000) draft
                var n = Math.Min(size, data.Length - read);
                Array.Copy(data, read, buffer, at, n);
                read += n;
                return n;
            },
            (buffer, at, size) =>
            {
                // orig: R003+0x00A0 (0E76:00A0) draft
                output.AddRange(buffer.AsSpan(at, size));
                written += size;
            },
            0, dictionary);
        return (ushort)written;
    }

    private static byte[] Block(ReadOnlySpan<byte> data, ref int at, int packed, string eofName, string what)
    {
        if (at + packed > data.Length)
        {
            // orig: R003+0x19CA (0E76:19CA) draft: a block read short ends the game with "EOF-" and the block's name
            throw new InvalidDataException($"EOF-{eofName}");
        }
        var bytes = Explode.Decompress(data.Slice(at, packed), what);
        at += packed;
        return bytes;
    }

    private static byte[] Sized(byte[] data, int size) => data.Length == size ? data : data.AsSpan(0, Math.Min(size, data.Length)).ToArray().Concat(new byte[Math.Max(0, size - data.Length)]).ToArray();
}

/// <summary>One level of the battlefield a battle file remembers (level%d.flr, .atv, .enm).</summary>
/// <param name="Floor">The floor image; its object functions are stored as the numbers 0..8.</param>
/// <param name="Activations">The .atv data: a count byte and 30 bytes per group.</param>
/// <param name="Enemies">The .enm data: a count byte and 0x122 bytes per enemy.</param>
public sealed record SavedLevel(BattlefieldLevel Floor, byte[] Activations, byte[] Enemies)
{
    // C: the level's three parts follow each other: 0x33FC bytes, then *p * 30 + 1, then *p * 0x122 + 1.
    public static SavedLevel Split(byte[] level)
    {
        const int floor = BattlefieldLevel.ImageSize;
        if (level.Length < floor + 2)
        {
            throw new InvalidDataException("a saved level is shorter than its floor image");
        }
        var activations = level[floor] * 30 + 1;
        var enemies = level.Length > floor + activations ? level[floor + activations] * 0x122 + 1 : 0;
        if (floor + activations + enemies > level.Length)
        {
            throw new InvalidDataException("a saved level is shorter than its parts");
        }
        return new SavedLevel(
            new BattlefieldLevel(level[..floor]),
            level[floor..(floor + activations)],
            level[(floor + activations)..(floor + activations + enemies)]);
    }
}
