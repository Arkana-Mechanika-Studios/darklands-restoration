using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The order mode's state paired with the original's memory: the member being given an order, the order chosen, the cursor, the spot clicked, the
/// throw chosen, the pause and the move mode, the marching order. The keys (R016), command-mode-begin / leave (R014+0xBC0C, 0xBCA8) and the menus read
/// and write these. Each entry is a place of the original's data segment and the property or field of <see cref="TacticalBattle"/> that keeps it.
/// </summary>
internal static class OrderOverlay
{
    public const int Selected = 0xA6B2, OrderMode = 0xA6B3, OrderAction = 0xA6B0, PlaceOrder = 0xA6D1, CursorTarget = 0xD845, CursorX = 0xE1B4, CursorY = 0xE1B6, Cursor = 0xE1BC;
    public const int TargetChosen = 0xC7CC, Adjacent = 0xC762, SpotX = 0xC652, SpotY = 0xC67D, FleeBefore = 0xD6EA, OrderClock = 0xC867;
    public const int ThrowPotion = 0xC1CE, ThrowQuality = 0xC1F2, ThrowAtTargetOnly = 0xE1C8, CursorKeys = 0xD7FE, MoveMode = 0xC102, FollowsActive = 0x11B1;
    public const int SelectedFlags = 0x9064;

    /// <summary>+0x10 of record -1 (the bytes before combatant 0): the order command-mode-end reads and writes with nobody selected.</summary>
    public const int ActionBeforeRecords = 0x9BE5;
    public const int KeyMoved = 0xE079, PendingFlag = 0xD6F1, PendingMember = 0xD6ED, MarchingOrderAt = 0xC624, MarchingOrderLength = 0xC629;

    /// <summary>The cursor sprites (DS:E1BC holds the handle the words at these places keep) by what the rewrite calls them.</summary>
    private static readonly (OrderCursor Cursor, int Place)[] CursorPlaces =
    [
        (OrderCursor.Ground, 0xC28F), (OrderCursor.GroundBlocked, 0xC244), (OrderCursor.Target, 0xC242),
        (OrderCursor.TargetBlocked, 0xC2BD), (OrderCursor.Throw, 0xC4DB), (OrderCursor.ThrowBlocked, 0xC621),
    ];

    /// <summary>DS:E1BC holding none of the six handles: no cursor the rewrite names.</summary>
    private const OrderCursor UnknownCursor = (OrderCursor)(-1);

    private static int Word(byte[] m, int at) => m[at] | m[at + 1] << 8;

    private static void PutWord(byte[] m, int at, int v)
    {
        m[at] = (byte)v;
        m[at + 1] = (byte)(v >> 8);
    }

    private static OrderCursor? CursorOf(byte[] ds, int handle) => handle == 0 ? OrderCursor.None : CursorPlaces.Where(c => Word(ds, c.Place) == handle).Select(c => (OrderCursor?)c.Cursor).FirstOrDefault();

    private static int HandleOf(byte[] ds, OrderCursor cursor) => cursor == OrderCursor.None ? 0 : Word(ds, CursorPlaces.First(c => c.Cursor == cursor).Place);

    public static void Apply(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        TacticsOverlay.Set(t, "Selected", (int)(sbyte)ds[Selected]);
        TacticsOverlay.Set(t, "_orderMode", ds[OrderMode] != 0);
        TacticsOverlay.Set(t, "OrderAction", (int)(short)Word(ds, OrderAction));
        TacticsOverlay.Set(t, "PlaceOrder", (int)(short)Word(ds, PlaceOrder));
        TacticsOverlay.Set(t, "CursorTarget", (int)(sbyte)ds[CursorTarget]);
        TacticsOverlay.Set(t, "CursorPoint", new MapPoint((short)Word(ds, CursorX), (short)Word(ds, CursorY)));
        TacticsOverlay.Set(t, "Cursor", CursorOf(ds, Word(ds, Cursor)) ?? UnknownCursor);
        TacticsOverlay.Set(t, "_targetChosen", ds[TargetChosen] != 0);
        TacticsOverlay.Set(t, "_adjacent", ds[Adjacent] != 0);
        TacticsOverlay.Set(t, "OrderSpot", new MapPoint((short)Word(ds, SpotX), (short)Word(ds, SpotY)));
        TacticsOverlay.Set(t, "_fleeBefore", ds[FleeBefore] != 0);
        TacticsOverlay.Set(t, "_orderClock", (ushort)Word(ds, OrderClock));
        TacticsOverlay.Set(t, "_actionBeforeRecords", (int)(short)Word(ds, ActionBeforeRecords));
        TacticsOverlay.Set(t, "_throwPotion", (int)ds[ThrowPotion]);
        TacticsOverlay.Set(t, "_throwQuality", (int)ds[ThrowQuality]);
        TacticsOverlay.Set(t, "_throwAtTargetOnly", ds[ThrowAtTargetOnly] != 0);
        TacticsOverlay.Set(t, "CursorKeysOn", ds[CursorKeys] != 0);
        TacticsOverlay.Set(t, "MoveMode", (int)ds[MoveMode]);
        t.FollowsActive = ds[FollowsActive] != 0;
        t.KeyMoved = ds[KeyMoved] != 0;
        t.PendingOrderMember = ds[PendingFlag] != 0 ? (sbyte)ds[PendingMember] : -1;
        // the five order bytes as they are (single file reads the first past a length of 0)
        ds.AsSpan(MarchingOrderAt, t.MarchingOrderBytes.Length).CopyTo(t.MarchingOrderBytes);
        t.MarchingOrder.Clear();
        for (var k = 0; k < Math.Min((int)ds[MarchingOrderLength], 5); k++)
        {
            t.MarchingOrder.Add((sbyte)ds[MarchingOrderAt + k]);
        }
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            t.Battle[k].Selected = ds[SelectedFlags + k] != 0;
        }
    }

    public static void Extract(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        ds[Selected] = (byte)TacticsOverlay.Get<int>(t, "Selected");
        ds[OrderMode] = (byte)(TacticsOverlay.Get<bool>(t, "_orderMode") ? 1 : 0);
        PutWord(ds, OrderAction, TacticsOverlay.Get<int>(t, "OrderAction"));
        PutWord(ds, PlaceOrder, TacticsOverlay.Get<int>(t, "PlaceOrder"));
        ds[CursorTarget] = (byte)TacticsOverlay.Get<int>(t, "CursorTarget");
        var point = TacticsOverlay.Get<MapPoint>(t, "CursorPoint");
        PutWord(ds, CursorX, point.X);
        PutWord(ds, CursorY, point.Y);
        var cursor = TacticsOverlay.Get<OrderCursor>(t, "Cursor");
        // a handle that is none of the six sprites' (a mutated state) stays an unknown cursor until the rewrite sets one
        if (cursor != UnknownCursor && (CursorOf(ds, Word(ds, Cursor)) ?? UnknownCursor) != cursor)
        {
            PutWord(ds, Cursor, HandleOf(ds, cursor));
        }
        ds[TargetChosen] = (byte)(TacticsOverlay.Get<bool>(t, "_targetChosen") ? 1 : 0);
        ds[Adjacent] = (byte)(TacticsOverlay.Get<bool>(t, "_adjacent") ? 1 : 0);
        var spot = TacticsOverlay.Get<MapPoint>(t, "OrderSpot");
        PutWord(ds, SpotX, spot.X);
        PutWord(ds, SpotY, spot.Y);
        ds[FleeBefore] = (byte)(TacticsOverlay.Get<bool>(t, "_fleeBefore") ? 1 : 0);
        PutWord(ds, OrderClock, TacticsOverlay.Get<ushort>(t, "_orderClock"));
        PutWord(ds, ActionBeforeRecords, TacticsOverlay.Get<int>(t, "_actionBeforeRecords"));
        ds[ThrowPotion] = (byte)TacticsOverlay.Get<int>(t, "_throwPotion");
        ds[ThrowQuality] = (byte)TacticsOverlay.Get<int>(t, "_throwQuality");
        ds[ThrowAtTargetOnly] = (byte)(TacticsOverlay.Get<bool>(t, "_throwAtTargetOnly") ? 1 : 0);
        ds[CursorKeys] = (byte)(TacticsOverlay.Get<bool>(t, "CursorKeysOn") ? 1 : 0);
        ds[MoveMode] = (byte)TacticsOverlay.Get<int>(t, "MoveMode");
        ds[FollowsActive] = (byte)(t.FollowsActive ? 1 : 0);
        ds[KeyMoved] = (byte)(t.KeyMoved ? 1 : 0);
        ds[PendingFlag] = (byte)(t.PendingOrderMember != -1 ? 1 : 0);
        if (t.PendingOrderMember != -1)
        {
            ds[PendingMember] = (byte)t.PendingOrderMember;
        }
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            ds[SelectedFlags + k] = (byte)(t.Battle[k].Selected ? 1 : 0);
        }
        t.StoreMarchingOrder(ds.AsSpan(MarchingOrderAt, 6));
    }

    /// <summary>
    /// command-mode-end with nobody selected gives an order to record -1 (the bytes before combatant 0) when the cursor target is a combatant (>= 1) or a
    /// free spot (-2) and the order is not a place action nor refused (R014+0xBCB7..0xBD09, 0xBD60, 0xBF0E): the rewrite keeps no record -1.
    /// </summary>
    /// <summary>
    /// command-mode-end with nobody selected gives the order to record -1 (TacticalBattle.GiveToRecordBeforeCombatants): DS:17B0 = 0xFF there is
    /// not reproduced (the view and the keys then read record -1, the menu records: undefined memory), so the original's write is taken over.
    /// </summary>
    public static void TakeActiveMemberMinusOne(BattleCallsTests.Context c, CallJob job)
    {
        if ((sbyte)c.Input.Ds[Selected] == -1 && job.Orig.Changed.TryGetValue("6064", out var active) && active == 0xFF)
        {
            c.Outputs[0x17B0] = "active member (0xFF: not reproduced, undefined memory follows)";
            c.Actual[0x17B0] = 0xFF;
        }
    }

    /// <summary>A DS offset → what models it here, or null.</summary>
    public static string? Describe(int ds)
    {
        foreach (var (name, at, width) in new[]
        {
            ("selected member", Selected, 1), ("order mode", OrderMode, 1), ("order action", OrderAction, 2), ("place order", PlaceOrder, 2), ("cursor target", CursorTarget, 1),
            ("cursor x", CursorX, 2), ("cursor y", CursorY, 2), ("cursor sprite", Cursor, 2), ("target chosen", TargetChosen, 1), ("adjacent", Adjacent, 1),
            ("order spot x", SpotX, 2), ("order spot y", SpotY, 2), ("flee before", FleeBefore, 1), ("order clock", OrderClock, 2), ("throw potion", ThrowPotion, 1),
            ("throw quality", ThrowQuality, 1), ("throw at target only", ThrowAtTargetOnly, 1), ("cursor keys", CursorKeys, 1), ("move mode", MoveMode, 1),
            ("follows active", FollowsActive, 1), ("key moved", KeyMoved, 1), ("pending order flag", PendingFlag, 1), ("marching order length", MarchingOrderLength, 1),
        })
        {
            if (ds >= at && ds < at + width)
            {
                return name;
            }
        }
        if (ds >= SelectedFlags && ds < SelectedFlags + Combatant.PartySlots)
        {
            return $"selected flag[{ds - SelectedFlags}]";
        }
        return ds >= MarchingOrderAt && ds < MarchingOrderAt + 5 ? $"marching order[{ds - MarchingOrderAt}]" : null;
    }
}
