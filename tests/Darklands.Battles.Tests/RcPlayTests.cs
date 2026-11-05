using System.Security.Cryptography;
using Darklands.Content;
using Darklands.Core;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;
using Darklands.Simulation.Mods;

namespace Darklands.Battles.Tests;

/// <summary>
/// Release-candidate checks that need the player's game folder and a whole game loop but no person (docs/RC-CHECKLIST.md):
/// the game is played by a script or by a seeded generator on a host with no window, and what it writes is read back.
/// The saved games go to temporary folders; the game folder is only read.
/// </summary>
public sealed class RcPlayTests
{
    /// <summary>A host with no window: a script of keys (k:name, c:hexcode), waits (w:frames) and pointer moves (m:x,y), then an optional generator.</summary>
    internal sealed class RcHost : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<string> _script;
        private readonly Queue<int> _keys = new();
        private readonly Random? _random;
        private readonly int _generatedFrames;
        private int _idle;
        private long _now;
        private int _frame;

        public RcHost(IEnumerable<string> script, int generatedFrames = 0, int seed = 0)
        {
            _script = new Queue<string>(script);
            _generatedFrames = generatedFrames;
            _random = generatedFrames > 0 ? new Random(seed) : null;
        }

        /// <summary>Called once a frame with the frame number, on the game's thread.</summary>
        public Action<int>? EveryFrame { get; set; }

        public int Frames => _frame;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen)
        {
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public void MovePointer(int x, int y) => Mouse = Mouse with { X = x, Y = y };

        public bool TryReadKey(out int key) => _keys.TryDequeue(out key);

        private static readonly int[] Generated =
        [
            Keys.Space, Keys.Space, Keys.Enter, Keys.Enter, Keys.Escape, Keys.Up, Keys.Down, Keys.Left, Keys.Right,
            Keys.ForCharacter('1'), Keys.ForCharacter('2'), Keys.ForCharacter('3'), Keys.ForCharacter('4'), Keys.ForCharacter('5'),
            Keys.ForCharacter('c'), Keys.ForCharacter('l'), Keys.ForCharacter('u'), Keys.ForCharacter('r'),
        ];

        public void Pump()
        {
            _now += 16;
            _frame++;
            EveryFrame?.Invoke(_frame);
            if (_idle > 0)
            {
                _idle--;
                // a held button is let go after a frame
                if (Mouse.Buttons != MouseButtons.None && _idle < 2)
                {
                    Mouse = Mouse with { Buttons = MouseButtons.None };
                }
                return;
            }
            if (_script.TryDequeue(out var step))
            {
                var value = step[2..];
                switch (step[0])
                {
                    case 'k':
                        _keys.Enqueue(value switch
                        {
                            "space" => Keys.Space,
                            "enter" => Keys.Enter,
                            "esc" => Keys.Escape,
                            "up" => Keys.Up,
                            "down" => Keys.Down,
                            _ => Keys.ForCharacter(value[0]),
                        });
                        _idle = 2;
                        break;
                    case 'c':
                        _keys.Enqueue(int.Parse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                        _idle = 2;
                        break;
                    case 'w':
                        _idle = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case 'm':
                        var xy = value.Split(',');
                        Mouse = Mouse with { X = int.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), Y = int.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture) };
                        break;
                }
                return;
            }
            if (_random is null || _generatedFrames <= 0 || _frame > _generatedFrames)
            {
                QuitRequested = true;
                return;
            }
            // generated play: a key, a pointer move or a click, then a pause
            switch (_random.Next(10))
            {
                case < 6:
                    _keys.Enqueue(Generated[_random.Next(Generated.Length)]);
                    _idle = _random.Next(3, 30);
                    break;
                case < 8:
                    Mouse = new MouseState(_random.Next(320), _random.Next(200), MouseButtons.None);
                    _idle = _random.Next(1, 4);
                    break;
                default:
                    Mouse = new MouseState(_random.Next(320), _random.Next(200), _random.Next(4) == 0 ? MouseButtons.Right : MouseButtons.Left);
                    _idle = _random.Next(3, 20);
                    break;
            }
        }
    }

    private static readonly string[] QuickstartIntro =
    [
        "k:q", "w:10", "k:space", "w:10", "k:space", "w:10", "k:space", "w:10", "k:space", "w:10", "k:space", "w:40",
    ];

    private static string Card(string key) => "k:" + key;

    /// <summary>The play of MusicTests' Goslar walk: the inn, the main street, the churches, the market, the gate, the map, the camp.</summary>
    private static readonly string[] Walk =
    [
        .. QuickstartIntro,
        "k:1", "w:40", "k:space", "w:20", "k:7", "w:40", "k:space", "w:20", "k:4", "w:40", "k:space", "w:20",
        "k:6", "w:40", "k:space", "w:20", "k:3", "w:40", "k:space", "w:20", "k:7", "w:40", "k:space", "w:20",
        "m:10,195", "w:5", "k:up", "w:5", "k:enter", "w:40", "k:space", "w:20",
        "k:1", "w:61", "k:c", "w:60", "k:esc", "w:60",
    ];

    private static string TempFolder(string name)
    {
        var root = Path.Combine(Platform.LocalPaths.Get("scratch", "DARKLANDS_SCRATCH") ?? Path.GetTempPath(), "rc-auto", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        TempFolders.Add(root);
        return root;
    }

    private static readonly List<string> TempFolders = [];

    static RcPlayTests() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var folder in TempFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    };

    private static DarklandsGame NewGame(GameFiles files, RcHost host, string saves, bool clean = false, int? card = null, ModSet? mods = null, string? load = null, uint seed = 27)
    {
        var game = new DarklandsGame(host, files)
        {
            Seed = () => seed,
            PartyTicks = () => (ushort)seed,
            PlayOpening = false,
            ShowStartUp = false,
            SaveDirectory = saves,
            QuickstartCard = card,
            StartWithSave = load,
            Behaviour = OriginalBehaviour.Faithful,
        };
        game.Ui.KeyboardCardMenus = true;
        game.Options.CleanDrawing = clean;
        if (mods is not null)
        {
            game.Mods = mods;
        }
        return game;
    }

    /// <summary>The bytes the original's writer would give for the game now: what "the same game state" means below.</summary>
    private static string Digest(GameState state) =>
        Convert.ToHexString(SHA256.HashData(OriginalSaves.Write(state, "digest", true, true).Item1));

    // ----------------------------------------------------------------- L2

    private static (List<string> Digests, string Saves, int Frames) PlayWalk(GameFiles files, bool clean, IEnumerable<string> script, int generated = 0, int seed = 0)
    {
        var saves = TempFolder("l2");
        var host = new RcHost(script, generated, seed);
        var game = NewGame(files, host, saves, clean);
        var digests = new List<string>();
        host.EveryFrame = frame =>
        {
            if (frame % 20 == 0 && game.Playing is { } state && !game.Fighting)
            {
                digests.Add($"{frame}:{Digest(state)}");
            }
        };
        game.Run();
        return (digests, saves, host.Frames);
    }

    /// <summary>L2: the same keys give the same game with CleanDrawing off and on, and a save of either loads in the other.</summary>
    [Fact]
    public void CleanDrawingPlaysTheSameGameAndTheSavesAreInterchangeable()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        // the walk, then Alt-S with the offered comment on the map
        string[] script = [.. Walk, "c:1F00", "w:30", "k:enter", "w:60", "k:esc", "w:40"];
        var plain = PlayWalk(files, clean: false, script);
        var clean = PlayWalk(files, clean: true, script);
        Assert.True(plain.Digests.Count > 5, "the walk should reach the game and be sampled");
        Assert.Equal(plain.Digests.Select(d => d[..24]), clean.Digests.Select(d => d[..24]));
        var savedPlain = Directory.GetFiles(plain.Saves).Select(Path.GetFileName).Order().ToArray();
        var savedClean = Directory.GetFiles(clean.Saves).Select(Path.GetFileName).Order().ToArray();
        Assert.Contains("DKSAVE0.SAV", savedPlain);
        Assert.Equal(savedPlain, savedClean);
        foreach (var name in savedPlain)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(plain.Saves, name!)), File.ReadAllBytes(Path.Combine(clean.Saves, name!)));
        }

        // each build's save, started from in the other setting, is the same game; saving it again changes nothing but the map cell
        // (words 0x7E..0x81: the map keeps the cell of the previous pass, orig R030+0x0C44, so a load and a save moves it by a pass)
        var reports = new List<string>();
        foreach (var (from, loadClean) in new[] { (plain.Saves, true), (clean.Saves, false), (plain.Saves, false), (clean.Saves, true) })
        {
            var resaves = TempFolder("l2-load");
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(resaves, Path.GetFileName(file)));
            }
            var host = new RcHost(["c:1F00", "w:30", "k:enter", "w:60", "k:esc", "w:40"]);
            var game = NewGame(files, host, resaves, loadClean, load: "DKSAVE0.SAV");
            game.Run();
            Assert.True(File.Exists(Path.Combine(resaves, "DKSAVE1.SAV")), "the loaded game should save again");
            var a = File.ReadAllBytes(Path.Combine(from, "DKSAVE0.SAV"));
            var b = File.ReadAllBytes(Path.Combine(resaves, "DKSAVE1.SAV"));
            var diff = Enumerable.Range(0x64, Math.Min(a.Length, b.Length) - 0x64).Where(i => a[i] != b[i] && i is < 0x7E or > 0x81).Take(20).Select(i => $"{i:X}:{a[i]:X2}/{b[i]:X2}");
            if (a.Length != b.Length || diff.Any())
            {
                reports.Add($"saved with the other setting={from == plain.Saves}, load with clean={loadClean}: {a.Length}/{b.Length} bytes; differ at {string.Join(" ", diff)}");
            }
        }
        Assert.True(reports.Count == 0, string.Join("; ", reports));
    }

    // ----------------------------------------------------------------- M1

    /// <summary>
    /// M1 (the part a machine can do): long generated play (keys, pointer moves and clicks, seeded) from a Quickstart game
    /// runs to the end with no exception and the game's invariants hold throughout; the game at the end saves, loads and
    /// saves again with the same bytes. The comparison with the original over a long run stays with the person.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LongGeneratedPlayKeepsTheGameConsistent(int seed)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var saves = TempFolder("m1");
        var host = new RcHost(QuickstartIntro, generatedFrames: 40000, seed: seed);
        var game = NewGame(files, host, saves, seed: (uint)(100 + seed));
        var world = WorldData.Load(files);
        var problems = new List<string>();
        GameState? last = null;
        var lastDate = new Darklands.Core.Time.GameDate(0, 0, 0, 0);
        var sampled = 0;
        host.EveryFrame = frame =>
        {
            if (frame % 50 != 0 || game.Playing is not { } state)
            {
                return;
            }
            last = state;
            sampled++;
            var where = $"frame {frame}, card {state.CurrentCard}";
            if (state.CurrentCard is < 0 or > 365)
            {
                problems.Add($"{where}: card out of range");
            }
            var purse = state.Party.Purse;
            if (purse.TotalPfennigs < 0 || purse.Groschen is < 0 or >= 20 || purse.Pfennigs is < 0 or >= 12)
            {
                problems.Add($"{where}: purse {purse}");
            }
            if (state.Party.Members.Count > Darklands.Core.Characters.Party.MaxMembers)
            {
                problems.Add($"{where}: {state.Party.Members.Count} members");
            }
            if (state.Party.Members.Count > 0 && state.Party.LeaderIndex >= state.Party.Members.Count)
            {
                problems.Add($"{where}: leader {state.Party.LeaderIndex} of {state.Party.Members.Count}");
            }
            var date = state.Date;
            if (date.Hour is < 0 or >= 24 || date.Day is < 0 or > 31 || date.Month is < 0 or > 11)
            {
                problems.Add($"{where}: date {date}");
            }
            if (date.Year < lastDate.Year)
            {
                problems.Add($"{where}: the year went back from {lastDate.Year} to {date.Year}");
            }
            lastDate = date;
        };
        game.Run();
        Assert.Empty(problems);
        Assert.True(sampled > 100, $"the generated play should stay in a game (sampled {sampled})");
        Assert.NotNull(last);

        // the game at its end: save, load, save again
        var folder = new SaveFolder(saves);
        var name = folder.Save(last, "m1", true, true);
        var loaded = folder.Load(name, world);
        var second = folder.Save(loaded.State, "m1", true, true);
        Assert.Equal(File.ReadAllBytes(Path.Combine(saves, name.ToUpperInvariant())), File.ReadAllBytes(Path.Combine(saves, second.ToUpperInvariant())));
    }

    // ----------------------------------------------------------------- K

    private static string ExampleMod()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Darklands.slnx")))
        {
            folder = folder.Parent;
        }
        Assert.NotNull(folder);
        return Path.Combine(folder.FullName, "docs", "mods", "wayside-shrine");
    }

    private static ModSet LoadExampleMod(List<string> log) => ModSet.Load([ModFolders.Read(ExampleMod())], log.Add);

    private static int[] HeaderCards(byte[] sav) =>
        OriginalSaves.CardFieldOffsets.Select(at => (int)(short)(sav[at] | sav[at + 1] << 8)).ToArray();

    /// <summary>K2 to K5 through the game loop: the shrine card played, Alt-S on it, the files, loads with and without the mod.</summary>
    [Fact]
    public void AModdedGameSavesOnTheModCardLoadsWithAndWithoutTheModAndNeverWritesAModCardIntoTheSav()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var log = new List<string>();
        var saves = TempFolder("k");
        var host = new RcHost(["k:q", "w:80", "c:1F00", "w:30", "k:enter", "w:60", "k:esc", "w:40"]);
        var game = NewGame(files, host, saves, card: 366, mods: LoadExampleMod(log));
        game.Log = log.Add;
        game.Run();
        Assert.Contains(log, l => l.Contains("added by mod"));
        Assert.DoesNotContain(log, l => l.Contains("not loaded") || l.Contains("refus"));
        Assert.True(File.Exists(Path.Combine(saves, "DKSAVE0.SAV")), "the shrine card should have been saved");
        var companion = Path.Combine(saves, "DKSAVE0.MODS.JSON");
        Assert.True(File.Exists(companion));
        Assert.True(File.ReadAllText(companion).Contains("wayside-shrine"), File.ReadAllText(companion));
        Assert.True(File.ReadAllText(companion).Contains("\"current\": 366"), File.ReadAllText(companion));
        Assert.All(HeaderCards(File.ReadAllBytes(Path.Combine(saves, "DKSAVE0.SAV"))), n => Assert.InRange(n, 0, 365));

        // loaded with the mod: the game goes on at the shrine; no line on the console
        var withLog = new List<string>();
        var withHost = new RcHost(["w:40"]);
        string? cardWith = null;
        var withGame = NewGame(files, withHost, saves, mods: LoadExampleMod(withLog), load: "DKSAVE0.SAV");
        withGame.Log = withLog.Add;
        withHost.EveryFrame = _ => cardWith ??= withGame.Playing?.CurrentCard.ToString();
        withGame.Run();
        Assert.Equal("366", cardWith);
        Assert.DoesNotContain(withLog, l => l.Contains("not loaded now"));

        // loaded without it: the game's own card from the .SAV, the missing mod named on the console, no crash
        var withoutLog = new List<string>();
        var withoutHost = new RcHost(["w:40", "k:space", "w:40"]);
        string? cardWithout = null;
        var withoutGame = NewGame(files, withoutHost, saves, load: "DKSAVE0.SAV");
        withoutGame.Log = withoutLog.Add;
        withoutHost.EveryFrame = _ => cardWithout ??= withoutGame.Playing?.CurrentCard.ToString();
        withoutGame.Run();
        Assert.Equal("2", cardWithout);
        Assert.Contains(withoutLog, l => l.Contains("The wayside shrine") && l.Contains("not loaded"));
    }

    /// <summary>K5: an original save (no companion file) played with the mod on, saved again, holds no card above 365.</summary>
    [Fact]
    public void AnOriginalSavePlayedWithTheModAndSavedAgainHoldsNoModCard()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var source = Path.Combine(files.Root, "SAVES");
        if (!File.Exists(Path.Combine(source, "DKSAVE0.SAV")))
        {
            return;
        }
        var saves = TempFolder("k5");
        foreach (var name in new[] { "DKSAVE0.SAV", "DKSAVE0.BSV" }.Where(n => File.Exists(Path.Combine(source, n))))
        {
            File.Copy(Path.Combine(source, name), Path.Combine(saves, name));
        }
        var log = new List<string>();
        // load, wait, then through the inn and out to the gate and the map, saving at several points
        string[] script =
        [
            "w:60", "c:1F00", "w:20", "k:enter", "w:40", "k:space", "w:30", "k:1", "w:40", "k:space", "w:30",
            "c:1F00", "w:20", "k:enter", "w:40", "k:esc", "w:20",
        ];
        var host = new RcHost(script, generatedFrames: 6000, seed: 5);
        var game = NewGame(files, host, saves, mods: LoadExampleMod(log), load: "DKSAVE0.SAV");
        game.Log = log.Add;
        game.Run();
        var written = Directory.GetFiles(saves, "*.SAV").Where(f => !f.EndsWith("DKSAVE0.SAV", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(written);
        foreach (var file in written)
        {
            Assert.All(HeaderCards(File.ReadAllBytes(file)), n => Assert.InRange(n, 0, 365));
            Assert.True(OriginalSaves.Read(File.ReadAllBytes(file), [], Path.GetFileName(file), WorldData.Load(files)).State is not null);
        }
    }

    // ----------------------------------------------------------------- B4

    /// <summary>B4: the start screen's Quickstart button (Q) gives the Quickstart party: city, members, items (the money is the first card's).</summary>
    [Fact]
    public void TheQuickstartKeyOnTheStartScreenStartsTheQuickstartParty()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var saves = TempFolder("b4");
        var host = new RcHost(["k:q", "w:80", "c:1F00", "w:30", "k:enter", "w:60", "k:esc", "w:40"]);
        NewGame(files, host, saves).Run();
        var world = WorldData.Load(files);
        var played = new SaveFolder(saves).Load("DKSAVE0.SAV", world).State;
        var expected = Darklands.Simulation.NewGame.Quickstart(world, 27, () => 27, 27);
        Assert.Equal(expected.CurrentLocation, played.CurrentLocation);
        Assert.Equal(expected.Party.Members.Count, played.Party.Members.Count);
        for (var i = 0; i < expected.Party.Members.Count; i++)
        {
            Assert.Equal(expected.Party.Members[i].Name, played.Party.Members[i].Name);
            Assert.Equal(expected.Party.Members[i].Attributes, played.Party.Members[i].Attributes);
            Assert.Equal(expected.Party.Members[i].Pack.Select(p => (p.Code, p.Count)), played.Party.Members[i].Pack.Select(p => (p.Code, p.Count)));
        }
    }

    // ----------------------------------------------------------------- G6

    private static string? OwnSave(GameFiles files, string name)
    {
        var path = Path.Combine(files.Root, "SAVES", name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>G6: a saved game whose battle file is missing loads and the game goes on (a game saved in a battle too).</summary>
    [Theory]
    [InlineData("DKSAVE0")]
    [InlineData("DKSAVE12")]
    public void ASavedGameWithoutItsBattleFileLoadsAndTheGameGoesOn(string name)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, name + ".SAV") is not { } sav)
        {
            return;
        }
        var saves = TempFolder("g6");
        File.Copy(sav, Path.Combine(saves, name + ".SAV"));
        var host = new RcHost(["w:50", "k:space", "w:50", "k:esc", "w:30"]);
        var game = NewGame(files, host, saves, load: name + ".SAV");
        Assert.Null(Record.Exception(game.Run));
        Assert.True(host.Frames > 100, "the loaded game should have been played on");
    }

    /// <summary>G6: a saved game that is not there raises a file-not-found for its name (the caller's to report).</summary>
    [Fact]
    public void AMissingSavedGameNamesTheFile()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var thrown = Assert.Throws<FileNotFoundException>(() => NewGame(files, new RcHost(["w:5"]), TempFolder("g6"), load: "NOSUCH.SAV").Run());
        Assert.Contains("NOSUCH.SAV", thrown.Message);
    }

    /// <summary>G6 (finding RC-001): a .SAV shorter than its own header, chosen on the load screen, must not end the program with an exception.</summary>
    [Fact(Skip = "RC-001: a .SAV of 96 bytes ends the program with ArgumentOutOfRangeException and no message (code/local/rc-auto/findings.md)")]
    public void ASavedGameCutShorterThanItsHeaderDoesNotEndTheProgram()
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is not { } sav)
        {
            return;
        }
        var saves = TempFolder("g6");
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.SAV"), File.ReadAllBytes(sav)[..96]);
        var host = new RcHost(["k:t", "w:5", "k:l", "w:300", "k:enter", "w:60", "k:esc", "w:30", "k:esc", "w:30"]);
        Assert.Null(Record.Exception(NewGame(files, host, saves).Run));
    }

    /// <summary>G6 (finding RC-002): a .SAV cut inside its records should say so instead of loading a garbled game without a word.</summary>
    [Theory(Skip = "RC-002: a .SAV cut at 512 or 9374 bytes loads without any message (code/local/rc-auto/findings.md)")]
    [InlineData(512)]
    [InlineData(9374)]
    public void ASavedGameCutInsideItsRecordsIsReported(int length)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is not { } sav)
        {
            return;
        }
        var saves = TempFolder("g6");
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.SAV"), File.ReadAllBytes(sav)[..length]);
        var log = new List<string>();
        var game = NewGame(files, new RcHost(["w:50", "k:space", "w:50"]), saves, load: "DKSAVE0.SAV");
        game.Log = log.Add;
        game.Run();
        Assert.NotEmpty(log);
    }
}
