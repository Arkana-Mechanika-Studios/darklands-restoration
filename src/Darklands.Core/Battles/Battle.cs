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

    /// <summary>
    /// DS:C21A: the dragon fight: 0 none, <see cref="HydraFight"/> once a file of the seven-headed dragon (picture code M73) was
    /// loaded, <see cref="CaveDragonFight"/> for the cave dragon (M90). Combatant 5 is then the dragon: it breathes every
    /// <see cref="DemoAttackInterval"/>-th blow, picks its own victims and does not take part in the usual enemy turn.
    /// </summary>
    // orig: set by sprite_file_load R014+0x3D2C (0x3F89..0x3FB7), reset by R002+0x1830 (0x1936)
    public byte DemoMode { get; set; }

    /// <summary>DS:C21A of the seven-headed dragon's fight ('I').</summary>
    public const byte HydraFight = 0x49;

    /// <summary>DS:C21A of the cave dragon's fight ('Z').</summary>
    public const byte CaveDragonFight = 0x5A;

    /// <summary>DS:C26E: blows of combatant 5 in a dragon fight (a byte, 1 at the battle's start).</summary>
    public byte DemoAttackCount { get; set; } = 1;

    /// <summary>DS:C21F: every this many blows the dragon breathes instead (3 at the start, 6 once it has picked a fighter).</summary>
    public sbyte DemoAttackInterval { get; set; } = 3;

    /// <summary>
    /// 20A5:763B: the animation code of weapon type 40 for this battle when a dragon fight changed it ("D7" the seven-headed
    /// dragon, "CD" the cave dragon); null: the table's ("DG", which battle_run writes back after every battle).
    /// </summary>
    // orig: written by enemies_place R025+0x0000 (0x0779..0x07AA); "DG" written back by battle_run R002+0x0000 (0x0700) and R037+0x8B76 (0x8CF4)
    public string? DragonWeaponCode { get; set; }

    /// <summary>Weapon type 40, whose animation code a dragon fight changes.</summary>
    public const int DragonWeapon = 40;

    /// <summary>The two letters of a weapon type's animations as they stand in this battle.</summary>
    public string AnimationCode(int weapon) => weapon == DragonWeapon && DragonWeaponCode is { } code ? code : Tables.AnimationCode(weapon);

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

    /// <summary>DS:9084: leave right after the victory (set by a dragon fight, or the death of an enemy that ends the battle).</summary>
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
