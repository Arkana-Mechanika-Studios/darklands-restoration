using System.Reflection;
using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// GitHub issue 12 (senner007): in a battle, Shift with an arrow scrolled the view at the keys' repeat (DOSBox's typematic: a step,
/// 500 ms, then a step each 33 ms), three times the edge scroll's pace (one step each 100 ms on the SDL host). With
/// GameUi.KeyScrollMilliseconds the keyboard scroll keeps the same pace, its steps counted with the edge's; 0 (the oracle tools, the
/// headless host, an older recording) scrolls at each key, as before.
/// </summary>
public sealed class KeyScrollPaceTests
{
    private static readonly (int, int, int)[] NoGroups = [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)];

    /// <summary>The pointer in the middle of the view, Shift held, a clock the test sets.</summary>
    private sealed class ShiftHost : IHost, IVideo, IInput, IClock, IAudio
    {
        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(190, 100, MouseButtons.None);
        public bool ShiftHeld => true;
        public long Milliseconds { get; set; }

        public void Present(Screen screen) { }

        public void Pump() { }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Sleep(int milliseconds) => Milliseconds += Math.Max(1, milliseconds);

        public void PlayMusic(int tune) { }

        public void StopMusic() { }

        public void PlaySound(int effect) { }
    }

    private static readonly MethodInfo KeyMethod = typeof(BattleScreen).GetMethod("Key", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(int)])!;
    private static readonly FieldInfo ViewField = typeof(BattleScreen).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>The view's centre column after Shift + Right at each of the given times (ms), with the given pace.</summary>
    private static List<int>? Columns(int pace, params long[] times)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var host = new ShiftHost();
        var ui = new GameUi(host, new GameAssets(files)) { KeyScrollMilliseconds = pace };
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        var (screen, _) = BattleSaveTests.StartBattle(ui, files, state, new BattleRequest(0x14, 863, NoGroups, Persistent: true));
        var view = (BattlefieldView)ViewField.GetValue(screen)!;
        view.CentreColumn = 10;
        var columns = new List<int>();
        foreach (var t in times)
        {
            host.Milliseconds = t;
            KeyMethod.Invoke(screen, [Keys.Right]);
            columns.Add(view.CentreColumn);
        }
        return columns;
    }

    [Fact]
    public void ShiftWithAnArrowScrollsAtMostOnceAPace()
    {
        // the typematic's keys: the press at 1000 ms, its repeats from 1500 ms every 33 ms
        long[] keys = [1000, 1500, 1533, 1566, 1599, 1632, 1665, 1698];
        if (Columns(0, keys) is not { } each || Columns(100, keys) is not { } paced)
        {
            return;
        }
        // a step each key without a pace (the original's code, the oracle and the headless host)
        Assert.Equal(new[] { 11, 12, 13, 14, 15, 16, 17, 18 }, each);
        // with the SDL host's pace: the press, then the first key 100 ms or more after the last step (1500, then 1632): about ten a second
        Assert.Equal(new[] { 11, 12, 12, 12, 12, 13, 13, 13 }, paced);
    }
}
