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
        sbyte S(int at) => (sbyte)r[at];
        sbyte[] Bytes(int at, int n) => [.. r.Skip(at).Take(n).Select(b => (sbyte)b)];
        DiceEntry[] Dice(int at, int n) => [.. Enumerable.Range(0, n).Select(k => new DiceEntry((sbyte)r[at + 3 * k], (sbyte)r[at + 3 * k + 1], (sbyte)r[at + 3 * k + 2]))];
        short W(int at) => BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(at));
        return new EnemyTemplate
        {
            Index = index,
            Image = Text(r.AsSpan(0, 4)),
            Name = Text(r.AsSpan(4, 10)),
            VariantCount = S(0x0E),
            ColourSchemes = S(0x0F),
            PalettesPerScheme = S(0x10),
            FirstPalette = S(0x11),
            Byte12 = S(0x12),
            Byte13 = S(0x13),
            Attributes = Bytes(0x14, 7),
            Skills = Bytes(0x1B, 19),
            Load = S(0x2E),
            EffectImmunity = W(0x30),
            PotionDice = Dice(0x32, 22),
            ComponentDice = Dice(0x74, 10),
            ArmourTypes = Bytes(0x92, 2),
            ArmourItems = Bytes(0x94, 2),
            ArmourQuality = S(0x96),
            ShieldType = S(0x97),
            ShieldItem = S(0x98),
            ShieldQuality = S(0x99),
            MissileType = S(0x9A),
            MissileItem = S(0x9B),
            MissileQuality = S(0x9C),
            AmmunitionType = S(0x9D),
            AmmunitionItem = S(0x9E),
            AmmunitionCount = r[0x9F],
            WeaponTypes = Bytes(0xA0, 6),
            WeaponItems = Bytes(0xA6, 6),
            WeaponQuality = S(0xAC),
            ArmourDrops = [(S(0xAD), S(0xAE)), (S(0xAF), S(0xB0))],
            ShieldDrop = (S(0xB1), S(0xB2)),
            WeaponDrop = (S(0xB3), S(0xB4)),
            MissileDrop = (S(0xB5), S(0xB6)),
            ByteB9 = S(0xB9),
            StrengthBonusDice = Bytes(0xBA, 2),
            EnduranceBonusDice = Bytes(0xBC, 2),
            SkillBonusDice = Bytes(0xBE, 2),
            MoneyDiceSides = [W(0xC0), W(0xC2), W(0xC4)],
            MoneyDiceAdd = [W(0xC6), W(0xC8), W(0xCA)],
        };
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
