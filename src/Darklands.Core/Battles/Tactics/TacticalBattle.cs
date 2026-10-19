using Darklands.Core.Battles.Generation;
using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

/// <summary>What the tactical battle tells the screen (and where the screen looks: the renderer's random draws depend on it).</summary>
public interface ITacticalView : IBattleViewport
{
    /// <summary>index_of_word(R136+0x5890, cell, 0x8C): the screen tile (0..0x8B) that shows the cell (column &lt;&lt; 8 | row) in the
    /// last full redraw, 0x8C when none.</summary>
    int ScreenTileOf(int cell);

    /// <summary>Plays sound effect <paramref name="n"/> (R014 0E76:465E decides whether it is heard).</summary>
    void Sound(int n);

    /// <summary><c>show_message</c>: the string at DS:<paramref name="text"/> for <paramref name="ticks"/> clock ticks.</summary>
    void Message(int text, int ticks);

    /// <summary>A message about a member: its name, then the string at DS:<paramref name="text"/> (" finds a secret door").</summary>
    void Message(int text, int ticks, int member);

    /// <summary>
    /// A message in member <paramref name="member"/>'s panel row (−2: the whole column) in colour <paramref name="color"/>
    /// until a key or a left click (the skill changes at the end of a fight).
    /// </summary>
    void MemberMessage(int member, string text, int color);

    /// <summary>The panel row of a member (or everything when −1) needs redrawing.</summary>
    void RowChanged(int member);

    /// <summary>The level on screen changed (doors, furniture, floor switch: floor_view_draw(−1)).</summary>
    void MapChanged();

    /// <summary>
    /// The varying furniture: record <paramref name="record"/> of the tile set <paramref name="set"/> becomes furniture tile
    /// <paramref name="tile"/>; its shape, or null when nothing changed.
    /// </summary>
    FurnitureShape? VaryFurniture(int tile, string set, int record);

    /// <summary>
    /// <c>loot_screen</c>: the loot screen over a work list (a pile's block, the gathered loot or a chest's contents) and some
    /// money; what the party leaves stays in the list.
    /// </summary>
    void OpenLoot(int member, List<ItemStack> list, Economy.Money money);
}

/// <summary>
/// The tactical battle (overlay R014 and its children R016, R018, R024, R025): the real-time
/// loop that runs one combat tick every 8 clock ticks, the per-combatant action state machine,
/// movement, the AI, the enemies' turn, missiles and placement. The screen feeds the clock and
/// the player's orders and draws the state; the rules here never look at the screen.
/// </summary>
// orig: combat_main R014+0x0000 (0E76:0000) draft (spec D §1.2, asm-verified)
public sealed partial class TacticalBattle : IBattleEndActions
{
    /// <summary>Clock ticks between combat ticks.</summary>
    public const int TickLength = 8;

    /// <summary>DS:179C: sprite facing of a packed path direction (0..7 same, 8 → 1, 9 → 5).</summary>
    private static readonly int[] FacingOfDirection = [0, 1, 2, 3, 4, 5, 6, 7, 1, 5];

    /// <summary>DS:17A6: the facing of a push direction.</summary>
    private static readonly int[] PushFacing = [4, 5, 6, 7, 0, 1, 2, 3, 5, 1];

    /// <summary>DS:17B2: facing toward a point by quadrant and slope class.</summary>
    private static readonly int[,] FacingToward = { { 2, 1, 0 }, { 2, 3, 4 }, { 6, 5, 4 }, { 6, 7, 0 } };

    /// <summary>DS:11DE: the speed masks, rotated once per tick (8 − s bits of 8 set); [8], [9] stay 0.</summary>
    private readonly byte[] _speedMasks = [0xFF, 0xFE, 0xEE, 0xB6, 0xAA, 0x94, 0x88, 0x80, 0, 0];

    private readonly IReadOnlyList<BattlefieldLevel> _levels;
    private readonly IAnimationLibrary _library;
    private readonly ITacticalView _view;

    public TacticalBattle(Battle battle, GeneratedBattlefield field, IReadOnlyList<FurnitureShape?> furniture, IAnimationLibrary library, ITacticalView view)
    {
        Battle = battle;
        Field = field;
        _levels = field.Levels;
        _library = library;
        _view = view;
        Map = new BattleMap(battle, _levels[Math.Clamp(field.StartLevel, 0, _levels.Count - 1)], furniture, field.Terrain.FirstSightBlockingFurniture);
        Paths = new PathFinder(Map);
        Paths.UseHalfSizes(k => battle[k].HalfWidth, k => battle[k].HalfHeight);
        Effects = new EffectField(battle, Map, n => _view.Sound(n), ReadyWeapon, slot => SlotWeapons[slot], () => Paused) { Terrain = field.Terrain };
        Effects.Landed += () => DelayedStart = false;
        battle.Clouds = Effects;
        for (var k = 0; k < Slots.Length; k++)
        {
            Slots[k] = new AnimationSlot();
        }
        // The encounters of each level are the battlefield's own lists (the .atv files): those met are removed for good.
        for (var level = 0; level < BattlefieldForces.MaxLevels; level++)
        {
            Activations[level] = field.Forces.Groups[level] ?? [];
        }
    }

    public Battle Battle { get; }

    /// <summary>The effects on the field (R019).</summary>
    public EffectField Effects { get; }

    public GeneratedBattlefield Field { get; }

    public BattleMap Map { get; }

    public PathFinder Paths { get; }

    /// <summary>The animation sets: 0..4 the party, 5/6 the two enemy kinds walking, 7/8 fighting, 9/10 dying.</summary>
    public AnimationSlot[] Slots { get; } = new AnimationSlot[11];

    /// <summary>DS:1166[slot]: the picture code of the set ("F60", "E02"); anim_file[slot]: the weapon code ("SW", "PT" when casting).</summary>
    public string[] SlotCodes { get; } = [.. Enumerable.Repeat("", 7)];

    public string[] SlotWeapons { get; } = [.. Enumerable.Repeat("", 7)];

    /// <summary>enemy_anim_slot (DS:D67C) / DS:D67E: the enemy identity each enemy slot holds (−1 free).</summary>
    public int[] EnemySlotIdentity { get; } = [-1, -1];

    /// <summary>The level shown.</summary>
    public BattlefieldLevel Level => Map.Level;

    public BattlefieldLevel LevelAt(int floor) => _levels[floor];

    public int LevelCount => _levels.Count;

    /// <summary>The hidden encounters of each level (the .atv activation records; X &lt; 0 disabled).</summary>
    public List<EnemyGroup>[] Activations { get; } = new List<EnemyGroup>[BattlefieldForces.MaxLevels];

    public List<LootPile> LootPiles => Store.Loot.Piles;

    /// <summary>R136+0xE4E0: the money the encounters carried, for the loot.</summary>
    public Money Purse { get; set; }

    /// <summary>DS:07D1: timer callbacks since the last combat tick (the screen counts them, paused or not).</summary>
    public int TimerCallbacks { get; set; }

    /// <summary>ai_paused (DS:A695): order mode; the AI, melee and actions are frozen.</summary>
    public bool Paused { get; private set; }

    /// <summary>move_mode (DS:C102): 0 individually, 1 as a group, 2 single file.</summary>
    public int MoveMode { get; private set; }

    /// <summary>marching_order (DS:C624) and its length (DS:C629).</summary>
    public List<int> MarchingOrder { get; } = [];

    /// <summary>DS:E092: combat ticks run.</summary>
    public int Ticks { get; private set; }

    /// <summary>DS:D9E4: when the next combat tick is due.</summary>
    public ushort NextTickAt { get; private set; }

    /// <summary>DS:DA4F: the next activation check / enemy turn; DS:DA49 the combat-box retry; DS:DA47 the alarm; DS:C2BB the delayed start.</summary>
    public ushort ActivationsAt { get; set; }

    public ushort CombatBoxRetryAt { get; set; }

    public ushort AlarmAt { get; set; }

    public ushort DelayedStartAt { get; set; }

    /// <summary>DS:D842: the enemies wait for <see cref="DelayedStartAt"/> (combat_flags 0x40).</summary>
    public bool DelayedStart { get; set; }

    /// <summary>DS:D6F0: hidden encounters are checked (after the initial placement).</summary>
    public bool ActivationsEnabled { get; private set; }

    /// <summary>DS:D6EB / DS:D6EC: reinforcement waves on, enemies killed in this wave; R136+0xE526 the wave group.</summary>
    public bool WavesEnabled { get; private set; }

    public int WaveKills { get; private set; }

    public EnemyGroup? WaveGroup { get; private set; }

    /// <summary>DS:E079: a member was moved by the keys this pass.</summary>
    public bool KeyMoved { get; set; }

    /// <summary>DS:E07B: the map needs redrawing after the action pass.</summary>
    private bool _redraw;

    /// <summary>DS:E237: the rotating enemy that may rethink this turn.</summary>
    private int _turnRotation;

    /// <summary>
    /// DS:D6F1 / DS:D6ED: the member whose order mode the screen opens in the frame it draws after a combat tick, once the member has a
    /// hot spot there (−1 none).
    /// </summary>
    // orig: combat_tick R014+0x0F58 (0E76:0F58) draft (the DS:D6F1 test opens order mode; spec D §1.3, §7.2)
    public int PendingOrderMember { get; set; } = -1;

    /// <summary>A combat tick ran since the screen last drew after one: the screen's next frame is the tick's combat_draw.</summary>
    private bool _tickToDraw;

    /// <summary>Whether a combat tick ran since the last call (the screen asks once per frame, after drawing it).</summary>
    public bool TakeTickToDraw()
    {
        var ran = _tickToDraw;
        _tickToDraw = false;
        return ran;
    }

    /// <summary>DS:D610: the party may move as a group (not in real combat and all on one floor).</summary>
    public bool GroupMoveAllowed { get; private set; }

    /// <summary>DS:C3CD: where the enemies already in records 5..12 are placed (spot x, y, direction).</summary>
    public (MapPoint Position, int Direction) InitialEnemySpot { get; set; }

    /// <summary>DS:C3CD as a whole: the 14-byte stairs record the spot was taken from.</summary>
    public byte[] InitialSpotRecord { get; } = new byte[14];

    /// <summary>The animation set index of combatant i (cbt_anim_set, 14F7:343C).</summary>
    // orig: cbt_anim_set R014+0x9C4C (14F7:343C) draft
    // UNCERTAIN: read from the slot rules of cbt_set_anim (spec D §3.9): enemies use slot +2 fighting/hit and +4 dying.
    public int AnimationSet(int i)
    {
        var c = Battle[i];
        var kind = c.AnimationSlot;
        if (kind < Combatant.PartySlots)
        {
            return kind;
        }
        return c.Pose switch
        {
            Pose.Fighting or Pose.Hit => kind + 2,
            Pose.Dying => kind + 4,
            _ => kind,
        };
    }

    private AnimationSlot SetOf(int i) => Slots[AnimationSet(i)];

    private ushort Clock => Battle.Clock;

    /// <summary>
    /// Mode 1: the members stand where the last battle on this battlefield left them (their records keep x, y and both facings).
    /// </summary>
    // DIVERGENCE: a member who was not in that battle has whatever bytes the record held in the original; here such members are
    // placed in the level's start formation (the others then put back where they stood).
    private void KeepPlaces(IReadOnlyList<(MapPoint Position, int Facing, int SpriteFacing)?> kept)
    {
        var missing = false;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            missing |= Battle[i].Status != CharacterStatus.Empty && (i >= kept.Count || kept[i] is null);
        }
        if (missing)
        {
            PlaceParty(Level.PartyStart.X, Level.PartyStart.Y, Level.PartyFacing);
        }
        for (var i = 0; i < Combatant.PartySlots && i < kept.Count; i++)
        {
            if (kept[i] is { } place && Battle[i].Status != CharacterStatus.Empty)
            {
                Battle[i].Position = place.Position;
                Battle[i].Facing = place.Facing;
                Battle[i].SpriteFacing = place.SpriteFacing;
            }
        }
    }

    private int Scaled(int n) => Battle.RollScaled(n);

    /// <summary>
    /// The start of the fight (combat_main's initialisation): the party placed at the level's start
    /// (effect_start), the timers cleared, weapons readied, the first enemy turn.
    /// </summary>
    /// <param name="arrivedThroughDoor">combat_flags 0x20.</param>
    /// <param name="delayedStart">combat_flags 0x40: the enemies wait 600 ticks.</param>
    // orig: combat_main R014+0x0000 (0E76:0000) draft (spec D §1.2 steps 1..14, asm-verified)
    // C: step 2 (R014+0x0017, mode 0's combat_flags 0x10: the levels prepared, the party at the level's start) is PrepareLevels with the
    // placement below; steps 7 (R014+0x0110: the alarm trigger's furniture or wall removed) and 8 (R014+0x0153: an activation re-enabled)
    // act on the battlefield kept from the earlier visit (its levels stay in memory, GeneratedBattlefield.Store); the "battlefield save
    // rules" message (step 14) is the screen's.
    /// <param name="combatFlags">combat_flags from the card that ordered the battle: 8 re-enables the first disabled activation, 0x80
    /// re-enables it after keeping a disabled copy at the end.</param>
    /// <param name="keptPlaces">Battle mode 1: the party slots' places when the last battle on this battlefield ended (null: mode 0, 2 or 3).</param>
    public void Start(bool arrivedThroughDoor = false, bool delayedStart = false, int combatFlags = 0,
        IReadOnlyList<(MapPoint Position, int Facing, int SpriteFacing)?>? keptPlaces = null)
    {
        // orig: R002+0x1830 (0E76:1830) draft, the part of the battle's reset a battle taken up from a saved game skips (DS:9081 bit 0):
        // DS:D6F0 = 1 (hidden encounters are checked from the start), the alarm DS:D6F4, real combat DS:A6C1, DS:C600 and DS:CA29 = 0,
        // the result card DS:CA3D = -1.
        ActivationsEnabled = true;
        Battle.Alarm = 0;
        Battle.RealCombat = false;
        Map.IgnorePeople = false;
        _stopped = false;
        Battle.ResultCard = -1;
        ResetDragonFight();
        Ticks = 0;
        InitMenus();
        RebuildMarchingOrder();
        // C: combat flag 0x10 (a new battlefield) is set by mode 0 of R002+0x0000; its levels are not prepared yet.
        var newBattlefield = !Store.Prepared;
        PrepareLevels();
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            Battle[i].Floor = Battle.FloorShown;
        }
        Battle.PartyFloor = Battle.FloorShown;
        SwitchLevel(Battle.FloorShown);
        if (arrivedThroughDoor && Level.PartyRoom >= 0)
        {
            // C: arrived through a door: its wall group is remembered as open (doors_opened) and its cells uncovered.
            RevealGroup(Level.PartyRoom);
            _redraw = true;
        }
        // combat_flags 0x10 (R024 0000, mode 0) and 0x20 (R014+0x0058, modes 2 and 3) place the party at the level's start before the
        // timers are cleared; mode 1 (combat_flags 2) places nobody: each member stands where the last battle left it.
        // orig: R002+0x0098..0x00EE (R002+0x0000's modes) and R014+0x0058..0x0104, checked against the asm; tests: RevisitTests
        if (keptPlaces is null)
        {
            PlaceParty(Level.PartyStart.X, Level.PartyStart.Y, Level.PartyFacing);
        }
        else
        {
            KeepPlaces(keptPlaces);
        }
        // combat_flags 4: the fight was started by an alarm trigger: its furniture or wall goes now.
        // C: R014+0x0110 has no test of the box's x (−1 after the last battle): a trigger at x −1 would match.
        for (var k = 0; (combatFlags & 4) == 4 && k < (sbyte)Level.GetByte(TriggerCountAt); k++)
        {
            var (x, y, w, h) = Box(TriggersAt + 14 * k);
            if ((x, y, w, h) == AlarmTrigger)
            {
                var handler = (TriggerHandler)Level.GetWord(TriggersAt + 14 * k + 6);
                if (handler == TriggerHandler.FurnitureAlarm)
                {
                    RemoveFurniture((byte)Level.GetWord(TriggersAt + 14 * k + 10), k);
                }
                else if (handler == TriggerHandler.WallAlarm)
                {
                    RemoveWall((byte)Level.GetWord(TriggersAt + 14 * k + 10), k);
                }
                break;
            }
        }
        // C: only DS:C925 (the x) is reset; the y, width and height keep their values
        AlarmTrigger = AlarmTrigger with { X = -1 };
        var activations = Activations[Battle.FloorShown];
        if ((combatFlags & 8) == 8)
        {
            var k = activations.FindIndex(g => g.X < 0);
            if (k >= 0)
            {
                activations[k].X = (short)-activations[k].X;
            }
        }
        RepeatActivation(combatFlags);
        ClearTimers();
        if (delayedStart)
        {
            DelayedStartAt = (ushort)(Clock + 600);
            DelayedStart = true;
        }
        CheckActivations();
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            c.ClearOrders();
            if (c.Status != CharacterStatus.Empty && (i < Combatant.PartySlots || c.EnemyClass == 0))
            {
                ReadyWeapon(i, c.DefaultWeapon, 0);
            }
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            c.HalfWidth = Slots[i].HalfWidth;
            c.HalfHeight = Slots[i].HalfHeight;
            if (c.Status != CharacterStatus.Empty)
            {
                SetPose(i, Pose.Walking);
            }
        }
        EnemyTurn();
        Battle.Result = BattleResult.Running;
        foreach (var c in Battle.Combatants)
        {
            c.QueuedStep = -1;
        }
        _partyRethinkSlot = 0;
        _enemyRethinkSlot = 4;
        _enemyRethinkInterval = 0x10;
        SelectMember(Battle.Leader);
        // DS:D6F1 = 1, DS:D6ED: the frame drawn after the first tick opens order mode for that member, so the battle begins paused.
        PendingOrderMember = Battle.ActiveMember;
        MoveMode = 0;
        NextTickAt = (ushort)(Clock + TickLength);
        ActivationsAt = (ushort)(Clock + 120);
        SaveRulesMessage(newBattlefield);
    }

    /// <summary>DS:1776 "Battlefield save rules are in effect".</summary>
    public const int SaveRulesText = 0x1776;

    /// <summary>DS:C8B7: the battlefield's floor tiles are the keep's (saving is never allowed there).</summary>
    // orig: R002+0x0C8C (0E76:0C8C) draft: DS:C8B7 is set when the floor tile set is named "keep" (case ignored)
    public bool IsKeep => string.Equals(Field.Terrain.SetName(TileKind.Floor), "keep", StringComparison.OrdinalIgnoreCase);

    /// <summary>The message of a battlefield the game can be saved on, at the start of its first fight (or of a fight taken up again).</summary>
    // orig: R014+0x0000 (0E76:0000) draft: shown for 150 ticks after the start when the battlefield is new (combat flag 0x10) or the
    // fight is taken up again (DS:9081 bit 0), the battlefield persists (combat flag 1) and it is not the keep (DS:C8B7)
    private void SaveRulesMessage(bool shown)
    {
        if (shown && Battle.Persistent && !IsKeep)
        {
            _view.Message(SaveRulesText, 150);
        }
    }

    /// <summary>DS:9920: Save Game is offered on a battlefield that persists, not the keep's, outside real combat (each pass of the loop).</summary>
    // orig: R014+0x0000 (0E76:0000) draft: DS:9920 is 1 (on) when combat flag 1 is set, DS:C8B7 is 0 and DS:A6C1 (real combat) is 0, else 2
    private void OfferSave() => Menus.SaveGame = (byte)(Battle.Persistent && !IsKeep && !Battle.RealCombat ? 1 : 2);

    /// <summary>combat_flags 0x80: the first disabled activation is copied to the end of the list (still disabled) and itself re-enabled.</summary>
    // orig: R014+0x0620 (0E76:0620) matched
    private void RepeatActivation(int combatFlags)
    {
        if ((combatFlags & 0x80) != 0x80)
        {
            return;
        }
        var list = Activations[Battle.FloorShown];
        for (var k = 0; k < list.Count; k++)
        {
            if (list[k].X < 0)
            {
                list.Add(list[k].Clone());
                list[k].X = (short)-list[k].X;
                return;
            }
        }
    }

    /// <summary>combat_state_clear (R020 06B6): the clock and every timer back to 0, step and frame delays 8.</summary>
    // orig: combat_state_clear R020+0x06B6 (1EF8:06B6) draft (byte-matched per the draft notes)
    private void ClearTimers()
    {
        Battle.Clock = 0;
        Battle.TimedExitTime = 0;
        CombatBoxRetryAt = AlarmAt = DelayedStartAt = ActivationsAt = 0;
        _enemyRethinkAt = _partyRethinkAt = 0;
        foreach (var c in Battle.Combatants)
        {
            c.DamageNumberUntil = c.RecoverAt = c.EngageAt = c.NextFrameAt = c.AimEndAt = c.AimStartAt = c.ReloadStartAt = 0;
            c.GiveUpUntil = c.RepathAt = c.NextStepAt = 0;
            c.FlashUntil = 0;
            c.StepDelay = c.FrameDelay = 8;
            // The three modifier timers off (DS:A53E: x, y, z = −536); the modifiers themselves stay.
            c.Modifiers.AttributesUntil = c.Modifiers.SkillsUntil = c.Modifiers.HeldUntil = EffectModifiers.Off;
        }
    }

    /// <summary>
    /// One pass of the main loop (everything but the player's input): the end test, real combat,
    /// clock rebase, endurance recovery, idle frames, the activation check / enemy turn every 120
    /// ticks, the initial enemy placement, then <see cref="Tick"/>. Returns true when the battle is over.
    /// </summary>
    // orig: combat_main R014+0x0000 (0E76:0000) draft (the main loop, spec D §1.2, asm-verified)
    public bool Iterate()
    {
        OfferSave();
        if (BattleEnd.IsOver(Battle, this))
        {
            AfterBattle();
            return true;
        }
        if (Battle.RealCombat && CombatBoxRetryAt < Clock)
        {
            RetryCombatBoxes();
            CombatBoxRetryAt = (ushort)(Clock + 300);
        }
        if (Battle.Alarm == 1 && !Battle.RealCombat && AlarmAt < Clock)
        {
            Battle.Alarm = 0x63;
            Battle.RealCombat = true;
            Menus.Set(OrdersItem.Surrender, 1);
            CombatBoxRetryAt = (ushort)(Clock + 300);
            for (var i = 0; i < Combatant.Count; i++)
            {
                JoinFight(i);
            }
        }
        if (Battle.RealCombat && MoveMode != 0)
        {
            MoveMode = 0;
            _view.Message(TravelIndividuallyText, 60);
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                var c = Battle[i];
                c.Target = -1;
                c.Moving = false;
                c.Action = CombatAction.None;
                c.Exchange = Exchange.None;
            }
        }
        if (Clock > 50000)
        {
            Rebase();
        }
        if (!Battle.RealCombat)
        {
            RecoverEndurance();
        }
        if (!KeyMoved)
        {
            foreach (var c in Battle.Combatants)
            {
                if (c.IsAble && c.Frame != 0 && !c.Moving && c.Pose == Pose.Walking)
                {
                    c.Frame = 0;
                }
            }
        }
        if (ActivationsAt < Clock)
        {
            if (CheckActivations())
            {
                _view.Sound(0x0E);
            }
            EnemyTurn();
        }
        if (!ActivationsEnabled && DelayedStartAt < Clock)
        {
            PlaceInitialEnemies();
        }
        Tick();
        foreach (var c in Battle.Combatants)
        {
            if (c.DamageNumber != 0 && c.DamageNumberUntil < Clock)
            {
                c.DamageNumber = 0;
            }
        }
        KeyMoved = false;
        return false;
    }

    /// <summary>DS:0768 "Party will travel individually".</summary>
    public const int TravelIndividuallyText = 0x0768;

    /// <summary>The clock passed 50000: every timer moves back by 49000.</summary>
    // orig: combat_main R014+0x0000 (0E76:0000) draft (the rebase, spec D §1.2)
    // C: adding 0x4098 to a 16-bit word subtracts 49000 modulo 2^16.
    private void Rebase()
    {
        ushort R(ushort t) => (ushort)(t + 0x4098);
        Battle.Clock = R(Battle.Clock);
        NextTickAt = R(NextTickAt);
        _partyRethinkAt = R(_partyRethinkAt);
        _enemyRethinkAt = R(_enemyRethinkAt);
        ActivationsAt = R(ActivationsAt);
        DelayedStartAt = R(DelayedStartAt);
        Battle.TimedExitTime = R(Battle.TimedExitTime);
        Battle.EndTime = R(Battle.EndTime);
        CombatBoxRetryAt = R(CombatBoxRetryAt);
        AlarmAt = R(AlarmAt);
        foreach (var c in Battle.Combatants)
        {
            c.NextStepAt = R(c.NextStepAt);
            c.NextFrameAt = R(c.NextFrameAt);
            c.EngageAt = R(c.EngageAt);
            c.RepathAt = R(c.RepathAt);
            c.GiveUpUntil = R(c.GiveUpUntil);
            c.ReloadStartAt = R(c.ReloadStartAt);
            c.AimStartAt = R(c.AimStartAt);
            c.AimEndAt = R(c.AimEndAt);
            c.DamageNumberUntil = R(c.DamageNumberUntil);
            c.RecoverAt = R(c.RecoverAt);
            c.FlashUntil = R(c.FlashUntil);
            var m = c.Modifiers;
            // C: the modifier timers are compared with −536 (65000) and only moved when running.
            if (m.AttributesUntil != EffectModifiers.Off)
            {
                m.AttributesUntil = R(m.AttributesUntil);
            }
            if (m.SkillsUntil != EffectModifiers.Off)
            {
                m.SkillsUntil = R(m.SkillsUntil);
            }
            if (m.HeldUntil != EffectModifiers.Off)
            {
                m.HeldUntil = R(m.HeldUntil);
            }
        }
        Effects.Rebase(R);
    }

    /// <summary>One combat tick when due: AI, places, melee exchanges, speed masks, actions.</summary>
    /// <returns>True when a tick ran.</returns>
    // orig: combat_tick R014+0x0F58 (0E76:0F58) draft (spec D §1.3, asm-verified)
    // C: while paused (the clock stopped) a tick still runs every 8 timer callbacks (DS:07D1): it draws (with the renderer's random
    // numbers) and turns the combatants, but the AI, melee and actions stay frozen.
    public bool Tick()
    {
        // C: DS:CA29 (a pit or an alarm stopped the battle): combat_tick returns at once.
        if (_stopped || Clock < NextTickAt && (!Paused || TimerCallbacks < 8))
        {
            return false;
        }
        TimerCallbacks = 0;
        Ticks++;
        GroupMoveAllowed = !Battle.RealCombat;
        var first = Array.FindIndex(Battle.Combatants, 0, Combatant.PartySlots, c => c.IsAble);
        if (first >= 0)
        {
            for (var j = first + 1; j < Combatant.PartySlots; j++)
            {
                if (Battle[j].IsAble && Battle[j].Floor != Battle[first].Floor)
                {
                    GroupMoveAllowed = false;
                    break;
                }
            }
        }
        GroupEntries();
        RemoveArrivedMissiles();
        if (!Paused)
        {
            AiTick();
            PartyLocations();
            MeleeExchanges();
            RotateSpeedMasks();
            CombatActions();
        }
        NextTickAt = (ushort)(Clock + TickLength);
        TurnTowardTargets();
        Effects.Drawn(_view);
        RefreshTargetCursor();
        _tickToDraw = true;
        return true;
    }

    /// <summary>Rotates each speed mask right by one bit.</summary>
    // orig: R014+0x1158 (0E76:1158) draft (inline asm ror)
    private void RotateSpeedMasks()
    {
        for (var k = 0; k < 8; k++)
        {
            _speedMasks[k] = (byte)(_speedMasks[k] >> 1 | _speedMasks[k] << 7);
        }
    }

    /// <summary>+1 endurance every 5/15/30/45 s outside real combat, up to three quarters of the maximum.</summary>
    // orig: endurance_recover R014+0x45AE (0E76:45AE) draft (byte-matched, asm-verified)
    private void RecoverEndurance()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (c.Status is not (CharacterStatus.Able or CharacterStatus.Unconscious) || c.RecoverAt >= Clock)
            {
                continue;
            }
            int m = c.MaximumEndurance, e = c.Endurance;
            if ((m >> 2) + (m >> 1) <= e)
            {
                c.RecoverAt = (ushort)(Clock + 2700);
                continue;
            }
            c.Endurance = (sbyte)(e + 1);
            c.RecoverAt = (ushort)(Clock + (m >> 3 > e ? 300 : m >> 2 > e ? 900 : m >> 1 > e ? 1800 : 2700));
            _view.RowChanged(i);
        }
    }

    /// <summary>After the battle: skill improvement from what was used (unless the party fell into a pit).</summary>
    // orig: R014+0x0694 (0E76:0694) draft (byte-matched per the draft notes)
    // DIVERGENCE: saving the level files belongs to the battlefield store (the levels stay in memory here).
    private void AfterBattle()
    {
        Effects.EndPartyModifiers();
        if (Battle.Result != BattleResult.FellIntoPit)
        {
            // orig: skill_change_msg R021+0x0080 (1EF8:0080) matched: "<skill> changed from X to Y" (msg_buf) shown by
            // R021+0x01E2 (1EF8:01E2) matched in colour 15 (a gain) or 12 (a loss)
            BattleEnd.ImproveSkills(Battle, (m, skill, from, to) =>
                _view.MemberMessage(m, $"{Rules.MemberRules.SkillNames[skill]} changed from {from} to {to}", to > from ? 15 : 12));
        }
        FreeEnemySlots();
    }

    /// <summary>Shows another level (level_switch).</summary>
    // orig: level_switch R024+0x059A (1EF8:059A) draft (outline; the end checked against the asm: `if (DS:D6EE != 0) R024+0x07A2();`)
    // DIVERGENCE: the levels stay in memory (no .flr/.atv files); the hidden wall lists of unopened doors are applied by LevelShown.
    public void SwitchLevel(int floor)
    {
        if (floor < 0 || floor >= _levels.Count)
        {
            return;
        }
        Battle.FloorShown = floor;
        Map.Level = _levels[floor];
        Battle.ExitCards = Map.Level.ExitCards;
        LevelShown();
        // C: DS:D6EE is the varying furniture's buffer, which battle_tiles_load allocates when the terrain's +0xB4 is not 0
        if (Field.Terrain[0xB4] != 0)
        {
            VaryFurniture();
        }
        _view.MapChanged();
    }

    /// <summary>The level's own furniture record (byte 0x33D2 of the set named at 0x33C8) becomes furniture tile byte 0x33D1.</summary>
    // orig: R024+0x07A2 (1EF8:07A2) draft (checked against the asm): for tile t = level[0x33D1] and record r = level[0x33D2] (signed bytes)
    // of "%Fs.frc" (level + 0x33C8): DS:C8D5[t] = the sprite, DS:C12F[t] = +0x20, DS:C11D[t] = +0x21, DS:C907 = 1 when +0x24 == 1, and
    // for k < 4 DS:D7BE / DS:D802 / DS:C13F / DS:C181 [4t + k] = the bytes +2k, +8 + 2k, +0x10 + 2k, +0x18 + 2k
    private void VaryFurniture()
    {
        const int SetNameAt = 0x33C8, TileAt = 0x33D1, RecordAt = 0x33D2;
        var image = Map.Level.Image;
        var end = Array.IndexOf(image, (byte)0, SetNameAt);
        var set = System.Text.Encoding.ASCII.GetString(image, SetNameAt, (end < 0 ? image.Length : end) - SetNameAt);
        int tile = (sbyte)image[TileAt], record = (sbyte)image[RecordAt];
        if (_view.VaryFurniture(tile, set, record) is { } shape && tile >= 0 && tile < Map.Furniture.Count)
        {
            var furniture = Map.Furniture.ToArray();
            furniture[tile] = shape;
            Map.Furniture = furniture;
        }
    }

    // IBattleEndActions (combat_over's calls).

    public void LootAfterFight() => GatherLoot();

    public void OpenLoot(int member) => OpenLootPile(member);

    public void LeftCombatMode()
    {
        Menus.Set(OrdersItem.EnemyInfo, 2);
        Menus.Set(OrdersItem.Surrender, 2);
        // orig: combat_mode_leave R014+0x0D86 (0E76:0D86) draft (spec D §1.6): DS:A3D8[0..12] = 0 (0xDB2), the box bits and walk sizes.
        Array.Clear(_fireBlocked);
        foreach (var c in Battle.Combatants)
        {
            c.Flags &= ~(TacticalFlags.InFight | TacticalFlags.CombatBox);
            var set = Slots[AnimationSet(c.Index)];
            c.HalfWidth = set.HalfWidth;
            c.HalfHeight = set.HalfHeight;
        }
        _view.RowChanged(-1);
    }

    public void PlaceWokenMember(int member) => PlaceWoken(member);

    public void VictoryCleanUp()
    {
        FreeEnemySlots();
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (c.Status != CharacterStatus.Empty)
            {
                c.Moving = false;
                c.State = ActionState.Idle;
                SetPose(i, Pose.Walking);
            }
        }
    }
}
