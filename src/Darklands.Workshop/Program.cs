using Avalonia;

namespace Darklands.Workshop;

/// <summary>
/// The Darklands Workshop (decision 13): <c>DarklandsWorkshop [--game &lt;game folder&gt;] [&lt;pack folder&gt;]</c>. Its first editor is
/// the translators' text editor (docs/LOCALISATION.md); the packs it writes live outside the program and the repository.
/// </summary>
// Not in the original: the 1.1 Workshop (W-0 + E-text).
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new WorkshopApp()).UsePlatformDetect().LogToTrace();
}
