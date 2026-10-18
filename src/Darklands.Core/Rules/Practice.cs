using Darklands.Core.Characters;

namespace Darklands.Core.Rules;

/// <summary>Learning by doing, and the party's fame rewards.</summary>
public static class Practice
{
    /// <summary>
    /// A gain of <paramref name="low"/> + rnd(high − low) with chance |100 − value| %, else
    /// <paramref name="low"/> (<c>improve_roll</c>; the original computes a 0..99 limit of the
    /// value and ignores it). The chance roll comes first.
    /// </summary>
    // orig: improve_roll R029+0x18A8 (0E76:18A8) matched; tests: Oracle:rules.json improve_roll; PracticeAndItemsTests
    // C: clamp99(v) is called and its result dropped; abs(100 - v) >= rand_mod(100), then rand_mod(hi - lo) (n < 2 counts as 2).
    public static int ImproveRoll(GameState s, int value, int low, int high) =>
        Math.Abs(100 - value) >= s.Random.Below(100) ? s.Random.Below(high - low) + low : low;

    /// <summary>
    /// Practising a skill (<c>practice_skill</c>; mode 1 = a success, otherwise a failure).
    /// One member: chance <paramref name="percent"/> × 3.3 % on success, × 1.1 % otherwise
    /// (the roll is made whether or not the member is present). −2: every able member rolls
    /// separately with × 1.5 % or × 0.5 %. The gain is <see cref="ImproveRoll"/>(skill,
    /// level + 1, 2 × (level + 2)), at least 1.
    /// </summary>
    // orig: practice_skill_impl R039+0x0132 (1462:0132) matched; tests: Oracle:rules.json practice_skill; PracticeAndItemsTests
    // C: -2 loops i < party_size and draws only for status-1 members (&& short-circuit); one member always draws first.
    public static void PracticeSkill(GameState s, int who, int skill, int mode, int percent, int level)
    {
        who = s.Party.Resolve(who);
        if (who == Who.Everybody)
        {
            var limit = (mode == 1 ? percent * 15 : percent * 5) / 10;
            for (var i = 0; i < s.Party.Members.Count; i++)
            {
                if (s.Party.Members[i].Status == CharacterStatus.Able && s.Random.Below(100) <= limit)
                {
                    MemberRules.AddSkill(s, i, skill, Gain(s, i, skill, level));
                }
            }
            return;
        }
        var roll = s.Random.Below(100);
        if ((mode == 1 ? percent * 33 : percent * 11) / 10 >= roll)
        {
            MemberRules.AddSkill(s, who, skill, Gain(s, who, skill, level));
        }
    }

    private static int Gain(GameState s, int member, int skill, int level) =>
        Math.Clamp(ImproveRoll(s, MemberRules.Skill(s, member, skill), level + 1, (level + 2) * 2), 1, 99);

    /// <summary>
    /// A reward of size 0..5 (<c>give_reward</c>): 0, 3, 10, 25, 64 or 200 fame, ×2/3 at
    /// Basic and ×3/2 at Expert, added to the party's fame. Returns the new fame.
    /// </summary>
    // orig: give_reward_impl R039+0x1E06 (1462:1E06) matched; tests: MemberRulesTests
    // C: `party_set(4, v = party_get(4) + v); return v;` returns the new fame (16-bit), not the reward.
    public static int GiveReward(GameState s, int size)
    {
        int[] sizes = [0, 3, 10, 25, 64];
        var v = size is >= 0 and < 5 ? sizes[size] : 200;
        v = s.Difficulty switch
        {
            Difficulty.Basic => v * 2 / 3,
            Difficulty.Expert => v * 3 / 2,
            _ => v,
        };
        v = PartyQueries.Get(s, 4) + v;
        PartyQueries.Set(s, 4, v);
        return (short)v;
    }

    /// <summary>
    /// A fame cost of size 0..4 (0, 1, 2, 4 or 15 × max(1, fame / 200)). Original bug: the fame
    /// becomes cost − fame (and an unknown size uses an undefined factor; here 0). Returns it.
    /// </summary>
    // orig: R039+0x1E90 (1462:1E90) matched
    // BUG: v - fame is stored (FixFameCost).
    // DIVERGENCE: a size above 4 multiplies an uninitialised register in the original; here 0.
    public static int ChargeFame(GameState s, int size)
    {
        var v = size switch { 1 => 1, 2 => 2, 3 => 4, 4 => 15, _ => 0 };
        v *= Math.Max(1, PartyQueries.Get(s, 4) / 200);
        v = s.Behaviour.FixFameCost ? PartyQueries.Get(s, 4) - v : v - PartyQueries.Get(s, 4);
        PartyQueries.Set(s, 4, v);
        return (short)v;
    }
}
