namespace Darklands.Platform.Tests;

/// <summary>The Linux search on fake folder trees (real temporary folders, so it runs on any system).</summary>
public sealed class GameFolderSearchLinuxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dlrp-linux-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_root, "home", "deck");
    private string Mounts => Path.Combine(_root, "run", "media");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string Game(params string[] parts)
    {
        var folder = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "darkland.exe"), [0]);
        return folder;
    }

    private static bool IsGame(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(f => string.Equals(Path.GetFileName(f), "DARKLAND.EXE", StringComparison.OrdinalIgnoreCase));

    private (string Folder, string Source)? Search(Func<string, string?>? environment = null) =>
        GameFolderSearch.Find(GameFolderSearch.LinuxCandidates(Home, environment ?? (_ => null), [Mounts]), IsGame);

    [Fact]
    public void NothingFoundInAnEmptyHome()
    {
        Directory.CreateDirectory(Home);
        Assert.Null(Search());
    }

    [Fact]
    public void SteamLibraryOfTheClientIsFound()
    {
        var game = Game("home", "deck", ".local", "share", "Steam", "steamapps", "common", "Darklands", "DARKLAND");
        Assert.Equal((game, "Steam's library"), Search());
    }

    [Fact]
    public void ALibraryListedInTheVdfIsFound()
    {
        var steam = Path.Combine(Home, ".steam", "steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var sd = Path.Combine(_root, "elsewhere", "sdcard");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + sd + "\"\n\t}\n}\n");
        var game = Game("elsewhere", "sdcard", "steamapps", "common", "Darklands", "DARKLAND");
        Assert.Equal((game, "Steam's library"), Search());
    }

    [Fact]
    public void TheDecksSdCardIsFoundWithoutTheList()
    {
        var game = Game("run", "media", "mmcblk0p1", "steamapps", "common", "Darklands", "DARKLAND");
        Assert.Equal((game, "Steam's library"), Search());
    }

    [Fact]
    public void ALowercaseSubfolderIsAccepted()
    {
        var game = Game("home", "deck", "GOG Games", "Darklands", "darkland");
        Assert.Equal(game, Search()?.Folder, ignoreCase: true);
    }

    [Fact]
    public void HeroicNamesTheInstallFolder()
    {
        var game = Game("data", "gog", "Darklands");
        var folder = Path.Combine(Home, ".config", "heroic", "gog_store");
        Directory.CreateDirectory(folder);
        var path = game.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(folder, "installed.json"),
            "{\"installed\":[{\"appName\":\"123\",\"install_path\":\"/x\"},{\"appName\":\"" + GameFolderSearch.GogId + "\",\"install_path\":\"" + path + "\"}]}");
        Assert.Equal((game, "Heroic's installed games"), Search());
    }

    [Fact]
    public void HeroicFileThatIsNotHeroicsIsIgnored()
    {
        Assert.Null(GameFolderSearch.HeroicInstallPath("not json", GameFolderSearch.GogId));
        Assert.Null(GameFolderSearch.HeroicInstallPath("[1,2]", GameFolderSearch.GogId));
        Assert.Null(GameFolderSearch.HeroicInstallPath("{\"installed\":[]}", GameFolderSearch.GogId));
        Assert.Equal("/g", GameFolderSearch.HeroicInstallPath("[{\"appName\":\"5\",\"install_path\":\"/g\"}]", "5"));
    }

    [Fact]
    public void TheDefaultWinePrefixIsSearched()
    {
        var game = Game("home", "deck", ".wine", "drive_c", "GOG Games", "Darklands", "DARKLAND");
        var found = Search();
        Assert.Equal(game, found?.Folder);
        Assert.StartsWith("a Wine prefix", found?.Source);
    }

    [Fact]
    public void ANamedAndAManagersWinePrefixAreSearched()
    {
        var named = Path.Combine(_root, "prefixes", "mine");
        var game = Game("prefixes", "mine", "drive_c", "Program Files (x86)", "GOG Galaxy", "Games", "Darklands", "DARKLAND");
        Assert.Equal(game, Search(name => name == "WINEPREFIX" ? named : null)?.Folder);

        Directory.Delete(Path.Combine(_root, "prefixes"), true);
        var lutris = Game("home", "deck", "Games", "darklands-gog", "drive_c", "GOG Games", "Darklands", "DARKLAND");
        Assert.Equal(lutris, Search()?.Folder);
    }

    [Fact]
    public void AProtonPrefixOfASteamLibraryIsSearched()
    {
        var game = Game("home", "deck", ".steam", "steam", "steamapps", "compatdata", "123", "pfx", "drive_c", "Games", "Darklands", "DARKLAND");
        Assert.Equal(game, Search()?.Folder, ignoreCase: true);
    }

    [Fact]
    public void AnExternalDiskIsFound()
    {
        var game = Game("run", "media", "deck", "usb", "Darklands", "DARKLAND");
        Assert.Equal(game, Search()?.Folder, ignoreCase: true);
    }

    [Fact]
    public void XdgDataHomeMovesSteam()
    {
        var game = Game("xdg", "Steam", "steamapps", "common", "Darklands", "DARKLAND");
        Assert.Equal(game, Search(name => name == "XDG_DATA_HOME" ? Path.Combine(_root, "xdg") : null)?.Folder);
    }
}
