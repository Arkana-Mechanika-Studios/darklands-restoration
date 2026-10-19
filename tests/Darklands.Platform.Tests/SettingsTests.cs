using Darklands.Audio;
using Darklands.Audio.Mt32;
using Darklands.Content;
using Darklands.Platform;

namespace Darklands.Platform.Tests;

/// <summary>A scratch folder under the test program's own folder (never the system temp folder).</summary>
public sealed class Scratch : IDisposable
{
    public Scratch() => Path = System.IO.Path.Combine(AppContext.BaseDirectory, "scratch-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public string Folder(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

public class SettingsParsingTests
{
    private const string Everything = """
        // a comment
        {
          "gameFolder": "C:\\Games\\Darklands", /* block comment */
          "savesFolder": "C:\\Saves",
          "display": { "scale": 2, "fullscreen": true, "hdPack": "C:\\Packs\\HD", },
          "sound": {
            "card": "MT32",
            "mt32Roms": "C:\\Roms",
            "configDrk": { "card": "sbpro2", "port": "0x240", "irq": 5, "dma": 3, },
          },
          "startup": { "banner": false, "intro": false, "commandLine": ["-Q", "-AR"], },
          "textFont": "C:\\font.bin",
          "screenshotsFolder": "C:\\Shots",
          "sdl": { "library": "C:\\SDL2.dll", },
        }
        """;

    [Fact]
    public void ReadsEveryKeyWithCommentsAndTrailingCommas()
    {
        var load = SettingsFile.Parse(Everything.Replace("\\\\", "/"));
        Assert.Empty(load.Problems);
        var s = load.Settings;
        Assert.Equal("C:/Games/Darklands", s.GameFolder);
        Assert.Equal("C:/Saves", s.SavesFolder);
        Assert.Equal(2, s.Scale);
        Assert.True(s.Fullscreen);
        Assert.Equal("C:/Packs/HD", s.HdPack);
        Assert.Equal("mt32", s.SoundCard);
        Assert.Equal("C:/Roms", s.Mt32Roms);
        Assert.Equal(new ConfigDrkSettings(4, 0x240, 5, 3), s.ConfigDrk);
        Assert.False(s.Banner);
        Assert.False(s.Intro);
        Assert.Equal(["-Q", "-AR"], s.CommandLine);
        Assert.Equal("C:/font.bin", s.TextFont);
        Assert.Equal("C:/Shots", s.ScreenshotsFolder);
        Assert.Equal("C:/SDL2.dll", s.SdlLibrary);
    }

    /// <summary>The HD pack folder: relative to the settings file's folder; --hd-pack replaces it.</summary>
    [Fact]
    public void TheHdPackFolderIsAPathAndTheCommandLineWins()
    {
        using var scratch = new Scratch();
        var path = System.IO.Path.Combine(scratch.Folder("settings"), "darklands.settings.json");
        File.WriteAllText(path, """{ "display": { "hdPack": "packs/hd" } }""");
        var load = SettingsFile.Load(path);
        Assert.Empty(load.Problems);
        Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.Combine(scratch.Path, "settings", "packs", "hd")), load.Settings.HdPack);
        Assert.Equal(load.Settings.HdPack, ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, load.Settings).HdPack);
        Assert.Equal("D:/other", ResolvedSettings.Resolve(new CommandLineOptions { HdPack = "D:/other" }, _ => null, load.Settings).HdPack);
        Assert.Null(ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, new GameSettings()).HdPack);
    }

    [Fact]
    public void ResolutionAndCommandLineAsText()
    {
        var load = SettingsFile.Parse("""{ "display": { "resolution": "1440x800" }, "startup": { "commandLine": "-Q -O" } }""");
        Assert.Empty(load.Problems);
        Assert.Equal("1440x800", load.Settings.Resolution);
        Assert.Equal(["-Q", "-O"], load.Settings.CommandLine);
    }

    [Fact]
    public void UnknownKeysAreReportedAndIgnored()
    {
        var load = SettingsFile.Parse("""
            { "gameFolder": "x", "colour": 1, "display": { "scale": 3, "zoom": 2 },
              "sound": { "volume": 5, "configDrk": { "card": 2, "irqq": 7 } }, "sdl": { "driver": "a" } }
            """);
        Assert.Equal(3, load.Settings.Scale);
        Assert.Equal(new ConfigDrkSettings(2, null, null, null), load.Settings.ConfigDrk);
        Assert.Collection(load.Problems.Order(),
            p => Assert.Contains("\"colour\"", p),
            p => Assert.Contains("\"display.zoom\"", p),
            p => Assert.Contains("\"sdl.driver\"", p),
            p => Assert.Contains("\"sound.configDrk.irqq\"", p),
            p => Assert.Contains("\"sound.volume\"", p));
        Assert.All(load.Problems, p => Assert.Contains("unknown key", p));
    }

    [Theory]
    [InlineData("""{ "display": { "scale": 0 } }""", "display.scale")]
    [InlineData("""{ "display": { "scale": "big" } }""", "display.scale")]
    [InlineData("""{ "display": { "resolution": "wide" } }""", "display.resolution")]
    [InlineData("""{ "display": { "fullscreen": "yes" } }""", "display.fullscreen")]
    [InlineData("""{ "sound": { "card": "gravis" } }""", "sound.card")]
    [InlineData("""{ "sound": { "card": 3 } }""", "sound.card")]
    [InlineData("""{ "sound": { "configDrk": { "port": 70000 } } }""", "sound.configDrk.port")]
    [InlineData("""{ "sound": { "configDrk": { "card": "gravis" } } }""", "sound.configDrk.card")]
    [InlineData("""{ "sound": { "configDrk": 5 } }""", "sound.configDrk")]
    [InlineData("""{ "startup": { "banner": 1 } }""", "startup.banner")]
    [InlineData("""{ "startup": { "commandLine": [1] } }""", "startup.commandLine")]
    [InlineData("""{ "gameFolder": 5 }""", "gameFolder")]
    [InlineData("""{ "display": 7 }""", "display")]
    public void InvalidValuesAreReportedAndIgnored(string json, string key)
    {
        var load = SettingsFile.Parse(json);
        var problem = Assert.Single(load.Problems);
        Assert.Contains("\"" + key + "\"", problem);
        Assert.Equal(new GameSettings().Scale, load.Settings.Scale);
        Assert.Null(load.Settings.ConfigDrk);
        Assert.Null(load.Settings.SoundCard);
        Assert.Null(load.Settings.Banner);
        Assert.Null(load.Settings.Resolution);
        Assert.Null(load.Settings.GameFolder);
    }

    [Fact]
    public void ScaleAndResolutionTogetherKeepTheResolution()
    {
        var load = SettingsFile.Parse("""{ "display": { "scale": 2, "resolution": "800x600" } }""");
        Assert.Empty(load.Problems);
        Assert.Null(load.Settings.Scale);
        Assert.Equal("800x600", load.Settings.Resolution);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void BrokenFilesNeverThrow(string text)
    {
        var load = SettingsFile.Parse(text);
        Assert.NotEmpty(load.Problems);
        Assert.Null(load.Settings.GameFolder);
    }

    [Fact]
    public void RelativePathsFollowTheSettingsFile()
    {
        using var scratch = new Scratch();
        var folder = scratch.Folder("a");
        var load = SettingsFile.Parse("""{ "savesFolder": "..\\saves", "gameFolder": "game" }""".Replace("\\\\", "/"), folder);
        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "../saves")), load.Settings.SavesFolder);
        Assert.Equal(Path.Combine(folder, "game"), load.Settings.GameFolder);
    }

    [Fact]
    public void LoadPrefixesProblemsWithTheFile()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder("x"), "s.json");
        File.WriteAllText(path, """{ "bogus": 1 }""");
        var load = SettingsFile.Load(path);
        Assert.Contains(path, Assert.Single(load.Problems));
        Assert.Contains("cannot read", Assert.Single(SettingsFile.Load(path + ".missing").Problems));
    }

    [Theory]
    [InlineData("0x220", 0x220)]
    [InlineData("220h", 0x220)]
    [InlineData("544", 544)]
    [InlineData("zz", null)]
    public void WordsInTextForm(string text, int? expected) => Assert.Equal(expected, SettingsFile.ParseWord(text));
}

public class SettingsPrecedenceTests
{
    private static string? NoEnvironment(string name) => null;

    [Fact]
    public void DefaultsWithNothingSet()
    {
        var r = ResolvedSettings.Resolve(new CommandLineOptions(), NoEnvironment, new GameSettings());
        Assert.Null(r.GameFolder);
        Assert.Null(r.Window);
        Assert.False(r.Fullscreen);
        Assert.Null(r.SoundChoice);
        Assert.False(r.NoAudio);
        Assert.True(r.ShowBanner);
        Assert.True(r.PlayIntro);
        Assert.Empty(r.CommandLine);
    }

    [Fact]
    public void FileOverDefaults()
    {
        var file = new GameSettings { GameFolder = "F", Scale = 2, Fullscreen = true, SoundCard = "adlib", Banner = false, Intro = false, CommandLine = ["-Q"], Mt32Roms = "R" };
        var r = ResolvedSettings.Resolve(new CommandLineOptions(), NoEnvironment, file);
        Assert.Equal("F", r.GameFolder);
        Assert.Equal((1440, 800), r.Window);
        Assert.True(r.Fullscreen);
        Assert.Equal("adlib", r.SoundChoice);
        Assert.False(r.ShowBanner);
        Assert.False(r.PlayIntro);
        Assert.Equal(["-Q"], r.CommandLine);
        Assert.Equal("R", r.Mt32Roms);
    }

    [Fact]
    public void EnvironmentOverFileAndCommandLineOverEnvironment()
    {
        string? Env(string name) => name switch { "DARKLANDS_DIR" => "E", "DARKLANDS_MT32_ROMS" => "ER", "DARKLANDS_TEXT_FONT" => "EF", _ => null };
        var file = new GameSettings { GameFolder = "F", Mt32Roms = "FR", TextFont = "FF" };
        var fromEnvironment = ResolvedSettings.Resolve(new CommandLineOptions(), Env, file);
        Assert.Equal(("E", "ER", "EF"), (fromEnvironment.GameFolder, fromEnvironment.Mt32Roms, fromEnvironment.TextFont));
        var cli = new CommandLineOptions { GameFolder = "C", Mt32Roms = "CR", TextFont = "CF" };
        var fromCli = ResolvedSettings.Resolve(cli, Env, file);
        Assert.Equal(("C", "CR", "CF"), (fromCli.GameFolder, fromCli.Mt32Roms, fromCli.TextFont));
    }

    [Fact]
    public void CommandLineWindowReplacesTheFilesWindow()
    {
        var file = new GameSettings { Resolution = "800x600" };
        Assert.Equal((2160, 1200), ResolvedSettings.Resolve(new CommandLineOptions { Scale = "3" }, NoEnvironment, file).Window);
        Assert.Equal((1000, 500), ResolvedSettings.Resolve(new CommandLineOptions { Resolution = "1000x500" }, NoEnvironment, new GameSettings { Scale = 2 }).Window);
    }

    [Fact]
    public void CommandLineSoundAndStartupOverTheFile()
    {
        var file = new GameSettings { SoundCard = "none", Banner = true };
        Assert.True(ResolvedSettings.Resolve(new CommandLineOptions(), NoEnvironment, file).NoAudio);
        var r = ResolvedSettings.Resolve(new CommandLineOptions { Sound = "cm32l", NoBanner = true, GameCommandLine = [] }, NoEnvironment,
            new GameSettings { SoundCard = "none", Banner = true, CommandLine = ["-Q"] });
        Assert.False(r.NoAudio);
        Assert.Equal("cm32l", r.SoundChoice);
        Assert.False(r.ShowBanner);
        Assert.Empty(r.CommandLine);
        Assert.True(ResolvedSettings.Resolve(new CommandLineOptions { NoAudio = true }, NoEnvironment, new GameSettings { SoundCard = "sb" }).NoAudio);
    }

    [Fact]
    public void ConfigCardMeansTheGamesOwnChoice()
    {
        Assert.Null(ResolvedSettings.Resolve(new CommandLineOptions { Sound = "config" }, NoEnvironment, new GameSettings { SoundCard = "adlib" }).SoundChoice);
    }

    [Fact]
    public void InvalidCommandLineValuesThrowWithAMessage()
    {
        Assert.Throws<ArgumentException>(() => ResolvedSettings.Resolve(new CommandLineOptions { Sound = "gravis" }, NoEnvironment, new GameSettings()));
        Assert.Throws<FormatException>(() => ResolvedSettings.Resolve(new CommandLineOptions { Resolution = "wide" }, NoEnvironment, new GameSettings()));
        Assert.Throws<FormatException>(() => ResolvedSettings.Resolve(new CommandLineOptions { Scale = "x" }, NoEnvironment, new GameSettings()));
        Assert.Throws<ArgumentException>(() => ResolvedSettings.Resolve(new CommandLineOptions { Scale = "2", Resolution = "800x600" }, NoEnvironment, new GameSettings()));
    }
}

public class ConfigDrkOverrideTests
{
    private static GameFiles Game(Scratch scratch, int[]? config = null)
    {
        var folder = scratch.Folder("game");
        if (config is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, "CONFIG.DRK"), [.. config.SelectMany(w => new[] { (byte)w, (byte)(w >> 8) })]);
        }
        return new GameFiles(folder);
    }

    [Theory]
    [InlineData(0, 'a', "ASOUND.DLC")]
    [InlineData(2, 'a', "ASOUND.DLC")]
    [InlineData(4, 'p', "PSOUND.DLC")]
    [InlineData(7, 'r', "RSOUND.DLC")]
    [InlineData(0x7F, 'i', "ISOUND.DLC")]
    [InlineData(0xFFFF, 'n', "NSOUND.DLC")]
    public void CardOverrideSelectsTheDriverLetterAsAConfigDrkWould(int card, char letter, string towns)
    {
        using var scratch = new Scratch();
        var files = Game(scratch, [2, 0x220, 7, 1]);
        files.OverrideConfig(card, 0x240, 5, 3);
        var settings = SoundCardSettings.FromConfig(files);
        Assert.Equal(new SoundCardSettings((SoundCard)card, 0x240, 5, 3), settings);
        Assert.Equal(letter, settings!.DriverLetter);
        Assert.Equal(towns, SoundSetup.FromConfig(files).TownsDriverName);
    }

    [Fact]
    public void FieldsLeftOutKeepTheFilesWords()
    {
        using var scratch = new Scratch();
        var files = Game(scratch, [4, 0x230, 5, 3]);
        files.OverrideConfig(null, 0x260, null, null);
        Assert.Equal(new SoundCardSettings(SoundCard.SoundBlasterPro2, 0x260, 5, 3), SoundCardSettings.FromConfig(files));
    }

    [Fact]
    public void WithoutAConfigDrkTheOverrideMakesOne()
    {
        using var scratch = new Scratch();
        var files = Game(scratch);
        Assert.Null(SoundCardSettings.FromConfig(files));
        files.OverrideConfig(7, null, null, null);
        Assert.Equal(new SoundCardSettings(SoundCard.Roland, 0x220, 7, 1), SoundCardSettings.FromConfig(files));
        Assert.True(files.Exists("config.drk"));
    }

    [Fact]
    public void TheFileOnDiskIsNotTouched()
    {
        using var scratch = new Scratch();
        var files = Game(scratch, [2, 0x220, 7, 1]);
        files.OverrideConfig(4, null, null, null);
        Assert.Equal(2, File.ReadAllBytes(Path.Combine(scratch.Path, "game", "CONFIG.DRK"))[0]);
    }

    [Fact]
    public void AnOverriddenSoundBlasterCardKeepsItsResourcesForTheChosenCard()
    {
        using var scratch = new Scratch();
        var files = Game(scratch);
        files.OverrideConfig(2, 0x240, 5, 3);
        Assert.Equal(new SoundCardSettings(SoundCard.SoundBlaster, 0x240, 5, 3), SoundCardSettings.FromConfig(files));
    }

    [Fact]
    public void SoundCardNoneGivesNoSound()
    {
        using var scratch = new Scratch();
        var lines = new List<string>();
        Assert.Null(SoundSelection.Select(Game(scratch), "none", null, 44100, lines.Add));
        Assert.Single(lines);
    }
}

public class SettingsTemplateTests
{
    /// <summary>The template shows the defaults (no null): read, it resolves exactly as no settings file at all.</summary>
    [Fact]
    public void TheTemplateParsesCleanlyToTheDefaults()
    {
        var load = SettingsFile.Parse(SettingsFile.Template);
        Assert.Empty(load.Problems);
        Assert.DoesNotContain(": null", SettingsFile.Template);
        var cli = new CommandLineOptions();
        var fromTemplate = ResolvedSettings.Resolve(cli, _ => null, load.Settings);
        var fromNothing = ResolvedSettings.Resolve(cli, _ => null, new GameSettings());
        Assert.Equal(
            (fromNothing.GameFolder, fromNothing.SavesFolder, fromNothing.Window ?? DisplayLayout.WindowSize(null, null), fromNothing.Fullscreen, fromNothing.SoundChoice, fromNothing.NoAudio,
             fromNothing.Mt32Roms, fromNothing.ConfigDrk, fromNothing.ShowBanner, fromNothing.PlayIntro, string.Join(' ', fromNothing.CommandLine),
             fromNothing.TextFont, fromNothing.ScreenshotsFolder, fromNothing.SdlLibrary, fromNothing.ConsoleEnabled, fromNothing.ConsoleFont,
             fromNothing.ConsoleFontSize, fromNothing.HdPack),
            (fromTemplate.GameFolder, fromTemplate.SavesFolder, fromTemplate.Window ?? DisplayLayout.WindowSize(null, null), fromTemplate.Fullscreen, fromTemplate.SoundChoice, fromTemplate.NoAudio,
             fromTemplate.Mt32Roms, fromTemplate.ConfigDrk, fromTemplate.ShowBanner, fromTemplate.PlayIntro, string.Join(' ', fromTemplate.CommandLine),
             fromTemplate.TextFont, fromTemplate.ScreenshotsFolder, fromTemplate.SdlLibrary, fromTemplate.ConsoleEnabled, fromTemplate.ConsoleFont,
             fromTemplate.ConsoleFontSize, fromTemplate.HdPack));
    }

    /// <summary>A resolution filled in beside the template's "scale": 1 replaces the scale, without a complaint.</summary>
    [Fact]
    public void AResolutionReplacesTheScale()
    {
        var load = SettingsFile.Parse(SettingsFile.Template.Replace("\"resolution\": \"\"", "\"resolution\": \"1440x800\""));
        Assert.Empty(load.Problems);
        Assert.Null(load.Settings.Scale);
        Assert.Equal((1440, 800), ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, load.Settings).Window);
    }

    /// <summary>Files written before the template showed its defaults (null everywhere) still mean the defaults.</summary>
    [Fact]
    public void NullStillMeansTheDefault()
    {
        var load = SettingsFile.Parse("""{ "gameFolder": null, "display": { "scale": null, "fullscreen": null }, "sound": { "card": null, "configDrk": null } }""");
        Assert.Empty(load.Problems);
        Assert.Null(load.Settings.GameFolder);
        Assert.Null(load.Settings.Scale);
        Assert.Null(load.Settings.SoundCard);
    }

    [Fact]
    public void TheTemplateDocumentsEveryKey()
    {
        foreach (var key in new[] { "gameFolder", "savesFolder", "scale", "resolution", "fullscreen", "hdPack", "card", "mt32Roms", "configDrk", "banner", "intro",
                     "commandLine", "textFont", "screenshotsFolder", "library", "display", "sound", "startup", "sdl" })
        {
            Assert.Contains($"\"{key}\":", SettingsFile.Template);
        }
        foreach (var option in new[] { "--saves", "--scale", "--resolution", "--fullscreen", "--hd-pack", "--sound", "--mt32-roms", "--no-banner", "--no-intro", "--text-font", "--screenshots", "--sdl" })
        {
            Assert.Contains(option, SettingsFile.Template);
        }
    }

    [Fact]
    public void WrittenTemplateRoundTripsAndIsNotOverwritten()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder("out"), "my.json");
        Assert.True(SettingsFile.TryWriteTemplate(path, out _));
        Assert.Equal(SettingsFile.Template, File.ReadAllText(path));
        var load = SettingsFile.Load(path);
        Assert.Empty(load.Problems);
        File.WriteAllText(path, "{}");
        Assert.False(SettingsFile.TryWriteTemplate(path, out var message));
        Assert.Contains("already exists", message);
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void LookupOrderIsExplicitThenProgramFolderThenAppData()
    {
        using var scratch = new Scratch();
        var program = scratch.Folder("program");
        var appData = scratch.Folder("appdata");
        Assert.Null(SettingsFile.Locate(null, program, appData));
        var inAppData = SettingsFile.SearchPaths(program, appData)[1];
        Directory.CreateDirectory(Path.GetDirectoryName(inAppData)!);
        File.WriteAllText(inAppData, "{}");
        Assert.Equal(inAppData, SettingsFile.Locate(null, program, appData));
        var inProgram = Path.Combine(program, SettingsFile.FileName);
        File.WriteAllText(inProgram, "{}");
        Assert.Equal(inProgram, SettingsFile.Locate(null, program, appData));
        Assert.Equal("explicit.json", SettingsFile.Locate("explicit.json", program, appData));
        Assert.EndsWith(Path.Combine("DarklandsRestoration", SettingsFile.FileName), inAppData);
    }

    [Fact]
    public void FirstRunWritesNextToTheProgramElseInAppData()
    {
        using var scratch = new Scratch();
        var lines = new List<string>();
        var program = scratch.Folder("program");
        var appData = scratch.Folder("appdata");
        var written = SettingsFile.WriteFirstRunTemplate(lines.Add, program, appData);
        Assert.Equal(Path.Combine(program, SettingsFile.FileName), written);
        Assert.Contains(written!, Assert.Single(lines));

        // a program folder that cannot be created (a file stands in its place)
        var blocked = Path.Combine(scratch.Path, "blocked");
        File.WriteAllText(blocked, "");
        lines.Clear();
        written = SettingsFile.WriteFirstRunTemplate(lines.Add, Path.Combine(blocked, "sub"), appData);
        Assert.Equal(SettingsFile.SearchPaths(Path.Combine(blocked, "sub"), appData)[1], written);
        Assert.True(File.Exists(written));
        Assert.Contains(written!, Assert.Single(lines));
    }
}
