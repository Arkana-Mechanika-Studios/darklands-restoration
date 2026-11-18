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

// The battle screen: the mouse pointer. The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>The battle's pointer: the glove while running, the order hand while paused; arrows at the view edges.</summary>
    // orig: R014+0x33A2 (0E76:33A2) draft; mouse-cursor-plain R014+0x4780 (0E76:4780) draft
    // C: pointer-shape-set(hx, hy, sprite) gives each shape its hot spot: the driver draws the sprite at (mouse − hot spot). The glove and the
    // order hand have (0, 0); the arrows (3, 0) up, (7, 0) up-right, (7, 4) right, (7, 7) down-right, (3, 7) down, (0, 7) down-left, (0, 4) left,
    // (0, 0) up-left.
    private void DrawPointer(Canvas canvas)
    {
        _driverShape = PointerShape(_ui.Mouse.X, _ui.Mouse.Y);
        DrawPointer(canvas, _driverShape);
    }

    /// <summary>The shape the battle last gave the mouse driver (DS:A68F): the pass's (R014+0x33A2), the hand after the bar or the loot.</summary>
    // C: pointer-shape-set (root 047C:124F) keeps the shape in the driver: a pointer-show (root 047C:1293) with no shape set in between
    //   draws the last one (R021+0x0298 after a member's message, root 0000:10E7 in the pause box, R037+0x2B97 while a save is written)
    private (int Image, int HotX, int HotY) _driverShape = (BattleImages.NormalPointer, 0, 0);

    /// <summary>The driver's pointer shown in the battle's last shape (its pointer-show); the returned action puts the driver's state back.</summary>
    private Action ShowDriverPointer()
    {
        var (visible, shape, hotSpot) = (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot);
        ApplyDriverPointer();
        return () => (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot) = (visible, shape, hotSpot);
    }

    private void ApplyDriverPointer()
    {
        var (image, hotX, hotY) = _driverShape;
        (_ui.PointerVisible, _ui.PointerShape, _ui.PointerHotSpot) = (true, _assets.Images[image], (hotX, hotY));
    }

    /// <summary>The battle's hand (the glove running, the order hand paused), as the bar and the loot screen's end set it.</summary>
    private (int Image, int HotX, int HotY) Hand => (_battle!.PauseByte == 0 ? BattleImages.NormalPointer : BattleImages.OrderPointer, 0, 0);

    /// <summary>The shape the pass sets for the mouse at (x, y): the picture and its hot spot.</summary>
    private (int Image, int HotX, int HotY) PointerShape(int x, int y)
    {
        var arrow = x >= 60 ? EdgeArrow(x, y) : -1;
        if (arrow >= 0)
        {
            var (hx, hy) = ArrowHotSpots[arrow];
            return (BattleImages.ScrollArrows + arrow, hx, hy);
        }
        // C: root 0000:00A0 (and R022+0x0362 after the sheet): DS:A695 == 0 the normal pointer, any other value (0x11 too) the order pointer
        return (_battle!.PauseByte == 0 ? BattleImages.NormalPointer : BattleImages.OrderPointer, 0, 0);
    }

    /// <summary>Draws a pointer shape at the mouse (the driver draws the sprite at the mouse less the hot spot), keeping what it covers.</summary>
    private void DrawPointer(Canvas canvas, (int Image, int HotX, int HotY) shape)
    {
        if (!_ui.PointerInWindow)
        {
            // host enhancement (IInput.PointerInWindow): the host's pointer is outside the window, no hand drawn (nothing to take off later)
            _underPointer = null;
            return;
        }
        int x = _ui.Mouse.X - shape.HotX, y = _ui.Mouse.Y - shape.HotY;
        var sprite = _assets.Images[shape.Image];
        _underPointer = sprite is null ? null : canvas.Save(x, y, sprite.Width, sprite.Height);
        SpriteDrawing.Draw(canvas, sprite, x, y, new Clip(0, 0, Canvas.Width, Canvas.Height));
    }

    /// <summary>The hot spots of the scroll arrows (pointer-shape-set's first two arguments in R014+0x33A2), by <see cref="EdgeArrow"/>.</summary>
    private static readonly (int X, int Y)[] ArrowHotSpots = [(3, 0), (7, 0), (7, 4), (7, 7), (3, 7), (0, 7), (0, 4), (0, 0)];

    /// <summary>What the last pointer drawn covered: the original's pointer is the mouse driver's, never part of the picture.</summary>
    private SavedRegion? _underPointer;

    /// <summary>Takes the pointer drawn by the last frame off the canvas (pointer-hide).</summary>
    private void ErasePointer()
    {
        if (_underPointer is { } under)
        {
            _ui.Canvas.Restore(under);
            _underPointer = null;
        }
    }

    /// <summary>The scroll arrow of an edge zone (0 up … 7 up-left), −1 none.</summary>
    // orig: R014+0x33A2 (the zones: up 0x7D &lt; x &lt; 0xFF, y &lt; 8; the corners and sides as below; the zones do not overlap, so the order of the tests does not matter)
    private static int EdgeArrow(int x, int y)
    {
        if ((x is >= 60 and <= 125 && y < 8) || (x is >= 60 and < 68 && y <= 50))
        {
            return 7;
        }
        if ((x >= 255 && y < 8) || (x >= 311 && y <= 50))
        {
            return 1;
        }
        if ((x is >= 60 and < 68 && y >= 150) || (x <= 125 && y > 191))
        {
            return 5;
        }
        if ((x >= 311 && y >= 150) || (x >= 255 && y > 191))
        {
            return 3;
        }
        if (x is >= 126 and < 255 && y < 8)
        {
            return 0;
        }
        if (y > 191)
        {
            return 4;
        }
        if (x is >= 60 and < 68 && y is > 50 and < 150)
        {
            return 6;
        }
        if (x >= 311 && y is > 50 and < 150)
        {
            return 2;
        }
        return -1;
    }
}
