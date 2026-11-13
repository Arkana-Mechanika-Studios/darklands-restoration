using System.Buffers.Binary;
using System.Text;
using Darklands.Core.Battles;

namespace Darklands.Content.Formats.Battles;

/// <summary>Decodes DARKLAND.ENM: 71 enemy templates (0xCC bytes) then 82 enemy types (0x18 bytes).</summary>
// orig: battlefield-builder-entry R004+0x0000 loads darkland.enm whole into the enemy buffer (templates +0xB4C0, types +0xED54)
public static class EnemyCatalogueDecoder
{
    public const int TemplateCount = 71;
    public const int TypeCount = 82;

    public static EnemyCatalogue Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < TemplateCount * EnemyTemplate.Size + TypeCount * EnemyType.Size)
        {
            throw new InvalidDataException("DARKLAND.ENM is too short.");
        }
        var templates = new List<EnemyTemplate>();
        for (var i = 0; i < TemplateCount; i++)
        {
            templates.Add(Template(data.Slice(i * EnemyTemplate.Size, EnemyTemplate.Size).ToArray(), i));
        }
        var types = new List<EnemyType>();
        var first = TemplateCount * EnemyTemplate.Size;
        for (var i = 0; i < TypeCount; i++)
        {
            var r = data.Slice(first + i * EnemyType.Size, EnemyType.Size);
            types.Add(new EnemyType
            {
                Index = i,
                Template = BinaryPrimitives.ReadInt16LittleEndian(r),
                Name = Text(r.Slice(2, 20)),
                Flags = r[0x16],
            });
        }
        return new EnemyCatalogue(templates, types);
    }

    private static EnemyTemplate Template(byte[] r, int index)
    {
        sbyte SignedAt(int at) => (sbyte)r[at];
        sbyte[] SignedBytesAt(int at, int n) => [.. r.Skip(at).Take(n).Select(b => (sbyte)b)];
        DiceEntry[] DiceAt(int at, int n) => [.. Enumerable.Range(0, n).Select(k => new DiceEntry((sbyte)r[at + 3 * k], (sbyte)r[at + 3 * k + 1], (sbyte)r[at + 3 * k + 2]))];
        short WordAt(int at) => BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(at));
        return new EnemyTemplate
        {
            Index = index,
            Image = Text(r.AsSpan(0, 4)),
            Name = Text(r.AsSpan(4, 10)),
            VariantCount = SignedAt(0x0E),
            ColourSchemes = SignedAt(0x0F),
            PalettesPerScheme = SignedAt(0x10),
            FirstPalette = SignedAt(0x11),
            Byte12 = SignedAt(0x12),
            Byte13 = SignedAt(0x13),
            Attributes = SignedBytesAt(0x14, 7),
            Skills = SignedBytesAt(0x1B, 19),
            Load = SignedAt(0x2E),
            EffectImmunity = WordAt(0x30),
            PotionDice = DiceAt(0x32, 22),
            ComponentDice = DiceAt(0x74, 10),
            ArmourTypes = SignedBytesAt(0x92, 2),
            ArmourItems = SignedBytesAt(0x94, 2),
            ArmourQuality = SignedAt(0x96),
            ShieldType = SignedAt(0x97),
            ShieldItem = SignedAt(0x98),
            ShieldQuality = SignedAt(0x99),
            MissileType = SignedAt(0x9A),
            MissileItem = SignedAt(0x9B),
            MissileQuality = SignedAt(0x9C),
            AmmunitionType = SignedAt(0x9D),
            AmmunitionItem = SignedAt(0x9E),
            AmmunitionCount = r[0x9F],
            WeaponTypes = SignedBytesAt(0xA0, 6),
            WeaponItems = SignedBytesAt(0xA6, 6),
            WeaponQuality = SignedAt(0xAC),
            ArmourDrops = [(SignedAt(0xAD), SignedAt(0xAE)), (SignedAt(0xAF), SignedAt(0xB0))],
            ShieldDrop = (SignedAt(0xB1), SignedAt(0xB2)),
            WeaponDrop = (SignedAt(0xB3), SignedAt(0xB4)),
            MissileDrop = (SignedAt(0xB5), SignedAt(0xB6)),
            ByteB9 = SignedAt(0xB9),
            StrengthBonusDice = SignedBytesAt(0xBA, 2),
            EnduranceBonusDice = SignedBytesAt(0xBC, 2),
            SkillBonusDice = SignedBytesAt(0xBE, 2),
            MoneyDiceSides = [WordAt(0xC0), WordAt(0xC2), WordAt(0xC4)],
            MoneyDiceAdd = [WordAt(0xC6), WordAt(0xC8), WordAt(0xCA)],
        };
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
