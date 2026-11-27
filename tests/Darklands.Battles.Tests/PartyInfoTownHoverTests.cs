using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Core.World;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The party information's small map (GitHub issue 8): in the player's game (OriginalBehaviour.FixPartyInfoTownHover) the tip of the
/// hand on a town's dot shows that town, for every town; the original (the flag off, as the oracle plays) seeks the town with another
/// scale than the dots' and 3 pixels off the pointer, and misses Erfurt and Wien on their dots.
/// </summary>
public sealed class PartyInfoTownHoverTests
{
    /// <summary>
    /// The towns' dots in pics\ptystats.pic, in the location list's order, found and matched apart from the C# (a separate script over
    /// the screen's frames of two saved games, each dot from a frame whose party marker stands elsewhere: lone red pixels and the
    /// centres of 3 × 3 red rings, matched one to one with a least-squares scale).
    /// </summary>
    private static readonly (int X, int Y)[] Dots =
    [
        (216, 98), (240, 77), (261, 76), (255, 78), (241, 83), (249, 90), (255, 91), (261, 88), (268, 86), (280, 98),
        (243, 94), (247, 99), (232, 98), (223, 98), (225, 112), (239, 107), (244, 111), (211, 122), (212, 106), (210, 111),
        (220, 116), (204, 115), (210, 118), (214, 118), (207, 130), (215, 123), (215, 130), (219, 123), (223, 122), (240, 123),
        (230, 120), (256, 115), (265, 117), (252, 126), (263, 124), (270, 132), (246, 117), (284, 112), (276, 131), (312, 82),
        (315, 85), (315, 101), (303, 108), (266, 112), (272, 109), (276, 99), (271, 104), (204, 148), (220, 136), (224, 143),
        (231, 140), (210, 146), (227, 148), (223, 155), (222, 165), (222, 172), (233, 161), (241, 146), (247, 145), (248, 150),
        (261, 160), (247, 159), (230, 170), (237, 157), (241, 167), (249, 168), (254, 172), (272, 165), (268, 140), (276, 143),
        (283, 143), (292, 145), (306, 126), (308, 147), (302, 153), (313, 148), (219, 178), (229, 180), (233, 178), (241, 135),
        (260, 181), (268, 175), (280, 170), (282, 172), (299, 170), (306, 169), (296, 178), (305, 97), (231, 152), (202, 159),
        (286, 127), (206, 109),
    ];

    private const int Erfurt = 33, Wien = 84;

    /// <summary>The town box above the map, as both the pointer's and Tab's drawing clear it (the smaller of the two clears).</summary>
    private const int BoxX = 0xC8, BoxY = 0x12, BoxWidth = 0x78, BoxHeight = 0x32;

    [Fact]
    public void TheTipOfTheHandOnATownsDotShowsThatTown()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        Assert.Equal(Location.CityCount, Dots.Length);
        var (hovered, tabbed) = Run(files, OriginalBehaviour.DataSafe);
        var missed = Enumerable.Range(0, Dots.Length).Where(t => !hovered[t].SequenceEqual(tabbed[t])).ToList();
        Assert.Empty(missed);
        // the boxes tell the towns apart (two towns are called Freiberg; their standing lines differ or not, the rest is unique)
        Assert.True(tabbed.Select(Convert.ToBase64String).Distinct().Count() >= Dots.Length - 1);
    }

    [Fact]
    public void TheOriginalMissesErfurtAndWienOnTheirDots()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (hovered, tabbed) = Run(files, OriginalBehaviour.Faithful);
        Assert.False(hovered[Erfurt].SequenceEqual(tabbed[Erfurt]));
        Assert.False(hovered[Wien].SequenceEqual(tabbed[Wien]));
    }

    [Fact]
    public void TheDotsFoundInThePictureAreTheTownsOwn()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        List<(int X, int Y)>? found = null;
        var host = new ScriptHost(_ => (5, 5, Keys.Escape));
        var (ui, state, screens) = Open(files, host, OriginalBehaviour.DataSafe);
        // the screen as drawn, at its first poll, the party in the wilderness at the map's corner, so its marker covers no dot (the
        //   screen itself finds the dots before it draws the marker)
        state.CurrentLocation = GameState.Wilderness;
        state.MapPosition = new MapPosition(0, 0);
        host.OnPoll = _ => found ??= SmallMapDots.FindDots(ui.Canvas, 0xC6, 0x4A, 0x140, 0xC3);
        screens.PartyInfo();
        Assert.NotNull(found);
        Assert.Equal(Dots, SmallMapDots.Match(SmallMapDots.Towns(state), found, 116.0 / 327, 0xC8, 106.0 / 931, 0x46));
    }

    [Fact]
    public void TheNearestDotWithinReachIsTheTown()
    {
        (int X, int Y)[] dots = [(10, 10), (14, 10), (40, 40)];
        Assert.Equal(0, SmallMapDots.TownAt(dots, 10, 10));
        Assert.Equal(0, SmallMapDots.TownAt(dots, 11, 12));
        Assert.Equal(1, SmallMapDots.TownAt(dots, 13, 10));
        // halfway: the first
        Assert.Equal(0, SmallMapDots.TownAt(dots, 12, 10));
        Assert.Equal(2, SmallMapDots.TownAt(dots, 44, 40));
        Assert.Equal(-1, SmallMapDots.TownAt(dots, 45, 40));
        Assert.Equal(-1, SmallMapDots.TownAt(dots, 25, 25));
    }

    /// <summary>
    /// The town box with the hand's tip on each town's dot in turn, then with Tab stepping through the towns (Tab shows the town it
    /// names, through its own path), each in the location list's order.
    /// </summary>
    private static (byte[][] Hovered, byte[][] Tabbed) Run(GameFiles files, OriginalBehaviour behaviour)
    {
        const int Polls = 3;
        var n = Dots.Length;
        var hovered = new byte[n][];
        var tabbed = new byte[n][];
        var host = new ScriptHost(poll =>
        {
            var phase = poll / Polls;
            if (phase < n)
            {
                return (Dots[phase].X - SmallMapDots.TipDx, Dots[phase].Y - SmallMapDots.TipDy, 0);
            }
            if (phase < 2 * n)
            {
                return (5, 5, poll % Polls == 0 ? Keys.Tab : 0);
            }
            return (5, 5, Keys.Escape);
        });
        var (ui, state, screens) = Open(files, host, behaviour);
        var firstTab = state.InCity ? state.CurrentLocation : LocationRules.NearestCity(state, state.MapPosition.X, state.MapPosition.Y);
        host.OnPoll = poll =>
        {
            if (poll % Polls != Polls - 1)
            {
                return;
            }
            var phase = poll / Polls;
            if (phase < n)
            {
                hovered[phase] = Box(ui.Canvas);
            }
            else if (phase < 2 * n)
            {
                tabbed[(firstTab + phase - n) % n] = Box(ui.Canvas);
            }
        };
        screens.PartyInfo();
        Assert.All(hovered, Assert.NotNull);
        Assert.All(tabbed, Assert.NotNull);
        return (hovered, tabbed);
    }

    private static byte[] Box(Canvas canvas)
    {
        var box = new byte[BoxWidth * BoxHeight];
        for (var y = 0; y < BoxHeight; y++)
        {
            for (var x = 0; x < BoxWidth; x++)
            {
                box[y * BoxWidth + x] = canvas[BoxX + x, BoxY + y];
            }
        }
        return box;
    }

    private static (GameUi Ui, GameState State, GameScreens2 Screens) Open(GameFiles files, ScriptHost host, OriginalBehaviour behaviour)
    {
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        state.Behaviour = behaviour;
        return (ui, state, new GameScreens2(ui, new PartyPanel(ui.Assets), state, world, new NoScreens()));
    }

    /// <summary>A host whose pointer and keys follow a script by the screen's polls (each key read is one poll).</summary>
    private sealed class ScriptHost(Func<int, (int X, int Y, int Key)> script) : IHost, IVideo, IInput, IClock
    {
        private int _poll;
        private long _now;

        public Action<int>? OnPoll { get; set; }
        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => _poll > 5000;
        public MouseState Mouse => new(script(_poll).X, script(_poll).Y, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen)
        {
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            OnPoll?.Invoke(_poll);
            key = script(_poll).Key;
            _poll++;
            _now += 10;
            return key != 0;
        }

        public void Pump()
        {
        }
    }

    private sealed class NoScreens : IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
        public int Battle(BattleRequest request) => 0;
    }
}
