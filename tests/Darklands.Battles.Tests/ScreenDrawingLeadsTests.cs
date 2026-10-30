using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// Drawing details checked against the original's screens (local/batch/c5uncertain, vis10 pixel comparisons): the prayer page's member
/// list (R034+0x0E04), the card picture held until its first text (R043+0x0A9A, DS:EE55).
/// </summary>
public sealed class ScreenDrawingLeadsTests
{
    /// <summary>A host that keeps the last screen presented.</summary>
    private sealed class ShotHost : IHost, IVideo, IInput, IClock
    {
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[] Shown { get; } = new byte[Screen.Width * Screen.Height];

        /// <summary>Ends the run (<see cref="Stop"/>) at that present (counted from 1), once its screen is kept; 0: never.</summary>
        public int StopAtPresent { get; init; }

        private int _presents;

        public sealed class Stop : Exception;

        /// <summary>In <see cref="Keys"/>: that read finds no key (a pass of the loop that reads goes by).</summary>
        public const int NoKey = -1;

        /// <summary>The keys the host gives, in order.</summary>
        public Queue<int> Keys { get; } = new();

        /// <summary>Every screen presented, when <see cref="KeepFrames"/>.</summary>
        public List<byte[]> Frames { get; } = [];

        public bool KeepFrames { get; init; }

        public void Present(Screen screen)
        {
            screen.Pixels.CopyTo(Shown, 0);
            if (KeepFrames)
            {
                Frames.Add([.. screen.Pixels]);
            }
            if (++_presents == StopAtPresent)
            {
                throw new Stop();
            }
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        /// <summary>Each key as it is read (tests that look at the state a key is read in).</summary>
        public Action<int>? Read { get; set; }

        public bool TryReadKey(out int key)
        {
            if (!Keys.TryDequeue(out key) || key == NoKey)
            {
                key = 0;
                return false;
            }
            Read?.Invoke(key);
            return true;
        }

        public void Pump()
        {
        }
    }

    [Fact]
    public void ThePrayerMemberListHoldsTheShortNamesOfThePresentMembersInSlotOrder()
    {
        var party = new Party();
        party.Members.Add(new Character { Name = "Heidi Baldur", ShortName = "Heidi", Status = CharacterStatus.Able });
        party.Members.Add(new Character { Name = "Nobody", ShortName = "Nobody", Status = CharacterStatus.Empty });
        party.Members.Add(new Character { Name = "Wilhelm Kreisler", ShortName = "Wilhelm", Status = CharacterStatus.Unconscious });
        Assert.Equal("Heidi\nWilhelm", SaintPrayerScreen.MemberListText(party));
    }

    [Fact]
    public void AHeldScreenStaysOnShowUntilItIsReleasedOrInputIsAwaited()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost();
        var ui = new GameUi(host, new GameAssets(files));
        ui.Canvas.Fill(0, 0, 10, 10, 3);
        ui.Present();
        ui.HoldScreen();
        ui.Canvas.Fill(0, 0, 10, 10, 7);
        ui.Present();
        Assert.Equal(3, host.Shown[0]);
        ui.Poll();
        Assert.Equal(7, host.Shown[0]);
    }

    // orig: R014+0x02F8 root 047C:056F (the screen on again) before the battle's loop: a card that shows its picture and goes into a
    // battle before any text (card 0x24's city ambush, R053) shows the battle, not the screen held for the picture (vis10: local/batch/b24fix)
    [Fact]
    public void ABattleShowsItsOwnScreenOverAHeldCardPicture()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost { StopAtPresent = 30 };
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        ui.Canvas.Fill(0, 0, Screen.Width, Screen.Height, 3);
        ui.HoldScreen();
        ui.Canvas.Fill(0, 0, Screen.Width, Screen.Height, 7);
        var screens = new BattleScreens(ui, () => state);
        Assert.Throws<ShotHost.Stop>(() => screens.Battle(new Darklands.Cards.BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)])));
        Assert.Contains(host.Shown, p => p != 3 && p != 7);
        Assert.Equal(ui.Canvas.Screen.Pixels, host.Shown);
    }

    // orig: R002+0x0378..0x037B root 047C:0584(0x1C0) sets colours 0..15 to the driver's sixteen in every battle; R002+0x03B4..0x03C6 fills
    // colours 16..255 with 0xFF before the parts are copied and palette-load (root 0410:0397) writes the bytes as they are: the DAC shows the
    // colours no part sets (235..242, 255; 32..79 where no enemy scheme takes them) white (vis10 sessions D-09fbc1, D-b797f7, D-56a675: local/batch/batpal)
    [Fact]
    public void ABattleSetsTheSixteenStartUpColoursAndWhiteWhereNoPartIsCopied()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost { StopAtPresent = 30 };
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        // a city picture's colours before the battle (colour 9 as D-09fbc1's town leaves it)
        ui.Canvas.SetColor(9, new Darklands.Content.Formats.Graphics.Rgb24(60, 56, 199));
        ui.Canvas.SetColor(255, new Darklands.Content.Formats.Graphics.Rgb24(101, 85, 81));
        ui.Canvas.SetColor(238, new Darklands.Content.Formats.Graphics.Rgb24(170, 170, 170));
        var screens = new BattleScreens(ui, () => state);
        Assert.Throws<ShotHost.Stop>(() => screens.Battle(new Darklands.Cards.BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)])));
        var palette = ui.Canvas.Screen.Palette;
        uint Rgb(int i) => palette[i] & 0xFFFFFF;
        uint Of(Darklands.Content.Formats.Graphics.Rgb24 c) => (uint)(c.R << 16 | c.G << 8 | c.B);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(i == 13 ? 0xC7C7C7u : Of(Darklands.Presentation.Graphics.Palette.StartupColors[i]), Rgb(i));
        }
        Assert.Equal(0x5555FFu, Rgb(9));
        foreach (var white in new[] { 235, 238, 242, 255 })
        {
            Assert.Equal(0xFFFFFFu, Rgb(white));
        }
    }

    // orig: root 0000:0012..0x0033: menu-bar-loop sets the card window's font to 1 and runs the hot keys with it (root 0000:0CAA); its items
    // run with the font put back (root 0000:014C..0x0158): Alt-Q's box is font 1 (rows 93..106), the Game menu's Quit to DOS font 3 (92..106);
    // the battle's Alt-S "Saving Game" box is the same box (vis10 session D-b797f7: local/batch/batpal)
    [Theory]
    [InlineData(true, 93)]
    [InlineData(false, 92)]
    public void AHotKeysBoxIsInTheCardWindowsFontOneAndTheMenusInFontThree(bool hotKey, int top)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost { KeepFrames = true };
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        var keys = new HotKeys(ui, state, ui.Options);
        ui.GameKey = keys.Handle;
        ui.Canvas.Fill(0, 0, Screen.Width, Screen.Height, 3);
        ui.Present();
        if (hotKey)
        {
            host.Keys.Enqueue(Keys.AltQ);
            host.Keys.Enqueue(0x316E);
            ui.Poll();
        }
        else
        {
            host.Keys.Enqueue(0x316E);
            keys.QuitToDos();
        }
        // rows 30 and down: the pointer stands at (5, 5)
        static List<int> Rows(byte[] f) =>
            Enumerable.Range(30, Screen.Height - 30).Where(y => Enumerable.Range(0, Screen.Width).Any(x => f[y * Screen.Width + x] != 3)).ToList();
        var rows = host.Frames.Select(Rows).First(r => r.Count > 0);
        Assert.Equal(top, rows[0]);
        Assert.Equal(106, rows[^1]);
        Assert.Equal(3, ui.CardWindowFont);
    }

    // orig: root 0000:014C..0x0158: menu-bar-loop puts the card window's font back (3) before it calls the chosen item: the Game menu's
    // Quit to DOS, chosen through the bar (F10, Down to the last item, Enter), asks in font 3 (rows 92..106; font 1 would be 93)
    [Fact]
    public void AMenuBarChoiceThroughTheBarAsksInFontThree()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost();
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        var keys = new HotKeys(ui, state, ui.Options);
        ui.GameKey = keys.Handle;
        ui.MenuBar = MenuBar.ForGame(ui, state, ui.Options, keys);
        ui.Canvas.Fill(0, 0, Screen.Width, Screen.Height, 3);
        ui.Present();
        host.Keys.Enqueue(Keys.F10);
        // a read with no key: the bar's first pass opens the Game menu before the cursor moves
        host.Keys.Enqueue(ShotHost.NoKey);
        for (var i = 0; i < 10; i++)
        {
            host.Keys.Enqueue(Keys.Down);
        }
        host.Keys.Enqueue(Keys.Enter);
        host.Keys.Enqueue(0x316E);
        byte[]? asked = null;
        var font = 0;
        host.Read = key =>
        {
            if (key == 0x316E)
            {
                asked = [.. host.Shown];
                font = ui.CardWindowFont;
            }
        };
        Assert.Null(ui.Poll());
        Assert.NotNull(asked);
        Assert.Equal(3, font);
        var rows = Enumerable.Range(30, Screen.Height - 30).Where(y => Enumerable.Range(0, Screen.Width).Any(x => asked![y * Screen.Width + x] != 3)).ToList();
        Assert.Equal(92, rows[0]);
        Assert.Equal(106, rows[^1]);
        Assert.Equal(3, ui.CardWindowFont);
    }

    // orig: root 0000:0CAA's table (Alt-Q/O/P/S/D/F/L/C/M, F6, F10) runs with the font 1 (root 0000:0029..0x0033); F1..F5 and Ctrl-F1..F5 are
    // the screen's own keys (R030+0x0CC5, R043+0x33D9, R016+0x08EC, R022+0x05FE), read with the font put back: what they open is in font 3
    [Theory]
    [InlineData(Keys.AltQ, 1)]
    [InlineData(Keys.AltO, 1)]
    [InlineData(Keys.AltP, 1)]
    [InlineData(Keys.AltS, 1)]
    [InlineData(Keys.AltD, 1)]
    [InlineData(Keys.AltF, 1)]
    [InlineData(Keys.AltL, 1)]
    [InlineData(Keys.AltC, 1)]
    [InlineData(Keys.AltM, 1)]
    [InlineData(Keys.F6, 1)]
    [InlineData(Keys.F1, 3)]
    [InlineData(Keys.F5, 3)]
    [InlineData(Keys.CtrlF1, 3)]
    [InlineData(Keys.CtrlF5, 3)]
    public void OnlyTheHotKeysTableRunsInFontOne(int key, int font)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost();
        var ui = new GameUi(host, new GameAssets(files));
        var seen = new List<int>();
        ui.GameKey = _ =>
        {
            seen.Add(ui.CardWindowFont);
            return true;
        };
        host.Keys.Enqueue(key);
        Assert.Null(ui.PollWithSheetKeys());
        Assert.Equal([font], seen);
        Assert.Equal(3, ui.CardWindowFont);
    }

    // The real paths of a game: F1 on the first card opens the sheet, where Alt-Q (a hot key) asks in font 1 and the menu bar's Quit to DOS (F10, Down to the last item, Enter) asks
    // in font 3 (AUDIT b25 F1: was 1); Alt-Q on the card asks in font 1 (a hot key); Alt-L asks in font 3 (game-load-entry R037+0x8B76 sets 3 first)
    [Fact]
    public void TheSheetsMenuBarTheHotKeysAndAltLAskInTheirFonts()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        string Code(int key) => "code:" + key.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
        string[] steps =
        [
            "key:q", "wait:10", "key:space", "wait:10", "key:space", "wait:10", "key:space", "wait:10", "key:space", "wait:10",
            "key:space", "wait:40",
            Code(Keys.F1), "wait:40", Code(Keys.AltQ), "wait:10", "key:n", "wait:20", Code(Keys.F10), "wait:5", .. Enumerable.Repeat(Code(Keys.Down), 10), Code(Keys.Enter), "wait:10",
            "key:n", "wait:10", "key:esc", "wait:40",
            Code(Keys.AltQ), "wait:10", "key:n", "wait:20",
            Code(Keys.AltL), "wait:10", "key:n", "wait:20",
        ];
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), steps);
        var game = new DarklandsGame(host, files)
        {
            Seed = () => 27,
            PlayOpening = false,
            ShowStartUp = false,
            SaveDirectory = Path.Combine(Path.GetTempPath(), "darklands-font-tests"),
        };
        var fonts = new List<int>();
        host.Read = key =>
        {
            if (key == 0x316E)
            {
                fonts.Add(game.Ui.CardWindowFont);
            }
        };
        game.Run();
        Assert.Equal(0, host.Left);
        Assert.Equal([1, 3, 1, 3], fonts);
    }

    // orig: in a battle root 0000:0000 (R014+0x3185) runs the hot keys with the font 1 and the bar's items with it put back: Alt-P is the
    // hot key's (font 1), the Game menu's Quit to DOS (F10, Down to the last item, Enter) the item's (font 3)
    [Fact]
    public void TheBattlesHotKeysRunInFontOneAndItsMenuItemsInFontThree()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost { StopAtPresent = 200 };
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(WorldData.Load(files), 7);
        var seen = new List<(int Key, int Font)>();
        ui.GameKey = key =>
        {
            seen.Add((key, ui.CardWindowFont));
            return true;
        };
        host.Keys.Enqueue(Keys.AltP);
        host.Keys.Enqueue(Keys.F10);
        // reads with no key (NoKey): the battle's looks at the buffer take some, the bar's first passes the rest, which open the Game
        // menu before the cursor moves (extra ones only idle in the bar)
        for (var i = 0; i < 4; i++)
        {
            host.Keys.Enqueue(ShotHost.NoKey);
        }
        for (var i = 0; i < 10; i++)
        {
            host.Keys.Enqueue(Keys.Down);
        }
        host.Keys.Enqueue(Keys.Enter);
        var screens = new BattleScreens(ui, () => state);
        Assert.Throws<ShotHost.Stop>(() => screens.Battle(new Darklands.Cards.BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)])));
        Assert.Equal([(Keys.AltP, 1), (Keys.AltQ, 3)], seen);
    }

    // orig: R067+0x1ECE / R072+0x1494 fill card-title and image-buffer without card-run (the previous card stays on the screen); with
    // GameOptions.CleanDrawing the card is loaded and drawn at once
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACardThatOnlyNamesItselfIsDrawnAtOnceOnlyWithCleanDrawing(bool clean)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new ShotHost(), new GameAssets(files));
        ui.Options.CleanDrawing = clean;
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        ui.Canvas.Fill(0, 0, Screen.Width, Screen.Height, 3);
        screen.NameTheCard("$Sneak00", Darklands.Cards.PictureNames.Of(0));
        var untouched = ui.Canvas.Screen.Pixels.All(p => p == 3);
        Assert.Equal(!clean, untouched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSettingsChoiceReachesGameOptionsAndIsOffByDefault(bool clean)
    {
        Assert.False(new GameOptions().CleanDrawing);
        var file = SettingsFile.Parse(clean ? """{ "display": { "cleanDrawing": true } }""" : "{}").Settings;
        var options = new GameOptions();
        options.Apply(ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, file));
        Assert.Equal(clean, options.CleanDrawing);
    }
}
