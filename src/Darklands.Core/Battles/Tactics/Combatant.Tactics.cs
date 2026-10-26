using Darklands.Core.Battles.Tactics;

namespace Darklands.Core.Battles;

/// <summary>What a combatant's current action has reached (<c>cbt_action</c>, DS:CA64).</summary>
public enum ActionState : byte
{
    Idle = 0,

    /// <summary>A swing, a shot being drawn, or a spell being started.</summary>
    Swinging = 1,

    /// <summary>A knock-back exchange (pusher and pushed).</summary>
    KnockBack = 3,

    /// <summary>Ready: may attack again once its engage time has passed.</summary>
    Ready = 7,

    Reloading = 9,
    Aiming = 10,
    Chanting = 11,
    Casting = 12,

    /// <summary>Finished or cancelled: reset to idle on the next pass.</summary>
    Finished = 13,
}

/// <summary>The melee reaction state (<c>cbt_anim</c>, DS:CA57): the outcome of the last swing, or a push.</summary>
public enum Exchange : byte
{
    None = 0,
    LightHit = 1,
    HeavyHit = 2,
    PushedBack = 3,
    PusherWaiting = 4,
    PusherFollowing = 5,
    Parried = 6,

    /// <summary>Set for freshly placed enemies (enemies_place).</summary>
    Placed = 7,

    AfterPush = 0x0E,
}

/// <summary>An action at a place ordered from the menu (<c>cbt_special</c>, DS:A6C4).</summary>
public enum PlaceAction : byte
{
    None = 0,
    UseDoor = 1,
    Stairs = 2,
    PickDoorLock = 3,

    /// <summary>4: disarm the chest's trap (chest_disarm; on failure it goes off).</summary>
    DisarmChestTrap = 4,
    OpenChest = 5,
    PickChestLock = 6,

    /// <summary>7: Eater-Water devours the lock (the item is used up), then the door or chest opens.</summary>
    DissolveLock = 7,
    Leave = 8,
    TakeLoot = 9,
    Halt = 10,
}

/// <summary>The animation a combatant shows (record +0x18; −1 forces a reload).</summary>
public enum Pose : sbyte
{
    Reload = -1,
    Fighting = 2,
    Hit = 3,
    Dying = 4,
    Walking = 5,
}

/// <summary>cbt_flags (DS:A696 word per combatant).</summary>
[Flags]
public enum TacticalFlags : ushort
{
    None = 0,

    /// <summary>Stands on stairs (exit record in <see cref="Combatant.StairsExit"/>).</summary>
    OnStairs = 0x01,

    /// <summary>Stands in a doorway.</summary>
    InDoorway = 0x02,

    /// <summary>Stands at a chest or lock region.</summary>
    AtChest = 0x04,

    /// <summary>Entered or left a place this tick.</summary>
    PlaceChanged = 0x08,

    /// <summary>Walks without stopping to fight (letter F); cleared on arrival.</summary>
    Hurry = 0x10,

    /// <summary>Stands in an exit zone.</summary>
    InExitZone = 0x20,

    /// <summary>Left through an exit.</summary>
    LeftField = 0x40,

    /// <summary>The death sound was played (the corpse is processed on the next tick).</summary>
    DeathSounded = 0x80,

    /// <summary>The corpse has been processed.</summary>
    CorpseDone = 0x100,

    /// <summary>A loosed shot was cancelled (no arrow this time).</summary>
    ShotSkipped = 0x200,

    /// <summary>The enemy found no target or path: it waits for its turn in the rotation.</summary>
    NoTarget = 0x400,

    /// <summary>The order letter F (bit 12); also keeps the AI from engaging on its own.</summary>
    Following = 0x1000,

    /// <summary>In the fight (R018 0FC2): the bigger combat box applies...</summary>
    InFight = 0x2000,

    /// <summary>...and fitted.</summary>
    CombatBox = 0x4000,
}

/// <summary>The tactical state of a combatant: the battle's parallel per-combatant tables (R014 0E76 / 1DB7, R018).</summary>
public sealed partial class Combatant
{
    public const int PathLength = 250;

    /// <summary>+0x18.</summary>
    public Pose Pose { get; set; } = Pose.Walking;

    /// <summary>+0x1B: frame index into <see cref="Sequence"/> (−1 before the first).</summary>
    public int Frame { get; set; }

    /// <summary>+0x1E / +0x1F: clock ticks between steps and between animation frames (always 8).</summary>
    public int StepDelay { get; set; } = 8;

    public int FrameDelay { get; set; } = 8;

    public ActionState State { get; set; }

    public Exchange Exchange { get; set; }

    public PlaceAction Place { get; set; }

    /// <summary>DS:CA30: the path byte to take this tick, −1 none.</summary>
    public int QueuedStep { get; set; } = -1;

    /// <summary>cbt_paths[i] (far 20A5:6238 + i·250) and DS:D6FB, the index of the next byte.</summary>
    // orig: neither is cleared when a battle starts (R002+0x1830 resets the other per-combatant tables): zeros in a fresh program, else
    // what the last battle or the last load's battle file left (TacticalBattle.StalePaths); a walk ordered without a path given
    // (Flee, then Space) follows those bytes (lockstep session gen-bheld-22)
    public byte[] Path { get; } = new byte[PathLength];

    public int PathIndex { get; set; }

    /// <summary>The clock times of the next step (DS:D9CA), the next frame (DS:D9B0), the next swing (cbt_engage_time), the next re-path (DS:D97C).</summary>
    public ushort NextStepAt { get; set; }

    public ushort NextFrameAt { get; set; }

    public ushort EngageAt { get; set; }

    public ushort RepathAt { get; set; }

    /// <summary>Missiles: reload start (DS:D948), aim start (DS:DABD), aim end (DS:DAA3). Spells: chant end (DS:D948).</summary>
    public ushort ReloadStartAt { get; set; }

    public ushort AimStartAt { get; set; }

    public ushort AimEndAt { get; set; }

    /// <summary>cbt_giveup_timeout (DS:D962) and the give-up flag (DS:D5E9) of the moved-target follow.</summary>
    public ushort GiveUpUntil { get; set; }

    public bool GaveUp { get; set; }

    /// <summary>DS:DA55: next endurance recovery.</summary>
    public ushort RecoverAt { get; set; }

    /// <summary>DS:D4E5 / DS:DA6F: the damage number shown and until when.</summary>
    public int DamageNumber { get; set; }

    public ushort DamageNumberUntil { get; set; }

    /// <summary>cbt_was_hit (DS:D4F2).</summary>
    public bool WasHit { get; set; }

    /// <summary>cbt_anim_seq / cbt_anim_len: the frames played and how many.</summary>
    public int[] Sequence { get; } = new int[10];

    public int SequenceLength { get; set; } = 1;

    /// <summary>DS:E05F / DS:E052 / DS:E06C: the reload/chant loop set up, restart the loop, the aim set up.</summary>
    public bool LoopSetUp { get; set; }

    public bool RestartLoop { get; set; }

    public bool AimSetUp { get; set; }

    /// <summary>DS:CA1C: steps to wait.</summary>
    public int WaitSteps { get; set; }

    /// <summary>DS:D5F6: what blocked the last step (combatant bits).</summary>
    public int BlockedBy { get; set; }

    /// <summary>DS:CA9A push direction, DS:CAA7 the pusher, DS:D41B / DS:D1CE push step counters, DS:D3FE the facing when the attack began.</summary>
    public int PushDirection { get; set; }

    public int Pusher { get; set; }

    public int PushSteps { get; set; }

    public int FollowSteps { get; set; }

    public int AttackFacing { get; set; }

    public TacticalFlags Flags { get; set; }

    /// <summary>order_queues[i][3]: orders taken one by one on arrival.</summary>
    public CombatAction[] Orders { get; } = new CombatAction[3];

    /// <summary>seenpos[i][j] (20A5:79A9): where i last saw j near its path (X = −1 none).</summary>
    public MapPoint[] SeenPositions { get; } = [.. Enumerable.Repeat(new MapPoint(-1, -1), Count)];

    /// <summary>DS:D645 / DS:D664: where it was placed.</summary>
    public MapPoint Home { get; set; }

    /// <summary>DS:A532[i].f9: held by an effect (R019 modifiers).</summary>
    public bool Held => Modifiers.Held;

    /// <summary>DS:D5C6 / DS:20EE[kind]: how many facings an enemy watches (7 all round) and which.</summary>
    public int WatchedFacingCount { get; set; } = 7;

    public int[] WatchedFacings { get; } = new int[6];

    /// <summary>DS:C1F3: killing it ends the battle.</summary>
    public bool EndsBattleWhenKilled { get; set; }

    /// <summary>DS:DA36: the AI made it fight on its own. What reads it is not established.</summary>
    public bool EngagedByAi { get; set; }

    /// <summary>DS:C200: the enemy's animation identity (struct mon +0x02).</summary>
    public int Appearance { get; set; } = -1;

    /// <summary>The ENEMYPAL.DAT records of its kind (.enm +0xA7; −1 none): the screen applies them when it is placed.</summary>
    public sbyte[] Palettes { get; } = [-1, -1, -1];

    /// <summary>DS:D680: the other enemy slot a missile user moves to for its melee look (−1 none); kept from the slot's last occupant otherwise.</summary>
    public int PreferredSlot { get; set; }

    /// <summary>Spells: charges and power per spell type (DS:D37C / DS:D411), charges left (DS:D1D7), the spell being cast (DS:CA0C, DS:C97A).</summary>
    public int[] SpellCharges { get; } = new int[10];

    public int[] SpellPowers { get; } = new int[10];

    public int SpellsLeft { get; set; }

    public int Spell { get; set; }

    public int SpellPower { get; set; }

    /// <summary>DS:C77E / DS:C8B2: the stairs record it stands on / takes; DS:C64C door; DS:C784 chest; DS:C7FE exit zone; DS:C81C loot pile.</summary>
    public int StairsExit { get; set; } = -1;

    public int Door { get; set; } = -1;

    public int Chest { get; set; } = -1;

    public int ExitZone { get; set; } = -1;

    public int LootPile { get; set; } = -1;

    /// <summary>DS:C6EF: the side of its door cell the door is on (0 north, 1 east, 2 south, 3 west); DS:C678: using it opens it (it is shut).</summary>
    public int DoorSide { get; set; }

    public bool DoorOpens { get; set; }

    /// <summary>DS:C7CD: the pack slot of the weakest Eater-Water (item 99), −1 none.</summary>
    public int DissolveSlot { get; set; } = -1;

    /// <summary>DS:C8B2: the stairs record on its new level that leads back (take_exit).</summary>
    // C: a byte array never cleared (BSS): 0 at the start, then whatever the last stairs left (stale between battles).
    public int StairsBack { get; set; }

    /// <summary>DS:C1C1: the base load of the kind last placed in this slot (written by the placement, read by nothing but the battle file).</summary>
    public sbyte PlacedBaseLoad { get; set; }

    /// <summary>DS:D62B: a word per combatant the battle file saves and restores (nothing else reads or writes it).</summary>
    public short SavedWord { get; set; }

    /// <summary>The members selected in order mode (cbt_selected).</summary>
    public bool Selected { get; set; }

    /// <summary>Clears the order queue (order_queue_clear).</summary>
    // orig: R014+0x3D0C (0E76:3D0C) draft (byte-matched per the draft notes)
    public void ClearOrders() => Array.Clear(Orders);

    /// <summary>Queues an order in the first free slot (dropped when full).</summary>
    // orig: order_queue R014+0x3C86 (0E76:3C86) draft (byte-matched per the draft notes)
    public void QueueOrder(CombatAction order)
    {
        var k = Array.IndexOf(Orders, CombatAction.None);
        if (k >= 0)
        {
            Orders[k] = order;
        }
    }

    /// <summary>Takes the next queued order as the current action.</summary>
    // orig: R014+0x3CC4 (0E76:3CC4) draft (byte-matched per the draft notes)
    public void TakeNextOrder()
    {
        Action = Orders[0];
        Orders[0] = Orders[1];
        Orders[1] = Orders[2];
        Orders[2] = CombatAction.None;
    }

}
