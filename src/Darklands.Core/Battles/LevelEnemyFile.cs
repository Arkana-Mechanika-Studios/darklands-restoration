using System.Text;
using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>
/// A level's enemy kinds as the battle reads them (<c>level%d.enm</c>: a count byte, then 0x122 bytes per kind): the
/// level loader's conversion of the battlefield's kinds (level%d.trn) into a record that starts as a combatant record
/// (weapons, armour, attributes, skills at the combatant offsets) followed by the kind's identity, looks and spells.
/// </summary>
// orig: the level loader R020+0x0000 (1EF8:0000) draft (the conversion loop); tests: BattleSaveTests
// C: the loader writes only the fields below; the other bytes of each record are what its buffer (DS:A896) held before (the
// previous level's records, or an earlier battle's).
public static class LevelEnemyFile
{
    public const int RecordSize = 0x122;

    /// <summary>The .enm data of a level's kinds; the bytes the loader does not write come from <paramref name="before"/> (the buffer's last content), else 0.</summary>
    public static byte[] Write(IReadOnlyList<EnemyKind> kinds, ReadOnlySpan<byte> before = default)
    {
        var data = new byte[1 + kinds.Count * RecordSize];
        before[..Math.Min(before.Length, data.Length)].CopyTo(data);
        data[0] = (byte)kinds.Count;
        for (var i = 0; i < kinds.Count; i++)
        {
            Write(kinds[i], data.AsSpan(1 + i * RecordSize, RecordSize));
        }
        return data;
    }

    // C: the copy of each field, in the loader's order (destination offset ← source field of struct mon).
    private static void Write(EnemyKind k, Span<byte> d)
    {
        // C: _fstrcpy: the text and its zero; the bytes after it keep what the buffer held.
        Text(d, 0x80, 4, k.Image);
        Text(d, 0x84, 0x17, k.Name);
        for (var j = 0; j < EnemyKind.MaxItems; j++)
        {
            var at = 0xC2 + 6 * j;
            var s = j < k.Items.Count ? k.Items[j] : new ItemStack(-1, 0xFF, 25, 0, 0);
            Word(d, at, s.Code);
            d[at + 2] = (byte)s.Type;
            d[at + 3] = (byte)s.Quality;
            d[at + 4] = (byte)s.Count;
            d[at + 5] = (byte)s.Weight;
        }
        d[0x7E] = (byte)k.Items.Count;
        d[0x1E] = d[0x1F] = 8;
        d[0x22] = (byte)k.MissileType;
        d[0x5A] = (byte)k.MissileQuality;
        Word(d, 0x9E, k.MissileFlag);
        d[0x24] = (byte)k.PiercingResistance;
        d[0x48] = k.WeaponBonus;
        d[0x49] = k.Load;
        d[0x9C] = (byte)k.Byte26;
        Word(d, 0xAC, k.EffectImmunity);
        for (var j = 0; j < 10; j++)
        {
            d[0xAE + j] = (byte)k.Components[j];
            d[0xB8 + j] = (byte)k.ComponentQualities[j];
        }
        for (var j = 0; j < 2; j++)
        {
            d[0x4B + j] = (byte)k.ArmourTypes[j];
            d[0x4F + j] = (byte)k.ArmourQualities[j];
        }
        d[0x51] = (byte)k.WeaponType;
        d[0x58] = (byte)k.WeaponQuality;
        d[0x55] = k.Protection;
        d[0x5B] = (byte)k.ShieldQuality;
        d[0x5C] = (byte)k.ShieldType;
        d[0x9B] = (byte)k.Byte27;
        Word(d, 0xA4, k.LevelMask);
        d[0xA6] = (byte)k.BaseLoad;
        d[0xAA] = (byte)k.EndsBattleWhenKilled;
        d[0xAB] = k.Class;
        Word(d, 0xA2, k.Appearance);
        Word(d, 0xA0, k.Identity);
        for (var j = 0; j < 3; j++)
        {
            d[0xA7 + j] = (byte)k.Palettes[j];
        }
        for (var j = 0; j < Character.AttributeCount; j++)
        {
            // C: the current attributes go to both the current and the maximum values.
            d[0x5D + j] = d[0x64 + j] = (byte)k.Attributes[j];
        }
        for (var j = 0; j < Character.SkillCount; j++)
        {
            d[0x6B + j] = (byte)k.Skills[j];
        }
    }

    /// <summary>The kinds of a level's .enm data (as a battle placed from a battle file reads them).</summary>
    public static List<EnemyKind> Read(ReadOnlySpan<byte> data)
    {
        var kinds = new List<EnemyKind>();
        var n = data.IsEmpty ? 0 : data[0];
        for (var i = 0; i < n && 1 + (i + 1) * RecordSize <= data.Length; i++)
        {
            kinds.Add(ReadKind(data.Slice(1 + i * RecordSize, RecordSize)));
        }
        return kinds;
    }

    private static EnemyKind ReadKind(ReadOnlySpan<byte> d)
    {
        var k = new EnemyKind
        {
            Image = TextOf(d, 0x80, 4), Name = TextOf(d, 0x84, 0x17), MissileType = (sbyte)d[0x22], MissileQuality = (sbyte)d[0x5A],
            MissileFlag = (short)WordOf(d, 0x9E), PiercingResistance = (sbyte)d[0x24], WeaponBonus = d[0x48], Load = d[0x49],
            Byte26 = (sbyte)d[0x9C], EffectImmunity = (short)WordOf(d, 0xAC), WeaponType = (sbyte)d[0x51], WeaponQuality = (sbyte)d[0x58],
            Protection = d[0x55], ShieldQuality = (sbyte)d[0x5B], ShieldType = (sbyte)d[0x5C], Byte27 = (sbyte)d[0x9B],
            LevelMask = (ushort)WordOf(d, 0xA4), BaseLoad = (sbyte)d[0xA6], EndsBattleWhenKilled = (sbyte)d[0xAA], Class = d[0xAB],
            Appearance = (ushort)WordOf(d, 0xA2), Identity = (ushort)WordOf(d, 0xA0),
        };
        for (var j = 0; j < 10; j++)
        {
            k.Components[j] = (sbyte)d[0xAE + j];
            k.ComponentQualities[j] = (sbyte)d[0xB8 + j];
        }
        for (var j = 0; j < 2; j++)
        {
            k.ArmourTypes[j] = (sbyte)d[0x4B + j];
            k.ArmourQualities[j] = (sbyte)d[0x4F + j];
        }
        for (var j = 0; j < 3; j++)
        {
            k.Palettes[j] = (sbyte)d[0xA7 + j];
        }
        for (var j = 0; j < Character.AttributeCount; j++)
        {
            k.Attributes[j] = (sbyte)d[0x5D + j];
            // C: the .enm holds no maxima of the kind (the conversion wrote the attributes there).
            k.Maximums[j] = (sbyte)d[0x64 + j];
        }
        for (var j = 0; j < Character.SkillCount; j++)
        {
            k.Skills[j] = (sbyte)d[0x6B + j];
        }
        for (var j = 0; j < d[0x7E] && j < EnemyKind.MaxItems; j++)
        {
            var at = 0xC2 + 6 * j;
            k.Items.Add(new ItemStack((short)WordOf(d, at), d[at + 2], d[at + 3], d[at + 4], d[at + 5]));
        }
        return k;
    }

    private static int WordOf(ReadOnlySpan<byte> d, int at) => d[at] | d[at + 1] << 8;

    private static void Word(Span<byte> d, int at, int value)
    {
        d[at] = (byte)value;
        d[at + 1] = (byte)(value >> 8);
    }

    private static void Text(Span<byte> d, int at, int size, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        var n = Math.Min(bytes.Length, size - 1);
        bytes.AsSpan(0, n).CopyTo(d[at..]);
        d[at + n] = 0;
    }

    private static string TextOf(ReadOnlySpan<byte> d, int at, int size)
    {
        var field = d.Slice(at, size);
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }
}
