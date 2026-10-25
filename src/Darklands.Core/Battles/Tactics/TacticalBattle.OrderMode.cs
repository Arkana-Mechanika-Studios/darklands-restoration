using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>What the order cursor shows (the sprite DS:E1BC of the cursor box).</summary>
public enum OrderCursor
{
    /// <summary>0: no cursor.</summary>
    None,

    /// <summary>DS:C28F: a free spot to walk to.</summary>
    Ground,

    /// <summary>DS:C244: a blocked spot.</summary>
    GroundBlocked,

    /// <summary>DS:C242: an enemy targeted.</summary>
    Target,

    /// <summary>DS:C2BD: an enemy without a line of fire (or unreachable).</summary>
    TargetBlocked,

    /// <summary>DS:C4DB: a spot to throw at.</summary>
    Throw,

    /// <summary>DS:C621: a spot the throw cannot reach.</summary>
    ThrowBlocked,
}

/// <summary>
/// Order mode (the game paused while a member is given an order): the order chosen from the
/// menus or the letters, the cursor that follows the pointer, the click that picks a target or a
/// spot, and leaving order mode, which gives the order.
/// </summary>
// orig: order_mode_enter R014+0xBC0C, order_mode_leave R014+0xBCA8, the hover R014+0xC044, the click R014+0xC33C,
// the initial cursor R014+0xCF5E (draft); the order letters of R016 (156E, 1590, 152E, 1332, 133E,
// 13D4, 1436: matched)
public sealed partial class TacticalBattle
{
    /// <summary>selected_member (DS:A6B2): the member in order mode, −1 none.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>order_action (DS:A6B0): −1 none chosen, 1 walk, an attack stance, 0x80 missile, 0x100 throw, 0x400 a place action.</summary>
    public int OrderAction { get; private set; } = -1;

    /// <summary>DS:A6D1: the place action of a 0x400 order.</summary>
    public int PlaceOrder { get; private set; } = -1;

    /// <summary>cursor_target (DS:D845): ≥ 0 the combatant pointed at, −1 nothing, −2 a free spot, −3 a blocked spot.</summary>
    public int CursorTarget { get; private set; } = -1;

    /// <summary>DS:E1B4 / DS:E1B6: the map point of the ground cursor.</summary>
    public MapPoint CursorPoint { get; private set; }

    public OrderCursor Cursor { get; private set; }

    /// <summary>DS:C7CC: a target or spot was clicked; DS:C762: the clicked enemy is in reach (no walk).</summary>
    private bool _targetChosen, _adjacent;

    /// <summary>DS:C652 / DS:C67D: the spot clicked (x 0: none).</summary>
    private MapPoint _orderSpot;

    /// <summary>DS:D6EA: the member's flee bit when order mode began; DS:C867: the clock then.</summary>
    private bool _fleeBefore;
    private ushort _orderClock;

    /// <summary>DS:C1CE / DS:C1F2 / DS:E1C8: the potion chosen to throw, its quality, and whether it cannot be thrown at a spot (potion 4, Eater-Water).</summary>
    private int _throwPotion, _throwQuality;
    private bool _throwAtTargetOnly;

    /// <summary>DS:C600: the members do not block the ground cursor's collision test (set around the throw's test).</summary>
    private bool OrderModeOn => Selected != -1 && _orderMode;

    private bool _orderMode;

    /// <summary>
    /// DS:D7FE: the cursor keys move the pointer (set by entering order mode and by travelling as a group, cleared by leaving
    /// order mode, by the group's end and by single file).
    /// </summary>
    // orig: R014+0xBC0C sets it, R014+0xBCA8 clears it; R016+0x0BD6 (the group: 1 when move_mode
    // becomes 1, else 0), R016+0x0CDC (single file: 0)
    public bool CursorKeysOn { get; private set; }

    /// <summary>order_mode (DS:A6B3): a member is being given an order.</summary>
    public bool InOrderMode => _orderMode;

    /// <summary>
    /// The left button held over the view outside order mode: the member under the pointer (−1 none) becomes the selected
    /// one, with the cursor of its current order, without entering order mode (the release then enters it).
    /// </summary>
    // orig: R014+0xB632.. (the hover of the mouse handler with order_mode 0) draft, checked against the asm: `selected_member =
    // hotspot_cbt[k]; member cursor; order_action = -1; DS:C7CC = 0; R014+0xCF5E();` for the last-drawn member hot spot under the
    // pointer, else selected_member = -1; tests: BattleUiTests
    public void PreviewMember(int member)
    {
        if (member < 0)
        {
            Selected = -1;
            return;
        }
        Selected = member;
        OrderAction = -1;
        _targetChosen = false;
        InitialCursor();
    }

    /// <summary>After the character sheet opened from the panel: every able member readies its weapon again.</summary>
    // orig: R014+0x3198.. `for (i = 0; i < 5; i++) if (status == 1) R019+0x0B9E(i);` (R019+0x0B9E)
    public void ReReadyAbleMembers()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (Battle[i].Status == CharacterStatus.Able)
            {
                Effects.ReReady(i);
            }
        }
    }

    /// <summary>
    /// Order mode for member i: the cursor of its current order, the order menu, and the game
    /// paused (the clock stops).
    /// </summary>
    // orig: order_mode_enter R014+0xBC0C (19CD:069C) matched
    public void EnterOrderMode(int member)
    {
        if (member is < 0 or >= Combatant.PartySlots)
        {
            return;
        }
        Selected = member;
        _orderMode = true;
        CursorKeysOn = true;
        Battle[member].Selected = true;
        _view.RowChanged(-1);
        _fleeBefore = (Battle[member].Flags & TacticalFlags.Following) != 0;
        OrderAction = -1;
        PlaceOrder = -1;
        _targetChosen = false;
        Cursor = OrderCursor.None;
        _orderSpot = default;
        InitialCursor();
        if (!Battle.RealCombat)
        {
            PileNextTo(member);
        }
        BuildOrderMenu(member);
        // orig: R014+0xBC87 (== 0)
        if (Running)
        {
            Paused = true;
            _orderClock = Clock;
            Menus.Set(OrdersItem.Resume, 1);
        }
    }

    /// <summary>The cursor of the selected member's current order: its target, or the spot it walks or throws to.</summary>
    // orig: R014+0xCF5E (19CD:19EE) draft
    private void InitialCursor()
    {
        var c = Battle[Selected];
        if (c.Target == -1)
        {
            if (!c.Moving && c.Action != CombatAction.UseItem && c.Action != CombatAction.Walk)
            {
                CursorTarget = -1;
                Cursor = OrderCursor.None;
                return;
            }
            Cursor = c.Action == CombatAction.UseItem ? OrderCursor.Throw : OrderCursor.Ground;
            CursorPoint = c.Destination;
            CursorTarget = -2;
            return;
        }
        // C: the target is looked for among the hot spots of the last frame (R014+0xCF7A..0xCF9C): one without a hot spot leaves the
        // C: cursor target and the sprite as they were.
        if (!_view.HasHotSpot(c.Target))
        {
            return;
        }
        CursorTarget = c.Target;
        Cursor = OrderCursor.Target;
    }

    /// <summary>The letters and the Attack menu entries choose an order: walk / flee (1), a stance, missile (0x80).</summary>
    // orig: R016+0x156E (1EF8:156E), R016+0x1332, R016+0x133E matched
    public void ChooseOrder(int action, bool flee = false)
    {
        if (Selected == -1)
        {
            return;
        }
        OrderAction = action;
        if (action == 1)
        {
            if (flee)
            {
                Battle[Selected].Flags |= TacticalFlags.Following;
            }
            return;
        }
        if (CursorTarget < -1)
        {
            CursorTarget = -1;
            Cursor = OrderCursor.None;
        }
        // 15BE (the letter) clears the flee bit after drawing it.
        Battle[Selected].Flags &= ~TacticalFlags.Following;
    }

    /// <summary>The flee bit (0x1000) of a member's flags in the high byte of its word: what an access to flags[-1] reaches in DS:A695.</summary>
    private const int FlagsHighByte = (int)TacticalFlags.Following >> 8;

    /// <summary>A place action is chosen (Use Door, Stairs, Pick Lock …): it is given when order mode ends.</summary>
    // orig: R016+0x1590 (1EF8:1590) matched
    // C: R016+0x1590 has no test of DS:A6B2: H with Halt on outside order mode (R016+0x101E, individually) gives DS:A6B0 = 0x400,
    // C: DS:A6D1 = 10 and DS:E1CA = 0xFF with nobody selected. R016+0x15BE then clears 0x10 of flags[-1]'s high byte, which is DS:A695
    // C: (ai_paused): nothing changes while it is 0 or 1; after Esc made it 0x11 (CancelOrder) it becomes 1 again.
    public void ChoosePlace(PlaceAction action)
    {
        OrderAction = 0x400;
        PlaceOrder = (int)action;
        if (CursorTarget < -1)
        {
            CursorTarget = -1;
            Cursor = OrderCursor.None;
        }
        if (Selected != -1)
        {
            Battle[Selected].Flags &= ~TacticalFlags.Following;
        }
        else if (!Battle.Behaviour.FixEscapeWithoutSelectionPauseByte)
        {
            // BUG: OriginalBehaviour.FixEscapeWithoutSelectionPauseByte - flags[-1]'s high byte is the pause byte DS:A695 (R016+0x15BE, bx = -2)
            PauseByte = (byte)(PauseByte & ~FlagsHighByte);
        }
        _picker = Selected;
    }

    /// <summary>A potion of the Throw list is chosen (Attack, Throw, line k).</summary>
    // orig: R016+0x152E (1EF8:152E) matched
    public void ChooseThrow(int potion, int quality)
    {
        if (Selected == -1)
        {
            return;
        }
        Battle[Selected].Flags &= ~TacticalFlags.Following;
        OrderAction = 0x100;
        _throwPotion = potion;
        _throwQuality = quality;
        _throwAtTargetOnly = potion == 4;
        Battle[Selected].Flags &= ~TacticalFlags.Following;
    }

    /// <summary>The pointer moves in order mode: the cursor box follows it (a target, a free or blocked spot).</summary>
    /// <param name="topmost">The front-most combatant under the pointer (any side), −1 none.</param>
    /// <param name="enemy">The front-most enemy under the pointer, −1 none.</param>
    /// <param name="ground">The map point under the pointer.</param>
    // orig: R014+0xC044 (19CD:0AD4) draft
    public void Hover(int topmost, int enemy, MapPoint ground)
    {
        if (Selected == -1 || topmost is >= 0 and < Combatant.PartySlots)
        {
            return;
        }
        CursorTarget = -1;
        var f = (int)Battle[Selected].Action;
        var k = -1;
        if (!(OrderAction == 1 || (OrderAction == -1 && f is 1 or 0)) && enemy >= Combatant.PartySlots)
        {
            k = enemy;
            CursorTarget = enemy;
            Cursor = OrderCursor.Target;
            if (OrderAction is 0x80 or 0x100 || (OrderAction == -1 && f is 0x80 or 0x100))
            {
                if (Map.LineBlocked(Battle[Selected].Position, Battle[enemy].Position, Selected, enemy, 0x80) != 0)
                {
                    Cursor = OrderCursor.TargetBlocked;
                }
            }
        }
        if (k < 0 && ((OrderAction == -1 && f != 0x1082) || OrderAction == 1 || (OrderAction == 0x100 && !_throwAtTargetOnly)))
        {
            CursorPoint = ground;
            Map.IgnorePeople = OrderAction == 0x100;
            var free = Map.Collides(Selected, ground.X, ground.Y).IsFree;
            Map.IgnorePeople = false;
            if (free)
            {
                CursorTarget = -2;
                Cursor = OrderCursor.Ground;
                if (OrderAction == 0x100 && !_throwAtTargetOnly)
                {
                    Cursor = OrderCursor.Throw;
                    if (Map.LineBlocked(Battle[Selected].Position, ground, Selected, Selected, 0x100) == 1)
                    {
                        Cursor = OrderCursor.ThrowBlocked;
                        CursorTarget = -3;
                    }
                }
            }
            else
            {
                Cursor = OrderCursor.GroundBlocked;
                CursorTarget = -3;
            }
        }
        if (CursorTarget == -1)
        {
            Cursor = OrderCursor.None;
        }
    }

    /// <summary>
    /// A click in order mode: on a member it changes the member; on an enemy it is the target (a
    /// path is found when out of reach); on the ground the spot (a path is found to it).
    /// </summary>
    /// <returns>True when the click was refused (the sound of a refusal was made).</returns>
    // orig: R014+0xC33C (19CD:0DCC) draft
    public bool OrderClick(int topmost, int enemy, MapPoint ground)
    {
        if (topmost is >= 0 and < Combatant.PartySlots)
        {
            if (Selected != -1)
            {
                LeaveOrderMode();
            }
            SelectMember(topmost);
            return false;
        }
        CursorTarget = -1;
        Cursor = OrderCursor.None;
        if (Battle.PartyFloor != Battle.FloorShown)
        {
            _view.Message(UseStairsText, 0x96);
            return false;
        }
        _adjacent = false;
        var i = Selected;
        var c = Battle[i];
        var f = (int)c.Action;
        var refused = false;
        if (!(OrderAction == 1 || (OrderAction == -1 && f is 1 or 0)) && enemy >= Combatant.PartySlots)
        {
            var saved = (c.Destination, c.Target, c.Facing);
            CursorTarget = enemy;
            c.Destination = CursorPoint = Battle[enemy].Position;
            c.Target = (sbyte)enemy;
            if (OrderAction is 0x100 or 0x80)
            {
                if (Map.LineBlocked(c.Position, c.Destination, i, enemy, 0x100) == 1)
                {
                    _view.Sound(0x12);
                    refused = true;
                    Cursor = OrderCursor.TargetBlocked;
                }
                else
                {
                    _targetChosen = true;
                    Cursor = OrderCursor.Target;
                }
            }
            else if (!Battle.Reaches[i, enemy])
            {
                if (!FindPath(i))
                {
                    _view.Sound(0x12);
                    refused = true;
                    Cursor = OrderCursor.TargetBlocked;
                }
                else
                {
                    SavePath();
                    _targetChosen = true;
                    Cursor = OrderCursor.Target;
                }
            }
            else
            {
                _adjacent = true;
                _targetChosen = true;
                Cursor = OrderCursor.Target;
            }
            (c.Destination, c.Target, c.Facing) = saved;
            return refused;
        }
        if (!((OrderAction == -1 && f != 0x1082) || OrderAction == 1 || (OrderAction == 0x100 && !_throwAtTargetOnly)))
        {
            return false;
        }
        var keep = (c.Destination, c.Target, c.Facing);
        c.Destination = CursorPoint = ground;
        c.Target = -1;
        c.Facing = 0x14;
        if (OrderAction == 0x100 && !_throwAtTargetOnly)
        {
            Cursor = OrderCursor.Throw;
            if (Map.LineBlocked(c.Position, ground, i, i, 0x100) == 1)
            {
                Cursor = OrderCursor.ThrowBlocked;
                CursorTarget = -3;
                _view.Sound(0x12);
                (c.Destination, c.Target, c.Facing) = keep;
                return true;
            }
        }
        else
        {
            if (!FindPath(i))
            {
                Cursor = OrderCursor.GroundBlocked;
                CursorTarget = -3;
                _view.Sound(0x12);
                (c.Destination, c.Target, c.Facing) = keep;
                return true;
            }
            SavePath();
            Cursor = OrderCursor.Ground;
        }
        CursorTarget = -2;
        _orderSpot = ground;
        (c.Destination, c.Target, c.Facing) = keep;
        return false;
    }

    /// <summary>
    /// Order mode ends and the order is given: a place action; a walk to the spot clicked; a throw
    /// at the spot or the enemy; a shot; an attack (walking there first when out of reach).
    /// </summary>
    // orig: order_mode_leave R014+0xBCA8 (19CD:0738) draft
    // C: a cursor target of 0 (party member 0) takes the spot branch and gives nothing (cursor_target &lt; 1).
    // C: R014+0xBCA8 has no test of DS:A6B2: Enter outside order mode (R016+0x0734, individually) runs it with nobody selected. The spot
    // C: of the last walk order (DS:C652, cleared only by R014+0xBC6A and R016+0x13DA) or DS:C7CC still set make it give an order to
    // C: "member -1"; then the end (R014+0xC004..: the order menu reset, the cursor) runs as always.
    public void LeaveOrderMode()
    {
        if (Selected == -1)
        {
            LeaveWithNobodySelected();
            return;
        }
        var i = Selected;
        var c = Battle[i];
        var spot = _orderSpot;
        if (!(OrderAction == -1 && !_targetChosen && spot.X == 0))
        {
            Give();
        }
        c.Selected = false;
        _orderMode = false;
        CursorKeysOn = false;
        _view.RowChanged(-1);
        OrderAction = -1;
        Cursor = OrderCursor.None;
        Selected = -1;
        CursorTarget = -1;
        ResetOrderMenu();

        void Give()
        {
            if (OrderAction == 0x400)
            {
                c.Action = CombatAction.None;
                c.Place = (PlaceAction)PlaceOrder;
                c.Flags &= ~TacticalFlags.Hurry;
                return;
            }
            if (CursorTarget == -3 || Cursor == OrderCursor.TargetBlocked)
            {
                return;
            }
            if (spot.X != 0)
            {
                c.Destination = CursorPoint = spot;
            }
            if (OrderAction == -1)
            {
                OrderAction = (int)c.Action;
            }
            if ((OrderAction & 0x1BE) == 0x1BE)
            {
                c.Flags &= ~TacticalFlags.Hurry;
            }
            if (CursorTarget < 1)
            {
                if (CursorTarget != -2)
                {
                    return;
                }
                if (OrderAction != 0x100)
                {
                    if (Battle.RealCombat && Battle.PartyFloor != Battle.FloorShown)
                    {
                        _view.Message(UseStairsText, 0x96);
                        return;
                    }
                    if (_targetChosen || spot.X != 0)
                    {
                        GiveSavedPath(i);
                        c.PathIndex = 0;
                    }
                    Battle.ActiveMember = i;
                    c.Destination = spot;
                    c.Target = -1;
                    c.Moving = true;
                    c.Action = CombatAction.Walk;
                    c.State = ActionState.Idle;
                    c.Exchange = Exchange.None;
                    c.QueuedStep = -1;
                    c.RepathAt = (ushort)(_orderClock + 30);
                    return;
                }
                c.Moving = false;
                SlotWeapons[c.AnimationSlot] = "PT";
                c.Spell = _throwPotion;
                c.SpellPower = _throwQuality;
                c.Action = CombatAction.UseItem;
                c.Destination = spot;
                c.Target = -1;
                c.EngageAt = _orderClock;
            }
            else if (OrderAction is 0x80 or 0x100)
            {
                c.Moving = false;
                if (OrderAction == 0x80)
                {
                    ReadyWeapon(i, c.ReadiedWeapon, 0x80);
                    c.Target = (sbyte)CursorTarget;
                    c.Action = CombatAction.Missile;
                    // R014+0xBE85: 0x02 of the flags' high byte (0x200)
                    c.Flags |= TacticalFlags.ShotSkipped;
                }
                else
                {
                    SlotWeapons[c.AnimationSlot] = "PT";
                    c.Spell = _throwPotion;
                    c.SpellPower = _throwQuality;
                    c.Action = CombatAction.UseItem;
                    c.Target = (sbyte)CursorTarget;
                }
                c.EngageAt = _orderClock;
            }
            else
            {
                if ((_targetChosen || spot.X != 0) && !_adjacent)
                {
                    GiveSavedPath(i);
                }
                ReadyWeapon(i, c.DefaultWeapon, 0);
                c.Target = (sbyte)CursorTarget;
                if (!_targetChosen)
                {
                    if ((OrderAction & 2) != 2)
                    {
                        return;
                    }
                    c.Action = (CombatAction)OrderAction;
                }
                else if (!_adjacent)
                {
                    c.Moving = true;
                    c.PathIndex = 0;
                    c.Action = CombatAction.Walk;
                    Battle.ActiveMember = i;
                    if ((OrderAction & 2) == 2)
                    {
                        c.QueueOrder((CombatAction)OrderAction);
                    }
                    return;
                }
                else
                {
                    c.Action = (CombatAction)OrderAction;
                }
            }
            c.State = ActionState.Ready;
        }
    }

    /// <summary>
    /// The word of record -1 that order_mode_leave reads as the order with nobody selected (+0x10 of the record before combatant 0,
    /// DS:9BE5, inside the menu bar's sub-menu item records) and writes the order it gives "member -1".
    /// </summary>
    // UNCERTAIN: the menu bar's sub-menu building (root 0000:0984 and the battle's lists) writes these bytes too (a sub-menu item's text);
    //   the rewrite starts it at 0 and keeps only what order_mode_leave writes. The branches below agree for any value other than 0x80,
    //   0x100, one with bit 1 set or one with all of 0x1BE (the value seen in the battle states, 0x1A6D, takes the same branches as 0).
    private int _actionBeforeRecords;

    /// <summary>order_mode_leave with DS:A6B2 = 0xFF: the order given to "member -1" where it lands on data the rewrite keeps, then its end.</summary>
    // orig: R014+0xBCA8 (19CD:0738) draft, checked against the asm for A6B2 = 0xFF and by direct calls (battle states, DS:A6B2 = 0xFF):
    //   entry -1 of the per-member tables is the end of the table before: action state[-1] (DS:CA63) is exchange[12], repath[-1] (DS:D97A)
    //   is give-up[12], engage[-1] (DS:D994) is repath[12], the flags' high byte [-1] (DS:A695) is the pause byte; tests: OrderModeLeadsTests
    // BUG: OriginalBehaviour.FixLeaveOrderModeWithoutSelection - Enter travelling individually (R016+0x0734) calls it with nobody selected,
    //   and a cursor target left by the button held over a member (R014+0xB632..: the target of that member's order) gives the order to
    //   record -1: for 0x400 the pause byte loses 0x10; the 0x1BE test clears it too; a missile (0x80) sets 0x02 of the pause byte; a
    //   missile or a throw sets exchange[12] = 7 (Ready) and repath[12] = the order clock DS:C867; an attack with bit 1 (or adjacent) sets
    //   exchange[12] = 7; a walk to a spot sets exchange[12] = 0 and give-up[12] = the order clock + 30, or, the party split in real combat,
    //   shows "USE STAIRS..." (DS:064A) for 150 ticks.
    // DIVERGENCE: not reproduced, the original then reads or writes undefined memory: DS:17B0 = 0xFF (walk, attack with a target chosen):
    //   the view's follow (R014+0x1065) and the keys read record -1's screen place and floor (the menu records, DS:A6B3); the record -1
    //   fields (DS:9BD5.., the menu bar's sub-menu items and DS:9C1E..9C2C: its range, the UI word, the alchemy file's far pointer through
    //   weapon_ready(-1)), path_give_saved(-1), order_queue(-1), the 'PT' name of the animation slot of record -1's kind byte, and the
    //   bytes before other tables (DS:A6C3, DS:CA2F, DS:D6FA, DS:D492, DS:9063, and DS:CA56, the high byte of the palette
    //   buffer's segment DS:CA55 that the loot screen saves the palette through). moving[-1] (DS:A6D2) is the place order's high byte (modelled).
    private void LeaveWithNobodySelected()
    {
        CursorKeysOn = false;
        var spot = _orderSpot;
        if (!(OrderAction == -1 && !_targetChosen && spot.X == 0) && OrderAction != 0x400 && CursorTarget != -3 && Cursor != OrderCursor.TargetBlocked
            && spot.X != 0)
        {
            // R014+0xBD25 / 0xBD2D
            CursorPoint = spot;
        }
        if (!Battle.Behaviour.FixLeaveOrderModeWithoutSelection)
        {
            GiveToRecordBeforeCombatants(spot);
        }
        _orderMode = false;
        _view.RowChanged(-1);
        OrderAction = -1;
        Cursor = OrderCursor.None;
        CursorTarget = -1;
        ResetOrderMenu();
    }

    // orig: R014+0xBCCD..0xC000 with i = -1, the effects on modelled data (see LeaveWithNobodySelected)
    private void GiveToRecordBeforeCombatants(MapPoint spot)
    {
        if (OrderAction == -1 && !_targetChosen && spot.X == 0)
        {
            return;
        }
        const byte Hurry = 0x10, ShotSkipped = 0x02;     // 0x1000 / 0x0200 of the flags: their high byte [-1] is the pause byte
        if (OrderAction == 0x400)
        {
            // R014+0xBCDC..0xBCEE
            _actionBeforeRecords = 0;
            PauseByte &= unchecked((byte)~Hurry);
            return;
        }
        if (CursorTarget == -3 || Cursor == OrderCursor.TargetBlocked)
        {
            return;
        }
        // R014+0xBD31..0xBD5B
        var action = OrderAction == -1 ? _actionBeforeRecords : OrderAction;
        if ((action & 0x1BE) == 0x1BE)
        {
            PauseByte &= unchecked((byte)~Hurry);
        }
        var last = Battle[Combatant.Count - 1];
        if (CursorTarget >= 1)
        {
            if (action == 0x80)
            {
                // R014+0xBE48..0xBE94
                _actionBeforeRecords = 0x80;
                MovingBeforeTable(0);
                PauseByte |= ShotSkipped;
                last.RepathAt = _orderClock;
                last.Exchange = (Exchange)ActionState.Ready;
            }
            else if (action == 0x100)
            {
                // R014+0xBEA3..0xBF0A
                _actionBeforeRecords = 0x100;
                MovingBeforeTable(0);
                last.RepathAt = _orderClock;
                last.Exchange = (Exchange)ActionState.Ready;
            }
            else if (!_targetChosen)
            {
                // R014+0xBDD8..0xBDF3
                if ((action & 2) == 2)
                {
                    _actionBeforeRecords = action;
                    last.Exchange = (Exchange)ActionState.Ready;
                }
            }
            else if (!_adjacent)
            {
                // R014+0xBE06..0xBE3A (DS:17B0 = 0xFF and order_queue(-1) not reproduced)
                _actionBeforeRecords = 1;
                MovingBeforeTable(1);
            }
            else
            {
                // R014+0xBDFD..0xBE04
                _actionBeforeRecords = action;
                last.Exchange = (Exchange)ActionState.Ready;
            }
            return;
        }
        if (CursorTarget != -2)
        {
            return;
        }
        if (action == 0x100)
        {
            // R014+0xBF20..0xBF72
            _actionBeforeRecords = 0x100;
            MovingBeforeTable(0);
            last.RepathAt = _orderClock;
            last.Exchange = (Exchange)ActionState.Ready;
            return;
        }
        if (Battle.RealCombat && Battle.PartyFloor != Battle.FloorShown)
        {
            // R014+0xBF76..0xBF91
            _view.Message(UseStairsText, 0x96);
            return;
        }
        // R014+0xBF94..0xC000 (action state[-1] = 0, repath[-1] = clock + 30; DS:17B0 = 0xFF not reproduced)
        _actionBeforeRecords = 1;
        MovingBeforeTable(1);
        last.Exchange = Exchange.None;
        last.GiveUpUntil = (ushort)(_orderClock + 30);
    }

    /// <summary>moving[-1] (DS:A6D2, the byte before the moving table) is the high byte of the place order word DS:A6D1.</summary>
    // orig: R014+0xBE4E / 0xBEAC / 0xBF26 (0), 0xBE09 / 0xBFD7 (1); DS:A6D1 is read as a word (R016+0x1652, root 0000:19FE)
    private void MovingBeforeTable(int value) => PlaceOrder = (short)((PlaceOrder & 0xFF) | value << 8);

    /// <summary>Esc in order mode: the order is dropped and order mode ends (the game stays paused).</summary>
    // orig: R016+0x13D4 (1EF8:13D4) matched; DS:D7FE (the cursor keys) is not touched
    // C: no test of DS:A6B2: Esc while paused outside order mode runs it with nobody selected.
    public void CancelOrder()
    {
        OrderAction = -1;
        _orderSpot = default;
        if (Selected != -1)
        {
            var c = Battle[Selected];
            c.Flags = _fleeBefore ? c.Flags | TacticalFlags.Following : c.Flags & ~TacticalFlags.Following;
            c.Selected = false;
        }
        else if (!Battle.Behaviour.FixEscapeWithoutSelectionPauseByte)
        {
            // BUG: OriginalBehaviour.FixEscapeWithoutSelectionPauseByte - with nobody selected the xor at R016+0x13F9 (bx = -2) lands on
            // flags[-1]'s high byte, the pause byte DS:A695: its bit 0x10 becomes DS:D6EA (the flee bit before order mode), so a pause byte
            // of 1 with D6EA = 1 turns 0x11, which the tests for paused (== 1) and for running (== 0) both refuse.
            PauseByte = (byte)((PauseByte & ~FlagsHighByte) | (_fleeBefore ? FlagsHighByte : 0));
        }
        _orderMode = false;
        _targetChosen = false;
        _view.RowChanged(-1);
        Cursor = OrderCursor.None;
        CursorTarget = -1;
        Selected = -1;
        ResetOrderMenu();
    }

    /// <summary>Space: pause (order mode may follow) or resume (leaving order mode; the attack rates are worked out again).</summary>
    // orig: combat_keys R016+0x0000 (the Space case) and R016+0x1436 (1EF8:1436) matched
    public void TogglePause()
    {
        // orig: R016+0x074E (== 0 pauses; any other value, 0x11 too, takes the resume path)
        if (Running)
        {
            Paused = true;
            Menus.Set(OrdersItem.Resume, 1);
            return;
        }
        if (Selected != -1)
        {
            LeaveOrderMode();
        }
        Paused = false;
        Menus.Set(OrdersItem.Resume, 2);
        foreach (var c in Battle.Combatants)
        {
            c.AttackRate = 0xFF;
        }
    }

    /// <summary>
    /// Each draw: the target cursor follows its combatant; for a shot or a throw (ordered, or the member's current action with no
    /// new order) its colour says whether the line of fire is clear now.
    /// </summary>
    // orig: R014+0xCD66 (19CD:17F6) unmatched (the target part; the member cursor's
    // position is the screen's); called by combat_draw
    // C: the cursor is only redone when the target has a hot spot (is drawn); without one the cursor sprite DS:E1BC = 0 (0xCECB),
    // C: which also lifts a blocked line's refusal of the order.
    private void RefreshTargetCursor()
    {
        if (CursorTarget < 0)
        {
            return;
        }
        if (!_view.CombatantInView(CursorTarget))
        {
            Cursor = OrderCursor.None;
            return;
        }
        if (Selected < 0)
        {
            return;
        }
        var f = (int)Battle[Selected].Action;
        if (OrderAction is 0x80 or 0x100 || (OrderAction == -1 && f is 0x80 or 0x100))
        {
            var blocked = Map.LineBlocked(Battle[Selected].Position, Battle[CursorTarget].Position, Selected, CursorTarget, 0x80) != 0;
            Cursor = blocked ? OrderCursor.TargetBlocked : OrderCursor.Target;
        }
    }
}
