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

public sealed class SettingsFileSetTests
{
    private static readonly System.Text.Json.Nodes.JsonNode True = System.Text.Json.Nodes.JsonValue.Create(true);

    private static string Set(string text, params (string Key, System.Text.Json.Nodes.JsonNode? Value)[] values) =>
        SettingsFileEdits.Set(text, values.Select(v => KeyValuePair.Create(v.Key, v.Value)));

    [Fact]
    public void EveryKeyOfTheTemplateIsChangedInPlaceAndTheCommentsStay()
    {
        var text = Set(SettingsFile.Template,
            ("gameFolder", "D:/Games/Darklands"),
            ("display.fullscreen", True),
            ("display.resolution", "1440x800"),
            ("sound.card", "mt32"),
            ("startup.banner", System.Text.Json.Nodes.JsonValue.Create(false)));
        var load = SettingsFile.Parse(text);
        Assert.Empty(load.Problems);
        Assert.Equal("D:/Games/Darklands", load.Settings.GameFolder);
        Assert.True(load.Settings.Fullscreen);
        Assert.Equal("1440x800", load.Settings.Resolution);
        Assert.Equal("mt32", load.Settings.SoundCard);
        Assert.False(load.Settings.Banner);
        Assert.Equal(SettingsFile.Template.Split('\n').Length, text.Split('\n').Length);
        Assert.Contains("// \"configDrk\": { \"card\": 2", text);
    }

    [Fact]
    public void AMissingKeyIsAddedToItsSectionAndAMissingSectionIsCreated()
    {
        var text = Set("{\n  // mine\n  \"display\": {\n    \"scale\": 2\n  }\n}",
            ("display.fullscreen", True),
            ("sound.card", "adlib"));
        var load = SettingsFile.Parse(text);
        Assert.Empty(load.Problems);
        Assert.Equal(2, load.Settings.Scale);
        Assert.True(load.Settings.Fullscreen);
        Assert.Equal("adlib", load.Settings.SoundCard);
        Assert.Contains("    \"fullscreen\": true", text);
        Assert.Contains("// mine", text);
    }

    [Fact]
    public void AnEmptyFileObjectTakesItsFirstKey()
    {
        var load = SettingsFile.Parse(Set("{}", ("startup.intro", System.Text.Json.Nodes.JsonValue.Create(false))));
        Assert.Empty(load.Problems);
        Assert.False(load.Settings.Intro);
    }

    [Fact]
    public void ACommentedKeyAndArraysAreSteppedOver()
    {
        var text = Set("{\n  \"startup\": {\n    // \"banner\": false,\n    \"commandLine\": [\"-Q\", \"}\"],\n    \"banner\": true\n  }\n}",
            ("startup.banner", System.Text.Json.Nodes.JsonValue.Create(false)));
        Assert.Contains("// \"banner\": false,", text);
        Assert.False(SettingsFile.Parse(text).Settings.Banner);
    }

    [Fact]
    public void TextThatIsNotAnObjectIsRefused()
    {
        Assert.Throws<FormatException>(() => Set("[1, 2]", ("gameFolder", "x")));
    }
}

public sealed class CommandLineOverridesTests
{
    [Fact]
    public void TheCommandLineAndTheEnvironmentFixTheirKeys()
    {
        var cli = new CommandLineOptions { Fullscreen = true, Resolution = "1440x800", NoIntro = true };
        var fixedKeys = cli.Overrides(name => name == "DARKLANDS_DIR" ? "D:/Darklands" : null);
        Assert.Equal("--fullscreen", fixedKeys["display.fullscreen"]);
        Assert.Equal("--resolution", fixedKeys["display.scale"]);
        Assert.Equal("--resolution", fixedKeys["display.resolution"]);
        Assert.Equal("--no-intro", fixedKeys["startup.intro"]);
        Assert.Equal("DARKLANDS_DIR", fixedKeys["gameFolder"]);
        Assert.False(fixedKeys.ContainsKey("startup.banner"));
        Assert.False(fixedKeys.ContainsKey("sound.card"));
    }
}
