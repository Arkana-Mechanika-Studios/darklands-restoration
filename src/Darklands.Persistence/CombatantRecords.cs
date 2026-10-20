using Darklands.Core.Battles;
using Darklands.Core.Characters;

namespace Darklands.Persistence;

/// <summary>
/// The original's 0x80-byte combatant record ↔ <see cref="Combatant"/> (the record a party
/// member's character record also is, and an enemy's level%d.enm head). Used by the oracle
/// tests and by battle files.
/// </summary>
// orig: struct cbt (combatants DS:9C55, 13 × 0x80)
public static class CombatantRecords
{
    public const int Size = 0x80;

    /// <summary>Fills a combatant from a record (the battle-only tables stay as they are).</summary>
    public static void Read(ReadOnlySpan<byte> r, Combatant c)
    {
        c.ScreenPlace = (Word(r, 0x00), Word(r, 0x02));
        c.Position = new MapPoint(Word(r, 0x04), Word(r, 0x06));
        c.Destination = new MapPoint(Word(r, 0x08), Word(r, 0x0A));
        c.WeaponRange = (sbyte)r[0x0C];
        c.StrikeQuality = (sbyte)r[0x0D];
        c.Action = (CombatAction)(ushort)Word(r, 0x10);
        c.Status = (CharacterStatus)r[0x14];
        c.AnimationSlot = (sbyte)r[0x16];
        c.Facing = (sbyte)r[0x19];
        c.SpriteFacing = (sbyte)r[0x1A];
        c.AimDelay = (sbyte)r[0x1D];
        c.FightingValue = r[0x20];
        c.AimingValue = r[0x21];
        c.ReadiedWeapon = (sbyte)r[0x22];
        c.ReadiedMinimumSkill = (sbyte)r[0x23];
        c.PiercingResistance = (sbyte)r[0x24];
        c.WeaponBonus = r[0x48];
        c.Load = (sbyte)r[0x49];
        c.Target = (sbyte)r[0x4A];
        for (var k = 0; k < 2; k++)
        {
            c.ArmourType[k] = (sbyte)r[0x4B + k];
            c.ArmourValue[k] = (sbyte)r[0x4D + k];
            c.ArmourQuality[k] = (sbyte)r[0x4F + k];
        }
        c.DefaultWeapon = (sbyte)r[0x51];
        c.WeaponSpeed = r[0x52];
        c.WeaponClass = r[0x53];
        c.WeaponDamage = (sbyte)r[0x54];
        c.Protection = r[0x55];
        c.MinimumStrength = (sbyte)r[0x56];
        c.FullStrength = r[0x57];
        c.WeaponQuality = (sbyte)r[0x58];
        c.ReadiedSkill = (sbyte)r[0x59];
        c.MissileQuality = (sbyte)r[0x5A];
        c.ShieldQuality = (sbyte)r[0x5B];
        c.ShieldType = (sbyte)r[0x5C];
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            c.Attributes[k] = (sbyte)r[0x5D + k];
            c.Maximums[k] = (sbyte)r[0x64 + k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            c.Skills[k] = (sbyte)r[0x6B + k];
        }
    }

    /// <summary>Writes the fields <see cref="Read"/> reads into a record.</summary>
    public static void Write(Combatant c, Span<byte> r)
    {
        PutWord(r, 0x00, c.ScreenPlace.X);
        PutWord(r, 0x02, c.ScreenPlace.Y);
        PutWord(r, 0x04, c.Position.X);
        PutWord(r, 0x06, c.Position.Y);
        PutWord(r, 0x08, c.Destination.X);
        PutWord(r, 0x0A, c.Destination.Y);
        r[0x0C] = (byte)c.WeaponRange;
        r[0x0D] = (byte)c.StrikeQuality;
        PutWord(r, 0x10, (int)c.Action);
        r[0x14] = (byte)c.Status;
        r[0x16] = (byte)c.AnimationSlot;
        r[0x19] = (byte)c.Facing;
        r[0x1A] = (byte)c.SpriteFacing;
        r[0x1D] = (byte)c.AimDelay;
        r[0x20] = c.FightingValue;
        r[0x21] = c.AimingValue;
        r[0x22] = (byte)c.ReadiedWeapon;
        r[0x23] = (byte)c.ReadiedMinimumSkill;
        r[0x24] = (byte)c.PiercingResistance;
        r[0x48] = c.WeaponBonus;
        r[0x49] = (byte)c.Load;
        r[0x4A] = (byte)c.Target;
        for (var k = 0; k < 2; k++)
        {
            r[0x4B + k] = (byte)c.ArmourType[k];
            r[0x4D + k] = (byte)c.ArmourValue[k];
            r[0x4F + k] = (byte)c.ArmourQuality[k];
        }
        r[0x51] = (byte)c.DefaultWeapon;
        r[0x52] = c.WeaponSpeed;
        r[0x53] = c.WeaponClass;
        r[0x54] = (byte)c.WeaponDamage;
        r[0x55] = c.Protection;
        r[0x56] = (byte)c.MinimumStrength;
        r[0x57] = c.FullStrength;
        r[0x58] = (byte)c.WeaponQuality;
        r[0x59] = (byte)c.ReadiedSkill;
        r[0x5A] = (byte)c.MissileQuality;
        r[0x5B] = (byte)c.ShieldQuality;
        r[0x5C] = (byte)c.ShieldType;
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            r[0x5D + k] = (byte)c.Attributes[k];
            r[0x64 + k] = (byte)c.Maximums[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            r[0x6B + k] = (byte)c.Skills[k];
        }
    }

    /// <summary>The animation bytes of the record the battle keeps running (+0x18 pose, +0x1B frame, +0x1E/+0x1F step and frame delays).</summary>
    // orig: struct cbt +0x18, +0x1B, +0x1E, +0x1F (a party member's are saved with its character record in a battle)
    public static void WriteAnimation(Combatant c, Span<byte> r)
    {
        PutWord(r, 0x0E, InfoPointer(c));
        r[0x18] = (byte)c.Pose;
        r[0x1B] = (byte)c.Frame;
        r[0x1E] = (byte)c.StepDelay;
        r[0x1F] = (byte)c.FrameDelay;
    }

    /// <summary>Reads what <see cref="WriteAnimation"/> writes.</summary>
    public static void ReadAnimation(ReadOnlySpan<byte> r, Combatant c)
    {
        c.Pose = (Pose)(sbyte)r[0x18];
        c.Frame = (sbyte)r[0x1B];
        c.StepDelay = r[0x1E];
        c.FrameDelay = r[0x1F];
    }

    /// <summary>+0x0E: the near pointer to the animation set's info block for the pose (an address in the original's data segment).</summary>
    // orig: cbt_set_anim R014+0x38B6 (0E76:38B6) draft, checked against the asm (0x3C2E..0x3C38 for dying): &DS:CAB4[slot] walking
    // (0x90-byte blocks), &DS:CEAE[slot] (0x70-byte blocks, also R014's ANIMINFO) fighting and hit, &DS:D1E4[slot] (0x40-byte blocks) dying
    // C: before any pose the battle's reset (R002's 1830) leaves &DS:CAB4[i]; the blocks' addresses were read in the original's memory
    private static int InfoPointer(Combatant c) => c.Pose switch
    {
        Pose.Walking => 0xCAB4 + 0x90 * c.AnimationSlot,
        Pose.Fighting or Pose.Hit or Pose.Reload => 0xCEAE + 0x70 * c.AnimationSlot,
        Pose.Dying => 0xD1E4 + 0x40 * c.AnimationSlot,
        _ => 0xCAB4 + 0x90 * c.Index,
    };

    private static int Word(ReadOnlySpan<byte> r, int at) => (short)(r[at] | r[at + 1] << 8);

    private static void PutWord(Span<byte> r, int at, int value)
    {
        r[at] = (byte)value;
        r[at + 1] = (byte)(value >> 8);
    }
}
