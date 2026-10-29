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

        public bool TryReadKey(out int key) => Keys.TryDequeue(out key);

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
    // colours 16..255 with 0xFF before the parts are copied and set_palette (root 0410:0397) writes the bytes as they are: the DAC shows the
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

    // orig: root 0000:0012..0x0033: menu_bar_run sets the card window's font to 1 and runs the hot keys with it (root 0000:0CAA); its items
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

    // orig: R067+0x1ECE / R072+0x1494 fill card_name and pic_buf without card_load (the previous card stays on the screen); with
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
