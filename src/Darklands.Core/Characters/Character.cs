namespace Darklands.Core.Characters;

/// <summary>The seven attributes, in the game's order.</summary>
public enum Attribute
{
    Endurance,
    Strength,
    Agility,
    Perception,
    Intelligence,
    Charisma,
    DivineFavor,
}

/// <summary>The nineteen skills, in the game's order.</summary>
public enum Skill
{
    EdgedWeapons,
    ImpactWeapons,
    FlailWeapons,
    Polearms,
    ThrownWeapons,
    Bows,
    MissileDevices,
    Alchemy,
    Religion,
    Virtue,
    SpeakCommon,
    SpeakLatin,
    ReadWrite,
    Healing,
    Artifice,
    Stealth,
    Streetwise,
    Riding,
    Woodwise,
}

/// <summary>Whether a party slot holds someone and what state they are in.</summary>
public enum CharacterStatus
{
    Empty = 0,
    Able = 1,
    Unconscious = 2,
    Dead = 3,
    Away = 5,
}

/// <summary>An equipped item: its item type and quality; type -1 means nothing equipped.</summary>
public readonly record struct EquippedItem(int Type, int Quality)
{
    public static EquippedItem None => new(-1, 25);

    public bool IsEmpty => Type < 0;
}

/// <summary>A stack of identical items in a pack.</summary>
public sealed record ItemStack(int Code, int Type, int Quality, int Count, int Weight);

/// <summary>A party member.</summary>
// orig: party DS:9C69
// UNCERTAIN: IsFemale is record byte +3 (the "women" queries test it == 1); Age is the word before the record (see MemberRules.Age notes in CardContext.MemberAge).
public sealed class Character
{
    public const int AttributeCount = 7;
    public const int SkillCount = 19;
    public const int SaintCount = 160;
    public const int FormulaCount = 66;
    public const int PackSize = 64;

    public string Name { get; set; } = "";

    /// <summary>The short name shown in lists and on cards ("Gretch").</summary>
    public string ShortName { get; set; } = "";

    public int Age { get; set; }

    public bool IsFemale { get; set; }

    public CharacterStatus Status { get; set; } = CharacterStatus.Able;

    /// <summary>The letter of the heraldic shield picture (pics\shield&lt;letter&gt;.pic).</summary>
    public char ShieldLetter { get; set; } = 'A';

    /// <summary>The figure the member is drawn with (pics\<code>small.pic, the <code>C.CAT sprites): "F01", "F60", "A00", "C00".</summary>
    // orig: member_shield[slot] (DS:A668, 4 bytes per party slot); saved with the party block of charactr.tmp
    public string Image { get; set; } = "F01";

    /// <summary>The figure's own colours: 8 VGA triples (0..63) for colours 237..244.</summary>
    // orig: member_palette = R136+0x33FC[slot] (24 bytes)
    public byte[] ImagePalette { get; set; } = new byte[24];

    public int[] Attributes { get; } = new int[AttributeCount];

    public int[] AttributeMaximums { get; } = new int[AttributeCount];

    public int[] Skills { get; } = new int[SkillCount];

    public EquippedItem VitalArmor { get; set; } = EquippedItem.None;

    public EquippedItem LimbArmor { get; set; } = EquippedItem.None;

    public EquippedItem Weapon { get; set; } = EquippedItem.None;

    public EquippedItem MissileWeapon { get; set; } = EquippedItem.None;

    public EquippedItem Shield { get; set; } = EquippedItem.None;

    /// <summary>
    /// Battle charms (member record byte +0x41): the low nibble is set by the Hardarmor potion,
    /// the high nibble by Firewall (+1) and by saints (0xA, 0xB); battle shows "+" on the armour
    /// names while bits 0..6 are set. The same byte as <see cref="ArmourPotions"/>.
    /// </summary>
    // UNCERTAIN: the nibbles' exact meaning in battle is not established.
    public byte BattleCharms
    {
        get => (byte)ArmourPotions;
        set => ArmourPotions = value;
    }

    /// <summary>The strength of a battle charm (member record byte +0x10: Firewall 50/70/90, St.Polycarp 90); the same byte as <see cref="FirewallStrength"/>.</summary>
    // UNCERTAIN: named after the potion and saint that set it; no reader of it is known.
    public byte CharmStrength
    {
        get => (byte)FirewallStrength;
        set => FirewallStrength = value;
    }

    /// <summary>Total weight of the equipped items.</summary>
    // orig: party DS:9C69
    // C: record byte +0x35 is a signed char (member_fatigue cbw): values above 127 read back negative.
    public int Encumbrance { get; set; }

    /// <summary>Weapon potions in effect (record +0x34): high nibble the damage bonus, low nibble the penetration bonus.</summary>
    public int WeaponPotions { get; set; }

    /// <summary>Armour potions in effect (record +0x41): high nibble the Firewalls, low nibble the Hardarmor bonus.</summary>
    // orig: the same record byte as BattleCharms (+0x41): one storage, two names (potions and saints write it); a byte, so writes wrap.
    public int ArmourPotions
    {
        get => _armourPotions;
        set => _armourPotions = (byte)value;
    }

    private byte _armourPotions;

    /// <summary>Strength of the last Firewall drunk (record +0x10).</summary>
    // orig: the same record byte as CharmStrength (+0x10); in battle it is the combatant's +0x24 (R019 reads it against fire); a byte.
    public int FirewallStrength
    {
        get => _firewallStrength;
        set => _firewallStrength = (byte)value;
    }

    private byte _firewallStrength;

    /// <summary>The pack: 64 entries and a signed stack count, as the original keeps them (see <see cref="Characters.Pack"/>).</summary>
    public Pack Pack { get; } = new();

    /// <summary>
    /// Where the character was left (the record's first word: the location when the character
    /// was made or last left the party); the inn's roster lists the characters left there.
    /// </summary>
    // orig: struct chr +0x00 (R037 seg 1462): R037+0x38AA stores cur_loc for a new character, party_remove R037+0x3B58 for one who leaves
    public int RosterLocation { get; set; }

    /// <summary>
    /// The 0x22A-byte record this character was read from (CHARACTR.TMP / a saved game), kept by
    /// the persistence layer so that the bytes this model does not name are written back as
    /// they were. The rules never read it; null for characters made by this program.
    /// </summary>
    public byte[]? OriginalRecord { get; set; }

    /// <summary>Saints the character knows (0..159), kept as the record's 20 bytes.</summary>
    public KnownBits Saints { get; } = KnownBits.SaintTable();

    /// <summary>Alchemical formulas the character knows (0..65: potion = f / 3, grade = f % 3), kept as the record's 22 bytes.</summary>
    public KnownBits Formulas { get; } = KnownBits.FormulaTable();

    public int this[Attribute a]
    {
        get => Attributes[(int)a];
        set => Attributes[(int)a] = value;
    }

    public int this[Skill s]
    {
        get => Skills[(int)s];
        set => Skills[(int)s] = value;
    }

    public int Maximum(Attribute a) => AttributeMaximums[(int)a];

    public bool IsPresent => Status != CharacterStatus.Empty;

    /// <summary>A separate copy (the party slot's record read from the roster).</summary>
    public Character Copy()
    {
        var copy = new Character
        {
            Name = Name, ShortName = ShortName, Age = Age, IsFemale = IsFemale, Status = Status, ShieldLetter = ShieldLetter,
            Image = Image, ImagePalette = [.. ImagePalette],
            VitalArmor = VitalArmor, LimbArmor = LimbArmor, Weapon = Weapon, MissileWeapon = MissileWeapon, Shield = Shield,
            Encumbrance = Encumbrance, WeaponPotions = WeaponPotions, ArmourPotions = ArmourPotions,
            FirewallStrength = FirewallStrength, RosterLocation = RosterLocation, OriginalRecord = OriginalRecord,
        };
        Attributes.CopyTo(copy.Attributes, 0);
        AttributeMaximums.CopyTo(copy.AttributeMaximums, 0);
        Skills.CopyTo(copy.Skills, 0);
        copy.Pack.CopyFrom(Pack);
        Saints.Bytes.CopyTo(copy.Saints.Bytes, 0);
        Formulas.Bytes.CopyTo(copy.Formulas.Bytes, 0);
        return copy;
    }

    public bool IsAble => Status == CharacterStatus.Able;
}
