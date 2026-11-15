using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher;

// The launcher screen: what a choice changes: the folder dialogs, dropped files, the packs, the game folder, the sound font, the ROMs. The state and the input are in LauncherScreen.cs.
internal sealed partial class LauncherScreen
{
    /// <summary>
    /// Where a dialog opens (the player's request, 2026-11-08): at the folder or file the row names when it exists, else in the
    /// program's own folder (<paramref name="programFolder"/>), where the packs, MT32_ROMS and SOUNDFONT folders are.
    /// </summary>
    internal static string DialogStart(string? current, string programFolder)
    {
        var named = CleanPath(current);
        return named.Length > 0 && (Directory.Exists(named) || File.Exists(named)) ? Path.GetFullPath(named) : programFolder;
    }

    /// <summary>
    /// The system's folder dialog: false when there is none (or it could not be shown: <see cref="_message"/> says why); else true,
    /// with the folder chosen or null when cancelled.
    /// </summary>
    private bool Pick(string title, string? current, out string? folder)
    {
        folder = null;
        if (_surface.PickFolder is not { } pick)
        {
            return false;
        }
        try
        {
            folder = pick(title, DialogStart(current, AppContext.BaseDirectory)) is { } picked ? CleanPath(picked) : null;
            return true;
        }
        catch (NotSupportedException e)
        {
            _message = e.Message;
            _messageColour = PaleRed;
            return false;
        }
    }

    /// <summary>
    /// A folder as typed, pasted or dropped, made usable: its first line, without the quotes and spaces around it, a file:// link
    /// as its path, and a leading ~ as the home folder (as a Linux shell reads it).
    /// </summary>
    internal static string CleanPath(string? text)
    {
        var line = (text ?? "").Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = line.Trim('"', '\'').Trim();
        if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            line = uri.LocalPath;
        }
        if (line == "~" || line.StartsWith("~/", StringComparison.Ordinal) || line.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0)
            {
                line = line.Length == 1 ? home : Path.Combine(home, line[2..]);
            }
        }
        return line;
    }

    private void Dropped(string path)
    {
        path = CleanPath(path);
        if (_editing)
        {
            StopEditing(apply: false);
        }
        if (_context.CheckGameFolder(path) is not null)
        {
            SetGameFolder(path);
        }
        else if (_context.DescribeRoms(path).Found)
        {
            SetRoms(path);
        }
        else if (File.Exists(path) && path.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase))
        {
            SetSoundFont(path);
        }
        else if (Directory.Exists(path) && (_model.Holds(path, PackKind.Pictures) || _model.Holds(path, PackKind.Sounds)))
        {
            // a folder of pictures, of sounds or of both: it becomes the pack of each kind it holds
            foreach (var kind in new[] { PackKind.Sounds, PackKind.Pictures }.Where(k => _model.Holds(path, k)))
            {
                SetPack(kind, path);
            }
        }
        else
        {
            _message = $"{path} holds neither Darklands (DARKLAND.EXE), Roland ROMs nor a pack (PNG pictures, OGG, WAV or MIDI sounds), and is not a SoundFont (.sf2).";
        }
    }

    private static Item PackItem(PackKind kind) => kind == PackKind.Pictures ? Item.HdPack : Item.AudioPack;

    /// <summary>A pack row: ←/→ (or a click on ‹ ›) go through None and the packs found; Enter or its Browse… asks the system for a folder.</summary>
    private void ChoosePack(PackKind kind, int step, bool cycle)
    {
        var what = kind == PackKind.Pictures ? "a picture (HD) pack: PNG files named after the game's pictures"
            : "a sound pack: OGG or WAV files named after the game's music and effects";
        if (!cycle && _surface.PickFolder is null)
        {
            (_message, _messageColour) = ("No folder dialog here: drop the pack's folder on this window, or ←/→ for the packs found.", PaleCream);
            return;
        }
        if (!cycle && Pick("Choose the folder of " + what, _model.PackOf(kind), out var chosen))
        {
            if (chosen is { } picked)
            {
                if (_model.Holds(picked, kind))
                {
                    SetPack(kind, picked);
                }
                else
                {
                    _message = $"{picked} holds no {(kind == PackKind.Pictures ? "PNG pictures" : "OGG or WAV sounds")}.";
                }
            }
            return;
        }
        _model.CyclePack(kind, step);
        PackMessage(kind);
    }

    private void SetPack(PackKind kind, string folder)
    {
        if (IsFixed(PackItem(kind), out var by))
        {
            _message = $"The {(kind == PackKind.Pictures ? "HD" : "audio")} pack is set by {by}.";
            return;
        }
        _model.SetPack(kind, folder);
        _focus = PackItem(kind);
        PackMessage(kind);
    }

    /// <summary>A pack chosen: its state shows on the line under its row (and in full in the details box), not as a message.</summary>
    private void PackMessage(PackKind kind) => _message = null;

    private string PackSummary(PackKind kind, PackStatus status) => kind == PackKind.Pictures
        ? $"{status.Pictures} picture{(status.Pictures == 1 ? "" : "s")}."
        : $"{status.Sounds} sound{(status.Sounds == 1 ? "" : "s")}{(_model.PackOf(kind) is { } f && File.Exists(Path.Combine(f, "audio.json")) ? ", audio.json read" : "")}.";

    private void SetGameFolder(string folder)
    {
        if (IsFixed(Item.GameFolder, out var by))
        {
            _message = $"The game folder is set by {by}.";
            return;
        }
        _model.SetGameFolder(folder);
        _focus = _model.CanPlay ? Item.Play : Item.GameFolder;
    }

    private void PickSoundFont()
    {
        if (_surface.PickFile is not { } pick)
        {
            _message = "Drop a SoundFont (.sf2 file) on this window, or put it in the SOUNDFONT folder next to the program.";
            return;
        }
        try
        {
            if (pick("Choose a General MIDI SoundFont (.sf2 file)", DialogStart(_model.SoundFont, AppContext.BaseDirectory)) is { } chosen)
            {
                SetSoundFont(CleanPath(chosen));
            }
        }
        catch (NotSupportedException e)
        {
            (_message, _messageColour) = (e.Message, PaleRed);
        }
    }

    private void SetSoundFont(string file)
    {
        if (IsFixed(Item.SoundFont, out var by))
        {
            _message = $"The SoundFont is set by {by}.";
            return;
        }
        if (!file.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            _message = $"{file} is not a SoundFont (.sf2 file).";
            return;
        }
        _model.SoundFont = file;
        _focus = Item.SoundFont;
        SoundFontMessage();
    }

    /// <summary>A SoundFont chosen: its state shows on the line under its row, not as a message.</summary>
    private void SoundFontMessage() => _message = null;

    /// <summary>The SoundFont row's value: the file's name; "(default)" when it comes from the SOUNDFONT folder; "none found".</summary>
    private string SoundFontShown()
    {
        if (_model.SoundFont is { } chosen)
        {
            return Path.GetFileName(chosen) + (File.Exists(chosen) ? "" : " (!)");
        }
        return _model.SoundFontFound is { } found ? Path.GetFileName(found) + " (default)" : "none found";
    }

    private void SetRoms(string folder)
    {
        if (IsFixed(Item.Roms, out var by))
        {
            _message = $"The ROM folder is set by {by}.";
            return;
        }
        var status = _context.DescribeRoms(folder);
        if (!status.Found)
        {
            _message = $"No MT-32 or CM-32L ROM files in {folder}.";
            return;
        }
        _model.Mt32Roms = folder;
        _focus = Item.Roms;
    }
}
