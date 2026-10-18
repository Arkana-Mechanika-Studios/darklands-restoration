using Darklands.Core.Characters;
using Darklands.Core.Items;

namespace Darklands.Core.Rules;

/// <summary>
/// Questions about the whole party: lowest, highest and average skills and attributes, who
/// is able, the lore level. Most queries also leave what they found in
/// <see cref="Party.Last"/> (as bytes), because card scripts read it afterwards. "Able" is
/// status 1; "present" is any status but empty.
/// </summary>
public static class PartyQueries
{
    private static IEnumerable<(int Index, Character Member)> Able(GameState s) =>
        s.Party.Members.Select((m, i) => (i, m)).Where(x => x.m.Status == CharacterStatus.Able);

    private static IEnumerable<(int Index, Character Member)> Present(GameState s) =>
        s.Party.Members.Select((m, i) => (i, m)).Where(x => x.m.IsPresent);

    // ---- skills ---------------------------------------------------------------------

    /// <summary>The lowest skill among able members (<c>party_min_skill</c>; 199 when nobody is able).</summary>
    // orig: party_min_skill R029+0x1396 (0E76:1396) matched; tests: Oracle:rules.json party_min_skill
    public static int MinSkill(GameState s, int skill) => Lowest(s, Able(s), m => m.Skills[skill]);

    /// <summary>The lowest of two skills among able members (<c>party_min_skill2</c>).</summary>
    // orig: party_min_skill2 R029+0x13DE (0E76:13DE) matched; tests: Oracle:rules.json party_min_skill2
    public static int MinSkill(GameState s, int a, int b)
    {
        var last = s.Party.Last;
        last.Value = 199;
        foreach (var (i, m) in Able(s))
        {
            foreach (var k in new[] { a, b })
            {
                if (last.Value > m.Skills[k])
                {
                    last.Value = (byte)m.Skills[k];
                    last.Member = i;
                }
            }
        }
        return last.Value;
    }

    /// <summary>The lowest sum of two skills among able members (<c>party_min_skill_sum</c>).</summary>
    // orig: party_min_skill_sum R029+0x1446 (0E76:1446) draft; tests: Oracle:rules.json party_min_skill_sum
    public static int MinSkillSum(GameState s, int a, int b) => Lowest(s, Able(s), m => m.Skills[b] + m.Skills[a]);

    /// <summary>The highest skill among able members (<c>party_max_skill</c>).</summary>
    // orig: party_max_skill R029+0x14A4 (0E76:14A4) matched; tests: Oracle:rules.json party_max_skill; FidelityFixTests
    public static int MaxSkill(GameState s, int skill) => Highest(s, Able(s), m => m.Skills[skill]);

    /// <summary>The highest skill among able women.</summary>
    // orig: R029+0x14EC (0E76:14EC) matched; tests: Oracle:rules.json party_max_skill_women
    public static int MaxSkillOfWomen(GameState s, int skill) => Highest(s, Able(s).Where(x => x.Member.IsFemale), m => m.Skills[skill]);

    /// <summary>The highest of two skills among able members (<c>party_max_skill2</c>).</summary>
    // orig: party_max_skill2 R029+0x153A (0E76:153A) matched; tests: Oracle:rules.json party_max_skill2
    public static int MaxSkill(GameState s, int a, int b)
    {
        var last = s.Party.Last;
        last.Value = 0;
        foreach (var (i, m) in Able(s))
        {
            foreach (var k in new[] { a, b })
            {
                if (last.Value < m.Skills[k])
                {
                    last.Value = (byte)m.Skills[k];
                    last.Member = i;
                }
            }
        }
        return last.Value;
    }

    /// <summary>The highest sum of two skills among able members (<c>party_max_skill_sum</c>).</summary>
    // orig: party_max_skill_sum R029+0x15A2 (0E76:15A2) matched; tests: Oracle:rules.json party_max_skill_sum
    public static int MaxSkillSum(GameState s, int a, int b) => Highest(s, Able(s), m => m.Skills[a] + m.Skills[b]);

    /// <summary>The average skill of the able members (<c>party_avg_skill</c>).</summary>
    // orig: party_avg_skill R029+0x1600 (0E76:1600) matched; tests: Oracle:rules.json party_avg_skill
    // C: `return best_value = sum / n;` returns the unsigned char just stored.
    public static int AverageSkill(GameState s, int skill) =>
        s.Party.Last.Value = (byte)Average(Able(s).Select(x => x.Member.Skills[skill]));

    /// <summary>The best of weapon skills 0–3 of any present member (<c>party_best_skill_lo</c>).</summary>
    // orig: party_best_skill_lo R029+0x021E (0E76:021E) matched; tests: Oracle:rules.json party_best_skill_lo
    public static int BestHandWeaponSkill(GameState s) => BestOfSkills(s, 0, 4);

    /// <summary>The best of weapon skills 4–6 of any present member (<c>party_best_skill_hi</c>).</summary>
    // orig: party_best_skill_hi R029+0x026A (0E76:026A) matched; tests: Oracle:rules.json party_best_skill_hi
    public static int BestMissileSkill(GameState s) => BestOfSkills(s, 4, 7);

    private static int BestOfSkills(GameState s, int from, int to)
    {
        var last = s.Party.Last;
        last.Value = 0;
        foreach (var (i, m) in Present(s))
        {
            for (var k = from; k < to; k++)
            {
                if (m.Skills[k] > last.Value)
                {
                    last.Value = (byte)m.Skills[k];
                    last.Skill = k;
                    last.Member = i;
                }
            }
        }
        return last.Value;
    }

    // ---- attributes (their own result value) ------------------------------------------

    /// <summary>The lowest attribute among able members (<c>party_min_attr</c>).</summary>
    // orig: party_min_attr R029+0x164A (0E76:164A) matched; tests: Oracle:rules.json party_min_attr
    public static int MinAttribute(GameState s, int attribute) => LowestAttribute(s, Able(s), m => m.Attributes[attribute]);

    /// <summary>
    /// The lowest attribute sum a + b among able members. Original bug: it records 2 × a
    /// instead of the sum (the comparison uses the real sum).
    /// </summary>
    // orig: R029+0x1696 (0E76:1696) matched; tests: Oracle:rules.json party_min_attr_sum; MemberRulesTests
    // BUG: best_value = a * 2 (FixAttributeSums); it writes best_value, not DS:9C1D, and also DS:9A44.
    public static int MinAttributeSum(GameState s, int a, int b)
    {
        var last = s.Party.Last;
        last.Value = 199;
        foreach (var (i, m) in Able(s))
        {
            if (m.Attributes[a] + m.Attributes[b] < last.Value)
            {
                last.Value = (byte)(s.Behaviour.FixAttributeSums ? m.Attributes[a] + m.Attributes[b] : m.Attributes[a] * 2);
                last.AttributeMember = i;
                last.Member = i;
            }
        }
        return last.Value;
    }

    /// <summary>The highest attribute among able members (<c>party_max_attr</c>).</summary>
    // orig: party_max_attr R029+0x16FE (0E76:16FE) matched; tests: Oracle:rules.json party_max_attr
    public static int MaxAttribute(GameState s, int attribute) => HighestAttribute(s, Able(s), m => m.Attributes[attribute]);

    /// <summary>The highest attribute among present women.</summary>
    // orig: R029+0x174A (0E76:174A) matched; tests: Oracle:rules.json party_max_attr_women; FidelityFixTests
    // C: present members (status != 0) with record byte +3 == 1, unlike the skill variant (able).
    public static int MaxAttributeOfWomen(GameState s, int attribute) =>
        HighestAttribute(s, Present(s).Where(x => x.Member.IsFemale), m => m.Attributes[attribute]);

    /// <summary>
    /// The highest attribute sum a + b among present members (<c>party_max_attr_sum</c>).
    /// Original bug: it records 2 × a instead of the sum.
    /// </summary>
    // orig: party_max_attr_sum R029+0x179C (0E76:179C) matched; tests: Oracle:rules.json party_max_attr_sum; MemberRulesTests
    // BUG: best_value = a * 2 (FixAttributeSums); DS:9A44 is not written here.
    public static int MaxAttributeSum(GameState s, int a, int b)
    {
        var last = s.Party.Last;
        last.Value = 0;
        foreach (var (i, m) in Present(s))
        {
            if (m.Attributes[a] + m.Attributes[b] > last.Value)
            {
                last.Value = (byte)(s.Behaviour.FixAttributeSums ? m.Attributes[a] + m.Attributes[b] : m.Attributes[a] * 2);
                last.Member = i;
            }
        }
        return last.Value;
    }

    /// <summary>The average attribute of the present members (<c>party_avg_attr</c>).</summary>
    // orig: party_avg_attr R029+0x1800 (0E76:1800) matched; tests: Oracle:rules.json party_avg_attr
    public static int AverageAttribute(GameState s, int attribute) =>
        s.Party.Last.AttributeValue = (byte)Average(Present(s).Select(x => x.Member.Attributes[attribute]));

    /// <summary>The highest attribute a + skill b among present members.</summary>
    // orig: R029+0x184A (0E76:184A) matched; tests: Oracle:rules.json party_max_attr_skill
    public static int MaxAttributePlusSkill(GameState s, int attribute, int skill) =>
        Highest(s, Present(s), m => m.Skills[skill] + m.Attributes[attribute]);

    // ---- agility under burden ---------------------------------------------------------

    /// <summary>The average effective agility of the able members.</summary>
    // orig: party_avg_eff_attr2 R029+0x060E (0E76:060E) matched
    public static int AverageEffectiveAgility(GameState s) =>
        s.Party.Last.Value = (byte)Average(Able(s).Select(x => MemberRules.EffectiveAgility(s, x.Index)));

    /// <summary>The lowest effective agility among able members.</summary>
    // orig: party_min_eff_attr2 R029+0x0656 (0E76:0656) matched
    // C: the value is truncated to an unsigned char before the comparison (equal for 0..255).
    public static int MinEffectiveAgility(GameState s) => Lowest(s, Able(s), (i, _) => MemberRules.EffectiveAgility(s, i));

    /// <summary>The highest effective agility among able members.</summary>
    // orig: party_max_eff_attr2 R029+0x0690 (0E76:0690) matched
    public static int MaxEffectiveAgility(GameState s) => Highest(s, Able(s), (i, _) => MemberRules.EffectiveAgility(s, i));

    /// <summary>The highest effective agility plus the best mount's quality among able members (riding speed).</summary>
    // orig: party_max_eff_attr2_bonus R029+0x072E (0E76:072E) matched
    // C: member_eff_attr2 before best_item_quality(i, 0x2000L); neither draws or writes the last-query values.
    public static int MaxMountedAgility(GameState s) =>
        Highest(s, Able(s), (i, _) => MemberRules.EffectiveAgility(s, i) + Inventory.BestQualityWithFlags(s, i, ItemFlags.Mount));

    /// <summary>The lowest effective agility plus the best mount's quality among able members.</summary>
    // orig: party_min_eff_attr2_bonus R029+0x088E (0E76:088E) matched
    public static int MinMountedAgility(GameState s) =>
        Lowest(s, Able(s), (i, _) => MemberRules.EffectiveAgility(s, i) + Inventory.BestQualityWithFlags(s, i, ItemFlags.Mount));

    // ---- counts and the party values --------------------------------------------------

    /// <summary>
    /// Able women in the party (the reference calls it <c>party_count_able</c>: it counts members with
    /// status 1 and the female flag).
    /// </summary>
    // orig: party_count_able R029+0x007C (0E76:007C) matched; tests: PracticeAndItemsTests
    public static int AbleWomen(GameState s) => Able(s).Count(x => x.Member.IsFemale);

    /// <summary>Present members (<c>party_present_count</c>).</summary>
    // orig: party_present_count_impl R039+0x1DE6 (1462:1DE6) matched
    public static int PresentCount(GameState s) => Present(s).Count();

    /// <summary>False if any member is unconscious or dead (<c>party_all_ok</c>).</summary>
    // orig: party_all_ok R029+0x1972 (0E76:1972) matched
    public static bool AllWell(GameState s) =>
        !s.Party.Members.Any(m => m.Status is CharacterStatus.Dead or CharacterStatus.Unconscious);

    /// <summary>
    /// A party value (<c>party_get</c>): 4 fame, 5 whether every present member is able and has
    /// a mount, 6 the philosopher's stone; anything else 0.
    /// </summary>
    // orig: party_get R029+0x1326 (0E76:1326) matched; tests: PracticeAndItemsTests
    public static int Get(GameState s, int what) => what switch
    {
        4 => s.Party.Fame,
        5 => Present(s).All(x => x.Member.Status == CharacterStatus.Able && Inventory.CountWithFlags(s, x.Index, ItemFlags.Mount) != 0) ? 1 : 0,
        6 => s.Party.PhilosophersStone,
        _ => 0,
    };

    /// <summary>Sets a party value (<c>party_set</c>): only 4, the fame.</summary>
    // orig: party_set R029+0x1382 (0E76:1382) matched
    public static void Set(GameState s, int what, int value)
    {
        if (what == 4)
        {
            s.Party.Fame = (short)value;
        }
    }

    /// <summary>
    /// How learned the party is, 1..10 (<c>party_lore_level</c>): (average best weapon skill +
    /// best missile skill + average Virtue + average Alchemy) / 37, −2 alone, −1 for two, +1
    /// for five.
    /// </summary>
    // orig: party_lore_level_impl R039+0x0470 (1462:0470) matched; tests: Oracle:rules.json party_lore_level
    // C: member_best_skill per present member (it rewrites best_value/best_skill), then party_best_skill_hi, then the averages of 9 and 7.
    public static int LoreLevel(GameState s)
    {
        var sum = 0;
        var n = 0;
        foreach (var (i, _) in Present(s))
        {
            sum += MemberRules.BestWeaponSkill(s, i);
            n++;
        }
        if (n == 0)
        {
            n = 1;
        }
        var v = BestMissileSkill(s) + sum / n;
        v += AverageSkill(s, (int)Skill.Virtue);
        v += AverageSkill(s, (int)Skill.Alchemy);
        v /= 37;
        v += n switch { 1 => -2, 2 => -1, 5 => 1, _ => 0 };
        return Math.Clamp(v, 1, 10);
    }

    // ---- helpers --------------------------------------------------------------------

    private static int Lowest(GameState s, IEnumerable<(int Index, Character Member)> members, Func<Character, int> value) =>
        Lowest(s, members, (_, m) => value(m));

    // orig: party_min_skill R029+0x1396 (0E76:1396) matched
    // C: best_value starts at 199 and stores bytes; the comparison is strict, so the first member wins ties.
    private static int Lowest(GameState s, IEnumerable<(int Index, Character Member)> members, Func<int, Character, int> value)
    {
        var last = s.Party.Last;
        last.Value = 199;
        foreach (var (i, m) in members)
        {
            var v = value(i, m);
            if (v < last.Value)
            {
                last.Value = (byte)v;
                last.Member = i;
            }
        }
        return last.Value;
    }

    private static int Highest(GameState s, IEnumerable<(int Index, Character Member)> members, Func<Character, int> value) =>
        Highest(s, members, (_, m) => value(m));

    private static int Highest(GameState s, IEnumerable<(int Index, Character Member)> members, Func<int, Character, int> value)
    {
        var last = s.Party.Last;
        last.Value = 0;
        foreach (var (i, m) in members)
        {
            var v = value(i, m);
            if (v > last.Value)
            {
                last.Value = (byte)v;
                last.Member = i;
            }
        }
        return last.Value;
    }

    // orig: party_min_attr R029+0x164A (0E76:164A) matched
    // C: DS:9C1D, DS:9A44 and best_member are written together.
    private static int LowestAttribute(GameState s, IEnumerable<(int Index, Character Member)> members, Func<Character, int> value)
    {
        var last = s.Party.Last;
        last.AttributeValue = 199;
        foreach (var (i, m) in members)
        {
            if (value(m) < last.AttributeValue)
            {
                last.AttributeValue = (byte)value(m);
                last.AttributeMember = i;
                last.Member = i;
            }
        }
        return last.AttributeValue;
    }

    // orig: party_max_attr R029+0x16FE (0E76:16FE) matched; R029+0x174A (0E76:174A) matched
    private static int HighestAttribute(GameState s, IEnumerable<(int Index, Character Member)> members, Func<Character, int> value)
    {
        var last = s.Party.Last;
        last.AttributeValue = 0;
        foreach (var (i, m) in members)
        {
            if (value(m) > last.AttributeValue)
            {
                last.AttributeValue = (byte)value(m);
                last.AttributeMember = i;
                last.Member = i;
            }
        }
        return last.AttributeValue;
    }

    private static int Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Sum() / Math.Max(list.Count, 1);
    }
}
