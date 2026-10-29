using System.Globalization;
using System.Text.Json.Nodes;
using Darklands.Platform;

namespace Darklands.Launcher;

/// <summary>The settings being edited on the launcher, what each shows, and what changed.</summary>
internal sealed class LauncherModel
{
    /// <summary>The game's own size; window sizes keep its shape.</summary>
    public const int BaseWidth = 720;
    public const int BaseHeight = 400;

    /// <summary>The sound choices offered, with their labels (the PC speaker is left out: it is silent here).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> SoundCards =
    [
        ("config", "As in the game's setup"),
        ("adlib", "AdLib"),
        ("sb", "Sound Blaster"),
        ("sbpro2", "Sound Blaster Pro 2"),
        ("mt32", "Roland MT-32"),
        ("cm32l", "Roland CM-32L"),
        ("none", "No sound"),
    ];

    private static readonly double[] Factors = [1, 1.5, 2, 2.5, 3, 4];

    private readonly LauncherContext _context;
    private readonly (string? GameFolder, (int W, int H) Window, bool Fullscreen, string? HdPack, string SoundCard, string? Roms, string? AudioPack, bool Banner, bool Intro, bool Always) _initial;
    private readonly Dictionary<string, PackStatus> _packs = new(StringComparer.Ordinal);

    /// <summary>The folder shown at the start without the player choosing it (given at the start or found), or null.</summary>
    private readonly string? _automatic;

    public LauncherModel(LauncherContext context, (int Width, int Height)? display)
    {
        _context = context;
        var s = context.Settings;
        Windows = WindowChoices(display, CurrentWindow(s));
        WindowIndex = Windows.IndexOf(CurrentWindow(s));
        Fullscreen = s.Fullscreen ?? false;
        HdPack = string.IsNullOrWhiteSpace(s.HdPack) ? null : s.HdPack;
        SoundCard = SoundCards.Any(c => c.Value == s.SoundCard) ? s.SoundCard! : "config";
        Mt32Roms = string.IsNullOrWhiteSpace(s.Mt32Roms) ? null : s.Mt32Roms;
        AudioPack = string.IsNullOrWhiteSpace(s.AudioPack) ? null : s.AudioPack;
        Banner = s.Banner ?? true;
        Intro = s.Intro ?? true;
        Always = s.Launcher ?? false;
        Found = context.FoundGameFolders();
        var fileFolder = string.IsNullOrWhiteSpace(s.GameFolder) ? null : s.GameFolder;
        // the folder the command line (or DARKLANDS_DIR) gives is the one the game would start with: shown first
        var given = string.IsNullOrWhiteSpace(context.GivenGameFolder) ? null : context.GivenGameFolder;
        SetGameFolder(given ?? fileFolder, given is null ? null : "given at the start");
        if (GameFolder is null && Found.Count > 0)
        {
            SetGameFolder(Found[0].Folder, Found[0].Source);
        }
        _initial = (fileFolder, Windows[WindowIndex], Fullscreen, HdPack, SoundCard, Mt32Roms, AudioPack, Banner, Intro, Always);
        _automatic = GameFolder;
    }

    public IReadOnlyList<(string Folder, string Source)> Found { get; }

    /// <summary>The game folder as the player named it (or as it was found), null: none.</summary>
    public string? GameFolder { get; private set; }

    /// <summary>The folder with DARKLAND.EXE in <see cref="GameFolder"/>, null when it holds none.</summary>
    public string? GameFolderChecked { get; private set; }

    /// <summary>Where the game folder came from ("GOG's registration"), or null.</summary>
    public string? GameFolderSource { get; private set; }

    public List<(int Width, int Height)> Windows { get; }

    public int WindowIndex { get; set; }

    public bool Fullscreen { get; set; }

    public string? HdPack { get; set; }

    public string SoundCard { get; set; }

    /// <summary>The sound pack's folder (replacement music and battle effects); null: none.</summary>
    public string? AudioPack { get; set; }

    /// <summary>The picture pack's (or the sound pack's) folder.</summary>
    public string? PackOf(PackKind kind) => kind == PackKind.Pictures ? HdPack : AudioPack;

    public void SetPack(PackKind kind, string? folder)
    {
        if (kind == PackKind.Pictures)
        {
            HdPack = folder;
        }
        else
        {
            AudioPack = folder;
        }
    }

    /// <summary>What a folder holds as a pack (looked at once per folder: it reads the disk).</summary>
    public PackStatus Pack(string folder)
    {
        if (!_packs.TryGetValue(folder, out var status))
        {
            status = _packs[folder] = Directory.Exists(folder)
                ? (_context.DescribePack ?? PackStatus.Count)(folder)
                : new PackStatus(0, 0, "The folder does not exist.");
        }
        return status;
    }

    /// <summary>Whether a folder holds a pack of that kind.</summary>
    public bool Holds(string folder, PackKind kind) => kind == PackKind.Pictures ? Pack(folder).Pictures > 0 : Pack(folder).Sounds > 0;

    /// <summary>None, then the packs of that kind found in the packs folders, then the one chosen when it is elsewhere.</summary>
    public IReadOnlyList<string?> PackChoices(PackKind kind)
    {
        var found = _context.FindPacks?.Invoke(GameFolderChecked) ?? [];
        var choices = new List<string?> { null };
        choices.AddRange(found.Where(f => Holds(f, kind)));
        if (PackOf(kind) is { } chosen && !choices.Contains(chosen))
        {
            choices.Add(chosen);
        }
        return choices;
    }

    /// <summary>The next (or previous) pack choice.</summary>
    public void CyclePack(PackKind kind, int step)
    {
        var choices = PackChoices(kind);
        var i = Math.Max(0, choices.ToList().IndexOf(PackOf(kind)));
        SetPack(kind, choices[(i + step + choices.Count) % choices.Count]);
    }

    /// <summary>A pack folder's name as shown ("None" for none).</summary>
    public static string PackLabel(string? folder) => folder is null ? "None" : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

    /// <summary>The Roland (MT-32 / CM-32L) ROM folder chosen; null: the default (MT32_ROMS next to the program).</summary>
    public string? Mt32Roms
    {
        get => _mt32Roms;
        set
        {
            _mt32Roms = value;
            _roms = null;
        }
    }

    private string? _mt32Roms;
    private RomStatus? _roms;

    public bool Banner { get; set; }

    public bool Intro { get; set; }

    /// <summary>This screen at every start.</summary>
    public bool Always { get; set; }

    public bool CanPlay => GameFolderChecked is not null;

    /// <summary>What the ROM folder holds; looked at once for each folder (it reads the disk).</summary>
    public RomStatus Roms => _roms ??= _context.DescribeRoms(Mt32Roms);

    public bool IsFixed(string key, out string by) => _context.FixedKeys.TryGetValue(key, out by!);

    public void SetGameFolder(string? folder, string? source = null)
    {
        GameFolder = folder;
        GameFolderChecked = folder is null ? null : _context.CheckGameFolder(folder);
        GameFolderSource = GameFolderChecked is null ? null : source;
    }

    /// <summary>The next (or previous) of the folders found, after the current one.</summary>
    public void CycleFoundFolder(int step)
    {
        if (Found.Count == 0)
        {
            return;
        }
        var i = Found.ToList().FindIndex(f => f.Folder == GameFolder);
        i = i < 0 ? 0 : (i + step + Found.Count) % Found.Count;
        SetGameFolder(Found[i].Folder, Found[i].Source);
    }

    public void CycleSoundCard(int step)
    {
        var i = SoundCards.ToList().FindIndex(c => c.Value == SoundCard);
        SoundCard = SoundCards[(i + step + SoundCards.Count) % SoundCards.Count].Value;
    }

    public void CycleWindow(int step) => WindowIndex = (WindowIndex + step + Windows.Count) % Windows.Count;

    public static string SizeLabel((int Width, int Height) size) => $"{size.Width} × {size.Height}";

    public string SoundCardLabel => SoundCards.First(c => c.Value == SoundCard).Label;

    /// <summary>The settings-file keys that changed, with their new values.</summary>
    /// <param name="playing">The player pressed Play: a folder they did not choose themselves (given at the start or found) is
    /// written only then; else (the launcher closed) it is left to the next start.</param>
    public IReadOnlyDictionary<string, JsonNode?> Changes(bool playing = true)
    {
        var changes = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        // a folder fixed by the command line goes into the file only when the file names none (the next start then finds it)
        if (GameFolder != _initial.GameFolder && GameFolderChecked is not null && (_initial.GameFolder is null || !IsFixed("gameFolder", out _))
            && (playing || GameFolder != _automatic))
        {
            changes["gameFolder"] = GameFolder!.Replace('\\', '/');
        }
        var window = Windows[WindowIndex];
        if (window != _initial.Window)
        {
            if (window.Width % BaseWidth == 0 && window.Height == window.Width / BaseWidth * BaseHeight)
            {
                changes["display.scale"] = window.Width / BaseWidth;
                changes["display.resolution"] = "";
            }
            else
            {
                changes["display.resolution"] = $"{window.Width}x{window.Height}";
            }
        }
        if (Fullscreen != _initial.Fullscreen)
        {
            changes["display.fullscreen"] = Fullscreen;
        }
        if (HdPack != _initial.HdPack)
        {
            changes["display.hdPack"] = HdPack?.Replace('\\', '/') ?? "";
        }
        if (SoundCard != _initial.SoundCard)
        {
            changes["sound.card"] = SoundCard;
        }
        if (Mt32Roms != _initial.Roms)
        {
            changes["sound.mt32Roms"] = Mt32Roms?.Replace('\\', '/') ?? "";
        }
        if (AudioPack != _initial.AudioPack)
        {
            changes["sound.audioPack"] = AudioPack?.Replace('\\', '/') ?? "";
        }
        if (Banner != _initial.Banner)
        {
            changes["startup.banner"] = Banner;
        }
        if (Intro != _initial.Intro)
        {
            changes["startup.intro"] = Intro;
        }
        if (Always != _initial.Always)
        {
            changes["startup.launcher"] = Always;
        }
        return changes;
    }

    /// <summary>The settings to run with: the file's, with the changes.</summary>
    public GameSettings Settings()
    {
        var s = _context.Settings;
        var window = Windows[WindowIndex];
        var exact = window.Width % BaseWidth == 0 && window.Height == window.Width / BaseWidth * BaseHeight;
        return new GameSettings
        {
            GameFolder = GameFolderChecked is not null ? GameFolder : s.GameFolder,
            SavesFolder = s.SavesFolder,
            Scale = exact ? window.Width / BaseWidth : null,
            Resolution = exact ? null : $"{window.Width}x{window.Height}",
            Fullscreen = Fullscreen,
            HdPack = HdPack,
            SoundCard = SoundCard,
            Mt32Roms = Mt32Roms,
            AudioPack = AudioPack,
            ConfigDrk = s.ConfigDrk,
            Banner = Banner,
            Intro = Intro,
            Launcher = Always,
            CommandLine = s.CommandLine,
            TextFont = s.TextFont,
            ScreenshotsFolder = s.ScreenshotsFolder,
            SdlLibrary = s.SdlLibrary,
            ConsoleEnabled = s.ConsoleEnabled,
            ConsoleFont = s.ConsoleFont,
            ConsoleFontSize = s.ConsoleFontSize,
        };
    }

    private static (int Width, int Height) CurrentWindow(GameSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.Resolution))
        {
            var parts = s.Resolution.Split('x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        var scale = s.Scale ?? 1;
        return (BaseWidth * scale, BaseHeight * scale);
    }

    /// <summary>The sizes that fit the screen (all of them when its size is unknown), and the current one.</summary>
    private static List<(int Width, int Height)> WindowChoices((int Width, int Height)? display, (int Width, int Height) current)
    {
        var sizes = Factors
            .Select(f => ((int)(BaseWidth * f), (int)(BaseHeight * f)))
            .Where(size => display is not { } d || (size.Item1 <= d.Width && size.Item2 <= d.Height))
            .ToList();
        if (sizes.Count == 0)
        {
            sizes.Add((BaseWidth, BaseHeight));
        }
        if (!sizes.Contains(current))
        {
            sizes.Add(current);
            sizes.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }
        return sizes;
    }
}

/// <summary>The two kinds of pack the launcher sets: pictures (display.hdPack) and sounds (sound.audioPack).</summary>
internal enum PackKind
{
    Pictures,
    Sounds,
}
