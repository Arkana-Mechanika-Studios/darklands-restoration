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

        /// <summary>The last frame handed to the display: its pixels and palette (copies).</summary>
        public (byte[] Pixels, uint[] Palette)? Shown { get; private set; }

        public void Present(Screen screen) => Shown = ((byte[])screen.Pixels.Clone(), (uint[])screen.Palette.Clone());

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

    private static DarklandsGame NewGame(GameFiles files, RcHost host, string saves, bool clean = false, int? card = null, ModSet? mods = null, string? load = null, uint seed = 27, OriginalBehaviour? behaviour = null)
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
            Behaviour = behaviour ?? OriginalBehaviour.Faithful,
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

    /// <summary>
    /// The battle globals (GenerationProcessMemory: DS:E051, floor-picture, the party's sizes, the alarm trigger, the walk spot, DS:9BE5,
    /// the effects' modifiers) are the program's: an Alt-L plays the loaded game with new battle screens but the same globals.
    /// </summary>
    [Fact]
    public void ALoadKeepsTheProgramsBattleGlobals()
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is not { } sav)
        {
            return;
        }
        var saves = TempFolder("carry");
        File.Copy(sav, Path.Combine(saves, "DKSAVE0.SAV"));
        // Alt-L, Y to the question, L on the load screen (DKSAVE0, the first line)
        var host = new RcHost(["w:50", "c:2600", "w:30", "k:y", "w:30", "k:l", "w:80", "k:esc", "w:30"]);
        var game = NewGame(files, host, saves, load: "DKSAVE0.SAV");
        var screens = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var memories = new HashSet<object>(ReferenceEqualityComparer.Instance);
        host.EveryFrame = _ =>
        {
            if (game.Battles is { } battles)
            {
                screens.Add(battles);
                memories.Add(battles.GenerationMemory);
            }
        };
        game.Run();
        Assert.True(screens.Count >= 2, "the load should play the loaded game with new battle screens");
        Assert.Single(memories);
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

    private static string CutSave(GameFiles files, int length, bool withBattleFile = true)
    {
        var saves = TempFolder("g6");
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.SAV"), File.ReadAllBytes(OwnSave(files, "DKSAVE0.SAV")!)[..length]);
        if (withBattleFile && OwnSave(files, "DKSAVE0.BSV") is { } bsv)
        {
            File.Copy(bsv, Path.Combine(saves, "DKSAVE0.BSV"));
        }
        return saves;
    }

    private static string FolderDigest(string saves) =>
        string.Join("|", Directory.GetFiles(saves).Order().Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));

    /// <summary>
    /// G6 (RC-001, RC-002; original: the load reads a short file as far as it goes, a header cut at 0 or 96 bytes ends in DOS with a file error
    /// ("pics\shield.pic: No such file or directory"), a cut inside the records plays on from garbage; code/local/rc-auto/badsave-original.md):
    /// with the data-safety fix the load screen refuses a .SAV that ends inside its header, records or location table, says so, changes
    /// nothing and stays on the load screen.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(96)]
    [InlineData(0xEF)]
    [InlineData(512)]
    [InlineData(9374)]
    public void ASavedGameCutShortIsRefusedOnTheLoadScreenAndTheGameStaysThere(int length)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null)
        {
            return;
        }
        var saves = CutSave(files, length);
        var before = FolderDigest(saves);
        var host = new RcHost(["k:t", "w:5", "k:l", "w:300", "k:enter", "w:60", "k:esc", "w:30", "k:esc", "w:30"]);
        var game = NewGame(files, host, saves, behaviour: OriginalBehaviour.DataSafe);
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        Assert.Null(Record.Exception(game.Run));
        Assert.Contains(log, l => l.Contains("damaged"));
        Assert.False(played, "no game should have been started from the damaged file");
        Assert.Equal(before, FolderDigest(saves));
    }

    /// <summary>The same with the file named at the start (StartWithSave): refused, the start screen follows, nothing is played or written.</summary>
    [Theory]
    [InlineData(96)]
    [InlineData(512)]
    [InlineData(9374)]
    public void ASavedGameCutShortIsRefusedAtTheStartToo(int length)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null)
        {
            return;
        }
        var saves = CutSave(files, length);
        var before = FolderDigest(saves);
        var host = new RcHost(["w:20", "k:enter", "w:20"]);
        var game = NewGame(files, host, saves, load: "DKSAVE0.SAV", behaviour: OriginalBehaviour.DataSafe);
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        Assert.Null(Record.Exception(game.Run));
        Assert.Contains(log, l => l.Contains("damaged"));
        Assert.False(played);
        Assert.Equal(before, FolderDigest(saves));
    }

    private static string CutBattleFile(GameFiles files, int length)
    {
        var saves = TempFolder("g6");
        File.Copy(OwnSave(files, "DKSAVE0.SAV")!, Path.Combine(saves, "DKSAVE0.SAV"));
        File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.BSV"), File.ReadAllBytes(OwnSave(files, "DKSAVE0.BSV")!)[..length]);
        return saves;
    }

    /// <summary>
    /// G6 (the battle file; original: the restore reads the .BSV in every game and ends in DOS with "EOF-..." or "Corrupt file-..." on an empty,
    /// short or corrupt one; code/local/rc-auto/badbsv-original.md): with the fix a saved game whose .BSV is cut is refused on both routes, as a
    /// cut .SAV is, and nothing is written.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(141, true)]
    [InlineData(182, true)]
    [InlineData(0, false)]
    [InlineData(141, false)]
    public void ASavedGameWithACutBattleFileIsRefusedWithTheFix(int length, bool fromTheStart)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null || OwnSave(files, "DKSAVE0.BSV") is null)
        {
            return;
        }
        var saves = CutBattleFile(files, length);
        var before = FolderDigest(saves);
        var host = fromTheStart ? new RcHost(["w:20", "k:enter", "w:20"]) : new RcHost(["k:t", "w:5", "k:l", "w:300", "k:enter", "w:60", "k:esc", "w:30"]);
        var game = NewGame(files, host, saves, load: fromTheStart ? "DKSAVE0.SAV" : null, behaviour: OriginalBehaviour.DataSafe);
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        Assert.Null(Record.Exception(game.Run));
        Assert.Contains(log, l => l.Contains("damaged"));
        Assert.False(played);
        Assert.Equal(before, FolderDigest(saves));
    }

    /// <summary>Faithful keeps today's path: an empty .BSV is taken for a missing one and the game plays on (the original ends in DOS); a cut one throws.</summary>
    [Fact]
    public void FaithfulAnEmptyBattleFileIsTakenForMissingAndACutOneThrowsAsBefore()
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null || OwnSave(files, "DKSAVE0.BSV") is null)
        {
            return;
        }
        var host = new RcHost(["w:50", "k:space", "w:50"]);
        var game = NewGame(files, host, CutBattleFile(files, 0), load: "DKSAVE0.SAV");
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        game.Run();
        Assert.True(played);
        Assert.DoesNotContain(log, l => l.Contains("damaged"));
        Assert.Throws<InvalidDataException>(() => NewGame(files, new RcHost(["w:50"]), CutBattleFile(files, 141), load: "DKSAVE0.SAV").Run());
    }

    private static int Brightness(uint colour) => (int)((colour >> 16 & 0xFF) + (colour >> 8 & 0xFF) + (colour & 0xFF)) / 3;

    /// <summary>
    /// The notice of a damaged saved game given on the command line (nothing drawn before it) is readable: its box and text are drawn in the
    /// load screen's colours, not dark on black. Every shown frame while the notice waits is checked.
    /// </summary>
    [Theory]
    [InlineData(96, false)]
    [InlineData(512, false)]
    [InlineData(0, true)]
    [InlineData(141, true)]
    public void TheNoticeOfADamagedSavedGameGivenAtTheStartIsNotDarkOnBlack(int length, bool battleFile)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null || OwnSave(files, "DKSAVE0.BSV") is null)
        {
            return;
        }
        var host = new RcHost(["w:30", "k:enter", "w:10"]);
        var game = NewGame(files, host, battleFile ? CutBattleFile(files, length) : CutSave(files, length), load: "DKSAVE0.SAV", behaviour: OriginalBehaviour.DataSafe);
        var log = new List<string>();
        game.Log = log.Add;
        var sinceNotice = -1;
        (byte[] Pixels, uint[] Palette)? frame = null;
        host.EveryFrame = n =>
        {
            if (sinceNotice < 0 && log.Any(l => l.Contains("damaged")))
            {
                sinceNotice = 0;
            }
            if (sinceNotice >= 0 && ++sinceNotice == 10)
            {
                frame = host.Shown;
            }
        };
        game.Run();
        Assert.NotNull(frame);
        var (pixels, palette) = frame!.Value;
        // the box (colour 0xFF) and the text (colour 1) of MessageBox: both seen, the box not black, the text clearly brighter or darker than it
        Assert.Contains(pixels, p => p == 0xFF);
        Assert.Contains(pixels, p => p == MessageBox.TextColor);
        Assert.True(Brightness(palette[0xFF]) >= 40, "the notice's box is drawn black");
        Assert.True(Math.Abs(Brightness(palette[0xFF]) - Brightness(palette[MessageBox.TextColor])) >= 60, "the notice's text does not stand out of its box");
    }

    /// <summary>The notice is a sentence the game's own boxes show, one line a box.</summary>
    [Fact]
    public void TheNoticeOfADamagedSavedGameReadsAsASentence()
    {
        Assert.Equal("This saved game is damaged and cannot be loaded.", DarklandsGame.DamagedSavedGame[0]);
        Assert.All(DarklandsGame.DamagedSavedGame, l => Assert.InRange(l.Length, 1, 60));
    }

    /// <summary>
    /// Faithful (the testers and oracles; a documented approximation of the original, which leaves a header cut at 0 or 96 bytes for DOS with a
    /// file error and plays on from what is left of its buffers on a cut inside the records): a short header goes on and ends the program
    /// with the exception of the first card, never a refusal.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(96)]
    public void FaithfulALoadOfAHeaderCutShortGoesOnAndEndsTheProgramLaterAsBefore(int length)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null)
        {
            return;
        }
        var host = new RcHost(["k:t", "w:5", "k:l", "w:300", "k:enter", "w:60", "k:esc", "w:30", "k:esc", "w:30"]);
        var game = NewGame(files, host, CutSave(files, length));
        var log = new List<string>();
        game.Log = log.Add;
        Assert.Throws<ArgumentOutOfRangeException>(game.Run);
        Assert.DoesNotContain(log, l => l.Contains("damaged"));
    }

    /// <summary>Faithful: a file cut inside its records loads with no message (as in the original) and the game is played on.</summary>
    [Theory]
    [InlineData(512)]
    [InlineData(9374)]
    public void FaithfulALoadOfAFileCutInsideItsRecordsPlaysOnWithoutAMessage(int length)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, "DKSAVE0.SAV") is null)
        {
            return;
        }
        var host = new RcHost(["w:50", "k:space", "w:50"]);
        var game = NewGame(files, host, CutSave(files, length), load: "DKSAVE0.SAV");
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        game.Run();
        Assert.True(played);
        Assert.DoesNotContain(log, l => l.Contains("damaged"));
    }

    /// <summary>With the fix a whole file still loads and plays: real saves of the folder, one without its battle file, and one cut only in its cache.</summary>
    [Theory]
    [InlineData("DKSAVE0.SAV", false)]
    [InlineData("DKSAVE0.SAV", true)]
    [InlineData("DKSAVE12.SAV", false)]
    [InlineData("DKSAVE1.SAV", false)]
    [InlineData("DKSAVE17.SAV", false)]
    public void WithTheFixAWholeSavedGameStillLoadsAndPlays(string name, bool cacheCut)
    {
        if (GameFolder.Files is not { } files || OwnSave(files, name) is not { } sav)
        {
            return;
        }
        var saves = TempFolder("g6");
        var bytes = File.ReadAllBytes(sav);
        File.WriteAllBytes(Path.Combine(saves, name), cacheCut ? bytes[..^20] : bytes);
        var host = new RcHost(["w:50", "k:space", "w:50"]);
        var game = NewGame(files, host, saves, load: name, behaviour: OriginalBehaviour.DataSafe);
        var log = new List<string>();
        game.Log = log.Add;
        var played = false;
        host.EveryFrame = _ => played |= game.Playing is not null;
        game.Run();
        Assert.True(played, "the whole saved game should have loaded");
        Assert.DoesNotContain(log, l => l.Contains("damaged"));
    }
}
