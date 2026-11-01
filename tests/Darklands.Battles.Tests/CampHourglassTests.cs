using Darklands.Content;
using Darklands.Core;
using Darklands.Simulation;
using Darklands.Content.Formats.Graphics;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// "Spend a day" passes the day through pass-hours (R036+0x147A), which shows the hourglass for more than two hours
/// (R043+0x3494): the timer01 dial at the card window's (0x3C, 0x32), the sand glass and the bell, day and month over it.
/// </summary>
public sealed class CampHourglassTests
{
    [Fact]
    public void SpendingADayAtTheInnShowsTheHourglass()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "move:0,190", "wait:2", "key:s", "wait:600", "key:l", "wait:5");
        if (MusicTests.Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        state.Party.Purse = Money.From(10, 0, 0);
        var dial = ui.Assets.Picture("timer01").Image;
        var scores = new List<double>();
        host.Presented = screen => scores.Add(Matching(screen, dial, CardScreen.WindowX + 0x3C, 0x32));
        var before = state.Date;
        MusicTests.Camp(ui, panel, world).Stay(state, 0);
        Assert.NotEqual(before, state.Date);
        // the camp screen alone has little of the dial; with the hourglass up most of it shows (the glass and the texts cover the rest)
        Assert.True(scores[0] < 0.5, $"before the day {scores[0]:P0} of the dial");
        Assert.True(scores.Max() > 0.8, $"at most {scores.Max():P0} of the dial");
        Assert.True(scores[^1] < 0.5, $"after the day {scores[^1]:P0} of the dial");
    }

    /// <summary>
    /// The waits of R043+0x3494 look at nothing: a key typed during them is only looked at by the next step's key-waiting-test (R043+0x3576), which
    /// then skips every later draw and wait, and exactly one key is eaten at the end (R043+0x3773); the second key stays for the screen's loop.
    /// </summary>
    [Fact]
    public void TwoKeysTypedInTheHourglassEatOnlyOneAtTheEnd()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "move:0,190", "wait:2", "key:s", "wait:3", "key:x", "key:y", "wait:600", "key:l", "wait:5");
        if (MusicTests.Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        state.Party.Purse = Money.From(10, 0, 0);
        var dial = ui.Assets.Picture("timer01").Image;
        long lastDial = -1;
        var reads = new List<(int Key, long At)>();
        host.Presented = screen =>
        {
            if (Matching(screen, dial, CardScreen.WindowX + 0x3C, 0x32) > 0.8)
            {
                lastDial = host.Milliseconds;
            }
        };
        host.Read = key => reads.Add((key, host.Milliseconds));
        MusicTests.Camp(ui, panel, world).Stay(state, 0);
        var x = reads.Single(r => r.Key == Keys.ForCharacter('x'));
        var y = reads.Single(r => r.Key == Keys.ForCharacter('y'));
        Assert.True(lastDial > 0, $"last {lastDial} x {x.At} y {y.At}");
        Assert.True(x.At <= lastDial + 32, "the first key is looked at and eaten with the hourglass");
        Assert.True(y.At > lastDial, $"x {x.At}, y {y.At}, hourglass to {lastDial}: the second key is not eaten by the hourglass: it stays waiting for the screen's loop");
    }

    /// <summary>
    /// A card that passes hours before its first text (R043+0x0A9A has drawn its picture on the off-screen page, DS:EE55 set): the hourglass draws
    /// into video memory (R043+0x3494), so the dial shows over the previous card's screen and the waits do not bring the picture up.
    /// </summary>
    [Fact]
    public void TheHourglassOfACardWhosePictureIsStillHeldShowsOverTheScreenOnShow()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "wait:400");
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        ui.Canvas.Fill(0, 0, 320, 200, 7);
        ui.Present();
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var dial = ui.Assets.Picture("timer01").Image;
        var shown = new List<(double Dial, int Behind)>();
        host.Presented = frame => shown.Add((Matching(frame, dial, CardScreen.WindowX + 0x3C, 0x32), frame.Pixels[10 * Screen.Width + 10]));
        screen.PassTime(6);
        Assert.Contains(shown, f => f.Dial > 0.8);
        Assert.All(shown, f => Assert.Equal(7, f.Behind));
        Assert.Equal(7, shown[^1].Behind);
        Assert.True(shown[^1].Dial < 0.5, "the dial is gone again at the end");
    }

    /// <summary>
    /// R043+0x37CD..0x37D3: <c>cmp ax, 7 / ja</c> leaves at once for a frame above 7 as an unsigned word, so a frame outside 0..7 (hour -3 of an
    /// edited saved game gives band -1) draws no glass, while the bell, day and month under it are still drawn; frame 7 (hour 21) draws its glass.
    /// </summary>
    [Theory]
    [InlineData(-3, false)]
    [InlineData(21, true)]
    public void AFrameAboveSevenDrawsNoGlassButTheTextsStay(int hour, bool glass)
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "move:0,190", "wait:2", "key:s", "wait:600", "key:l", "wait:5");
        if (MusicTests.Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        state.Party.Purse = Money.From(10, 0, 0);
        var dial = ui.Assets.Picture("timer01").Image;
        const int GlassX = CardScreen.WindowX + 0x6C, GlassY = 0x53, GlassWidth = 24, GlassHeight = 20;
        Screen? first = null;
        host.Presented = screen =>
        {
            // the first picture with the dial up is the first step's frame
            if (first is null && Matching(screen, dial, CardScreen.WindowX + 0x3C, 0x32) > 0.8)
            {
                first = new Screen();
                Array.Copy(screen.Pixels, first.Pixels, screen.Pixels.Length);
            }
        };
        state.Date = state.Date with { Hour = (short)hour };
        MusicTests.Camp(ui, panel, world).Stay(state, 0);
        Assert.NotNull(first);
        var bare = 0;
        for (var row = 0; row < GlassHeight; row++)
        {
            for (var column = 0; column < GlassWidth; column++)
            {
                var under = dial.Pixels[(GlassY - 0x32 + row) * dial.Width + (GlassX - CardScreen.WindowX - 0x3C) + column];
                if (first!.Pixels[(GlassY + row) * Screen.Width + GlassX + column] == (under == 0 ? (byte)5 : under))
                {
                    bare++;
                }
            }
        }
        Assert.Equal(glass, bare < GlassWidth * GlassHeight);
        var text = 0;
        for (var column = 0; column < 0x28; column++)
        {
            for (var row = 0; row < 8; row++)
            {
                text += first!.Pixels[(0x82 + row) * Screen.Width + CardScreen.WindowX + 0x48 + column] != 0 ? 1 : 0;
            }
        }
        Assert.True(text > 0, "the bell text is drawn");
    }

    /// <summary>The share of the picture's opaque pixels the screen shows at (x, y).</summary>
    private static double Matching(Screen screen, IndexedImage picture, int x, int y)
    {
        var (opaque, same) = (0, 0);
        for (var row = 0; row < picture.Height; row++)
        {
            for (var column = 0; column < picture.Width; column++)
            {
                var colour = picture.Pixels[row * picture.Width + column];
                if (colour == 0)
                {
                    continue;
                }
                opaque++;
                if (screen.Pixels[(y + row) * Screen.Width + x + column] == colour)
                {
                    same++;
                }
            }
        }
        return (double)same / opaque;
    }
}

/// <summary>The map's C / F8 (R036+0x0000): the wilderness stay with the day's poaching risk, remembered afterwards as event 0x61.</summary>
public sealed class CampOnTheMapTests
{
    [Fact]
    public void CampingFromTheMapGrowsTheRiskAndRemembersIt()
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "move:0,190", "wait:2", "key:s", "wait:600", "key:l", "wait:5");
        if (MusicTests.Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        var before = state.Date;
        MusicTests.Camp(ui, panel, world).CampInTheWild(state, 3);
        Assert.NotEqual(before, state.Date);
        Assert.True(state.Events.Started(Darklands.Core.Events.EventKinds.CampRisk));
        Assert.True(state.Events.Value(Darklands.Core.Events.EventKinds.CampRisk) > 0);
    }
}
