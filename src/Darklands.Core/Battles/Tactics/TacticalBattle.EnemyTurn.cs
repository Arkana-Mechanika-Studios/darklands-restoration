using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:214E / DS:215E: the 16 offsets an archer or caster tries for a new spot.</summary>
    private static readonly int[] SpotX = [-29, -29, -19, -19, 29, 29, 19, 19, -29, -29, -19, -19, 29, 29, 19, 19];
    private static readonly int[] SpotY = [-15, -9, -15, -9, -15, -9, -15, -9, 15, 9, 15, 9, 15, 9, 15, 9];

    /// <summary>DS:21DC: the area size byte per spell type (size class in the high nibble).</summary>
    /// <summary>The distance of an unseen or unwanted target.</summary>
    private const int FarAway = 0x7D00;

    /// <summary>Which party members the enemies see (all of them right after the alarm).</summary>
    // orig: enemy_sees R018+0x04B6 (1F43:0006) draft (asm-verified per spec D §6.1)
    private int EnemySees(bool[] seen)
    {
        Array.Clear(seen);
        var count = 0;
        var alive = Battle.Combatants.Take(Combatant.PartySlots).Count(c => c.IsAble);
        if (Battle.Alarm == 0x63)
        {
            for (var p = 0; p < Combatant.PartySlots; p++)
            {
                if (Battle[p].IsAble)
                {
                    seen[p] = true;
                    count++;
                }
            }
            Battle.Alarm = 0;
            return count;
        }
        for (var e = Combatant.PartySlots; e < Combatant.Count; e++)
        {
            var en = Battle[e];
            if (!en.IsAble || en.Held)
            {
                continue;
            }
            for (var p = 0; p < Combatant.PartySlots; p++)
            {
                var m = Battle[p];
                if (seen[p] || !m.IsAble || m.Floor != Battle.FloorShown)
                {
                    continue;
                }
                var dir = DirectionToward(en.Position, m.Position, false);
                var ok = en.WatchedFacingCount == 7 || en.WatchedFacings.Take(en.WatchedFacingCount).Contains(dir);
                if (ok && Map.LineBlocked(en.Position, m.Position) == 0)
                {
                    // C: DS:C4FD is set (only the battle file reads it).
                    EnemySawMember = true;
                    seen[p] = true;
                    count++;
                }
                if (count == alive)
                {
                    break;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// The facing from one point toward another (DS:17B2 by quadrant and slope class; x is halved).
    /// The slope class is 0 flat, 1 for a ratio 2..7, else 2 — a ratio of 1 counts as steep (faithful).
    /// </summary>
    /// <param name="formation">The group-move variant: class 2 for a ratio below 2 or above 7.</param>
    private static int DirectionToward(MapPoint from, MapPoint to, bool formation)
    {
        var c = (to.X - from.X) / 2;
        var dy = to.Y - from.Y;
        var r = c == 0 ? 20 : Math.Abs(dy) * 4 / Math.Abs(c);
        var cls = r == 0 ? 0 : formation ? (r < 2 || r > 7 ? 2 : 1) : (r is >= 2 and < 8 ? 1 : 2);
        var quad = c >= 0 && dy < 0 ? 0 : c >= 0 ? 1 : dy >= 0 ? 2 : 3;
        return FacingToward[quad, cls];
    }

    /// <summary>
    /// The enemies' turn (about every 120 clock ticks): real combat starts when one sees the
    /// party; lost targets are dropped; then, nearest first, each idle enemy picks the least-engaged
    /// visible member: hand-to-hand fighters walk to it, archers shoot from 32 pixels or more (else
    /// close in or find a spot), casters cast now and then.
    /// </summary>
    // orig: enemy_turn R018+0x0706 (1F43:0256) draft (near-match; key conditions
    // checked against the asm per spec D §6.2)
    private void EnemyTurn()
    {
        if (Paused)
        {
            return;
        }
        if (DelayedStart && Clock < DelayedStartAt)
        {
            return;
        }
        if (Battle.DemoMode)
        {
            // DORMANT: the demonstration fight's set-up of combatant 5.
            return;
        }
        DelayedStart = false;
        _turnRotation = (_turnRotation + 1) & 3;
        var turn = _turnRotation + 5;
        var seen = new bool[Combatant.PartySlots];
        var count = EnemySees(seen);
        if (count > 0 && !Battle.RealCombat)
        {
            Battle.RealCombat = true;
            CombatBoxRetryAt = (ushort)(Clock + 300);
            for (var i = 0; i < Combatant.Count; i++)
            {
                JoinFight(i);
            }
        }
        if (count > 0 && Battle.RealCombat)
        {
            Menus.Set(OrdersItem.Surrender, 1);
        }
        for (var e = Combatant.PartySlots; e < Combatant.Count; e++)
        {
            var en = Battle[e];
            if (!en.IsAble || en.Target == -1 || seen[en.Target])
            {
                continue;
            }
            var t = Battle[en.Target];
            if (!t.IsAble || t.Floor != Battle.FloorShown)
            {
                en.ClearOrders();
                CancelAction(e);
                en.Moving = false;
                en.Exchange = Exchange.None;
                en.Target = -1;
            }
            if (Battle.RealCombat && en.EnemyClass is 1 or 2)
            {
                // Archers and casters who lost their target fall back to their hand weapon and see everybody.
                BackToMelee(e);
                ReadyWeapon(e, en.DefaultWeapon, 0);
                en.Pose = Pose.Reload;
                SetPose(e, Pose.Fighting);
                en.ClearOrders();
                CancelAction(e);
                en.Frame = 0;
                count = 0;
                for (var m = 0; m < Combatant.PartySlots; m++)
                {
                    if (Battle[m].IsAble)
                    {
                        seen[m] = true;
                        count++;
                    }
                }
                en.Target = -1;
            }
        }
        if (count <= 0)
        {
            return;
        }
        for (var m = Combatant.PartySlots; m < Combatant.Count; m++)
        {
            DistanceRow(m, seen);
        }
        // Selection sort by distance (strict <): the nearest act first.
        for (var m = Combatant.PartySlots; m < Combatant.Count - 1; m++)
        {
            var min = m;
            var best = _best[m];
            for (var k = m + 1; k < Combatant.Count; k++)
            {
                if (_best[k].Cost < best.Cost)
                {
                    min = k;
                    best = _best[k];
                }
            }
            _best[min] = _best[m];
            _best[m] = best;
        }
        for (var m = Combatant.PartySlots; m < Combatant.Count; m++)
        {
            var e = _best[m].Who;
            var en = Battle[e];
            if (!(((en.Flags & TacticalFlags.NoTarget) == 0 || e == turn || turn - e == -4)
                && en.IsAble && en.Target == -1 && en.Action == CombatAction.None && !en.Held))
            {
                continue;
            }
            var list = CandidateList(e);
            if (en.EnemyClass != 2)
            {
                MeleeOrRanged(e, list, seen);
            }
            else if (Scaled(3) == 0)
            {
                Cast(e, list, seen);
            }
        }
    }

    /// <summary>The party members for enemy e: by how many enemies already go for them, then by distance.</summary>
    private (int Dist, int Count, int Who)[] CandidateList(int e)
    {
        var list = new (int Dist, int Count, int Who)[Combatant.PartySlots];
        for (var j = 0; j < Combatant.PartySlots; j++)
        {
            var n = 0;
            if (Battle[j].IsAble)
            {
                for (var k = Combatant.PartySlots; k < Combatant.Count; k++)
                {
                    if (Battle[k].Target == j)
                    {
                        n++;
                    }
                }
            }
            list[j] = (Battle.Reaches.Cost(e * 5 + j), n, j);
        }
        for (var j = 0; j < 4; j++)
        {
            var min = j;
            var tmp = list[j];
            for (var k = j + 1; k < 5; k++)
            {
                if (list[k].Count < tmp.Count || (list[k].Count == tmp.Count && list[k].Dist < tmp.Dist))
                {
                    min = k;
                    tmp = list[k];
                }
            }
            list[min] = list[j];
            list[j] = tmp;
        }
        return list;
    }

    /// <summary>Class 0 walks to a target; class 1 shoots from 32 pixels or more, else closes in; failing that the archer looks for a spot.</summary>
    private void MeleeOrRanged(int e, (int Dist, int Count, int Who)[] list, bool[] seen)
    {
        var en = Battle[e];
        var done = false;
        for (var k = 0; k < Combatant.PartySlots && !done; k++)
        {
            var t = list[k].Who;
            if (!Battle[t].IsAble || !seen[t])
            {
                continue;
            }
            var cls = en.EnemyClass;
            if (cls == 0 || (cls == 1 && list[k].Dist < 0x20))
            {
                if (cls == 1)
                {
                    // Too close to shoot: fight hand to hand.
                    BackToMelee(e);
                    ReadyWeapon(e, en.DefaultWeapon, 0);
                    en.Pose = Pose.Reload;
                    SetPose(e, Pose.Fighting);
                    en.Frame = 0;
                }
                en.Destination = Battle[t].Position;
                en.Target = (sbyte)t;
                if (FindPath(e))
                {
                    GivePath(e);
                    en.Action = CombatAction.Walk;
                    en.PathIndex = 0;
                    en.Moving = true;
                    en.QueueOrder(CombatAction.StandardAttack);
                    en.RepathAt = (ushort)(Scaled(90) + Clock + 30);
                    en.Flags &= ~TacticalFlags.NoTarget;
                    done = true;
                }
                else
                {
                    en.Target = -1;
                }
            }
            else if (cls == 1)
            {
                if (Map.LineBlocked(en.Position, Battle[t].Position, e, t, 0x80) == 0)
                {
                    ReadyWeapon(e, en.ReadiedWeapon, 0x80);
                    en.Target = (sbyte)t;
                    en.Action = CombatAction.Missile;
                    en.EngageAt = Clock;
                    en.State = ActionState.Ready;
                    done = true;
                }
            }
        }
        if (!done)
        {
            if (en.EnemyClass == 1)
            {
                FindSpot(e, list);
            }
            else
            {
                en.Flags |= TacticalFlags.NoTarget;
            }
        }
    }

    /// <summary>A caster: a spell drawn from its charges; a missile spell at the midpoint toward a visible target, an area spell where it hits the fewest of its own.</summary>
    private void Cast(int e, (int Dist, int Count, int Who)[] list, bool[] seen)
    {
        var en = Battle[e];
        if (en.SpellsLeft == 0)
        {
            StopCasting(e, list, seen);
            return;
        }
        int type;
        do
        {
            type = Scaled(10);
        }
        while (type == 2 || en.SpellCharges[type] == 0);
        var power = en.SpellPowers[type];
        en.SpellCharges[type]--;
        en.SpellsLeft--;
        if (type == 6)
        {
            for (var k = 0; k < Combatant.PartySlots; k++)
            {
                var t = list[k].Who;
                var tm = Battle[t];
                if (tm.IsAble && seen[t] && Map.LineBlocked(en.Position, tm.Position) == 0)
                {
                    if (list[k].Dist < 0x20)
                    {
                        StopCasting(e, list, seen);
                        return;
                    }
                    BeginCast(e, type, power);
                    en.Destination = new MapPoint((en.Position.X + tm.Position.X) >> 1, (en.Position.Y + tm.Position.Y) >> 1);
                    en.Target = -1;
                    break;
                }
            }
            return;
        }
        var candidates = new (int Who, int Count, int Dist)[Combatant.PartySlots];
        for (var j = 0; j < Combatant.PartySlots; j++)
        {
            var m = Battle[j];
            if (m.IsAble && seen[j] && Map.LineBlocked(en.Position, m.Position) == 0)
            {
                var dist = candidates[j].Dist;
                for (var k = 0; k < Combatant.PartySlots; k++)
                {
                    if (list[k].Who == j)
                    {
                        dist = list[k].Dist;
                        break;
                    }
                }
                candidates[j] = (j, 0, dist);
                if (dist >= 0x20)
                {
                    var n = 0;
                    for (var k = Combatant.PartySlots; k < Combatant.Count; k++)
                    {
                        if (Battle[k].IsAble && Effects.InArea(m.Position, type, k))
                        {
                            n++;
                        }
                    }
                    candidates[j].Count = n;
                    continue;
                }
            }
            candidates[j] = (0xFF, 0xFF, candidates[j].Dist);
        }
        for (var j = 0; j < 4; j++)
        {
            var min = j;
            var tmp = candidates[j];
            for (var k = j + 1; k < 5; k++)
            {
                if ((byte)candidates[k].Count < (byte)tmp.Count)
                {
                    min = k;
                    tmp = candidates[k];
                }
            }
            candidates[min] = candidates[j];
            candidates[j] = tmp;
        }
        if (candidates[0].Who != 0xFF)
        {
            // C: n is an uninitialised local when every candidate has the same count; the number of candidates is used.
            // DIVERGENCE: the original's n would be whatever the stack held (spec D §9.5).
            var n = Combatant.PartySlots;
            for (var j = 1; j < Combatant.PartySlots; j++)
            {
                if (candidates[j].Count != candidates[0].Count)
                {
                    n = j;
                    break;
                }
            }
            var who = candidates[Scaled(n)].Who;
            BeginCast(e, type, power);
            en.Destination = Battle[who].Position;
            en.Target = (sbyte)who;
            return;
        }
        var allNear = true;
        for (var j = 0; j < Combatant.PartySlots; j++)
        {
            // C: a candidate of 0xFF reads combatants[255]; such entries are skipped here (their dist is stale).
            if (candidates[j].Dist > 0x20 && candidates[j].Who < Combatant.Count && Battle[candidates[j].Who].IsAble)
            {
                allNear = false;
                break;
            }
        }
        if (allNear)
        {
            StopCasting(e, list, seen);
            return;
        }
        FindSpot(e, list);
    }

    /// <summary>A caster without spells (or too close) fights hand to hand from now on.</summary>
    private void StopCasting(int e, (int Dist, int Count, int Who)[] list, bool[] seen)
    {
        var en = Battle[e];
        BackToMelee(e);
        ReadyWeapon(e, en.DefaultWeapon, 0);
        en.Pose = Pose.Reload;
        SetPose(e, Pose.Fighting);
        en.Frame = 0;
        MeleeOrRanged(e, list, seen);
    }

    private void BeginCast(int e, int type, int power)
    {
        var en = Battle[e];
        // The cast pose: "PT" replaces the weapon letters of the caster's attack file.
        SlotWeapons[en.AnimationSlot] = "PT";
        en.Spell = type;
        en.SpellPower = power;
        en.Action = CombatAction.UseItem;
        en.EngageAt = Clock;
        en.State = ActionState.Ready;
    }

    /// <summary>An archer or caster looks for a spot with a line of fire; failing that it walks toward a member, then toward another enemy.</summary>
    // orig: R018+0x113A (1F43:0C8A) draft (asm-verified per spec D §6.3)
    private void FindSpot(int e, (int Dist, int Count, int Who)[] list)
    {
        var en = Battle[e];
        var found = false;
        for (var tries = 0; tries < 50 && !found; tries++)
        {
            var k = Scaled(16);
            var spot = new MapPoint(SpotX[k] + en.Position.X, SpotY[k] + en.Position.Y);
            var ok = false;
            for (var j = 0; j < Combatant.PartySlots; j++)
            {
                if (list[j].Dist != FarAway && Map.LineBlocked(spot, Battle[list[j].Who].Position, e, list[j].Who, 0x80) == 0)
                {
                    ok = true;
                }
            }
            if (ok)
            {
                en.Destination = spot;
                if (FindPath(e))
                {
                    GivePath(e);
                    en.Action = CombatAction.Walk;
                    en.PathIndex = 0;
                    en.Moving = true;
                    en.RepathAt = (ushort)(Scaled(90) + Clock + 30);
                    found = true;
                }
            }
        }
        if (!found)
        {
            for (var k = 0; k < Combatant.PartySlots && !found; k++)
            {
                if (list[k].Dist == FarAway)
                {
                    continue;
                }
                en.Destination = Battle[list[k].Who].Position;
                // C: the target is set to the LIST index k, not the member (a slip; overwritten on success).
                en.Target = (sbyte)k;
                found = TryWalkCut(e);
            }
        }
        if (!found)
        {
            for (var k = Combatant.PartySlots; k < Combatant.Count && !found; k++)
            {
                if (Battle[k].IsAble && k != e)
                {
                    en.Destination = Battle[k].Position;
                    en.Target = (sbyte)k;
                    found = TryWalkCut(e);
                }
            }
        }
        if (!found)
        {
            en.Target = -1;
        }
    }

    /// <summary>A path is found and immediately cut (the first byte set to 0xFF): the enemy does not actually walk.</summary>
    private bool TryWalkCut(int e)
    {
        var en = Battle[e];
        if (!FindPath(e))
        {
            return false;
        }
        GivePath(e);
        // C: cbt_paths[e][0] = 0xFF right after path_give.
        en.Path[0] = 0xFF;
        en.Action = CombatAction.Walk;
        en.PathIndex = 0;
        en.Moving = true;
        en.RepathAt = (ushort)(Scaled(90) + Clock + 30);
        en.Target = -1;
        return true;
    }

    /// <summary>The distances from enemy n to the members it sees (unless it already has a target), and the nearest.</summary>
    // orig: R018+0x13B2 (1F43:0F02) draft (asm-verified per spec D §6.4)
    private void DistanceRow(int n, bool[] seen)
    {
        var best = FarAway;
        var c = Battle[n];
        if (c.IsAble && c.Target == -1)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (Battle[i].IsAble && seen[i])
                {
                    var d = Distance((Battle[i].Position.X - c.Position.X) / 2, Battle[i].Position.Y - c.Position.Y);
                    Battle.Reaches.SetCost(n * 5 + i, d);
                    if (d < best)
                    {
                        best = d;
                    }
                }
                else
                {
                    Battle.Reaches.SetCost(n * 5 + i, FarAway);
                }
            }
        }
        _best[n] = (best, n);
    }

    /// <summary>Joining the fight: the bigger combat box when it fits.</summary>
    // orig: R018+0x1472 (1F43:0FC2) draft (byte-matched)
    private void JoinFight(int i)
    {
        var c = Battle[i];
        if (!c.IsAble)
        {
            return;
        }
        c.Flags |= TacticalFlags.InFight;
        var set = Slots[AnimationSet(i)];
        c.HalfWidth = set.CombatHalfWidth;
        c.HalfHeight = set.CombatHalfHeight;
        c.AimSetUp = c.LoopSetUp = c.RestartLoop = false;
        if (!Map.Collides(i, c.Position.X, c.Position.Y).IsFree)
        {
            c.HalfWidth = set.HalfWidth;
            c.HalfHeight = set.HalfHeight;
            c.Flags &= ~TacticalFlags.CombatBox;
        }
        else
        {
            c.Flags |= TacticalFlags.CombatBox;
        }
    }

    /// <summary>Every 300 ticks of real combat: those whose bigger box did not fit try again.</summary>
    // orig: R018+0x14FC (1F43:104C) draft (byte-matched)
    private void RetryCombatBoxes()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.IsAble && (c.Flags & TacticalFlags.InFight) != 0 && (c.Flags & TacticalFlags.CombatBox) == 0)
            {
                JoinFight(i);
            }
        }
    }
}
