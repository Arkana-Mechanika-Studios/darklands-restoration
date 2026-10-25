using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:1C36 / DS:1C40: the minimum reach per direction.</summary>
    private static readonly int[] ReachX = [-2, 0, 3, 2, 2, 0, -3, -2, 2, -2];
    private static readonly int[] ReachY = [-2, -2, 0, 2, 2, 2, 0, -2, -2, 2];

    /// <summary>DS:E0AC: who is in reach of the probed combatant (13 furniture, 14 walls).</summary>
    private readonly bool[] _inReach = new bool[16];

    /// <summary>R136+0x7C4D: target preference bytes.</summary>
    private readonly byte[] _preference = new byte[Combatant.Count];

    /// <summary>DS:E1F9: the best cost/distance per combatant (shared with the enemy turn).</summary>
    private readonly (int Cost, int Who)[] _best = new (int, int)[Combatant.Count];

    /// <summary>The scheduler: DS:DA53 / DS:DA51 times, DS:E09C the next member, DS:E09B the next enemy, DS:E09A the enemy interval.</summary>
    private ushort _partyRethinkAt;
    private ushort _enemyRethinkAt;
    private int _partyRethinkSlot;
    private int _enemyRethinkSlot;
    private int _enemyRethinkInterval;

    /// <summary>The AI's part of a tick: who reaches whom, following moved targets, and in real combat engaging, fighting back and rethinking.</summary>
    // orig: ai_tick R014+0xF418 (1DB7:0008) draft (asm-verified per spec D §5.1)
    private void AiTick()
    {
        if (Paused)
        {
            return;
        }
        BuildReachTable();
        FollowMovedTargets();
        if (Battle.RealCombat)
        {
            EngageIdle();
            FightBack();
            Schedule();
        }
    }

    /// <summary>Who is in reach of i: its box moved by one walk step (at least the minimum reach) times <paramref name="multiple"/> in each direction.</summary>
    // orig: R014+0xFE90 (1DB7:0A80) draft (asm-verified per spec D §5.2)
    private void Probe(int i, int multiple)
    {
        var set = Slots[AnimationSet(i)];
        Array.Clear(_inReach);
        var c = Battle[i];
        var at = c.Position;
        for (var d = 0; d < 10; d++)
        {
            int ry = set.StepY[d], rx = set.StepX[d];
            if (Math.Abs(rx) < Math.Abs(ReachX[d]))
            {
                rx = ReachX[d];
            }
            if (Math.Abs(ry) < Math.Abs(ReachY[d]))
            {
                ry = ReachY[d];
            }
            var r = Map.Collides(i, at.X + rx * multiple, at.Y + ry * multiple);
            // C: a signed > 0 test: the void result −1 is ignored.
            if (r.Mask > 0)
            {
                for (var k = 0; k < 16; k++)
                {
                    if (Map.LastParts[k] != 0)
                    {
                        _inReach[k] = true;
                    }
                }
            }
        }
        c.Position = at;
    }

    /// <summary>The reach table, made symmetric.</summary>
    // orig: R014+0xFDBE (1DB7:09AE) draft (byte-matched per the draft notes)
    private void BuildReachTable()
    {
        var reach = Battle.Reaches;
        reach.ClearReach();
        for (var i = 0; i < Combatant.Count; i++)
        {
            if (Battle[i].IsAble)
            {
                Probe(i, 1);
                for (var j = 0; j < Combatant.Count; j++)
                {
                    reach[i, j] = _inReach[j];
                }
            }
        }
        for (var i = 0; i < Combatant.Count; i++)
        {
            if (!Battle[i].IsAble)
            {
                continue;
            }
            for (var j = 0; j < Combatant.Count; j++)
            {
                if (reach[i, j] != reach[j, i])
                {
                    reach[i, j] = reach[j, i] = true;
                }
            }
        }
    }

    /// <summary>Walkers re-path when someone they saw near their path moved (or their target moved far enough).</summary>
    // orig: R014+0xF67A (1DB7:026A) draft (asm-verified per spec D §5.3)
    private void FollowMovedTargets()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.IsAble && c.Moving && !c.GaveUp)
            {
                for (var j = 0; j < Combatant.Count; j++)
                {
                    var q = c.SeenPositions[j];
                    var other = Battle[j];
                    if (q.X == -1 || q == other.Position)
                    {
                        continue;
                    }
                    if (Distance((q.X - c.Position.X) / 2, q.Y - c.Position.Y) < 17)
                    {
                        if (c.Target == j)
                        {
                            c.Destination = other.Position;
                        }
                    }
                    else
                    {
                        if (c.Target != j)
                        {
                            continue;
                        }
                        var sameSide = (j < Combatant.PartySlots) == (i < Combatant.PartySlots);
                        if (Distance((q.X - other.Position.X) / 2, q.Y - other.Position.Y) < 12
                            && (Battle[Battle.Leader].Moving || MoveMode != 2 || !sameSide))
                        {
                            continue;
                        }
                        c.Destination = other.Position;
                    }
                    if (FindPath(i))
                    {
                        GivePath(i);
                        c.PathIndex = 0;
                    }
                    else
                    {
                        c.GaveUp = true;
                        c.GiveUpUntil = (ushort)(Clock + 60);
                    }
                    break;
                }
            }
            else if (c.GaveUp && c.GiveUpUntil < Clock)
            {
                c.GaveUp = false;
            }
        }
    }

    /// <summary>After a path is found: remember who stands near its waypoints.</summary>
    // orig: R014+0xF568 (1DB7:0158) draft (asm-verified per spec D §5.4)
    private void AiPathHook(int i)
    {
        var c = Battle[i];
        var mask = 0;
        Map.IgnoreScenery = true;
        for (var k = 0; k < Paths.WaypointCount; k++)
        {
            var w = Paths.Waypoint(k);
            for (var d = 0; d < 8; d++)
            {
                // C: a void result (−1) ORs every bit in: then everybody counts as seen.
                mask |= Map.Collides(i, w.X + ReachX[d], w.Y + ReachY[d]).Mask;
            }
        }
        Map.IgnoreScenery = false;
        for (var j = 0; j < Combatant.Count; j++)
        {
            var o = Battle[j];
            c.SeenPositions[j] = (mask >> j & 1) == 1 && o.IsAble && (!o.Moving || c.Target == j)
                ? o.Position
                : new MapPoint(-1, -1);
        }
    }

    /// <summary>In real combat, an idle combatant attacks an adjacent opponent that is not fighting it.</summary>
    // orig: R014+0xFADC (1DB7:06CC) draft (byte-matched per the draft notes)
    private void EngageIdle()
    {
        if (!Battle.RealCombat)
        {
            return;
        }
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if ((Battle.DragonFight != 0 && i == 5) || (c.Flags & TacticalFlags.Following) != 0 || !c.IsAble || (c.Action & CombatAction.Melee) != 0)
            {
                continue;
            }
            var (lo, hi) = Opponents(i);
            for (var j = lo; j < hi; j++)
            {
                var o = Battle[j];
                if (Battle.Reaches[i, j] && o.Target != i && c.Target != j && o.Floor == c.Floor && (o.Action & CombatAction.Melee) == 0)
                {
                    StartFight(i, j, false);
                    break;
                }
            }
        }
    }

    /// <summary>Somebody adjacent attacks i: unless i already fights someone who fights it, i turns on him.</summary>
    // orig: R014+0xFC50 (1DB7:0840) draft (asm-verified per spec D §5.6)
    private void FightBack()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if ((Battle.DragonFight != 0 && i == 5) || !c.IsAble || KeyMoved || (c.Flags & TacticalFlags.Following) != 0)
            {
                continue;
            }
            for (var j = 0; j < Combatant.Count; j++)
            {
                var o = Battle[j];
                if (Battle.Reaches[i, j] && o.IsAble && o.Target == i && o.Floor == c.Floor && (o.Action & CombatAction.Melee) != 0)
                {
                    // C: combatants[c.target] with a target of −1 reads the record before combatant 0; never reached since
                    // the melee bit implies a target.
                    var engaged = (c.Action & CombatAction.Melee) != 0 && c.Target >= 0 && Battle[c.Target].Target == i;
                    if (!engaged)
                    {
                        StartFight(i, j, true);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>The common part of engage_idle and fight_back: ready the hand weapon and attack j at once.</summary>
    private void StartFight(int i, int j, bool fightingBack)
    {
        var c = Battle[i];
        if (fightingBack)
        {
            c.Moving = false;
        }
        if (i >= Combatant.PartySlots && c.EnemyClass != 0)
        {
            BackToMelee(i);
        }
        if (fightingBack)
        {
            c.ClearOrders();
            c.EngagedByAi = true;
        }
        ReadyWeapon(i, c.DefaultWeapon, 0);
        c.Pose = Pose.Reload;
        SetPose(i, Pose.Fighting);
        c.Target = (sbyte)j;
        if (!fightingBack)
        {
            c.AttackRate = 0xFF;
            c.EngagedByAi = true;
            c.Moving = false;
            c.ClearOrders();
        }
        c.Action = Stance(i, j, false);
        c.AttackRate = 0xFF;
        c.EngageAt = Clock;
        c.State = ActionState.Ready;
        _view.RowChanged(i);
    }

    /// <summary>The attack stance against t: a member keeps an ordered berserk/parry stance; else standard or vulnerable when stronger, parry when weaker.</summary>
    // orig: R014+0xFFB8 (1DB7:0BA8) draft (asm-verified per spec D §5.7)
    private CombatAction Stance(int i, int t, bool force)
    {
        var c = Battle[i];
        if (i < Combatant.PartySlots && (c.Action == CombatAction.BerserkAttack || c.Action == CombatAction.ParryAttack))
        {
            return c.Action;
        }
        var o = Battle[t];
        if (o.ReadiedSkill * 2 / 3 < c.ReadiedSkill || force)
        {
            return (c.WeaponClass & 0x0F) > o.ArmourValue[0] ? CombatAction.StandardAttack : CombatAction.VulnerableAttack;
        }
        return CombatAction.ParryAttack;
    }

    /// <summary>i goes for t: attacks at once when already there, else walks there with the attack queued.</summary>
    // orig: ai_set_target R014+0x1003A (1DB7:0C2A) draft (asm-verified per spec D §5.8)
    private void SetTarget(int i, int t)
    {
        var c = Battle[i];
        c.Target = (sbyte)t;
        c.Destination = Battle[t].Position;
        if (Battle.Reaches.Cost(i * 5 + t) == 0)
        {
            c.ClearOrders();
            c.Action = Stance(i, t, true);
            c.State = ActionState.Ready;
            return;
        }
        if (FindPath(i))
        {
            GivePath(i);
            c.Moving = true;
            c.PathIndex = 0;
            c.ClearOrders();
            c.QueueOrder(Stance(i, t, true));
            c.Action = CombatAction.Walk;
            ClockAfterPathSearch();
            c.RepathAt = (ushort)(Clock + 30);
        }
    }

    /// <summary>
    /// A debugging aid for the oracle (no effect in play: <see cref="ClockPhase"/> is null there): a rethink's path search in the
    /// scheduler is where the original's long passes spend their emulated time (the path costs and the path search of ai_rethink
    /// R014+0x10232 / ai_set_target R014+0x1003A), and both then read the combat clock DS:07D4 for the re-path time
    /// (R014+0x1031F: clock + 30). The tester measures that time only as the callbacks that came before the melee exchanges
    /// (phase -2), so they are given here, before the clock is read, rather than after the scheduler.
    /// </summary>
    private static void ClockAfterPathSearch() => ClockPhase?.Invoke(-2);

    /// <summary>Everybody's endurance fraction (×64), 0xFF for those not standing.</summary>
    // orig: R014+0x1010A (1DB7:0CFA) draft
    private void UpdatePreferences()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            // C: (endur << 6) / maxend as a signed int truncated to a byte (a maximum of 0 would fault in the original).
            _preference[i] = c.IsAble ? (byte)(c.MaximumEndurance == 0 ? 0 : (c.Endurance << 6) / c.MaximumEndurance) : (byte)0xFF;
        }
    }

    /// <summary>One party member per tick and an enemy pair every 16 clock ticks rethink their targets.</summary>
    // orig: R014+0x10146 (1DB7:0D36) draft (asm-verified per spec D §5.10)
    private void Schedule()
    {
        if (!Battle.RealCombat)
        {
            return;
        }
        ClockPhase?.Invoke(-1);
        var list = new List<int>(4);
        if (_partyRethinkAt < Clock)
        {
            list.Add(_partyRethinkSlot);
            _partyRethinkAt += 5;
            _partyRethinkSlot = (_partyRethinkSlot + 1) % 5;
        }
        if (_enemyRethinkAt < Clock)
        {
            list.Add(_enemyRethinkSlot);
            list.Add(_enemyRethinkSlot + 5 < Combatant.Count ? _enemyRethinkSlot + 5 : -1);
            // C: (DS:E09B − 4) % 5 + 5: the first pair is (4, 9), 4 being a party member.
            _enemyRethinkSlot = (_enemyRethinkSlot - 4) % 5 + 5;
            _enemyRethinkAt = (ushort)(_enemyRethinkAt + (byte)_enemyRethinkInterval);
        }
        foreach (var v in list)
        {
            if (v == -1)
            {
                continue;
            }
            var c = Battle[v];
            if (c.IsAble && c.Target != -1 && c.Floor == Battle.PartyFloor && (c.Action & CombatAction.Melee) != 0)
            {
                Rethink(v, c.Target);
            }
        }
    }

    /// <summary>
    /// i rethinks its target t: chases it when out of reach; when t is down it answers whoever
    /// hits it, else helps against whoever attacks the most endangered, else takes the nearest.
    /// </summary>
    // orig: ai_rethink R014+0x10232 (1DB7:0E22) draft (asm-verified per spec D §5.11; the
    // draft's "target down" test reads the wrong flag byte: the asm tests the corpse-processed flag, cbt_flags[t][1] & 1)
    private void Rethink(int i, int t)
    {
        if (Battle.DragonFight != 0 && i == 5)
        {
            return;
        }
        var c = Battle[i];
        var target = Battle[t];
        if (target.IsAble && !Battle.Reaches[i, t] && target.Exchange != Exchange.PushedBack && target.Floor == c.Floor
            && c.Exchange is not (Exchange.PusherFollowing or Exchange.PushedBack or Exchange.AfterPush))
        {
            c.Destination = target.Position;
            if (FindPath(i))
            {
                GivePath(i);
                c.Moving = true;
                c.PathIndex = 0;
                c.ClearOrders();
                c.QueueOrder(Stance(i, t, false));
                c.Action = CombatAction.Walk;
                ClockAfterPathSearch();
                c.RepathAt = (ushort)(Clock + 30);
            }
        }
        if (target.Status != CharacterStatus.Unconscious && (target.Flags & TacticalFlags.CorpseDone) == 0)
        {
            return;
        }
        var (lo, hi) = Opponents(i);
        PathCosts(i, lo, hi);
        var first = IndexOfMinimumCost(i);
        if (Battle.Reaches.Cost(i * 5 + first) == ReachTable.Unreachable)
        {
            if (i < Combatant.PartySlots)
            {
                c.Target = -1;
                c.Action = CombatAction.None;
                c.Moving = false;
                SetPose(i, Pose.Walking);
            }
            else
            {
                c.State = ActionState.Idle;
            }
            return;
        }
        for (var j = lo; j < hi; j++)
        {
            var o = Battle[j];
            if (o.IsAble && o.Target == i && (o.Action & CombatAction.Melee) != 0)
            {
                c.Target = (sbyte)j;
                c.State = ActionState.Ready;
                c.Action = Stance(i, j, false);
                return;
            }
        }
        UpdatePreferences();
        if (_preference[IndexOfMinimum(_preference)] <= 0x20)
        {
            for (var j = 0; j < Combatant.Count; j++)
            {
                var w = Battle.Reaches.Cost(i * 5 + j);
                if (_preference[j] != 0xFF && w != ReachTable.Unreachable)
                {
                    // C: a byte add (wraps).
                    _preference[j] = (byte)(_preference[j] + w / 5);
                }
            }
            while (true)
            {
                var w = IndexOfMinimum(_preference);
                if (_preference[w] == 0xFF)
                {
                    break;
                }
                var sel = -1;
                for (var j = lo; j < hi; j++)
                {
                    if (Battle[j].Target == w && Battle.Reaches.Cost(i * 5 + j) < 9999)
                    {
                        sel = j;
                    }
                }
                if (sel != -1)
                {
                    SetTarget(i, sel);
                    return;
                }
                _preference[w] = 0xFF;
            }
        }
        SetTarget(i, first);
    }

    /// <summary>The path costs from i to the opponents lo..hi−1 (enemies skip a victim three enemies already go for).</summary>
    // orig: R014+0xF438 (1DB7:0028) draft (asm-verified per spec D §5.12)
    private void PathCosts(int i, int lo, int hi)
    {
        var best = ReachTable.Unreachable;
        var c = Battle[i];
        if (c.IsAble)
        {
            for (var k = 0; k < 13; k++)
            {
                Battle.Reaches.SetCost(i * 5 + k, ReachTable.Unreachable);
            }
            var set = Slots[AnimationSet(i)];
            for (var j = lo; j < hi; j++)
            {
                if (!Battle[j].IsAble)
                {
                    continue;
                }
                if (i >= Combatant.PartySlots)
                {
                    var n = 0;
                    for (var k = Combatant.PartySlots; k < Combatant.Count; k++)
                    {
                        var e = Battle[k];
                        if (e.Target == j && e.IsAble && ((e.Action & CombatAction.Melee) != 0 || e.Action == CombatAction.Walk))
                        {
                            n++;
                        }
                    }
                    if (n >= 3)
                    {
                        continue;
                    }
                }
                var length = Paths.TrackLength(i, j, c.Position, Battle[j].Position, c.Facing, set.StepX, set.StepY);
                if (length >= 0)
                {
                    Battle.Reaches.SetCost(i * 5 + j, length);
                    if (length < best)
                    {
                        best = Battle.Reaches.Cost(i * 5 + j);
                    }
                }
            }
        }
        _best[i] = (best, i);
    }

    /// <summary>index_of_min_int over row i (13 words): the last minimum.</summary>
    // orig: index_of_min_int root 0410:02A8 (the tie-break read in the asm: 0410:02B9 jg skips only a greater word, so an equal one replaces the minimum)
    private int IndexOfMinimumCost(int i)
    {
        var k = 0;
        for (var j = 1; j < 13; j++)
        {
            if (Battle.Reaches.Cost(i * 5 + j) <= Battle.Reaches.Cost(i * 5 + k))
            {
                k = j;
            }
        }
        return k;
    }

    /// <summary>index_of_min_byte: the last minimum.</summary>
    // orig: index_of_min_byte root 0410:01DC (the tie-break read in the asm: 0410:01EC ja skips only a greater byte, so an equal one replaces the minimum)
    private static int IndexOfMinimum(byte[] values)
    {
        var k = 0;
        for (var j = 1; j < values.Length; j++)
        {
            if (values[j] <= values[k])
            {
                k = j;
            }
        }
        return k;
    }

    /// <summary>The other side's slots.</summary>
    private static (int Lo, int Hi) Opponents(int i) => i < Combatant.PartySlots ? (Combatant.PartySlots, Combatant.Count) : (0, Combatant.PartySlots);

    /// <summary>distance3: an integer approximation of the distance (all unsigned 16-bit).</summary>
    // orig: distance3 root 047C:1120 draft (checked: big + small/4, + 3t/16 when t = 2·small − big > 0, − d/64); tests: Oracle:tactics.json
    // DORMANT: distance3's third argument is a threshold: an approximation below it is replaced by the exact root (047C:1170 jb 1177:
    // the squares a·a + b·b and the integer square root of 047C:11B4). All eight callers (R014, R018, R019, R023) pass 0, so the exact
    // path never runs and the threshold is left out here; it would matter only for a caller passing a positive limit.
    public static int Distance(int a, int b)
    {
        // C: cwd / xor / sub: the absolute value of a 16-bit word (−32768 stays 0x8000, read unsigned).
        int sa = (short)a, sb = (short)b;
        int big = (ushort)(sa < 0 ? -sa : sa), small = (ushort)(sb < 0 ? -sb : sb);
        if (big < small)
        {
            (big, small) = (small, big);
        }
        var d = (ushort)(big + (small >> 2));
        // C: shl / sub / jbe on 16-bit words: 2·small wraps before the unsigned compare.
        var twice = (ushort)(small * 2);
        var t = (ushort)(twice - big);
        if (twice > big)
        {
            d = (ushort)(d + ((ushort)(t * 3) >> 4));
        }
        d = (ushort)(d - (d >> 6));
        return d;
    }
}
