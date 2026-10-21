using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>
/// Filling the combatant slots: a party member's slot is its character record; an enemy's slot
/// is built from its kind (the level loader's conversion of level%d.trn into level%d.enm and the
/// placement's copy into combatants 5..12).
/// </summary>
public static class CombatantSetup
{
    /// <summary>Puts a party member into its slot (the original's party record is the combatant record).</summary>
    // orig: party record = combatants[i] + 0x14 (struct cbt); the battle-only fields as battle_state_reset R002+0x1830 leaves them
    // C: the record's +0x48 is the weapon potions byte (character +0x34), +0x55 the armour potions and charms (+0x41), +0x24 the
    // Firewall's strength (+0x10): the same bytes, so the potions drunk before (and during) the battle count in it.
    public static void FromMember(Combatant c, Character member)
    {
        c.Member = member;
        c.Status = member.Status;
        // C: +0x00 (the roster's location word) and +0x02 are scrx and scry until a frame draws the member
        c.ScreenPlace = (member.RosterLocation, member.OriginalRecord is { Length: >= 4 } r ? (short)(r[2] | r[3] << 8) : 0);
        // orig: the record is copied whole, so the destination (+0x08..+0x0B) and the aim delay (+0x1D) are what the saved game held (the last
        // fight's) until the member walks or aims, and a saved game written afterwards keeps them
        if (member.OriginalRecord is { Length: > 0x1D } held)
        {
            c.Destination = new MapPoint((short)(held[0x08] | held[0x09] << 8), (short)(held[0x0A] | held[0x0B] << 8));
            c.AimDelay = (sbyte)held[0x1D];
        }
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            c.Attributes[k] = (sbyte)member.Attributes[k];
            c.Maximums[k] = (sbyte)member.AttributeMaximums[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            c.Skills[k] = (sbyte)member.Skills[k];
        }
        c.ArmourType[0] = (sbyte)member.VitalArmor.Type;
        c.ArmourType[1] = (sbyte)member.LimbArmor.Type;
        c.ArmourQuality[0] = (sbyte)member.VitalArmor.Quality;
        c.ArmourQuality[1] = (sbyte)member.LimbArmor.Quality;
        c.DefaultWeapon = (sbyte)member.Weapon.Type;
        c.WeaponQuality = (sbyte)member.Weapon.Quality;
        c.ReadiedWeapon = (sbyte)member.MissileWeapon.Type;
        c.MissileQuality = (sbyte)member.MissileWeapon.Quality;
        c.ShieldType = (sbyte)member.Shield.Type;
        c.ShieldQuality = (sbyte)member.Shield.Quality;
        c.Load = (sbyte)Math.Clamp(member.Encumbrance, sbyte.MinValue, sbyte.MaxValue);
        c.WeaponBonus = (byte)member.WeaponPotions;
        c.Protection = (byte)member.ArmourPotions;
        c.PiercingResistance = (sbyte)member.FirewallStrength;
        c.EffectImmunity = 0;
        // orig: the combatant's items are the member's pack itself: all 64 entries and the signed count, stale entries included
        c.Items.CopyFrom(member.Pack);
        c.Target = -1;
        c.Action = CombatAction.None;
    }

    /// <summary>
    /// What a screen opened during the battle may have changed in the member (pack, equipment,
    /// attributes and skills, the potion bytes) back into its combatant, the tactical state kept.
    /// </summary>
    // orig: the party record IS the combatant record (combat_char_sheet works on it directly)
    public static void Refresh(Combatant c, Character member)
    {
        c.Status = member.Status;
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            c.Attributes[k] = (sbyte)member.Attributes[k];
            c.Maximums[k] = (sbyte)member.AttributeMaximums[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            c.Skills[k] = (sbyte)member.Skills[k];
        }
        c.ArmourType[0] = (sbyte)member.VitalArmor.Type;
        c.ArmourType[1] = (sbyte)member.LimbArmor.Type;
        c.ArmourQuality[0] = (sbyte)member.VitalArmor.Quality;
        c.ArmourQuality[1] = (sbyte)member.LimbArmor.Quality;
        c.DefaultWeapon = (sbyte)member.Weapon.Type;
        c.WeaponQuality = (sbyte)member.Weapon.Quality;
        c.ReadiedWeapon = (sbyte)member.MissileWeapon.Type;
        c.MissileQuality = (sbyte)member.MissileWeapon.Quality;
        c.ShieldType = (sbyte)member.Shield.Type;
        c.ShieldQuality = (sbyte)member.Shield.Quality;
        c.Load = (sbyte)Math.Clamp(member.Encumbrance, sbyte.MinValue, sbyte.MaxValue);
        c.WeaponBonus = (byte)member.WeaponPotions;
        c.Protection = (byte)member.ArmourPotions;
        c.PiercingResistance = (sbyte)member.FirewallStrength;
        c.Items.CopyFrom(member.Pack);
    }

    /// <summary>Writes what the battle changed back into the party member.</summary>
    // orig: the party record is the combatant record itself; battle_run R002+0x0000 clears +0x02 and +0x3C of able/unconscious members afterwards
    public static void BackToMember(Combatant c)
    {
        if (c.Member is not { } member)
        {
            return;
        }
        member.Status = c.Status;
        // BUG: the record's +0x00 word is the roster's location of the character and the combatant's scrx: the battle's last frame
        // leaves the member's screen x there (cbt_screen_pos R014+0x99F6), which the inn's roster later reads as a location.
        member.RosterLocation = c.ScreenPlace.X;
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            member.Attributes[k] = c.Attributes[k];
        }
        for (var k = 0; k < Character.SkillCount; k++)
        {
            member.Skills[k] = c.Skills[k];
        }
        // C: the record's +0x22 / +0x5A as the battle left them (an empty slot keeps its quality byte)
        member.MissileWeapon = new EquippedItem(c.ReadiedWeapon < 0 ? -1 : c.ReadiedWeapon, c.MissileQuality);
        // The effects wear armour down and throw it away when it is worn through (R019 0968 / 0914).
        // orig: the party record is the combatant record: an empty armour slot keeps its quality byte (+0x4F / +0x50) as it was: the
        // loaded file's (a stale 0 for a member who never wore armour) until R019+0x0914 sets 0x19 on a piece worn through; the combat
        // character sheet (R022+0x0000) does not touch it, so its close leaves it (the quality of None, 25, would replace a 0)
        member.VitalArmor = new EquippedItem(c.ArmourType[0] < 0 ? -1 : c.ArmourType[0], c.ArmourQuality[0]);
        member.LimbArmor = new EquippedItem(c.ArmourType[1] < 0 ? -1 : c.ArmourType[1], c.ArmourQuality[1]);
        member.WeaponPotions = c.WeaponBonus;
        member.ArmourPotions = c.Protection;
        member.FirewallStrength = (byte)c.PiercingResistance;
        // orig: no copy in the original (the battle works on the pack 2E38:003E + i × 0x180 in place): every one of the 64
        // entries and the count come back as the battle left them, the stale entries past the last stack included; tests: CombatantPackTests
        member.Pack.CopyFrom(c.Items);
    }

    /// <summary>Fills an enemy slot from its kind, facing <paramref name="facing"/>, on level <paramref name="floor"/>, and readies its weapon.</summary>
    /// <param name="slot">The animation slot of its kind (5 or 6).</param>
    /// <param name="activation">True when placed by an activation (keeps its class); false for reinforcements (fight hand to hand).</param>
    // orig: level_enemies_load R020+0x0000 (1EF8:0000) draft (the .trn → .enm conversion);
    // enemies_place R025+0x0000 (1EF8:0000) draft (the copy into the slot)
    // DIVERGENCE: the head bytes +0x1E/+0x1F (both 8) are not modelled; the slot's other head fields are whatever the
    // .enm buffer held (0 for a fresh load), reset here.
    public static void FromEnemy(Battle battle, int index, EnemyKind kind, int floor, int facing, int slot, bool activation)
    {
        var c = battle[index];
        c.Member = null;
        c.Action = CombatAction.None;
        c.Floor = floor;
        c.Facing = c.SpriteFacing = facing;
        c.AnimationSlot = slot;
        c.Target = -1;
        c.ReadiedWeapon = kind.MissileType;
        c.MissileQuality = kind.MissileQuality;
        c.PiercingResistance = kind.PiercingResistance;
        c.WeaponBonus = kind.WeaponBonus;
        c.Load = (sbyte)kind.Load;
        for (var k = 0; k < 2; k++)
        {
            c.ArmourType[k] = kind.ArmourTypes[k];
            c.ArmourQuality[k] = kind.ArmourQualities[k];
        }
        c.DefaultWeapon = kind.WeaponType;
        c.WeaponQuality = kind.WeaponQuality;
        c.Protection = kind.Protection;
        c.ShieldQuality = kind.ShieldQuality;
        c.ShieldType = kind.ShieldType;
        for (var k = 0; k < Character.AttributeCount; k++)
        {
            // C: level_enemies_load copies the current attributes into both the current and the maximum values.
            c.Attributes[k] = kind.Attributes[k];
            c.Maximums[k] = kind.Attributes[k];
        }
        kind.Skills.CopyTo(c.Skills, 0);
        c.Items.Clear();
        c.Items.AddRange(kind.Items);
        c.EffectImmunity = kind.EffectImmunity;
        c.EnemyClass = activation ? kind.Class : 0;
        c.QueuedEndurance = c.QueuedStrength = 0;
        c.DamageShown = 0;
        c.AttackRate = 0xFF;
        if (kind.Class != 2)
        {
            var weapon = kind.MissileFlag == 0 ? c.DefaultWeapon : c.ReadiedWeapon;
            Attacks.ReadyWeapon(battle, index, weapon, kind.MissileFlag);
        }
        c.Status = CharacterStatus.Able;
    }

    /// <summary>At the start of the fight every party member and every hand-to-hand enemy readies its hand weapon.</summary>
    // orig: combat_main R014+0x0000 (0E76:0000) draft (the weapon_ready loop)
    public static void ReadyHandWeapons(Battle battle)
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = battle[i];
            if (c.Status != CharacterStatus.Empty && (i < Combatant.PartySlots || c.EnemyClass == 0))
            {
                Attacks.ReadyWeapon(battle, i, c.DefaultWeapon, 0);
            }
        }
    }
}
