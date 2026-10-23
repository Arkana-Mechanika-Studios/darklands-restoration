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
        if (!Paused)
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

    /// <summary>A place action is chosen (Use Door, Stairs, Pick Lock …): it is given when order mode ends.</summary>
    // orig: R016+0x1590 (1EF8:1590) matched
    // C: R016+0x1590 has no test of DS:A6B2: H with Halt on outside order mode (R016+0x101E, individually) gives DS:A6B0 = 0x400,
    // C: DS:A6D1 = 10 and DS:E1CA = 0xFF with nobody selected. R016+0x15BE then clears 0x10 of flags[-1]'s high byte, which is DS:A695
    // C: (ai_paused, 0 or 1): nothing changes.
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

    /// <summary>order_mode_leave with DS:A6B2 = 0xFF: what the original does to the data the rewrite keeps, then its end.</summary>
    // orig: R014+0xBCA8 (19CD:0738) draft, checked against the asm for A6B2 = 0xFF
    // DIVERGENCE: the original writes record -1 (DS:9BD5.., the bytes before combatant 0) and entry -1 of the per-member tables: for
    // 0x400 DS:9BE5 = 0, DS:A6C3 = the place, 0x10 of DS:A695 cleared; for a spot its +0x08/+0x0A; for a cursor target >= 1 or -2 the
    // whole order (R014+0xBD92.., 0xBF9F..: the path, the weapon, DS:17B0 = 0xFF). The rewrite keeps no record -1: none of these is done.
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
        _orderMode = false;
        _view.RowChanged(-1);
        OrderAction = -1;
        Cursor = OrderCursor.None;
        CursorTarget = -1;
        ResetOrderMenu();
    }

    /// <summary>Esc in order mode: the order is dropped and order mode ends (the game stays paused).</summary>
    // orig: R016+0x13D4 (1EF8:13D4) matched; DS:D7FE (the cursor keys) is not touched
    // C: no test of DS:A6B2: Esc while paused outside order mode runs it with nobody selected.
    // DIVERGENCE: with nobody selected the original's flee bit goes to flags[-1]'s high byte, DS:A695 (ai_paused): with DS:D6EA = 1 the
    // pause byte becomes 0x11, which its tests read as neither paused (== 1) nor running (== 0). Paused is a flag here: not reproduced.
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
        if (!Paused)
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
