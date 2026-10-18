using System.Text;
using Darklands.Core.Battles;
using Darklands.Core.Characters;

namespace Darklands.Persistence;

/// <summary>
/// The battlefield builder's enemy records ↔ the domain: <see cref="EnemyKind"/> as the 0xD6-byte
/// <c>struct mon</c> (level%d.trn) and <see cref="EnemyGroup"/> as the 0x1E-byte <c>struct group</c>
/// (level%d.atv). Used by the oracle tests and battle files.
/// </summary>
// orig: struct mon / struct group (R004 enemy buffer bf_monsters +0x180, bf_groups +0xA200)
public static class EnemyRecords
{
    public const int KindSize = 0xD6;
    public const int GroupSize = 0x1E;

    public static byte[] Write(EnemyKind k)
    {
        var r = new byte[KindSize];
        PutWord(r, 0x00, k.Identity);
        PutWord(r, 0x02, k.Appearance);
        PutWord(r, 0x04, k.LevelMask);
        PutWord(r, 0x06, k.MissileFlag);
        r[0x08] = (byte)k.BaseLoad;
        for (var i = 0; i < 3; i++)
        {
            r[0x09 + i] = (byte)k.Palettes[i];
        }
        r[0x0C] = (byte)k.EndsBattleWhenKilled;
        r[0x0D] = k.Class;
        PutText(r, 0x0E, 4, k.Image);
        PutText(r, 0x12, 0x14, k.Name);
        r[0x26] = (byte)k.Byte26;
        r[0x27] = (byte)k.Byte27;
        r[0x28] = (byte)k.WeaponType;
        r[0x29] = (byte)k.WeaponQuality;
        r[0x2A] = (byte)k.MissileType;
        r[0x2B] = (byte)k.MissileQuality;
        r[0x2C] = (byte)k.PiercingResistance;
        r[0x2D] = k.WeaponBonus;
        r[0x2E] = k.Load;
        for (var i = 0; i < 2; i++)
        {
            r[0x2F + i] = (byte)k.ArmourTypes[i];
            r[0x31 + i] = (byte)k.ArmourQualities[i];
            r[0x58 + i] = (byte)k.StrengthBonusDice[i];
            r[0x5A + i] = (byte)k.EnduranceBonusDice[i];
            r[0x5C + i] = (byte)k.SkillBonusDice[i];
        }
        r[0x33] = k.Protection;
        r[0x34] = (byte)k.ShieldQuality;
        r[0x35] = (byte)k.ShieldType;
        for (var i = 0; i < 7; i++)
        {
            r[0x36 + i] = (byte)k.Attributes[i];
            r[0x3D + i] = (byte)k.Maximums[i];
        }
        for (var i = 0; i < 19; i++)
        {
            r[0x44 + i] = (byte)k.Skills[i];
        }
        r[0x57] = (byte)k.Byte57;
        PutWord(r, 0x5E, k.EffectImmunity);
        for (var i = 0; i < 10; i++)
        {
            r[0x60 + i] = (byte)k.Components[i];
            r[0x6A + i] = (byte)k.ComponentQualities[i];
        }
        for (var i = 0; i < EnemyKind.MaxItems; i++)
        {
            var at = 0x74 + 6 * i;
            var s = i < k.Items.Count ? k.Items[i] : new ItemStack(-1, 0xFF, 25, 0, 0);
            PutWord(r, at, s.Code);
            r[at + 2] = (byte)s.Type;
            r[at + 3] = (byte)s.Quality;
            r[at + 4] = (byte)s.Count;
            r[at + 5] = (byte)s.Weight;
        }
        r[0xD4] = (byte)k.Items.Count;
        return r;
    }

    public static byte[] Write(EnemyGroup g) => g.ToRecord();

    public static EnemyGroup ReadGroup(ReadOnlySpan<byte> r) => EnemyGroup.FromRecord(r);

    private static short Word(ReadOnlySpan<byte> r, int at) => (short)(r[at] | r[at + 1] << 8);

    private static void PutWord(byte[] r, int at, int value)
    {
        r[at] = (byte)value;
        r[at + 1] = (byte)(value >> 8);
    }

    // C: _fstrcpy copies up to the terminating zero; the bytes after it keep the default record's bytes (zeros).
    private static void PutText(byte[] r, int at, int size, string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, size - 1)).CopyTo(r.AsSpan(at));
    }
}
