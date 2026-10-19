using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>The to-hit result of one attack (<c>to_hit_roll</c>).</summary>
public enum HitResult
{
    /// <summary>No blow this tick (the attack-rate roll failed), or a plain miss.</summary>
    None = 0,

    /// <summary>Parried or dodged.</summary>
    Parried = 1,

    /// <summary>A clean hit.</summary>
    Hit = 2,

    /// <summary>A hit within 10 of the chance: a glancing blow (less penetration).</summary>
    Glancing = 3,
}

/// <summary>What one attack did: its to-hit result, the location hit, the damage, and the reaction shown.</summary>
/// <param name="MissVariant">For a plain miss, which of four miss sequences is played (drawn here, as in the original).</param>
/// <param name="Reaction">The attacker's next animation (cbt_anim): 1 weak hit, 2 hit, 6 parried, unchanged otherwise (−1).</param>
public readonly record struct AttackOutcome(HitResult Result, int Location, int Damage, int MissVariant, int Reaction);

/// <summary>
/// Attack resolution and damage (R014 18BF): attack rate, to-hit chances for hand-to-hand and
/// missile attacks, damage against armour, applying the damage, readying weapons, ammunition.
/// </summary>
// orig: R014 18BF (13 functions, 3 byte-matched per the draft notes); attack_rate, cbt_attack, to_hit_roll, 0592, hit_damage, damage_queue, damage_apply and weapon_ready were checked against and run against the original (Oracle:battle.json); the ammunition and pack helpers were read from the draft only
public static class Attacks
{
    /// <summary>Works out the attack rate when it is marked stale (255).</summary>
    // orig: attack_rate R014+0xA492 (18BF:0002) draft (byte-matched per the draft notes; checked against asm); tests: Oracle:battle.json
    public static void UpdateAttackRate(Battle battle, int i)
    {
        var c = battle[i];
        if (c.AttackRate != 0xFF)
        {
            return;
        }
        if (c.Action.Has(CombatAction.Melee))
        {
            // C: the skill and agility are signed chars; (200 − skill − agility) >> 1 is an arithmetic shift.
            var t = ((200 - c.ReadiedSkill - c.Agility) >> 1) + (c.WeaponSpeed + 15) * 2;
            if (c.Action.Has(CombatAction.BerserkAttack))
            {
                t -= 30;
            }
            else if (c.Action.Has(CombatAction.ParryAttack))
            {
                t += 60;
            }
            else if (c.Action.Has(CombatAction.VulnerableAttack))
            {
                t += 120;
            }
            var e = c.MaximumStrength + c.MaximumEndurance;
            var load = c.Load;
            if (e / 2 < load && load <= e)
            {
                t += 30;
            }
            else if (e * 3 / 2 < load)
            {
                t += 100;
            }
            // C: 6000 / t as a signed 16-bit division stored in a byte (t is never 0 for game data;
            // a t of 0 would fault, as in the original).
            c.AttackRate = (byte)(6000 / t);
        }
        else
        {
            // C: agility >> 1 and skill >> 1 are char shifts (sar on the byte, then sign extension).
            var aim = -(c.Agility >> 1) - ((c.ReadiedSkill >> 1) - 150);
            c.AimDelay = (sbyte)Math.Max(0, aim);
            c.AttackRate = 100;
        }
    }

    /// <summary>One attack of <paramref name="a"/> on <paramref name="t"/>: to-hit, damage and the attacker's reaction.</summary>
    /// <param name="hasMissSequence">DS:C7D2[set]: the attacker's animation set has miss sequences.</param>
    /// <param name="noParrySequence">DS:C86B[slot] == 1: its animation slot has no parry sequence (then a parry plays the hit sequence).</param>
    // orig: cbt_attack R014+0xA592 (18BF:0102) draft, checked against asm; tests: Oracle:battle.json
    // DIVERGENCE: the sound/animation sequences (DS:A3E7 rows, lengths DS:C8A7/DS:C84A) and the animation info pointer are
    // chosen by the battle screen from the outcome; the draws and the result follow the original exactly.
    public static AttackOutcome Attack(Battle battle, int a, int t, bool hasMissSequence = true, bool noParrySequence = false)
    {
        var result = ToHit(battle, a, t, out var location);
        var reaction = -1;
        var damage = 0;
        if (result < HitResult.Hit)
        {
            if (result == HitResult.None)
            {
                if (!hasMissSequence)
                {
                    result = HitResult.Parried;
                }
            }
            else
            {
                reaction = 6;
            }
        }
        else
        {
            damage = HitDamage(battle, a, t, result, location);
            reaction = damage <= 1 ? 1 : 2;
        }
        if (battle.DragonFight != 0 && a == 5)
        {
            // C: the dragon's blows are counted (a byte: it wraps at 256); melee_frame breathes every DS:C21F-th.
            battle.DragonBlowCount++;
        }
        var missVariant = -1;
        if (result == HitResult.None)
        {
            // C: random_n(4) picks the miss sequence row (n + 2) * 5 of the attacker's set.
            missVariant = battle.RollScaled(4);
        }
        // C: r == 1 returns 1 even when the parry sequence is empty; with no parry sequence for
        // the slot (DS:C86B == 1) the hit sequence is played and 1 is still returned.
        _ = noParrySequence;
        return new AttackOutcome(result, location, damage, missVariant, reaction);
    }

    /// <summary>The to-hit roll; <paramref name="location"/> gets the hit location (0 vital, 1 limbs).</summary>
    // orig: to_hit_roll R014+0xA738 (18BF:02A8) draft, checked against asm; tests: Oracle:battle.json
    // DIVERGENCE (from the draft, not the game): the draft calls 0592 "missile_to_hit" and takes the inline branch for
    // melee; the asm takes 0592 when the attacker's action has the melee bit, so 0592 is the hand-to-hand chance and the
    // inline branch the missile chance. The draft also inverts the low-roll rescue: the asm rescues rolls of 5 or less.
    public static HitResult ToHit(Battle battle, int a, int t, out int location)
    {
        location = 0;
        var roll = battle.RollOneTo(100);
        if (battle.DragonFight != 0 && a == 5 && battle[5].Target == -1)
        {
            // C: the dragon without a victim (DS:9F1F, its record's +0x4A) misses; the roll is drawn first.
            return HitResult.None;
        }
        var ca = battle[a];
        // C: the asm sign-extends the rate byte (cbw): a stale rate of 255 reads as −1 and so does
        // not strike; the draft declares it unsigned.
        if ((sbyte)ca.AttackRate < roll)
        {
            return HitResult.None;
        }
        location = roll & 1;
        var action = ca.Action;
        if (action.Has(CombatAction.UseItem))
        {
            // C: a spell or throw returns before the target's record is read (asm 18F04): a cast at a spot (t = −1) rolls like
            // any attack.
            return HitResult.Hit;
        }
        var chance = action.Has(CombatAction.Melee)
            ? CloseCombatChance(battle, a, t, (byte)action)
            : MissileChance(battle, a, t);
        if (chance < roll)
        {
            // A roll of 5 or less still hits when the skill exceeds the weapon's minimum by twice the roll.
            if (roll <= 5 && ca.ReadiedSkill - ca.ReadiedMinimumSkill >= roll * 2)
            {
                return HitResult.Hit;
            }
            return HitResult.Parried;
        }
        return chance < roll + 10 ? HitResult.Glancing : HitResult.Hit;
    }

    /// <summary>The missile chance: the attacker's aiming value against the target's dodge and shield, fewer when allies of the shooter crowd the target.</summary>
    // orig: to_hit_roll R014+0xA738 R014+0xA7D8..0xAA1B (inline branch) draft, checked against asm
    private static int MissileChance(Battle battle, int a, int t)
    {
        var ca = battle[a];
        var ct = battle[t];
        int aim = ca.AimingValue;
        var (first, last) = a < Combatant.PartySlots ? (0, 5) : (5, 13);
        // C: the loop runs first..last inclusive: for the party that includes slot 5 (the first enemy).
        var alliesNearTarget = 0;
        for (var k = first; k <= last && k < Combatant.Count; k++)
        {
            if (battle.Reaches[t, k] && battle[k].IsAble)
            {
                alliesNearTarget++;
            }
        }
        // DIVERGENCE: for enemies the original loop also reads slot 13 (one past the table: DS:7900[t][13], which is
        // DS:7900[t + 1][0], and the record after the 13th combatant). The C# stops at 12.
        var crowding = alliesNearTarget == 0 ? 10 : Math.Max(1, (5 - alliesNearTarget) * 2);
        var shooters = AttackersOf(battle, a);
        var harried = shooters == 0 ? 10 : Math.Max(1, 10 - shooters * 3);
        var cloud = battle.Clouds.LineCrossesCloud(a, t) ? 2 : 10;
        // C: each step is a signed 16-bit multiply then a division truncating toward zero.
        var effective = cloud * (harried * (crowding * aim / 10) / 10) / 10;
        if (effective < 0)
        {
            effective = 0;
        }
        var guard = Dodge(ct) * 2 + ShieldValue(ct);
        if (guard < 0)
        {
            guard = 0;
        }
        return (effective - guard) * 2 / 3 + 50;
    }

    /// <summary>The target's dodge: its agility, less the more it carries.</summary>
    private static int Dodge(Combatant target)
    {
        // C: e = (End + Str maxima) * 5 >> 2; the thresholds are e >> 1, e and e * 3 >> 1.
        var e = (target.MaximumEndurance + target.MaximumStrength) * 5 >> 2;
        var load = target.Load;
        if (e >> 1 >= load)
        {
            return target.Agility;
        }
        if (e >= load)
        {
            return target.Agility * 2 / 3;
        }
        // C: agility / 3 is an 8-bit signed division (idiv cl).
        return (e * 3 >> 1) >= load ? (sbyte)(target.Agility / 3) : 0;
    }

    /// <summary>The target's shield against missiles: small q·2/7+1, medium q/2+4, large q·2/3+8, none −5.</summary>
    private static int ShieldValue(Combatant target)
    {
        int q = target.ShieldQuality;
        if ((target.WeaponClass & 0xF0) != 0x10)
        {
            return -5;
        }
        return target.ShieldType switch
        {
            0x5F => q * 2 / 7 + 1,
            0x60 => (q >> 1) + 4,
            0x61 => q * 2 / 3 + 8,
            _ => -5,
        };
    }

    /// <summary>How many able combatants fight <paramref name="who"/> hand to hand.</summary>
    private static int AttackersOf(Battle battle, int who)
    {
        var n = 0;
        foreach (var k in battle.Combatants)
        {
            if (k.Target == who && k.IsAble && k.Action.Has(CombatAction.Melee))
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>The hand-to-hand chance: fighting values, shield and stances, the numbers on each side, clouds.</summary>
    // orig: R014+0xAA22 (18BF:0592) draft ("missile_to_hit" in the draft), checked against asm; tests: Oracle:battle.json
    // DIVERGENCE (from the draft): against a berserk target the asm keeps the attacker's −skill/4 only when it is −10 or
    // lower and otherwise uses −10 (the draft writes the opposite clamp).
    public static int CloseCombatChance(Battle battle, int a, int t, byte stance)
    {
        var ca = battle[a];
        var ct = battle[t];
        int guard = ct.FightingValue;
        int shield;
        if (battle.Tables.SkillCategory(ca.DefaultWeapon) == 2 || (ct.WeaponClass & 0xF0) != 0x10)
        {
            // C: the test reads the attacker's hand weapon category (2 = flail: flails ignore shields).
            shield = -5;
        }
        else
        {
            int q = ct.ShieldQuality;
            shield = ct.ShieldType switch
            {
                0x5F => q * 2 / 7,
                0x60 => q * 2 / 5,
                0x61 => q / 2,
                _ => -5,
            };
        }
        guard += shield;
        if (guard < 0)
        {
            guard = 0;
        }
        var action = (CombatAction)stance;
        var modifier = 0;
        // C: skill >> 2 is a char shift (sar al, 2) before sign extension.
        if (action.Has(CombatAction.VulnerableAttack))
        {
            modifier = -5;
        }
        else if (action.Has(CombatAction.ParryAttack))
        {
            modifier = -2 - (ca.ReadiedSkill >> 2);
        }
        else if (action.Has(CombatAction.BerserkAttack))
        {
            modifier = Math.Max(ca.ReadiedSkill >> 2, 10);
        }
        // C: only the low byte of the target's action is tested.
        var targetStance = (CombatAction)(byte)ct.Action;
        if (targetStance.Has(CombatAction.ParryAttack))
        {
            modifier -= (ct.ReadiedSkill >> 2) + 5;
        }
        else if (targetStance.Has(CombatAction.BerserkAttack))
        {
            // C: the attacker's skill / 4 is an 8-bit signed division (idiv cl); it replaces the modifier.
            modifier = Math.Min(-(sbyte)(ca.ReadiedSkill / 4), -10);
        }
        modifier += AttackersOf(battle, t) * 10 - AttackersOf(battle, a) * 10;
        if (battle.Clouds.InsideACloud(a))
        {
            modifier -= 15;
        }
        var chance = (ca.FightingValue - guard) * 2 / 3 + modifier + 50;
        if (action.Has(CombatAction.BerserkAttack) && chance < 10)
        {
            chance = 10;
        }
        return chance;
    }

    /// <summary>The damage of a hit at <paramref name="location"/>; queues it on the target and returns the endurance damage.</summary>
    // orig: hit_damage R014+0xAC08 (18BF:0778) draft, checked against asm; tests: Oracle:battle.json
    public static int HitDamage(Battle battle, int a, int t, HitResult result, int location)
    {
        var ca = battle[a];
        if (ca.Action.Has(CombatAction.UseItem))
        {
            return 0;
        }
        var ct = battle[t];
        // BUG: the whole charm byte but bit 7 counts as armour: its high nibble (bits 4-6) is the fire charm that Firewall
        // (+1 each), St.Polycarp, St.Charity and the other SetCharm saints set, so they give +16 to +112 armour that never
        // wears off (the "invulnerable after Polycarp and Firewall" bug); only the low nibble is the potions' and saints' armour.
        var armour = ct.ArmourValue[location] + (ct.Protection & 0x7F);
        // BUG: the weapon potions' bonus byte counts with any weapon the member strikes with (bows and fists too), where the
        // manual and the potions' own descriptions limit Deadly Blade, Strongedge and Great Power to some kinds of weapon.
        var bonus = (ca.WeaponBonus >> 4) + ca.WeaponDamage;
        var fullStrength = ca.FullStrength & 0x7F;
        int minimumStrength = ca.MinimumStrength;
        // C: the bonus byte is added whole (not just its low nibble) before masking.
        // BUG: the penetration wraps: (bonus + weapon) & 15, so Strongedge or Great Power on a weapon of high penetration
        // gives almost none ("after Great Power my bows only do 3-5 damage").
        var penetration = (ca.WeaponBonus + ca.WeaponClass) & 0xF;
        if (ca.Action.Has(CombatAction.VulnerableAttack))
        {
            penetration += battle.RollOneTo(4);
        }
        if (result == HitResult.Glancing)
        {
            penetration -= battle.RollOneTo(4);
        }
        if (penetration <= armour)
        {
            // C: equal halves toward zero; less keeps the sign of |bonus| >> 3.
            bonus = armour == penetration ? bonus / 2 : Math.Sign(bonus) * (Math.Abs(bonus) >> 3);
        }
        int strength = ca.MaximumStrength;
        if (strength > fullStrength)
        {
            bonus += (strength - fullStrength) / 5 + 1;
        }
        else if (strength < minimumStrength)
        {
            bonus += (strength - minimumStrength) / 5 - 1;
        }
        // C: the target's +0x24 / 2 is an 8-bit signed division.
        var armourQuality = (ca.Protection & 0x80) != 0 ? (sbyte)(ct.PiercingResistance / 2) : 0;
        armourQuality += ct.ArmourQuality[location];
        if (armourQuality > 99)
        {
            armourQuality = 99;
        }
        return QueueDamage(battle, (ca.StrikeQuality - armourQuality) / 10 + bonus, t, penetration, armour);
    }

    /// <summary>Rolls damage of level <paramref name="level"/> (0..40) and queues it on <paramref name="t"/>; returns the endurance damage.</summary>
    // orig: damage_queue R014+0xAD5A (18BF:08CA) draft, checked against draft; tests: Oracle:battle.json
    public static int QueueDamage(Battle battle, int level, int t, int penetration, int armour)
    {
        level = Math.Clamp(level, 0, 40);
        var roll = battle.RollOneTo(6) + battle.RollOneTo(6);
        int damage;
        if (level <= 7)
        {
            damage = roll / battle.Tables.LowDamageDivisor(level) + battle.Tables.LowDamageAddition(level);
        }
        else if (level <= 20)
        {
            damage = (roll >> 1) + level - 6;
        }
        else
        {
            damage = level + roll - 10;
        }
        var target = battle[t];
        // C: the queue is a signed byte: the addition wraps at 8 bits.
        target.QueuedEndurance = (sbyte)(target.QueuedEndurance + damage);
        int factor;
        if (armour < penetration)
        {
            factor = battle.RollOneTo(6) + 3;
        }
        else if (armour == penetration)
        {
            factor = battle.RollOneTo(4) + 1;
        }
        else
        {
            factor = battle.RollScaled(3);
        }
        target.QueuedStrength = (sbyte)(target.QueuedStrength + factor * damage / 10);
        return damage;
    }

    /// <summary>Applies the queued damage: endurance 0 = unconscious, strength 0 = dead (in a non-lethal fight both stop at 1).</summary>
    // orig: damage_apply R014+0xAE1E (18BF:098E) draft (byte-matched per the draft notes); checked against asm; tests: Oracle:battle.json
    // DIVERGENCE: the party panel redraw (party_panel_rows(0x100)) is left to the battle screen.
    public static void ApplyDamage(Battle battle, int i)
    {
        var c = battle[i];
        if (c.Status != CharacterStatus.Able)
        {
            return;
        }
        if (c.QueuedEndurance > 42)
        {
            c.QueuedEndurance = 42;
        }
        var d = c.QueuedEndurance;
        // C: endurance and the shown damage are signed bytes (sub/add byte ptr).
        c.Endurance = (sbyte)(c.Endurance - d);
        c.DamageShown = (sbyte)(c.DamageShown + d);
        c.QueuedEndurance = 0;
        if (c.Endurance <= 0)
        {
            c.Endurance = 0;
            c.Status = CharacterStatus.Unconscious;
        }
        c.Strength = (sbyte)(c.Strength - c.QueuedStrength);
        c.QueuedStrength = 0;
        if (c.Strength <= 0)
        {
            c.Strength = 0;
            c.Status = CharacterStatus.Dead;
        }
        if (!battle.RealCombat)
        {
            if (c.Endurance == 0)
            {
                c.Endurance = 1;
                c.Status = CharacterStatus.Able;
            }
            if (c.Strength == 0)
            {
                c.Strength = 1;
                c.Status = CharacterStatus.Able;
            }
        }
    }

    /// <summary>Readies weapon type <paramref name="weapon"/> (−1 with no missile flag: bare hands).</summary>
    /// <param name="missileFlag">0 for the hand weapon, 0x80 for the missile weapon (kept in bit 7 of +0x57).</param>
    /// <param name="animationCode">Receives the two letters of the weapon's animations (anim_file[slot]).</param>
    // orig: weapon_ready R014+0xAED0 (18BF:0A40) draft, checked against draft; tests: Oracle:battle.json
    public static void ReadyWeapon(Battle battle, int i, int weapon, int missileFlag, Action<string>? animationCode = null)
    {
        var tables = battle.Tables;
        var c = battle[i];
        if (weapon == -1 && missileFlag == 0)
        {
            weapon = CombatTables.BareHands;
        }
        // C: a weapon of −1 with the missile flag indexes the tables at −1 (the byte before each table).
        c.AttackRate = 0xFF;
        c.StrikeQuality = missileFlag == 0 ? c.WeaponQuality : c.MissileQuality;
        c.WeaponSpeed = tables.WeaponSpeed(weapon);
        c.WeaponClass = tables.WeaponClass(weapon);
        c.WeaponDamage = tables.WeaponDamage(weapon);
        c.ReadiedMinimumSkill = tables.MinimumSkill(weapon);
        var minimumStrength = tables.MinimumStrength(weapon);
        c.MinimumStrength = minimumStrength;
        var fullStrength = (byte)tables.FullStrength(weapon);
        c.FullStrength = fullStrength;
        c.WeaponRange = tables.WeaponRange(weapon);
        c.ArmourValue[0] = tables.ArmourValue(Math.Max((int)c.ArmourType[0], 0));
        c.ArmourValue[1] = tables.ArmourValue(Math.Max((int)c.ArmourType[1], 0));
        int skill = c.ReadiedSkill = c.Skills[tables.SkillCategory(weapon)];
        c.FullStrength = (byte)(fullStrength | missileFlag);
        animationCode?.Invoke(battle.AnimationCode(weapon));
        int strength = c.MaximumStrength;
        int tooWeakFighting = 0, tooWeakAiming = 0, unskilledFighting = 0, unskilledAiming = 0;
        var d = minimumStrength - strength;
        if (d > 0)
        {
            tooWeakFighting = d * 3;
            tooWeakAiming = d;
        }
        d = tables.MinimumSkill(weapon) - skill;
        if (d > 0)
        {
            unskilledFighting = d * 2;
            unskilledAiming = d * 2;
        }
        d = strength - tables.FullStrength(weapon);
        var strong = d < 1 ? 0 : d * 2;
        var aiming = skill - tooWeakAiming - unskilledAiming;
        var fighting = strong - tooWeakFighting - unskilledFighting + skill;
        c.FightingValue = (byte)Math.Clamp(fighting, 0, 0xFF);
        c.AimingValue = (byte)Math.Clamp(aiming, 0, 0xFF);
    }

    /// <summary>True when the readied weapon needs no ammunition or the combatant carries ammunition of quality 25.</summary>
    // orig: can_fire R014+0xB092 (18BF:0C02) draft (byte-matched per the draft notes)
    public static bool CanFire(Battle battle, int i)
    {
        var c = battle[i];
        var ammunition = battle.Tables.AmmunitionType(c.ReadiedWeapon);
        return ammunition == 0 || Carries(battle, i, (byte)ammunition, 25);
    }

    /// <summary>Uses one ammunition (or the thrown weapon itself); when it runs out the combatant goes back to its hand weapon.</summary>
    /// <param name="ranOut">Called when the ammunition ran out (the battle screen updates the panel and the AI).</param>
    // orig: use_ammo R014+0xB0D8 (18BF:0C48) draft
    // C: the AI reset for enemies (R014 1DB7:05C8), DS:A3D8 = 2 and the panel redraw are the caller's ranOut, the action state
    // 0x0D (DS:CA64) the caller's after the call.
    public static int UseAmmunition(Battle battle, int i, Action<int>? ranOut = null)
    {
        var c = battle[i];
        c.AttackRate = 0xFF;
        var weapon = c.ReadiedWeapon;
        var ammunition = battle.Tables.AmmunitionType(weapon);
        var left = ammunition == 0
            ? ConsumeItem(battle, i, (byte)weapon, (byte)c.MissileQuality)
            : ConsumeItem(battle, i, (byte)ammunition, 25);
        if (left == -1)
        {
            if (battle.Tables.AmmunitionType(c.ReadiedWeapon) == 0)
            {
                c.ReadiedWeapon = -1;
            }
            ranOut?.Invoke(i);
            c.EnemyClass = 0;
            c.Target = -1;
            ReadyWeapon(battle, i, c.DefaultWeapon, 0);
        }
        return left;
    }

    /// <summary>True when combatant i carries an item of type <paramref name="type"/> and quality <paramref name="quality"/>.</summary>
    // orig: R014+0xB18E (18BF:0CFE) draft
    public static bool Carries(Battle battle, int i, byte type, byte quality) =>
        battle[i].Items.Any(s => (byte)s.Type == type && (byte)s.Quality == quality);

    /// <summary>Takes one item of a type and quality; returns how many are left in that stack, or −1 when it is gone or was not there.</summary>
    // orig: consume_item R014+0xB244 (18BF:0DB4) draft
    public static int ConsumeItem(Battle battle, int i, byte type, byte quality)
    {
        var items = battle[i].Items;
        var k = items.FindIndex(s => (byte)s.Type == type && (byte)s.Quality == quality);
        if (k < 0)
        {
            return -1;
        }
        return ConsumeSlot(battle, i, k);
    }

    /// <summary>Takes one item from pack slot <paramref name="k"/>; −1 when the slot is used up (and removed).</summary>
    // orig: consume_slot R014+0xB434 (18BF:0FA4) draft
    public static int ConsumeSlot(Battle battle, int i, int k)
    {
        var items = battle[i].Items;
        var stack = items[k];
        // C: the count is an unsigned byte decremented in place (cnt-- - 1): a stack of 0 wraps to 255 and is removed.
        // C: the decrement is stored before the removal, so a used-up last stack stays behind as a stale entry of count
        // cnt − 1 (R014+0xB434: dec byte es:[bx+0x42] before the shift).
        var left = stack.Count - 1;
        items[k] = stack with { Count = (byte)left };
        if (left > 0)
        {
            return left;
        }
        items.RemoveInBattle(k);
        return -1;
    }
}
