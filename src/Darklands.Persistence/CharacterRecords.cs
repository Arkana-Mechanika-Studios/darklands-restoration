using Darklands.Core.Characters;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>
/// A character's 0x22A-byte record (CHARACTR.TMP, a saved game) ↔ <see cref="Character"/>: the
/// 0x80-byte member record (the combatant record of the party slot), then the saints known
/// (0x14 bytes), the formulas known (0x16 bytes) and the pack (64 entries of 6 bytes).
/// </summary>
// orig: the record as R037+0x2ACE writes it for a party member (combatants + slot * 0x80, saints-learned[slot],
// known-recipes[slot], inventories[slot]) and R037+0x8ED4 reads it back; field offsets from the member accessors
// (party[] = combatants + 0x14, DS:9C69) as the rules use them (Character, CombatantRecords); tests: OriginalSaveTests
// C: party-relative offsets in the rules are record offsets − 0x14 (party[i][1] is the shield letter at record +0x15).
// C: the short name field runs into the weapon potion byte (+0x48): a nickname of 10 letters ends on it, as the original's strcpy reads it.
// C: the bytes this model does not name (+0x02..+0x11 combat position and orders, +0x16, +0x18..+0x21, +0x23, +0x4A,
// +0x4D/+0x4E, +0x52..+0x54, +0x56/+0x57, +0x59, +0x7F) are kept as read (Character.OriginalRecord): R037+0x2ACE writes the 0x80 + 0x14 +
// 0x16 + 0x180 bytes raw; a character made by this program carries the bytes the generator's reset writes (CharacterCreation.ResetRecord,
// R037+0x675A; tests CharacterCreationTests.TheMadeCharacterKeepsTheResetRecordsBytes, OriginalSaveTests.AMadeCharacterIsWrittenWithTheResetRecordsBytes).
// The saint and formula bytes and all 64 pack entries are modelled as raw bytes (KnownBits, Pack).
public static class CharacterRecords
{
    public const int Size = 0x22A;

    private const int RosterLocationAt = 0x00;
    private const int AgeAt = 0x12;
    private const int StatusAt = 0x14;
    private const int ShieldAt = 0x15;
    private const int SexAt = 0x17;
    private const int MissileTypeAt = 0x22;
    private const int FirewallAt = 0x24;
    private const int NameAt = 0x25;
    private const int NameLength = 0x19;
    private const int ShortNameAt = 0x3E;
    private const int ShortNameLength = 0x0B;
    private const int WeaponPotionsAt = 0x48;
    private const int EncumbranceAt = 0x49;
    private const int VitalTypeAt = 0x4B;
    private const int LimbTypeAt = 0x4C;
    private const int VitalQualityAt = 0x4F;
    private const int LimbQualityAt = 0x50;
    private const int WeaponTypeAt = 0x51;
    private const int ArmourPotionsAt = 0x55;
    private const int WeaponQualityAt = 0x58;
    private const int MissileQualityAt = 0x5A;
    private const int ShieldQualityAt = 0x5B;
    private const int ShieldTypeAt = 0x5C;
    private const int AttributesAt = 0x5D;
    private const int MaximumsAt = 0x64;
    private const int SkillsAt = 0x6B;
    private const int ItemCountAt = 0x7E;
    private const int SaintsAt = 0x80;
    private const int SaintBytes = 0x14;
    private const int FormulasAt = 0x94;
    private const int FormulaBytes = 0x16;
    private const int PackAt = 0xAA;
    private const int ItemSize = 6;

    public static Character Read(ReadOnlySpan<byte> r)
    {
        var c = new Character
        {
            OriginalRecord = r[..Size].ToArray(),
            RosterLocation = Word(r, RosterLocationAt),
            Age = Word(r, AgeAt),
            Status = (CharacterStatus)r[StatusAt],
            ShieldLetter = (char)r[ShieldAt],
            IsFemale = r[SexAt] == 1,
            Name = Text(r, NameAt, NameLength),
            ShortName = Text(r, ShortNameAt, ShortNameLength),
            WeaponPotions = r[WeaponPotionsAt],
            // C: member-tiredness is a signed char (cbw).
            Encumbrance = (sbyte)r[EncumbranceAt],
            ArmourPotions = r[ArmourPotionsAt],
            FirewallStrength = r[FirewallAt],
            VitalArmor = Equipped(r[VitalTypeAt], r[VitalQualityAt]),
            LimbArmor = Equipped(r[LimbTypeAt], r[LimbQualityAt]),
            Weapon = Equipped(r[WeaponTypeAt], r[WeaponQualityAt]),
            MissileWeapon = Equipped(r[MissileTypeAt], r[MissileQualityAt]),
            Shield = Equipped(r[ShieldTypeAt], r[ShieldQualityAt]),
        };
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            c.Attributes[k] = r[AttributesAt + k];
            c.AttributeMaximums[k] = r[MaximumsAt + k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            c.Skills[k] = r[SkillsAt + k];
        }
        // C: the stack count is the signed char at +0x7E (party[].nitems); all 64 entries are read, the stale ones past the count too.
        for (var i = 0; i < Character.PackSize; i++)
        {
            var e = r.Slice(PackAt + ItemSize * i, ItemSize);
            c.Pack[i] = new ItemStack(UWord(e, 0), e[2], e[3], e[4], e[5]);
        }
        c.Pack.Stacks = (sbyte)r[ItemCountAt];
        // orig: member-flag-bit / saint tests: saint s is bit 0x80 >> (s & 7) of byte s >> 3 (Saints.cs)
        r.Slice(SaintsAt, SaintBytes).CopyTo(c.Saints.Bytes);
        // orig: formula f = potion * 3 + grade is bit (1 << grade) of the potion's byte (CharacterCreation, carries-item); the whole byte is kept
        r.Slice(FormulasAt, FormulaBytes).CopyTo(c.Formulas.Bytes);
        return c;
    }

    /// <summary>The record of a character: its own record's bytes (or zeros past them) with every field the model names written over them.</summary>
    public static byte[] Write(Character c)
    {
        var r = new byte[Size];
        // a hireling's record is its member record only (PartyRules.AddMember): the rest of the record is zeros
        c.OriginalRecord?.AsSpan(0, Math.Min(c.OriginalRecord.Length, Size)).CopyTo(r);
        PutWord(r, RosterLocationAt, c.RosterLocation);
        PutWord(r, AgeAt, c.Age);
        r[StatusAt] = (byte)c.Status;
        r[ShieldAt] = (byte)c.ShieldLetter;
        if ((r[SexAt] == 1) != c.IsFemale)
        {
            r[SexAt] = (byte)(c.IsFemale ? 1 : 0);
        }
        PutText(r, NameAt, NameLength, c.Name);
        PutText(r, ShortNameAt, ShortNameLength, c.ShortName);
        r[WeaponPotionsAt] = (byte)c.WeaponPotions;
        r[EncumbranceAt] = (byte)c.Encumbrance;
        r[ArmourPotionsAt] = (byte)c.ArmourPotions;
        r[FirewallAt] = (byte)c.FirewallStrength;
        PutEquipped(r, VitalTypeAt, VitalQualityAt, c.VitalArmor);
        PutEquipped(r, LimbTypeAt, LimbQualityAt, c.LimbArmor);
        PutEquipped(r, WeaponTypeAt, WeaponQualityAt, c.Weapon);
        PutEquipped(r, MissileTypeAt, MissileQualityAt, c.MissileWeapon);
        PutEquipped(r, ShieldTypeAt, ShieldQualityAt, c.Shield);
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            r[AttributesAt + k] = (byte)c.Attributes[k];
            r[MaximumsAt + k] = (byte)c.AttributeMaximums[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            r[SkillsAt + k] = (byte)c.Skills[k];
        }
        r[ItemCountAt] = (byte)c.Pack.Stacks;
        for (var i = 0; i < Character.PackSize; i++)
        {
            var item = c.Pack[i];
            var e = r.AsSpan(PackAt + ItemSize * i, ItemSize);
            PutWord(e, 0, item.Code);
            e[2] = (byte)item.Type;
            e[3] = (byte)item.Quality;
            e[4] = (byte)item.Count;
            e[5] = (byte)item.Weight;
        }
        c.Saints.Bytes.CopyTo(r, SaintsAt);
        c.Formulas.Bytes.CopyTo(r, FormulasAt);
        return r;
    }

    // C: an item type is an unsigned byte; 0xFF means nothing equipped (EquippedItem type -1).
    private static EquippedItem Equipped(byte type, byte quality) => new(type == 0xFF ? -1 : type, quality);

    private static void PutEquipped(byte[] r, int typeAt, int qualityAt, EquippedItem item)
    {
        r[typeAt] = item.Type < 0 ? (byte)0xFF : (byte)item.Type;
        r[qualityAt] = (byte)item.Quality;
    }
}
