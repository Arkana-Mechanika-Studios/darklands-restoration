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
    private readonly (string? GameFolder, (int W, int H) Window, bool Fullscreen, string? HdPack, string SoundCard, string? Roms, bool Banner, bool Intro, bool Always) _initial;

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
        Banner = s.Banner ?? true;
        Intro = s.Intro ?? true;
        Always = s.Launcher ?? false;
        Found = context.FoundGameFolders();
        SetGameFolder(string.IsNullOrWhiteSpace(s.GameFolder) ? null : s.GameFolder);
        if (GameFolder is null && Found.Count > 0)
        {
            SetGameFolder(Found[0].Folder, Found[0].Source);
        }
        _initial = (string.IsNullOrWhiteSpace(s.GameFolder) ? null : s.GameFolder, Windows[WindowIndex], Fullscreen, HdPack, SoundCard, Mt32Roms, Banner, Intro, Always);
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
    public IReadOnlyDictionary<string, JsonNode?> Changes()
    {
        var changes = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (GameFolder != _initial.GameFolder && GameFolderChecked is not null)
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
