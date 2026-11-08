using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// The renderer's side of the picture filters (<see cref="PictureFilter"/>): it draws the game's picture on the window
/// through the chosen filter. The host calls it the same way whatever draws: today SDL2's renderer with render targets
/// and blend modes (<see cref="SdlPictureFilter"/>); written once more as pixel shaders for SDL3's renderer. The looks
/// both must give are <see cref="Darklands.Rendering.PictureFilterReference"/>'s (the CPU reference).
/// </summary>
internal interface IPictureFilterRenderer : IDisposable
{
    PictureFilter Filter { get; set; }

    /// <summary>
    /// A game picture (<paramref name="texture"/>, the renderer's texture of <paramref name="width"/> x <paramref name="height"/>
    /// pixels) onto <paramref name="destination"/> (output pixels). <paramref name="blended"/>: it has transparent parts (an HD
    /// pack's overlay) and is laid over what is there; else it covers its destination.
    /// </summary>
    void DrawGame(IntPtr texture, int width, int height, DisplayRectangle destination, bool blended);

    /// <summary>The frame's last step over the whole picture's rectangle (the CRT's mask, over replacements too).</summary>
    void Finish(DisplayRectangle picture);
}
