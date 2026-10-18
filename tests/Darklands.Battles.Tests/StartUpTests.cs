using System.Text;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The program start (StartUp) on a folder holding the player's BANNER.DAT and stand-ins for the
/// drivers: the banner's rows and colours, the cursor, the wait, the command line and the messages
/// of every check, where the original prints them.
/// </summary>
public sealed class StartUpTests
{
    private static readonly string[] Drivers = ["MISC.EXE", "MGRAPHIC.EXE", "FONTS.FNT", "COMMONSP.IMG", "PSOUND.DLC", "NSOUND.DLC", "ASOUND.DLC"];

    [Fact]
    public void TheBannerFillsRowsOneToTenInWhiteOnGreyAndStaysThreeToFourSeconds()
    {
        if (Folder() is not { } folder)
        {
            return;
        }
        var host = new TestHost();
        var startUp = new StartUp(new GameUi(host, new GameAssets(folder.Files)), folder.Files, []);
        var started = startUp.Run();
        Assert.NotNull(started);
        var banner = new Content.Formats.Config.BannerDecoder().Decode(folder.Files.Read("BANNER.DAT"));
        var text = startUp.TextScreen;
        for (var column = 0; column < TextScreen.Columns; column++)
        {
            Assert.Equal((byte)' ', text.Characters[column]);
            Assert.Equal(TextScreen.DefaultAttribute, text.Attributes[column]);
            for (var row = 1; row <= 10; row++)
            {
                Assert.Equal(banner.Entries[row].Bytes[column], text.Characters[row * TextScreen.Columns + column]);
                Assert.Equal(0x7F, text.Attributes[row * TextScreen.Columns + column]);
            }
            Assert.Equal(TextScreen.DefaultAttribute, text.Attributes[11 * TextScreen.Columns + column]);
        }
        Assert.Equal("Darklands(TM) - Version 483.07", Row(text, 3).Trim(' ', 'º'));
        Assert.Equal((14, 0), (text.CursorRow, text.CursorColumn));
        // time(0) - t0 <= 3 on a clock that starts at 0: four whole seconds; no key is read on the way
        Assert.InRange(host.FirstGraphicsAt, 4000, 4000 + 32);
        Assert.True(host.TextFrames > 100);
        Assert.Equal(0, host.KeyReads);
        Assert.False(started.NoAnimations);
        Assert.Equal("psound.dlc", started.SoundDriver);
        Assert.True(started.LoadsOpeningSamples);
    }

    [Theory]
    [InlineData(null, "nsound.dlc")]
    [InlineData(0, "asound.dlc")]
    [InlineData(3, "asound.dlc")]
    [InlineData(5, "psound.dlc")]
    [InlineData(6, "psound.dlc")]
    [InlineData(0xFF, "nsound.dlc")]
    public void ConfigDrkPicksTheDriver(int? card, string driver)
    {
        if (Folder(card) is not { } folder)
        {
            return;
        }
        var started = new StartUp(new GameUi(new TestHost(), new GameAssets(folder.Files)), folder.Files, []).Run();
        Assert.Equal(driver, started?.SoundDriver);
    }

    [Fact]
    public void QAndADriverLetterAreTakenUpperCased()
    {
        if (Folder() is not { } folder)
        {
            return;
        }
        var started = new StartUp(new GameUi(new TestHost(), new GameAssets(folder.Files)), folder.Files, ["/q", "-aa"]).Run();
        Assert.NotNull(started);
        Assert.True(started.NoAnimations);
        Assert.Equal("Asound.dlc", started.SoundDriver);
        Assert.False(started.LoadsOpeningSamples);
    }

    [Fact]
    public void BadArgumentsArePrintedFromRow22AndStopTheProgram()
    {
        if (Folder() is not { } folder)
        {
            return;
        }
        var host = new TestHost();
        var startUp = new StartUp(new GameUi(host, new GameAssets(folder.Files)), folder.Files, ["-ax", "-z"]);
        Assert.Null(startUp.Run());
        Assert.Equal("Sound driver not found: -AX", Row(startUp.TextScreen, 22).TrimEnd());
        Assert.Equal("Unknown argument: -Z", Row(startUp.TextScreen, 23).TrimEnd());
        Assert.Equal(TextScreen.DefaultAttribute, startUp.TextScreen.Attributes[22 * TextScreen.Columns]);
        Assert.Equal(0, host.GraphicsFrames);

        startUp = new StartUp(new GameUi(new TestHost(), new GameAssets(folder.Files)), folder.Files, ["q"]);
        Assert.Null(startUp.Run());
        Assert.Equal("Unknown argument: Q", Row(startUp.TextScreen, 22).TrimEnd());
    }

    [Fact]
    public void TheMemoryReportOfOIsPrintedFromRow14()
    {
        if (Folder() is not { } folder)
        {
            return;
        }
        var startUp = new StartUp(new GameUi(new TestHost(), new GameAssets(folder.Files)), folder.Files, ["-O"]);
        Assert.NotNull(startUp.Run());
        Assert.StartsWith("Darklands requires ", Row(startUp.TextScreen, 14));
        Assert.StartsWith("Your machine has only 648192 bytes (633K) of conventional memory.", Row(startUp.TextScreen, 15));
        Assert.StartsWith("Therefore, you need to free an additional -", Row(startUp.TextScreen, 16));
        Assert.Equal((17, 0), (startUp.TextScreen.CursorRow, startUp.TextScreen.CursorColumn));
    }

    [Fact]
    public void AMissingBannerClearsTheScreenWithTheLibrarysMessage()
    {
        if (Folder(remove: "BANNER.DAT") is not { } folder)
        {
            return;
        }
        var startUp = new StartUp(new GameUi(new TestHost(), new GameAssets(folder.Files)), folder.Files, []);
        Assert.Null(startUp.Run());
        Assert.Equal("banner.dat: No such file or directory", Row(startUp.TextScreen, 0).TrimEnd());
        Assert.Equal((1, 0), (startUp.TextScreen.CursorRow, startUp.TextScreen.CursorColumn));
    }

    [Fact]
    public void AMissingGraphicsDriverIsReportedUnderTheBanner()
    {
        if (Folder(remove: "MGRAPHIC.EXE") is not { } folder)
        {
            return;
        }
        var host = new TestHost();
        var startUp = new StartUp(new GameUi(host, new GameAssets(folder.Files)), folder.Files, []);
        Assert.Null(startUp.Run());
        Assert.Equal("2 - Filename not found", Row(startUp.TextScreen, 14).TrimEnd());
        Assert.Equal(0x7F, startUp.TextScreen.Attributes[3 * TextScreen.Columns]);
        Assert.Equal(0, host.GraphicsFrames);
    }

    [Fact]
    public void AMissingSoundDriverIsPrintedOnTheGraphicsScreen()
    {
        if (Folder(remove: "PSOUND.DLC") is not { } folder)
        {
            return;
        }
        var host = new TestHost();
        var ui = new GameUi(host, new GameAssets(folder.Files));
        Assert.Null(new StartUp(ui, folder.Files, []).Run());
        Assert.True(host.GraphicsFrames > 0);
        // "2 - ..." in colour 7 from the top left; the rest stays black
        Assert.Contains(Enumerable.Range(0, 8 * 8), i => ui.Canvas[i % 8, i / 8] == 7);
        Assert.Equal(0, ui.Canvas[100, 100]);
    }

    private static string Row(TextScreen screen, int row) =>
        Encoding.Latin1.GetString(screen.Characters, row * TextScreen.Columns, TextScreen.Columns);

    private sealed record TempFolder(GameFiles Files);

    /// <summary>A folder with the player's BANNER.DAT, a CONFIG.DRK for <paramref name="card"/> (none for null) and empty drivers.</summary>
    private static TempFolder? Folder(int? card = 4, string? remove = null)
    {
        if (GameFolder.Files is not { } game)
        {
            return null;
        }
        var path = Directory.CreateTempSubdirectory("darklands-startup-").FullName;
        File.WriteAllBytes(Path.Combine(path, "BANNER.DAT"), game.Read("BANNER.DAT"));
        foreach (var name in Drivers)
        {
            File.WriteAllBytes(Path.Combine(path, name), []);
        }
        if (card is { } code)
        {
            File.WriteAllBytes(Path.Combine(path, "CONFIG.DRK"), [(byte)code, (byte)(code >> 8), 0x20, 0x02, 5, 0, 1, 0]);
        }
        if (remove is not null)
        {
            File.Delete(Path.Combine(path, remove));
        }
        return new TempFolder(new GameFiles(path));
    }

    private sealed class TestHost : IHost, IVideo, IInput, IClock, IAudio
    {
        private long _now;
        private int _calls;

        public int TextFrames { get; private set; }

        public int GraphicsFrames { get; private set; }

        public long FirstGraphicsAt { get; private set; } = -1;

        public int KeyReads { get; private set; }

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(0, 0, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen)
        {
            if (GraphicsFrames++ == 0)
            {
                FirstGraphicsAt = _now;
            }
        }

        public void PresentText(TextScreen screen) => TextFrames++;

        public void Pump()
        {
        }

        public bool TryReadKey(out int key)
        {
            // a key every third call (after a flush the screen waits for the next one)
            KeyReads++;
            key = Keys.Escape;
            return ++_calls % 3 == 0;
        }

        public void Sleep(int milliseconds) => _now += milliseconds;

        public void PlayMusic(int tune)
        {
        }

        public void StopMusic()
        {
        }

        public void PlaySound(int effect)
        {
        }
    }
}
