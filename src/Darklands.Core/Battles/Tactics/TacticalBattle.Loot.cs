using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:C654 / DS:C67F: the work list the loot screen shows (and the merges build).</summary>
    private readonly List<ItemStack> _lootList = [];

    /// <summary>DS:1B98: the fight left loot (loot_pile_open does nothing otherwise).</summary>
    private bool _lootLeft;

    /// <summary>DS:D846: the block of the gathered loot when no pile took it (the leader's pile index is then −count).</summary>
    private int _gatheredOffset;

    /// <summary>
    /// The body of a fallen combatant whose set drops items becomes a pile: its belongings as a new block, or — with a pile
    /// within 16 pixels — merged with that pile's block into a new one that both piles then share; a set that drops nothing
    /// leaves an empty pile (−2). The new pile is moved into one screen tile.
    /// </summary>
    // orig: cbt_dying R014+0x2324 (0E76:2324) draft (the loot pile part)
    private void LeavePile(Combatant c, AnimationSlot slot)
    {
        var loot = Store.Loot;
        var pile = new LootPile { Level = Battle.FloorShown };
        if (slot.DropsLoot)
        {
            var n = c.Items.Count;
            var near = LootPiles.FindIndex(p => p.Level == Battle.FloorShown
                && c.Position.X - 16 <= p.Position.X && p.Position.X < c.Position.X + 16
                && c.Position.Y - 16 <= p.Position.Y && p.Position.Y < c.Position.Y + 16);
            if (near == -1)
            {
                pile.Count = n;
                pile.Offset = loot.Append(c.Items, n);
            }
            else
            {
                int[] offset = [LootPiles[near].Offset];
                int[] count = [LootPiles[near].Count & 0xFF];
                _lootList.Clear();
                if (offset[0] >= 0)
                {
                    loot.Take(_lootList, 0, count, offset, 1);
                }
                LootStore.Merge(_lootList, c.Items, n);
                var o = loot.Append(_lootList, _lootList.Count);
                pile.Offset = o;
                pile.Count = _lootList.Count;
                foreach (var p in LootPiles)
                {
                    if (p.Offset == -1)
                    {
                        p.Offset = o;
                        p.Count = _lootList.Count;
                    }
                }
            }
        }
        // C: (else) the record keeps offset −2, count 0.
        pile.Position = c.Position;
        pile.Position = NudgePile(pile.Position);
        if (slot.CorpseKind == 1)
        {
            pile.Visible = true;
        }
        else if (slot.CorpseKind == 0)
        {
            pile.Visible = false;
        }
        LootPiles.Add(pile);
    }

    /// <summary>The newest pile is moved so that it is drawn inside one screen tile (by the screen tiles of the last full redraw).</summary>
    // orig: R014+0x2802 (0E76:2802) draft
    // C: the tiles are looked up in R136+0x5890 (the cell shown in each of the 140 screen tiles): a point off the screen finds
    // none (0x8C) for itself and its neighbours alike and stays.
    private MapPoint NudgePile(MapPoint at)
    {
        int[] dx = [-8, 8, -8, 8], dy = [-4, -4, 4, 4];
        int x = at.X, y = at.Y;
        // C: % of signed ints (a negative coordinate gives a negative remainder).
        int e1 = -(x % 32 - 8), e2 = -(y % 16 - 4), e3 = x % 32 - 24, e4 = y % 16 - 12;
        var t0 = _view.ScreenTileOf((x & 0xFFE0) * 8 + (y >> 4));
        var t = new int[4];
        var n = 0;
        for (var k = 0; k < 4; k++)
        {
            t[k] = _view.ScreenTileOf(((dx[k] + x) & 0xFFE0) * 8 + ((dy[k] + y) >> 4));
            if (t[k] == t0)
            {
                n++;
            }
        }
        if (n == 2)
        {
            if (t[0] == t0 && t[1] == t0)
            {
                y -= e4;
            }
            else if (t[0] == t0 && t[2] == t0)
            {
                x -= e3;
            }
            else if (t[2] == t0 && t0 == t[3])
            {
                y += e2;
            }
            else
            {
                x += e1;
            }
        }
        else if (n == 1)
        {
            if (t[0] == t0)
            {
                x -= e3;
                y -= e4;
            }
            else if (t[1] == t0)
            {
                x += e1;
                y -= e4;
            }
            else if (t0 == t[2])
            {
                x -= e3;
                y += e2;
            }
            else
            {
                x += e1;
                y += e2;
            }
        }
        return new MapPoint(x, y);
    }

    /// <summary>
    /// After a fight: the recovered missiles and the blocks of the piles left since the last gathering (each block once) are
    /// merged into one list, which becomes the block of the first of those piles (the leader's pile); with no pile it is kept
    /// apart for the leader (−count).
    /// </summary>
    // orig: loot_after_fight R014+0xEA10 (19CD:34A0) unmatched
    private void GatherLoot()
    {
        var loot = Store.Loot;
        var offsets = new List<int>();
        var counts = new List<int>();
        var index = new List<int>();
        for (var k = loot.Gathered; k < LootPiles.Count; k++)
        {
            if (LootPiles[k].Offset != -2)
            {
                offsets.Add(LootPiles[k].Offset);
                index.Add(k);
                counts.Add(LootPiles[k].Count);
            }
        }
        var n = offsets.Count;
        for (var a = 0; a < n - 1; a++)
        {
            for (var b = a + 1; b < n; b++)
            {
                if (offsets[a] == offsets[b])
                {
                    LootPiles[index[b]].Offset = -1;
                    offsets[b] = -1;
                }
            }
        }
        _lootList.Clear();
        foreach (var r in Recoverables)
        {
            _lootList.Add(new ItemStack(r.Item, r.Kind, r.Quality, r.Count, r.Pad));
        }
        var ofs = offsets.ToArray();
        if (n != 0)
        {
            loot.Take(_lootList, _lootList.Count, [.. counts], ofs, n);
        }
        var leader = Battle[Battle.Leader];
        leader.LootPile = 0;
        if (n < 1 && Recoverables.Count == 0)
        {
            _lootLeft = false;
        }
        else
        {
            _lootLeft = true;
            var o = loot.Append(_lootList, _lootList.Count);
            var k = 0;
            for (; k < LootPiles.Count; k++)
            {
                if (LootPiles[k].Offset == -1)
                {
                    LootPiles[k].Count = _lootList.Count;
                    LootPiles[k].Offset = o;
                    leader.LootPile = k;
                    break;
                }
            }
            if (k == LootPiles.Count)
            {
                leader.LootPile = -Recoverables.Count;
                _gatheredOffset = o;
            }
        }
        loot.Gathered = LootPiles.Count;
        Recoverables.Clear();
    }

    /// <summary>
    /// The member's pile (or the gathered loot kept apart) on the loot screen with the purse; what the party leaves goes back
    /// into the file as a new block for every pile that pointed at the old one.
    /// </summary>
    // orig: loot_pile_open R014+0xE278 (19CD:2D08) matched
    private void OpenLootPile(int i)
    {
        if (!_lootLeft)
        {
            return;
        }
        var loot = Store.Loot;
        int ofs, cnt;
        var pile = Battle[i].LootPile;
        if (pile < 0)
        {
            // C: −1 (no pile) reads as a block of one record at DS:D846.
            cnt = (byte)-pile;
            ofs = _gatheredOffset;
        }
        else
        {
            ofs = LootPiles[pile].Offset;
            cnt = LootPiles[pile].Count;
        }
        if (cnt == 0)
        {
            return;
        }
        int[] offset = [ofs];
        loot.Take(_lootList, 0, [cnt], offset, 1);
        ofs = offset[0];
        _view.OpenLoot(i, _lootList, Purse);
        Purse = default;
        if (LootPiles.Count != 0)
        {
            if (_lootList.Count != 0)
            {
                ofs = loot.Append(_lootList, _lootList.Count);
            }
            foreach (var p in LootPiles)
            {
                if (p.Offset == -1)
                {
                    p.Count = _lootList.Count;
                    p.Offset = ofs;
                }
            }
        }
        _view.MapChanged();
    }
}
