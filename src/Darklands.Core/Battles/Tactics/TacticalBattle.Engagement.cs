using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:A3D8: 1 no line of fire this tick, 2 out of ammunition (the panel's letters 'b' and 'e'), else 0.</summary>
    private readonly byte[] _fireBlocked = new byte[Combatant.Count];

    /// <summary>DS:A3D8 of combatant <paramref name="i"/>: 1 no line of fire, 2 out of ammunition, else 0.</summary>
    public int FireBlocked(int i) => _fireBlocked[i];

    /// <summary>
    /// The start of swings, shots and casts: whoever is ready, not walking and past its engage
    /// time begins its action now (to-hit and damage rolled at once); walkers queue their next
    /// path step.
    /// </summary>
    // orig: melee_exchanges R014+0x2A4A (0E76:2A4A) draft (spec D §3.1, asm-verified)
    private void MeleeExchanges()
    {
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (!c.IsAble)
            {
                // goto mods: the modifiers of the fallen run out too.
                Effects.EndModifiersDue(i);
                continue;
            }
            if ((c.Action & AttackOrders) != 0 && !c.Moving && c.EngageAt <= Clock && c.State == ActionState.Ready)
            {
                _fireBlocked[i] = 0;
                if (Battle.DragonFight != 0 && i == 5 && Battle.Result == BattleResult.Running)
                {
                    DragonPicksVictim(c);
                }
                int t = c.Target;
                if ((c.Action & CombatAction.Missile) != 0 && t >= 0)
                {
                    if (Map.LineBlocked(c.Position, Battle[t].Position, i, t, 0x80) == 1)
                    {
                        if (c.Pose != Pose.Fighting)
                        {
                            SetPose(i, Pose.Fighting);
                            c.Frame = 0;
                        }
                        _fireBlocked[i] = 1;
                        _view.RowChanged(i);
                        if (i < Combatant.PartySlots)
                        {
                            c.EngageAt = (ushort)(Scaled(60) + Clock + 30);
                        }
                        else
                        {
                            c.State = ActionState.Finished;
                            c.Target = -1;
                        }
                    }
                    else
                    {
                        _view.RowChanged(i);
                    }
                }
                if (_fireBlocked[i] == 0)
                {
                    if ((c.Action & CombatAction.Melee) != 0 && !HostilePair(i, t))
                    {
                        c.State = ActionState.Finished;
                        c.Target = -1;
                        Effects.EndModifiersDue(i);
                        continue;
                    }
                    c.Frame = -1;
                    c.State = ActionState.Swinging;
                    c.NextFrameAt = Clock;
                    Attacks.UpdateAttackRate(Battle, i);
                    c.EngageAt = (ushort)(Scaled(30) + Clock + 60);
                    Swing(i, t);
                }
            }
            if (c.Moving)
            {
                Approach(i);
            }
            Effects.EndModifiersDue(i);
        }
    }

    /// <summary>
    /// cbt_attack at the start of a swing: the to-hit and damage (queued), the attacker's reaction
    /// state and the frames of the swing: one of four feints, the parry row, or the attack row.
    /// </summary>
    // orig: cbt_attack R014+0xA592 (18BF:0102) draft, checked against asm (spec D §3.2)
    // C: the rows are those of the animation set the attacker has NOW (its walking set if it is not in its fighting pose
    // yet); melee_frame's first cbt_set_anim then replaces them with the attack row when the pose changes.
    private void Swing(int i, int t)
    {
        // C: a spell cast at a spot (target −1) calls cbt_attack with t = −1: the to-hit roll (and a miss's row) is drawn, then
        // to_hit_roll and hit_damage return on the spell bit before combatants[−1] is read.
        var c = Battle[i];
        var set = Slots[AnimationSet(i)];
        var kind = Slots[c.AnimationSlot];
        var outcome = Attacks.Attack(Battle, i, t, set.CanMiss, kind.NoParrySequence);
        if (outcome.Reaction >= 0)
        {
            c.Exchange = (Exchange)outcome.Reaction;
        }
        if (outcome.Result == HitResult.None)
        {
            for (var k = 0; k < 5; k++)
            {
                c.Sequence[k] = set.Sequences[10 + 5 * outcome.MissVariant + k];
            }
            c.SequenceLength = 5;
            return;
        }
        if (outcome.Result == HitResult.Parried)
        {
            if (Battle.DragonFight != 0)
            {
                // C: in a dragon fight a parried blow keeps the frames it had (R014+0xA690).
                return;
            }
            if (!kind.NoParrySequence)
            {
                var n = set.AimLength & 0x7F;
                if (n == 0)
                {
                    // C: the length byte is stored before the test: an empty parry row leaves a length of 0.
                    c.SequenceLength = 0;
                    return;
                }
                c.SequenceLength = n;
                for (var k = 0; k < n && k < c.Sequence.Length; k++)
                {
                    c.Sequence[k] = set.Sequences[5 + k];
                }
                return;
            }
        }
        if (Battle.DragonFight == 0)
        {
            var n = set.AttackLength;
            c.SequenceLength = n;
            for (var k = 0; k < n && k < c.Sequence.Length; k++)
            {
                c.Sequence[k] = set.Sequences[k];
            }
        }
    }

    /// <summary>Both on one floor and one can reach the other (cbt_hostile_pair).</summary>
    // orig: cbt_hostile_pair R014+0x47AE (0E76:47AE) draft (asm-verified per spec D §1.11)
    private bool HostilePair(int i, int j)
    {
        if (i == 5 && Battle.DragonFight != 0)
        {
            return true;
        }
        if (j == -1 || Battle[i].Floor != Battle[j].Floor)
        {
            return false;
        }
        Probe(i, 4);
        if (_inReach[j])
        {
            return true;
        }
        Probe(j, 4);
        return _inReach[i];
    }

    /// <summary>The direction the target is pushed: the attacker's facing, turned by one 20% of the time each way.</summary>
    // orig: push_direction R014+0x2A04 (0E76:2A04) draft (byte-matched per the draft notes)
    private int PushDirection(int i)
    {
        var f = Battle[i].AttackFacing;
        var r = Scaled(10);
        if (r < 2)
        {
            f++;
        }
        if (r > 7)
        {
            f--;
        }
        if (f > 7)
        {
            f = 0;
        }
        if (f < 0)
        {
            f = 7;
        }
        return f;
    }

    /// <summary>Following a path: queue the next step; re-path toward the target when standing still long enough.</summary>
    // orig: approach R014+0x2CCA (0E76:2CCA) draft (spec D §3.4, asm-verified)
    private void Approach(int i)
    {
        var c = Battle[i];
        if (c.Held)
        {
            c.Frame = 0;
            return;
        }
        // DIVERGENCE: DS:D6FB is a byte; an index past 249 reads the next combatant's path in the original (wrapped here).
        var b = c.Path[(byte)c.PathIndex % Combatant.PathLength];
        if (b == 0xFF)
        {
            Arrive(i);
            return;
        }
        if (c.QueuedStep == -1)
        {
            c.QueuedStep = b;
        }
        if (c.RepathAt > Clock)
        {
            return;
        }
        if (c.Target != -1)
        {
            c.Destination = Battle[c.Target].Position;
        }
        if (FindPath(i))
        {
            GivePath(i);
            c.PathIndex = 0;
        }
        else
        {
            var saved = c.Position;
            c.Position = c.Destination;
            var r = Map.Collides(i, c.Position.X, c.Position.Y);
            if (!r.IsFree && StandingOneBlocks(r))
            {
                // The destination is taken by somebody standing: give up (|x − tx| < 16 always holds here).
                CancelAction(i);
                c.State = ActionState.Idle;
                c.Moving = false;
                c.Target = -1;
                c.ClearOrders();
                if (i >= Combatant.PartySlots)
                {
                    c.Flags |= TacticalFlags.NoTarget;
                }
                // DORMANT: the original keeps an unreachable branch where a party member searches a random free visible spot
                // near the destination (101 tries); the distance test before it always holds.
            }
            c.Position = saved;
        }
        c.RepathAt = (ushort)(Scaled(60) + Clock + 30);
    }

    /// <summary>Some combatant in the mask stands still.</summary>
    private bool StandingOneBlocks(Blocking r)
    {
        if (r.IsVoid)
        {
            return false;
        }
        for (var k = 0; k < Combatant.Count; k++)
        {
            if ((r.Mask & (1 << k)) != 0 && !Battle[k].Moving)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The end of a path: the queued order becomes active and an attack starts at once.</summary>
    // orig: arrive R014+0x2FA2 (0E76:2FA2) draft (spec D §3.5, asm-verified)
    private void Arrive(int i)
    {
        var c = Battle[i];
        if (c.Frame != 0)
        {
            c.Frame = 0;
        }
        c.Moving = false;
        c.Flags &= ~TacticalFlags.Following;
        c.State = ActionState.Ready;
        var was = false;
        if ((c.Action & AttackOrders) != 0)
        {
            c.EngageAt = Clock;
            was = true;
        }
        c.TakeNextOrder();
        if ((c.Action & AttackOrders) != 0)
        {
            c.EngageAt = Clock;
            if (!was)
            {
                c.AttackFacing = c.Facing;
            }
        }
        if (c.Target == -1 && c.Action != CombatAction.None)
        {
            c.Action = CombatAction.None;
        }
        if (i < Combatant.PartySlots)
        {
            _view.RowChanged(i);
        }
    }

    /// <summary>The sprite facing toward the target (or toward the spot of a spell).</summary>
    // orig: facing_to_target R014+0x306A (0E76:306A) draft (spec D §3.7, asm-verified)
    public int FacingToTarget(int i)
    {
        var c = Battle[i];
        int dx, dy;
        if (c.Action == CombatAction.UseItem && c.Target == -1)
        {
            dx = c.Destination.X - c.Position.X;
            dy = c.Destination.Y - c.Position.Y;
        }
        else if (c.Target >= 0)
        {
            dx = Battle[c.Target].Position.X - c.Position.X;
            dy = Battle[c.Target].Position.Y - c.Position.Y;
        }
        else
        {
            return c.SpriteFacing;
        }
        int ady = Math.Abs(dy), adx = Math.Abs(dx);
        int r1, r4;
        if (adx == 0)
        {
            r1 = r4 = 20;
        }
        else
        {
            r1 = ady / adx;
            r4 = ady * 4 / adx;
        }
        var quad = dx >= 0 && dy < 0 ? 0 : dx >= 0 ? 1 : dy >= 0 ? 2 : 3;
        var s = quad is 0 or 2 ? (r1 != 0 ? 1 : 0) : r4 == 0 ? 0 : r4 <= 3 ? 1 : r4 <= 9 ? 2 : 3;
        int[,] table = { { 2, 1, 0, 0 }, { 2, 3, 4, 5 }, { 6, 5, 0, 0 }, { 6, 7, 0, 1 } };
        return table[quad, s];
    }

    /// <summary>path_find for combatant i (the AI's path hook runs on success).</summary>
    // orig: path_find R014+0x481A (12F7:000A) draft
    private bool FindPath(int i)
    {
        var set = Slots[AnimationSet(i)];
        if (!Paths.Find(Battle, i, set.StepX, set.StepY, OpenDoorsForSearch))
        {
            return false;
        }
        if (Paths.PathLength > 0 && Paths.Path[0] != 0xFF)
        {
            AiPathHook(i);
        }
        return true;
    }

    /// <summary>path_give: the last path found becomes combatant i's (packed with facings).</summary>
    // orig: path_give R014+0x5D64 (12F7:1554) draft
    private void GivePath(int i) => PathFinder.GiveTo(Battle[i].Path, Paths.Path, Paths.PathLength);

    /// <summary>2E38:0C64 / DS:DA43: the saved path (250 bytes, unpacked) and its length; the battle file keeps both.</summary>
    public byte[] SavedPath { get; } = new byte[PathFinder.MaxSteps];

    public int SavedPathLength { get; set; }

    /// <summary>path_save / path_give_saved: keep the last path for an order confirmed later.</summary>
    // orig: path_save R014+0x5DAC (12F7:159C) draft
    // C: both copy length + 1 bytes (the path and its end byte); the bytes after them keep what an earlier path left.
    private void SavePath()
    {
        SavedPathLength = Paths.PathLength;
        Array.Copy(Paths.Path, SavedPath, Math.Min(SavedPathLength + 1, PathFinder.MaxSteps));
    }

    // orig: path_give_saved R014+0x5DE0 (12F7:15D0) draft (the saved path copied into combatant i's, then packed)
    private void GiveSavedPath(int i) => PathFinder.GiveTo(Battle[i].Path, SavedPath, SavedPathLength);
}
