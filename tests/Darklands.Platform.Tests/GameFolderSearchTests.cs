namespace Darklands.Platform.Tests;

public sealed class GameFolderSearchTests
{
    private static readonly HashSet<string> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        @"D:\Games\Darklands\DARKLAND",
        @"C:\DARKLAND",
    };

    private static bool IsGame(string folder) => Games.Contains(folder);

    [Fact]
    public void AFolderOrItsDarklandSubfolderCounts()
    {
        Assert.Equal(@"C:\DARKLAND", GameFolderSearch.GameFolderIn(@"C:\DARKLAND", IsGame));
        Assert.Equal(Path.Combine(@"D:\Games\Darklands", "DARKLAND"), GameFolderSearch.GameFolderIn(@"D:\Games\Darklands", IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn(@"D:\Games", IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn("", IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn(null, IsGame));
    }

    [Fact]
    public void TheFirstCandidateHoldingTheGameWinsWithItsSource()
    {
        var found = GameFolderSearch.Find(
            [(@"E:\Nothing", "a"), (@"D:\Games\Darklands", "GOG"), (@"C:\DARKLAND", "usual")], IsGame);
        Assert.Equal((Path.Combine(@"D:\Games\Darklands", "DARKLAND"), "GOG"), found);
        Assert.Null(GameFolderSearch.Find([(@"E:\Nothing", "a")], IsGame));
    }

    [Fact]
    public void AnUnreadableCandidateIsSkipped()
    {
        bool Throwing(string folder) => folder.StartsWith(@"X:\", StringComparison.Ordinal) ? throw new UnauthorizedAccessException() : IsGame(folder);
        Assert.Equal((@"C:\DARKLAND", "usual"), GameFolderSearch.Find([(@"X:\Locked", "a"), (@"C:\DARKLAND", "usual")], Throwing));
    }

    [Fact]
    public void SteamLibrariesAreReadFromLibraryFolders()
    {
        const string Vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"D:\\Games\\Steam"
                }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\Games\Steam"], GameFolderSearch.SteamLibraries(Vdf));
    }

    [Fact]
    public void TheCandidatesStartWithTheProgramsFolderAndTheOneAbove()
    {
        var program = Path.Combine(Path.GetTempPath(), "dlrp", "release");
        var first = GameFolderSearch.Candidates(program).Take(2).ToList();
        Assert.Equal(program, first[0].Folder);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "dlrp"), first[1].Folder);
    }
}

public sealed class SettingsFileEditsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"darklands-settings-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void AnEmptyGameFolderOfTheTemplateIsFilledAndTheFileStillReads()
    {
        File.WriteAllText(_file, SettingsFile.Template);
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, @"D:\Games\Darklands\DARKLAND", replace: false, out _));
        var load = SettingsFile.Load(_file);
        Assert.Empty(load.Problems);
        Assert.Equal("D:/Games/Darklands/DARKLAND", load.Settings.GameFolder);
        Assert.Contains("// The folder with the original game", File.ReadAllText(_file));
    }

    [Fact]
    public void ANamedFolderIsKeptUnlessReplacing()
    {
        File.WriteAllText(_file, """{ "gameFolder": "C:/Old", "savesFolder": "" }""");
        Assert.False(SettingsFileEdits.TrySetGameFolder(_file, @"D:\New", replace: false, out _));
        Assert.Equal("C:/Old", SettingsFile.Load(_file).Settings.GameFolder);
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, @"D:\New", replace: true, out _));
        Assert.Equal("D:/New", SettingsFile.Load(_file).Settings.GameFolder);
    }

    [Fact]
    public void NullIsFilledACommentedKeyIsNot()
    {
        File.WriteAllText(_file, "{\n  // \"gameFolder\": \"\",\n  \"gameFolder\": null\n}");
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, "E:/Darklands", replace: false, out _));
        var text = File.ReadAllText(_file);
        Assert.Contains("// \"gameFolder\": \"\",", text);
        Assert.Equal("E:/Darklands", SettingsFile.Load(_file).Settings.GameFolder);
    }

    [Fact]
    public void AFileWithoutTheKeyIsLeftAlone()
    {
        File.WriteAllText(_file, """{ "savesFolder": "" }""");
        Assert.False(SettingsFileEdits.TrySetGameFolder(_file, "E:/Darklands", replace: false, out var message));
        Assert.Contains("no \"gameFolder\"", message);
    }
}
