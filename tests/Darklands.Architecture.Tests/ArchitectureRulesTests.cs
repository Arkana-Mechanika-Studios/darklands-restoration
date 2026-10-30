namespace Darklands.Architecture.Tests;

/// <summary>
/// The dependency direction of docs/ARCHITECTURE-RULES.md, checked on the compiled assemblies. Each rule holds for the code as it is;
/// the places that break a rule today are listed as known exceptions with their reason, so the test is green and blocks every new
/// break. A known exception that no longer breaks the rule fails the test too: take it off the list.
/// </summary>
public sealed class ArchitectureRulesTests
{
    private const string Core = "Darklands.Core";
    private const string Persistence = "Darklands.Persistence";
    private const string Content = "Darklands.Content";
    private const string Cards = "Darklands.Cards";
    private const string Simulation = "Darklands.Simulation";
    private const string Platform = "Darklands.Platform";
    private const string Rendering = "Darklands.Rendering";
    private const string Audio = "Darklands.Audio";
    private const string Mt32 = "Darklands.Audio.Mt32";
    private const string Launcher = "Darklands.Launcher";
    private const string DevConsole = "Darklands.DevConsole";
    private const string Presentation = "Darklands.Presentation";
    private const string Headless = "Darklands.Host.Headless";
    private const string Sdl = "DarklandsRP"; // Darklands.Host.Sdl builds the program players start

    /// <summary>The game: its model, rules, files and saved games. It knows nothing of screens, devices or hosts.</summary>
    private static readonly string[] Game = [Core, Persistence, Content, Cards, Simulation];

    /// <summary>What each product assembly may reference among the product assemblies (dependencies point down, ARCHITECTURE.md "Layers").</summary>
    private static readonly Dictionary<string, string[]> MayReference = new()
    {
        [Core] = [],
        [Persistence] = [Core],
        [Content] = [Core],
        [Cards] = [Core, Content],
        [Simulation] = [Core, Content, Cards],
        [Platform] = [],
        [Rendering] = [Platform],
        [Audio] = [Platform, Content, Core],
        [Mt32] = [Audio, Platform, Content, Core],
        [Launcher] = [Platform],
        [DevConsole] = [Core],
        [Presentation] = [Core, Persistence, Content, Cards, Simulation, Platform, Audio],
        [Headless] = [Core, Persistence, Content, Cards, Simulation, Platform, Audio, Mt32, Rendering, DevConsole, Presentation],
        [Sdl] = [Core, Persistence, Content, Cards, Simulation, Platform, Audio, Mt32, Rendering, DevConsole, Launcher, Presentation],
    };

    [Fact]
    public void EveryProductAssemblyHasARow()
    {
        Assert.Equal(Dependencies.Products.Order(), MayReference.Keys.Order());
    }

    [Fact]
    public void EachAssemblyReferencesOnlyTheLayersBelowIt()
    {
        var breaks = new List<string>();
        foreach (var name in Dependencies.Products)
        {
            foreach (var reference in References(name))
            {
                if (!MayReference[name].Contains(reference))
                {
                    breaks.Add($"{name} -> {reference}");
                }
            }
        }
        Assert.True(breaks.Count == 0, "references against the layers:\n" + string.Join("\n", breaks));
    }

    /// <summary>The game never references a screen, a device, a host or a host feature; nothing references a host.</summary>
    [Fact]
    public void TheGameDependsOnNoPresentationAndNoHost()
    {
        string[] above = [Platform, Rendering, Audio, Mt32, Launcher, DevConsole, Presentation, Headless, Sdl];
        foreach (var name in Game)
        {
            Assert.Empty(References(name).Intersect(above));
        }
        foreach (var name in Dependencies.Products)
        {
            Assert.DoesNotContain(Headless, References(name));
            Assert.DoesNotContain(Sdl, References(name));
            if (name is not (Headless or Sdl))
            {
                // the developer console, the launcher and the HD backend are host features
                Assert.Empty(References(name).Intersect([DevConsole, Launcher, Rendering]));
            }
        }
    }

    /// <summary>
    /// The rules are deterministic and do no I/O (ARCHITECTURE.md "Principles"): no file system, no console, no wall clock, no
    /// System.Random in the model, the card scripts and the simulation. Time and chance come in through IRandomSource and the
    /// game's own clock; files come in through Content.
    /// </summary>
    [Fact]
    public void TheRulesDoNoIoAndReadNoClock()
    {
        var breaks = Owners([Core, Cards, Simulation], use => IsIoOrClock(use));
        Check(breaks, KnownIoOrClock, "I/O or the wall clock in the rules");
    }

    /// <summary>The places in the rules that do I/O or read the wall clock today, and why.</summary>
    private static readonly Dictionary<string, string> KnownIoOrClock = new()
    {
        ["Darklands.Core.Randomness.MscRandom"] = "tester trace DARKLANDS_RANDOM_TRACE to the console, off in play (null unless the variable is set)",
        ["Darklands.Core.Battles.Tactics.TacticalBattle"] = "tester trace DARKLANDS_STEP_TRACE to the console, off in play (null unless the variable is set)",
    };

    /// <summary>
    /// Views only show and ask: a screen draws, reads the player and hands the choice to the rules; it does not write the game's
    /// model itself (no field store, no property setter on a Darklands.Core type). The screens that still apply a rule inline are
    /// listed with the rule they apply; each comes off the list when the rule moves into Core or Simulation (with the screen's
    /// move onto the command layer, docs/COMMANDS.md).
    /// </summary>
    [Fact]
    public void ViewsDoNotWriteTheGameModel()
    {
        var breaks = Owners([Presentation], use => use.Write && Dependencies.AssemblyOf(use.Target) == Core);
        Check(breaks, KnownModelWriters, "screens that write the game model");
    }

    private static readonly Dictionary<string, string> KnownModelWriters = new()
    {
        ["Darklands.Presentation.DarklandsGame"] = "the game's top loop (the card, the map position, a battle to resume, the clock seed, a loaded game's orders), not a view; goes to Simulation with the screens",
        ["Darklands.Presentation.Battles.BattleScreen"] = "the battle's whole loop runs in the screen (set-up, clock, orders, combatants, the cursor keys); the battle screen refactor is LAST (PLAN-1.0)",
        ["Darklands.Presentation.BattleScreens"] = "battle set-up and end inline (battles fought, the orders' flags)",
        ["Darklands.Presentation.CharacterCreationScreen"] = "creation builds the new party and the starting state inline (figures, roster place, date, city)",
        ["Darklands.Presentation.PartyRosterScreen"] = "the roster menu's selection and record buffer kept in the model's PartyMenuState",
        ["Darklands.Presentation.CampScreen"] = "the camp's day loop inline (risk, teacher, hourglass) and the member chosen as leader",
        ["Darklands.Presentation.AlchemyScreen"] = "the batch count the player chooses is stored into the brewing rule's batch",
        ["Darklands.Presentation.OverlandScreen"] = "map travel inline (position, location, leave mode)",
        ["Darklands.Presentation.HotKeys"] = "option toggles (difficulty, saintly changes) and Ctrl-F1..F5's new leader written directly",
        ["Darklands.Presentation.BarterScreen"] = "choosing whose page is shown writes the party's leader, as the original's shop does (and puts it back)",
        ["Darklands.Presentation.ItemStoreScreen"] = "choosing whose page is shown writes the party's leader, as the original's shop does",
        ["Darklands.Presentation.LootScreen"] = "choosing whose page is shown writes the party's leader",
        ["Darklands.Presentation.CharacterSheetScreen"] = "the sheet's 'make leader' and member choice write the party's leader",
        ["Darklands.Presentation.SaveGameDialog"] = "a save names the battle file in the state before writing",
    };

    /// <summary>Saved games are read and written by the screens that save and load, and by the game's start, nowhere else in the views.</summary>
    [Fact]
    public void OnlyTheSaveAndLoadPlacesUseTheSavedGames()
    {
        var breaks = Owners([Presentation], use => Dependencies.AssemblyOf(use.Target) == Persistence);
        Check(breaks, SaveAndLoadPlaces, "views that use Darklands.Persistence", KnownPersistenceUsers);
    }

    /// <summary>The places that save and load by design.</summary>
    private static readonly HashSet<string> SaveAndLoadPlaces =
    [
        "Darklands.Presentation.DarklandsGame", "Darklands.Presentation.LoadGameScreen", "Darklands.Presentation.SaveGameDialog",
    ];

    private static readonly Dictionary<string, string> KnownPersistenceUsers = new()
    {
        ["Darklands.Presentation.Battles.BattleScreen"] = "reads and writes the combatant .tmp records itself (the battle screen refactor is LAST)",
        ["Darklands.Presentation.CharacterCreationScreen"] = "reads the characters of a saved game for the party menu's pick",
    };

    /// <summary>Views ask the player through GameUi (the one door for keys, the mouse and the commands): no screen reads the host's input itself.</summary>
    [Fact]
    public void ViewsAskThroughGameUi()
    {
        var breaks = Owners([Presentation], use => use.Target.FullName is "Darklands.Platform.IInput" or "Darklands.Platform.IHost");
        Check(breaks, ["Darklands.Presentation.GameUi", "Darklands.Presentation.DarklandsGame"], "views that read the host's input", []);
    }

    private static IEnumerable<string> References(string name) =>
        Dependencies.Load(name).GetReferencedAssemblies().Select(a => a.Name ?? "").Where(Dependencies.Products.Contains);

    private static readonly HashSet<string> IoTypes =
    [
        "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream",
        "System.IO.StreamReader", "System.IO.StreamWriter", "System.Console", "System.Random", "System.Diagnostics.Stopwatch",
    ];

    private static bool IsIoOrClock(Use use) =>
        use.Target.FullName is { } name
        && (IoTypes.Contains(name)
            || name is "System.DateTime" or "System.DateTimeOffset" && use.Member is "get_Now" or "get_UtcNow" or "get_Today"
            || name == "System.Environment" && use.Member.StartsWith("get_TickCount", StringComparison.Ordinal)
            || name == "System.Threading.Thread" && use.Member == "Sleep");

    /// <summary>The types of <paramref name="assemblies"/> that make a use <paramref name="breaks"/> matches, each with its first such use.</summary>
    private static Dictionary<string, Use> Owners(string[] assemblies, Func<Use, bool> breaks)
    {
        var owners = new Dictionary<string, Use>();
        foreach (var assembly in assemblies)
        {
            foreach (var use in Dependencies.Uses(assembly).Where(breaks))
            {
                owners.TryAdd(use.Owner.FullName ?? use.Owner.Name, use);
            }
        }
        return owners;
    }

    private static void Check(Dictionary<string, Use> breaks, Dictionary<string, string> known, string what) => Check(breaks, [], what, known);

    /// <param name="byDesign">The types the rule allows (its doors): never a break.</param>
    private static void Check(Dictionary<string, Use> breaks, HashSet<string> byDesign, string what, Dictionary<string, string> known)
    {
        Assert.All(known.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        var found = breaks.Keys.Except(byDesign).ToList();
        var added = found.Except(known.Keys).Order().Select(k => breaks[k].ToString()).ToList();
        var gone = known.Keys.Except(found).Order().ToList();
        Assert.True(added.Count == 0, $"new {what} (move the work where it belongs, or list it with its reason):\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, $"known exceptions that no longer apply (take them off the list):\n" + string.Join("\n", gone));
    }
}
