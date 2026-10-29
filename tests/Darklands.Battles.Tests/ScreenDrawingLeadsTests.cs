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

        public void Present(Screen screen)
        {
            screen.Pixels.CopyTo(Shown, 0);
            if (++_presents == StopAtPresent)
            {
                throw new Stop();
            }
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
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
