using Darklands.Core.Battles.Tactics;
using System.Buffers.Binary;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Battles;

/// <summary>
/// An image bank of row-run sprites: a data size word, a table of paragraph offsets, then the
/// data (BATTLEGR.IMG: 201 battle images; COMMONSP.IMG: 14 panel and pointer images).
/// </summary>
// orig: R002+0x08FE (0E76:08FE) draft (BATTLEGR.IMG); R000+0x1010 (0E76:1010) draft (COMMONSP.IMG)
public sealed class ImageBank
{
    private ImageBank(RowRunSprite[] images) => Images = images;

    public IReadOnlyList<RowRunSprite> Images { get; }

    public RowRunSprite this[int index] => Images[index];

    /// <summary>Decodes the bank; with <paramref name="assetName"/> (the file, "BATTLEGR.IMG") image i is named "BATTLEGR.IMG#i".</summary>
    public static ImageBank Decode(byte[] file, int count, string? assetName = null)
    {
        var size = BinaryPrimitives.ReadUInt16LittleEndian(file);
        var data = file.AsSpan(2 + 2 * count, Math.Min(size, file.Length - 2 - 2 * count));
        var images = new RowRunSprite[count];
        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2 + 2 * i)) * 16;
            images[i] = offset < data.Length ? RowRunSprite.Decode(data, offset, assetName is null ? null : $"{assetName}#{i}") : RowRunSprite.Empty;
        }
        return new ImageBank(images);
    }
}

/// <summary>The indexes of the battle images (BATTLEGR.IMG) the battle screen uses.</summary>
public static class BattleImages
{
    public const int Count = 201;

    /// <summary>Damage numbers "−n" for n = 1..42: party colour at <see cref="PartyDamage"/> + n, enemy colour at <see cref="EnemyDamage"/> + n.</summary>
    public const int PartyDamage = 0;

    public const int EnemyDamage = 43;

    /// <summary>Scroll arrows, 8 directions from up, clockwise.</summary>
    public const int ScrollArrows = 86;

    public const int MemberCursor = 94;
    public const int TargetCursor = 95;
    public const int GroundBlocked = 96;
    public const int GroundFree = 97;
    public const int TargetBlocked = 98;
    public const int GroupFree = 99;
    public const int GroupBlocked = 100;
    public const int SingleFileFree = 101;
    public const int SingleFileBlocked = 102;
    public const int NormalPointer = 113;
    public const int OrderPointer = 114;

    /// <summary>Missiles: 115 + 6 × direction + group (0 arrow, 1 stuck arrow, 2 energy bolt, 3 axe, 4 dagger, 5 javelin).</summary>
    public const int Missiles = 115;

    public const int BigProjectile = 163;
    public const int BigProjectile2 = 164;
    public const int Bullet = 165;
    public const int Sparkles = 193;
    public const int LootPile = 198;
}

/// <summary>
/// HITSEQ.DTA: per weapon animation code (56 codes, listed in the program at DS:1836) the attack
/// sequence, the aim/parry sequence, three frames for the miss sequences, and their lengths.
/// </summary>
// orig: R002+0x1750 (0E76:1750) draft loads it; sprite_file_load R014+0x3D2C selects a row (spec E §1.6)
public sealed class HitSequences
{
    private readonly Dictionary<string, HitRow> _rows;

    private HitSequences(Dictionary<string, HitRow> rows) => _rows = rows;

    public HitRow? For(string code) => _rows.TryGetValue(code.ToUpperInvariant(), out var row) ? row : null;

    public static HitSequences Load(GameFiles files, DarklandsExecutable exe)
    {
        var data = files.Read("HITSEQ.DTA");
        var codes = exe.DataSegment(0x1836, 112).ToArray();
        var rows = new Dictionary<string, HitRow>();
        for (var j = 0; j < 56 && 15 * j + 15 <= data.Length; j++)
        {
            var code = System.Text.Encoding.ASCII.GetString(codes, 2 * j, 2);
            var r = data.AsSpan(15 * j, 15);
            rows.TryAdd(code, new HitRow(r[..10].ToArray(), r[10], r[11], r[12], r[13], r[14]));
        }
        return new HitSequences(rows);
    }
}

/// <summary>ENEMYPAL.DAT: 71 enemy colour schemes, each 16 colours for one of the ranges 32, 48 or 64, and an optional colour cycle.</summary>
// orig: enemies_place R025+0x0000 (1EF8:0000) draft applies them (spec E §1.8)
public sealed class EnemyPalettes
{
    private readonly byte[] _data;

    private EnemyPalettes(byte[] data) => _data = data;

    public static EnemyPalettes Load(GameFiles files) => new(files.Read("ENEMYPAL.DAT"));

    public int Count => _data.Length / 53;

    /// <summary>The first colour index the scheme replaces (byte offset / 3).</summary>
    public int FirstColor(int scheme) => _data[53 * scheme] / 3;

    /// <summary>The 16 colours as 6-bit RGB triples.</summary>
    public ReadOnlySpan<byte> Colors(int scheme) => _data.AsSpan(53 * scheme + 1, 48);
}
