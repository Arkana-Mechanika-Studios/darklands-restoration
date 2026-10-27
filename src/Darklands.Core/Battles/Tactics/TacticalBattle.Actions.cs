using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>The orders that attack (f10 &amp; 0x1BE).</summary>
    private const CombatAction AttackOrders = (CombatAction)0x1BE;

    /// <summary>
    /// Every combatant's action for this tick, in two passes (the second for those pushed or
    /// killed by a higher-numbered one): stairs, finished actions, the walk step, the melee frame,
    /// the missile and spell actions, place actions, pushes and dying; then the missiles fly and
    /// the damage numbers are set.
    /// </summary>
    // orig: combat_actions R014+0x116E (0E76:116E) draft (spec D §2.1, asm-verified)
    private void CombatActions()
    {
        var again = new bool[Combatant.Count];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < Combatant.Count; i++)
            {
                var c = Battle[i];
                if ((pass == 1 && !again[i]) || c.Status == CharacterStatus.Empty)
                {
                    continue;
                }
                var set = AnimationSet(i);
                if (c.Place == PlaceAction.Stairs && Battle.FloorShown != Battle.PartyFloor)
                {
                    TakeExit(c.StairsExit, i);
                    CancelAction(i);
                    continue;
                }
                if (c.State == ActionState.Finished || c.Place == PlaceAction.Halt)
                {
                    CancelAction(i);
                    _view.RowChanged(i);
                    SetPose(i, Pose.Walking);
                    c.Moving = false;
                    c.State = ActionState.Idle;
                }
                if ((c.Action & CombatAction.Walk) != 0)
                {
                    Step(i, set);
                }
                if ((c.Action & CombatAction.Melee) != 0 && c.State == ActionState.Swinging && c.NextFrameAt <= Clock)
                {
                    MeleeFrame(i, set, again);
                }
                if ((c.Action & CombatAction.Missile) != 0)
                {
                    MissileAction(i, set);
                }
                if ((c.Action & CombatAction.UseItem) != 0)
                {
                    SpellAction(i, set);
                }
                if (c.Place != PlaceAction.None)
                {
                    DoPlaceAction(i);
                }
                if (c.Exchange == Exchange.PushedBack)
                {
                    PushBackStep(i, set, again);
                }
                if (c.Exchange == Exchange.PusherFollowing)
                {
                    PusherFollowStep(i, set);
                }
                if (c.Status is CharacterStatus.Unconscious or CharacterStatus.Dead)
                {
                    Dying(i, set);
                }
            }
        }
        AdvanceMissiles();
        foreach (var c in Battle.Combatants)
        {
            if (c.IsAble && c.WasHit)
            {
                // C: a signed byte compare: at most 42 shown.
                c.DamageNumber = Math.Min((int)c.DamageShown, 42);
                c.DamageNumberUntil = (ushort)(Clock + 150);
                c.DamageShown = 0;
                c.WasHit = false;
            }
        }
        if (_redraw)
        {
            _view.MapChanged();
            _redraw = false;
        }
    }

    /// <summary>A debugging aid (DARKLANDS_STEP_TRACE): each combatant's step call, as tools/oracle/lockstep.py compares it.</summary>
    public static Action<string>? StepTrace { get; set; } = Environment.GetEnvironmentVariable("DARKLANDS_STEP_TRACE") is null ? null : Console.WriteLine;

    /// <summary>One step along the path (or the step queued by a key).</summary>
    // orig: cbt_step R014+0x1624 (0E76:1624) draft (spec D §2.3, asm-verified)
    private void Step(int i, int set)
    {
        var c = Battle[i];
        StepTrace?.Invoke($"step {i} clock {Clock} queued {c.QueuedStep} next {c.NextStepAt} delay {c.StepDelay} held {c.Held} wait {c.WaitSteps} moving {c.Moving} at {c.Position.X},{c.Position.Y}");
        if (c.QueuedStep == -1)
        {
            return;
        }
        var now = Clock;
        // C: the clock-rebase guard: an unsigned compare of (step delay + now) with the step time.
        if ((ushort)(c.StepDelay + now) < c.NextStepAt)
        {
            c.NextStepAt = now;
        }
        if (c.NextStepAt > now)
        {
            return;
        }
        if (c.Held)
        {
            c.RepathAt = (ushort)(Scaled(90) + Clock + 30);
            return;
        }
        if (MoveMode == 2 && MarchingOrder.Count > 0 && MarchingOrder[0] == i)
        {
            FollowTheHead();
        }
        if (c.WaitSteps != 0)
        {
            c.WaitSteps--;
            c.RepathAt = (ushort)(Scaled(90) + Clock + 30);
            return;
        }
        c.NextStepAt = (ushort)(c.StepDelay + Clock);
        SetPose(i, Pose.Walking);
        if (Effects.AreaCount > 0)
        {
            // Slowing areas: the walker steps only on the ticks the rotating speed mask of the strongest area allows.
            var best = Effects.SlowingStrength(i);
            if (best != 0 && (_speedMasks[best] & 1) == 0)
            {
                return;
            }
        }
        var st = c.QueuedStep;
        c.Facing = st & 0x0F;
        c.SpriteFacing = FacingOfDirection[(st >> 4) & 0x0F];
        var saved = (c.Position, c.Frame);
        var ok = false;
        var stepX = Slots[set].StepX;
        var stepY = Slots[set].StepY;
        c.Position = new MapPoint(c.Position.X + stepX[c.Facing % 10], c.Position.Y + stepY[c.Facing % 10]);
        if (Map.Collides(i, c.Position.X, c.Position.Y).IsFree)
        {
            ok = true;
            // C: a signed idiv: ((f1b + 1) − 1) % n + 1 with n = frames − 1 (a one-frame walk would divide by zero).
            var n = c.SequenceLength - 1;
            c.Frame = n > 0 ? c.Frame % n + 1 : 0;
            if (c.Moving)
            {
                c.PathIndex++;
                c.RepathAt = (ushort)(Scaled(90) + Clock + 30);
                c.BlockedBy = 0;
            }
        }
        else if (c.Moving)
        {
            if (c.Target != -1 && Map.LastParts[c.Target] != 0)
            {
                // Blocked by its own target: engage.
                c.Position = saved.Position;
                c.Frame = saved.Frame;
                ok = true;
                Arrive(i);
                c.EngageAt = Clock;
                c.State = ActionState.Ready;
            }
            else
            {
                c.BlockedBy = Map.LastMask & 0x1FFF;
            }
        }
        if (!ok)
        {
            (c.Position, c.Frame) = saved;
        }
        c.QueuedStep = -1;
    }

    /// <summary>Single file: before the head steps, every follower re-paths to the one in front.</summary>
    // orig: cbt_step R014+0x1624 (0E76:1624) draft (the single-file block, spec D §2.3)
    private void FollowTheHead()
    {
        for (var k = 1; k < MarchingOrder.Count; k++)
        {
            var a = Battle[MarchingOrder[k - 1]];
            var b = Battle[MarchingOrder[k]];
            b.Destination = a.Position;
            b.Target = (sbyte)a.Index;
            if (FindPath(b.Index))
            {
                GivePath(b.Index);
                b.PathIndex = 0;
                b.RepathAt = (ushort)(Scaled(60) + Clock + 30);
                b.Moving = true;
                b.Action = CombatAction.Walk;
                // orig: cbt_step R014+0x1738..0x1747 DS:CA30[b] = the path byte; 0xFF (the path ended at once: the follower stands next to the
                // one in front) is the byte of "no step", which cbt_step's first test (R014+0x162D) returns on: QueuedStep is −1 for that here
                b.QueuedStep = b.Path[b.PathIndex] == 0xFF ? -1 : b.Path[b.PathIndex];
            }
        }
        if (!Battle.RealCombat)
        {
            _view.RowChanged(-1);
        }
    }

    /// <summary>The attack animation advances one frame; on the last frame the blow lands (or swishes).</summary>
    // orig: melee_frame R014+0x199C (0E76:199C) draft (spec D §2.4, asm-verified)
    private void MeleeFrame(int i, int set, bool[] again)
    {
        var c = Battle[i];
        c.NextFrameAt = (ushort)(c.FrameDelay + Clock);
        SetPose(i, Pose.Fighting);
        c.Frame++;
        if (i < Combatant.PartySlots)
        {
            c.SkillsUsed |= SkillUse.HandWeapon;
        }
        if (c.Frame != c.SequenceLength - 1)
        {
            return;
        }
        if (Battle.DragonFight != 0 && i == 5)
        {
            DragonMayBreathe(c);
        }
        var t = c.Target;
        if (c.Exchange is not (Exchange.LightHit or Exchange.HeavyHit) || t < 0)
        {
            _view.Sound(0x11);
            c.State = ActionState.Ready;
            return;
        }
        var target = Battle[t];
        if (c.Exchange == Exchange.HeavyHit && target.Target == i && Slots[set].CanBePushed && SetOf(t).CanBePushed)
        {
            // A heavy blow between two who fight each other knocks the target back.
            target.Exchange = Exchange.PushedBack;
            target.Pusher = i;
            // C: the attacker's own queued endurance damage is halved (a signed byte sar), not the target's.
            c.QueuedEndurance >>= 1;
            target.Frame = -1;
            target.PushDirection = PushDirection(i);
            c.State = ActionState.KnockBack;
            target.State = ActionState.KnockBack;
            c.PushSteps = 0;
            _view.Sound(0x10);
            if (t < i)
            {
                again[t] = true;
            }
        }
        else
        {
            c.State = ActionState.Ready;
        }
        if (i < Combatant.PartySlots)
        {
            c.SkillsUsed |= SkillUse.HandWeaponHit;
        }
        if (target.QueuedEndurance > 0)
        {
            target.WasHit = true;
        }
        ApplyDamage(t);
        if (target.Status is CharacterStatus.Unconscious or CharacterStatus.Dead)
        {
            c.Exchange = Exchange.None;
            target.Exchange = Exchange.None;
            c.State = Battle.DragonFight != 0 && i == 5 ? ActionState.Ready : ActionState.Idle;
            _view.RowChanged(i);
            _view.RowChanged(t);
            if (t < i)
            {
                again[t] = true;
            }
        }
    }

    /// <summary>The missile weapon: draw and loose, reload, aim.</summary>
    /// <param name="set">The animation set combat_actions worked out at the head of the combatant's pass (R014+0x11C4, pushed at R014+0x1288).</param>
    // orig: cbt_missile_action R014+0x1BA0 (0E76:1BA0) draft (spec D §2.5, asm-verified); the aim sequence and its length are the caller's set's
    // (R014+0x1E3F: DS:C8A7[set], R014+0x1E67: DS:A3EC + 30 * set)
    private void MissileAction(int i, int set)
    {
        var c = Battle[i];
        var now = Clock;
        if (c.State == ActionState.Swinging)
        {
            if (c.NextFrameAt > now)
            {
                return;
            }
            c.NextFrameAt = (ushort)(c.FrameDelay + now);
            SetPose(i, Pose.Fighting);
            c.Frame++;
            var skip = (c.SkillsUsed & SkillUse.Missile) != 0 && (c.Flags & TacticalFlags.ShotSkipped) != 0;
            c.Flags &= ~TacticalFlags.ShotSkipped;
            if (!skip)
            {
                if (c.Frame != c.SequenceLength - 1)
                {
                    return;
                }
                LaunchMissile(i, c.Target, 0);
                if (i < Combatant.PartySlots)
                {
                    c.SkillsUsed |= SkillUse.Missile;
                }
                if (c.ReadiedWeapon is >= 0x17 and <= 0x1A)
                {
                    DropRecoverable(c.ReadiedWeapon, c.MissileQuality);
                }
                // The draw is always made: 70% of arrows and quarrels can be picked up afterwards.
                if (Scaled(10) < 7)
                {
                    var ammunition = Battle.Tables.AmmunitionType(c.ReadiedWeapon);
                    if (ammunition is 0x40 or 0x41)
                    {
                        DropRecoverable(ammunition, 0x19);
                    }
                }
                if (Attacks.UseAmmunition(Battle, i, RanOutOfAmmunition) == -1)
                {
                    // use_ammo readied the hand weapon already; again here only to record its animation code.
                    ReadyWeapon(i, c.DefaultWeapon, 0);
                    c.State = ActionState.Finished;
                    return;
                }
            }
            // C: draws in this order: reload start, reload length, aim length.
            c.State = ActionState.Reloading;
            c.ReloadStartAt = (ushort)(Scaled(10) + Clock + 10);
            c.AimStartAt = (ushort)((byte)c.WeaponRange * 4 + c.ReloadStartAt + Scaled(10));
            c.AimEndAt = (ushort)((byte)c.AimDelay + c.AimStartAt + Scaled(10));
            c.LoopSetUp = false;
            c.AimSetUp = false;
            return;
        }
        if (c.State == ActionState.Reloading && c.ReloadStartAt <= Clock && Clock < c.AimStartAt)
        {
            PlayLoop(c);
            return;
        }
        if ((c.State == ActionState.Aiming && Clock < c.AimEndAt) || (c.State == ActionState.Reloading && c.AimStartAt <= Clock))
        {
            c.State = ActionState.Aiming;
            if (c.NextFrameAt <= now)
            {
                c.NextFrameAt = (ushort)(c.FrameDelay + now);
                var slot = Slots[set];
                if (!c.AimSetUp)
                {
                    c.AimSetUp = true;
                    var n = slot.AimLength & 0x7F;
                    for (var k = 0; k < n && k < c.Sequence.Length; k++)
                    {
                        c.Sequence[k] = slot.Sequences[5 + k];
                    }
                    c.SequenceLength = n;
                    c.RestartLoop = true;
                }
                if (c.RestartLoop)
                {
                    c.Frame = -1;
                    c.RestartLoop = false;
                }
                if (c.Frame < c.SequenceLength - 1)
                {
                    c.Frame++;
                }
                if ((slot.AimLength & 0x7F) - 1 <= c.Frame && (slot.AimLength & 0x80) != 0)
                {
                    c.RestartLoop = true;
                }
            }
            return;
        }
        if (c.State == ActionState.Aiming && c.AimEndAt <= Clock)
        {
            c.State = ActionState.Ready;
        }
    }

    /// <summary>The reload / chant loop: frames 0, 1, 0, 1 …</summary>
    private void PlayLoop(Combatant c)
    {
        if (c.NextFrameAt > Clock)
        {
            return;
        }
        c.NextFrameAt = (ushort)(c.FrameDelay + Clock);
        if (!c.LoopSetUp)
        {
            c.Sequence[0] = 0;
            c.Sequence[1] = 1;
            c.LoopSetUp = true;
            c.RestartLoop = true;
        }
        if (c.RestartLoop)
        {
            c.Frame = -1;
            c.RestartLoop = false;
        }
        if (++c.Frame == 1)
        {
            c.RestartLoop = true;
        }
    }

    /// <summary>use_ammo's caller part when the ammunition ran out: an enemy archer fights hand to hand.</summary>
    private void RanOutOfAmmunition(int i)
    {
        if (i >= Combatant.PartySlots)
        {
            BackToMelee(i);
        }
        _fireBlocked[i] = 2;
        _view.RowChanged(i);
    }

    /// <summary>A spell: chant, then the cast frames, then the spell flies.</summary>
    /// <param name="set">The animation set combat_actions worked out at the head of the combatant's pass (R014+0x11C4, pushed at R014+0x12A0).</param>
    // orig: spell_action R014+0x1F00 (0E76:1F00) draft (spec D §2.6, asm-verified); the cast sequence is the caller's set's (R014+0x203E)
    private void SpellAction(int i, int set)
    {
        var c = Battle[i];
        if (c.State == ActionState.Swinging)
        {
            c.State = ActionState.Chanting;
            c.ReloadStartAt = (ushort)(Scaled(40) + Clock + 90);
            c.LoopSetUp = false;
            if (Battle.DragonFight != 0 && i == 5)
            {
                // C: the dragon breathes at once (no chant; R014+0x1F33..0x1F50).
                c.State = ActionState.Casting;
                c.Frame = 0;
            }
        }
        if (c.State == ActionState.Chanting && Clock < c.ReloadStartAt)
        {
            if (c.NextFrameAt <= Clock)
            {
                SetPose(i, Pose.Fighting);
                PlayLoop(c);
            }
            return;
        }
        if ((c.State == ActionState.Chanting && c.ReloadStartAt <= Clock) || c.State == ActionState.Casting)
        {
            var slot = Slots[set];
            if (c.State != ActionState.Casting)
            {
                c.State = ActionState.Casting;
                var n = slot.AttackLength;
                c.SequenceLength = n;
                for (var k = 0; k < n && k < c.Sequence.Length; k++)
                {
                    c.Sequence[k] = slot.Sequences[k];
                }
                c.Frame = -1;
            }
            if (c.NextFrameAt <= Clock)
            {
                c.NextFrameAt = (ushort)(c.FrameDelay + Clock);
                SetPose(i, Pose.Fighting);
                c.Frame++;
                if (c.SequenceLength - c.Frame == 1)
                {
                    LaunchMissile(i, c.Target, 6, c.Spell, c.SpellPower);
                    Effects.UseUpPotion(i, c.Spell, c.SpellPower);
                    c.State = ActionState.Finished;
                    if (Battle.DragonFight != 0 && i == 5)
                    {
                        // C: the dragon goes back to its claws: state 7, action 0x12, no victim, its fighting animation
                        // (R014+0x2115..0x2150: DS:CA69, DS:9EE5, DS:9F1F, anim_file[5] = DS:17DC / DS:17DF).
                        c.State = ActionState.Ready;
                        c.Action = CombatAction.StandardAttack;
                        c.Target = -1;
                        SlotWeapons[5] = ClawCode(Battle.DragonFight);
                        return;
                    }
                    if (i < Combatant.PartySlots)
                    {
                        c.SkillsUsed |= SkillUse.Thrown;
                        return;
                    }
                    c.Target = -1;
                }
            }
        }
    }

    /// <summary>cbt_cancel_action: no place action, no order, no exchange.</summary>
    // orig: cbt_cancel_action R014+0x27AC (0E76:27AC) draft
    private void CancelAction(int i)
    {
        var c = Battle[i];
        c.Place = PlaceAction.None;
        c.Action = CombatAction.None;
        c.Exchange = Exchange.None;
    }

    /// <summary>The enemies fighting combatant i stop (it left the field).</summary>
    // orig: cbt_forget_target R014+0x27CC (0E76:27CC) draft
    private void ForgetTarget(int i)
    {
        for (var k = Combatant.PartySlots; k < Combatant.Count; k++)
        {
            var e = Battle[k];
            if (e.Target == i)
            {
                e.Target = -1;
                e.Action = CombatAction.None;
                e.Exchange = Exchange.None;
                e.Moving = false;
            }
        }
    }

    /// <summary>Pushed back: one walk step backwards per tick through the 3-frame hit sequence; attackers follow.</summary>
    // orig: combat_actions R014+0x116E (0E76:116E) draft (push_back_step, spec D §2.1, asm-verified)
    private void PushBackStep(int i, int set, bool[] again)
    {
        var c = Battle[i];
        var p = c.Pusher;
        SetPose(i, Pose.Hit);
        c.Facing = c.PushDirection;
        c.SpriteFacing = PushFacing[c.PushDirection];
        var saved = (c.Position, c.Frame);
        var ok = false;
        c.Position = new MapPoint(c.Position.X + Slots[set].StepX[c.Facing % 10], c.Position.Y + Slots[set].StepY[c.Facing % 10]);
        Map.StrictWalls = true;
        if (Map.Collides(i, c.Position.X, c.Position.Y).IsFree)
        {
            ok = true;
            Battle[p].Exchange = Exchange.PusherWaiting;
            Battle[p].PushSteps++;
        }
        Map.StrictWalls = false;
        if (!ok)
        {
            (c.Position, c.Frame) = saved;
        }
        if (++c.Frame == 1)
        {
            c.Exchange = Exchange.AfterPush;
            c.EngageAt = (ushort)(c.EngageAt + 60);
            c.State = ActionState.Ready;
            var pusher = Battle[p];
            if (ok || pusher.Exchange == Exchange.PusherWaiting)
            {
                pusher.Exchange = Exchange.PusherFollowing;
                if (p < i)
                {
                    again[p] = true;
                }
            }
            else
            {
                pusher.State = ActionState.Ready;
                pusher.Exchange = Exchange.Parried;
            }
            var (lo, hi) = i < Combatant.PartySlots ? (Combatant.PartySlots, Combatant.Count) : (0, Combatant.PartySlots);
            for (var j = lo; j < hi; j++)
            {
                var a = Battle[j];
                if (a.Target == i && c.Target != j && !a.Moving && (a.Action & CombatAction.Melee) != 0 && FindPath(j))
                {
                    GivePath(j);
                    a.Moving = true;
                    a.PathIndex = 0;
                    a.ClearOrders();
                    a.QueueOrder(a.Action);
                    a.Action = CombatAction.Walk;
                    a.Pose = Pose.Reload;
                    SetPose(j, Pose.Walking);
                }
            }
        }
        c.Facing = c.SpriteFacing;
    }

    /// <summary>The pusher makes one follow-up step after the one it pushed.</summary>
    // orig: combat_actions R014+0x116E (0E76:116E) draft (pusher_follow_step, spec D §2.1, asm-verified)
    private void PusherFollowStep(int i, int set)
    {
        var c = Battle[i];
        SetPose(i, Pose.Walking);
        var saved = (c.Position, c.Frame);
        var ok = false;
        c.Position = new MapPoint(c.Position.X + Slots[set].StepX[c.Facing % 10], c.Position.Y + Slots[set].StepY[c.Facing % 10]);
        c.FollowSteps++;
        Map.StrictWalls = true;
        if (Map.Collides(i, c.Position.X, c.Position.Y).IsFree)
        {
            ok = true;
            c.PushSteps--;
            c.Frame++;
            c.RepathAt = (ushort)(Clock + 30);
        }
        Map.StrictWalls = false;
        if (!ok)
        {
            (c.Position, c.Frame) = saved;
        }
        if (c.FollowSteps == 1)
        {
            c.Exchange = Exchange.Parried;
            c.State = ActionState.Ready;
        }
    }

    /// <summary>Falling, then death: the corpse is processed once (loot pile, waves, shooters stop).</summary>
    // orig: cbt_dying R014+0x2324 (0E76:2324) draft (spec D §2.8, asm-verified)
    private void Dying(int i, int set)
    {
        var c = Battle[i];
        SetPose(i, Pose.Dying);
        c.Action = CombatAction.None;
        c.Exchange = Exchange.None;
        c.State = ActionState.Idle;
        if (c.Frame < c.SequenceLength - 1)
        {
            c.Frame++;
        }
        // R014+0x2363 / 0x2415: the set the caller worked out before the dying pose (DS:C83F[set], DS:C803[set]), not the dying pose's
        var slot = Slots[set];
        if (slot.CorpseKind == 2)
        {
            return;
        }
        if (c.Status != CharacterStatus.Dead && i < Combatant.PartySlots)
        {
            return;
        }
        if (c.Frame != c.SequenceLength - 1 || (c.Flags & TacticalFlags.CorpseDone) != 0)
        {
            return;
        }
        if ((c.Flags & TacticalFlags.DeathSounded) != 0)
        {
            c.Flags |= TacticalFlags.CorpseDone;
            if (c.Floor != BattleEnd.LeftThroughExit)
            {
                _redraw = true;
            }
            if (i >= Combatant.PartySlots)
            {
                c.Status = CharacterStatus.Empty;
                ReleaseEnemySlot(i, c.AnimationSlot);
            }
            return;
        }
        c.Flags |= TacticalFlags.DeathSounded;
        _view.Sound(10);
        if (c.EndsBattleWhenKilled)
        {
            Battle.LeaveAfterVictory = true;
        }
        LeavePile(c, slot);
        if (WavesEnabled && i >= Combatant.PartySlots && WaveGroup is { } wave)
        {
            WaveKills++;
            if (WaveKills == wave.ReinforcementBatch && wave.ReinforcementPool > 0)
            {
                WaveKills = 0;
                wave.ReinforcementPool -= wave.ReinforcementBatch;
                if (wave.ReinforcementPool < 0)
                {
                    wave.ReinforcementBatch += wave.ReinforcementPool;
                }
                PlaceEnemies(1, null, -1);
            }
        }
        for (var k = 0; k < Combatant.Count; k++)
        {
            var s = Battle[k];
            if (s.IsAble && s.Target == i && (s.Action == CombatAction.Missile || s.Action == CombatAction.UseItem) && !(Battle.DragonFight != 0 && k == 5))
            {
                s.State = ActionState.Finished;
                s.Target = -1;
            }
        }
    }

}
