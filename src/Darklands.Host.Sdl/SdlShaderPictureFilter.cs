using System.Runtime.InteropServices;
using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Host.Sdl;

/// <summary>
/// The picture filters as a pixel shader on SDL3's GPU renderer (SDL 3.4: SDL_CreateGPURenderState): smooth pixels and the CRT
/// drawn in one pass from the game's 320x200 texture (Shaders/picture.frag.hlsl, the arithmetic of
/// <see cref="PictureFilterReference"/>), the CRT's optional curvature included. With an HD pack the CRT's mask must lie over the
/// replacements too: the frame is then drawn into a texture the size of the window and that texture onto the window through the
/// shader's mask. <see cref="Create"/> gives null where the renderer is not SDL's GPU renderer or takes none of the shader
/// formats the program brings; the host then draws with <see cref="SdlPictureFilter"/>.
/// </summary>
// Host feature, new code (docs/SETTINGS.md display.filter); the shader is this project's own (no third-party code).
internal sealed class SdlShaderPictureFilter : IPictureFilterRenderer
{
    /// <summary>The curvature's strength when display.crtCurvature is on: the corners pulled in by about 2 % of the picture.</summary>
    internal const float CurvatureAmount = 0.06f;

    private readonly IntPtr _renderer;
    private readonly IntPtr _device;
    private readonly IntPtr _shader;
    // one render state per use, so a draw's uniforms stay as they were set until SDL draws it
    private readonly Slot _game, _overlay, _mask;
    private IntPtr _canvas;
    private (int Width, int Height) _canvasSize;
    private bool _composing;

    private SdlShaderPictureFilter(IntPtr renderer, IntPtr device, IntPtr shader, IntPtr[] states)
    {
        _renderer = renderer;
        _device = device;
        _shader = shader;
        _game = new Slot(states[0]);
        _overlay = new Slot(states[1]);
        _mask = new Slot(states[2]);
    }

    public PictureFilter Filter { get; set; } = PictureFilter.Sharp;

    public bool Curvature { get; set; }

    public string Kind => "shaders";

    /// <summary>The GPU device's backend ("direct3d12", "vulkan" ...): which shader it draws with (DXIL or SPIR-V).</summary>
    public string Driver => Marshal.PtrToStringUTF8(SdlNative.SDL_GetGPUDeviceDriver(_device)) ?? "";

    /// <summary>The shader filters on this renderer, or null (not SDL's GPU renderer, no shader format it takes, a failure).</summary>
    public static SdlShaderPictureFilter? Create(IntPtr renderer, Action<string>? log = null)
    {
        var device = SdlNative.SDL_GetGPURendererDevice(renderer);
        if (device == IntPtr.Zero)
        {
            return null;
        }
        var formats = SdlNative.SDL_GetGPUShaderFormats(device);
        var (code, format) = (formats & SdlNative.GpuShaderFormatDxil) != 0 && Resource("picture.frag.dxil") is { } dxil ? (dxil, SdlNative.GpuShaderFormatDxil)
            : (formats & SdlNative.GpuShaderFormatSpirv) != 0 && Resource("picture.frag.spv") is { } spirv ? (spirv, SdlNative.GpuShaderFormatSpirv)
            : (null, 0u);
        if (code is null)
        {
            log?.Invoke($"Picture filters: the GPU renderer takes none of the program's shader formats (0x{formats:X}); drawn with render targets");
            return null;
        }
        var entry = Marshal.StringToCoTaskMemUTF8("main");
        var pinned = GCHandle.Alloc(code, GCHandleType.Pinned);
        IntPtr shader;
        try
        {
            var info = new SdlNative.GpuShaderCreateInfo
            {
                CodeSize = (nuint)code.Length,
                Code = pinned.AddrOfPinnedObject(),
                Entrypoint = entry,
                Format = format,
                Stage = SdlNative.GpuShaderStageFragment,
                NumSamplers = 1,
                NumUniformBuffers = 1,
            };
            shader = SdlNative.SDL_CreateGPUShader(device, ref info);
        }
        finally
        {
            pinned.Free();
            Marshal.FreeCoTaskMem(entry);
        }
        if (shader == IntPtr.Zero)
        {
            log?.Invoke("Picture filters: the shader could not be made (" + SdlNative.GetErrorText() + "); drawn with render targets");
            return null;
        }
        var states = new IntPtr[3];
        for (var i = 0; i < states.Length; i++)
        {
            var state = new SdlNative.GpuRenderStateCreateInfo { FragmentShader = shader };
            states[i] = SdlNative.SDL_CreateGPURenderState(renderer, ref state);
            if (states[i] == IntPtr.Zero)
            {
                log?.Invoke("Picture filters: no GPU render state (" + SdlNative.GetErrorText() + "); drawn with render targets");
                foreach (var made in states.Where(s => s != IntPtr.Zero))
                {
                    SdlNative.SDL_DestroyGPURenderState(made);
                }
                SdlNative.SDL_ReleaseGPUShader(device, shader);
                return null;
            }
        }
        var filter = new SdlShaderPictureFilter(renderer, device, shader, states);
        if (!filter.SelfTest())
        {
            // a pipeline the graphics driver refuses draws nothing, without an error to the program: a black picture
            log?.Invoke("Picture filters: the shader draws nothing here (" + SdlNative.GetErrorText() + "); drawn with render targets");
            filter.Dispose();
            return null;
        }
        return filter;
    }

    /// <summary>One colour drawn through the shader (smooth, 1 x 1 to 4 x 4) into a texture and read back: whether the shader draws.</summary>
    private bool SelfTest()
    {
        const uint colour = 0xFF4080C0;
        var source = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessStatic, 1, 1);
        var target = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessTarget, 4, 4);
        try
        {
            if (source == IntPtr.Zero || target == IntPtr.Zero)
            {
                return false;
            }
            SdlNative.SDL_UpdateTexture(source, IntPtr.Zero, new[] { colour }, 4);
            SdlNative.SDL_SetTextureBlendMode(source, SdlNative.BlendModeNone);
            SdlNative.SDL_SetRenderTarget(_renderer, target);
            SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
            SdlNative.SDL_RenderClear(_renderer);
            Set(_game, Uniforms(1, 1, 4, 4, 4, 4, 0, 0, 4, 4, mode: 0, crt: false));
            SdlNative.SDL_SetGPURenderState(_renderer, _game.State);
            SdlNative.RenderCopy(_renderer, source, new SdlNative.SdlRect { X = 0, Y = 0, Width = 4, Height = 4 });
            SdlNative.SDL_SetGPURenderState(_renderer, IntPtr.Zero);
            var pixels = new int[16];
            var read = SdlNative.ReadPixels(_renderer, pixels, 4, 4);
            SdlNative.SDL_SetRenderTarget(_renderer, IntPtr.Zero);
            return read && pixels.All(p => (uint)p == colour);
        }
        finally
        {
            SdlNative.SDL_SetRenderTarget(_renderer, IntPtr.Zero);
            foreach (var texture in new[] { source, target }.Where(t => t != IntPtr.Zero))
            {
                SdlNative.SDL_DestroyTexture(texture);
            }
        }
    }

    private static byte[]? Resource(string name)
    {
        using var stream = typeof(SdlShaderPictureFilter).Assembly.GetManifestResourceStream("Darklands.Host.Sdl.Shaders." + name);
        if (stream is null)
        {
            return null;
        }
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Begin(DisplayRectangle picture, bool layered)
    {
        _composing = Filter == PictureFilter.Crt && layered && picture.Width > 0 && picture.Height > 0 && Canvas() != IntPtr.Zero;
        if (_composing)
        {
            // the frame into the canvas, at the window's own coordinates; Finish puts it on the window through the mask
            SdlNative.SDL_SetRenderTarget(_renderer, _canvas);
            SdlNative.SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
            SdlNative.SDL_RenderClear(_renderer);
        }
    }

    public void DrawGame(IntPtr texture, int width, int height, DisplayRectangle place, bool blended)
    {
        var destination = new SdlNative.SdlRect { X = place.X, Y = place.Y, Width = place.Width, Height = place.Height };
        if (Filter == PictureFilter.Sharp || place.Width <= 0 || place.Height <= 0)
        {
            SdlNative.RenderCopy(_renderer, texture, destination);
            return;
        }
        var (kx, ky) = PictureFilterMath.Prescale(place.Width, place.Height, width, height);
        var crt = Filter == PictureFilter.Crt && !_composing;
        var slot = blended ? _overlay : _game;
        Set(slot, Uniforms(width, height, place.Width, place.Height, place.Width, place.Height, 0, 0, kx, ky, mode: 0, crt));
        SdlNative.SDL_SetGPURenderState(_renderer, slot.State);
        SdlNative.RenderCopy(_renderer, texture, destination);
        SdlNative.SDL_SetGPURenderState(_renderer, IntPtr.Zero);
    }

    public void Finish(DisplayRectangle picture)
    {
        if (!_composing)
        {
            return;
        }
        _composing = false;
        SdlNative.SDL_SetRenderTarget(_renderer, IntPtr.Zero);
        var area = new SdlNative.SdlRect { X = picture.X, Y = picture.Y, Width = picture.Width, Height = picture.Height };
        Set(_mask, Uniforms(Screen.Width, Screen.Height, picture.Width, picture.Height, _canvasSize.Width, _canvasSize.Height,
            picture.X, picture.Y, 1, 1, mode: 1, crt: true));
        SdlNative.SDL_SetGPURenderState(_renderer, _mask.State);
        SdlNative.RenderCopyPart(_renderer, _canvas, area, area);
        SdlNative.SDL_SetGPURenderState(_renderer, IntPtr.Zero);
    }

    /// <summary>The shader's uniform buffer (Shaders/picture.frag.hlsl, cbuffer Context): sizes, place, shape, crt.</summary>
    private float[] Uniforms(int sourceWidth, int sourceHeight, int width, int height, int mapWidth, int mapHeight, int x, int y,
        int kx, int ky, int mode, bool crt)
    {
        var depth = PictureFilterMath.ScanlineDepthFor(height / (double)sourceHeight);
        var gain = PictureFilterMath.CrtGain(depth);
        return
        [
            sourceWidth, sourceHeight, width, height,
            mapWidth, mapHeight, x, y,
            kx, ky, mode, crt ? 1 : 0,
            (float)depth, (float)PictureFilterMath.MaskStrength, (float)gain, crt && Curvature ? CurvatureAmount : 0,
        ];
    }

    /// <summary>A render state's uniforms, set again only when they change (after what is queued with the old ones is drawn).</summary>
    private void Set(Slot slot, float[] uniforms)
    {
        if (slot.Uniforms is { } old && old.AsSpan().SequenceEqual(uniforms))
        {
            return;
        }
        SdlNative.SDL_FlushRenderer(_renderer);
        SdlNative.SDL_SetGPURenderStateFragmentUniforms(slot.State, 0, uniforms, (uint)(uniforms.Length * sizeof(float)));
        slot.Uniforms = uniforms;
    }

    /// <summary>The texture the frame is drawn into for the mask (the window's size), or IntPtr.Zero.</summary>
    private IntPtr Canvas()
    {
        SdlNative.SDL_GetRenderOutputSize(_renderer, out var width, out var height);
        if (_canvas != IntPtr.Zero && _canvasSize == (width, height))
        {
            return _canvas;
        }
        if (_canvas != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(_canvas);
        }
        _canvas = SdlNative.SDL_CreateTexture(_renderer, SdlNative.PixelFormatArgb8888, SdlNative.TextureAccessTarget, width, height);
        _canvasSize = (width, height);
        if (_canvas != IntPtr.Zero)
        {
            SdlNative.SDL_SetTextureBlendMode(_canvas, SdlNative.BlendModeNone);
            SdlNative.SDL_SetTextureScaleMode(_canvas, SdlNative.ScaleModeLinear);
        }
        return _canvas;
    }

    public void Dispose()
    {
        foreach (var slot in new[] { _game, _overlay, _mask })
        {
            SdlNative.SDL_DestroyGPURenderState(slot.State);
        }
        SdlNative.SDL_ReleaseGPUShader(_device, _shader);
        if (_canvas != IntPtr.Zero)
        {
            SdlNative.SDL_DestroyTexture(_canvas);
        }
    }

    private sealed class Slot(IntPtr state)
    {
        public IntPtr State { get; } = state;

        public float[]? Uniforms { get; set; }
    }
}
