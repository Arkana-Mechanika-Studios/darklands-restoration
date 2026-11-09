using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// Draws the game's 320x200 picture (and an HD pack's overlay of it) on the window through the chosen <see cref="PictureFilter"/>
/// with what every SDL renderer has: render targets, scale modes and blend modes. The fallback of
/// <see cref="SdlShaderPictureFilter"/> where SDL's GPU renderer or its shaders are missing (SDL's software renderer, a system
/// without Direct3D 12, ...): the same picture, without the CRT's curvature. Presentation only: the game's frame, its
/// screenshots and what the oracles compare are made before this and never read back from it.
/// </summary>
// Host feature, new code (docs/SETTINGS.md display.filter): the original drew to the VGA card, which the monitor showed.
internal sealed class SdlPictureFilter(IntPtr renderer) : IPictureFilterRenderer
{
    private readonly Dictionary<IntPtr, (IntPtr Target, int Width, int Height)> _targets = [];
    private readonly HashSet<IntPtr> _usedThisFrame = [];
    private IntPtr _darken, _brighten;
    private (int Width, int Height) _maskSize;
    private uint? _addTimesDst;

    public PictureFilter Filter { get; set; } = PictureFilter.Sharp;

    /// <summary>What is drawn for <see cref="Filter"/>: the shader-only filters as Even or CRT (PictureFilters.WithoutShader).</summary>
    private PictureFilter Drawn => PictureFilters.WithoutShader(Filter);

    /// <summary>Not drawn here (render targets and blend modes cannot bend the picture).</summary>
    public bool Curvature { get; set; }

    public string Kind => "render targets";

    public void Begin(DisplayRectangle picture, bool layered)
    {
    }

    public void DrawGame(IntPtr texture, int width, int height, DisplayRectangle place, bool blended)
    {
        var destination = ToSdl(place);
        if (Drawn == PictureFilter.Sharp)
        {
            SdlNative.RenderCopy(renderer, texture, destination);
            return;
        }
        // sharp bilinear: enlarged by whole numbers with hard pixels into a texture, then smoothly onto the window
        var (kx, ky) = PictureFilterMath.Prescale(destination.Width, destination.Height, width, height);
        var target = Target(texture, width * kx, height * ky);
        if (target == IntPtr.Zero)
        {
            SdlNative.RenderCopy(renderer, texture, destination);
            return;
        }
        var whole = new SdlNative.SdlRect { X = 0, Y = 0, Width = width * kx, Height = height * ky };
        SdlNative.SDL_SetRenderTarget(renderer, target);
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);
        SdlNative.SDL_RenderClear(renderer);
        SdlNative.SDL_GetTextureBlendMode(texture, out var blend);
        SdlNative.SDL_GetTextureScaleMode(texture, out var scale);
        SdlNative.SDL_SetTextureBlendMode(texture, SdlNative.BlendModeNone);   // its pixels as they are, alpha included
        SdlNative.SDL_SetTextureScaleMode(texture, SdlNative.ScaleModeNearest);
        SdlNative.RenderCopy(renderer, texture, whole);
        SdlNative.SDL_SetTextureBlendMode(texture, blend);
        SdlNative.SDL_SetTextureScaleMode(texture, scale);
        SdlNative.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        // the overlay's transparent pixels are 0 (black, alpha 0) and the others opaque, so the smooth step's blend of
        // the two is premultiplied: drawn as such, its edges get no dark fringe
        SdlNative.SDL_SetTextureBlendMode(target, blended ? SdlNative.BlendModeBlendPremultiplied : SdlNative.BlendModeNone);
        SdlNative.RenderCopy(renderer, target, destination);
    }

    /// <summary>The frame's last step over the picture's rectangle (the CRT's mask); then the textures not used this frame go.</summary>
    public void Finish(DisplayRectangle place)
    {
        var picture = ToSdl(place);
        if (Drawn == PictureFilter.Crt && picture.Width > 0 && picture.Height > 0)
        {
            Mask(picture.Width, picture.Height);
            if (_darken != IntPtr.Zero)
            {
                SdlNative.RenderCopy(renderer, _darken, picture);
                // the brightening part where the renderer can blend dst + dst x src (not the software renderer: a little darker there)
                if (_brighten != IntPtr.Zero)
                {
                    SdlNative.RenderCopy(renderer, _brighten, picture);
                }
            }
        }
        foreach (var source in _targets.Keys.Where(k => !_usedThisFrame.Contains(k)).ToList())
        {
            SdlNative.SDL_DestroyTexture(_targets[source].Target);
            _targets.Remove(source);
        }
        _usedThisFrame.Clear();
    }

    private static SdlNative.SdlRect ToSdl(DisplayRectangle r) => new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

    private IntPtr Target(IntPtr source, int width, int height)
    {
        _usedThisFrame.Add(source);
        if (_targets.TryGetValue(source, out var kept) && kept.Width == width && kept.Height == height)
        {
            return kept.Target;
        }
        if (kept.Target != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(kept.Target);
        }
        var target = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessTarget, width, height);
        if (target == IntPtr.Zero)
        {
            _targets.Remove(source);
            return IntPtr.Zero;
        }
        SdlNative.SDL_SetTextureScaleMode(target, SdlNative.ScaleModeLinear);
        _targets[source] = (target, width, height);
        return target;
    }

    /// <summary>dst + dst x src for colour, dst's alpha kept (SDL_BLENDMODE_INVALID where the renderer has no such blend).</summary>
    private uint AddTimesDst() => _addTimesDst ??= SdlNative.SDL_ComposeCustomBlendMode(
        SdlNative.BlendFactorDstColor, SdlNative.BlendFactorOne, SdlNative.BlendOperationAdd,
        SdlNative.BlendFactorZero, SdlNative.BlendFactorOne, SdlNative.BlendOperationAdd);

    /// <summary>The CRT mask's two textures at the picture's size in window pixels (made again when the size changes).</summary>
    private void Mask(int width, int height)
    {
        if (_maskSize == (width, height) && _darken != IntPtr.Zero)
        {
            return;
        }
        DestroyMask();
        _maskSize = (width, height);
        var (darken, brighten) = PictureFilterMath.CrtLayers(width, height, Screen.Height);
        _darken = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStatic, width, height);
        if (_darken == IntPtr.Zero)
        {
            return;
        }
        SdlNative.SDL_UpdateTexture(_darken, IntPtr.Zero, darken, width * 4);
        SdlNative.SDL_SetTextureBlendMode(_darken, SdlNative.BlendModeMod);
        SdlNative.SDL_SetTextureScaleMode(_darken, SdlNative.ScaleModeNearest);
        _brighten = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStatic, width, height);
        if (_brighten != IntPtr.Zero)
        {
            SdlNative.SDL_UpdateTexture(_brighten, IntPtr.Zero, brighten, width * 4);
            SdlNative.SDL_SetTextureScaleMode(_brighten, SdlNative.ScaleModeNearest);
            if (!SdlNative.SDL_SetTextureBlendMode(_brighten, AddTimesDst()))
            {
                SdlNative.SDL_DestroyTexture(_brighten);
                _brighten = IntPtr.Zero;
            }
        }
    }

    private void DestroyMask()
    {
        foreach (var texture in new[] { _darken, _brighten }.Where(t => t != IntPtr.Zero))
        {
            SdlNative.SDL_DestroyTexture(texture);
        }
        _darken = _brighten = IntPtr.Zero;
        _maskSize = default;
    }

    public void Dispose()
    {
        DestroyMask();
        foreach (var (target, _, _) in _targets.Values)
        {
            SdlNative.SDL_DestroyTexture(target);
        }
        _targets.Clear();
    }
}
