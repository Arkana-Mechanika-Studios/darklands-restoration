using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

// The effects on the field: what the effects do: a potion or spell landing, clouds and markers acting on the combatants, the modifiers and holds and their end, the throw list, the cloud and line tests. The slots and the state are in TacticalBattle.Effects.cs.
public sealed partial class EffectField
{
    /// <summary>
    /// effect-cast: an effect of <paramref name="type"/> lands at (x, y) — a slowing area (9), a
    /// flash or blast marker (6..8), or a cloud (any other type) — and acts at once on whoever
    /// is in it on <paramref name="level"/>.
    /// </summary>
    /// <param name="thrower">The thrower (p3; for the flash: whose opponents are blinded).</param>
    /// <param name="quality">The potion's quality (clamped to 20..49: three grades).</param>
    /// <param name="target">The combatant aimed at (−1 a spot): it gets the stronger effect.</param>
    // orig: effect-cast R019+0x0000 (1EF8:0000) matched; tests: Oracle:effects.json
    /// <summary>The level an effect is made on: the level shown (R019+0x0000 / 0x0300), or with FixCombatAcrossLevels its missile's.</summary>
    // BUG: OriginalBehaviour.FixCombatAcrossLevels - a spell or potion effect lands on the level SHOWN, whatever level its missile flew on
    //   (split audit row 7)
    private int EffectLevel(int level) => Battle.Behaviour.FixCombatAcrossLevels && level >= 0 ? level : Battle.LevelShown;

    public void Lands(int x, int y, int thrower, int type, int quality, int target, int level)
    {
        // C: root 0410:001A clamps signed.
        var dur = Math.Clamp(quality, 0x14, 0x31);
        Landed?.Invoke();
        if (type == 9)
        {
            if (AreaCount >= 8)
            {
                return;
            }
            ref var a = ref _areas[AreaCount];
            a.X = x;
            a.Y = y;
            a.Size = 0x49;
            a.Level = (sbyte)EffectLevel(level);
            switch (dur / 10)
            {
                case 2:
                    a.Strength = 4;
                    break;
                case 3:
                    a.Strength = 5;
                    break;
                case 4:
                    a.Strength = 6;
                    break;
            }
            a.Until = (ushort)(Unsigned(0x221D + dur / 10) * 60 + Clock);
            AreaCount++;
            _sound(0x0C);
            return;
        }
        if (type is >= 6 and <= 8)
        {
            if (MarkerCount >= 8)
            {
                return;
            }
            ref var m = ref _markers[MarkerCount];
            m.X = x;
            m.Y = y;
            m.Frame = 0x40;
            m.Level = (sbyte)EffectLevel(level);
            m.Kind = (byte)(type - 6);
            MarkerActs(++MarkerCount - 1, type, thrower, dur, target, level);
            _sound(0x0D);
            return;
        }
        if (CloudCount >= 8)
        {
            int i;
            for (i = 0; i < CloudCount; i++)
            {
                if (_clouds[i].Kind != 2)
                {
                    break;
                }
            }
            if (i == CloudCount)
            {
                return;
            }
            RemoveCloud(i);
            ref var r = ref _clouds[CloudCount];
            r.X = x;
            r.Y = y;
            r.Kind = (byte)type;
            r.Size = Unsigned(0x21DC + type);
            // C: the level byte is not written on this path: the slot keeps the level of the cloud that was shifted down into it.
        }
        else
        {
            ref var r = ref _clouds[CloudCount];
            r.X = x;
            r.Y = y;
            r.Kind = (byte)type;
            r.Size = Unsigned(0x21DC + type);
            r.Level = (sbyte)EffectLevel(level);
        }
        _clouds[CloudCount].Until = (ushort)(Unsigned(0x2202 + type * 3 + dur / 10) * 60 + Clock);
        CloudActs(++CloudCount - 1, dur, target, level);
        _sound(0x0C);
    }

    /// <summary>The cloud acts on everybody of the level inside it (and in sight of its centre) who does not resist its type.</summary>
    // orig: R019+0x0300 (1EF8:0300) matched; tests: Oracle:effects.json
    private void CloudActs(int n, int dur, int target, int level)
    {
        var kind = _clouds[n].Kind;
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status != CharacterStatus.Able || c.Level != level || !Inside(_clouds[n].X, _clouds[n].Y, _clouds[n].Size, i)
                || (c.EffectImmunity & (1 << kind)) != 0)
            {
                continue;
            }
            var lvl = dur / 10 - 2;
            var t = dur / 10 + kind * 3 - 2;
            c.FlashUntil = (ushort)(Unsigned(0x21E6 + t) * 60 + Clock);
            c.Flash[1] = FlashSprite1;
            c.Flash[2] = FlashSprite2;
            c.Flash[0] = -(Unsigned(0x21DC + kind) >> 4);
            switch (kind)
            {
                case 0:
                    EndAttributeModifier(i);
                    EndSkillModifier(i);
                    var pct = target == i ? 0x32 : 0x19;
                    LowerAttributes(i, pct, t);
                    LowerSkills(i, pct, t);
                    break;
                case 1:
                    EndAttributeModifier(i);
                    EndSkillModifier(i);
                    if (target == i)
                    {
                        EndHold(i);
                        LowerAttributes(i, 0x21, t);
                        LowerSkills(i, 0x21, t);
                        Hold(i, t);
                    }
                    else
                    {
                        LowerAttributes(i, 0x19, t);
                        LowerSkills(i, 0x19, t);
                    }
                    break;
                case 3:
                    EndSkillModifier(i);
                    LowerSkills(i, (c.ArmourValue[1] + c.ArmourValue[0]) * 5, t);
                    break;
                case 4:
                    {
                        var d = Battle.RollOneTo(8) + (lvl + 2) * 2;
                        if (c.ArmourType[0] != -1)
                        {
                            WearItem(i, c.ArmourType[0], (byte)c.ArmourQuality[0], d);
                            c.ArmourQuality[0] = (sbyte)(c.ArmourQuality[0] - d);
                        }
                        if (c.ArmourType[1] != -1)
                        {
                            WearItem(i, c.ArmourType[1], (byte)c.ArmourQuality[1], d);
                            c.ArmourQuality[1] = (sbyte)(c.ArmourQuality[1] - d);
                        }
                        if (c.ArmourQuality[0] <= 0 || c.ArmourType[0] == -1)
                        {
                            if (c.ArmourType[0] != -1)
                            {
                                d = -c.ArmourQuality[0];
                            }
                            Attacks.QueueDamage(Battle, d, i, 1, 0);
                            ApplyDamage(i);
                        }
                        DropWornOut(i);
                        break;
                    }
                case 5:
                    {
                        var d = (lvl + 2) * 2 + Battle.RollOneTo(8);
                        Attacks.QueueDamage(Battle, ((lvl + 2) * 10 - c.MaximumEndurance) / 10 + d, i, c.ArmourValue[0] + 1, c.ArmourValue[0]);
                        ApplyDamage(i);
                        break;
                    }
            }
        }
    }

    /// <summary>A flash (6: the thrower's opponents who look toward it are held) or a blast (7 Thunderbolt, 8 Arabian Fire: damage, armour worn).</summary>
    // orig: R019+0x058A (1EF8:058A) matched; tests: Oracle:effects.json
    private void MarkerActs(int n, int type, int thrower, int dur, int target, int level)
    {
        var m = _markers[n];
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status != CharacterStatus.Able || c.Level != level || (c.EffectImmunity & (1 << type)) != 0)
            {
                continue;
            }
            var lvl = dur / 10 - 2;
            var idx = Math.Min(lvl, 2) + type * 3;
            if (type == 6)
            {
                if ((thrower < Combatant.PartySlots && i >= Combatant.PartySlots) || (thrower >= Combatant.PartySlots && i < Combatant.PartySlots))
                {
                    int dx = m.X - c.Position.X, dy = m.Y - c.Position.Y;
                    int adx = Math.Abs(dx), ady = Math.Abs(dy);
                    var r = adx != 0 ? ady * 4 / adx : 0x14;
                    var s = r == 0 ? 0 : r is >= 1 and < 4 ? 1 : 2;
                    var q = dx >= 0 && dy < 0 ? 0 : dx >= 0 ? 1 : dy >= 0 ? 2 : 3;
                    // C: heading-add (root 0410:005A) subtracts and heading-subtract adds (the names are swapped; checked against the asm):
                    // d = (toward − 1) − (facing − 1), all mod 8, as a signed byte above 4.
                    var toward = (Unsigned(0x222E + q * 3 + s) - 1) & 7;
                    var facing = (c.SpriteFacing - 1) & 7;
                    var d = (toward - facing) & 7;
                    if (d > 4)
                    {
                        d -= 8;
                    }
                    if (Math.Abs(d) <= 2 || target == i)
                    {
                        Hold(i, idx);
                        c.FlashUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
                        c.Flash[1] = FlashSprite1;
                        c.Flash[2] = FlashSprite2;
                        c.Flash[0] = -2;
                    }
                }
                continue;
            }
            if (!Inside(m.X, m.Y, m.Frame, i))
            {
                continue;
            }
            // C: the distance is taken from x twice (dx / 2 and dx, no dy): faithful.
            var near = Distance((m.X - c.Position.X) / 2, m.X - c.Position.X, 0);
            c.FlashUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
            c.Flash[1] = FlashSprite1;
            c.Flash[2] = FlashSprite2;
            c.Flash[0] = -4;
            int dmg;
            switch (type)
            {
                case 7:
                    {
                        dmg = Battle.RollOneTo(8) + (lvl + 2) * 2;
                        var th = target == i ? Signed(0x2222 + lvl) : near <= 0x10 ? Signed(0x2226 + lvl) : Signed(0x222A + lvl);
                        int sk = c.ArmourValue[0];
                        var r = sk < th ? dmg : sk == th ? dmg / 3 : dmg / 8;
                        Attacks.QueueDamage(Battle, ((lvl + 2) * 10 - c.ArmourQuality[0]) / 10 + r, i, th, sk);
                        ApplyDamage(i);
                        break;
                    }
                case 8:
                    dmg = Battle.RollOneTo(6) + (lvl + 2) * 2;
                    if (near > 0x10)
                    {
                        dmg >>= 2;
                    }
                    Attacks.QueueDamage(Battle, (c.ArmourQuality[0] + c.PiercingResistance) / -10 + dmg, i, c.ArmourValue[0] + 1, c.ArmourValue[0]);
                    ApplyDamage(i);
                    break;
                default:
                    continue;
            }
            if (dmg >> 2 > 0)
            {
                dmg >>= 2;
                WearItem(i, c.ArmourType[0], (byte)c.ArmourQuality[0], dmg);
                WearItem(i, c.ArmourType[1], (byte)c.ArmourQuality[1], dmg);
                c.ArmourQuality[0] = (sbyte)(c.ArmourQuality[0] - dmg);
                c.ArmourQuality[1] = (sbyte)(c.ArmourQuality[1] - dmg);
                DropWornOut(i);
            }
        }
    }

    /// <summary>three-way-distance with its third word (always 0 here).</summary>
    private static int Distance(int a, int b, int c)
    {
        _ = c;
        return TacticalBattle.Distance(a, b);
    }

    /// <summary>Removes cloud n (the later ones move down; the last slot keeps its old contents).</summary>
    // orig: R019+0x01E2 (1EF8:01E2) matched
    // C: the renderer's last boxes DS:C8F7/DS:C915/DS:C897/DS:C8BA move with the records (the growths DS:8574.. stay by slot).
    private void RemoveCloud(int n)
    {
        if (CloudCount == 0)
        {
            return;
        }
        for (var i = n; i < CloudCount - 1; i++)
        {
            _clouds[i] = _clouds[i + 1];
            _lastBoxW[i] = _lastBoxW[i + 1];
            _lastBoxH[i] = _lastBoxH[i + 1];
            _lastBoxX[i] = _lastBoxX[i + 1];
            _lastBoxY[i] = _lastBoxY[i + 1];
        }
        CloudCount--;
    }

    // orig: R019+0x0260 (1EF8:0260) matched
    private void RemoveArea(int n)
    {
        if (AreaCount == 0)
        {
            return;
        }
        for (var i = n; i < AreaCount - 1; i++)
        {
            _areas[i] = _areas[i + 1];
        }
        AreaCount--;
    }

    // orig: R019+0x02B4 (1EF8:02B4) matched
    private void RemoveMarker(int n)
    {
        if (MarkerCount == 0)
        {
            return;
        }
        for (var i = n; i < MarkerCount - 1; i++)
        {
            _markers[i] = _markers[i + 1];
        }
        MarkerCount--;
    }

    /// <summary>Armour worn through (quality 0 or less) is taken off and thrown away.</summary>
    // orig: R019+0x0914 (1EF8:0914) matched
    private void DropWornOut(int i)
    {
        var c = Battle[i];
        for (var k = 0; k <= 1; k++)
        {
            if (c.ArmourQuality[k] <= 0)
            {
                // BUG: items are consumed until none of that type and quality is left: the spares stacked with the worn piece
                // (worn down with it by WearItem) are thrown away with it.
                while (Attacks.ConsumeItem(Battle, i, (byte)c.ArmourType[k], (byte)c.ArmourQuality[k]) != -1)
                {
                }
                c.ArmourType[k] = -1;
                c.ArmourValue[k] = 0;
                c.ArmourQuality[k] = 0x19;
            }
        }
    }

    /// <summary>The first carried item of type <paramref name="code"/> and quality <paramref name="quality"/> loses <paramref name="amount"/> quality.</summary>
    // orig: R019+0x0968 (1EF8:0968) matched
    // C: the type is compared as an int with the item's unsigned byte (a type of −1 never matches); the quality is an
    // unsigned byte compare and subtraction.
    private void WearItem(int i, int code, byte quality, int amount)
    {
        var items = Battle[i].Items;
        for (var k = 0; k < items.Count; k++)
        {
            if ((byte)items[k].Type == code && (byte)items[k].Quality == quality)
            {
                // BUG: armour wear hits the whole stack: the stack loses the quality, not one piece, so identical spare
                // pieces stacked with the worn armour wear down with it.
                items[k] = items[k] with { Quality = (byte)(items[k].Quality - amount) };
                return;
            }
        }
    }

    /// <summary>The attribute modifier ends: agility and perception get back what was taken.</summary>
    // orig: R019+0x0A7C (1EF8:0A7C) matched
    private void EndAttributeModifier(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        c.Attributes[2] = (sbyte)(c.Attributes[2] + m.Attributes[0]);
        m.Attributes[0] = 0;
        c.Attributes[3] = (sbyte)(c.Attributes[3] + m.Attributes[1]);
        m.Attributes[1] = 0;
        m.AttributesUntil = EffectModifiers.Off;
    }

    // orig: R019+0x0AAC (1EF8:0AAC) matched
    // C: char × int / 100 is a signed int division truncated into the char.
    private void LowerAttributes(int i, int pct, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Attributes[0] = (sbyte)(c.Attributes[2] * pct / 100);
        c.Attributes[2] = (sbyte)(c.Attributes[2] - m.Attributes[0]);
        m.Attributes[1] = (sbyte)(c.Attributes[3] * pct / 100);
        c.Attributes[3] = (sbyte)(c.Attributes[3] - m.Attributes[1]);
        m.AttributesUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
    }

    // orig: R019+0x0B00 (1EF8:0B00) matched
    private void EndSkillModifier(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        for (var k = 0; k < 7; k++)
        {
            c.Skills[k] = (sbyte)(c.Skills[k] + m.Skills[k]);
            m.Skills[k] = 0;
        }
        ReReady(i);
        m.SkillsUntil = EffectModifiers.Off;
    }

    // orig: R019+0x0B44 (1EF8:0B44) matched
    private void LowerSkills(int i, int pct, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        for (var k = 0; k < 7; k++)
        {
            m.Skills[k] = (sbyte)(c.Skills[k] * pct / 100);
            c.Skills[k] = (sbyte)(c.Skills[k] - m.Skills[k]);
        }
        ReReady(i);
        m.SkillsUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
    }

    /// <summary>The readied weapon is readied again (its values follow the changed skills).</summary>
    // orig: R019+0x0B9E (1EF8:0B9E) matched
    internal void ReReady(int i)
    {
        var c = Battle[i];
        if ((c.FullStrength & 0x80) == 0x80 && !string.Equals(_attackCode(c.AnimationSlot), "PT", StringComparison.OrdinalIgnoreCase))
        {
            ReadyWeapon(i, c.MissileWeapon, 0x80);
        }
        else
        {
            ReadyWeapon(i, c.DefaultWeapon, 0);
        }
    }

    /// <summary>The hold ends; a fighter goes back to its attack.</summary>
    // orig: R019+0x0BF8 (1EF8:0BF8) matched
    private void EndHold(int i)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Held = false;
        m.HeldUntil = EffectModifiers.Off;
        if (((ushort)m.SavedAction & 2) == 2)
        {
            c.Action = m.SavedAction;
        }
    }

    /// <summary>Held: a fighter parries (0x22); a shooter or caster stops.</summary>
    // orig: R019+0x0C28 (1EF8:0C28) matched
    private void Hold(int i, int idx)
    {
        var c = Battle[i];
        var m = c.Modifiers;
        m.Held = true;
        m.HeldUntil = (ushort)(Unsigned(0x21E6 + idx) * 60 + Clock);
        m.SavedAction = c.Action;
        if (((ushort)c.Action & 2) == 2)
        {
            c.Action = CombatAction.ParryAttack;
        }
        if (c.Action is CombatAction.Missile or CombatAction.UseItem)
        {
            c.State = ActionState.Finished;
        }
    }

    /// <summary>Takes one of the potion thrown (party members only; enemies cast from their charges).</summary>
    // orig: R019+0x0DFE (1EF8:0DFE) matched
    public void UseUpPotion(int i, int potion, int quality)
    {
        if (i < Combatant.PartySlots)
        {
            Attacks.ConsumeItem(Battle, i, (byte)(potion + 0x84), (byte)quality);
        }
    }

    /// <summary>The Throw submenu of member i: its throwable potions (types 0x5F..0x68 but Black Cloud, quality 20..49), one line per potion and grade, at most 20.</summary>
    /// <returns>The lines (potion 0..9, quality, count), empty when there is none (or the member is held: DS:97DD = 2 either way).</returns>
    // orig: R019+0x0C80 (1EF8:0C80) matched
    // C: a member whose action word has bit 1 (fighting hand to hand) gets no list (the menu entry is disabled).
    public List<(int Potion, int Quality, int Count)> ThrowList(int i)
    {
        var c = Battle[i];
        var lines = new List<(int, int, int)>();
        if (((ushort)c.Action & 2) == 2)
        {
            return lines;
        }
        var list = new (int Id, int Quality, int Count)[30];
        Array.Fill(list, (-1, -1, -1));
        var count = 0;
        foreach (var s in c.Items)
        {
            if (s.Code is >= 0x5F and <= 0x68 && (byte)s.Quality is >= 0x14 and < 0x32 && s.Code != 0x61)
            {
                var a = s.Code - 0x5F;
                var n = a * 3 + (byte)s.Quality / 10 - 2;
                list[n] = (a, (byte)s.Quality, (byte)s.Count);
                count++;
            }
        }
        // C: count counts stacks, not distinct lines (two stacks of one grade count twice): faithful.
        for (var k = 1; count > 20 && k < 30; k += 3)
        {
            if (list[k].Id != -1)
            {
                list[k].Id = -1;
                count--;
            }
        }
        foreach (var e in list)
        {
            if (e.Id != -1)
            {
                lines.Add((e.Id, e.Quality, e.Count));
            }
        }
        return lines;
    }

    /// <summary>
    /// Combatant i stands inside the effect at (x, y) of size byte <paramref name="size"/> (its
    /// class in the high nibble) and the line from the centre to its puff is clear.
    /// </summary>
    // orig: R014+0x469E (0E76:469E) draft (asm-verified per spec D §1.11); tests: Oracle:effects.json
    public bool Inside(int x, int y, byte size, int i)
    {
        var s = size >> 4;
        var w = s < EffectHalfWidth.Length ? EffectHalfWidth[s] : 0;
        var h = s < EffectHalfHeight.Length ? EffectHalfHeight[s] : 0;
        int lx = -(w - x), ly = -(h - y), hx = w + x, hy = h + y;
        var p = Battle[i].Position;
        if (lx < p.X && p.X < hx && ly < p.Y && p.Y < hy)
        {
            var r = ((p.Y - ly) * 4 >> 5) * s;
            var c = (p.X - lx) * 4 >> 6;
            var (ox, oy) = PuffOffset(s, r + c);
            return Map.LineBlocked(new MapPoint(x, y), new MapPoint(ox + x, oy + y)) == 0;
        }
        return false;
    }

    /// <summary>The enemies' area test of a spell (the same test with the type's size).</summary>
    public bool InArea(MapPoint centre, int type, int k) => Inside(centre.X, centre.Y, Unsigned(0x21DC + type), k);

    /// <summary>
    /// The four words the stack holds where cloud-on-line-test keeps its saved line (bp−6, −8, −0x0A, −0x0C) before it writes
    /// them: in the game the frame of rand() left them, from hit-roll's roll-one-to-n(100) just before (the long multiply's
    /// operands: 3, 0x43FD — the multiplier 214013 — and the generator's state before that draw, high and low word).
    /// </summary>
    // orig: rand (root 06A1:29B6, MSC run-time) as found by running hit-roll R014+0xA738 from the original bytes
    // (tools/oracle run_unit with a frame trace at 1EC9:0242)
    // C: the four words are below the caller's SP from rand()'s return to cloud-on-line-test's own writes; the only other writer in that
    //   stretch (a few hundred instructions) is the timer interrupt (root 047C:01EA), whose FLAGS / CS / IP and handler frame land there
    //   when a tick falls inside it.
    // DIVERGENCE: a tick in that stretch is the phase of the PC's timer against the code, not a state the game keeps; the rewrite always
    //   takes the words rand() left.
    public (int X0, int Y0, int X1, int Y1) StackResidue()
    {
        var before = Battle.Random is Randomness.MscRandom m ? m.PreviousState() : 0u;
        return (3, 0x43FD, (short)(before >> 16), (short)before);
    }

    /// <summary>cloud-on-line-test: the line from a to t passes through one of the clouds (its box, then a puff seen from its centre).</summary>
    public bool LineCrossesCloud(int attacker, int target) => LineCrossesCloud(attacker, target, StackResidue());

    /// <summary>cloud-on-line-test with the stack words its saved line starts as.</summary>
    // orig: cloud-on-line-test R014+0x10672 (1EC9:0142) draft, checked against asm
    // (the draft's restore after a missed box is only a remark; the asm reloads the line from the saved words there);
    // tests: Oracle:effects.json
    // BUG: the loop runs to the count inclusive, so the slot after the last cloud (an old cloud's copy, or zeros) is tested too
    // (OriginalBehaviour.FixCloudLineOneTooMany stops at the last cloud).
    // BUG: a missed box reloads the line from the saved words, which are the stack residue until a box was hit: the clouds
    // after it are tested against that line (3, 17405)-(state words), not a → t. Kept.
    // C: clouds of every level are tested against the shown level's walls; the puff tests restore only the line's start.
    // DIVERGENCE: a ninth cloud slot (the bytes after the array, DS:A33B) reads as an empty cloud at (0, 0).
    public bool LineCrossesCloud(int attacker, int target, (int X0, int Y0, int X1, int Y1) residue)
    {
        if (CloudCount == 0)
        {
            return false;
        }
        var a = Battle[attacker].Position;
        var t = Battle[target].Position;
        // sight-line-x0, sight-line-y0, sight-line-x1, sight-line-y1 (DS:E0A5, DS:E0A3, DS:E0A9, DS:E0A7) and the saved line (bp-6, -8, -0x0A, -0x0C).
        int x0 = a.X, y0 = a.Y, x1 = t.X, y1 = t.Y;
        var (sx0, sy0, sx1, sy1) = residue;
        // orig: its own clip tests (R014+0xF284 from this frame) and those of the line tests it calls (one frame deeper) keep two runs (BattleMap.ClipResidue)
        BattleMap.ClipResidue boxes = new(), lines = new();
        var last = Battle.Behaviour.FixCloudLineOneTooMany ? CloudCount - 1 : CloudCount;
        for (var i = 0; i <= last; i++)
        {
            var cl = _clouds[i];
            var n = cl.Size >> 4;
            int w = n < EffectHalfWidth.Length ? EffectHalfWidth[n] : 0, h = n < EffectHalfHeight.Length ? EffectHalfHeight[n] : 0;
            if (Map.SegmentHitsBox(x0, y0, x1, y1, cl.X - w, cl.Y - h, w + cl.X, h + cl.Y, boxes))
            {
                (sx0, sy0, sx1, sy1) = (x0, y0, x1, y1);
                x0 = cl.X;
                y0 = cl.Y;
                for (var k = 0; k < n * n; k++)
                {
                    var (ox, oy) = PuffOffset(n, k);
                    x1 = ox + cl.X;
                    y1 = oy + cl.Y;
                    if (Map.LineBlocked(new MapPoint(x0, y0), new MapPoint(x1, y1), residue: lines) != 0)
                    {
                        continue;
                    }
                    var (px, py) = (x0, y0);
                    var box = (x1 - 8, y1 - 4, x1 + 8, y1 + 4);
                    (x0, y0, x1, y1) = (sx0, sy0, sx1, sy1);
                    if (Map.SegmentHitsBox(x0, y0, x1, y1, box.Item1, box.Item2, box.Item3, box.Item4, boxes))
                    {
                        return true;
                    }
                    (x0, y0) = (px, py);
                }
            }
            (x0, y0, x1, y1) = (sx0, sy0, sx1, sy1);
        }
        return false;
    }

    /// <summary>The shooter stands in a cloud (0592's loop: the first that holds it).</summary>
    // orig: R014+0xAA22 (18BF:0592) draft (the cloud loop) calling R014+0x469E
    public bool InsideACloud(int combatant)
    {
        for (var k = 0; k < CloudCount; k++)
        {
            if (Inside(_clouds[k].X, _clouds[k].Y, _clouds[k].Size, combatant))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The strongest slowing area the walker stands in (0 none or it resists type 9).</summary>
    // orig: fighter-step R014+0x1624 (0E76:1624) draft (the DS:A395 test, spec D §2.2)
    public int SlowingStrength(int i)
    {
        var best = 0;
        for (var k = 0; k < AreaCount; k++)
        {
            // C: DS:C928[i][0] & 2 is bit 9 of the immunity word (type 9).
            if ((Battle[i].EffectImmunity & 0x200) == 0 && Inside(_areas[k].X, _areas[k].Y, _areas[k].Size, i) && best < _areas[k].Strength)
            {
                best = _areas[k].Strength;
            }
        }
        return best;
    }

    /// <summary>The modifiers whose time is up end (every tick the battle is not paused, for every combatant).</summary>
    // orig: close-combat-rounds R014+0x2A4A (0E76:2A4A) draft (the mods: tail)
    public void EndModifiersDue(int i)
    {
        if (Paused)
        {
            return;
        }
        var m = Battle[i].Modifiers;
        if (m.AttributesUntil != EffectModifiers.Off && m.AttributesUntil < Clock)
        {
            EndAttributeModifier(i);
        }
        if (m.SkillsUntil != EffectModifiers.Off && m.SkillsUntil < Clock)
        {
            EndSkillModifier(i);
        }
        if (m.HeldUntil != EffectModifiers.Off && m.HeldUntil < Clock)
        {
            EndHold(i);
        }
    }

    /// <summary>After the battle the party's running modifiers end (0694); a save in a battle ends those of the members present.</summary>
    /// <param name="presentOnly">The save's test of the member's status: empty slots are skipped.</param>
    public void EndPartyModifiers(bool presentOnly = false)
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (presentOnly && Battle[i].Status == CharacterStatus.Empty)
            {
                continue;
            }
            var m = Battle[i].Modifiers;
            if (m.AttributesUntil != EffectModifiers.Off)
            {
                EndAttributeModifier(i);
            }
            if (m.SkillsUntil != EffectModifiers.Off)
            {
                EndSkillModifier(i);
            }
            if (m.HeldUntil != EffectModifiers.Off)
            {
                EndHold(i);
            }
        }
    }
}
