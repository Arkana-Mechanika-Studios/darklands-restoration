using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// Draws the game's 320x200 picture (and an HD pack's overlay of it) on the window through the chosen <see cref="PictureFilter"/>,
/// with what SDL2's renderer has: render targets, scale modes and blend modes. Presentation only: the game's frame, its
/// screenshots and what the oracles compare are made before this and never read back from it. The SDL2 implementation of
/// <see cref="IPictureFilterRenderer"/>, kept until the host moves to SDL3, whose renderer takes pixel shaders.
/// </summary>
// Host feature, new code (docs/SETTINGS.md display.filter): the original drew to the VGA card, which the monitor showed.
internal sealed class SdlPictureFilter(IntPtr renderer) : IPictureFilterRenderer
{
    private readonly Dictionary<IntPtr, (IntPtr Target, int Width, int Height)> _targets = [];
    private readonly HashSet<IntPtr> _usedThisFrame = [];
    private IntPtr _darken, _brighten;
    private (int Width, int Height) _maskSize;
    private int _premultiplied = -1, _addTimesDst = -1;
    private bool? _targetsSupported;

    public PictureFilter Filter { get; set; } = PictureFilter.Sharp;

    /// <summary>
    /// A game picture (<paramref name="texture"/>, <paramref name="width"/> x <paramref name="height"/>) onto
    /// <paramref name="destination"/>. <paramref name="blended"/>: it has transparent parts (an HD pack's overlay) and is laid
    /// over what is there; else it covers its destination.
    /// </summary>
    public void DrawGame(IntPtr texture, int width, int height, DisplayRectangle place, bool blended)
    {
        var destination = new SdlNative.SdlRect { X = place.X, Y = place.Y, Width = place.Width, Height = place.Height };
        if (Filter == PictureFilter.Sharp || !TargetsSupported())
        {
            SdlNative.SDL_RenderCopyTo(renderer, texture, IntPtr.Zero, ref destination);
            return;
        }
        // sharp bilinear: enlarged by whole numbers with hard pixels into a texture, then smoothly onto the window
        var (kx, ky) = PictureFilterMath.Prescale(destination.Width, destination.Height, width, height);
        var target = Target(texture, width * kx, height * ky);
        if (target == IntPtr.Zero)
        {
            SdlNative.SDL_RenderCopyTo(renderer, texture, IntPtr.Zero, ref destination);
            return;
        }
        var whole = new SdlNative.SdlRect { X = 0, Y = 0, Width = width * kx, Height = height * ky };
        SdlNative.SDL_SetRenderTarget(renderer, target);
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 0);
        SdlNative.SDL_RenderClear(renderer);
        SdlNative.SDL_SetTextureBlendMode(texture, SdlNative.BlendModeNone);   // its pixels as they are, alpha included
        SdlNative.SDL_RenderCopyTo(renderer, texture, IntPtr.Zero, ref whole);
        SdlNative.SDL_SetTextureBlendMode(texture, SdlNative.BlendModeBlend);
        SdlNative.SDL_SetRenderTarget(renderer, IntPtr.Zero);
        SdlNative.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
        // the overlay's transparent pixels are 0 (black, alpha 0) and the others opaque, so the smooth step's blend of
        // the two is premultiplied: drawn as such, its edges get no dark fringe
        if (!blended)
        {
            SdlNative.SDL_SetTextureBlendMode(target, SdlNative.BlendModeNone);
        }
        else if (SdlNative.SDL_SetTextureBlendMode(target, Premultiplied()) != 0)
        {
            SdlNative.SDL_SetTextureBlendMode(target, SdlNative.BlendModeBlend);
        }
        SdlNative.SDL_RenderCopyTo(renderer, target, IntPtr.Zero, ref destination);
    }

    /// <summary>The frame's last step over the picture's rectangle (the CRT's mask); then the textures not used this frame go.</summary>
    public void Finish(DisplayRectangle place)
    {
        var picture = new SdlNative.SdlRect { X = place.X, Y = place.Y, Width = place.Width, Height = place.Height };
        if (Filter == PictureFilter.Crt && picture.Width > 0 && picture.Height > 0)
        {
            Mask(picture.Width, picture.Height);
            if (_darken != IntPtr.Zero)
            {
                SdlNative.SDL_RenderCopyTo(renderer, _darken, IntPtr.Zero, ref picture);
                // the brightening part where the renderer can blend dst + dst x src (not the software renderer: a little darker there)
                if (_brighten != IntPtr.Zero)
                {
                    SdlNative.SDL_RenderCopyTo(renderer, _brighten, IntPtr.Zero, ref picture);
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

    private bool TargetsSupported() => _targetsSupported ??= SdlNative.SDL_RenderTargetSupported(renderer) != 0;

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
        var target = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessTarget, width, height);
        if (target == IntPtr.Zero)
        {
            _targets.Remove(source);
            return IntPtr.Zero;
        }
        SdlNative.SDL_SetTextureScaleMode(target, SdlNative.ScaleModeLinear);
        _targets[source] = (target, width, height);
        return target;
    }

    private int Premultiplied() => _premultiplied >= 0 ? _premultiplied : _premultiplied = SdlNative.SDL_ComposeCustomBlendMode(
        SdlNative.BlendFactorOne, SdlNative.BlendFactorOneMinusSrcAlpha, SdlNative.BlendOperationAdd,
        SdlNative.BlendFactorOne, SdlNative.BlendFactorOneMinusSrcAlpha, SdlNative.BlendOperationAdd);

    /// <summary>dst + dst x src for colour, dst's alpha kept.</summary>
    private int AddTimesDst() => _addTimesDst >= 0 ? _addTimesDst : _addTimesDst = SdlNative.SDL_ComposeCustomBlendMode(
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
        _darken = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessStatic, width, height);
        if (_darken == IntPtr.Zero)
        {
            return;
        }
        SdlNative.SDL_UpdateTexture(_darken, IntPtr.Zero, darken, width * 4);
        SdlNative.SDL_SetTextureBlendMode(_darken, SdlNative.BlendModeMod);
        SdlNative.SDL_SetTextureScaleMode(_darken, SdlNative.ScaleModeNearest);
        _brighten = SdlNative.SDL_CreateTexture(renderer, SdlNative.PixelFormatArgb8888, (int)SdlNative.TextureAccessStatic, width, height);
        if (_brighten != IntPtr.Zero)
        {
            SdlNative.SDL_UpdateTexture(_brighten, IntPtr.Zero, brighten, width * 4);
            SdlNative.SDL_SetTextureScaleMode(_brighten, SdlNative.ScaleModeNearest);
            if (SdlNative.SDL_SetTextureBlendMode(_brighten, AddTimesDst()) != 0)
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
