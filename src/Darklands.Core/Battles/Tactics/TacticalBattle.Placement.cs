using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:18A6 / DS:18AE: the view origin offset per arrival direction (in quarter cells).</summary>
    private static readonly sbyte[] ViewOffsetX = [0, 4, 12, 12, 0, -4, -12, -12];
    private static readonly sbyte[] ViewOffsetY = [-16, -16, 0, 16, 16, 16, 0, -16];

    /// <summary>DS:261C / DS:2628: how an encounter's box lies around its spot, by formation and facing.</summary>
    private static readonly sbyte[] BoxSideX = [-15, -15, -15, -15, 0, -16, -15, -15, -15, -15, -16, 0];
    private static readonly sbyte[] BoxSideY = [-15, -16, 0, -15, -15, -15, -15, 0, -16, -15, -15, -15];

    /// <summary>A debugging aid (DARKLANDS_BATTLE_STALE_DEST, "5:-256,511;7:..."): the destination a record keeps when its enemy is first placed,
    /// by record. The original copies the enemy file's record whole (R025+0x0267) and its destination bytes (+0x08..+0x0B) are whatever the
    /// stack held; until the enemy first moves they steer what reads them (the facing of a spell at a spot, R014+0x306A). The oracle reads
    /// them off the original's run; the C# leaves them 0.</summary>
    public static Dictionary<int, MapPoint>? StaleDestinations { get; set; } = ParseStaleDestinations(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_STALE_DEST"));

    private static Dictionary<int, MapPoint>? ParseStaleDestinations(string? text) => string.IsNullOrEmpty(text) ? null
        : text.Split(';').Select(p => p.Split(':', ',')).ToDictionary(
            p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p => new MapPoint(int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>DS:DA34: the levels whose encounters were met (kept by the battlefield memory: they do not come back).</summary>
    public int EncounteredLevels { get; private set; }

    /// <summary>The view origin cell the screen should centre (DS:C866 / DS:C886) after a placement; null when the view follows the leader.</summary>
    public (int Column, int Row)? ViewOrigin { get; set; }

    /// <summary>DS:11B1: the view follows the active member (combat_tick's test); set by choosing a member, cleared by a placement and
    /// by scrolling the view.</summary>
    // orig: set by R016+0x09BC and 0x0A2C; cleared by effect_start R014+0x4188 and R016+0x0B92
    public bool FollowsActive { get; set; }

    /// <summary>
    /// The party arrives at (x, y) facing <paramref name="dir"/>: the marching order in a
    /// five-slot formation, those that do not fit at random free spots in sight.
    /// </summary>
    // orig: effect_start R014+0x4188 (0E76:4188) draft (spec D §8.2, asm-verified)
    public void PlaceParty(int x, int y, int dir)
    {
        if (x == -1 && y == -1)
        {
            return;
        }
        var from = new MapPoint(x, y);
        if (dir is >= 0 and < 8)
        {
            // C: signed byte divisions by 4.
            ViewOrigin = (ViewOffsetX[dir] / 4 + (x >> 5), ViewOffsetY[dir] / 4 + (y >> 4));
        }
        FollowsActive = false;
        int[] dx, dy;
        switch (dir)
        {
            case 0 or 1 or 7:
                (dx, dy) = ([-8, 8, -16, 16, 0], [0, 0, 8, 8, 8]);
                break;
            case 2:
                (dx, dy) = ([0, 0, -16, -16, -16], [5, -5, 10, -10, 0]);
                break;
            case 3 or 4 or 5:
                (dx, dy) = ([-8, 8, -16, 16, 0], [0, 0, -8, -8, -8]);
                break;
            case 6:
                (dx, dy) = ([0, 0, 16, 16, 16], [5, -5, 10, -10, 0]);
                break;
            default:
                // DIVERGENCE: directions above 7 leave the offsets uninitialised in the original; the north formation is used.
                (dx, dy) = ([-8, 8, -16, 16, 0], [0, 0, 8, 8, 8]);
                break;
        }
        foreach (var m in Battle.Combatants.Take(Combatant.PartySlots))
        {
            if (m.IsAble)
            {
                m.Position = new MapPoint(0, 0);
            }
        }
        var k = 0;
        for (var slot = 0; slot < 5 && k < MarchingOrder.Count; slot++)
        {
            var c = Battle[MarchingOrder[k]];
            c.SpriteFacing = c.Facing = dir;
            c.Position = new MapPoint(x + dx[slot], y + dy[slot]);
            if (Map.Collides(c.Index, c.Position.X, c.Position.Y).IsFree)
            {
                k++;
            }
        }
        Map.StrictWalls = true;
        var cleared = false;
        while (k < MarchingOrder.Count)
        {
            var c = Battle[MarchingOrder[k]];
            var tries = 0;
            while (true)
            {
                // C: the x draw comes first.
                var px = Scaled(0x30) + x;
                var py = Scaled(0x18) + y;
                c.Position = new MapPoint(px, py);
                if (Map.Collides(c.Index, px, py).IsFree && Map.LineBlocked(from, c.Position) == 0)
                {
                    c.SpriteFacing = c.Facing = dir;
                    break;
                }
                tries++;
                if (tries > 500 && !cleared)
                {
                    ClearFurnitureAround(x, y);
                    cleared = true;
                    tries = 0;
                    continue;
                }
                if (tries > 500)
                {
                    break;
                }
            }
            k++;
        }
        Map.StrictWalls = false;
    }

    /// <summary>The furniture of the 3 × 3 cells around a point is removed (a stuck placement).</summary>
    private void ClearFurnitureAround(int x, int y)
    {
        int cx = x >> 5, cy = y >> 4;
        for (var ox = -1; ox <= 1; ox++)
        {
            for (var oy = -1; oy <= 1; oy++)
            {
                // byte 0 &= 0x0F: the furniture nibble.
                var cell = Level[cx + ox, cy + oy];
                Level[cx + ox, cy + oy] = cell.WithFurniture(0, cell.FurnitureOrientation);
            }
        }
        _redraw = true;
    }

    /// <summary>
    /// Places combatant <paramref name="who"/> around a spot facing away from the spot's direction:
    /// enemies try the 12 offsets from the far end; members the first 12, then random spots in sight.
    /// </summary>
    // orig: place_around R024+0x08F2 (1EF8:08F2) draft (asm-verified per spec D §8.3; the draft
    // tries 13 offsets for members, the asm 12)
    private bool PlaceAround(int who, MapPoint spot, int dir)
    {
        var c = Battle[who];
        var saved = c.Position;
        c.Facing = c.SpriteFacing = (dir - 4) & 7;
        int[] dx, dy;
        switch (dir)
        {
            case 0:
                (dx, dy) = ([0, -12, 12, 0, -12, 12, 0, -12, 12, 0, -12, 12, 12], [0, 0, 0, -8, -8, -8, -16, -16, -16, -24, -24, -24, -32]);
                break;
            case 2:
                (dx, dy) = ([0, 0, 0, 12, 12, 12, 24, 24, 24, 36, 36, 36, 48], [0, -8, 8, 0, -8, 8, 0, -8, 8, 0, -8, 8, 8]);
                break;
            case 4:
                (dx, dy) = ([0, 12, -12, 0, 12, -12, 0, 12, -12, 0, 12, -12, -12], [0, 0, 0, 8, 8, 8, 16, 16, 16, 24, 24, 24, 32]);
                break;
            case 6:
                (dx, dy) = ([0, 0, 0, -12, -12, -12, -24, -24, -24, -36, -36, -36, -48], [0, 8, -8, 0, 8, -8, 0, 8, -8, 0, 8, -8, -8]);
                break;
            default:
                // DIVERGENCE: odd directions read stack garbage in the original; the next even direction's offsets are used.
                return PlaceAround(who, spot, (dir + 1) & 6);
        }
        bool Free(int i)
        {
            c.Position = new MapPoint(spot.X + dx[i], spot.Y + dy[i]);
            return Map.Collides(who, c.Position.X, c.Position.Y).IsFree;
        }
        if (who < Combatant.PartySlots)
        {
            for (var i = 0; i < 12; i++)
            {
                if (Free(i))
                {
                    return Placed();
                }
            }
            var cleared = false;
            for (var i = 0; ; i++)
            {
                c.Position = new MapPoint(Scaled(48) + spot.X, Scaled(24) + spot.Y);
                if (Map.Collides(who, c.Position.X, c.Position.Y).IsFree && Map.LineBlocked(spot, c.Position) == 0)
                {
                    return Placed();
                }
                if (i > 500 && !cleared)
                {
                    ClearFurnitureAround(spot.X, spot.Y);
                    cleared = true;
                    i = 0;
                    continue;
                }
                if (i > 500)
                {
                    break;
                }
            }
        }
        else
        {
            for (var i = 12; i >= 1; i--)
            {
                if (Free(i))
                {
                    return Placed();
                }
            }
        }
        c.Position = saved;
        return false;

        bool Placed()
        {
            c.Home = c.Position;
            return true;
        }
    }

    /// <summary>The enemies already in records 5..12 are placed around the spot; from now on hidden encounters are checked.</summary>
    // orig: R025+0x0CFC (1EF8:0CFC) draft
    private void PlaceInitialEnemies()
    {
        ActivationsEnabled = true;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            if (c.Status == CharacterStatus.Empty)
            {
                continue;
            }
            c.Floor = Battle.FloorShown;
            if (!PlaceAround(i, InitialEnemySpot.Position, InitialEnemySpot.Direction))
            {
                c.Status = CharacterStatus.Empty;
            }
        }
        ActivationsAt = Clock;
    }

    /// <summary>
    /// Hidden encounters on the party's level: one whose spot a member sees, or comes near
    /// enough to (stealth + streetwise), is placed and removed from the list.
    /// </summary>
    // orig: activations_check R014+0x4442 (0E76:4442) draft (spec D §1.10, asm-verified)
    private bool CheckActivations()
    {
        var result = false;
        var list = Activations[Battle.FloorShown];
        if (Battle.PartyFloor == Battle.FloorShown && ActivationsEnabled && !Paused && list.Count != 0)
        {
            var k = 0;
            while (k < list.Count)
            {
                var rec = list[k];
                var found = -1;
                if (rec.X >= 0 && !Level[rec.X >> 5, rec.Y >> 4].IsVoid)
                {
                    var spot = new MapPoint(rec.X, rec.Y);
                    for (var i = 0; i < Combatant.PartySlots && found < 0; i++)
                    {
                        var c = Battle[i];
                        if (c.IsAble && Map.LineBlocked(spot, c.Position) == 0)
                        {
                            found = i;
                        }
                    }
                    for (var i = 0; i < Combatant.PartySlots && found < 0; i++)
                    {
                        var c = Battle[i];
                        if (c.IsAble && Distance((rec.X - c.Position.X) / 2, rec.Y - c.Position.Y) < c.Skills[15] + c.Skills[16])
                        {
                            found = i;
                        }
                    }
                }
                if (found < 0)
                {
                    k++;
                    continue;
                }
                result = PlaceEnemies(0, rec, found);
                // orig: R014+0xD2CE (19CD:1D5E) matched (the record removed, the rest moved down)
                list.RemoveAt(k);
            }
        }
        ActivationsAt = (ushort)(Clock + 120);
        return result;
    }

    /// <summary>
    /// Places an encounter's enemies (mode 0, facing the member who found them) or the next
    /// reinforcement wave (mode 1) into the free records 5..12: each kind into one of the two enemy
    /// animation slots, at the corner of its box first, then at random spots in sight of the spot.
    /// </summary>
    // orig: enemies_place R025+0x0000 (1EF8:0000) draft (decoded fully per the draft notes;
    // the placement part asm-verified per spec D §8.4: the flags word is masked with 0xFE00, the draft clears only 5 bits)
    // DIVERGENCE: the palettes (ENEMYPAL.DAT ranges) are the screen's; in mode 1 the original adds money from an
    // uninitialised encounter pointer — no money is added here.
    private bool PlaceEnemies(int mode, EnemyGroup? encounter, int who)
    {
        MapPoint origin;
        int spread, facing;
        sbyte[] types, counts;
        if (mode == 0 && encounter is not null)
        {
            origin = new MapPoint(encounter.X, encounter.Y);
            spread = encounter.Spread;
            types = encounter.Kinds;
            counts = encounter.Counts;
            facing = DirectionToward(origin, Battle[who].Position, false);
        }
        else if (WaveGroup is { } wave)
        {
            origin = new MapPoint(wave.ReinforcementX, wave.ReinforcementY);
            spread = wave.Byte15;
            types = [wave.ReinforcementKind];
            counts = [wave.ReinforcementBatch];
            facing = wave.ReinforcementFacing;
        }
        else
        {
            return false;
        }
        var placed = false;
        var kinds = Field.Forces.Kinds[Battle.FloorShown];
        for (var g = 0; g < types.Length; g++)
        {
            if (types[g] == -1 || types[g] >= kinds.Count)
            {
                continue;
            }
            var kind = kinds[types[g]];
            for (var j = 0; j < counts[g]; j++)
            {
                var si = Array.FindIndex(Battle.Combatants, Combatant.PartySlots, Combatant.Count - Combatant.PartySlots, c => c.Status == CharacterStatus.Empty);
                if (si < 0)
                {
                    continue;
                }
                var identity = kind.Identity;
                int slot;
                if (identity == EnemySlotIdentity[0])
                {
                    slot = 5;
                }
                else if (identity == EnemySlotIdentity[1])
                {
                    slot = 6;
                }
                else if (EnemySlotIdentity[0] == -1)
                {
                    slot = 5;
                    EnemySlotIdentity[0] = identity;
                }
                else if (EnemySlotIdentity[1] == -1)
                {
                    slot = 6;
                    EnemySlotIdentity[1] = identity;
                }
                else
                {
                    continue;
                }
                SlotCodes[slot] = kind.Image;
                // DS:C28D[slot] = the kind on this level; DS:C23E[slot] = its level mask (the levels it follows the party to).
                _slotKind[slot] = types[g];
                _slotFollows[slot] = kind.LevelMask;
                CombatantSetup.FromEnemy(Battle, si, kind, Battle.FloorShown, facing, slot, mode == 0);
                var c = Battle[si];
                // C: DS:C1C1 of the slot = the kind's base load (only the battle file reads it).
                c.PlacedBaseLoad = kind.BaseLoad;
                c.Appearance = kind.Appearance;
                kind.Palettes.CopyTo(c.Palettes, 0);
                c.EndsBattleWhenKilled = kind.EndsBattleWhenKilled != 0;
                c.SpellsLeft = 0;
                for (var k = 0; k < 10; k++)
                {
                    if (k != 2)
                    {
                        c.SpellsLeft += kind.Components[k];
                    }
                    c.SpellCharges[k] = kind.Components[k];
                    c.SpellPowers[k] = kind.ComponentQualities[k];
                }
                if (kind.MissileFlag != 0)
                {
                    // DS:D680: a missile user may move to the other slot when it holds its melee look (identity = this appearance).
                    c.PreferredSlot = -1;
                    if (slot == 5)
                    {
                        if (c.Appearance == EnemySlotIdentity[1])
                        {
                            c.PreferredSlot = 6;
                        }
                    }
                    else if (c.Appearance == EnemySlotIdentity[0])
                    {
                        c.PreferredSlot = 5;
                    }
                }
                if (kind.Class != 2)
                {
                    var weapon = kind.MissileFlag == 0 ? c.DefaultWeapon : c.ReadiedWeapon;
                    ReadyWeapon(si, weapon, kind.MissileFlag);
                }
                else
                {
                    SlotWeapons[slot] = "PT";
                }
                var p = kind.Byte27 + facing / 2 * 3;
                p = Math.Clamp(p, 0, BoxSideX.Length - 1);
                int a = BoxSideX[p], b = BoxSideY[p];
                var w2 = spread * 2;
                var w1 = spread;
                var baseX = origin.X - (a < 0 ? w2 : 0);
                var baseY = origin.Y - (b < 0 ? w1 : 0);
                var rx = w2 << (a & 1);
                var ry = w1 << (b & 1);
                c.Flags = (TacticalFlags)((ushort)c.Flags & 0xFE00) | TacticalFlags.InFight | TacticalFlags.CombatBox;
                c.Place = PlaceAction.None;
                c.Moving = false;
                c.QueuedEndurance = c.QueuedStrength = 0;
                c.DamageShown = 0;
                c.GaveUp = false;
                c.WasHit = false;
                c.DamageNumber = 0;
                c.State = ActionState.Idle;
                c.Action = CombatAction.None;
                c.Exchange = Exchange.Placed;
                c.WatchedFacingCount = 7;
                if (_library.Sizes(kind.Image) is { } sizes)
                {
                    Slots[slot].SetSizes(sizes.HalfWidth, sizes.HalfHeight, sizes.CombatHalfWidth, sizes.CombatHalfHeight);
                }
                c.HalfWidth = Slots[slot].CombatHalfWidth;
                c.HalfHeight = Slots[slot].CombatHalfHeight;
                int tries;
                for (tries = 0; tries <= 500; tries++)
                {
                    if (tries == 0)
                    {
                        c.Position = new MapPoint(baseX, baseY);
                    }
                    else
                    {
                        // C: the x draw comes first.
                        var x = Scaled(rx) + baseX;
                        var y = Scaled(ry) + baseY;
                        c.Position = new MapPoint(x, y);
                    }
                    if (Map.Collides(si, c.Position.X, c.Position.Y).IsFree && Map.LineBlocked(origin, c.Position) == 0)
                    {
                        break;
                    }
                }
                if (tries > 500)
                {
                    c.Status = CharacterStatus.Empty;
                }
                else
                {
                    placed = true;
                    if (mode == 0 && encounter is not null && encounter.HasReinforcements == 1)
                    {
                        WavesEnabled = true;
                        WaveGroup = encounter.Clone();
                    }
                    c.Pose = Pose.Reload;
                    SetPose(si, Pose.Walking);
                    c.Frame = 0;
                    DragonWeaponForFight(si);
                }
                c.Home = c.Position;
                if (StaleDestinations is not null && StaleDestinations.Remove(si, out var stale))
                {
                    c.Destination = stale;
                }
            }
        }
        if (!placed)
        {
            return false;
        }
        _redraw = true;
        Menus.Set(OrdersItem.EnemyInfo, 1);
        EncounteredLevels |= 1 << Battle.FloorShown;
        if (mode == 0 && encounter is not null)
        {
            Purse = AddMoney(Purse, encounter.Florins, encounter.Groschen, encounter.Pfennigs);
        }
        return true;
    }

    /// <summary>DS:C28D / DS:C23E per animation slot: the enemy kind (index on the level) it shows and the levels that kind follows the party to.</summary>
    private readonly int[] _slotKind = new int[7];
    private readonly int[] _slotFollows = new int[7];

    private int EnemyKindOfSlot(int slot) => _slotKind[slot];

    private int EnemyFollows(int slot) => _slotFollows[slot];

    /// <summary>The encounter's money into the loot purse (pfennigs carry at 12, groschen at 20).</summary>
    private static Money AddMoney(Money purse, int florins, int groschen, int pfennigs)
    {
        int fl = purse.Florins, gr = purse.Groschen, pf = purse.Pfennigs + pfennigs;
        if (pf >= 12)
        {
            gr += pf / 12;
            pf %= 12;
        }
        gr += groschen;
        if (gr >= 20)
        {
            fl += gr / 20;
            gr %= 20;
        }
        fl += florins;
        return new Money((short)fl, (short)gr, (short)pf);
    }

    /// <summary>combat_recover's search: a woken member is put on a free spot right of / below where it lay, in sight.</summary>
    // orig: combat_recover R014+0x0DEE (0E76:0DEE) draft (spec D §1.7, asm-verified)
    private void PlaceWoken(int i)
    {
        var c = Battle[i];
        var first = true;
        var any = false;
        var anchor = i;
        var r = 16;
        var tries = 0;
        var from = c.Position;
        Map.StrictWalls = true;
        while (true)
        {
            var x = Scaled(r) + from.X;
            var y = Scaled(r) + from.Y;
            c.Position = new MapPoint(x, y);
            if (Map.Collides(i, x, y).IsFree && (any || Map.LineBlocked(from, c.Position) == 0))
            {
                break;
            }
            tries++;
            if (tries <= 75)
            {
                continue;
            }
            tries = 0;
            r += 8;
            if (r <= 60)
            {
                continue;
            }
            if (first)
            {
                anchor = 0;
                first = false;
            }
            if (anchor >= Combatant.PartySlots)
            {
                r = 30;
                any = true;
            }
            else if (Battle[anchor].IsAble)
            {
                from = Battle[anchor].Position;
                anchor++;
                r = 16;
            }
            else
            {
                anchor++;
            }
        }
        Map.StrictWalls = false;
        _view.RowChanged(i);
    }
}
