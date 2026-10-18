using Darklands.Core.Randomness;

namespace Darklands.Core.Battles;

/// <summary>
/// A battle in progress: the 13 combatant slots, the tables they fight with, who can reach
/// whom, whether the fight is for real, and the battle's result.
/// </summary>
public sealed class Battle
{
    public Battle(CombatTables tables, IRandomSource random)
    {
        Tables = tables;
        Random = random;
        Combatants = [.. Enumerable.Range(0, Combatant.Count).Select(i => new Combatant(i))];
    }

    public CombatTables Tables { get; }

    public IRandomSource Random { get; }

    public Combatant[] Combatants { get; }

    public Combatant this[int index] => Combatants[index];

    /// <summary>
    /// DS:7900[target][attacker]: the attacker stands next to the target and can strike it
    /// (kept up to date by the movement code).
    /// </summary>
    // orig: DS:7900 (R136+0x7900, 13 × 13)
    public Tactics.ReachTable Reaches { get; } = new();

    /// <summary>real_combat (DS:A6C1): 1 once the enemies are engaged; 0 in a non-lethal fight, where nobody falls.</summary>
    public bool RealCombat { get; set; }

    /// <summary>The spell and potion clouds on the field (R019 effects; the tactical battle sets itself).</summary>
    public IEffectClouds Clouds { get; set; } = NoEffectClouds.Instance;

    /// <summary>Which original bugs are kept.</summary>
    public OriginalBehaviour Behaviour { get; set; } = OriginalBehaviour.Faithful;

    /// <summary>demo_mode (DS:C21A): the tutorial/demonstration fight; never set in a normal game.</summary>
    public bool DemoMode { get; set; }

    /// <summary>DS:9F1F: in demo mode, −1 makes combatant 5's attacks miss.</summary>
    public int DemoMissFlag { get; set; }

    /// <summary>DS:C26E: attacks made by combatant 5 in demo mode.</summary>
    public int DemoAttackCount { get; set; }

    /// <summary>battle_result (DS:A893): −1 while fighting.</summary>
    public BattleResult Result { get; set; } = BattleResult.Running;

    /// <summary>battle_args (DS:CA3D): the card <c>battle_run</c> returns (the level's card for the result).</summary>
    public int ResultCard { get; set; }

    /// <summary>combat_clock (DS:07D4): clock ticks (rebased at 50000).</summary>
    public ushort Clock { get; set; }

    /// <summary>DS:DA45: when the end of the battle is acted on (results 0, 3, 5 wait a while).</summary>
    public ushort EndTime { get; set; }

    /// <summary>DS:DA4D: when a timed exit (result 7) ends the battle.</summary>
    public ushort TimedExitTime { get; set; }

    /// <summary>floor_shown (DS:11CA): the level on screen.</summary>
    public int FloorShown { get; set; }

    /// <summary>party_floor (DS:C623).</summary>
    public int PartyFloor { get; set; }

    /// <summary>DS:C651: the enemies' floor as the end-of-battle test (or the surrender) last found it.</summary>
    public int EnemyFloor { get; set; }

    /// <summary>leader (DS:907B) and the active member DS:17B0 the keys move.</summary>
    public int Leader { get; set; }

    public int ActiveMember { get; set; }

    /// <summary>
    /// combat_flags bit 0: the battlefield persists (a location's battlefield, or any battle
    /// started from the main loop): winning does not end it; the party leaves through an exit.
    /// </summary>
    public bool Persistent { get; set; }

    /// <summary>DS:9084: leave right after the victory (set by the demonstration fight).</summary>
    public bool LeaveAfterVictory { get; set; }

    /// <summary>The card numbers of the level for each way of leaving it (floor image +0x1F42..+0x1F4A).</summary>
    public LevelExitCards ExitCards { get; set; }

    /// <summary>game_difficulty: 0 basic, 1 standard, 2 expert.</summary>
    public int Difficulty { get; set; } = 1;

    /// <summary>combat_alarm (DS:D6F4): real combat starts when it goes off.</summary>
    public int Alarm { get; set; }

    /// <summary><c>rand_1_n</c>: 1..n (n below 2 counts as 2).</summary>
    // orig: rand_1_n root 0000:0C8C (asm)
    public int RollOneTo(int n) => Random.Below(n) + 1;

    /// <summary><c>random_n</c>: 0..n−1 by scaling.</summary>
    // orig: random_n root 0410:0008 (matched)
    public int RollScaled(int n) => Random.Scaled(n);
}

/// <summary>The clouds of spells and potions that block missiles and hamper shooters.</summary>
public interface IEffectClouds
{
    /// <summary><c>cloud_blocks_line</c> (R014 1EC9:0142): the line from a to t crosses a cloud.</summary>
    bool LineCrossesCloud(int attacker, int target);

    /// <summary>R014 0E76:469E per cloud: combatant i stands inside one of the clouds (counted in order; the original stops at the first).</summary>
    bool InsideACloud(int combatant);
}

/// <summary>No clouds (a battle without its tactical part, as the rule oracles run it).</summary>
public sealed class NoEffectClouds : IEffectClouds
{
    public static readonly NoEffectClouds Instance = new();

    public bool LineCrossesCloud(int attacker, int target) => false;

    public bool InsideACloud(int combatant) => false;
}
