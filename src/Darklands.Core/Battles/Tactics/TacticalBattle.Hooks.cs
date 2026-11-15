using Darklands.Core.Battles.Generation;
using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

// The tactical battle: the debugging hooks the oracle and the tester set (all null in play), in one place.
public sealed partial class TacticalBattle
{
    /// <summary>A debugging aid: called where a tick's phases begin (-1 the scheduler, -2 the melee exchanges, or earlier: after a rethink's path search in the scheduler; -3 after the activations' test; -6 a tick found not due), as MscRandom.BeforeDraw is before a draw
    /// (tools/oracle/battle_oracle.py gives the C# the clock the original's long passes had reached there, DARKLANDS_BATTLE_CLOCK_JUMPS).</summary>
    [field: ThreadStatic]
    public static Action<int>? ClockPhase { get; set; }

    /// <summary>A debugging aid for the oracle (null in play): called before the endurance regain reads the clock for a member (its slot),
    /// where tools/oracle/timer_marks.py found the original's timer callbacks inside the loop (DARKLANDS_BATTLE_REGAIN_RISES).</summary>
    [field: ThreadStatic]
    public static Action<int>? RegainRead { get; set; }

    /// <summary>A debugging aid: each combatant's step call, as tools/oracle/lockstep.py compares it; null in play (the headless host
    /// sets it from DARKLANDS_STEP_TRACE).</summary>
    public static Action<string>? StepTrace { get; set; }

    /// <summary>A debugging aid: the destination a record keeps when its enemy is first placed,
    /// by record. The original copies the enemy file's record whole (R025+0x0267) and its destination bytes (+0x08..+0x0B) are whatever the
    /// work buffer DS:A895 held when R020+0x0000 converted the level (CombatantSetup.FromEnemy); until the enemy first moves they steer what reads them (the facing of a spell at a spot, R014+0x306A). The oracle reads
    /// them off the original's run; the C# leaves them 0. The headless host sets it from DARKLANDS_BATTLE_STALE_DEST through
    /// <see cref="ParseStaleDestinations"/>.</summary>
    public static Dictionary<int, MapPoint>? StaleDestinations { get; set; }

    /// <summary>The oracle's stale destinations by record, "5:-256,511;7:..."; null for an empty or missing text.</summary>
    public static Dictionary<int, MapPoint>? ParseStaleDestinations(string? text) => string.IsNullOrEmpty(text) ? null
        : text.Split(';').Select(p => p.Split(':', ',')).ToDictionary(
            p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p => new MapPoint(int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)));
}
