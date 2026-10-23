using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>A missile or spell in flight (DS:944D, 0x1A-byte records).</summary>
public sealed class Missile
{
    /// <summary>+08 the position, +0C where it goes.</summary>
    public MapPoint Position { get; set; }

    public MapPoint Goal { get; set; }

    /// <summary>+10: the target combatant (−1 a spot).</summary>
    public int Target { get; init; }

    /// <summary>+11: it misses (flies past) — 1; bit 7 once it arrived.</summary>
    public byte Done { get; set; }

    /// <summary>+12: the shooter's sprite facing.</summary>
    public int Facing { get; init; }

    /// <summary>+13: the shooter (−1 a trap).</summary>
    public int Shooter { get; init; }

    /// <summary>+14: the sprite (0..5 weapons, 6..9 spells).</summary>
    public int Sprite { get; set; }

    /// <summary>+15 / +16: the spell and its power.</summary>
    public int Spell { get; init; }

    public int Power { get; init; }

    /// <summary>+17: pixels per tick along the major axis.</summary>
    public int Speed { get; set; }

    /// <summary>+18: the level it flies on.</summary>
    public int Floor { get; init; }

    public bool Arrived => (Done & 0x80) != 0;
}

/// <summary>A missile that can be picked up after the fight (R136+0x3954, at most 10 kinds).</summary>
public sealed record Recoverable(int Item, int Kind, int Quality, int Count, int Pad);

public sealed partial class TacticalBattle
{
    /// <summary>missile_recs / missile_count.</summary>
    public List<Missile> Missiles { get; } = [];

    /// <summary>The thrown weapons and arrows on the ground.</summary>
    public List<Recoverable> Recoverables { get; } = [];

    /// <summary>
    /// Launches a missile from <paramref name="who"/> (−1/−2 a trap from the line start) at
    /// <paramref name="target"/> (−1 a spot): a weapon missile (type 0) by the readied weapon, a
    /// spell (type ≥ 6) by the caster's sprite family. A missing shot flies past (extend_miss).
    /// </summary>
    // orig: missile_launch R018+0x0000 (1EF8:0000) matched
    // C: traps (who −1/−2, from the line-of-sight globals) go through LaunchTrapMissile.
    // DIVERGENCE: the speed and sprite of an unknown weapon (uninitialised in the original) are 15 / 5 here.
    public void LaunchMissile(int who, int target, int type, int spell = 0, int power = 0)
    {
        if (Missiles.Count == 0x11)
        {
            Missiles.RemoveAt(0);
        }
        var c = Battle[who];
        var goal = target != -1 ? Battle[target].Position : c.Destination;
        int sprite = 5, speed = 15;
        if (type == 0)
        {
            switch (c.ReadiedWeapon)
            {
                case 0x17:
                    (sprite, speed) = (5, 15);
                    break;
                case 0x18 or 0x19:
                    (sprite, speed) = (4, 15);
                    break;
                case 0x1A:
                    (sprite, speed) = (3, 15);
                    break;
                case >= 0x1B and <= 0x1F:
                    (sprite, speed) = (0, 30);
                    _view.Sound(15);
                    break;
                case 0x20 or 0x21:
                    (sprite, speed) = (1, 120);
                    _view.Sound(11);
                    break;
            }
        }
        else
        {
            var family = SlotCodes[c.AnimationSlot];
            if (family.StartsWith('E') || family.StartsWith('e') || family.Equals("m02", StringComparison.OrdinalIgnoreCase))
            {
                (sprite, speed) = (6, 20);
            }
            else if (family.StartsWith('M') || family.StartsWith('m'))
            {
                speed = 20;
                sprite = family.Equals("m73", StringComparison.OrdinalIgnoreCase) ? 8
                    : family.Equals("m90", StringComparison.OrdinalIgnoreCase) || family.Equals("m13", StringComparison.OrdinalIgnoreCase) ? 9
                    : 7;
            }
            else
            {
                (sprite, speed) = (6, 20);
            }
        }
        var missile = new Missile
        {
            Position = c.Position, Goal = goal, Facing = c.SpriteFacing, Shooter = who, Target = target,
            Sprite = sprite, Speed = speed, Floor = Battle.FloorShown, Spell = type >= 6 ? spell : 0, Power = type >= 6 ? power : 0,
        };
        Missiles.Add(missile);
        if (type >= 6)
        {
            missile.Done = 0;
        }
        else if (c.Exchange is not (Exchange.LightHit or Exchange.HeavyHit))
        {
            missile.Done = 1;
            ExtendMiss(missile, who, target);
        }
        else
        {
            if (who < Combatant.PartySlots)
            {
                c.SkillsUsed |= SkillUse.MissileHit;
            }
            missile.Done = 0;
        }
    }

    /// <summary>
    /// A trap's dart (who −1: it hits, −2: it misses and flies on) from the start of the dart's line
    /// at <paramref name="target"/> (−1: to the line's end), its sprite and speed by the dart weapon.
    /// </summary>
    // orig: missile_launch R018+0x0000 (1EF8:0000) matched (the who &lt; 0 branch)
    // C: the record's shooter is −1 for both (a shooter of 15 after the &amp; 0xF of missiles_advance); a weapon outside the switch leaves
    // the sprite and speed uninitialised (5 / 15 here, DIVERGENCE).
    private void LaunchTrapMissile(int who, int target, int facing, int weapon, MapPoint from, MapPoint to)
    {
        if (Missiles.Count == 0x11)
        {
            Missiles.RemoveAt(0);
        }
        var (sprite, speed) = weapon switch
        {
            0x17 => (5, 15),
            0x18 or 0x19 => (4, 15),
            0x1A => (3, 15),
            >= 0x1B and <= 0x1F => (0, 30),
            0x20 or 0x21 => (1, 120),
            _ => (5, 15),
        };
        if (weapon is >= 0x1B and <= 0x1F)
        {
            _view.Sound(15);
        }
        else if (weapon is 0x20 or 0x21)
        {
            _view.Sound(11);
        }
        Missiles.Add(new Missile
        {
            Position = from, Goal = target != -1 ? Battle[target].Position : to, Facing = facing, Shooter = -1, Target = target,
            Sprite = sprite, Speed = speed, Floor = Battle.FloorShown, Done = (byte)(who == -1 ? 0 : 1),
        });
    }

    /// <summary>A miss flies past the target: up to six times the distance while the line stays clear, then bisected back from the wall.</summary>
    // orig: R018+0x02BE (1EF8:02BE) matched
    // C: after the last clear test the halved step is added and the loop may end without testing again, so a missed arrow
    // can stop past a wall (known original bug DL 0037, kept).
    private void ExtendMiss(Missile m, int who, int target)
    {
        var start = m.Position;
        int x = m.Goal.X, y = m.Goal.Y;
        int dx = x - start.X, dy = y - start.Y;
        var c = Battle[who];
        var save = c.ReadiedSkill;
        // 110 ≥ 90: people never block the line.
        c.ReadiedSkill = 0x6E;
        int lim = 6, shift = 0, i = 0;
        do
        {
            if (Map.LineBlocked(start, new MapPoint(x, y), who, target, 0x80) == 0)
            {
                dx >>= shift;
                x += dx;
                dy >>= shift;
                y += dy;
                if (lim == 16)
                {
                    break;
                }
            }
            else
            {
                if (lim == 6)
                {
                    lim = 16;
                    shift = 1;
                    i = 0;
                }
                dx >>= shift;
                x -= dx;
                dy >>= shift;
                y -= dy;
            }
        }
        while (++i <= lim);
        c.ReadiedSkill = save;
        m.Goal = new MapPoint(x, y);
    }

    /// <summary>Counts a missile that can be recovered (thrown weapons, 70% of arrows and quarrels).</summary>
    // orig: R018+0x03AE (1EF8:03AE) matched
    private void DropRecoverable(int kind, int quality)
    {
        // C: for other kinds the id and item are uninitialised; only these six are ever passed.
        var (item, pad) = kind switch
        {
            0x17 => (0x17, 1),
            0x18 => (0x18, 3),
            0x19 => (0x19, 1),
            0x1A => (1, 1),
            0x40 => (0x21, 1),
            0x41 => (0x22, 1),
            _ => (kind, 1),
        };
        var q = (sbyte)quality;
        var k = Recoverables.FindIndex(r => r.Item == item && r.Kind == kind && r.Quality == q);
        if (k >= 0)
        {
            // C: the count is a byte (cnt++ wraps).
            Recoverables[k] = Recoverables[k] with { Count = (Recoverables[k].Count + 1) & 0xFF };
            return;
        }
        if (Recoverables.Count < 10)
        {
            Recoverables.Add(new Recoverable(item, kind, q, 1, pad));
        }
    }

    /// <summary>Every missile flies on (homing on a target on its floor); on arrival a hit applies its damage.</summary>
    // orig: missiles_advance R014+0xED10 (1D47:0000) draft (spec D §4.9, asm-verified)
    // C: an arrived missile is not skipped (only its done byte keeps a hit from counting twice); the draw at the end of the tick removes
    // it after drawing it once (RemoveArrivedMissiles).
    private void AdvanceMissiles()
    {
        foreach (var m in Missiles)
        {
            var sh = m.Shooter & 15;
            var t = m.Target;
            if (t != -1 && (m.Done & 0x7F) == 0 && !m.Arrived && m.Floor == Battle[t].Floor)
            {
                m.Goal = Battle[t].Position;
            }
            m.Position = Bresenham(m.Position, m.Goal, m.Speed);
            if (m.Position != m.Goal)
            {
                continue;
            }
            if ((m.Sprite & 0x7F) < 6)
            {
                // C: a spot target (−1) reads cbt_floor[−1] (DS:A6B3); a spot shot never hits here.
                if (m.Done == 0 && t >= 0 && m.Floor == Battle[t].Floor)
                {
                    var target = Battle[t];
                    target.WasHit = true;
                    Attacks.ApplyDamage(Battle, t);
                    if (target.Status is CharacterStatus.Unconscious or CharacterStatus.Dead)
                    {
                        if (sh < Combatant.Count)
                        {
                            Battle[sh].Exchange = Exchange.None;
                            _view.RowChanged(sh);
                            Battle[sh].State = ActionState.Idle;
                            CancelAction(sh);
                        }
                        else
                        {
                            // C: a trap's missile (shooter 15) writes past the 13-entry arrays on the fixed DS layout: cbt_anim[15]
                            // is cbt_action[2] (member 2's action state is cleared, twice), cbt_special[15] is cbt_moving[0]
                            // (member 0 stops following its path); cbt_action[15] and combatants[15].f10 land in picture
                            // handles (nothing to model) and party_panel_rows is skipped for 15.
                            Battle[2].State = ActionState.Idle;
                            Battle[0].Moving = false;
                        }
                        target.Exchange = Exchange.None;
                        _view.RowChanged(t);
                    }
                }
            }
            else
            {
                // The spell or potion lands (thunk 09C0:1DC9: effect_spell with the shooter, type, quality, target and level).
                // C: the type and quality bytes are pushed zero-extended, the target and level sign-extended.
                Effects.Lands(m.Goal.X, m.Goal.Y, sh, (byte)m.Spell, (byte)m.Power, (sbyte)m.Target, (sbyte)m.Floor);
            }
            m.Done |= 0x80;
        }
    }

    /// <summary>The missiles that arrived go once drawn (the draw's missile loop calls missile_remove after queueing their sprite).</summary>
    // orig: combat_draw R014+0x6818 (14F7:0008) draft (the missile loop R014+0x6B22..0x6D4A, from the last missile down: after the sprite
    // R014+0x6D39 `if (done < 0) missile_remove(k)` via 09C0:1D79); missile_remove R018+0x027E (1EF8:027E) matched
    // C: the renderer here draws later, from the state the tick left: the removed ones are kept for it until the next tick (ArrivedShown).
    private void RemoveArrivedMissiles()
    {
        _arrivedShown.AddRange(Missiles.Where(m => m.Arrived));
        Missiles.RemoveAll(m => m.Arrived);
    }

    private readonly List<Missile> _arrivedShown = [];

    /// <summary>What the last draw showed of the missiles: those flying and those that arrived in it (removed by it).</summary>
    public IEnumerable<Missile> MissilesDrawn => Missiles.Concat(_arrivedShown);

    /// <summary>Moves a point up to <paramref name="speed"/> pixels toward the goal along the major axis (the error restarts each tick).</summary>
    // orig: R014+0xEE4E (1D47:013E) draft (asm-verified per spec D §4.9)
    private static MapPoint Bresenham(MapPoint from, MapPoint to, int speed)
    {
        int x = from.X, y = from.Y;
        int dx = to.X - x, dy = to.Y - y;
        var sx = dx >= 0 ? 1 : -1;
        var sy = dy >= 0 ? 1 : -1;
        int a = Math.Abs(dx), b = Math.Abs(dy);
        var steep = b > a;
        if (steep)
        {
            (a, b) = (b, a);
        }
        var err = 2 * b - a;
        var n = Math.Min(speed, a);
        for (var k = 0; k < n; k++)
        {
            while (err >= 0)
            {
                if (steep)
                {
                    x += sx;
                }
                else
                {
                    y += sy;
                }
                err -= 2 * a;
            }
            if (steep)
            {
                y += sy;
            }
            else
            {
                x += sx;
            }
            err += 2 * b;
        }
        return new MapPoint(x, y);
    }
}
