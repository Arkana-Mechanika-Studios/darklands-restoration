using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;

namespace Darklands.Core.Rules;

/// <summary>
/// Reading and changing one party member: skills, attributes and their maxima, encumbrance,
/// temporary boosts, injuries. <c>who</c> is a member slot 0..4, −1 the leader, −2 (where the
/// original allows it) every present member. Attribute and skill numbers are the game's
/// (<see cref="Attribute"/>, <see cref="Skill"/>).
/// </summary>
public static class MemberRules
{
    /// <summary>The skill names the change reports use.</summary>
    public static readonly string[] SkillNames =
    [
        "Edged Wpns", "Impact Wpns", "Flail Wpns", "Polearm Wpns", "Thrown Wpns", "Bow Wpns", "Missile Dvc",
        "Alchemy", "Religion", "Virtue", "Speak Common", "Speak Latin", "Read & Write", "Healing", "Artifice",
        "Stealth", "Streetwise", "Riding", "Woodwise",
    ];

    // ---- skills ---------------------------------------------------------------------

    /// <summary><c>member_skill</c>.</summary>
    // orig: member_skill R029+0x01A0 (0E76:01A0) matched; tests: MemberRulesTests
    public static int Skill(GameState s, int who, int skill) => s.Party.Slot(who).Skills[skill];

    /// <summary>Sets a skill, kept in 0..99 (<c>set_skill</c>).</summary>
    // orig: set_skill R029+0x02B8 (0E76:02B8) matched; clamp root 0410:001A (0410:001A) asm; tests: MemberRulesTests
    // C: no presence test; root 0410:001A(v, 0, 99) clamps.
    // DIVERGENCE: who -2 or past the slots: the original writes outside the records; here the write is dropped (Party.Slot gives a scratch member).
    public static void SetSkill(GameState s, int who, int skill, int value) =>
        s.Party.Slot(who).Skills[skill] = Math.Clamp(value, 0, 99);

    /// <summary>
    /// Adds to a skill, kept in 0..99, and reports "&lt;skill&gt; changed from X to Y" when
    /// <see cref="GameState.ReportSkillChanges"/> (<c>add_skill</c>; −2: every present member).
    /// </summary>
    // orig: add_skill R029+0x02EA (0E76:02EA) matched; tests: MemberRulesTests; Oracle:rules.json (through practice_skill)
    // C: the report text is also copied into msg_buf (DS:9085) and shown with thunk 09C0:1E91(member, 15 up / 12 down) only while DS:26BC is set; here Messages.Report.
    public static void AddSkill(GameState s, int who, int skill, int change)
    {
        if (change == 0)
        {
            return;
        }
        who = s.Party.Resolve(who);
        if (who == Who.Everybody)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is not null)
                {
                    ChangeSkill(s, i, skill, change);
                }
            }
        }
        else if (s.Party.PresentMember(who) is not null)
        {
            ChangeSkill(s, who, skill, change);
        }
    }

    private static void ChangeSkill(GameState s, int member, int skill, int change)
    {
        var m = s.Party.Members[member];
        var before = m.Skills[skill];
        m.Skills[skill] = Math.Clamp(before + change, 0, 99);
        if (s.ReportSkillChanges)
        {
            s.Messages.Report(member, $"{SkillNames[skill]} changed from {before} to {m.Skills[skill]}", change > 0);
        }
    }

    /// <summary>
    /// The best of the member's seven weapon skills (<c>member_best_skill</c>). Original bug:
    /// <see cref="PartyQueryResult.Skill"/> is set on every step, so it always ends as 6.
    /// </summary>
    // orig: member_best_skill R029+0x01C0 (0E76:01C0) matched; tests: MemberRulesTests; FidelityFixTests; Oracle:rules.json
    // BUG: `best_skill = i` sits outside the if (missing braces): it ends as 6 (FixBestWeaponSkillNumber).
    public static int BestWeaponSkill(GameState s, int who)
    {
        var m = s.Party.Slot(who);
        var last = s.Party.Last;
        last.Value = 0;
        for (var k = 0; k < 7; k++)
        {
            if (m.Skills[k] > last.Value)
            {
                last.Value = (byte)m.Skills[k];
                last.Skill = k;
            }
            else if (!s.Behaviour.FixBestWeaponSkillNumber)
            {
                last.Skill = k;
            }
        }
        // DORMANT: `if (best_skill == -1) { best_skill = 0; return 0; }` - best_skill is an unsigned
        // char, so the test never holds in the original. With the fix (no skill above 0) it is
        // what keeps the reported skill from being stale.
        if (s.Behaviour.FixBestWeaponSkillNumber && last.Value == 0)
        {
            last.Skill = 0;
            return 0;
        }
        return last.Value;
    }

    // ---- attributes -----------------------------------------------------------------

    /// <summary>The current attribute (<c>member_attr</c>).</summary>
    // orig: member_attr R029+0x05EE (0E76:05EE) matched
    public static int Attribute(GameState s, int who, int attribute) => s.Party.Slot(who).Attributes[attribute];

    /// <summary>The attribute's maximum (<c>member_attr_max</c>).</summary>
    // orig: member_attr_max R029+0x0B64 (0E76:0B64) matched
    public static int AttributeMaximum(GameState s, int who, int attribute) => s.Party.Slot(who).AttributeMaximums[attribute];

    /// <summary>
    /// The highest an attribute may be: its maximum plus the largest temporary boost.
    /// </summary>
    // orig: event_48_max R029+0x4E0C (0E76:4E0C) matched
    // C: event_48_max(i, k) + p[7] (the maximum byte) as an int, compared with the signed attribute byte.
    public static int AttributeCap(GameState s, int member, int attribute) =>
        s.Events.AttributeBoost(member, attribute) + s.Party.Slot(member).AttributeMaximums[attribute];

    /// <summary>Sets an attribute, kept in 0..99 and under its cap (<c>set_attr</c>; −2: every present member).</summary>
    // orig: set_attr R029+0x0988 (0E76:0988) matched; tests: MemberRulesTests
    // C: one member is written whatever its status; -2 only the present ones.
    public static void SetAttribute(GameState s, int who, int attribute, int value)
    {
        foreach (var i in Targets(s, who, presentOnly: false))
        {
            var m = s.Party.Members[i];
            m.Attributes[attribute] = Math.Min(Math.Clamp(value, 0, 99), AttributeCap(s, i, attribute));
        }
    }

    /// <summary>Adds to an attribute, kept in 1..99 and under its cap (<c>add_attr</c>; −2: every present member).</summary>
    // orig: add_attr R029+0x0A72 (0E76:0A72) matched; tests: MemberRulesTests; Oracle:rules.json add_attr
    public static void AddAttribute(GameState s, int who, int attribute, int change)
    {
        foreach (var i in Targets(s, who, presentOnly: true))
        {
            var m = s.Party.Members[i];
            var v = Math.Clamp(m.Attributes[attribute] + change, 1, 99);
            m.Attributes[attribute] = Math.Min(v, AttributeCap(s, i, attribute));
        }
    }

    /// <summary>
    /// Sets an attribute maximum (<c>set_attr_max</c>). Original bug: the 0..99 limit is
    /// computed and then ignored, so the value is stored as given.
    /// </summary>
    // orig: set_attr_max R029+0x0B84 (0E76:0B84) matched; tests: MemberRulesTests
    // BUG: the clamp's result is dropped and v is stored as a char (FixSetAttributeMaximumLimit).
    public static void SetAttributeMaximum(GameState s, int who, int attribute, int value) =>
        s.Party.Slot(who).AttributeMaximums[attribute] = s.Behaviour.FixSetAttributeMaximumLimit ? Math.Clamp(value, 0, 99) : (sbyte)value;

    /// <summary>Adds to an attribute maximum, kept in 1..99 (<c>add_attr_max</c>; −2: every present member).</summary>
    // orig: add_attr_max R029+0x0BB6 (0E76:0BB6) matched
    public static void AddAttributeMaximum(GameState s, int who, int attribute, int change)
    {
        foreach (var i in Targets(s, who, presentOnly: true))
        {
            var max = s.Party.Members[i].AttributeMaximums;
            max[attribute] = Math.Clamp(max[attribute] + change, 1, 99);
        }
    }

    /// <summary>
    /// Restores <paramref name="percent"/> % of what an attribute lacks of its maximum, kept in
    /// 1..99 (<c>heal_attr_pct</c>; −2: every present member).
    /// </summary>
    // orig: heal_attr_pct R029+0x08F2 (0E76:08F2) matched
    // C: (max - a) * pct / 100 is a signed 16-bit product and division (truncates toward zero); one member is healed whatever its status.
    public static void HealAttribute(GameState s, int who, int attribute, int percent)
    {
        foreach (var i in Targets(s, who, presentOnly: false))
        {
            var m = s.Party.Members[i];
            var a = m.Attributes[attribute];
            m.Attributes[attribute] = Math.Clamp(a + (m.AttributeMaximums[attribute] - a) * percent / 100, 1, 99);
        }
    }

    /// <summary>The members a <c>who</c> argument covers (one member: only if <paramref name="presentOnly"/> allows).</summary>
    // orig: set_attr R029+0x0988 (0E76:0988) matched; add_attr R029+0x0A72 (0E76:0A72) matched
    // C: -2 loops over the five slots with party[i][0] != 0; one member: -1 resolves to the leader.
    private static IEnumerable<int> Targets(GameState s, int who, bool presentOnly)
    {
        who = s.Party.Resolve(who);
        if (who == Who.Everybody)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is not null)
                {
                    yield return i;
                }
            }
        }
        else if (who >= 0 && who < s.Party.Members.Count && (!presentOnly || s.Party.PresentMember(who) is not null))
        {
            yield return who;
        }
    }

    // ---- encumbrance and agility ----------------------------------------------------

    /// <summary>The weight the member carries in equipment (<c>member_fatigue</c>, record +0x35).</summary>
    // orig: member_fatigue R029+0x00C6 (0E76:00C6) matched
    public static int Encumbrance(GameState s, int who) => s.Party.Slot(who).Encumbrance;

    /// <summary>Sets the encumbrance, not below 0 (stored in the record's signed byte).</summary>
    // orig: set_member_fatigue R029+0x00E4 (0E76:00E4) matched; tests: FidelityFixTests
    public static void SetEncumbrance(GameState s, int who, int value) => s.Party.Slot(who).Encumbrance = (sbyte)Math.Max(value, 0);

    /// <summary>Adds to the encumbrance, not below 0.</summary>
    // orig: R029+0x010E (0E76:010E) matched; tests: FidelityFixTests
    public static void AddEncumbrance(GameState s, int who, int change)
    {
        var m = s.Party.Slot(who);
        // C: `(DS:9C9E[who][0] += d) < 0` adds in the signed byte: past 127 it wraps negative and becomes 0.
        var v = (sbyte)(m.Encumbrance + (sbyte)change);
        m.Encumbrance = v < 0 ? 0 : v;
    }

    /// <summary>
    /// How burdened the member is, from Endurance + Strength: 0 up to half of it, 1 up to all
    /// of it, 2 up to one and a half times it, else 3.
    /// </summary>
    // orig: R029+0x013A (0E76:013A) draft; tests: FidelityFixTests
    // C: checked against the asm (013a differs only in its frame slot): Strength + Endurance and max * 3 / 2 are unsigned.
    public static int EncumbranceBand(GameState s, int who)
    {
        var m = s.Party.Slot(who);
        // C: `unsigned max` = Strength + Endurance, compared with the signed byte +0x35 promoted to
        // unsigned: a negative load (equipment weight over 127) is huge and gives band 3.
        var limit = (ushort)(m[Characters.Attribute.Strength] + m[Characters.Attribute.Endurance]);
        var load = (ushort)(sbyte)m.Encumbrance;
        return limit / 2 >= load ? 0 : load <= limit ? 1 : load <= (ushort)(limit * 3) / 2 ? 2 : 3;
    }

    /// <summary>Agility as burden allows: all of it, 2/3, 1/3, or 1 (<c>member_eff_attr2</c>).</summary>
    // orig: member_eff_attr2 R029+0x06DE (0E76:06DE) matched; tests: PracticeAndItemsTests
    // DIVERGENCE: the original does not resolve -1 for the Agility byte (DS:9CB4[-1] reads the bytes before the records; only the band resolves it); callers pass a slot (CardContext resolves -1 first).
    // DORMANT: the switch has no default; R029+0x013A only returns 0..3.
    public static int EffectiveAgility(GameState s, int member)
    {
        var agility = s.Party.Slot(member)[Characters.Attribute.Agility];
        return EncumbranceBand(s, member) switch
        {
            0 => agility,
            1 => agility * 2 / 3,
            2 => agility / 3,
            _ => 1,
        };
    }

    // ---- temporary boosts -----------------------------------------------------------

    /// <summary>
    /// Raises an attribute for about <paramref name="hours"/> (6 + rnd 3 + hours − 5 hours),
    /// recorded as an <see cref="EventKinds.AttributeBoost"/> event that undoes it
    /// (<c>raise_attr_temp</c>). A member already above the maximum gets only the rest of the
    /// boost. Original bugs (−2): a member whose excess is larger than the boost ends the whole
    /// call, and the reduced boost carries over to the next members.
    /// </summary>
    // orig: raise_attr_temp R029+0x4C04 (0E76:4C04) matched; tests: MemberRulesTests
    // C: the event (one rand_mod(3) draw) is written before add_attr.
    // BUG: (-2) a member above the boost ends the call and the reduced boost carries over (FixWholePartyBoosts).
    public static void RaiseAttributeTemporarily(GameState s, int who, int attribute, int points, int hours)
    {
        who = s.Party.Resolve(who);
        if (who == Who.Everybody && s.Behaviour.FixWholePartyBoosts)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is not null)
                {
                    RaiseAttributeTemporarily(s, i, attribute, points, hours);
                }
            }
            return;
        }
        if (who == Who.Everybody)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is null)
                {
                    continue;
                }
                var excess = Attribute(s, i, attribute) - AttributeMaximum(s, i, attribute);
                if (excess > 0 && points < excess)
                {
                    return;
                }
                if (excess > 0)
                {
                    points -= excess;
                }
                RecordBoost(s, i, EventKinds.AttributeBoost, attribute, points, hours);
                AddAttribute(s, i, attribute, points);
            }
            return;
        }
        // BUG: a temporary attribute boost that is not wholly applied lowers the attribute for good: the boost is cut
        // only by the excess over the maximum and then recorded in full, but AddAttribute keeps the attribute within
        // 1..99 and under the maximum plus the largest single boost (R029+0x4E0C); the expiry takes back the recorded
        // amount, so a boost cut short (an attribute near 99, or a second boost overlapping a larger one, in either
        // order) leaves the attribute lower than before. The whole-party path above does the same.
        var gap = Attribute(s, who, attribute) - AttributeMaximum(s, who, attribute);
        if (gap > 0 && points < gap)
        {
            return;
        }
        if (gap > 0)
        {
            points -= gap;
        }
        RecordBoost(s, who, EventKinds.AttributeBoost, attribute, points, hours);
        AddAttribute(s, who, attribute, points);
    }

    /// <summary>
    /// Raises a skill for about <paramref name="hours"/>, at most to 99, recorded as a
    /// <see cref="EventKinds.SkillBoost"/> event (the reference's misnamed <c>lower_skill_temp</c>).
    /// Reports only with the "saintly changes" option. Original bug (−2): for each present
    /// member it adds the boost to <em>everybody</em> and records an event owned by −2, so each
    /// member gains the boost once per present member; the 99 limit is taken from an undefined
    /// record (here: skill 0) and the capped boost carries over.
    /// </summary>
    // orig: lower_skill_temp R029+0x4B08 (0E76:4B08) draft; tests: MemberRulesTests
    // C: checked against the asm (4b08 differs in register/slot use only): add_skill runs before event_new and its rand_mod(3).
    // BUG: (-2) member_skill(-2) and add_skill(-2) for each present member (FixWholePartyBoosts).
    // DIVERGENCE: member -2's skill is a byte before the records in the original (undefined); here 0.
    public static void RaiseSkillTemporarily(GameState s, int who, int skill, int points, int hours)
    {
        s.ReportSkillChanges = s.ShowSaintlyChanges;
        who = s.Party.Resolve(who);
        if (who == Who.Everybody && s.Behaviour.FixWholePartyBoosts)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is not null)
                {
                    RaiseSkillTemporarily(s, i, skill, points, hours);
                }
            }
        }
        else if (who == Who.Everybody)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is null)
                {
                    continue;
                }
                points = Math.Min(points, 99 - UndefinedRecordSkill);
                AddSkill(s, Who.Everybody, skill, points);
                RecordBoost(s, Who.Everybody, EventKinds.SkillBoost, skill, points, hours);
            }
        }
        else
        {
            points = Math.Min(points, 99 - Skill(s, who, skill));
            AddSkill(s, who, skill, points);
            RecordBoost(s, who, EventKinds.SkillBoost, skill, points, hours);
        }
        s.ReportSkillChanges = true;
    }

    /// <summary>What the original reads as "member −2's skill" (memory before the party records).</summary>
    private const int UndefinedRecordSkill = 0;

    /// <summary>
    /// Records a potion's weapon bonus as a <see cref="EventKinds.PotionBonus"/> event (the
    /// original never undoes it). For −2 one event per present member, all owned by −2.
    /// </summary>
    // orig: R029+0x4D24 (0E76:4D24) matched
    // C: -2 writes one event per present member, each owned by -2 (who is not replaced in the loop).
    public static void RecordPotionBonus(GameState s, int who, int type, int value, int hours)
    {
        who = s.Party.Resolve(who);
        var count = who == Who.Everybody ? s.Party.Present.Count() : 1;
        for (var k = 0; k < count; k++)
        {
            RecordBoost(s, who, EventKinds.PotionBonus, type, value, hours);
        }
    }

    // orig: event_new R029+0x2C4E (0E76:2C4E) draft
    // C: event_new(who, cur_loc, 0x63, kind, k, d, 6, rand_mod(3) + h - 5, 1, 0).
    private static void RecordBoost(GameState s, int owner, int kind, int type, int points, int hours) =>
        s.Events.Create(owner, s.CurrentLocation, 0x63, kind, type, points, (int)ExpiryHandler.UndoTemporaryBoost,
            s.Random.Below(3) + hours - 5, (int)EventFlags.CallExpiryHandler, 0);

    /// <summary>
    /// Raises the weapon skills the equipped weapon and missile weapon are made for by
    /// <paramref name="points"/> for a day (a saint's help).
    /// </summary>
    // orig: equip_bonuses R029+0x0792 (0E76:0792) matched
    // C: weapon flag bits 1, 2, 4, 8 -> skills 0, 1, 3, 2; missile weapon 0x10, 0x20, 0x8000000 -> 4, 5, 6; each a 24-hour lower_skill_temp, in that order.
    public static void BoostEquippedWeaponSkills(GameState s, int who, int points)
    {
        if (Inventory.Equipped(s, who, EquipmentSlot.Weapon) is var weapon and not -1)
        {
            var flags = s.Item(weapon).Flags;
            int[] skillOfBit = [0, 1, 3, 2];
            for (var bit = 0; bit < 4; bit++)
            {
                if ((flags & (1u << bit)) != 0)
                {
                    RaiseSkillTemporarily(s, who, skillOfBit[bit], points, 24);
                }
            }
        }
        if (Inventory.Equipped(s, who, EquipmentSlot.MissileWeapon) is var missile and not -1)
        {
            var flags = s.Item(missile).Flags;
            if ((flags & 0x10) != 0)
            {
                RaiseSkillTemporarily(s, who, 4, points, 24);
            }
            if ((flags & 0x20) != 0)
            {
                RaiseSkillTemporarily(s, who, 5, points, 24);
            }
            if ((flags & 0x0800_0000) != 0)
            {
                RaiseSkillTemporarily(s, who, 6, points, 24);
            }
        }
    }

    // ---- injuries -------------------------------------------------------------------

    /// <summary>
    /// Wounds a member (<c>injure</c>; −2: every present member). Strength loses
    /// rnd(mult × Str / 40 + 1) − 1 points, at least <paramref name="minStrength"/>, at most
    /// its maximum; Endurance loses rnd(mult × End / 20 + 1) − 1, at least the Strength loss
    /// and <paramref name="minEndurance"/>, at most its maximum. Neither falls below 1.
    /// </summary>
    // orig: injure_impl R039+0x026A (1462:026A) matched; tests: MemberRulesTests
    // C: -2 loops i < party_size over status != 0; one member is not resolved here (member_attr and add_attr resolve -1).
    public static void Injure(GameState s, int who, int minStrength, int minEndurance, int multiplier)
    {
        if (who == Who.Everybody)
        {
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.PresentMember(i) is not null)
                {
                    InjureOne(s, i, minStrength, minEndurance, multiplier);
                }
            }
        }
        else
        {
            InjureOne(s, s.Party.Resolve(who), minStrength, minEndurance, multiplier);
        }
    }

    // orig: injure_impl R039+0x026A (1462:026A) matched
    // C: the current value (not the maximum) scales the loss: rand_mod(mult * cur / 40 + 1) - 1, imax with a, imin with the maximum; Endurance after the Strength change, imax with d then b.
    private static void InjureOne(GameState s, int who, int minStrength, int minEndurance, int multiplier)
    {
        const int strength = (int)Characters.Attribute.Strength;
        const int endurance = (int)Characters.Attribute.Endurance;
        var lost = s.Random.Below(multiplier * Attribute(s, who, strength) / 40 + 1) - 1;
        lost = Math.Min(Math.Max(lost, minStrength), AttributeMaximum(s, who, strength));
        AddAttribute(s, who, strength, -lost);
        var tired = s.Random.Below(multiplier * Attribute(s, who, endurance) / 20 + 1) - 1;
        tired = Math.Min(Math.Max(Math.Max(tired, lost), minEndurance), AttributeMaximum(s, who, endurance));
        AddAttribute(s, who, endurance, -tired);
    }
}
