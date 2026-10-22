using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Time;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>
/// Time passing (<c>advance_hours</c>, <c>advance_days</c>): the party recovers, the clock
/// moves on with the monthly update at each new month, the expired events run their
/// handlers, and the random generator is reseeded from the real-time clock (once after the
/// recovery and once at the end, as the game does).
/// </summary>
public static class TimePassage
{
    /// <summary>Raised for each month the clock enters, after the monthly update (the month number may be 12 = January).</summary>
    public static event Action<GameState, int>? MonthStarted;

    /// <summary>Raised when nobody is left in the party (the original shows the "death" screen and ends).</summary>
    public static event Action<GameState>? PartyPerished;

    // orig: advance_hours R029+0x54D8 (1367:05C8) matched; tests: CoreValueRulesTests; EventLogTests; FidelityFixTests
    // C: `if (n > 2) pass_hours_hourglass_vec(n)` (R043+0x3494, draft; screen only) runs between hours_pass and the clock: the
    //   presentation's <paramref name="hourglass"/> (null: nothing is shown).
    public static void Advance(GameState state, int hours, Action<int>? hourglass = null)
    {
        Recover(state, hours);
        if (hours > 2)
        {
            hourglass?.Invoke(hours);
        }
        state.Date = Calendar.AdvanceHours(state.Date, hours, (date, month) =>
        {
            state.Date = date;
            NewMonth(state, month);
        });
        EventExpiry.Run(state);
        Reseed(state);
    }

    // orig: advance_days R029+0x5590 (1367:0680) matched; tests: CoreValueRulesTests
    // C: cur_day += n comes first; hours_pass(n * 24) then sees the old hour (a new day for n > 0).
    public static void AdvanceDays(GameState state, int days)
    {
        state.Date = state.Date with { Day = (short)(state.Date.Day + days) };
        // C: hours_pass(n * 24) multiplies in 16 bits.
        Recover(state, (short)(days * GameDate.HoursPerDay));
        state.Date = Calendar.AdvanceDays(state.Date, 0, (date, month) =>
        {
            state.Date = date;
            NewMonth(state, month);
        });
        EventExpiry.Run(state);
        Reseed(state);
    }

    private static void NewMonth(GameState state, int month)
    {
        MonthlyUpdate.Run(state, month);
        MonthStarted?.Invoke(state, month);
    }

    /// <summary>
    /// The party's recovery for one passage of time (applied once per advance, however long,
    /// <c>hours_pass</c>): the dead leave their slots, Endurance returns, and on crossing
    /// midnight Divine Favor, Strength, Agility, Perception and Charisma may grow back, up to
    /// their maximum plus any temporary boost. Empty slots lose their hired-member term, and
    /// the generator is reseeded.
    /// </summary>
    // orig: hours_pass R029+0x255A (0E76:255A) matched; tests: EventLogTests; PracticeAndItemsTests; FidelityFixTests
    // DIVERGENCE: with nobody left the original plays the death tune, shows the "death" animation and exits to DOS (exit_(n)); here
    //   PartyPerished is raised: the game's presentation does the same and ends the game (returning to the start screen); without a
    //   handler the passage goes on.
    // C: the record bytes are signed chars (no overflow below 128); the caps use event_48_max + the maximum.
    public static void Recover(GameState state, int hours)
    {
        var party = state.Party;
        party.EnsureAbleLeader();
        // C: cur_hour + hours >= 24 with the hour before the clock moves (advance_days has already moved the day).
        var newDay = state.Date.Hour + hours >= GameDate.HoursPerDay;
        for (var i = 0; i < party.Members.Count; i++)
        {
            var member = party.Members[i];
            if (member.Status == CharacterStatus.Empty)
            {
                continue;
            }
            if (member.Status == CharacterStatus.Dead)
            {
                member.Status = CharacterStatus.Empty;
                continue;
            }
            RegainEndurance(member);
            if (newDay)
            {
                OvernightRecovery(state, member);
            }
            foreach (var a in RecoveringAttributes)
            {
                member[a] = Math.Min(member[a], MemberRules.AttributeCap(state, i, (int)a));
            }
        }
        if (!party.Members.Any(m => m.IsPresent))
        {
            PartyPerished?.Invoke(state);
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (!party.Slot(i).IsPresent && state.Events.Find(EventKinds.MemberTerm, i, -1, -1, -1, -1) is var term and not -1)
            {
                state.Events.Free(term);
            }
        }
        Reseed(state);
    }

    private static readonly Attribute[] RecoveringAttributes =
        [Attribute.DivineFavor, Attribute.Strength, Attribute.Perception, Attribute.Agility, Attribute.Charisma];

    /// <summary>Endurance comes back to within 4 of its maximum at once, then one point at a time.</summary>
    private static void RegainEndurance(Character member)
    {
        var gap = member.Maximum(Attribute.Endurance) - member[Attribute.Endurance];
        if (gap > 4)
        {
            member[Attribute.Endurance] = member.Maximum(Attribute.Endurance) - 4;
        }
        else if (gap is 3 or 4)
        {
            member[Attribute.Endurance]++;
        }
    }

    // orig: hours_pass R029+0x255A (0E76:255A) matched
    private static void OvernightRecovery(GameState state, Character member)
    {
        var random = state.Random;
        // C: clamp_range(1, 5, religion / (rand_mod(5) + 20)): one draw; then random_n(150), random_n(100) x 3 for Agility, Perception, Charisma in that order.
        member[Attribute.DivineFavor] += Math.Clamp(member[Skill.Religion] / (20 + random.Below(5)), 1, 5);
        // C: the original asks party_max_skill(13), which also leaves the best healer in
        // best_member/best_value (Party.Last) for the card scripts; random_n(150) is drawn first.
        if (random.Scaled(150) <= PartyQueries.MaxSkill(state, (int)Skill.Healing))
        {
            member[Attribute.Strength]++;
        }
        foreach (var a in new[] { Attribute.Agility, Attribute.Perception, Attribute.Charisma })
        {
            if (random.Scaled(100) <= 10)
            {
                member[a]++;
            }
        }
    }

    // orig: srand (MSC 6 run-time library)
    // DIVERGENCE: srand((unsigned)time(0)) reads the real-time clock; here GameState.ClockSeed supplies it (none: the generator is left alone).
    /// <summary>srand((unsigned)time(0)): the generator restarts from the clock.</summary>
    public static void Reseed(GameState state)
    {
        if (state.ClockSeed is { } seed)
        {
            state.Random.Seed(seed());
        }
    }
}
