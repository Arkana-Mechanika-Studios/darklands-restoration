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
