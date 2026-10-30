namespace Darklands.Core.Battles;

/// <summary>
/// The weapon and armour tables the battle reads by item type (far segment 20A5 of the
/// program, 63 weapon types each): skill category, animation code, speed, class/penetration,
/// damage, minimum skill, minimum and full strength, range, ammunition type, and armour value
/// by type. They come from the player's DARKLAND.EXE (Darklands.Content reads them).
/// </summary>
/// <remarks>
/// The tables lie back to back, and the original indexes them without bounds: the armour table
/// at 20A5:781E is declared with 4 entries but read with armour types 67..84, which reach past
/// the ammunition table into the armour values. The whole block is kept so that any index reads
/// what the original reads.
/// </remarks>
// orig: weapon tables R136+0x75AC..R136+0x7822 (read by weapon-armed R014+0xAED0, hit-roll, may-shoot)
public sealed class CombatTables
{
    /// <summary>Far offset of the block kept: one byte before the first table (skill category), which a weapon of −1 reads.</summary>
    public const int FirstOffset = 0x75AB;

    /// <summary>Bytes kept: up to the armour table plus any byte index.</summary>
    public const int Length = 0x781E - FirstOffset + 0x100;

    public const int BareHands = 9;

    private const int Category = 0x75AC;
    private const int AnimationCodes = 0x75EB;
    private const int Speed = 0x7669;
    private const int ClassTable = 0x76A8;
    private const int Damage = 0x76E7;
    private const int MinimumSkillTable = 0x7726;
    private const int MinimumStrengthTable = 0x7765;
    private const int FullStrengthTable = 0x77A4;
    private const int Range = 0x77E3;
    private const int ArmourTable = 0x781E;
    private const int AmmunitionTable = 0x7822;

    private readonly byte[] _bytes;
    private readonly byte[] _lowDamage;
    private readonly byte[] _skillGain;

    /// <param name="bytes">The weapon block (<see cref="Length"/> bytes from 20A5:75AB).</param>
    /// <param name="lowDamage">The 16 bytes at DS:1B88: additions (8) then divisors (8) of the low damage levels.</param>
    /// <param name="skillGain">The 7 bytes at DS:22CE: skill-gain additions (4) then dice (3) by difficulty.</param>
    /// <param name="neighbourhoodPatterns">The 32 bytes at DS:13D0 (16 words): see <see cref="NeighbourhoodPatterns"/>; zeros when not given.</param>
    public CombatTables(byte[] bytes, byte[] lowDamage, byte[]? skillGain = null, byte[]? neighbourhoodPatterns = null)
    {
        _skillGain = skillGain ?? [0, 0, 0, 0, 3, 4, 5];
        NeighbourhoodPatterns = [.. Enumerable.Range(0, 16).Select(i => neighbourhoodPatterns is { Length: >= 32 } ? neighbourhoodPatterns[2 * i] | neighbourhoodPatterns[2 * i + 1] << 8 : 0)];
        if (bytes.Length < Length || lowDamage.Length < 16)
        {
            throw new ArgumentException($"The combat tables are {Length} + 16 bytes.", nameof(bytes));
        }
        _bytes = bytes;
        _lowDamage = lowDamage;
    }

    /// <summary>
    /// DS:13D0: sixteen words indexed by the open/closed state of four of the eight cells around a cell; bit k of the word is set when
    /// the state of the other four (k) would leave no way through. Used by the castle generator to keep stairs and furniture from
    /// blocking passages.
    /// </summary>
    // orig: DS:13D0 (read by R006+0x491A and R007+0x3BCE)
    public IReadOnlyList<int> NeighbourhoodPatterns { get; }

    /// <summary>DS:1B88: added to the 2d6 roll's quotient for damage levels 0..7.</summary>
    // orig: DS:1B88 (read by damage-pending-list R014+0xAD5A)
    public int LowDamageAddition(int level) => _lowDamage[level];

    /// <summary>DS:22D2: the die of a skill gain in battle, by difficulty (0 basic .. 2 expert).</summary>
    // orig: DS:22D2 (R021 data, read by ability-improve-attempt R021+0x0000)
    public int SkillGainDie(int difficulty) => (sbyte)_skillGain[4 + difficulty];

    /// <summary>DS:22CE: added to a skill gain in battle, by difficulty.</summary>
    // orig: DS:22CE
    public int SkillGainAddition(int difficulty) => (sbyte)_skillGain[difficulty];

    /// <summary>DS:1B90: what the 2d6 roll is divided by for damage levels 0..7.</summary>
    // orig: DS:1B90
    public int LowDamageDivisor(int level) => _lowDamage[8 + level];

    /// <summary>Skill used with a weapon type (0 edged … 6 missile devices).</summary>
    public int SkillCategory(int type) => (sbyte)At(Category, type);

    /// <summary>Two letters naming the weapon's animations ("SW", "BW" …).</summary>
    public string AnimationCode(int type) => new([(char)At(AnimationCodes, 2 * type), (char)At(AnimationCodes, 2 * type + 1)]);

    public byte WeaponSpeed(int type) => At(Speed, type);

    public byte WeaponClass(int type) => At(ClassTable, type);

    public sbyte WeaponDamage(int type) => (sbyte)At(Damage, type);

    public sbyte MinimumSkill(int type) => (sbyte)At(MinimumSkillTable, type);

    public sbyte MinimumStrength(int type) => (sbyte)At(MinimumStrengthTable, type);

    public sbyte FullStrength(int type) => (sbyte)At(FullStrengthTable, type);

    public sbyte WeaponRange(int type) => (sbyte)At(Range, type);

    /// <summary>The ammunition item type a weapon type shoots (0 none).</summary>
    public sbyte AmmunitionType(int type) => (sbyte)At(AmmunitionTable, type);

    /// <summary>Armour value of an armour type (the type indexes the table without bounds).</summary>
    public sbyte ArmourValue(int type) => (sbyte)At(ArmourTable, type);

    // C: indexes are char values promoted to int; a negative index would read before the table
    // (the original guards only the armour index).
    private byte At(int table, int index) => _bytes[table - FirstOffset + index];
}
