using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Darklands.Host.Sdl;

/// <summary>
/// SDL3's file and folder dialogs (SDL_ShowFileDialogWithProperties), for the launcher's Change… and its SoundFont row: Windows' own
/// (the shell's IFileDialog, on a thread of SDL's), on Linux the XDG desktop portal's (KDE's, GNOME's ...) else zenity, macOS's.
/// The answer comes back while the program's events are pumped.
/// </summary>
internal static unsafe class SdlDialogs
{
    private static readonly object Lock = new();
    private static int _generation;
    private static (int Generation, bool Done, string? Path, string? Error) _result;

    /// <summary>
    /// The folder (<paramref name="folders"/>) or file chosen, or null when the player cancelled (or the program closes meanwhile:
    /// <paramref name="whileOpen"/>, run over and over on this thread while the dialog is open, returns false then). It must pump
    /// the window's events: SDL delivers the portal's answer and Windows' messages to the owner through them. Throws
    /// <see cref="NotSupportedException"/> with SDL's reason when no dialog could be shown.
    /// </summary>
    public static string? Pick(IntPtr window, string title, string? start, bool folders, Func<bool> whileOpen)
    {
        int generation;
        lock (Lock)
        {
            generation = ++_generation;
            _result = (generation, false, null, null);
        }
        var properties = SdlNative.SDL_CreateProperties();
        try
        {
            SdlNative.SDL_SetPointerProperty(properties, "SDL.filedialog.window", window);
            SdlNative.SDL_SetStringProperty(properties, "SDL.filedialog.title", title);
            if (start is { Length: > 0 })
            {
                SdlNative.SDL_SetStringProperty(properties, "SDL.filedialog.location", Location(start));
            }
            SdlNative.SDL_SetBooleanProperty(properties, "SDL.filedialog.many", false);
            // SDL copies what it needs before it returns
            SdlNative.SDL_ShowFileDialogWithProperties(folders ? SdlNative.FileDialogOpenFolder : SdlNative.FileDialogOpenFile,
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, void>)&Chosen, generation, properties);
        }
        finally
        {
            SdlNative.SDL_DestroyProperties(properties);
        }
        while (true)
        {
            lock (Lock)
            {
                if (_result.Done)
                {
                    break;
                }
            }
            if (!whileOpen())
            {
                return null; // the program closes: its answer is not waited for
            }
            Thread.Sleep(10);
        }
        lock (Lock)
        {
            if (_result.Error is { } error)
            {
                throw new NotSupportedException(error);
            }
            return _result.Path;
        }
    }

    /// <summary>
    /// <paramref name="start"/> as SDL's dialogs read a start location: SDL splits it at its last separator into a folder and a file
    /// name to preselect, so an existing folder gets a separator at its end (it opens inside it); a file opens its folder with it chosen.
    /// </summary>
    internal static string Location(string start) =>
        Directory.Exists(start) ? start.TrimEnd('/', '\\') +Path.DirectorySeparatorChar : start;

    // SDL_DialogFileCallback(void *userdata, const char * const *filelist, int filter): a NULL list: an error; an empty one: cancelled
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Chosen(IntPtr userdata, IntPtr filelist, int filter)
    {
        lock (Lock)
        {
            if ((int)userdata != _result.Generation)
            {
                return;
            }
            if (filelist == IntPtr.Zero)
            {
                // SDL's error is kept per thread: read on the thread that reports it
                var error = SdlNative.Text(SdlNative.SDL_GetError()) ?? "";
                _result = (_result.Generation, true, null, error.Length > 0 ? error : "the dialog failed");
                return;
            }
            var first = Marshal.ReadIntPtr(filelist);
            _result = (_result.Generation, true, first == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(first), null);
        }
    }
}
