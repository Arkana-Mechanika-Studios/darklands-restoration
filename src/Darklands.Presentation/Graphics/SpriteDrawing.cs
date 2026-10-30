using Darklands.Content.Formats.Battles;
using Darklands.Platform;

namespace Darklands.Presentation.Graphics;

/// <summary>Draws row-run sprites (colour 0 transparent), clipped to a rectangle of the screen.</summary>
// orig: gfx-picture-draw of a sprite (root 047C, the graphics driver; asm)
public static class SpriteDrawing
{
    private static readonly Clip WholeScreen = new(0, 0, Canvas.Width, Canvas.Height);

    public static void Draw(Canvas canvas, RowRunSprite? sprite, int x, int y) => Draw(canvas, sprite, x, y, WholeScreen);

    public static void Draw(Canvas canvas, RowRunSprite? sprite, int x, int y, Clip clip) => Draw(canvas, sprite, x, y, clip, null);

    /// <summary>Draws a sprite through a colour table (the team-colour remaps of the effect sprites). The pixels drawn belong to the sprite's asset, remapped or not.</summary>
    public static void Draw(Canvas canvas, RowRunSprite? sprite, int x, int y, Clip clip, byte[]? remap)
    {
        if (sprite is null)
        {
            return;
        }
        var owner = ScreenOwners.IdOf(sprite.AssetName, sprite.Width, sprite.Height);
        for (var row = 0; row < sprite.RowCount; row++)
        {
            var sy = y + row;
            if (sy < clip.Top || sy >= clip.Bottom)
            {
                continue;
            }
            var (start, pixels) = sprite.Row(row);
            for (var i = 0; i < pixels.Length; i++)
            {
                var sx = x + start + i;
                if (pixels[i] != 0 && sx >= clip.Left && sx < clip.Right)
                {
                    canvas.Plot(sx, sy, remap is null ? pixels[i] : remap[pixels[i]], owner, ScreenTexel.Pack(start + i, row));
                }
            }
        }
    }
}

/// <summary>A screen rectangle drawing is limited to.</summary>
public readonly record struct Clip(int Left, int Top, int Right, int Bottom);
