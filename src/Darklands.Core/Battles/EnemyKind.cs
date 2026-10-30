using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>
/// One kind of enemy on a battlefield level (<c>struct mon</c>, 0xD6 bytes, the level%d.trn
/// record): its template, weapon, missile weapon, armour, shield, attributes, skills, money,
/// components and the items it carries. Fields keep the original's widths.
/// </summary>
// orig: battlefield-monster-table (R004 enemy buffer +0x180, 16 levels × 12 kinds); defaults from monsters-reset R004+0x04CC
public sealed class EnemyKind
{
    public const int MaxItems = 16;

    /// <summary>+0x00: (template &lt;&lt; 8) | weapon type, or the missile type / 0x84 for missile users and casters.</summary>
    public ushort Identity { get; set; } = 0xFFFF;

    /// <summary>+0x02: (template &lt;&lt; 8) | weapon type (the sprite variant).</summary>
    public ushort Appearance { get; set; } = 0xFFFF;

    /// <summary>+0x04: bit per level on which this kind appears.</summary>
    public ushort LevelMask { get; set; }

    /// <summary>+0x06: 0x80 when it starts with its missile weapon (or casts).</summary>
    public short MissileFlag { get; set; }

    /// <summary>+0x08: the template's base load (+0x2E).</summary>
    public sbyte BaseLoad { get; set; }

    /// <summary>+0x09: its three palettes in ENEMYPAL.DAT (−1 none).</summary>
    public sbyte[] Palettes { get; } = [-1, -1, -1];

    /// <summary>+0x0C: killing it ends the battle (the castle lord); becomes DS:C1F3 of its combatant (R020 .enm +0xAA).</summary>
    public sbyte EndsBattleWhenKilled { get; set; }

    /// <summary>+0x0D: 0 fights hand to hand, 1 shoots, 2 casts.</summary>
    public byte Class { get; set; }

    /// <summary>+0x0E: sprite family ("E02").</summary>
    public string Image { get; set; } = "";

    /// <summary>+0x12: the enemy type's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>+0x26 / +0x27: from the template's +0x13 / +0x12 (+0x27 is 2 for missile users and casters). UNCERTAIN.</summary>
    public sbyte Byte26 { get; set; }

    public sbyte Byte27 { get; set; }

    /// <summary>+0x28 hand weapon type, +0x29 its quality.</summary>
    public sbyte WeaponType { get; set; } = -1;

    public sbyte WeaponQuality { get; set; } = 25;

    /// <summary>+0x2A missile weapon type, +0x2B its quality.</summary>
    public sbyte MissileType { get; set; } = -1;

    public sbyte MissileQuality { get; set; } = 25;

    /// <summary>+0x2C: becomes the combatant's +0x24.</summary>
    public sbyte PiercingResistance { get; set; }

    /// <summary>+0x2D: damage/penetration bonus byte.</summary>
    public byte WeaponBonus { get; set; }

    /// <summary>+0x2E: weight of what it carries.</summary>
    public byte Load { get; set; }

    /// <summary>+0x2F armour types, +0x31 armour qualities.</summary>
    public sbyte[] ArmourTypes { get; } = [-1, -1];

    public sbyte[] ArmourQualities { get; } = [25, 25];

    /// <summary>+0x33: armour-piercing bit and natural armour (the combatant's +0x55).</summary>
    public byte Protection { get; set; }

    /// <summary>+0x34 shield quality, +0x35 shield type.</summary>
    public sbyte ShieldQuality { get; set; } = 25;

    public sbyte ShieldType { get; set; } = -1;

    /// <summary>+0x36 attributes, +0x3D their maxima (the battle uses +0x36 for both).</summary>
    public sbyte[] Attributes { get; } = Filled(7, 20);

    public sbyte[] Maximums { get; } = Filled(7, 20);

    /// <summary>+0x44: skills.</summary>
    public sbyte[] Skills { get; } = Filled(19, 20);

    /// <summary>+0x57 and the bonus dice copies +0x58/+0x5A/+0x5C. What reads them is not established.</summary>
    public sbyte Byte57 { get; set; }

    public sbyte[] StrengthBonusDice { get; } = new sbyte[2];

    public sbyte[] EnduranceBonusDice { get; } = new sbyte[2];

    public sbyte[] SkillBonusDice { get; } = new sbyte[2];

    /// <summary>+0x5E: the effect types it does not feel (bit per type; the template's +0x30).</summary>
    public short EffectImmunity { get; set; }

    /// <summary>+0x60: alchemical components carried (count per component), +0x6A their qualities.</summary>
    public sbyte[] Components { get; } = new sbyte[10];

    public sbyte[] ComponentQualities { get; } = Filled(10, 20);

    /// <summary>+0x74: the items carried (at most 16; +0xD4 the count).</summary>
    public List<ItemStack> Items { get; } = [];

    public EnemyKind Clone()
    {
        var copy = new EnemyKind
        {
            Identity = Identity, Appearance = Appearance, LevelMask = LevelMask, MissileFlag = MissileFlag, BaseLoad = BaseLoad,
            EndsBattleWhenKilled = EndsBattleWhenKilled, Class = Class, Image = Image, Name = Name, Byte26 = Byte26, Byte27 = Byte27,
            WeaponType = WeaponType, WeaponQuality = WeaponQuality, MissileType = MissileType, MissileQuality = MissileQuality,
            PiercingResistance = PiercingResistance, WeaponBonus = WeaponBonus, Load = Load, Protection = Protection,
            ShieldQuality = ShieldQuality, ShieldType = ShieldType, Byte57 = Byte57, EffectImmunity = EffectImmunity,
        };
        Palettes.CopyTo(copy.Palettes, 0);
        ArmourTypes.CopyTo(copy.ArmourTypes, 0);
        ArmourQualities.CopyTo(copy.ArmourQualities, 0);
        Attributes.CopyTo(copy.Attributes, 0);
        Maximums.CopyTo(copy.Maximums, 0);
        Skills.CopyTo(copy.Skills, 0);
        StrengthBonusDice.CopyTo(copy.StrengthBonusDice, 0);
        EnduranceBonusDice.CopyTo(copy.EnduranceBonusDice, 0);
        SkillBonusDice.CopyTo(copy.SkillBonusDice, 0);
        Components.CopyTo(copy.Components, 0);
        ComponentQualities.CopyTo(copy.ComponentQualities, 0);
        copy.Items.AddRange(Items);
        return copy;
    }

    private static sbyte[] Filled(int n, sbyte value) => Enumerable.Repeat(value, n).ToArray();
}

/// <summary>
/// A group of enemies on a level (<c>struct group</c>, 0x1E bytes, the level%d.atv record):
/// where it waits, how far it spreads, up to three kinds with their numbers, and its money.
/// </summary>
// orig: battlefield-group-table (R004 enemy buffer +0xA200, 16 levels × 10 groups); defaults from groups-reset R004+0x1060
public sealed class EnemyGroup
{
    /// <summary>+0x00/+0x02: where it starts (map pixels), +0x04 how far it spreads.</summary>
    public short X { get; set; }

    public short Y { get; set; }

    public short Spread { get; set; }

    /// <summary>+0x06: the room of its marker (−1 none).</summary>
    public sbyte Room { get; set; } = -1;

    /// <summary>+0x07: 1 when it has a reinforcement record (tac.txt "AllowReenf").</summary>
    public sbyte HasReinforcements { get; set; }

    /// <summary>+0x08: kind indexes on its level (−1 none), +0x0B how many of each.</summary>
    public sbyte[] Kinds { get; } = [-1, -1, -1];

    public sbyte[] Counts { get; } = new sbyte[3];

    /// <summary>+0x0E/+0x10/+0x12: its money (florins, groschen, pfennigs).</summary>
    public short Florins { get; set; }

    public short Groschen { get; set; }

    public short Pfennigs { get; set; }

    /// <summary>+0x14..+0x1C: the reinforcement record: the kind that arrives (−1 none), +0x15 (16), where (+0x16/+0x18), how many at a time
    /// (+0x1A), how many in all (+0x1B), facing toward the party (+0x1C). Whenever a batch has fallen and the pool is not empty, a new batch arrives.</summary>
    public sbyte ReinforcementKind { get; set; } = -1;

    /// <summary>+0x15: 16 when a reinforcement record is written. UNCERTAIN.</summary>
    public sbyte Byte15 { get; set; }

    public short ReinforcementX { get; set; }

    public short ReinforcementY { get; set; }

    public sbyte ReinforcementBatch { get; set; }

    public sbyte ReinforcementPool { get; set; }

    public sbyte ReinforcementFacing { get; set; } = -1;

    /// <summary>+0x1D: never set by groups-reset (C: it keeps whatever the buffer held); 0 here.</summary>
    public sbyte Byte1D { get; set; }

    /// <summary>A group from its 0x1E-byte record (<c>level%d.atv</c>, the groups of a prepared battlefield file).</summary>
    public static EnemyGroup FromRecord(ReadOnlySpan<byte> r)
    {
        static short Word(ReadOnlySpan<byte> r, int at) => (short)(r[at] | r[at + 1] << 8);
        var g = new EnemyGroup
        {
            X = Word(r, 0x00), Y = Word(r, 0x02), Spread = Word(r, 0x04), Room = (sbyte)r[0x06], HasReinforcements = (sbyte)r[0x07],
            Florins = Word(r, 0x0E), Groschen = Word(r, 0x10), Pfennigs = Word(r, 0x12), ReinforcementKind = (sbyte)r[0x14],
            Byte15 = (sbyte)r[0x15], ReinforcementX = Word(r, 0x16), ReinforcementY = Word(r, 0x18), ReinforcementBatch = (sbyte)r[0x1A],
            ReinforcementPool = (sbyte)r[0x1B], ReinforcementFacing = (sbyte)r[0x1C], Byte1D = (sbyte)r[0x1D],
        };
        for (var i = 0; i < 3; i++)
        {
            g.Kinds[i] = (sbyte)r[0x08 + i];
            g.Counts[i] = (sbyte)r[0x0B + i];
        }
        return g;
    }

    /// <summary>The group's 0x1E-byte record (the level%d.atv record).</summary>
    public byte[] ToRecord()
    {
        var r = new byte[0x1E];
        void Word(int at, int value)
        {
            r[at] = (byte)value;
            r[at + 1] = (byte)(value >> 8);
        }
        Word(0x00, X);
        Word(0x02, Y);
        Word(0x04, Spread);
        r[0x06] = (byte)Room;
        r[0x07] = (byte)HasReinforcements;
        for (var i = 0; i < 3; i++)
        {
            r[0x08 + i] = (byte)Kinds[i];
            r[0x0B + i] = (byte)Counts[i];
        }
        Word(0x0E, Florins);
        Word(0x10, Groschen);
        Word(0x12, Pfennigs);
        r[0x14] = (byte)ReinforcementKind;
        r[0x15] = (byte)Byte15;
        Word(0x16, ReinforcementX);
        Word(0x18, ReinforcementY);
        r[0x1A] = (byte)ReinforcementBatch;
        r[0x1B] = (byte)ReinforcementPool;
        r[0x1C] = (byte)ReinforcementFacing;
        r[0x1D] = (byte)Byte1D;
        return r;
    }

    public EnemyGroup Clone()
    {
        var copy = new EnemyGroup
        {
            X = X, Y = Y, Spread = Spread, Room = Room, HasReinforcements = HasReinforcements, Florins = Florins, Groschen = Groschen,
            Pfennigs = Pfennigs, ReinforcementKind = ReinforcementKind, Byte15 = Byte15, ReinforcementX = ReinforcementX, ReinforcementY = ReinforcementY, ReinforcementBatch = ReinforcementBatch,
            ReinforcementPool = ReinforcementPool, ReinforcementFacing = ReinforcementFacing, Byte1D = Byte1D,
        };
        Kinds.CopyTo(copy.Kinds, 0);
        Counts.CopyTo(copy.Counts, 0);
        return copy;
    }
}
