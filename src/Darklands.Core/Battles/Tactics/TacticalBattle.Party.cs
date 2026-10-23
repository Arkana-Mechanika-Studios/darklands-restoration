using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

public sealed partial class TacticalBattle
{
    /// <summary>
    /// The party surrenders (after "Do you really want to surrender?"): result 4 with the level's
    /// card; members who already left make it a flight for those (result 2), the others are taken;
    /// members on another level than the enemies leave the fight going on.
    /// </summary>
    // orig: do_surrender R022+0x03E4 (1EF8:03E4) matched
    // C: the card is the level's word at +0x1F44 (the "Unknown" exit card) and, with members gone, the one after it (+0x1F46).
    public void Surrender()
    {
        if (!Battle.RealCombat)
        {
            return;
        }
        Battle.Result = BattleResult.Surrendered;
        bool any = false, other = false;
        Battle.ResultCard = Battle.ExitCards.Unknown;
        Battle.EnemyFloor = Battle.FloorShown;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            if (Battle[i].IsAble)
            {
                Battle.EnemyFloor = Battle[i].Floor;
                break;
            }
        }
        var enemyFloor = Battle.EnemyFloor;
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (c.IsAble)
            {
                if (c.Floor == BattleEnd.LeftThroughExit)
                {
                    any = true;
                    Battle.Result = BattleResult.Fled;
                }
                if (c.Floor != enemyFloor)
                {
                    other = true;
                }
            }
        }
        if (any)
        {
            Battle.ResultCard = Battle.ExitCards.Fled;
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                var c = Battle[i];
                if (c.Floor != BattleEnd.LeftThroughExit && c.Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                {
                    c.Status = CharacterStatus.Away;
                }
            }
            return;
        }
        if (other)
        {
            Battle.Result = BattleResult.Running;
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                var c = Battle[i];
                if (c.Floor == enemyFloor && c.Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                {
                    c.Status = CharacterStatus.Away;
                }
            }
        }
    }

    /// <summary>The enemy kinds the Enemy Info shows: the first two different animation slots among the enemies present, with a combatant of each.</summary>
    // orig: enemy_info R021+0x02CE (1EF8:02CE) matched
    public List<int> EnemyInfoKinds()
    {
        var list = new List<int>();
        int first = -1, second = -1;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var c = Battle[i];
            var kind = c.AnimationSlot;
            if (c.Status != CharacterStatus.Empty && kind != first && kind != second)
            {
                if (list.Count == 0)
                {
                    first = kind;
                }
                else
                {
                    second = kind;
                }
                list.Add(i);
                if (list.Count == 2)
                {
                    break;
                }
            }
        }
        return list;
    }

    /// <summary>DS:C28D[slot]: the enemy kind (index on the level) an enemy animation slot shows.</summary>
    public int EnemyKindIndex(int slot) => slot is >= 0 and < 7 ? _slotKind[slot] : -1;

    /// <summary>The marching order the player chose (Change Marching Order); in single file the others follow again.</summary>
    // orig: choose_party_order R022+0x051C (1EF8:051C) draft (the end: single_file_vec when move_mode is 2); R022+0x07AA matched
    public void SetMarchingOrder(IReadOnlyList<int> order)
    {
        MarchingOrder.Clear();
        MarchingOrder.AddRange(order);
        if (MoveMode == 2)
        {
            SingleFile();
        }
    }

    /// <summary>Alt-O in the battle's key switch: everybody stops and real combat ends.</summary>
    // orig: combat_keys R016+0x0000 (1EF8:0000) draft (case 0x1800)
    // C: a debugging key left in; the hot key (Change Marching Order) takes Alt-O first when that entry is on.
    public void StopEverything()
    {
        foreach (var c in Battle.Combatants)
        {
            c.Action = CombatAction.None;
            c.Moving = false;
        }
        Battle.RealCombat = false;
        ResetOrderMenu();
    }

    /// <summary>The group cursor while the left button is held travelling together: the leader's box at the point, free or blocked.</summary>
    // orig: combat_mouse R014+0xB576 (19CD:0006) draft (the group cursor; DS:DA4B its timeout)
    // C: `DS:DA4B = DS:D7FE == 0 ? combat_clock + 60 : combat_clock + 1800`: with the cursor keys on (order mode's DS:D7FE) the cursor stays 30 s.
    public void GroupCursor(MapPoint ground, bool cursorKeys = false)
    {
        if (MarchingOrder.Count == 0)
        {
            return;
        }
        var lead = MarchingOrder[0];
        CursorPoint = ground;
        _groupCursorUntil = (ushort)(Clock + (cursorKeys ? 1800 : 60));
        // C: the test is made on a scratch copy of the head's record facing 0x14, with no target.
        var c = Battle[lead];
        var saved = c.Facing;
        c.Facing = 0x14;
        var free = Map.Collides(lead, ground.X, ground.Y).IsFree;
        c.Facing = saved;
        CursorTarget = free ? -2 : -3;
        Cursor = free ? OrderCursor.Ground : OrderCursor.GroundBlocked;
    }

    private ushort _groupCursorUntil;

    /// <summary>The keyboard's click while travelling together: the group cursor stays a second (DS:DA4B = clock + 60).</summary>
    // orig: R014+0xB838.. `if (DS:D73B == 1) { if (move_mode != 0) DS:DA4B = combat_clock + 60; ... }`
    public void KeepGroupCursor() => _groupCursorUntil = (ushort)(Clock + 60);

    /// <summary>Travelling together, the group cursor goes a second after the last move (DS:DA4B &lt; clock).</summary>
    public void GroupCursorTimeout()
    {
        if (MoveMode != 0 && _groupCursorUntil < Clock)
        {
            CursorTarget = -1;
        }
    }

    /// <summary>The party records get the combatants' state before a screen that works on the members (the character sheet).</summary>
    public void MembersFromCombat()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (Battle[i].Member is not null)
            {
                CombatantSetup.BackToMember(Battle[i]);
            }
        }
    }

    /// <summary>
    /// After the character sheet: what it changed (pack, equipment, attributes, potions) comes back;
    /// a shooter whose missile weapon is gone stops shooting and readies the hand weapon; the attack
    /// rates are worked out again.
    /// </summary>
    // orig: combat_char_sheet R022+0x0000 (1EF8:0000) draft (the end: the 0x80 loop and memset of the rates)
    public void MembersBackToCombat()
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var c = Battle[i];
            if (c.Member is not { } m)
            {
                continue;
            }
            CombatantSetup.Refresh(c, m);
            var shooting = c.Action == CombatAction.Missile || (Selected == i && OrderAction == 0x80);
            if (c.IsAble && shooting && c.ReadiedWeapon == -1)
            {
                c.Action = CombatAction.None;
                c.State = ActionState.Idle;
                c.Target = -1;
                ReadyWeapon(i, c.DefaultWeapon, 0);
                if (Selected == i)
                {
                    OrderAction = -1;
                    _targetChosen = false;
                }
            }
        }
        foreach (var c in Battle.Combatants)
        {
            c.AttackRate = 0xFF;
        }
        // orig: R022+0x03A6..0x03B0: floor_view_draw(-1) and combat_draw (R014+0x6818), the same two calls as the battle's set-up (R014+0x02FF /
        // 0x0307): the draw list's random draws (the animated pieces of the view) come once more as the sheet closes
        _view.MapChanged();
        TurnTowardTargets();
        Effects.Drawn(_view);
        RemoveArrivedMissiles();
        RefreshTargetCursor();
    }
}
