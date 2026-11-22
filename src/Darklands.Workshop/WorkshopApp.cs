using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Darklands.Workshop;

/// <summary>The Workshop's application: the Fluent theme in the Workshop's colours, its one window.</summary>
internal sealed class WorkshopApp : Application
{
    /// <summary>
    /// The game program's folder: in a release the Workshop is in the <c>Workshop</c> folder inside the game's, so the one above;
    /// in a developer's build or the tests, its own when DarklandsRP is beside it (Darklands.Platform.PackLayout.GameProgramFolder).
    /// Its packs folder is where the Workshop lists and makes packs; its DarklandsRP is the program "Play in game" starts.
    /// </summary>
    public static string GameProgramFolder { get; internal set; } = Darklands.Platform.PackLayout.GameProgramFolder(AppContext.BaseDirectory);

    /// <summary>The game's program (DarklandsRP.exe on Windows, DarklandsRP elsewhere) in <see cref="GameProgramFolder"/>.</summary>
    public static string GameProgram => Path.Combine(GameProgramFolder, OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP");

    /// <summary>
    /// The sample translation packs that come with the Workshop (its <c>examples/sample-languages</c> folder: short excerpts of the opening,
    /// docs/language-samples), listed on the start page to open or play.
    /// </summary>
    public static string SamplesFolder { get; internal set; } = Path.Combine(AppContext.BaseDirectory, "examples", "sample-languages");

    /// <summary>Is <paramref name="folder"/> one of the samples (inside <see cref="SamplesFolder"/>)?</summary>
    public static bool IsSample(string folder) =>
        Path.GetFullPath(folder).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(SamplesFolder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static (string Path, DateTime Written, string? Version)? _gameVersion;

    /// <summary>
    /// The version of the game's program (<see cref="GameProgram"/>), as it says it ("1.0.0"), null when it is not there or cannot
    /// say. On Windows the program's file version; elsewhere (no such resource) what <c>DarklandsRP --version</c> prints, which every
    /// release since 0.9 answers ("Darklands Restoration Project 1.0.0"). Read again when the program changes.
    /// </summary>
    public static string? GameProgramVersion()
    {
        var program = GameProgram;
        if (!File.Exists(program))
        {
            return null;
        }
        var written = File.GetLastWriteTimeUtc(program);
        if (_gameVersion is { } known && known.Path == program && known.Written == written)
        {
            return known.Version;
        }
        string? version = null;
        try
        {
            version = Darklands.Workshop.Text.WorkshopVersion.Clean(System.Diagnostics.FileVersionInfo.GetVersionInfo(program).ProductVersion);
            if (version.Length == 0)
            {
                var start = new System.Diagnostics.ProcessStartInfo(program, "--version") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var run = System.Diagnostics.Process.Start(start);
                var said = run?.StandardOutput.ReadLineAsync();
                version = said is not null && said.Wait(3000) && said.Result is { } line && line.LastIndexOf(' ') is var space and >= 0
                    ? Darklands.Workshop.Text.WorkshopVersion.Clean(line[(space + 1)..])
                    : null;
                if (run is { HasExited: false })
                {
                    run.Kill();
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // a program that cannot say its version: it is tried as it is
        }
        version = version is { Length: > 0 } ? version : null;
        _gameVersion = (program, written, version);
        return version;
    }

    /// <summary>The plain words for an older game beside the Workshop (WorkshopVersion.OlderGameWords), null when the game will do.</summary>
    public static string? OlderGame() =>
        GameProgramVersion() is { } game && Darklands.Workshop.Text.WorkshopVersion.IsOlderGame(game) ? Darklands.Workshop.Text.WorkshopVersion.OlderGameWords(game) : null;

    public override void Initialize()
    {
        // a pack's fonts are read as the game's hosts read them (font packs, docs/LOCALISATION.md)
        Darklands.Presentation.FontPack.ReadImage = Darklands.Rendering.PngCodec.ReadAlpha;
        // a pack's own decorated capitals (capitals/*.png), as the game reads them
        Darklands.Presentation.Graphics.PackCapitals.ReadImage = path => { var image = Darklands.Rendering.PngCodec.Read(path); return (image.Width, image.Height, image.Argb); };
        Darklands.Presentation.FontPack.LoadScalable = json => (Darklands.Rendering.TtfFont.Load(json, out var why), why);
        // a TrueType font's text in the preview: drawn again at a higher resolution, by the game's own text layer
        Darklands.Workshop.Text.TextPreview.DrawScalable = (screen, scale) =>
            new Darklands.Rendering.TextLayer().Item(screen, new Darklands.Platform.DisplayRectangle(0, 0, Darklands.Platform.Screen.Width * scale, Darklands.Platform.Screen.Height * scale))?.Texture.Argb;
        Styles.Add(new FluentTheme());
        // the text editor's look (AvaloniaEdit: the translation box)
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Darklands.Workshop/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        WorkshopTheme.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new WorkshopWindow(WorkshopSettings.Load(), desktop.Args ?? []);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
