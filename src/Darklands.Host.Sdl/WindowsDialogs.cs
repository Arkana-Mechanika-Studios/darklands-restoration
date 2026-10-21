using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Darklands.Host.Sdl;

/// <summary>Windows' own folder picker, for the launcher's Change… (SDL2 has none; SDL3's would serve every system).</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDialogs
{
    /// <summary>
    /// The folder picker over <paramref name="owner"/> (a window handle, or zero); null when the player cancels.
    /// <paramref name="whileOpen"/> runs over and over on this thread meanwhile: it must handle the owner's window messages,
    /// which the dialog sends it (else both wait for each other).
    /// </summary>
    public static string? PickFolder(string title, IntPtr owner, Action whileOpen)
    {
        string? chosen = null;
        // the shell's dialogs want a single-threaded apartment
        var thread = new Thread(() => chosen = PickFolderHere(title, owner));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        while (!thread.Join(15))
        {
            whileOpen();
        }
        return chosen;
    }

    private static string? PickFolderHere(string title, IntPtr owner)
    {
        var dialog = (IFileDialog)new FileOpenDialog();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
            dialog.SetTitle(title);
            if (dialog.Show(owner) != 0)
            {
                return null; // cancelled (or failed)
            }
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(FileSystemPath, out var path);
                try
                {
                    return Marshal.PtrToStringUni(path);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(path);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    private const uint PickFolders = 0x20;
    private const uint ForceFileSystem = 0x40;
    private const uint PathMustExist = 0x800;
    private const uint FileSystemPath = 0x80058000;

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog;

    // IFileDialog's methods in vtable order (IModalWindow.Show first); only those used are given real signatures
    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr owner);

        void SetFileTypes(uint count, IntPtr filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IntPtr item);

        void SetFolder(IntPtr item);

        void GetFolder(out IntPtr item);

        void GetCurrentSelection(out IntPtr item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName(out IntPtr name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint kind, out IntPtr name);
    }
}
