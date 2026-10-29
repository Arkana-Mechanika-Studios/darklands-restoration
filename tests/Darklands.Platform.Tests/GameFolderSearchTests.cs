namespace Darklands.Platform.Tests;

public sealed class GameFolderSearchTests
{
    // Folders rooted on the running system: on Windows the drives themselves; on Linux "D:\Games" would be a relative name
    // with backslashes in it, so the same tree stands under /d.
    private static string On(char drive, params string[] parts) =>
        Path.Combine([OperatingSystem.IsWindows() ? drive + @":\" : "/" + char.ToLowerInvariant(drive) + "/", .. parts]);

    private static readonly HashSet<string> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        On('D', "Games", "Darklands", "DARKLAND"),
        On('C', "DARKLAND"),
    };

    private static bool IsGame(string folder) => Games.Contains(folder);

    [Fact]
    public void AFolderOrItsDarklandSubfolderCounts()
    {
        Assert.Equal(On('C', "DARKLAND"), GameFolderSearch.GameFolderIn(On('C', "DARKLAND"), IsGame));
        Assert.Equal(On('D', "Games", "Darklands", "DARKLAND"), GameFolderSearch.GameFolderIn(On('D', "Games", "Darklands"), IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn(On('D', "Games"), IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn("", IsGame));
        Assert.Null(GameFolderSearch.GameFolderIn(null, IsGame));
    }

    [Fact]
    public void TheFirstCandidateHoldingTheGameWinsWithItsSource()
    {
        var found = GameFolderSearch.Find(
            [(On('E', "Nothing"), "a"), (On('D', "Games", "Darklands"), "GOG"), (On('C', "DARKLAND"), "usual")], IsGame);
        Assert.Equal((On('D', "Games", "Darklands", "DARKLAND"), "GOG"), found);
        Assert.Null(GameFolderSearch.Find([(On('E', "Nothing"), "a")], IsGame));
    }

    [Fact]
    public void AnUnreadableCandidateIsSkipped()
    {
        bool Throwing(string folder) => folder.StartsWith(On('X'), StringComparison.Ordinal) ? throw new UnauthorizedAccessException() : IsGame(folder);
        Assert.Equal((On('C', "DARKLAND"), "usual"), GameFolderSearch.Find([(On('X', "Locked"), "a"), (On('C', "DARKLAND"), "usual")], Throwing));
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

    // A folder rooted on the running system as given (Windows: with backslashes) and as the settings file then reads it: a
    // "D:/..." is relative on Linux and resolved against the settings file's folder, as any relative name is.
    private static (string Given, string Read) Rooted(string windows, string linux) =>
        OperatingSystem.IsWindows() ? (windows, windows.Replace('\\', '/')) : (linux, linux);

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void AnEmptyGameFolderOfTheTemplateIsFilledAndTheFileStillReads()
    {
        var folder = Rooted(@"D:\Games\Darklands\DARKLAND", "/d/Games/Darklands/DARKLAND");
        File.WriteAllText(_file, SettingsFile.Template);
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, folder.Given, replace: false, out _));
        var load = SettingsFile.Load(_file);
        Assert.Empty(load.Problems);
        Assert.Equal(folder.Read, load.Settings.GameFolder);
        Assert.Contains("// The folder with the original game", File.ReadAllText(_file));
    }

    [Fact]
    public void ANamedFolderIsKeptUnlessReplacing()
    {
        var old = Rooted("C:/Old", "/c/Old");
        var named = Rooted(@"D:\New", "/d/New");
        File.WriteAllText(_file, $$"""{ "gameFolder": "{{old.Given}}", "savesFolder": "" }""");
        Assert.False(SettingsFileEdits.TrySetGameFolder(_file, named.Given, replace: false, out _));
        Assert.Equal(old.Read, SettingsFile.Load(_file).Settings.GameFolder);
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, named.Given, replace: true, out _));
        Assert.Equal(named.Read, SettingsFile.Load(_file).Settings.GameFolder);
    }

    [Fact]
    public void NullIsFilledACommentedKeyIsNot()
    {
        File.WriteAllText(_file, "{\n  // \"gameFolder\": \"\",\n  \"gameFolder\": null\n}");
        var folder = Rooted("E:/Darklands", "/e/Darklands");
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, folder.Given, replace: false, out _));
        var text = File.ReadAllText(_file);
        Assert.Contains("// \"gameFolder\": \"\",", text);
        Assert.Equal(folder.Read, SettingsFile.Load(_file).Settings.GameFolder);
    }

    [Fact]
    public void BackslashesBecomeSlashesOnlyWhereTheySeparateFolders()
    {
        Assert.Equal("D:/Games/Darklands", SettingsFileEdits.Written(@"D:\Games\Darklands", '\\'));
        Assert.Equal(@"/home/deck/odd\name/DARKLAND", SettingsFileEdits.Written(@"/home/deck/odd\name/DARKLAND", '/'));
        // and on this system: on Linux a folder whose name holds a backslash is written and read back as it is
        var folder = Rooted(@"D:\Games\Darklands", @"/d/odd\name/DARKLAND");
        File.WriteAllText(_file, """{ "gameFolder": "" }""");
        Assert.True(SettingsFileEdits.TrySetGameFolder(_file, folder.Given, replace: false, out _));
        Assert.Equal(folder.Read, SettingsFile.Load(_file).Settings.GameFolder);
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
