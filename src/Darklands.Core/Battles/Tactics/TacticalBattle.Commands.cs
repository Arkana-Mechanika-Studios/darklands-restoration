using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>DS:1B9A..DS:1BC2: the two-file formation per direction (first file x/y, second file offset, step per rank).</summary>
    private static readonly int[] FileX = [-8, -5, 0, -5, -8, -5, 0, -5];
    private static readonly int[] SecondFileX = [16, 10, 0, 10, 16, 10, 0, 10];
    private static readonly int[] RankX = [0, -10, -14, -10, 0, 10, 14, 10];
    private static readonly int[] FileY = [0, -5, -6, 5, 0, -5, -6, 5];
    private static readonly int[] SecondFileY = [0, 10, 12, -10, 0, 10, 12, -10];
    private static readonly int[] RankY = [12, 10, 0, -10, -12, -10, 0, 10];

    /// <summary>The key-step bytes per compass direction (0x00 up … 0x77 up-left).</summary>
    private static readonly int[] KeySteps = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77];

    /// <summary>DS:17B0: the member the keys move.</summary>
    public int ActiveMember => Battle.ActiveMember;

    /// <summary>
    /// The marching order is kept when it lists exactly the able members; otherwise it becomes
    /// the able members in slot order.
    /// </summary>
    /// <param name="message">marching_order_rebuild's argument: 1 from G and Q (R016+0x0C1B, R016+0x0D22), 0 at the battle's start (R014+0x000D).</param>
    // orig: marching_order_rebuild R016+0x0DA6 (1EF8:0DA6) draft; R016+0x0E2E (1EF8:0E2E) draft
    // C: R016+0x0E54..0x0E73: a rebuilt order with the argument 1 copies the far string 286B:0231 to a buffer and shows it for 60 ticks
    // C: (show_message root 0000:24BC: the battle waits, the timer runs; the clock too unless DS:07D3 holds it).
    // C: (checked with the draft of R016+0x0DA6) kept when the length is not 0, every entry's combatant has status 1 and every able member
    // C: is in it: an order that names a member twice (Change Marching Order's F keys) is kept.
    // DIVERGENCE: an entry past the thirteen combatant records (a saved game's bytes) reads past the records; here it makes the order rebuilt.
    public void RebuildMarchingOrder(bool message = false)
    {
        var able = Enumerable.Range(0, Combatant.PartySlots).Where(i => Battle[i].IsAble).ToList();
        if (MarchingOrder.Count > 0 && MarchingOrder.All(m => m >= 0 && m < Combatant.Count && Battle[m].IsAble) && able.All(MarchingOrder.Contains))
        {
            return;
        }
        MarchingOrder.Clear();
        MarchingOrder.AddRange(able);
        // C: only the able members' bytes are written: with nobody able the length is 0 and the five bytes stay (single file still reads the first).
        StoreMarchingOrderBytes();
        if (message)
        {
            _view.FarMessage(MarchingOrderRebuiltText, 60);
        }
    }

    /// <summary>286B:0231: the message of a marching order rebuilt by G or Q.</summary>
    public const int MarchingOrderRebuiltText = 0x0231;

    /// <summary>Keys 1..5 / a click on a member: the member the keys move (the leader stays).</summary>
    // orig: R016+0x0A2C (1EF8:0A2C) draft (byte-matched)
    public void SelectMember(int i)
    {
        if (i is < 0 or >= Combatant.PartySlots)
        {
            return;
        }
        var c = Battle[i];
        if (c.Status is CharacterStatus.Empty or CharacterStatus.Dead or CharacterStatus.Away || c.Floor == BattleEnd.LeftThroughExit)
        {
            return;
        }
        if (Selected != -1)
        {
            LeaveOrderMode();
        }
        FollowsActive = true;
        Battle.ActiveMember = i;
        // R016+0x0A72..0x0A7D: DS:D6EA = the member's flee bit (flags 0x1000), after order mode was left
        _fleeBefore = (c.Flags & TacticalFlags.Following) != 0;
        if (c.Floor != Battle.FloorShown)
        {
            SwitchLevel(c.Floor);
            _view.Recentre(i);
        }
        ResetOrderMenu();
        PendingOrderMember = i;
    }

    /// <summary>Shift-F1..F5 / Ctrl-click: the member becomes the leader too.</summary>
    // orig: member_activate R016+0x09BC (1EF8:09BC) draft (byte-matched)
    public void ActivateMember(int i)
    {
        if (i is < 0 or >= Combatant.PartySlots)
        {
            return;
        }
        var c = Battle[i];
        if (c.Status is CharacterStatus.Empty or CharacterStatus.Dead or CharacterStatus.Away || c.Floor == BattleEnd.LeftThroughExit)
        {
            return;
        }
        FollowsActive = true;
        Battle.ActiveMember = Battle.Leader = i;
        if (c.Floor != Battle.FloorShown)
        {
            SwitchLevel(c.Floor);
        }
        _view.RowChanged(-1);
        ResetOrderMenu();
    }

    /// <summary>An arrow key: the active member takes one step that way this tick (not when the party travels as a group).</summary>
    /// <param name="direction">Compass 0 north … 7 north-west.</param>
    // orig: combat_keys R016+0x0000 (1EF8:0000) draft (spec D §7.1)
    public void StepKey(int direction)
    {
        if (Battle.PartyFloor != Battle.FloorShown)
        {
            _view.Message(UseStairsText, 0x96);
            return;
        }
        var i = Battle.ActiveMember;
        if (i is < 0 or >= Combatant.PartySlots || Battle[i].Floor != Battle.FloorShown || MoveMode == 1)
        {
            return;
        }
        KeyMoved = true;
        var c = Battle[i];
        c.Action = CombatAction.Walk;
        c.QueuedStep = KeySteps[direction & 7];
    }

    /// <summary>DS:064A "USE STAIRS - your only option…".</summary>
    public const int UseStairsText = 0x064A;

    /// <summary>G: travel as a group on or off (the caller tests the menu entry's enable byte).</summary>
    // orig: R016+0x0BD6 (1EF8:0BD6) draft (byte-matched; checked against the asm)
    // C: move_mode = move_mode == 1 ? 0 : 1; on: the text DS:074A, the ticks of the Orders entries DS:07AA = 1 / DS:07BC = 2 (the first
    // C: bytes of their texts; here read from MoveMode when drawn), DS:D7FE = 1, marching_order_rebuild(1), DS:A6D3[0..4] = 0 and for
    // C: the five party records +0x4A = 0xFF, order_queue_clear(i), +0x10 = 0; off: the text DS:0768, both ticks 2, DS:D7FE = 0,
    // C: DS:9681 = 2 when DS:D610 == 0, the same clears without order_queue_clear; then show_message(text, 60), order_menu_reset and
    // C: DS:9609 = 1 when DS:A695 == 1
    public void ToggleGroup()
    {
        MoveMode = MoveMode == 1 ? 0 : 1;
        if (MoveMode == 1)
        {
            CursorKeysOn = true;
            RebuildMarchingOrder(message: true);
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                var c = Battle[i];
                c.Moving = false;
                c.Target = -1;
                c.ClearOrders();
                c.Action = CombatAction.None;
            }
        }
        else
        {
            CursorKeysOn = false;
            if (!GroupMoveAllowed)
            {
                Menus.Set(OrdersItem.Group, 2);
            }
            StopMembers();
        }
        _view.Message(MoveMode == 1 ? TravelAsGroupText : TravelIndividuallyText, 60);
        AfterMoveModeChange();
    }

    /// <summary>Q: single file on or off (the caller tests the menu entry's enable byte).</summary>
    // orig: R016+0x0CDC (1EF8:0CDC) draft (byte-matched; checked against the asm)
    // C: DS:D7FE = 0, move_mode = move_mode == 2 ? 0 : 2; on: the text DS:0788, the ticks DS:07AA = 2 / DS:07BC = 1,
    // C: marching_order_rebuild(1), single_file; off: the text DS:0768, both ticks 2, DS:9695 = 2 when DS:D610 == 0, DS:A6D3[0..4] = 0 and
    // C: for the five party records +0x4A = 0xFF, +0x10 = 0; then show_message(text, 60), order_menu_reset and DS:9609 = 1 when DS:A695 == 1
    public void ToggleSingleFile()
    {
        CursorKeysOn = false;
        MoveMode = MoveMode == 2 ? 0 : 2;
        if (MoveMode == 2)
        {
            RebuildMarchingOrder(message: true);
            SingleFile();
        }
        else
        {
            if (!GroupMoveAllowed)
            {
                Menus.Set(OrdersItem.SingleFile, 2);
            }
            StopMembers();
        }
        _view.Message(MoveMode == 2 ? SingleFileText : TravelIndividuallyText, 60);
        AfterMoveModeChange();
    }

    /// <summary>DS:074A "Party will travel as a group".</summary>
    public const int TravelAsGroupText = 0x074A;

    /// <summary>DS:0788 "Party will travel in single file".</summary>
    public const int SingleFileText = 0x0788;

    /// <summary>The end of both toggles: the order menu reset (its "(Ldr)" / "(All)" suffixes follow the new move mode), Resume on in order mode.</summary>
    // C: R016+0x0BD6 at 0x0CC6..0x0CD2, R016+0x0CDC at 0x0D91..0x0D9D
    private void AfterMoveModeChange()
    {
        ResetOrderMenu();
        if (Paused)
        {
            Menus.Set(OrdersItem.Resume, 1);
        }
    }

    private void StopMembers()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            c.Moving = false;
            c.Target = -1;
            c.Action = CombatAction.None;
        }
    }

    /// <summary>The head stops; every other member follows the one in front.</summary>
    // orig: single_file R016+0x0E7E (1EF8:0E7E) draft (byte-matched)
    // C: no test of the length: the head is DS:C624[0] (cbw) even when the length is 0 (nobody able: a rebuild leaves the bytes as they were),
    // C: so that stale member is activated (R016+0x09BC, which refuses empty, dead, away and departed members) and its path byte 0 becomes 0xFF;
    // C: the followers' loop (R016+0x0EC8, length > 1) does not run.
    // DIVERGENCE: a first byte past the thirteen combatant records (a saved game's bytes) would write past them; here nothing is done.
    private void SingleFile()
    {
        var first = MarchingOrder.Count > 0 ? MarchingOrder[0] : (sbyte)MarchingOrderBytes[0];
        if (first is < 0 or >= Combatant.Count)
        {
            return;
        }
        ActivateMember(first);
        var head = Battle[first];
        head.Path[0] = 0xFF;
        head.PathIndex = 0;
        head.Target = -1;
        head.ClearOrders();
        head.Action = CombatAction.None;
        for (var k = 1; k < MarchingOrder.Count; k++)
        {
            var b = Battle[MarchingOrder[k]];
            b.ClearOrders();
            b.Action = CombatAction.None;
            // C: the target is the INDEX in the marching order, not the combatant (faithful slip).
            b.Target = (sbyte)(k - 1);
            b.Destination = Battle[MarchingOrder[k - 1]].Position;
            if (FindPath(b.Index))
            {
                GivePath(b.Index);
                b.PathIndex = 0;
                b.RepathAt = (ushort)(Scaled(60) + Clock + 30);
                b.Moving = true;
                b.Action = CombatAction.Walk;
            }
        }
    }

    /// <summary>H (when Halt is on): individually the selected member is ordered to halt (a place action given when order mode ends);
    /// travelling together everybody stops at once.</summary>
    // orig: R016+0x101E (1EF8:101E) matched; R016+0x1364 (1590(10))
    public void Halt(int member)
    {
        _ = member;
        if (!Menus.On(OrdersItem.Halt))
        {
            return;
        }
        if (MoveMode == 0)
        {
            ChoosePlace(PlaceAction.Halt);
            return;
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (c.IsAble)
            {
                c.Action = CombatAction.None;
                c.Moving = false;
                _view.RowChanged(i);
                SetPose(i, Pose.Walking);
                c.State = ActionState.Idle;
            }
        }
    }

    /// <summary>X (when Exit Battlefield is on): in order mode the selected member is ordered to leave; else the leader leaves now
    /// (travelling together outside real combat, everybody).</summary>
    // orig: R016+0x1242 (1EF8:1242) matched; R016+0x138E (1590(8))
    public void ExitKey()
    {
        if (!Menus.On(OrdersItem.Exit))
        {
            return;
        }
        if (Paused)
        {
            if (Selected != -1)
            {
                ChoosePlace(PlaceAction.Leave);
            }
            return;
        }
        if (MoveMode == 0)
        {
            LeaveField(Battle.Leader);
            return;
        }
        if (!Battle.RealCombat)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (Battle[i].IsAble)
                {
                    LeaveField(i);
                }
            }
            _view.RowChanged(-1);
        }
    }

    private void LeaveField(int i)
    {
        var c = Battle[i];
        c.Floor = BattleEnd.LeftThroughExit;
        c.Flags = (c.Flags & ~TacticalFlags.InExitZone) | TacticalFlags.LeftField;
        ForgetTarget(i);
        CancelAction(i);
        _redraw = true;
        _view.RowChanged(i);
    }

    /// <summary>A whole order at once (tests, the screen's shortcuts): a place action for member i, given now.</summary>
    public void OrderPlace(int i, PlaceAction action)
    {
        if (Selected != i)
        {
            EnterOrderMode(i);
        }
        ChoosePlace(action);
        LeaveOrderMode();
    }

    /// <summary>A whole throw at once: member i throws the potion at the enemy (or the spot).</summary>
    /// <returns>False when the throw is refused.</returns>
    public bool OrderThrow(int i, int potion, int quality, int target, MapPoint spot)
    {
        if (Selected != i)
        {
            EnterOrderMode(i);
        }
        ChooseThrow(potion, quality);
        var refused = OrderClick(-1, target, spot);
        LeaveOrderMode();
        return !refused;
    }

    /// <summary>A click with the party travelling as a group or in single file: the formation (or the head) walks to the point.</summary>
    /// <returns>False when the move is refused (blocked).</returns>
    // orig: combat_mouse R014+0xB576 (19CD:0006) draft (the group move) and group_move R014+0xC75C (19CD:11EC)
    // draft (outline, unverified per spec D §7.4)
    // C: checked against the asm (R014+0xB8E8..0xBB4C): DS:DA4B = clock + 60 first; each path found shows the free ground cursor (DS:D845 =
    // 0xFE), a refusal the blocked one (0xFD) with the sound 0x12 (R014+0x4694)
    public bool GroupMove(MapPoint to)
    {
        if (MarchingOrder.Count == 0)
        {
            return false;
        }
        _groupCursorUntil = (ushort)(Clock + 60);
        var lead = MarchingOrder[0];
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (Battle[i].IsAble)
            {
                Battle[i].Moving = true;
            }
        }
        if (!PlanGroupMove(lead, to))
        {
            Refuse();
            return false;
        }
        if (MoveMode != 1)
        {
            var head = Battle[lead];
            head.Destination = to;
            if (FindPath(lead))
            {
                GivePath(lead);
                Shown(free: true);
                head.Action = CombatAction.Walk;
                head.Target = -1;
                head.PathIndex = 0;
                return true;
            }
            Shown(free: false);
            _view.Sound(0x12); // orig: R014+0x4694 (0E76:4694) matched: sound 0x12, a refused order
            head.Moving = false;
            return false;
        }
        foreach (var n in MarchingOrder)
        {
            var c = Battle[n];
            if (!c.IsAble)
            {
                continue;
            }
            if (!FindPath(n))
            {
                Shown(free: false);
                _view.Sound(0x12);
                for (var i = 0; i < Combatant.PartySlots; i++)
                {
                    if (Battle[i].IsAble)
                    {
                        Battle[i].Moving = false;
                    }
                }
                return false;
            }
            GivePath(n);
            Shown(free: true);
            c.Action = CombatAction.Walk;
            c.PathIndex = 0;
        }
        return true;

        void Refuse()
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (Battle[i].IsAble)
                {
                    Battle[i].Moving = false;
                }
            }
            Shown(free: false);
            _view.Sound(0x12);
        }

        void Shown(bool free)
        {
            CursorTarget = free ? -2 : -3;
            Cursor = free ? OrderCursor.Ground : OrderCursor.GroundBlocked;
        }
    }

    /// <summary>The lead paths to the point; in group mode the formation is laid out there (facing the move) and becomes everybody's destination.</summary>
    // orig: group_move R014+0xC75C (19CD:11EC) draft
    private bool PlanGroupMove(int lead, MapPoint to)
    {
        var saved = Battle.Combatants.Take(Combatant.PartySlots).Select(c => c.Position).ToArray();
        var l = Battle[lead];
        l.Destination = to;
        if (!FindPath(lead))
        {
            return false;
        }
        var ok = true;
        if (MoveMode == 1)
        {
            int dir;
            if (Distance((to.X - l.Position.X) / 2, to.Y - l.Position.Y) < 49)
            {
                dir = DirectionToward(l.Position, to, true);
            }
            else
            {
                GivePath(lead);
                var p = l.Path;
                var n = Paths.PathLength;
                if (n > 0 && p[n - 1] == 0xFF)
                {
                    n--;
                }
                // R014+0xC833..0xC891: es:[0x6237 + lead * 0xFA + DS:E118] is path byte n - 1 (the paths start at 20A5:6238), its high
                // nibble indexes DS:179C (bytes 0x0A..0x0F lie in DS:17A6, the next table: 0xFF reads DS:17AB).
                // BUG: a path of fewer than 2 steps (a lead whose first step meets its target gives the bare 0xFF) is read before its start:
                // the previous combatant's path bytes 248, 249 (for combatant 0 the last loot pile record's +10, +11, 20A5:6236, which no
                // code writes: 0 here).
                int Byte(int at) => at >= 0 ? p[at] : lead > 0 ? Battle[lead - 1].Path[Combatant.PathLength + at] : 0;
                int At(int k)
                {
                    var f = Byte(n - k) >> 4;
                    return f < FacingOfDirection.Length ? FacingOfDirection[f] : PushFacing[f - FacingOfDirection.Length];
                }
                // R014+0xC854..0xC8B9: a = root 0410:005A(At(2), last) = (At(2) - last) & 7; dir = root 0410:004C(last, (2a + 1) / 3) = (last + ..) & 7
                var last = At(1);
                var a1 = (At(2) - last) & 7;
                dir = (last + (a1 * 2 + 1) / 3) & 7;
            }
            var (ox, oy) = (0, 0);
            if (InAnyBox(to))
            {
                (ox, oy) = (SecondFileX[dir] / 2, SecondFileY[dir] / 2);
            }
            var at = new MapPoint(to.X + ox, to.Y + oy);
            // R014+0xCA2C..0xCA79: the direction, then one less (root 0410:005A(dir, 1)), then one more (root 0410:004C(dir, 1))
            ok = Formation(at, dir, to) || Formation(at, (dir - 1) & 7, to) || Formation(at, (dir + 1) & 7, to);
        }
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            var c = Battle[k];
            c.Destination = c.Position;
            c.Position = saved[k];
        }
        if (!ok)
        {
            for (var k = 0; k < Combatant.PartySlots; k++)
            {
                Battle[k].Moving = false;
            }
        }
        return ok;
    }

    /// <summary>The point lies in a doorway, on stairs, in a chest region or in an exit zone (edges included).</summary>
    // orig: group_move R014+0xC75C (19CD:11EC) draft (the shift test)
    private bool InAnyBox(MapPoint p)
    {
        bool In(int x, int y, int w, int h) => x <= p.X && p.X <= w + x && y <= p.Y && p.Y <= h + y;
        for (var k = 0; k < Level.GetByte(DoorCountAt); k++)
        {
            var (x, y, w, h) = Box(DoorsAt + 14 * k);
            if (In(x, y, w, h))
            {
                return true;
            }
        }
        for (var k = 0; k < Level.GetByte(StairsCountAt); k++)
        {
            var (x, y, w, h) = Box(StairsAt + 14 * k);
            if (In(x, y, w, h))
            {
                return true;
            }
        }
        for (var k = 0; k < Level.GetByte(RegionCountAt); k++)
        {
            var (x, y, w, h) = Box(RegionsAt + RegionSize * k);
            if (In(x, y, w, h))
            {
                return true;
            }
        }
        for (var k = 0; k < Level.GetByte(ZoneCountAt); k++)
        {
            var at = ZonesAt + 10 * k;
            if (In(Level.GetWord(at), Level.GetWord(at + 2), Level.GetWord(at + 4), Level.GetWord(at + 6)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The marching order in two files behind the point; every spot free and in sight of the point clicked.</summary>
    // orig: R014+0xCBBA (19CD:164A) draft; R014+0xCC84 (19CD:1714) draft
    // C: the line of sight starts at the point clicked (DS:E0A5 / DS:E0A3, set at R014+0xC9FD / 0xCA13), not at the shifted point.
    private bool Formation(MapPoint at, int dir, MapPoint clicked)
    {
        for (var k = 0; k < MarchingOrder.Count; k++)
        {
            var row = k >> 1;
            var c = Battle[MarchingOrder[k]];
            var odd = (k & 1) == 1;
            c.Position = new MapPoint(FileX[dir] + row * RankX[dir] + at.X + (odd ? SecondFileX[dir] : 0),
                FileY[dir] + row * RankY[dir] + (odd ? SecondFileY[dir] : 0) + at.Y);
            if (!Map.Collides(c.Index, c.Position.X, c.Position.Y).IsFree || Map.LineBlocked(clicked, c.Position) != 0)
            {
                return false;
            }
        }
        return true;
    }
}
