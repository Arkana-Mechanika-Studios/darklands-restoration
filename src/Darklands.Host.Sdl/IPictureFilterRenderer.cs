using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// The renderer's side of the picture filters (<see cref="PictureFilter"/>): it draws the game's picture on the window
/// through the chosen filter. Two implementations: pixel shaders on SDL3's GPU renderer (<see cref="SdlShaderPictureFilter"/>),
/// and render targets with blend modes on any renderer (<see cref="SdlPictureFilter"/>). Both must draw what
/// <see cref="Darklands.Rendering.PictureFilterReference"/> (the CPU reference) draws; the tests hold them to it.
/// A frame: <see cref="Begin"/>, the pictures (<see cref="DrawGame"/> for the game's own, the host's plain copies for an HD
/// pack's replacements), <see cref="Finish"/>.
/// </summary>
internal interface IPictureFilterRenderer : IDisposable
{
    PictureFilter Filter { get; set; }

    /// <summary>A slight curvature of the CRT's picture (display.crtCurvature); only where the renderer has the shaders.</summary>
    bool Curvature { get; set; }

    /// <summary>What draws: "shaders" or "render targets" (the log, the tests).</summary>
    string Kind { get; }

    /// <summary>
    /// A frame begins on the picture's rectangle (output pixels). <paramref name="layered"/>: an HD pack's replacements will be
    /// drawn between the game's pictures, so a mask over the whole picture goes over them too.
    /// </summary>
    void Begin(DisplayRectangle picture, bool layered);

    /// <summary>
    /// A game picture (<paramref name="texture"/>, the renderer's texture of <paramref name="width"/> x <paramref name="height"/>
    /// pixels) onto <paramref name="destination"/> (output pixels). <paramref name="blended"/>: it has transparent parts (an HD
    /// pack's overlay) and is laid over what is there; else it covers its destination.
    /// </summary>
    void DrawGame(IntPtr texture, int width, int height, DisplayRectangle destination, bool blended);

    /// <summary>The frame's last step over the picture's rectangle (the CRT's mask, over replacements too).</summary>
    void Finish(DisplayRectangle picture);
}
