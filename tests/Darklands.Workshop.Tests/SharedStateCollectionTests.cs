namespace Darklands.Workshop.Tests;

/// <summary>
/// The Workshop's process-wide settings (the game program's folder, the Workshop's own folder, the samples' folder, the launch hook and
/// the readers of a pack's pictures) are set by tests to folders and stand-ins of their own: every test class that sets or reads one
/// is in the Workshop's collection, where its tests run one at a time, so none reads another's while it runs (a parallel class
/// reading WorkshopApp.GameProgramFolder once saw another test's temporary folder, on Windows CI).
/// </summary>
public sealed class SharedStateCollectionTests
{
    private static readonly string[] Shared =
    [
        "WorkshopApp.GameProgramFolder", "WorkshopApp.GameProgram", "WorkshopApp.SamplesFolder", "WorkshopApp.IsSample",
        "WorkshopSettings.Folder", "StartProcessForTests", "new WorkshopWindow(", "FontPack.ReadImage", "FontPack.LoadScalable",
        "PackCapitals.ReadImage", "TextPreview.DrawScalable",
    ];

    [Fact]
    public void EveryTestClassThatTouchesTheWorkshopsSharedSettingsIsInTheWorkshopsCollection()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var tests = Path.Combine(dir!.FullName, "tests", "Darklands.Workshop.Tests");
        var outside = Directory.EnumerateFiles(tests, "*.cs")
            .Where(f => Path.GetFileName(f) != nameof(SharedStateCollectionTests) + ".cs")
            .Where(f => File.ReadAllText(f) is var text && Shared.Any(s => text.Contains(s, StringComparison.Ordinal))
                && !text.Contains("[Collection(WorkshopWindowTests.Collection)]", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(outside);
    }
}
