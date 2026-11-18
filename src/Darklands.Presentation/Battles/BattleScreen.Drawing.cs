using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

// The battle screen: the battlefield frame: screen places, combatants, missiles, cursors, clouds, the depth order. The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>DS:E160: the view was drawn anew since the last fight-draw (which then forgets the logged sprites).</summary>
    private bool _viewRedrawn;

    /// <summary>A placement's view centre (effect-begin's DS:C866 / DS:C886) becomes the view's.</summary>
    private void ApplyViewOrigin()
    {
        if (_battle!.ViewOrigin is { } origin)
        {
            _view.CentreColumn = origin.Column;
            _view.CentreRow = origin.Row;
            _battle.ViewOrigin = null;
        }
    }

    /// <summary>fighter-screen-position's places: each combatant on the level shown (any status but empty) gets its sprite's top left.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft: scrx = foot x − DS:D505[set][facing], scry = DS:C8CA[set] − DS:D5D3[set] + y + 0x69
    public void SetUpScreenPlaces() => RecordScreenPlaces(tickDraw: true);

    /// <summary>
    /// The places as the last fight-draw of a tick left them (R014+0x10AA), for the tick's follow test (KeepInView); null: not drawn
    /// since the battle was set up or taken up (the record's own places then).
    /// </summary>
    // orig: the follow test R014+0x1065 reads +0x00 / +0x02 as the tick before drew them: a view moved between two ticks (a member on
    //   another level selected, floor-view-paint through R016+0x0A2C) is not seen by it until the next fight-draw. GitHub issue 3, lockstep
    //   castle-rotenburg-split-reselect: member 1 selected after U with the view on level 2, his places at clock 9283 still the frame's
    //   before the selection (0x99, 6), so the follow centres the view on him and his pending order mode opens in that tick.
    // DIVERGENCE: the C# draws every pass and keeps the record's places (+0x00 / +0x02, saved with the game) from the last frame drawn; the
    //   follow reads these tick places instead, the original's fight-draw (ticks, the battle's start and end) being the only writer of both.
    private readonly (int X, int Y)?[] _tickPlaces = new (int X, int Y)?[Combatant.Count];

    private void RecordScreenPlaces(bool tickDraw)
    {
        var tb = _battle!;
        // BUG: only the level SHOWN is drawn and has hot spots (R014+0x719E, R014+0x99F6): a member who is pending for order mode
        //   (DS:D6F1 / DS:D6ED) and has left the shown level opens order mode later, whenever he is drawn again (R014+0x0F58). Kept as
        //   the original (party-split audit row 22). Decision: never, display only: his order mode opens when he is drawn again.
        for (var i = 0; i < Combatant.Count; i++)
        {
            if (tb.Battle[i].Status != CharacterStatus.Empty && tb.Levels.IsShown(i) && SpriteBox(i) is { } box)
            {
                tb.Battle[i].ScreenPlace = (box.Left - BattlefieldView.Left, box.Top);
                if (tickDraw)
                {
                    _tickPlaces[i] = tb.Battle[i].ScreenPlace;
                }
            }
        }
    }

    /// <summary>Draws the whole screen.</summary>
    // orig: fight-draw's sprite lists: list C (depth-sorted sprites), which R014+0x7BBE fills
    // unmatched (a sprite queued with its depth and screen tile) and R014+0x8724 matched (at most 157), list A
    // R014+0x87E4 matched, list B R014+0x8862 matched; the dirty screen tiles re-queued by
    // R014+0x88E6 unmatched and R014+0x827A unmatched (walls, furniture, floor objects); list C
    // sorted by R014+0x97A6 unmatched (insertion sort by depth); the lists blitted by R014+0x74A2
    // unmatched through R014+0x9DAA, R014+0x9DDE, R014+0x9CE6 and the view
    // clip R014+0x8680 (all matched). Here the view, the piles, the areas, the fallen (list B, by y and x), then list C: the animated
    // wall decorations (R014+0x827A), the clouds' edges (R014+0x6FFC), the able combatants in view (R014+0x719E), the wall pieces of the
    // view slots this frame's sprites touch (R014+0x88E6, each within its slot's box: BattlefieldDirtyBoxes, BattlefieldView.WallPieces)
    // and the chosen member's and the target cursor (CursorEntries) sorted together by depth (DepthOrder), then the missiles, the
    // clouds' puffs and markers (R014+0x76AE) are drawn in that order every frame.
    // C: round railing (2026-11-07): the wall pieces sorted in with the sprites make the railing of DKSAVE99's mine ambush cover the
    //   figures behind it as in the original (vis10 V-mines, D-b797f7, ordercarry, D-e4fbd6: the 136..208 px of x 152..199 gone).
    // DIVERGENCE: the whole view is redrawn each frame (no dirty tiles): under the sorted list lies the full paint's order everywhere,
    // where the original puts a dirty tile's floor tiles and floor decorations back from lists A and B in their own order, and its
    // screen keeps what an earlier frame's boxes took (a box drawn on the screen itself is kept: KeepScreenLeftover), here only this frame's
    // boxes get the redrawn wall pieces; the boxes hold this frame's sprites only (not the last frame's logged entries, nor a body's
    // growth by its movement), and the 157-sprite limit is not kept for the
    // drawing of the sprites before the walls (see EffectField.Drawn for its effect on the random numbers); the missiles and the ground
    // cursor are drawn after the sorted list instead of in it (they mark their slots and take their places), the last frame's logged
    // entries (which draw nothing) are not in it, and a combatant's flash and damage number go with its body.
    // C: a screen pixel that a box drawn straight on the screen left (the save's comment) stays until a frame's box or a full paint takes
    //   it back (KeepScreenLeftover); the chosen member's cursor is the one the last fight-draw queued (QueuedChosen).
    // C: one visible effect of the dirty tiles (round fortpx 2026-11-07): vis10 visuals-fortress-cell, 1 px at (231,101) by the party
    //   (original 0xBA, C# 0xDA). Write watches on the pixel and on its back-buffer byte (D000:7F27) in the original: the view's full
    //   paint (floor-view-paint R017+0x0000, pass 8) draws a floor decoration's 0xBA there and then a later slot's floor tile's 0xDA
    //   over it, the same 0xDA the C# draws; from pass 8 on every fight-draw clears the dirty tile and re-blits it from its list
    //   (R014+0x74A2, the floor tile from the call before R014+0x753E, the decoration from the one before R014+0x75E7) in the other
    //   order, so the decoration's 0xBA stays on top. A consequence of the dirty-tile redraw, not of the game's state or its tables.
    // C: the same after the won battle's loot screen (round lootvis 2026-11-08): vis10 leftovers-msg, 13 px of the blood and debris at
    //   x 180..211, y 105..114 (original 88 / C0 / D8 / E2 / E3 / E5 / E6, C# A1 / 92 / E3 / E4 / E5). Write watches on (180,105),
    //   (187,105), (198,111), (204,111): floor-view-paint's copy of the page to the screen (driver 4A44:068F, after loot-pile-opened's
    //   R014+0xE3AB) writes A1 / 92 / E3 / E4, the C#'s values; then fight-draw (R014+0xE3B3) copies its boxes from the back page DS:0142
    //   to the screen DS:016A (R014+0x6FE2 -> R014+0x76AE, R014+0x7985 -> R014+0x9DDE; hooked: the box (0x78, 0x69) 0x30 x 0x10 of the
    //   view, screen x 180..227, y 105..120, holding all 13 px), and that box writes 88 / C0 / E2 / E5: the dirty tiles re-blitted from
    //   lists A and B.
    // C: R014+0x827A's roll-n(2) for each animated piece of the last full redraw (R017+0x0000's lists DS:E13D / DS:E178 / DS:E196) are
    // drawn by EffectField.Drawn at each fight-draw, the first one at the battle's start (R014+0x0307).
    // C: while paused the picture changes only at the paused ticks: fight-tick still runs every 8 timer callbacks (DS:07D1) and its
    // fight-draw re-rolls the clouds' puffs and edges and the animated pieces (vis10 session uncert-cloud-pause, Luebeck's gate with a
    // thrown Noxious Aroma, R014+0x3166 / 0x0F58 / 0x6818 / 0x827A and rand hooked: the key paused the battle at pass 533; at pass 539
    // (DS:07D1 = 8) fight-draw made 32 draws before the pieces' 8 and the screen changed; unchanged on the passes between).
    // TacticalBattle.Tick does the same (lockstep, the same steps: the same random sequence over the pause). The vis10 pictures of a pause pair only
    // to the timer's phase: vis10's C# host is not held to the original's callbacks while paused, so its paused ticks (and the jitter
    // they draw) fall on other frames (shots c and d of D-09fbc1, held14 here: 3,577 px, every pixel in the clouds' boxes).
    public void Draw()
    {
        var tb = _battle!;
        var canvas = _ui.Canvas;
        ApplyEnemyPalettes();
        ApplyViewOrigin();
        // orig: R014+0x105B..0x1062: once DS:CA29 is set (a pit or an alarm stopped the battle, also in the tick that set it) fight-tick
        // returns before fight-draw (R014+0x10AA), so fighter-screen-position (R014+0x99F6) no longer moves +0x00 / +0x02, which the saved game
        // keeps (lockstep session dungeon-mine-doormsg: the alarm of a riddle door)
        if (!tb.Stopped)
        {
            RecordScreenPlaces(tb.TickToDraw);
        }
        _view.Draw(canvas, tb.Levels.Shown, _tiles!);
        var clip = BattlefieldView.Area;
        foreach (var pile in tb.LootPiles)
        {
            if (pile.Level == tb.Levels.ShownIndex && pile.Visible)
            {
                // orig: R014+0x799E (14F7:118E) draft: x = −dx − ((y − sy0) − x) − sx0 + 0x88, y = (y − sy0) − dy + 0x69 (dx 10, dy 4).
                var (px, py) = _view.ToScreen(pile.Position.X, pile.Position.Y);
                SpriteDrawing.Draw(canvas, _assets.Images[BattleImages.LootPile], px - 10, py - 4, clip);
            }
        }
        DrawAreas(canvas);
        _hotSpots.Clear();
        var shown = Enumerable.Range(0, Combatant.Count)
            .Where(i => tb.Battle[i].Status is CharacterStatus.Able or CharacterStatus.Unconscious or CharacterStatus.Dead && tb.Levels.IsShown(i))
            .ToList();
        // list B: the fallen (and an able combatant out of view, which has no entry and draws nothing in the view)
        foreach (var i in shown.Where(i => !tb.Battle[i].IsAble || !CombatantInView(i)).OrderBy(i => tb.Battle[i].Position.Y).ThenBy(i => tb.Battle[i].Position.X))
        {
            DrawCombatant(canvas, i);
        }
        // list C: the animated wall decorations, the clouds' edges, then the able combatants in view (each body with its flash, cursors
        // and number), then the wall pieces of the view slots these sprites touch, sorted by depth
        _dirty.Clear();
        var listC = AnimatedPieceEntries();
        listC.AddRange(CloudEdgeEntries());
        foreach (var i in shown.Where(CombatantInView))
        {
            MarkCombatant(i);
        }
        foreach (var i in shown.Where(i => tb.Battle[i].IsAble && CombatantInView(i)))
        {
            var p = tb.Battle[i].Position;
            listC.Add(new DepthEntry(new DepthKey(p.X, p.Y, p.Y), i, 0, 0, 0, 0, default));
        }
        // the draw list goes on after the combatants' entries (EffectField.Drawn) with the ground cursor and the missiles, which are
        // logged for the next frame together with the damage numbers queued, then the wall pieces
        _listCount = tb.Effects.DrawListCount;
        _logged = Enumerable.Range(0, Combatant.Count).Count(tb.Effects.NumberQueued);
        DrawGroundCursorEntry();
        var missiles = PlaceMissiles();
        _loggedSprites = _logged;
        listC.AddRange(WallPieceEntries());
        QueueChosen();
        listC.AddRange(CursorEntries());
        DepthOrder.Sort(listC, e => e.Key);
        foreach (var e in listC)
        {
            DrawEntry(canvas, e);
        }
        DrawMissiles(canvas, missiles);
        DrawClouds(canvas);
        DrawGroundCursor(canvas);
        DrawScreenLeftover(canvas);
        OpenPendingOrderMode();
        DrawPanel(canvas);
        DrawMessage(canvas);
        // orig: notice-display root 0000:24BC: pointer-hide (root 047C:12C3) before the box and pointer-show (root 047C:1293) after the wait: no pointer while it waits
        if (_messageWaiting)
        {
            _underPointer = null;
        }
        else
        {
            DrawPointer(canvas);
        }
    }

    /// <summary>In the frame drawn after a combat tick: the member waiting for order mode (DS:D6F1 / DS:D6ED) enters it once it has a hot spot.</summary>
    // orig: fight-tick R014+0x0F58 (0E76:0F58) draft (its end, 0x10AF..0x1125, checked against the asm)
    // C: after the tick's fight-draw, if DS:D6F1 == 1 the hot spots (DS:D848[0..DS:E0A1 - 1]) are searched for the member DS:D6ED; with
    // C: one: chosen-member = it, the member cursor's place (DS:E09F / DS:E09D, drawn here from the member), command-mode-begin and
    // C: DS:D6F1 = 0; without one D6F1 stays for the next tick (the member's status is not tested)
    // C: the member cursor of the member put in order mode here (or by a key or a click) shows only from the next fight-draw, which
    //   queues it (R014+0x6818's member-cursor entry, DS:E09F / DS:E09D), a fight-tick later (every 8 timer callbacks while paused):
    //   QueuedChosen (vis10 session loadfix-order-cursor, DKSAVE99's mine ambush, key 1: command-mode-begin R014+0xBC0C at pass 593, the
    //   next fight-draw R014+0x74A2 at pass 601; the frames at passes 596 / 599 still with the old member's box, from 602 with the new).
    private void OpenPendingOrderMode()
    {
        var tb = _battle!;
        if (tb.TakeTickToDraw() && tb.PendingOrderMember >= 0 && HotSpot(tb.PendingOrderMember) is not null)
        {
            var member = tb.PendingOrderMember;
            tb.PendingOrderMember = -1;
            tb.EnterOrderMode(member);
        }
    }

    /// <summary>The draw list's count and the sprites logged in the frame being drawn; the last frame's logged sprites (DS:E176).</summary>
    private int _listCount, _logged, _loggedSprites;

    // C: fight-draw reads DS:E160 once at its start: after a floor-view-paint DS:E176 = 0 (and DS:E160 = 0)
    public int LoggedSprites
    {
        get
        {
            var logged = _viewRedrawn ? 0 : _loggedSprites;
            _viewRedrawn = false;
            return logged;
        }
    }

    /// <summary>A sprite after the combatants takes a place in the draw list while there is room; true when it has one.</summary>
    // orig: R014+0x7BD0 (13AE's `cmp word ptr [E162h], 9Dh / jl`), the ground cursor (R014+0x6B1C) and the missiles (R014+0x6D33) logged
    private bool TakeListPlace()
    {
        if (_listCount >= EffectField.DrawListLimit)
        {
            return false;
        }
        _listCount++;
        _logged++;
        return true;
    }

    /// <summary>The ground cursor's place in the draw list (it comes before the missiles).</summary>
    private void DrawGroundCursorEntry()
    {
        _groundCursorQueued = _battle!.CursorTarget is -2 or -3 && TakeListPlace();
        // orig: R014+0x6818 (14F7:0008): the cursor through R014+0x7BBE with arg 6 = −1, its map point DS:E1B4 / E1B6, its box
        // C: (DS:E1C2, DS:E1BE, DS:E1B8, DS:E1BA)
        if (_groundCursorQueued && GroundCursorShape() is var (_, x, y, width, height))
        {
            var p = _battle.CursorPoint;
            _dirty.MarkSprite(p.X, p.Y, _view.CameraX, _view.CameraY, EffectField.ClipToView(x - BattlefieldView.Left, y, width, height), -1, false);
        }
    }

    private bool _groundCursorQueued;

    /// <summary>One combatant: its frame at the foot point; hot spot, member cursor and damage number.</summary>
    // orig: fighter-screen-position R014+0x99F6 (14F7:31E6) draft; R014+0x719E (14F7:098E) draft (spec E §1.9, §3.3)
    private void DrawCombatant(Canvas canvas, int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        if (SpriteBox(i) is not { } box)
        {
            return;
        }
        var facing = c.SpriteFacing & 7;
        var animation = box.Animation;
        var (fx, fy, left, top) = (box.FootX, box.FootY, box.Left, box.Top);
        var frame = Math.Clamp(tb.ShownFrame(i), 0, Math.Max(0, animation.FrameCount - 1));
        if (c.IsAble && c.Pose == Pose.Walking && !c.Moving && c.Frame == 0)
        {
            frame = 0;
        }
        var clip = BattlefieldView.Area;
        SpriteDrawing.Draw(canvas, animation.Frame(frame, facing), left, top, clip);
        if (tb.Effects.FlashSpriteOf(i) is var flash and >= 0 && flash < BattleImages.Count)
        {
            // orig: R014+0x719E: the flash at (foot x - 5, top + height / 3 - 5).
            SpriteDrawing.Draw(canvas, _assets.Images[flash], fx - 5, top + animation.Height / 3 - 5, clip);
        }
        if (!c.IsAble)
        {
            return;
        }
        var h = animation.Height;
        var bottom = top + h;
        var mid = top + h / 2;
        var (l, t, r, b) = animation.SizeClass switch
        {
            0 => (fx - 4, bottom - 25, fx + 3, bottom),
            1 => (fx - 10, mid - 4, fx + 10, mid + 4),
            2 => (fx - 5, mid - 15, fx + 5, mid + 15),
            3 => (fx - 10, mid - 15, fx + 10, mid + 15),
            _ => (fx - 25, mid - 25, fx + 25, mid + 25),
        };
        // orig: R014+0x719E skips a combatant whose box (4 pixels round) is out of the view (R014+0x9C8C), and R014+0x7BBE
        // queues no sprite record, so no hot spot, when the box clipped to the view (R014+0x8680) is empty.
        // DIVERGENCE: the box is not grown by the last frame's movement (DS:85E8.., the dirty-rectangle deltas).
        // C: past the draw list's limit the body has no entry: no hot spot, no member or target cursor (DS:853E's bit) either
        var queued = tb.Effects.BodyQueued(i);
        if (queued && CombatantInView(i) && left - BattlefieldView.Left < 0x104 && left - BattlefieldView.Left + box.Width > 0 && top < Canvas.Height && top + animation.Height > 0)
        {
            _hotSpots.Add((i, l, t, r, b, c.Position.Y));
        }
        // the chosen member's cursor and the target cursor have their own entries in list C (CursorEntries); the other members marked
        // as selected (DS:9064) have none: fight-draw reads only DS:A6B2 (DS:9064 is read and written only by R014+0xBC0C / 0xBCA8)
        if (c.DamageNumber is > 0 and <= 42 && tb.Effects.NumberQueued(i))
        {
            var image = (i < Combatant.PartySlots ? BattleImages.PartyDamage : BattleImages.EnemyDamage) + c.DamageNumber;
            SpriteDrawing.Draw(canvas, _assets.Images[image], fx - 7, bottom - 0x27, clip);
        }
    }

    /// <summary>The ground cursor of order mode (a free or blocked spot, a throw's spot) and the group cursor.</summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft (the ground cursor: x = px - sx0 - (py - sy0) + 0x88 - w / 2, y = (py - sy0) - h + 0x69)
    private void DrawGroundCursor(Canvas canvas)
    {
        if (_battle!.CursorTarget is not (-2 or -3) || !_groundCursorQueued || GroundCursorShape() is not var (image, x, y, _, _))
        {
            return;
        }
        SpriteDrawing.Draw(canvas, _assets.Images[image], x, y, BattlefieldView.Area);
    }

    /// <summary>The ground or group cursor's image, top left (screen) and size; null when none is shown.</summary>
    private (int Image, int X, int Y, int Width, int Height)? GroundCursorShape()
    {
        var tb = _battle!;
        var (image, width, height) = tb.Cursor switch
        {
            OrderCursor.Ground => (BattleImages.GroundFree, 0x0B, 0x1A),
            OrderCursor.GroundBlocked => (BattleImages.GroundBlocked, 0x0B, 0x1A),
            OrderCursor.Throw => (199, 0x0B, 0x0F),
            OrderCursor.ThrowBlocked => (200, 0x0B, 0x0F),
            _ => (-1, 0, 0),
        };
        if (tb.MoveMode != 0 && tb.Selected == -1)
        {
            (image, width, height) = (tb.MoveMode == 1, tb.Cursor == OrderCursor.Ground) switch
            {
                (true, true) => (BattleImages.GroupFree, 0x16, 0x19),
                (true, false) => (BattleImages.GroupBlocked, 0x16, 0x19),
                (false, true) => (BattleImages.SingleFileFree, 0x0E, 0x1C),
                _ => (BattleImages.SingleFileBlocked, 0x0E, 0x1C),
            };
        }
        if (image < 0)
        {
            return null;
        }
        var (x, y) = _view.CursorTopLeft(tb.CursorPoint.X, tb.CursorPoint.Y, width, height);
        return (image, x, y, width, height);
    }

    /// <summary>Missiles in flight (and arrows stuck where they fell).</summary>
    // orig: fight-draw R014+0x6818 (14F7:0008) draft (spec E §2.1: sy = y − sy0; sx = x − sx0 − sy − dx + 0x88; y = sy + 0x55);
    // the screen positions by R014+0x9C14 (14F7:3404) unmatched
    private List<(int Image, int X, int Y, MapPoint Point)> PlaceMissiles()
    {
        var tb = _battle!;
        var placed = new List<(int Image, int X, int Y, MapPoint Point)>();
        foreach (var m in tb.MissilesDrawn)
        {
            if (m.Level != tb.Levels.ShownIndex || (m.Arrived && m.Sprite != 0))
            {
                continue;
            }
            var id = m.Sprite & 0x7F;
            var dir = m.Facing & 7;
            int image, dx;
            switch (id)
            {
                case 1:
                    (image, dx) = (BattleImages.Bullet, 1);
                    break;
                case 7 or 8:
                    (image, dx) = (BattleImages.BigProjectile, 10);
                    break;
                case 9:
                    (image, dx) = (BattleImages.BigProjectile2, 10);
                    break;
                default:
                    var group = id switch { 0 => m.Arrived ? 1 : 0, 6 => 2, 5 => 3, 3 => 4, 4 => 5, _ => 2 };
                    (image, dx) = (BattleImages.Missiles + 6 * dir + group, id == 4 ? 7 : 3);
                    break;
            }
            if (!TakeListPlace())
            {
                continue;
            }
            var p = m.Arrived ? new MapPoint(m.Position.X + 2, m.Position.Y + 2) : m.Position;
            var sy = p.Y - _view.CameraY;
            var sx = p.X - _view.CameraX - sy - dx + 0x88 + BattlefieldView.Left;
            placed.Add((image, sx, sy + 0x55, p));
            var sprite = _assets.Images[image];
            _dirty.MarkSprite(p.X, p.Y, _view.CameraX, _view.CameraY,
                EffectField.ClipToView(sx - BattlefieldView.Left, sy + 0x55, sprite?.Width ?? 0, sprite?.Height ?? 0), -1, false);
        }
        return placed;
    }

    private void DrawMissiles(Canvas canvas, List<(int Image, int X, int Y, MapPoint Point)> missiles)
    {
        foreach (var (image, x, y, _) in missiles)
        {
            SpriteDrawing.Draw(canvas, _assets.Images[image], x, y, BattlefieldView.Area);
        }
    }

    /// <summary>DS:1C4A: the 3-colour schemes of the cloud sprites by effect type; DS:1C5C: the 4-colour schemes of the markers.</summary>
    // orig: DS:1C4A, DS:1C5C (dumped from the 483.07 EXE)
    private static readonly byte[][] CloudSchemes =
        [[0xFB, 0xF5, 0xF6], [0xF3, 0xFB, 0xFE], [0xF7, 0xF8, 0xFA], [0xFC, 0xF6, 0xFE], [0xF4, 0xF5, 0xF3], [0xF6, 0xFE, 0xF4]];

    private static readonly byte[][] MarkerSchemes = [[0xFC, 0xFE, 0xFB, 0xF3], [0xF7, 0xFE, 0xF4, 0xFB], [0xFD, 0xFC, 0xF9, 0xF6]];

    /// <summary>The colours sprite buffers 1 (clouds) and 2 (markers) hold now: the schemes applied so far, composed (DS:D438, DS:D4B0).</summary>
    private readonly byte[] _cloudColours = Identity(), _markerColours = Identity();
    private int _cloudScheme, _markerScheme = 2;

    private static byte[] Identity() => [.. Enumerable.Range(0, 256).Select(v => (byte)v)];

    /// <summary>Recolours a sprite buffer from its current scheme to scheme s (the pixels themselves are translated, so the history counts).</summary>
    // orig: R014+0x10530 (1EC9:0000) / R014+0x105A0 (1EC9:0070) draft (inline asm xlat)
    // DIVERGENCE: a scheme index past the tables reads the bytes after them in the original; such schemes are ignored here.
    private static void Remap(byte[] colours, byte[][] schemes, ref int current, int s)
    {
        if (current == s || s < 0 || s >= schemes.Length || current < 0 || current >= schemes.Length)
        {
            return;
        }
        var table = Identity();
        for (var i = 0; i < schemes[s].Length; i++)
        {
            table[schemes[current][i]] = schemes[s][i];
        }
        for (var v = 0; v < 256; v++)
        {
            colours[v] = table[colours[v]];
        }
        current = s;
    }

    /// <summary>The slowing areas' puffs (list B, under the combatants).</summary>
    // orig: R014+0x917E (14F7:296E) draft
    private void DrawAreas(Canvas canvas)
    {
        var tb = _battle!;
        for (var k = 0; k < tb.Effects.AreaCount; k++)
        {
            if (tb.Effects.Areas[k].Level != tb.Levels.ShownIndex)
            {
                continue;
            }
            var n = tb.Effects.Areas[k].Size & 7;
            for (var j = 0; j < n * n && j < 16; j++)
            {
                var (x, y) = tb.Effects.AreaPuff(k, j);
                var sprite = tb.Effects.AreaSprite(k, j);
                if (x != 0 && y != 0 && sprite is >= 196 and <= 197)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[sprite], BattlefieldView.Left + x, y, BattlefieldView.Area);
                }
            }
        }
    }

    /// <summary>An entry of list C: combatant <see cref="Who"/>'s body (with its flash, cursors and number), or (Who −1) an edge of cloud
    /// <see cref="Cloud"/>: its sprite (0 none), its point and its box (relative to the view, clipped to it).</summary>
    private readonly record struct DepthEntry(DepthKey Key, int Who, int Cloud, int Sprite, int X, int Y, (int X, int Y, int W, int H) Box,
        RowRunSprite? Piece = null);

    /// <summary>The view slots this frame's sprites touch and their boxes (the slots whose wall pieces are sorted in among them).</summary>
    private readonly BattlefieldDirtyBoxes _dirty = new();

    /// <summary>The animated wall decorations of the view in list C: the north walls' (DS:E178), then the west walls' (DS:E196), each
    /// with the frame drawn by lot and drawn within its box; the floor's (DS:E13D, list B) only mark their slots.</summary>
    // orig: R014+0x827A (14F7:1A6A) checked against the asm (called by R014+0x6818 for the lists in that order): layer 0 at
    // C: (R136+0x45BE[s] + DS:C76E[n], R136+0x46D6[s] + DS:C789[n]) with box DS:C7DE[n] x DS:C7EE[n], keys (c + 4, r, r), type 0x7D01;
    // C: layer 1 at (R136+0x4B4A[s] + DS:C821[n], R136+0x46D6[s] + DS:C855[n]), box DS:C876[n] x DS:C887[n], keys (c + 1, r + 8, r + 8),
    // C: type 0x7D04; layer 2 at the floor point with box 0x30 x 0x10 (list B). The box is clipped to the view (R014+0x8680): an empty
    // C: one has no entry; otherwise it marks the 14 slots round its own (BattlefieldDirtyBoxes.MarkPiece).
    private List<DepthEntry> AnimatedPieceEntries()
    {
        var tb = _battle!;
        var list = new List<DepthEntry>();
        var tiles = _tiles!;
        foreach (var layer in new[] { 2, 0, 1 })
        {
            for (var slot = 0; slot < BattlefieldDirtyBoxes.Slots; slot++)
            {
                int row = slot / BattlefieldView.Columns, column = slot % BattlefieldView.Columns;
                if (_view.SlotCell(tb.Levels.Shown, row, column) is not var (x, y) || tb.Effects.PieceFrame(layer, x, y) is var frame && frame < 0)
                {
                    continue;
                }
                var cell = tb.Levels.Shown[x, y];
                var (fx, fy) = BattlefieldView.SlotFloor(row, column);
                var (decoration, px, py, w, h, key) = layer switch
                {
                    0 => (tiles.NorthWallDecorations[cell.NorthWallDecoration], fx + 16, fy - 25, 0, 0, new DepthKey(x * 32 + 4, y * 16, y * 16)),
                    1 => (tiles.WestWallDecorations[cell.WestWallDecoration], fx, fy - 25, 0, 0, new DepthKey(x * 32 + 1, y * 16 + 8, y * 16 + 8)),
                    _ => (tiles.FloorDecorations[tb.Levels.Shown.FloorDecoration(x, y)], fx, fy, 0x30, 0x10, default),
                };
                if (layer != 2)
                {
                    (px, py, w, h) = (px + (decoration?.OffsetX ?? 0), py + (decoration?.OffsetY ?? 0), decoration?.BoxWidth ?? 0, decoration?.BoxHeight ?? 0);
                }
                var box = EffectField.ClipToView(px, py, w, h);
                if (box.W == 0 || box.H == 0)
                {
                    continue;
                }
                _dirty.MarkPiece(slot, box);
                if (layer != 2)
                {
                    list.Add(new DepthEntry(key, -2, 0, 0, px, py, box, frame == 1 ? decoration?.SecondFrame : decoration?.Sprite));
                }
            }
        }
        return list;
    }

    /// <summary>A combatant's sprites in view mark the slots round it: its body (list C or B) and its damage number.</summary>
    // orig: R014+0x719E (14F7:098E) checked against the asm: the body through R014+0x7BBE with arg 6 = i, its map point, its box (the
    // C: screen place, DS:D55D[set][facing] x DS:D5D3[set]); the number (0x762A + n, logged) at (foot x − 7, bottom − 0x27), 0x10 x 6.
    // C: the body's box is not grown by the last frame's movement (DS:85E8.., see DrawCombatant), so a slot's box can be smaller (D-420a1f).
    private void MarkCombatant(int i)
    {
        var tb = _battle!;
        if (SpriteBox(i) is not { } box)
        {
            return;
        }
        var p = tb.Battle[i].Position;
        var size = box.Animation.SizeClass;
        var varies = _tiles!.SomeFurnitureVaries;
        _dirty.MarkSprite(p.X, p.Y, _view.CameraX, _view.CameraY,
            EffectField.ClipToView(box.Left - BattlefieldView.Left, box.Top, box.Width, box.Height), size, varies);
        if (tb.Effects.NumberQueued(i))
        {
            _dirty.MarkSprite(p.X, p.Y, _view.CameraX, _view.CameraY,
                EffectField.ClipToView(box.FootX - 7 - BattlefieldView.Left, box.Top + box.Height - 0x27, 0x10, 6), size, varies);
        }
    }

    /// <summary>One entry of list C: a combatant (with its flash and number), a cloud's edge, a cursor or a wall piece within its box.</summary>
    private void DrawEntry(Canvas canvas, DepthEntry e)
    {
        if (e.Who >= 0)
        {
            DrawCombatant(canvas, e.Who);
        }
        else if (e.Who == -1)
        {
            DrawCloudEdge(canvas, e);
        }
        else if (e.Who == -3)
        {
            SpriteDrawing.Draw(canvas, _assets.Images[e.Sprite], e.X, e.Y, BattlefieldView.Area);
        }
        else if (e.Piece is { } piece)
        {
            // within its box, and of that only what a box of this frame's sprites covers (the screen copy takes those boxes only)
            var b = e.Box;
            foreach (var r in _dirty.Rects)
            {
                int x0 = Math.Max(b.X, r.X), y0 = Math.Max(b.Y, r.Y), x1 = Math.Min(b.X + b.W, r.X + r.W), y1 = Math.Min(b.Y + b.H, r.Y + r.H);
                if (x0 < x1 && y0 < y1)
                {
                    SpriteDrawing.Draw(canvas, piece, BattlefieldView.Left + e.X, e.Y, new Clip(BattlefieldView.Left + x0, y0, BattlefieldView.Left + x1, y1));
                }
            }
        }
    }

    /// <summary>The chosen member (DS:A6B2) whose cursor the frames show: taken at each fight-draw (a combat tick's frame) and at the first.</summary>
    // orig: R014+0x6818 queues the member cursor at fight-draw only (R014+0x6D70: DS:A6B2 as it is then); between two fight-draws the
    //   screen is not redrawn, so a member chosen or left in between (command-mode-begin R014+0xBC0C, R014+0xBCA8) shows at the next
    //   fight-draw (the first one at the battle's start, R014+0x0307). Here every frame is drawn whole: the drawing keeps the member the
    //   last tick's frame queued (drawing only: the battle's state is untouched); tests: BattleUiTests
    private void QueueChosen()
    {
        var tb = _battle!;
        if (tb.TickToDraw || !_chosenQueued)
        {
            _queuedChosen = tb.Selected;
            _chosenQueued = true;
        }
    }

    private int _queuedChosen = -1;
    private bool _chosenQueued;

    /// <summary>The chosen member's cursor, then the target cursor, each in list C just behind its combatant's body (its keys + 1).</summary>
    // orig: R014+0x6818 (14F7:0008) checked against the asm, after the wall pieces (R014+0x6D6D): with DS:A6B2's body queued (DS:853E) and
    // C: room in the list, an entry of type 0x7530 with the body entry's keys + 1, sprite DS:C21B at DS:E09F / E09D (R014+0x6D70..0x6E0C);
    // C: then, when the ground cursor's sprite DS:E1BC is the target cursor DS:C242 or DS:C2BD, one for DS:D845's body (type 0x7918, its
    // C: keys + 1, at the ground cursor's place, R014+0x6E10..0x6EB4). Then the sort (R014+0x97A6).
    // C: the member is DS:A6B2 as the last fight-draw found it (QueuedChosen); the cursor's place is the body's of this frame (the original's
    // C: member cursor place DS:E09F / E09D is set at its own time, R014+0x0F58 / 0xB576 / 0xCD66 / 0xCED6).
    private List<DepthEntry> CursorEntries()
    {
        var tb = _battle!;
        var list = new List<DepthEntry>();
        void Add(int i, int image, Func<(int Left, int Top, int Width, int Height, int FootX, int FootY, Content.Formats.Battles.CombatAnimation Animation), (int X, int Y)> at)
        {
            if (i < 0 || !tb.Battle[i].IsAble || !tb.Effects.BodyQueued(i) || !CombatantInView(i) || SpriteBox(i) is not { } box
                || _listCount >= EffectField.DrawListLimit)
            {
                return;
            }
            _listCount++;
            var p = tb.Battle[i].Position;
            var (x, y) = at(box);
            list.Add(new DepthEntry(new DepthKey(p.X + 1, p.Y + 1, p.Y + 1), -3, i, image, x, y, default));
        }
        Add(_queuedChosen, BattleImages.MemberCursor, b => (b.FootX - 4, b.Top + b.Animation.Height / 2 - 13));
        if (tb.Selected >= 0 && tb.CursorTarget >= 0 && tb.Cursor is OrderCursor.Target or OrderCursor.TargetBlocked)
        {
            // orig: R014+0xCCD4 (mode 0): the target cursor DS:C242 (or DS:C2BD blocked) at the sprite's foot.
            var image = tb.Cursor == OrderCursor.Target ? BattleImages.TargetCursor : BattleImages.TargetBlocked;
            Add(tb.CursorTarget, image, b => (b.FootX - 5, b.Top + b.Animation.Height - b.Animation.CursorY - 5));
        }
        return list;
    }

    /// <summary>The wall pieces of every slot a sprite touched, in slot order, each within the slot's box, while the draw list has room.</summary>
    // orig: R014+0x88E6 (14F7:20D6) checked against the asm (see BattlefieldView.WallPieces), R014+0x8724's limit (DS:E162 < 0x9D); the
    // C: blit R014+0x74A2 draws an entry with a sprite within its box (entry +6..+0xC: here the slot's box)
    private List<DepthEntry> WallPieceEntries()
    {
        var tb = _battle!;
        var list = new List<DepthEntry>();
        for (var slot = 0; slot < BattlefieldDirtyBoxes.Slots; slot++)
        {
            if (_dirty.Box(slot) is not { } box)
            {
                continue;
            }
            foreach (var piece in _view.WallPieces(tb.Levels.Shown, _tiles!, tb.Field.Terrain, slot / BattlefieldView.Columns, slot % BattlefieldView.Columns))
            {
                if (_listCount >= EffectField.DrawListLimit)
                {
                    return list;
                }
                _listCount++;
                list.Add(new DepthEntry(piece.Key, -2, 0, 0, piece.X, piece.Y, box, piece.Sprite));
            }
        }
        return list;
    }

    /// <summary>The clouds' edges as list C entries: for each cloud and column c the south edge of the column, then the east edge of row c.</summary>
    // orig: R014+0x6FFC (14F7:07EC) checked against the asm, queued by R014+0x7BBE (14F7:13AE): the south edge at its map point
    // (R136+0x706A / 70EA) with height 0, box 0x12 x 0x22; the east edge (R136+0x6F6A / 6FEA) with height 2, box 8 x 0x27; each box moved
    // and grown by the cloud's growth (DS:8574, DS:859E, DS:855A, DS:8540) and clipped to the view (R014+0x8680); an empty box has no entry.
    // C: the loop takes every cloud, whatever level it is on (07EC tests no level); c runs to the size, here at most 4 (as the layout).
    private List<DepthEntry> CloudEdgeEntries()
    {
        var fx = _battle!.Effects;
        var list = new List<DepthEntry>();
        for (var k = 0; k < fx.CloudCount; k++)
        {
            var n = fx.Clouds[k].Size & 7;
            var (south, east) = fx.CloudEdgeSprites(k);
            var g = fx.CloudBoxGrowth(k);
            for (var c = 0; c < n && c < 4; c++)
            {
                var (sx, sy) = fx.CloudSouthEdge(k, c);
                if (sx != -999)
                {
                    var box = EffectField.ClipToView(sx + g.X, sy + g.Y, 0x12 + g.W, 0x22 + g.H);
                    if (box.W != 0 && box.H != 0)
                    {
                        var (mx, my) = fx.CloudSouthEdgeAt(k, c);
                        list.Add(new DepthEntry(new DepthKey(mx, my, my), -1, k, south, sx, sy, box));
                        _dirty.MarkSprite(mx, my, _view.CameraX, _view.CameraY, box, -1, false);
                    }
                }
                var (ex, ey) = fx.CloudEastEdge(k, c);
                if (ex != -999)
                {
                    var box = EffectField.ClipToView(ex + g.X, ey + g.Y, 8 + g.W, 0x27 + g.H);
                    if (box.W != 0 && box.H != 0)
                    {
                        var (mx, my) = fx.CloudEastEdgeAt(k, c);
                        list.Add(new DepthEntry(new DepthKey(mx, my - 2, my + 2), -1, k, east, ex, ey, box));
                        _dirty.MarkSprite(mx, my, _view.CameraX, _view.CameraY, box, -1, false);
                    }
                }
            }
        }
        return list;
    }

    /// <summary>A cloud's edge in its place of list C: the sprite buffer takes the cloud's colours, the edge is drawn within its box.</summary>
    // orig: R014+0x74A2 (14F7:0C92) draft: an entry of type 28000 + t (t below 16) recolours sprite buffer 1 (R014+0x10530) before the
    // sprite test, then a sprite other than 0 is drawn at its point within its box (R014+0x9DAA)
    private void DrawCloudEdge(Canvas canvas, DepthEntry e)
    {
        var kind = _battle!.Effects.CloudSlots[e.Cloud].Kind;
        if (kind < 16)
        {
            Remap(_cloudColours, CloudSchemes, ref _cloudScheme, kind);
        }
        if (e.Sprite != 0)
        {
            var b = e.Box;
            var clip = new Clip(BattlefieldView.Left + b.X, b.Y, BattlefieldView.Left + b.X + b.W, b.Y + b.H);
            SpriteDrawing.Draw(canvas, _assets.Images[e.Sprite], BattlefieldView.Left + e.X, e.Y, clip, _cloudColours);
        }
    }

    /// <summary>The clouds' puffs on top of the sprites, each cloud's within its box, in the colours of its type; the flash and blast markers.</summary>
    // orig: R014+0x76AE (14F7:0E9E) checked against the asm, R014+0x9542 (14F7:2D32; the markers) draft
    // C: entry e (one per cloud on the level shown with a puff, R014+0x8CB4) draws cloud e's puffs within entry e's box (its puffs' points
    // C: 25 wider and 8 higher, with the last frame's box): a puff's lower rows past the box are not drawn; a cloud that has ended draws
    // C: nothing and does not recolour. A puff is drawn when its x is not 0 (the y is not tested).
    private void DrawClouds(Canvas canvas)
    {
        var tb = _battle!;
        var clip = BattlefieldView.Area;
        for (var e = 0; e < tb.Effects.PuffBoxCount; e++)
        {
            var body = tb.Effects.CloudSprite(e);
            if (body < 0)
            {
                continue;
            }
            var cloud = tb.Effects.CloudSlots[e];
            Remap(_cloudColours, CloudSchemes, ref _cloudScheme, cloud.Kind);
            var b = tb.Effects.PuffBox(e);
            if (b.W == 0 || b.H == 0)
            {
                continue;
            }
            var box = new Clip(BattlefieldView.Left + b.X, b.Y, BattlefieldView.Left + b.X + b.W, b.Y + b.H);
            var n = cloud.Size & 7;
            for (var j = 0; j < n * n && j < 16; j++)
            {
                var (x, y) = tb.Effects.CloudPuff(e, j);
                if (x != 0)
                {
                    SpriteDrawing.Draw(canvas, _assets.Images[body], BattlefieldView.Left + x, y, box, _cloudColours);
                }
            }
        }
        for (var k = 0; k < tb.Effects.MarkerCount; k++)
        {
            var m = tb.Effects.Markers[k];
            // C: frame 4 reads DS:CEA4[4] (DS:CEAC, set to 0 by the loader): nothing is drawn.
            var frame = m.Frame & 7;
            if (m.Level != tb.Levels.ShownIndex || frame > 3)
            {
                continue;
            }
            Remap(_markerColours, MarkerSchemes, ref _markerScheme, m.Kind);
            var (x, y) = tb.Effects.MarkerPoint(k);
            SpriteDrawing.Draw(canvas, _assets.Images[109 + frame], BattlefieldView.Left + x, y, clip, _markerColours);
        }
    }

    /// <summary>The ENEMYPAL.DAT colour schemes of the enemies on the field, and their colour cycles.</summary>
    // orig: enemy-placement-entry R025+0x0000 (1EF8:0000) draft (spec E §1.8)
    // orig: R025+0x0958..0x098D after the palette upload (R025+0x094B): for each of the three schemes with +0x31 != 0xFF,
    //   root 047C:0362(+0x34, +0x33, +0x31, +0x32) then 047C:03DA(+0x34): the scheme's range taken from the DAC and started forwards
    // C: ENEMYPAL.DAT's 14 cycling schemes all name slot 3: an enemy's (or a later placement's) last cycling scheme is the one that turns,
    //   the earlier ones keep the colours the take found. A cycle is taken once per combatant placed (not again when the sheet or the loot
    //   screen gave the palette back: the slot goes on from where it was).
    private void ApplyEnemyPalettes()
    {
        var tb = _battle!;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            var c = tb.Battle[i];
            if (c.Status == CharacterStatus.Empty || !_paletteApplied.Add(i * 1000 + c.Appearance))
            {
                continue;
            }
            foreach (var p in c.Palettes)
            {
                if (p < 0 || p >= _assets.EnemyPalettes.Count)
                {
                    continue;
                }
                var first = _assets.EnemyPalettes.FirstColor(p);
                var colors = _assets.EnemyPalettes.Colors(p);
                for (var k = 0; k < 16; k++)
                {
                    _ui.Canvas.SetColor(first + k, new Content.Formats.Graphics.Rgb24(
                        BattlePalette.Expand(colors[3 * k]), BattlePalette.Expand(colors[3 * k + 1]), BattlePalette.Expand(colors[3 * k + 2])));
                }
            }
            if (!_cyclesTaken.Add(i * 1000 + c.Appearance))
            {
                continue;
            }
            foreach (var p in c.Palettes)
            {
                if (p < 0 || p >= _assets.EnemyPalettes.Count || _assets.EnemyPalettes.Cycle(p) is not { First: not 0xFF } cycle)
                {
                    continue;
                }
                _ui.TakeCycle(cycle.Slot, cycle.Reload, cycle.First, cycle.Last);
                _ui.Cycles.Start(cycle.Slot);
            }
        }
    }
}
