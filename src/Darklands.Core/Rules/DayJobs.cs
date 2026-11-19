using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

// orig: R036+0x197E (1462:197E) draft (bytes match); table DS:3ACE (31 x 18 bytes), names R136+0xA82B; tests: CampAndSheetRulesTests
/// <summary>
/// Day labour at the inn ("Earn a little money"): the game's 31 jobs, each needing city
/// buildings or location flags, judged by an attribute and two skills.
/// </summary>
public static class DayJobs
{
    /// <summary>
    /// One job: <paramref name="Contents"/> city buildings of which one is needed (0: none),
    /// <paramref name="OtherContents"/> a second set (never met: see <see cref="Best"/>),
    /// <paramref name="LocationFlags"/> location flags of which one is needed, the attribute and
    /// skills with their minimums, and the pay factor. <paramref name="NameEntry"/> is the job's entry of the job-name table (the
    /// record's first byte; 0 is the day labourer).
    /// </summary>
    public sealed record Job(int NameEntry, int Contents, int OtherContents, int LocationFlags,
        Attribute Attribute, int AttributeMinimum, Skill Skill1, int Minimum1, Skill Skill2, int Minimum2, int Pay);

    private static Job J(int name, int contents, int other, int flags, int attribute, int amin, int s1, int min1, int s2, int min2, int pay) =>
        new(name, contents, other, flags, (Attribute)attribute, amin, (Skill)s1, min1, (Skill)s2, min2, pay);

    /// <summary>The day labourer's entry of the job-name table (what a member earning below 2 pfennigs is).</summary>
    public const int DayLabourer = 0;

    public static readonly IReadOnlyList<Job> All =
    [
        J(1, 0, 0, 0, 3, 20, 16, 10, 16, 20, 2),
        J(2, 0, 0, 0, 3, 20, 10, 15, 7, 15, 3),
        J(3, 0, 0, 0, 2, 20, 10, 15, 9, 15, 3),
        J(4, 0, 0x4000, 0, 5, 25, 10, 20, 8, 10, 3),
        J(4, 0, 0, 64, 5, 15, 10, 15, 9, 10, 5),
        J(5, 0, 0x8000, 0, 3, 20, 13, 15, 9, 15, 3),
        J(6, 0, 0, 0, 3, 20, 13, 15, 7, 15, 3),
        J(7, 0x20, 0, 0, 1, 20, 16, 10, 15, 25, 3),
        J(8, 0, 0, 0, 1, 20, 0, 15, 3, 15, 4),
        J(9, 0, 0, 16, 0, 20, 0, 15, 1, 15, 4),
        J(9, 0, 0, 4, 0, 20, 0, 15, 1, 15, 5),
        J(10, 0, 0, 1, 1, 20, 0, 15, 5, 15, 5),
        J(11, 0, 0, 1, 1, 20, 3, 15, 17, 15, 6),
        J(12, 0, 0, 0, 3, 20, 16, 15, 2, 15, 4),
        J(13, 0, 0, 0, 4, 20, 10, 15, 12, 15, 4),
        J(13, 0, 0, 8, 4, 20, 10, 15, 12, 15, 5),
        J(14, 0, 0, 0, 4, 20, 11, 15, 12, 15, 4),
        J(14, 0, 0, 16, 4, 20, 11, 15, 12, 15, 5),
        J(15, 0, 0, 0, 1, 20, 14, 5, 3, 25, 5),
        J(16, 0, 0, 0, 1, 20, 14, 5, 0, 25, 5),
        J(17, 0, 0, 0, 1, 20, 14, 5, 1, 25, 5),
        J(18, 0, 0, 0, 4, 20, 5, 15, 6, 25, 5),
        J(19, 0, 0, 0, 4, 20, 14, 5, 14, 15, 5),
        J(20, 0x4000, 0, 0, 4, 25, 12, 10, 8, 10, 5),
        J(21, 0, 0, 8, 2, 20, 10, 15, 4, 15, 3),
        J(22, 0, 0, 32, 2, 20, 10, 15, 4, 15, 4),
        J(23, 0, 0, 0, 3, 20, 16, 15, 9, 20, 3),
        J(23, 0, 0, 4, 3, 20, 16, 15, 9, 20, 4),
        J(24, 0, 0, 16, 3, 25, 16, 15, 9, 20, 9),
        J(24, 0, 0, 4, 3, 25, 16, 15, 9, 20, 10),
        J(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    ];

    // orig: R036+0x197E
    // BUG: the second contents test is written `!otherContents & city.Contents`, so a job with OtherContents != 0 is never open.
    // C: val is not reset for a filtered job; the stale value cannot beat best (it was already compared), so skipping is equivalent.
    // C: city ordinal % 4 and standing / 20 are C int division (truncation toward zero, as in C#).
    /// <summary>
    /// The best paying job for a member here, and its daily pay in pfennigs. Each job open
    /// here pays (city ordinal % 4 + min(0, standing / 20) + the two skill terms +
    /// (attribute − minimum) / 3) × max(9, city size) × pay / 70, a skill term being 1 below 4
    /// over the minimum, else √(excess) in 1..9. Below 2 pfennigs the member is a "Day Laborer"
    /// for 2 (name entry <see cref="DayLabourer"/>). Faithful quirk: a job needing <see cref="Job.OtherContents"/> is never open (the
    /// original tests <c>!otherContents &amp; city.Contents</c>).
    /// </summary>
    public static (int Pay, int NameEntry) Best(GameState s, int member)
    {
        var m = s.Party.Slot(member);
        var city = s.City;
        var place = s.Here;
        var best = 0;
        Job? chosen = null;
        foreach (var job in All)
        {
            var open = (job.Contents == 0 || (city.Contents & job.Contents) != 0)
                && job.OtherContents == 0
                && (job.LocationFlags == 0 || ((place?.Flags ?? 0) & job.LocationFlags) != 0);
            if (!open)
            {
                continue;
            }
            var attribute = (m[job.Attribute] - job.AttributeMinimum) / 3;
            var skills = SkillTerm(m[job.Skill1] - job.Minimum1) + SkillTerm(m[job.Skill2] - job.Minimum2);
            var standing = Math.Min(0, (place?.Reputation ?? 0) / 20);
            var pay = (city.Ordinal % 4 + standing + skills + attribute) * Math.Max(9, city.CitySize) * job.Pay / 70;
            if (best < pay)
            {
                best = pay;
                chosen = job;
            }
        }
        return best < 2 || chosen is null ? (2, DayLabourer) : (best, chosen.NameEntry);
    }

    // orig: R036+0x1B4C (1462:1B4C) draft (bytes match); the square root is root 047C:11AB (Newton iteration, floor)
    /// <summary>A skill's excess over the minimum as a job term: 1 below 4, else its square root in 1..9.</summary>
    public static int SkillTerm(int excess) => excess < 4 ? 1 : Math.Clamp((int)Math.Sqrt(excess), 1, 9);
}
