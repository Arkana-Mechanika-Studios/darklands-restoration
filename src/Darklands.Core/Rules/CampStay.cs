using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Time;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

// orig: the mode argument of R036+0x0082 (1462:0082), as cards pass it through thunk 09C0:1EEB
/// <summary>Where the party stays (the camp screen's mode).</summary>
public enum CampPlace
{
    /// <summary>A room at the inn (a city): lodging priced by the city's size.</summary>
    Inn = 0,

    /// <summary>Camping in the wild: free, but poaching may be discovered.</summary>
    Wilderness = 1,

    /// <summary>Other lodging (a village, a monastery …): priced as size 2.</summary>
    OtherLodging = 2,

    /// <summary>Sleeping rough in town: 1 pfennig a day, standing drops, the watch may roust the party.</summary>
    SleepingRough = 3,
}

// orig: the activity codes of DS:8A18[] (R036+0x08EC / R036+0x147A); texts DS:3AC0[]
/// <summary>What a member does each day of a stay.</summary>
public enum CampActivity
{
    Relax = 0,
    RegainStrength = 1,
    Pray = 2,
    Alchemy = 3,
    EarnMoney = 4,
    GuardCamp = 5,
    Train = 6,
}

/// <summary>Why a stay ended when the party chose to spend a day.</summary>
public enum StayEnd
{
    /// <summary>The day was spent; the stay goes on.</summary>
    None,

    /// <summary>The stay is over (turned out, rousted, discovered, or a mishap here).</summary>
    Over,
}

// orig: overlay R036 (draft, 19/23 functions byte-matched; 0082, 076a, 10d0 and 147a not); tests: CampAndSheetRulesTests
/// <summary>
/// A stay at an inn, other lodging, a camp in the wild or the streets (overlay R036): each
/// member's activity, what the day costs, spending a day, day labour and the poaching risk.
/// </summary>
public sealed class CampStay
{
    private readonly GameState _s;
    private readonly int[] _teacherSkills;

    // orig: the set-up of R036+0x0082 (draft, unmatched: register allocation): teacher rows, activities 0, gain Healing / 15 for every non-empty slot
    // C: the gain is `party[i].f64 / (char)15`, a signed byte division (Healing is 0..99, so as in C#).
    // DIVERGENCE: the detail words (DS:8A4E) are global in the original and keep the last stay's text; here each stay starts empty (only shown once an activity sets them).
    /// <param name="teacherRows">The teacher list's rows, kept from stay to stay as the game's global (DS:8A38); null for a fresh list.</param>
    public CampStay(GameState s, CampPlace place, int[]? teacherRows = null)
    {
        _s = s;
        _teacherSkills = teacherRows ?? new int[EventLog.Capacity];
        Place = place;
        ListTeachers();
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (s.Party.Slot(i).IsPresent)
            {
                Gain[i] = s.Party.Slot(i)[Skill.Healing] / 15;
            }
        }
    }

    public CampPlace Place { get; }

    /// <summary>Each member's activity.</summary>
    public CampActivity[] Activity { get; } = new CampActivity[Party.MaxMembers];

    /// <summary>Each member's number for the activity: the gain, the potions, the daily pay or the teacher's skill.</summary>
    public int[] Gain { get; } = new int[Party.MaxMembers];

    /// <summary>Each member's word for the activity: the job, "potions", or the teacher's fee ("12pfs").</summary>
    public string[] Detail { get; } = ["", "", "", "", ""];

    /// <summary>Days spent so far (also counted when a day could not be paid).</summary>
    public int Days { get; set; }

    /// <summary>The poaching risk so far (camping in the wild; shown as risk / 5).</summary>
    public int Risk { get; set; }

    /// <summary>What a day in the wild adds to the risk before the guards' work.</summary>
    public int DailyRisk { get; set; }

    /// <summary>The teacher picked from the list (an index into <see cref="TeacherSkill"/>).</summary>
    public int ChosenTeacher { get; set; }

    // C: DS:8A38 is filled at the scan position, not at the matching teacher's number (see TeacherList).
    /// <summary>
    /// The skill of the teacher in list row <paramref name="row"/>. Faithful quirk: the rows are
    /// numbered by the teacher events met in the whole log, so teachers in other towns leave
    /// gaps (and end the list early).
    /// </summary>
    public int TeacherSkill(int row) => row >= 0 && row < _teacherSkills.Length ? _teacherSkills[row] : 0;

    /// <summary>Whether any teacher is here.</summary>
    public bool TeachersHere => _s.Events.At(EventKinds.Teacher, _s.CurrentLocation);

    // orig: R036+0x1D24 (1462:1D24) draft (bytes match); the same scan repeated inline in R036+0x0082 and R036+0x147A
    // BUG: j counts every teacher event met, so rows are numbered by log position and the scan stops after "count here" events of any town.
    /// <summary>The names of the skills taught here, in list order (the teacher box).</summary>
    public List<int> TeacherList()
    {
        var skills = new List<int>();
        var count = _s.Events.CountAt(EventKinds.Teacher, _s.CurrentLocation);
        for (int k = 0, j = 0; j < count; k++, j++)
        {
            k = _s.Events.Next(EventKinds.Teacher, k);
            if (k == -1)
            {
                break;
            }
            if (_s.Events.HasStarted(k) && _s.Events[k]!.Location == _s.CurrentLocation)
            {
                _teacherSkills[j] = _s.Events[k]!.Type;
                skills.Add(_s.Events[k]!.Type);
            }
        }
        return skills;
    }

    private void ListTeachers() => TeacherList();

    // orig: event-search(0x28, skill, location-now, -1, -1, -1) as R036+0x08EC, R036+0x1858 and R036+0x147A use it
    // C: R036+0x147A (the day itself) tests for -1 (1462:1589) and then neither pays nor practises.
    // DIVERGENCE: with no teacher of the skill here (the list's rows stale: TeacherList) R036+0x08EC (key T) and R036+0x1858 (the day's
    //   cost) read the fee of events[-1] unguarded (1462:0BAF, 1462:18ED: les bx, es:[bx+1B2C] with bx = -4): the far pointer at 2E38:1B28,
    //   the tail of text-slot-named[4], 0 in play, so the fee is the word at 0000:001E (the INT 7 vector's segment, F000 under DOSBox: the
    //   "-4096pfs" players see); here no fee is shown or added. The day itself (R036+0x147A) finds no teacher and the member relaxes.
    /// <summary>The teacher of a skill here (−1: none).</summary>
    private TimedEvent? Teacher(int skill)
    {
        var slot = _s.Events.Find(EventKinds.Teacher, skill, _s.CurrentLocation, -1, -1, -1);
        return slot == -1 ? null : _s.Events[slot];
    }

    // ---- choosing activities --------------------------------------------------------

    private Character Member(int i) => _s.Party.Slot(i);

    // orig: the conditions of R036+0x0F8C (draft, bytes match) and of the keys in R036+0x08EC (draft, bytes match)
    /// <summary>Regaining strength is possible below the maximum.</summary>
    public bool CanRegainStrength(int i) => Member(i)[Attribute.Strength] < Member(i).Maximum(Attribute.Strength);

    /// <summary>Praying is possible below the maximum divine favour.</summary>
    public bool CanPray(int i) => Member(i)[Attribute.DivineFavor] < Member(i).Maximum(Attribute.DivineFavor);

    /// <summary>Alchemy needs a known formula.</summary>
    // orig: R036+0x1E80 (1462:1E80) draft(fn matched): 1 when one of the member's
    //   0x16 known-recipes bytes is non-zero (the A key and the menu line's colour both ask it).
    // C: `cmp byte ptr [si+0x7BE], 0` (R036+0x1E9B) tests the whole byte: bits 3..7 (not formulas; only a file can set them) count too;
    //   unit run of R036+0x1E80 (t_alch.py, local/batch/evid1): byte 0x80 -> 1, 0x08 -> 1, 0x07 -> 1, 0 -> 0; tests: CampAndSheetRulesTests
    public bool CanDoAlchemy(int i) => Member(i).Formulas.Bytes.Any(b => b != 0);

    public bool CanEarnMoney => Place == CampPlace.Inn;

    public bool CanGuard => Place == CampPlace.Wilderness;

    /// <summary>The daily gain from regaining strength: the party's best Healing / 15, in 1..99.</summary>
    public int StrengthGain => Math.Clamp(PartyQueries.MaxSkill(_s, (int)Skill.Healing) / 15, 1, 99);

    // orig: key J of R036+0x08EC: R036+0x1EC2, activity 0, gain Healing / 15
    public void Relax(int i, IReadOnlyList<Formula> formulas)
    {
        Brewing.CallOff(_s, formulas, i);
        Activity[i] = CampActivity.Relax;
        Gain[i] = Member(i)[Skill.Healing] / 15;
    }

    // orig: key R of R036+0x08EC: when Str < its maximum
    public void RegainStrength(int i, IReadOnlyList<Formula> formulas)
    {
        if (CanRegainStrength(i))
        {
            Brewing.CallOff(_s, formulas, i);
            Activity[i] = CampActivity.RegainStrength;
            Gain[i] = StrengthGain;
        }
    }

    // orig: key P of R036+0x08EC: when DF < its maximum
    public void Pray(int i, IReadOnlyList<Formula> formulas)
    {
        if (CanPray(i))
        {
            Brewing.CallOff(_s, formulas, i);
            Activity[i] = CampActivity.Pray;
            Gain[i] = PrayerGain(i);
        }
    }

    private int PrayerGain(int i) => (Member(i)[Skill.Religion] + Member(i)[Skill.Virtue]) / 12 + 1;

    // orig: key A of R036+0x08EC: activity 3 (R036+0x190A), the alchemy screen (R029+0x2204), gain = R036+0x191C
    // C: the test is `DS:89FE != -1 && DS:89FE == 0`; the -1 test is redundant. Unlike the other keys, A does not call off the brews first (the alchemy screen takes them back itself).
    /// <summary>
    /// Alchemy work: <paramref name="useBench"/> is the alchemy screen. If the member then has
    /// a brew under way the activity is "make N potions", otherwise the old activity stays.
    /// </summary>
    public void DoAlchemy(int i, Action useBench)
    {
        if (!CanDoAlchemy(i))
        {
            return;
        }
        var (oldActivity, oldGain) = (Activity[i], Gain[i]);
        Activity[i] = CampActivity.Alchemy;
        useBench();
        Gain[i] = Brewing.BatchesBrewing(_s, i);
        if (Gain[i] == 0)
        {
            (Activity[i], Gain[i]) = (oldActivity, oldGain);
        }
        else
        {
            Detail[i] = Gain[i] != 1 ? "potions" : "potion";
        }
    }

    // orig: key E of R036+0x08EC (inn only): R036+0x197E
    public void EarnMoney(int i, IReadOnlyList<Formula> formulas)
    {
        if (CanEarnMoney)
        {
            Brewing.CallOff(_s, formulas, i);
            Activity[i] = CampActivity.EarnMoney;
            (Gain[i], Detail[i]) = DayJobs.Best(_s, i);
        }
    }

    // orig: key G of R036+0x08EC (the wild only)
    public void Guard(int i, IReadOnlyList<Formula> formulas)
    {
        if (CanGuard)
        {
            Brewing.CallOff(_s, formulas, i);
            Activity[i] = CampActivity.GuardCamp;
        }
    }

    // orig: key T of R036+0x08EC: when a teacher is here; DS:89FA reset to 0 when out of range; detail = the fee + "pfs"
    /// <summary>Training with the chosen teacher (row 0 when the choice is out of range).</summary>
    public void Train(int i, IReadOnlyList<Formula> formulas)
    {
        if (!TeachersHere)
        {
            return;
        }
        Brewing.CallOff(_s, formulas, i);
        Activity[i] = CampActivity.Train;
        if (ChosenTeacher < 0 || _s.Events.CountAt(EventKinds.Teacher, _s.CurrentLocation) <= ChosenTeacher)
        {
            ChosenTeacher = 0;
        }
        Gain[i] = TeacherSkill(ChosenTeacher);
        Detail[i] = (Teacher(Gain[i])?.Value ?? 0) + "pfs";
    }

    // ---- the cost of a day ----------------------------------------------------------

    // orig: the first part of R036+0x1858 (draft, bytes match): thunk 09C0:2099 = LocationRules.ServicePrice
    /// <summary>The lodging price of a day (before work and fees): inn by city size, other lodging as size 2, rough 1 pf, the wild free.</summary>
    public int LodgingPrice => Place switch
    {
        CampPlace.Inn => LocationRules.ServicePrice(_s, Standing, _s.City.CitySize),
        CampPlace.OtherLodging => LocationRules.ServicePrice(_s, Standing, 2),
        CampPlace.SleepingRough => 1,
        _ => 0,
    };

    private int Standing => _s.CurrentLocation >= 0 ? LocationRules.Reputation(_s, _s.CurrentLocation) : 0;

    // orig: R036+0x1858 (1462:1858) draft (bytes match)
    /// <summary>A day's net cost in pfennigs: lodging, less the able workers' pay, plus the trainees' fees.</summary>
    public int DayCost()
    {
        var total = LodgingPrice;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (Member(i).IsAble)
            {
                total += Activity[i] switch
                {
                    CampActivity.EarnMoney => -Gain[i],
                    CampActivity.Train => Teacher(Gain[i])?.Value ?? 0,
                    _ => 0,
                };
            }
        }
        return total;
    }

    // ---- spending a day -------------------------------------------------------------

    // orig: key S of R036+0x08EC (draft, bytes match)
    // C: the rough-sleeping test is roll-below(100) >= limit-to-range(0x19, 0x5F, best Streetwise + 0x19); the wild test draws roll-below(0x32) twice (the order does not matter for a sum).
    // C: Days (DS:3ABE) is counted even when the day could not be paid.
    /// <summary>
    /// "Spend a day": first the checks that can end the stay (turned out of other lodging; the
    /// watch rousting the party in town, chance 100 − clamp(best Streetwise + 25, 25, 95) %;
    /// the camp discovered when rnd 50 + rnd 50 ≤ the risk, which remembers the first guard
    /// as the saved card), then the day itself, then a mishap here ends the stay.
    /// </summary>
    public StayEnd SpendDay()
    {
        var here = _s.CurrentLocation;
        if (Place == CampPlace.OtherLodging && _s.Events.At(EventKinds.TurnedOut, here))
        {
            return StayEnd.Over;
        }
        if (Place == CampPlace.SleepingRough &&
            _s.Random.Below(100) >= Math.Clamp(PartyQueries.MaxSkill(_s, (int)Skill.Streetwise) + 25, 25, 95))
        {
            _s.Events.CreateHere(EventKinds.RoustedSleepingRough, 2);
            return StayEnd.Over;
        }
        if (Place == CampPlace.Wilderness && _s.Random.Below(50) + _s.Random.Below(50) <= Risk)
        {
            _s.SavedCard = -1;
            for (var i = Party.MaxMembers - 1; i >= 0; i--)
            {
                if (Member(i).IsPresent && Activity[i] == CampActivity.GuardCamp)
                {
                    _s.SavedCard = i;
                }
            }
            _s.Events.CreateHere(EventKinds.CampDiscovered, 2);
            return StayEnd.Over;
        }
        PassTheDay();
        if (Place != CampPlace.Wilderness && _s.Events.At(EventKinds.AlchemyMishap, here))
        {
            return StayEnd.Over;
        }
        Days++;
        return StayEnd.None;
    }

    /// <summary>Shows the hourglass while the day's hours pass (the presentation's; null: nothing shown).</summary>
    public Action<int>? Hourglass { get; init; }

    // orig: camp-day-passes = R036+0x147A (1462:147A) draft (unmatched: the member loop's shape), checked against asm
    /// <summary>
    /// The day itself (<c>camp-day-passes</c>): unless the purse cannot pay the day's cost
    /// ("Not Enough Money"), every able member does the activity, lodging is paid, the poaching
    /// risk grows (also at the inn: harmless), and time passes to the next 05:00 at least nine
    /// hours away. Faithful quirks: regaining strength may pass the maximum (time passing caps
    /// it); a trainee pays only when the purse has the fee but practises anyway.
    /// </summary>
    public void PassTheDay()
    {
        var cost = DayCost();
        if (cost > 0 && Place != CampPlace.Wilderness && _s.Party.Purse < Money.FromPfennigs(cost))
        {
            // orig: R036+0x147A: root 0000:24BC("Not Enough Money", 0x50)
            _s.Messages.Show("Not Enough Money", 0x50);
            return;
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (Member(i).IsAble)
            {
                DoTheActivity(i);
            }
        }
        PayLodging();
        var hours = TimeOfDay.HoursUntil(_s.Date.Hour, 5);
        // orig: R036+0x147A: pass-hours(hours-remaining-until(5) < 9 ? hours-remaining-until(5) + 24 : hours-remaining-until(5)), with its hourglass
        TimePassage.Advance(_s, hours < 9 ? hours + 24 : hours, Hourglass);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (Activity[i] == CampActivity.RegainStrength)
            {
                Gain[i] = StrengthGain;
            }
        }
        ListTeachers();
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (Activity[i] == CampActivity.Train && (!TeachersHere || Teacher(Gain[i]) is null))
            {
                Activity[i] = CampActivity.Relax;
            }
        }
    }

    // orig: the member loop of R036+0x147A (asm: si runs over party+0x4A, status byte at si-0x4A == 1)
    // C: attributes are signed bytes: Str (+0x4A) += gain, compared with its maximum (+0x51) by `jg`; Endurance (+0x49) = its maximum (+0x50); DF (+0x4F) += gain against +0x56 by `jge`. The sums stay below 128 for skills up to 99.
    private void DoTheActivity(int i)
    {
        var m = Member(i);
        switch (Activity[i])
        {
            case CampActivity.RegainStrength:
                m[Attribute.Strength] += StrengthGain;
                if (m[Attribute.Strength] >= m.Maximum(Attribute.Strength))
                {
                    Activity[i] = CampActivity.Relax;
                }
                m[Attribute.Endurance] = m.Maximum(Attribute.Endurance);
                break;
            case CampActivity.Relax:
                m[Attribute.Endurance] = m.Maximum(Attribute.Endurance);
                break;
            case CampActivity.Pray:
                m[Attribute.DivineFavor] += PrayerGain(i);
                if (m[Attribute.DivineFavor] >= m.Maximum(Attribute.DivineFavor))
                {
                    Activity[i] = CampActivity.Relax;
                }
                break;
            case CampActivity.Alchemy:
                Activity[i] = CampActivity.Relax;
                break;
            case CampActivity.EarnMoney:
                _s.Party.Purse = _s.Party.Purse.Add(0, 0, Gain[i]);
                break;
            // C: the fee test compares the purse in pfennigs (32-bit) with the fee sign-extended; the trainee practises (thunk 09C0:1F63) even when it could not pay.
            case CampActivity.Train:
                if (Teacher(Gain[i]) is { } teacher)
                {
                    if (_s.Party.Purse.TotalPfennigs >= teacher.Value)
                    {
                        _s.Party.Purse = _s.Party.Purse.Subtract(0, 0, teacher.Value);
                    }
                    // BUG: no limit at the teacher's ability, which a guild text says a pupil "cannot progress beyond" (MSGFILES):
                    // any teacher can take a skill to 99.
                    Practice.PracticeSkill(_s, i, teacher.Type, 1, teacher.Extra * 20 / 100, -1);
                }
                break;
        }
    }

    // orig: the tail of R036+0x147A (asm: mode 0 pays and goes on to the risk; mode 2 pays; mode 3 pays 1 and lowers the standing; else the risk)
    private void PayLodging()
    {
        var price = LodgingPrice;
        switch (Place)
        {
            case CampPlace.Inn:
            case CampPlace.OtherLodging:
                if (_s.Party.Purse.TotalPfennigs >= price)
                {
                    _s.Party.Purse = _s.Party.Purse.Subtract(0, 0, price);
                }
                break;
            case CampPlace.SleepingRough:
                if (_s.Party.Purse.TotalPfennigs >= 1)
                {
                    _s.Party.Purse = _s.Party.Purse.Subtract(0, 0, 1);
                }
                var standing = LocationRules.Query(_s, _s.CurrentLocation, 0);
                if (standing >= -10)
                {
                    LocationRules.Set(_s, _s.CurrentLocation, 0, standing - 1);
                }
                break;
        }
        if (Place is CampPlace.Inn or CampPlace.Wilderness)
        {
            GrowRisk();
        }
    }

    // orig: R036+0x1B80 (1462:1B80) draft (bytes match)
    // BUG: the weapon-skill test reads +0x57, +0x5C, +0x59, +0x5A, +0x5B, +0x5D: Impact Weapons (+0x58) is not among them.
    /// <summary>
    /// The day's poaching risk: none while <see cref="EventKinds.SafeCamping"/> holds (the risk
    /// is reset); else <see cref="DailyRisk"/> less each guard's Woodwise (≥ 70: 3, ≥ 30: 2,
    /// else 1), Stealth (≥ 70: 2, ≥ 30: 1) and 1 for a weapon skill of 30 — faithful quirk:
    /// Impact Weapons is not looked at — kept in 1..15 and added to <see cref="Risk"/>.
    /// </summary>
    public void GrowRisk()
    {
        if (_s.Events.Started(EventKinds.SafeCamping))
        {
            Risk = 0;
            return;
        }
        var total = DailyRisk;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            var m = Member(i);
            if (!m.IsAble || Activity[i] != CampActivity.GuardCamp)
            {
                continue;
            }
            total -= m[Skill.Woodwise] >= 70 ? 3 : m[Skill.Woodwise] >= 30 ? 2 : 1;
            total -= m[Skill.Stealth] >= 70 ? 2 : m[Skill.Stealth] >= 30 ? 1 : 0;
            Skill[] weapons = [Skill.EdgedWeapons, Skill.Bows, Skill.FlailWeapons, Skill.Polearms, Skill.ThrownWeapons, Skill.MissileDevices];
            if (weapons.Any(w => m[w] >= 30))
            {
                total--;
            }
        }
        Risk += Math.Clamp(total, 1, 15);
    }
}
